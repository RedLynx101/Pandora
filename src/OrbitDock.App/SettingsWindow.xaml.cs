using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using OrbitDock.Core;

namespace OrbitDock.App;

public partial class SettingsWindow : Window
{
    private readonly DesktopZoneManager _manager;
    private bool _isRefreshing;
    private bool _isPseudoMaximized;
    private bool _isCoercingMaximize;
    private Rect _restoreBounds;
    private bool _initialStartupSelection;

    public SettingsWindow(DesktopZoneManager manager)
    {
        _manager = manager;
        InitializeComponent();
        RefreshFromWorkspace();
        SettingsNavigation.SelectedIndex = 0;
        VersionText.Text = $"Version {typeof(SettingsWindow).Assembly.GetName().Version?.ToString(3) ?? "1.0.0"} · Windows desktop";
        ThemeService.ThemeChanged += AppearanceThemeChanged;
        Closed += (_, _) =>
        {
            ThemeService.ThemeChanged -= AppearanceThemeChanged;
            UpdateBrandImage(_manager.Workspace.Settings.IconStyle);
            ThemeService.Apply(_manager.Workspace.Settings);
        };
    }

    public void RefreshFromWorkspace()
    {
        _isRefreshing = true;
        try
        {
            var selectedDockId = (ZonesList.SelectedItem as ZoneDefinition)?.Id;
            ZonesList.ItemsSource = null;
            ZonesList.ItemsSource = _manager.Workspace.Zones;
            LayoutsComboBox.ItemsSource = null;
            LayoutsComboBox.ItemsSource = _manager.Workspace.Layouts;
            LayoutsComboBox.SelectedItem = _manager.Workspace.Layouts.FirstOrDefault(layout =>
                string.Equals(layout.Name, _manager.Workspace.ActiveLayoutName, StringComparison.OrdinalIgnoreCase));
            LayoutNameTextBox.Text = _manager.Workspace.ActiveLayoutName;
            ZonesList.SelectedItem = _manager.Workspace.Zones.FirstOrDefault(zone => zone.Id == selectedDockId)
                ?? _manager.Workspace.Zones.FirstOrDefault();

            AttachDesktopCheckBox.IsChecked = _manager.Workspace.Settings.AttachWindowsToDesktop;
            CleanDesktopCheckBox.IsChecked = _manager.Workspace.Settings.HideDesktopIconsWhenRunning;
            StayVisibleOnShowDesktopCheckBox.IsChecked = _manager.Workspace.Settings.StayVisibleOnShowDesktop;
            _initialStartupSelection = StartupAppService.IsEnabled();
            StartWithWindowsCheckBox.IsChecked = _initialStartupSelection;
            SoundEffectsCheckBox.IsChecked = _manager.Workspace.Settings.Audio.EnableSoundEffects;
            MusicDockCheckBox.IsChecked = _manager.Workspace.Settings.Audio.EnableMusicDock;
            SoundEffectsVolumeTextBox.Text = _manager.Workspace.Settings.Audio.SoundEffectsVolume.ToString("0.00");
            MusicFolderTextBox.Text = _manager.Workspace.Settings.Audio.MusicRootPath;
            ExpansionEdgeComboBox.ItemsSource = Enum.GetValues(typeof(DockExpansionEdge));
            DeleteLayoutButton.IsEnabled = _manager.Workspace.Layouts.Count > 1;
            PopulateFields(ZonesList.SelectedItem as ZoneDefinition);
            LoadAppearanceFields();
        }
        finally
        {
            _isRefreshing = false;
        }
    }

