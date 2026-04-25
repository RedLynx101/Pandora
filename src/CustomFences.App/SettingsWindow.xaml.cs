using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using CustomFences.Core;

namespace CustomFences.App;

public partial class SettingsWindow : Window
{
    private readonly DesktopZoneManager _manager;
    private bool _isRefreshing;

    public SettingsWindow(DesktopZoneManager manager)
    {
        InitializeComponent();
        _manager = manager;
        RefreshFromWorkspace();
    }

    public void RefreshFromWorkspace()
    {
        _isRefreshing = true;
        try
        {
            ZonesList.ItemsSource = null;
            ZonesList.ItemsSource = _manager.Workspace.Zones;
            LayoutsComboBox.ItemsSource = null;
            LayoutsComboBox.ItemsSource = _manager.Workspace.Layouts;
            LayoutsComboBox.SelectedItem = _manager.Workspace.Layouts.FirstOrDefault(layout =>
                string.Equals(layout.Name, _manager.Workspace.ActiveLayoutName, StringComparison.OrdinalIgnoreCase));
            LayoutNameTextBox.Text = _manager.Workspace.ActiveLayoutName;
            if (ZonesList.SelectedIndex < 0 && _manager.Workspace.Zones.Count > 0)
            {
                ZonesList.SelectedIndex = 0;
            }

            AttachDesktopCheckBox.IsChecked = _manager.Workspace.Settings.AttachWindowsToDesktop;
            CleanDesktopCheckBox.IsChecked = _manager.Workspace.Settings.HideDesktopIconsWhenRunning;
            StartWithWindowsCheckBox.IsChecked = _manager.Workspace.Settings.StartWithWindows || StartupAppService.IsEnabled();
            SoundEffectsCheckBox.IsChecked = _manager.Workspace.Settings.Audio.EnableSoundEffects;
            MusicDockCheckBox.IsChecked = _manager.Workspace.Settings.Audio.EnableMusicDock;
            SoundEffectsVolumeTextBox.Text = _manager.Workspace.Settings.Audio.SoundEffectsVolume.ToString("0.00");
            MusicFolderTextBox.Text = _manager.Workspace.Settings.Audio.MusicRootPath;
            ExpansionEdgeComboBox.ItemsSource = Enum.GetValues(typeof(DockExpansionEdge));
            PopulateFields(ZonesList.SelectedItem as ZoneDefinition);
        }
        finally
        {
            _isRefreshing = false;
        }
    }

