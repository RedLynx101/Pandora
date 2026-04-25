using System;
using System.Collections.Generic;
using System.Linq;
using CustomFences.Core;

namespace CustomFences.App;

public sealed class DesktopZoneManager : IDisposable
{
    private readonly WorkspaceStore _store;
    private readonly List<ZoneWindow> _windows = [];
    private SettingsWindow? _settingsWindow;
    private bool _isPeekVisible;

    public DesktopZoneManager(WorkspaceStore store)
    {
        _store = store;
        Workspace = _store.LoadOrCreate();
    }

    public Workspace Workspace { get; private set; }
    public string WorkspacePath => _store.WorkspacePath;

    public void Start()
    {
        OpenZoneWindows();
    }

    public void Reload()
    {
        CloseZoneWindows();
        Workspace = _store.LoadOrCreate();
        OpenZoneWindows();
        _settingsWindow?.RefreshFromWorkspace();
    }

    public void Save()
    {
        _store.Save(Workspace);
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
    }

    public void Dispose()
    {
        CloseZoneWindows();
        _settingsWindow?.Close();
    }

    private void OpenZoneWindows()
    {
        foreach (var zone in Workspace.Zones.Where(zone => zone.IsVisible))
        {
            if (zone.Tabs.Count == 0)
            {
                zone.Tabs.Add(new ZoneTabDefinition { Name = zone.Name });
            }

            var viewModel = new ZoneViewModel(zone);
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
}
