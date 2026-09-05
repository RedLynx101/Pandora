using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Threading;
using Microsoft.Win32;
using Pandora.Core;

namespace Pandora.App;

public sealed class DesktopZoneManager : IDisposable
{
    private readonly WorkspaceStore _store;
    private readonly List<ZoneWindow> _windows = [];
    private readonly List<DesktopPinWindow> _pinWindows = [];
    private readonly Dispatcher _dispatcher = Dispatcher.CurrentDispatcher;
    private readonly DispatcherTimer _reloadTimer;
    private readonly DispatcherTimer _desktopOverlayTimer;
    private readonly DispatcherTimer _displayChangeTimer;
    private readonly DispatcherTimer _placementSaveTimer;
    private PlacementSave? _placementSave;
    private bool _placementDirty;
    private int _placementGestures;
    private SettingsWindow? _settingsWindow;
    private FileSystemWatcher? _workspaceWatcher;
    private bool _isPeekVisible;
    private bool _isReloading;
    private bool _disposed;
    private bool _wasDesktopExposed;
    private DateTime _lastLocalWriteUtc = DateTime.MinValue;

    public DesktopZoneManager(WorkspaceStore store)
    {
        _store = store;
        Workspace = _store.LoadOrCreate();
        ThemeService.Initialize(Workspace.Settings);
        _reloadTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _reloadTimer.Tick += (_, _) =>
        {
            if (_placementGestures > 0 || _placementSave is { Task.IsCompleted: false }) return;
            _reloadTimer.Stop();
            try
            {
                FinishPlacementSave();
                if (!_store.IsCurrent(Workspace)) Reload();
            }
            catch (Exception ex) when (IsExpectedStorageFailure(ex)) { ReportStorageError(ex.Message); }
        };
        _placementSaveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _placementSaveTimer.Tick += (_, _) =>
        {
            if (_placementGestures > 0 || _placementSave is { Task.IsCompleted: false }) return;
            _placementSaveTimer.Stop();
            try { FinishPlacementSave(); StartPlacementSave(); }
            catch (Exception ex) when (IsExpectedStorageFailure(ex)) { ReportStorageError("Dock placement was not saved. " + ex.Message); }
        };
        _desktopOverlayTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(350) };
        _desktopOverlayTimer.Tick += (_, _) => MaintainDesktopOverlays();
        _displayChangeTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1000) };
        _displayChangeTimer.Tick += DisplayChangeTimer_Tick;
        Audio = new PandoraAudioService();
        AgentFeeds = AgentFeedStore.ForWorkspace(_store.WorkspacePath);
        Audio.MusicEnded += (_, _) => OnMusicEnded();
    }

    public event Action<bool>? CleanDesktopModeChanged;
    public event Action? MusicEnded;
    public event Action<string>? StorageError;

    public Workspace Workspace { get; private set; }
    public string WorkspacePath => _store.WorkspacePath;
    public PandoraAudioService Audio { get; }
    public AgentFeedStore AgentFeeds { get; }
    public string? LastStorageError { get; private set; }
    public DateTime LastSuccessfulSaveUtc => _lastLocalWriteUtc;
    public bool IsPlacementGestureActive => _placementGestures > 0;
    public string RecoveryDirectory => _store.RecoveryDirectory;

    public void BeginPlacementGesture()
    {
        _placementGestures++;
        _placementSaveTimer.Stop();
    }

    public void EndPlacementGesture()
    {
        _placementGestures = Math.Max(0, _placementGestures - 1);
        if (_placementGestures == 0 && _placementDirty) _placementSaveTimer.Start();
    }

    /// <summary>Geometry events update the model only; the final snapshot is saved off the UI thread.</summary>
    public void SavePlacement()
    {
        if (_disposed || _isReloading) return;
        _placementDirty = true;
        if (_placementGestures == 0) { _placementSaveTimer.Stop(); _placementSaveTimer.Start(); }
    }

    private void StartPlacementSave()
    {
        if (!_placementDirty || _disposed) return;
        if (!IsCurrentDisplayVariantActive()) { QueueDisplayVariantRefresh(); return; }
        WorkspaceLayoutService.CaptureAllZoneStates(Workspace);
        var source = Workspace;
        var snapshot = _store.CreateSaveSnapshot(source);
        _placementDirty = false;
        var pending = new PlacementSave(source, snapshot, Task.Run(() => _store.Save(snapshot)));
        _placementSave = pending;
        // The worker never waits on the dispatcher. Synchronous editors can safely
        // drain it before saving, and this callback then becomes a no-op.
        _ = pending.Task.ContinueWith(_ => _dispatcher.BeginInvoke(() =>
        {
            if (!ReferenceEquals(_placementSave, pending)) return;
            try
            {
                FinishPlacementSave();
                if (_placementDirty && _placementGestures == 0) _placementSaveTimer.Start();
            }
            catch (Exception ex) when (IsExpectedStorageFailure(ex)) { ReportStorageError("Dock placement was not saved. " + ex.Message); }
        }), TaskScheduler.Default);
    }

    private void FinishPlacementSave()
    {
        var pending = _placementSave;
        if (pending is null) return;
        _placementSave = null;
        pending.Task.GetAwaiter().GetResult();
        _store.AcceptSavedSnapshot(pending.Source, pending.Snapshot);
        _lastLocalWriteUtc = DateTime.UtcNow;
        LastStorageError = null;
        RecordWorkspaceStatus();
    }

    public void RestoreWorkspace(string backupPath)
    {
        FinishPlacementSave();
        _store.RestoreFromBackup(backupPath);
        _placementDirty = false;
        Reload();
    }

    public void Start()
    {
        ApplyCurrentDisplayVariant(Workspace);
        OpenZoneWindows();
        OpenDesktopPins();
        RefreshDesktopOverlayPersistence();
        StartWorkspaceWatcher();
        SystemEvents.DisplaySettingsChanged += SystemEvents_DisplaySettingsChanged;
    }

    public void Reload()
    {
        if (_placementGestures > 0) { _reloadTimer.Start(); return; }
        FinishPlacementSave();
        ReloadCore(_store.LoadReadOnly, ApplyCurrentDisplayVariant,
            () => { CloseZoneWindows(); CloseDesktopPins(); },
            () =>
            {
                ThemeService.Apply(Workspace.Settings);
                OpenZoneWindows();
                OpenDesktopPins();
                RefreshDesktopOverlayPersistence();
                _settingsWindow?.RefreshFromWorkspace();
                CleanDesktopModeChanged?.Invoke(Workspace.Settings.HideDesktopIconsWhenRunning);
            });
    }

    // Keep replacement preparation separate from window teardown. The callbacks
    // also allow lifecycle checks without creating HWNDs or changing the shell.
    private bool ReloadCore(Func<Workspace> read, Action<Workspace> prepare, Action close, Action open)
    {
        if (_isReloading || _disposed)
        {
            return false;
        }

        _isReloading = true;
        try
        {
            Workspace replacement;
            try
            {
                replacement = read();
                prepare(replacement);
            }
            catch (Exception ex) when (IsExpectedStorageFailure(ex))
            {
                ReportStorageError("Workspace reload was not applied. Your existing docks remain open. " + ex.Message);
                return false;
            }

            close();
            Workspace = replacement;
            open();
            LastStorageError = null;
            RecordWorkspaceStatus();
            return true;
        }
        finally
        {
            _isReloading = false;
        }
    }

    public void Save()
    {
        if (!IsCurrentDisplayVariantActive())
        {
            QueueDisplayVariantRefresh();
            throw new IOException("The display layout changed before saving. Wait for the dock layout to refresh, then review and retry your change.");
        }

        WorkspaceLayoutService.CaptureAllZoneStates(Workspace);
        PersistWorkspace(Workspace);
    }

    public void ReloadDesktopPins()
    {
        CloseDesktopPins();
        OpenDesktopPins();
    }

    public void RemoveDesktopPin(DesktopPinDefinition pin)
    {
        var variant = WorkspaceLayoutService.EnsureActiveDisplayVariant(Workspace);
        variant.DesktopPins.Remove(pin);
        Save();
        ReloadDesktopPins();
    }

    public void RemoveDesktopPin(string pinId)
    {
        var variant = WorkspaceLayoutService.EnsureActiveDisplayVariant(Workspace);
        var pin = variant.DesktopPins.FirstOrDefault(candidate =>
            string.Equals(candidate.Id, pinId, StringComparison.OrdinalIgnoreCase));
        if (pin is null)
        {
            return;
        }

        variant.DesktopPins.Remove(pin);
        Save();
        ReloadDesktopPins();
    }

    public void SendDesktopPinToDock(DesktopPinDefinition pin)
    {
        var targetZone = Workspace.Zones
            .Where(zone => zone.IsVisible)
            .FirstOrDefault(zone => zone.Tabs.Any(tab => tab.Source == ZoneTabSource.SmartDesktop));
        var targetTab = targetZone?.Tabs.FirstOrDefault(tab => tab.Source == ZoneTabSource.SmartDesktop);
        if (targetZone is null || targetTab is null)
        {
            return;
        }

        WorkspaceLayoutService.AddOrShowItem(Workspace, pin.Path, targetZone.Id, targetTab.Id, displayName: pin.DisplayName);
        WorkspaceLayoutService.RemoveDesktopPin(Workspace, pin.Id);
        Save();
        Reload();
    }

    public void ShowSettings()
    {
        if (_settingsWindow is { IsVisible: true })
        {
            _settingsWindow.Activate();
            return;
        }

        _settingsWindow = new SettingsWindow(this);
        _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        _settingsWindow.Show();
        _settingsWindow.Activate();
    }

    public void ShowProjects()
    {
        var zone = WorkspaceLayoutService.EnsureProjectsDock(Workspace);
        zone.IsVisible = true;
        zone.IsCollapsed = false;
        Save();
        Reload();
    }

    public void TogglePeek()
    {
        _isPeekVisible = !_isPeekVisible;
        foreach (var window in _windows)
        {
            window.SetPeek(_isPeekVisible);
        }
        ApplyDockLayering();
    }

    public void ApplyDockLayering()
    {
        if (_placementGestures > 0) return;
        foreach (var pinWindow in _pinWindows.ToArray())
        {
            DockWindowLayer.SendBehindNormalWindows(pinWindow);
        }

        foreach (var window in _windows.Where(window => window.IsCollapsed).ToArray())
        {
            DockWindowLayer.SendBehindNormalWindows(window);
        }

        foreach (var window in _windows.Where(window => !window.IsCollapsed).ToArray())
        {
            DockWindowLayer.SendBehindNormalWindows(window);
        }
    }

    public void SaveAudioSettings()
    {
        Save();
        _settingsWindow?.RefreshFromWorkspace();
    }

    /// <summary>Persist appearance without capturing live dock placement or reopening windows.</summary>
    public void SaveAppearanceSettings()
    {
        PersistWorkspace(Workspace);
    }

    private void PersistWorkspace(Workspace workspace)
    {
        FinishPlacementSave();
        // Failed/conflicting writes must reach the caller so editors can roll
        // back. Only the successfully saved object gets a new store fingerprint.
        _store.Save(workspace);
        _placementDirty = false;
        _placementSaveTimer.Stop();
        _lastLocalWriteUtc = DateTime.UtcNow;
        LastStorageError = null;
        RecordWorkspaceStatus();
    }

    private void RecordWorkspaceStatus()
    {
        var layout = WorkspaceLayoutService.EnsureActiveLayout(Workspace);
        RuntimeDiagnostics.Record("workspace", new { workspacePath = WorkspacePath, lastSavedAt = _lastLocalWriteUtc,
            docks = Workspace.Zones.Count, visible = Workspace.Zones.Count(z => z.IsVisible),
            layout = layout.Name, displayVariant = layout.ActiveDisplayVariantKey, storageError = LastStorageError });
    }

    internal static bool IsExpectedStorageFailure(Exception error) =>
        error is IOException or UnauthorizedAccessException or JsonException;

    internal void ReportStorageError(string message)
    {
        if (string.Equals(LastStorageError, message, StringComparison.Ordinal)) return;
        LastStorageError = message;
        RecordWorkspaceStatus();
        StorageError?.Invoke(message);
    }

    public bool IsCurrentDisplayVariantActive()
    {
        var activeKey = WorkspaceLayoutService.EnsureActiveLayout(Workspace).ActiveDisplayVariantKey;
        return string.Equals(activeKey, GetCurrentDisplayVariantKey(), StringComparison.OrdinalIgnoreCase);
    }

    public void QueueDisplayVariantRefresh()
    {
        if (_disposed) return;
        _dispatcher.BeginInvoke(() =>
        {
            if (_isReloading || _disposed)
            {
                return;
            }

            _displayChangeTimer.Stop();
            _displayChangeTimer.Start();
        });
    }

    public void Dispose()
    {
        if (_disposed) return;
        try
        {
            FinishPlacementSave();
            if (_placementDirty) Save();
        }
        catch (Exception ex) when (IsExpectedStorageFailure(ex)) { ReportStorageError("Final dock placement was not saved. " + ex.Message); }
        _disposed = true;
        _placementSaveTimer.Stop();
        _reloadTimer.Stop();
        _desktopOverlayTimer.Stop();
        _displayChangeTimer.Stop();
        SystemEvents.DisplaySettingsChanged -= SystemEvents_DisplaySettingsChanged;
        _workspaceWatcher?.Dispose();
        CloseZoneWindows();
        CloseDesktopPins();
        _settingsWindow?.Close();
        Audio.Dispose();
    }

    private void OpenZoneWindows()
    {
        foreach (var zone in Workspace.Zones.Where(zone => zone.IsVisible))
        {
            if (zone.Kind == ZoneKind.Standard && zone.Tabs.Count == 0)
            {
                zone.Tabs.Add(new ZoneTabDefinition { Name = zone.Name });
            }

            var viewModel = new ZoneViewModel(zone, this);
            var window = new ZoneWindow(viewModel, this);
            _windows.Add(window);
            window.Show();
        }
    }

    private void CloseZoneWindows()
    {
        foreach (var window in _windows.ToArray())
        {
            window.Close();
        }

        _windows.Clear();
    }

    private void OpenDesktopPins()
    {
        var variant = WorkspaceLayoutService.EnsureActiveDisplayVariant(Workspace);
        foreach (var pin in variant.DesktopPins)
        {
            var window = new DesktopPinWindow(pin, this);
            _pinWindows.Add(window);
            window.Show();
        }
    }

    private void CloseDesktopPins()
    {
        foreach (var window in _pinWindows.ToArray())
        {
            window.Close();
        }

        _pinWindows.Clear();
    }

    private void StartWorkspaceWatcher()
    {
        var directory = Path.GetDirectoryName(_store.WorkspacePath);
        if (string.IsNullOrWhiteSpace(directory))
        {
            return;
        }

        Directory.CreateDirectory(directory);
        _workspaceWatcher = new FileSystemWatcher(directory, Path.GetFileName(_store.WorkspacePath))
        {
            IncludeSubdirectories = false,
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size
        };
        _workspaceWatcher.Changed += WorkspaceFileChanged;
        _workspaceWatcher.Created += WorkspaceFileChanged;
        _workspaceWatcher.Deleted += WorkspaceFileChanged;
        _workspaceWatcher.Renamed += WorkspaceFileChanged;
        _workspaceWatcher.EnableRaisingEvents = true;
    }

    private void WorkspaceFileChanged(object sender, FileSystemEventArgs e)
    {
        if (_disposed) return;
        _dispatcher.BeginInvoke(() =>
        {
            if (_isReloading || _disposed)
            {
                return;
            }

            try
            {
                // Timestamp windows can discard a real external edit immediately
                // after our save. Ignore only the exact content we already own.
                if (_placementSave is null && _store.IsCurrent(Workspace)) return;
            }
            catch (Exception ex) when (IsExpectedStorageFailure(ex))
            {
                // The debounced reload below owns validation and the single
                // recoverable error message if this read failure persists.
            }

            _reloadTimer.Stop();
            _reloadTimer.Start();
        });
    }

    private void SystemEvents_DisplaySettingsChanged(object? sender, EventArgs e)
    {
        DisplaySnapshotProvider.Invalidate();
        QueueDisplayVariantRefresh();
    }

    private void DisplayChangeTimer_Tick(object? sender, EventArgs e)
    {
        _displayChangeTimer.Stop();
        if (_placementGestures > 0) { _displayChangeTimer.Start(); return; }
        if (_isReloading || IsCurrentDisplayVariantActive())
        {
            return;
        }

        Reload();
    }

    private void ApplyCurrentDisplayVariant(Workspace workspace)
    {
        var signatureDisplays = DisplaySnapshotProvider.GetPhysicalDisplays();
        var displays = DisplaySnapshotProvider.GetDisplays();
        var signature = WorkspaceLayoutService.ComputeDisplaySignature(signatureDisplays);
        var key = WorkspaceLayoutService.ComputeDisplayVariantKey(signature);
        WorkspaceLayoutService.UseDisplayVariant(workspace, key, signature, displays);
        WorkspaceLayoutService.CaptureAllZoneStates(workspace);
        PersistWorkspace(workspace);
    }

    private static string GetCurrentDisplayVariantKey()
    {
        var signature = WorkspaceLayoutService.ComputeDisplaySignature(DisplaySnapshotProvider.GetPhysicalDisplays());
        return WorkspaceLayoutService.ComputeDisplayVariantKey(signature);
    }

    private void RefreshDesktopOverlayPersistence()
    {
        if (Workspace.Settings.StayVisibleOnShowDesktop)
        {
            _wasDesktopExposed = false;
            MaintainDesktopOverlays(forceLayerRefresh: true);
            _desktopOverlayTimer.Start();
        }
        else
        {
            _desktopOverlayTimer.Stop();
        }
    }

    private void MaintainDesktopOverlays(bool forceLayerRefresh = false)
    {
        if (_placementGestures > 0 || !Workspace.Settings.StayVisibleOnShowDesktop)
        {
            return;
        }

        var desktopExposed = DockWindowLayer.IsDesktopExposed();
        var refreshLayering = forceLayerRefresh || (desktopExposed && !_wasDesktopExposed);
        var changed = false;
        foreach (var window in _windows.ToArray())
        {
            changed |= window.MaintainDesktopOverlay(desktopExposed, refreshLayering);
        }

        foreach (var pinWindow in _pinWindows.ToArray())
        {
            changed |= pinWindow.MaintainDesktopOverlay(desktopExposed, refreshLayering);
        }

        if (changed || refreshLayering)
        {
            ApplyDockLayering();
        }

        _wasDesktopExposed = desktopExposed;
    }

    private void OnMusicEnded()
    {
        _dispatcher.BeginInvoke(() => MusicEnded?.Invoke());
    }

    private sealed record PlacementSave(Workspace Source, Workspace Snapshot, Task Task);
}
