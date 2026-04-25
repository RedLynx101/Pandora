using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Threading;
using CustomFences.Core;

namespace CustomFences.App;

public sealed class DesktopZoneManager : IDisposable
{
    private readonly WorkspaceStore _store;
    private readonly List<ZoneWindow> _windows = [];
    private readonly List<DesktopPinWindow> _pinWindows = [];
    private readonly Dispatcher _dispatcher = Dispatcher.CurrentDispatcher;
    private readonly DispatcherTimer _reloadTimer;
    private SettingsWindow? _settingsWindow;
    private FileSystemWatcher? _workspaceWatcher;
    private bool _isPeekVisible;
    private bool _isReloading;
    private DateTime _lastLocalWriteUtc = DateTime.MinValue;

    public DesktopZoneManager(WorkspaceStore store)
    {
        _store = store;
        Workspace = _store.LoadOrCreate();
        _reloadTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _reloadTimer.Tick += (_, _) =>
        {
            _reloadTimer.Stop();
            Reload();
        };
        Audio = new OrbitAudioService();
        Audio.MusicEnded += (_, _) => OnMusicEnded();
    }

    public event Action<bool>? CleanDesktopModeChanged;
    public event Action? MusicEnded;

    public Workspace Workspace { get; private set; }
    public string WorkspacePath => _store.WorkspacePath;
    public OrbitAudioService Audio { get; }

    public void Start()
    {
        ApplyCurrentDisplayVariant();
        OpenZoneWindows();
        OpenDesktopPins();
        StartWorkspaceWatcher();
    }

    public void Reload()
    {
        if (_isReloading)
        {
            return;
        }

        _isReloading = true;
        CloseZoneWindows();
        CloseDesktopPins();
        try
        {
            Workspace = _store.LoadOrCreate();
            ApplyCurrentDisplayVariant();
            OpenZoneWindows();
            OpenDesktopPins();
            _settingsWindow?.RefreshFromWorkspace();
            CleanDesktopModeChanged?.Invoke(Workspace.Settings.HideDesktopIconsWhenRunning);
        }
        finally
        {
            _isReloading = false;
        }
    }

    public void Save()
    {
        WorkspaceLayoutService.CaptureAllZoneStates(Workspace);
        _store.Save(Workspace);
        if (File.Exists(_store.WorkspacePath))
        {
            _lastLocalWriteUtc = File.GetLastWriteTimeUtc(_store.WorkspacePath);
        }
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
        foreach (var window in _windows.Where(window => !window.IsCollapsed))
        {
            DockWindowLayer.SendBehindNormalWindows(window);
        }

        foreach (var window in _windows.Where(window => window.IsCollapsed))
        {
            DockWindowLayer.SendBehindNormalWindows(window);
        }
    }

    public void SaveAudioSettings()
    {
        Save();
        _settingsWindow?.RefreshFromWorkspace();
    }

    public void Dispose()
    {
        _reloadTimer.Stop();
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
            if (zone.Tabs.Count == 0)
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
        _workspaceWatcher.Renamed += WorkspaceFileChanged;
        _workspaceWatcher.EnableRaisingEvents = true;
    }

    private void WorkspaceFileChanged(object sender, FileSystemEventArgs e)
    {
        _dispatcher.BeginInvoke(() =>
        {
            if (_isReloading || !File.Exists(_store.WorkspacePath))
            {
                return;
            }

            var writeTime = File.GetLastWriteTimeUtc(_store.WorkspacePath);
            if (writeTime <= _lastLocalWriteUtc.AddMilliseconds(250))
            {
                return;
            }

            _reloadTimer.Stop();
            _reloadTimer.Start();
        });
    }

    private void ApplyCurrentDisplayVariant()
    {
        var displays = DisplaySnapshotProvider.GetDisplays();
        var signature = WorkspaceLayoutService.ComputeDisplaySignature(displays);
        var key = WorkspaceLayoutService.ComputeDisplayVariantKey(signature);
        WorkspaceLayoutService.UseDisplayVariant(Workspace, key, signature, displays);
        WorkspaceLayoutService.CaptureAllZoneStates(Workspace);
        _store.Save(Workspace);
        if (File.Exists(_store.WorkspacePath))
        {
            _lastLocalWriteUtc = File.GetLastWriteTimeUtc(_store.WorkspacePath);
        }
    }

    private void OnMusicEnded()
    {
        _dispatcher.BeginInvoke(() => MusicEnded?.Invoke());
    }
}