    private void LayoutsComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isRefreshing)
        {
            return;
        }

        if (LayoutsComboBox.SelectedItem is LayoutProfile profile)
        {
            LayoutNameTextBox.Text = profile.Name;
        }
    }

    private void SaveLayout_Click(object sender, RoutedEventArgs e)
    {
        var name = string.IsNullOrWhiteSpace(LayoutNameTextBox.Text)
            ? _manager.Workspace.ActiveLayoutName
            : LayoutNameTextBox.Text.Trim();
        WorkspaceLayoutService.SaveCurrentLayoutAs(_manager.Workspace, name);
        _manager.Save();
        _manager.Reload();
        StatusText.Text = $"Saved layout '{name}'.";
    }

    private void SwitchLayout_Click(object sender, RoutedEventArgs e)
    {
        var name = LayoutsComboBox.SelectedItem is LayoutProfile profile
            ? profile.Name
            : LayoutNameTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        try
        {
            WorkspaceLayoutService.SwitchLayout(_manager.Workspace, name);
            _manager.Save();
            _manager.Reload();
            StatusText.Text = $"Switched to layout '{name}'.";
        }
        catch (InvalidOperationException ex)
        {
            StatusText.Text = ex.Message;
        }
    }

    private void DuplicateLayout_Click(object sender, RoutedEventArgs e)
    {
        var target = string.IsNullOrWhiteSpace(LayoutNameTextBox.Text)
            ? $"{_manager.Workspace.ActiveLayoutName} Copy"
            : LayoutNameTextBox.Text.Trim();

        try
        {
            WorkspaceLayoutService.DuplicateLayout(_manager.Workspace, _manager.Workspace.ActiveLayoutName, target);
            _manager.Save();
            RefreshFromWorkspace();
            StatusText.Text = $"Duplicated layout '{target}'.";
        }
        catch (InvalidOperationException ex)
        {
            StatusText.Text = ex.Message;
        }
    }

    private void ZonesList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_isRefreshing)
        {
            PopulateFields(ZonesList.SelectedItem as ZoneDefinition);
        }
    }

    private void SaveZone_Click(object sender, RoutedEventArgs e)
    {
        if (ZonesList.SelectedItem is not ZoneDefinition zone)
        {
            return;
        }

        ApplyFields(zone);
        _manager.Workspace.Settings.AttachWindowsToDesktop = AttachDesktopCheckBox.IsChecked == true;
        _manager.Workspace.Settings.HideDesktopIconsWhenRunning = CleanDesktopCheckBox.IsChecked == true;
        var startupStatus = ApplyStartupField();
        ApplyAudioFields();
        _manager.Save();
        _manager.Reload();
        StatusText.Text = string.IsNullOrWhiteSpace(startupStatus)
            ? "Saved and reloaded."
            : $"Saved and reloaded. {startupStatus}";
    }

    private void AddZone_Click(object sender, RoutedEventArgs e)
    {
        var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        var zone = new ZoneDefinition
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = "New Zone",
            Bounds = new ZoneBounds { X = 150, Y = 150, Width = 360, Height = 300 },
            Appearance = new ZoneAppearance { AccentColor = "#4FB3FF" },
            Tabs =
            [
                new ZoneTabDefinition
                {
                    Id = Guid.NewGuid().ToString("N"),
                    Name = "New Zone",
                    Path = PathExpander.CompressUserPath(documents)
                }
            ]
        };

        _manager.Workspace.Zones.Add(zone);
        _manager.Save();
        _manager.Reload();
        ZonesList.SelectedItem = zone;
        StatusText.Text = "Added zone.";
    }

    private void DuplicateZone_Click(object sender, RoutedEventArgs e)
    {
        if (ZonesList.SelectedItem is not ZoneDefinition source)
        {
            return;
        }

        var clone = new ZoneDefinition
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = source.Name + " Copy",
            Kind = source.Kind,
            IsVisible = source.IsVisible,
            IsLocked = source.IsLocked,
            IsCollapsed = false,
            Bounds = new ZoneBounds
            {
                X = source.Bounds.X + 34,
                Y = source.Bounds.Y + 34,
                Width = source.Bounds.Width,
                Height = source.Bounds.Height
            },
            Appearance = new ZoneAppearance
            {
                AccentColor = source.Appearance.AccentColor,
                BackgroundColor = source.Appearance.BackgroundColor,
                Opacity = source.Appearance.Opacity,
                CornerRadius = source.Appearance.CornerRadius,
                IconSize = source.Appearance.IconSize,
                Columns = source.Appearance.Columns,
                TabStyle = source.Appearance.TabStyle
            },
            Sort = source.Sort,
            Tabs = source.Tabs.Select(tab => new ZoneTabDefinition
            {
                Id = Guid.NewGuid().ToString("N"),
                Name = tab.Name,
                Source = tab.Source,
                DesktopGroup = tab.DesktopGroup,
                Path = tab.Path,
                AllowNavigation = tab.AllowNavigation
            }).ToList()
        };

        _manager.Workspace.Zones.Add(clone);
        _manager.Save();
        _manager.Reload();
        StatusText.Text = "Duplicated zone.";
    }

    private void DeleteZone_Click(object sender, RoutedEventArgs e)
    {
        if (ZonesList.SelectedItem is not ZoneDefinition zone)
        {
            return;
        }

        _manager.Workspace.Zones.Remove(zone);
        _manager.Save();
        _manager.Reload();
        StatusText.Text = "Deleted zone metadata. Files were not touched.";
    }

    private void OpenWorkspace_Click(object sender, RoutedEventArgs e)
    {
        OpenPath(_manager.WorkspacePath);
    }

    private void OpenMusicFolder_Click(object sender, RoutedEventArgs e)
    {
        var path = PathExpander.Expand(string.IsNullOrWhiteSpace(MusicFolderTextBox.Text)
            ? _manager.Workspace.Settings.Audio.MusicRootPath
            : MusicFolderTextBox.Text.Trim());
        try
        {
            Directory.CreateDirectory(path);
        }
        catch
        {
            // Explorer will surface any remaining path issue.
        }

        OpenPath(path);
    }

    private void StartWithWindows_Click(object sender, RoutedEventArgs e)
    {
        if (_isRefreshing)
        {
            return;
        }

        var startupStatus = ApplyStartupField();
        _manager.Save();
        StatusText.Text = string.IsNullOrWhiteSpace(startupStatus)
            ? "Startup setting already matched Windows."
            : startupStatus;
    }

    private void RestoreDockSize_Click(object sender, RoutedEventArgs e)
    {
        if (ZonesList.SelectedItem is not ZoneDefinition zone)
        {
            return;
        }

        WorkspaceLayoutService.RestoreDockBounds(_manager.Workspace, zone.Id, DisplaySnapshotProvider.GetDisplays());
        _manager.Save();
        _manager.Reload();
        StatusText.Text = $"Restored '{zone.Name}' to a normal dock size.";
    }

    private void RepairDockSizes_Click(object sender, RoutedEventArgs e)
    {
        var changed = WorkspaceLayoutService.RepairOversizedDockBounds(_manager.Workspace, DisplaySnapshotProvider.GetDisplays());
        _manager.Save();
        _manager.Reload();
        StatusText.Text = changed == 0
            ? "Dock sizes already look normal."
            : "Repaired oversized dock bounds and reloaded.";
    }

    private void Reload_Click(object sender, RoutedEventArgs e)
    {
        _manager.Reload();
        StatusText.Text = "Reloaded workspace.";
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
            return;
        }

        try
        {
            DragMove();
        }
        catch
        {
            // Windows can cancel a drag if the pointer state changes between messages.
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void PopulateFields(ZoneDefinition? zone)
    {
        var enabled = zone is not null;
        NameTextBox.IsEnabled = enabled;
        PathTextBox.IsEnabled = enabled;
        PathTextBox.IsReadOnly = false;
        AccentTextBox.IsEnabled = enabled;
        BackgroundTextBox.IsEnabled = enabled;
        OpacityTextBox.IsEnabled = enabled;
        IconSizeTextBox.IsEnabled = enabled;
        ExpansionEdgeComboBox.IsEnabled = enabled;
        VisibleCheckBox.IsEnabled = enabled;
        LockedCheckBox.IsEnabled = enabled;
        CollapsedCheckBox.IsEnabled = enabled;

        if (zone is null)
        {
            NameTextBox.Text = string.Empty;
            PathTextBox.Text = string.Empty;
            AccentTextBox.Text = string.Empty;
            BackgroundTextBox.Text = string.Empty;
            OpacityTextBox.Text = string.Empty;
            IconSizeTextBox.Text = string.Empty;
            ExpansionEdgeComboBox.SelectedItem = DockExpansionEdge.Top;
            VisibleCheckBox.IsChecked = false;
            LockedCheckBox.IsChecked = false;
            CollapsedCheckBox.IsChecked = false;
            return;
        }

        var primaryTab = zone.Tabs.FirstOrDefault();
        NameTextBox.Text = zone.Name;
        if (primaryTab?.Source == ZoneTabSource.SmartDesktop)
        {
            PathTextBox.Text = $"Smart desktop: {primaryTab.DesktopGroup}";
            PathTextBox.IsReadOnly = true;
        }
        else
        {
            PathTextBox.Text = primaryTab?.Path ?? string.Empty;
        }
        AccentTextBox.Text = zone.Appearance.AccentColor;
        BackgroundTextBox.Text = zone.Appearance.BackgroundColor;
        OpacityTextBox.Text = zone.Appearance.Opacity.ToString("0.00");
        IconSizeTextBox.Text = zone.Appearance.IconSize.ToString("0");
        ExpansionEdgeComboBox.SelectedItem = WorkspaceLayoutService.GetExpansionEdge(_manager.Workspace, zone);
        VisibleCheckBox.IsChecked = zone.IsVisible;
        LockedCheckBox.IsChecked = zone.IsLocked;
        CollapsedCheckBox.IsChecked = zone.IsCollapsed;
    }

    private void ApplyFields(ZoneDefinition zone)
    {
        zone.Name = string.IsNullOrWhiteSpace(NameTextBox.Text) ? "Untitled Zone" : NameTextBox.Text.Trim();
        zone.IsVisible = VisibleCheckBox.IsChecked == true;
        zone.IsLocked = LockedCheckBox.IsChecked == true;
        zone.IsCollapsed = CollapsedCheckBox.IsChecked == true;
        zone.Appearance.AccentColor = NormalizeColor(AccentTextBox.Text, "#4FB3FF");
        zone.Appearance.BackgroundColor = NormalizeColor(BackgroundTextBox.Text, "#121821");
        zone.Appearance.Opacity = TryReadDouble(OpacityTextBox.Text, 0.88, 0.10, 1.0);
        zone.Appearance.IconSize = TryReadDouble(IconSizeTextBox.Text, 42, 24, 96);
        if (ExpansionEdgeComboBox.SelectedItem is DockExpansionEdge edge)
        {
            WorkspaceLayoutService.SetExpansionEdge(_manager.Workspace, zone.Id, edge);
        }

        var primaryTab = zone.Tabs.FirstOrDefault();
        if (primaryTab is null)
        {
            primaryTab = new ZoneTabDefinition { Id = Guid.NewGuid().ToString("N"), Name = zone.Name };
            zone.Tabs.Add(primaryTab);
        }

        if (primaryTab.Source == ZoneTabSource.Folder)
        {
            primaryTab.Name = zone.Name;
            primaryTab.Path = string.IsNullOrWhiteSpace(PathTextBox.Text)
                ? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
                : PathExpander.CompressUserPath(PathTextBox.Text.Trim());
        }
    }

    private void ApplyAudioFields()
    {
        var audio = _manager.Workspace.Settings.Audio;
        audio.EnableSoundEffects = SoundEffectsCheckBox.IsChecked == true;
        audio.SoundEffectsVolume = TryReadDouble(SoundEffectsVolumeTextBox.Text, 0.35, 0, 1);
        audio.EnableMusicDock = MusicDockCheckBox.IsChecked == true;
        audio.MusicRootPath = string.IsNullOrWhiteSpace(MusicFolderTextBox.Text)
            ? "%USERPROFILE%\\Music\\OrbitDock"
            : PathExpander.CompressUserPath(MusicFolderTextBox.Text.Trim());

        var musicDock = WorkspaceLayoutService.EnsureMusicDock(_manager.Workspace);
        WorkspaceLayoutService.SetDockVisibility(_manager.Workspace, musicDock.Id, audio.EnableMusicDock);
    }

    private string ApplyStartupField()
    {
        var enabled = StartWithWindowsCheckBox.IsChecked == true;
        var wasEnabled = StartupAppService.IsEnabled();
        _manager.Workspace.Settings.StartWithWindows = enabled;
        try
        {
            StartupAppService.SetEnabled(enabled);
            return enabled == wasEnabled
                ? string.Empty
                : enabled ? "Startup app enabled." : "Startup app disabled.";
        }
        catch (Exception ex) when (ex is InvalidOperationException or UnauthorizedAccessException or IOException or System.Runtime.InteropServices.COMException)
        {
            return $"Startup setting could not be updated: {ex.Message}";
        }
    }

    private static string NormalizeColor(string value, string fallback)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        var trimmed = value.Trim();
        if (!trimmed.StartsWith('#'))
        {
            trimmed = "#" + trimmed;
        }

        return trimmed.Length is 7 or 9 ? trimmed : fallback;
    }

    private static double TryReadDouble(string value, double fallback, double min, double max)
    {
        return double.TryParse(value, out var parsed)
            ? Math.Clamp(parsed, min, max)
            : fallback;
    }

    private static void OpenPath(string path)
    {
        try
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch
        {
            // The settings window remains usable even when the shell cannot open a path.
        }
    }
}