    private void SettingsNavigation_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (AppearancePanel is null || SettingsNavigation.SelectedItem is not ListBoxItem item) return;
        var selected = item.Tag?.ToString() ?? "Appearance";
        foreach (var panel in new[] { AppearancePanel, DesktopPanel, DocksPanel, LayoutsPanel, AudioPanel, AboutPanel })
            panel.Visibility = panel.Name == selected + "Panel" ? Visibility.Visible : Visibility.Collapsed;
        PageTitle.Text = selected;
        PageDescription.Text = selected switch
        {
            "Desktop" => "Control how Pandora fits into Windows.",
            "Docks" => "Choose a dock, edit its details, then save that dock.",
            "Layouts" => "Keep useful arrangements for different work and displays.",
            "Audio" => "Manage local music and interface sounds.",
            "About" => "Local-first desktop tools. Compatible with your existing workspace.",
            _ => "Choose the dock structure, then its colors. Every palette works with every theme."
        };
        SettingsScrollViewer.ScrollToTop();
    }

    private void LoadAppearanceFields()
    {
        var settings = _manager.Workspace.Settings;
        DockThemeListBox.SelectedItem = DockThemeListBox.Items.OfType<ListBoxItem>().First(item =>
            item.Tag?.ToString() == ThemeService.NormalizeDockTheme(settings.DockTheme));
        SelectTag(ThemeComboBox, ThemeService.NormalizeTheme(settings.Theme));
        CustomAccentTextBox.Text = settings.CustomAccentColor ?? string.Empty;
        CustomSurfaceTextBox.Text = settings.CustomSurfaceColor ?? string.Empty;
        CustomColorsExpander.IsExpanded = !string.IsNullOrWhiteSpace(settings.CustomAccentColor) || !string.IsNullOrWhiteSpace(settings.CustomSurfaceColor);
        SelectTag(IconStyleComboBox, settings.IconStyle);
        UpdateBrandImage(settings.IconStyle);
        GlassOpacitySlider.Value = double.IsFinite(settings.GlassOpacity) ? Math.Clamp(settings.GlassOpacity * 100, 55, 100) : 88;
        GlassOpacityText.Text = $"{GlassOpacitySlider.Value:0}%";
        ReduceMotionCheckBox.IsChecked = settings.ReduceMotion;
        AccessibilityText.Text = ThemeService.IsHighContrast
            ? "Windows high contrast is active. System colors and opaque backgrounds override theme previews."
            : "Windows high contrast and reduced-animation preferences are always respected.";
        ValidateAppearanceColors(out _, out _);
        RefreshAppearancePreviews();
    }

    private static void SelectTag(ComboBox comboBox, string? tag)
    {
        comboBox.SelectedItem = comboBox.Items.OfType<ComboBoxItem>().FirstOrDefault(item =>
            string.Equals(item.Tag?.ToString(), tag, StringComparison.OrdinalIgnoreCase)) ?? comboBox.Items[0];
    }

    private static string SelectedTag(ComboBox comboBox, string fallback) =>
        (comboBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? fallback;

    private void PreviewAppearance()
    {
        if (_isRefreshing || GlassOpacitySlider is null || ReduceMotionCheckBox is null || ThemeComboBox.SelectedItem is null ||
            DockThemeListBox?.SelectedItem is null || CustomAccentTextBox is null || CustomSurfaceTextBox is null || AppearanceValidationText is null) return;
        var colorsValid = ValidateAppearanceColors(out var accent, out var surface);
        // An incomplete HEX draft must not become a color. Other independent controls remain
        // responsive, using the last valid value only for the invalid field until it is corrected.
        if (!ThemeService.TryNormalizeCustomColor(CustomAccentTextBox.Text, out _)) accent = ThemeService.EffectiveCustomAccentColor;
        if (!ThemeService.TryNormalizeCustomColor(CustomSurfaceTextBox.Text, out _)) surface = ThemeService.EffectiveCustomSurfaceColor;
        ThemeService.Apply(SelectedTag(ThemeComboBox, "LunarGlass"), GlassOpacitySlider.Value / 100,
            ReduceMotionCheckBox.IsChecked == true, SelectedDockTheme(), accent, surface);
        StatusText.Text = colorsValid ? "Appearance preview · Apply to keep, or revert." :
            "Invalid color draft · That color keeps its last valid preview. Correct it before applying.";
    }

    private string SelectedDockTheme() => (DockThemeListBox.SelectedItem as ListBoxItem)?.Tag?.ToString() ?? "Classic";

    private bool ValidateAppearanceColors(out string? accent, out string? surface)
    {
        var accentValid = ThemeService.TryNormalizeCustomColor(CustomAccentTextBox.Text, out accent);
        var surfaceValid = ThemeService.TryNormalizeCustomColor(CustomSurfaceTextBox.Text, out surface);
        var valid = accentValid && surfaceValid;
        var field = !accentValid && !surfaceValid ? "Accent and surface colors" : !accentValid ? "Accent color" : "Surface color";
        AppearanceValidationText.Text = valid ? string.Empty : $"{field} must use #RRGGBB (for example, #78CDBE), or be left blank.";
        AppearanceValidationText.Visibility = valid ? Visibility.Collapsed : Visibility.Visible;
        ApplyAppearanceButton.IsEnabled = valid;
        CustomAccentTextBox.SetResourceReference(BorderBrushProperty, accentValid ? "Pandora.BorderBrush" : "Pandora.DangerBrush");
        CustomSurfaceTextBox.SetResourceReference(BorderBrushProperty, surfaceValid ? "Pandora.BorderBrush" : "Pandora.DangerBrush");
        return valid;
    }

    private void CustomColor_TextChanged(object sender, TextChangedEventArgs e) => PreviewAppearance();

    private void ResetCustomColors_Click(object sender, RoutedEventArgs e)
    {
        _isRefreshing = true;
        try { CustomAccentTextBox.Text = string.Empty; CustomSurfaceTextBox.Text = string.Empty; }
        finally { _isRefreshing = false; }
        PreviewAppearance();
    }

    private void PickCustomColor_Click(object sender, RoutedEventArgs e)
    {
        var isAccent = (sender as Button)?.Tag?.ToString() == "Accent";
        var input = isAccent ? CustomAccentTextBox : CustomSurfaceTextBox;
        var fallback = TryFindResource(isAccent ? "Pandora.AccentBrush" : "Pandora.WindowBrush") as System.Windows.Media.SolidColorBrush;
        var color = fallback?.Color ?? System.Windows.Media.Colors.SlateGray;
        if (ThemeService.TryNormalizeCustomColor(input.Text, out var normalized) && normalized is not null)
            color = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(normalized);
        using var dialog = new System.Windows.Forms.ColorDialog
        {
            FullOpen = true,
            Color = System.Drawing.Color.FromArgb(color.R, color.G, color.B)
        };
        var owner = new DialogOwner(new System.Windows.Interop.WindowInteropHelper(this).Handle);
        if (dialog.ShowDialog(owner) == System.Windows.Forms.DialogResult.OK)
            input.Text = $"#{dialog.Color.R:X2}{dialog.Color.G:X2}{dialog.Color.B:X2}";
    }

    private sealed record DialogOwner(IntPtr Handle) : System.Windows.Forms.IWin32Window;

    private void AppearanceThemeChanged(object? sender, EventArgs e) => RefreshAppearancePreviews();

    private void RefreshAppearancePreviews()
    {
        ClassicPreview?.InvalidateVisual();
        HaloPreview?.InvalidateVisual();
        MeridianPreview?.InvalidateVisual();
    }

    private void Appearance_SelectionChanged(object sender, SelectionChangedEventArgs e) => PreviewAppearance();
    private void IconStyle_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isRefreshing || BrandImage is null || IconStyleComboBox.SelectedItem is null) return;
        UpdateBrandImage(SelectedTag(IconStyleComboBox, "Aperture"));
        if (StatusText is not null) StatusText.Text = "Icon preview · Apply appearance to keep.";
    }
    private void UpdateBrandImage(string? style)
    {
        var image = BrandIdentity.Image(style);
        if (image is null) return;
        BrandImage.Source = image;
        Icon = image;
    }
    private void AppearancePreference_Click(object sender, RoutedEventArgs e) => PreviewAppearance();
    private void GlassOpacitySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (GlassOpacityText is not null) GlassOpacityText.Text = $"{e.NewValue:0}%";
        PreviewAppearance();
    }
    private void RevertTheme_Click(object sender, RoutedEventArgs e)
    {
        _isRefreshing = true;
        try { LoadAppearanceFields(); }
        finally { _isRefreshing = false; }
        ThemeService.Apply(_manager.Workspace.Settings);
        StatusText.Text = "Restored saved appearance.";
    }
    private void SaveAppearance_Click(object sender, RoutedEventArgs e)
    {
        if (!ValidateAppearanceColors(out var accent, out var surface))
        {
            CustomColorsExpander.IsExpanded = true;
            StatusText.Text = "Appearance was not saved. Correct the color draft or use palette colors.";
            return;
        }
        var settings = _manager.Workspace.Settings;
        var saved = AppearanceSnapshot.Capture(settings);
        settings.Theme = SelectedTag(ThemeComboBox, "LunarGlass");
        settings.DockTheme = SelectedDockTheme();
        settings.CustomAccentColor = accent;
        settings.CustomSurfaceColor = surface;
        settings.GlassOpacity = GlassOpacitySlider.Value / 100;
        settings.ReduceMotion = ReduceMotionCheckBox.IsChecked == true;
        settings.IconStyle = SelectedTag(IconStyleComboBox, "Aperture");
        try
        {
            _manager.SaveAppearanceSettings();
            ThemeService.Apply(settings);
            RefreshAppearanceFields();
            StatusText.Text = "Appearance saved. Existing custom dock values are unchanged.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            saved.Restore(settings);
            ThemeService.Apply(settings);
            RefreshAppearanceFields();
            StatusText.Text = $"Appearance was not saved: {ex.Message}";
        }
    }

    private void RefreshAppearanceFields()
    {
        _isRefreshing = true;
        try { LoadAppearanceFields(); }
        finally { _isRefreshing = false; }
    }

    private sealed record AppearanceSnapshot(string Theme, string DockTheme, string? Accent, string? Surface, double Opacity, bool ReduceMotion, string Icon)
    {
        public static AppearanceSnapshot Capture(AppSettings settings) => new(settings.Theme, settings.DockTheme,
            settings.CustomAccentColor, settings.CustomSurfaceColor, settings.GlassOpacity, settings.ReduceMotion, settings.IconStyle);

        public void Restore(AppSettings settings)
        {
            settings.Theme = Theme;
            settings.DockTheme = DockTheme;
            settings.CustomAccentColor = Accent;
            settings.CustomSurfaceColor = Surface;
            settings.GlassOpacity = Opacity;
            settings.ReduceMotion = ReduceMotion;
            settings.IconStyle = Icon;
        }
    }
    private void SaveDesktop_Click(object sender, RoutedEventArgs e)
    {
        _manager.Workspace.Settings.AttachWindowsToDesktop = AttachDesktopCheckBox.IsChecked == true;
        _manager.Workspace.Settings.HideDesktopIconsWhenRunning = CleanDesktopCheckBox.IsChecked == true;
        _manager.Workspace.Settings.StayVisibleOnShowDesktop = StayVisibleOnShowDesktopCheckBox.IsChecked == true;
        _manager.Save();
        _manager.Reload();
        StatusText.Text = "Desktop settings saved.";
    }
    private void SaveAudio_Click(object sender, RoutedEventArgs e)
    {
        ApplyAudioFields();
        _manager.Save();
        _manager.Reload();
        StatusText.Text = "Audio settings saved.";
    }
    private void OpenWorkspaceFolder_Click(object sender, RoutedEventArgs e) => OpenPath(Path.GetDirectoryName(_manager.WorkspacePath) ?? _manager.WorkspacePath);
    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void Maximize_Click(object sender, RoutedEventArgs e) => TogglePseudoMaximized();

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

    private void DeleteLayout_Click(object sender, RoutedEventArgs e)
    {
        var name = LayoutsComboBox.SelectedItem is LayoutProfile profile
            ? profile.Name
            : LayoutNameTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        var result = MessageBox.Show(
            this,
            $"Delete layout '{name}'?\n\nThis removes only the saved Pandora layout. Files and shortcuts are not touched.",
            "Delete layout",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);
        if (result != MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            WorkspaceLayoutService.DeleteLayout(_manager.Workspace, name);
            _manager.Save();
            _manager.Reload();
            StatusText.Text = $"Deleted layout '{name}'.";
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
        _manager.Save();
        _manager.Reload();
        StatusText.Text = "Dock saved and reloaded.";
    }

    private void AddZone_Click(object sender, RoutedEventArgs e)
    {
        var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        var zone = new ZoneDefinition
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = "New Dock",
            Bounds = new ZoneBounds { X = 150, Y = 150, Width = 360, Height = 300 },
            Appearance = new ZoneAppearance { AccentColor = "#4FB3FF" },
            Tabs =
            [
                new ZoneTabDefinition
                {
                    Id = Guid.NewGuid().ToString("N"),
                    Name = "New Dock",
                    Path = PathExpander.CompressUserPath(documents)
                }
            ]
        };

        _manager.Workspace.Zones.Add(zone);
        _manager.Save();
        _manager.Reload();
        ZonesList.SelectedItem = _manager.Workspace.Zones.FirstOrDefault(candidate => candidate.Id == zone.Id);
        StatusText.Text = "Added dock.";
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
            AgentFeed = new AgentFeedDockSettings
            {
                FeedIds = source.AgentFeed.FeedIds.ToList(),
                DisplayMode = source.AgentFeed.DisplayMode,
                MarkReadOnExpand = source.AgentFeed.MarkReadOnExpand
            },
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
        ZonesList.SelectedItem = _manager.Workspace.Zones.FirstOrDefault(candidate => candidate.Id == clone.Id);
        StatusText.Text = "Duplicated dock.";
    }

    private void DeleteZone_Click(object sender, RoutedEventArgs e)
    {
        if (ZonesList.SelectedItem is not ZoneDefinition zone)
        {
            return;
        }

        if (MessageBox.Show(this, $"Remove '{zone.Name}' from this workspace?\n\nOnly dock metadata is removed. Source files are not deleted.",
                "Remove dock", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes)
            return;

        _manager.Workspace.Zones.Remove(zone);
        _manager.Save();
        _manager.Reload();
        StatusText.Text = "Removed dock metadata. Files were not touched.";
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

        WorkspaceLayoutService.RestoreDockBounds(_manager.Workspace, zone.Id, DisplaySnapshotProvider.GetDisplays(this));
        _manager.Save();
        _manager.Reload();
        StatusText.Text = $"Restored '{zone.Name}' to a normal dock size.";
    }

    private void CenterZone_Click(object sender, RoutedEventArgs e)
    {
        if (ZonesList.SelectedItem is not ZoneDefinition zone)
        {
            return;
        }

        var display = ChooseCenterDisplay(DisplaySnapshotProvider.GetDisplays(this), zone.Bounds);
        var width = Math.Clamp(
            zone.Bounds.Width,
            WorkspaceLayoutService.MinimumDockWidth,
            Math.Max(WorkspaceLayoutService.MinimumDockWidth, display.WorkAreaWidth - WorkspaceLayoutService.DockWorkAreaMargin * 2));
        var height = Math.Clamp(
            zone.Bounds.Height,
            WorkspaceLayoutService.MinimumDockHeight,
            Math.Max(WorkspaceLayoutService.MinimumDockHeight, display.WorkAreaHeight - WorkspaceLayoutService.DockWorkAreaMargin * 2));
        var x = display.WorkAreaX + (display.WorkAreaWidth - width) / 2;
        var y = display.WorkAreaY + (display.WorkAreaHeight - height) / 2;

        zone.IsVisible = true;
        zone.IsCollapsed = false;
        WorkspaceLayoutService.SetDockBounds(_manager.Workspace, zone.Id, x, y, width, height);
        _manager.Save();
        _manager.Reload();
        StatusText.Text = $"Centered '{zone.Name}' on the active display.";
    }

    private void RepairDockSizes_Click(object sender, RoutedEventArgs e)
    {
        var changed = WorkspaceLayoutService.RepairOversizedDockBounds(_manager.Workspace, DisplaySnapshotProvider.GetDisplays(this));
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
        if (e.ChangedButton != MouseButton.Left)
        {
            return;
        }

        if (e.ClickCount == 2)
        {
            TogglePseudoMaximized();
            return;
        }

        if (_isPseudoMaximized)
        {
            RestoreForHeaderDrag(e);
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

    protected override void OnStateChanged(EventArgs e)
    {
        base.OnStateChanged(e);

        if (_isCoercingMaximize || WindowState != WindowState.Maximized)
        {
            return;
        }

        _isCoercingMaximize = true;
        Dispatcher.BeginInvoke(new Action(() =>
        {
            try
            {
                if (!_isPseudoMaximized && RestoreBounds.Width > 0 && RestoreBounds.Height > 0)
                {
                    _restoreBounds = RestoreBounds;
                }

                WindowState = WindowState.Normal;
                ApplyBounds(GetCurrentScreenWorkArea());
                _isPseudoMaximized = true;
            }
            finally
            {
                _isCoercingMaximize = false;
            }
        }));
    }

    private void TogglePseudoMaximized()
    {
        if (_isPseudoMaximized)
        {
            ApplyBounds(_restoreBounds);
            _isPseudoMaximized = false;
            return;
        }

        _restoreBounds = new Rect(Left, Top, Width, Height);
        var workArea = GetCurrentScreenWorkArea();
        WindowState = WindowState.Normal;
        ApplyBounds(workArea);
        _isPseudoMaximized = true;
    }

    private void RestoreForHeaderDrag(MouseButtonEventArgs e)
    {
        var pointer = DevicePointToDip(PointToScreen(e.GetPosition(this)));
        var headerPoint = e.GetPosition(this);
        var restoreWidth = Math.Max(MinWidth, _restoreBounds.Width);
        var restoreHeight = Math.Max(MinHeight, _restoreBounds.Height);
        var xRatio = ActualWidth <= 1 ? 0.5 : Math.Clamp(headerPoint.X / ActualWidth, 0.18, 0.82);

        Width = restoreWidth;
        Height = restoreHeight;
        Left = pointer.X - restoreWidth * xRatio;
        Top = pointer.Y - Math.Min(24, restoreHeight / 3);
        _isPseudoMaximized = false;
    }

    private Rect GetCurrentScreenWorkArea()
    {
        return DisplaySnapshotProvider.GetWorkingAreaForBounds(Left, Top, Width, Height, this);
    }

    private Point DevicePointToDip(Point point)
    {
        var source = PresentationSource.FromVisual(this);
        return source?.CompositionTarget is null
            ? point
            : source.CompositionTarget.TransformFromDevice.Transform(point);
    }

    private void ApplyBounds(Rect bounds)
    {
        Left = bounds.Left;
        Top = bounds.Top;
        Width = Math.Max(MinWidth, bounds.Width);
        Height = Math.Max(MinHeight, bounds.Height);
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
        SaveZoneButton.IsEnabled = enabled;
        CenterZoneButton.IsEnabled = enabled;
        RestoreSizeButton.IsEnabled = enabled;
        DuplicateZoneButton.IsEnabled = enabled;
        DeleteZoneButton.IsEnabled = enabled;

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
        if (zone.Kind != ZoneKind.Standard)
        {
            PathTextBox.Text = zone.Kind == ZoneKind.Music ? "Managed in Audio settings" : "Managed by the dock's source settings";
            PathTextBox.IsReadOnly = true;
        }
        else if (primaryTab?.Source == ZoneTabSource.SmartDesktop)
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
        zone.Name = string.IsNullOrWhiteSpace(NameTextBox.Text) ? "Untitled Dock" : NameTextBox.Text.Trim();
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

        if (zone.Kind != ZoneKind.Standard) return;

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
        if (enabled == _initialStartupSelection) return string.Empty;
        var wasEnabled = StartupAppService.IsEnabled();
        try
        {
            StartupAppService.SetEnabled(enabled, _manager.Workspace.Settings.IconStyle);
            _manager.Workspace.Settings.StartWithWindows = enabled;
            _initialStartupSelection = enabled;
            return enabled == wasEnabled
                ? string.Empty
                : enabled ? "Startup app enabled." : "Startup app disabled.";
        }
        catch (Exception ex) when (ex is InvalidOperationException or UnauthorizedAccessException or IOException or System.Runtime.InteropServices.COMException)
        {
            StartWithWindowsCheckBox.IsChecked = _initialStartupSelection;
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

        try
        {
            _ = System.Windows.Media.ColorConverter.ConvertFromString(trimmed);
            return trimmed.Length is 7 or 9 ? trimmed : fallback;
        }
        catch (Exception ex) when (ex is FormatException or NotSupportedException)
        {
            return fallback;
        }
    }

    private static DisplayDescriptor ChooseCenterDisplay(IReadOnlyList<DisplayDescriptor> displays, ZoneBounds bounds)
    {
        if (displays.Count == 0)
        {
            return new DisplayDescriptor
            {
                IsPrimary = true,
                WorkAreaWidth = 1920,
                WorkAreaHeight = 1040,
                BoundsWidth = 1920,
                BoundsHeight = 1080
            };
        }

        var centerX = bounds.X + bounds.Width / 2;
        var centerY = bounds.Y + bounds.Height / 2;
        var containingDisplay = displays.FirstOrDefault(display =>
            centerX >= display.WorkAreaX &&
            centerX <= display.WorkAreaX + display.WorkAreaWidth &&
            centerY >= display.WorkAreaY &&
            centerY <= display.WorkAreaY + display.WorkAreaHeight);
        if (containingDisplay is not null)
        {
            return containingDisplay;
        }

        return displays
            .OrderByDescending(display => display.IsPrimary)
            .ThenBy(display => DistanceToDisplayCenter(bounds, display))
            .First();
    }

    private static double DistanceToDisplayCenter(ZoneBounds bounds, DisplayDescriptor display)
    {
        var boundsCenterX = bounds.X + bounds.Width / 2;
        var boundsCenterY = bounds.Y + bounds.Height / 2;
        var displayCenterX = display.WorkAreaX + display.WorkAreaWidth / 2;
        var displayCenterY = display.WorkAreaY + display.WorkAreaHeight / 2;
        var dx = boundsCenterX - displayCenterX;
        var dy = boundsCenterY - displayCenterY;
        return dx * dx + dy * dy;
    }

    private static double TryReadDouble(string value, double fallback, double min, double max)
    {
        return double.TryParse(value, out var parsed) && double.IsFinite(parsed)
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
