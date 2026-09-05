using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using OrbitDock.Core;

namespace OrbitDock.App;

public partial class ZoneWindow : Window
{
    public const string OrbitDockItemFormat = "OrbitDock.Item.Path";
    public const string OrbitDockSourceDockFormat = "OrbitDock.Item.SourceDock";
    public const string OrbitDockSourceTabFormat = "OrbitDock.Item.SourceTab";
    public const string OrbitDockDesktopPinFormat = "OrbitDock.DesktopPin.Id";

    private const double MinimumExpandedHeight = 180;
    private readonly DesktopZoneManager _manager;
    private readonly ZoneViewModel _viewModel;
    private bool _isApplyingPlacement;
    private bool _isDesktopAttached;
    private double _expandedHeight;
    private Point _dragStartPoint;
    private FileItemViewModel? _dragItem;
    private ProjectsControl? _projectsControl;

    public ZoneWindow(ZoneViewModel viewModel, DesktopZoneManager manager)
    {
        InitializeComponent();
        _viewModel = viewModel;
        _manager = manager;
        Icon = BrandIdentity.Image(manager.Workspace.Settings.IconStyle);
        DataContext = viewModel;
        _expandedHeight = Math.Max(viewModel.Zone.Bounds.Height, MinimumExpandedHeight);
        _manager.MusicEnded += Manager_MusicEnded;
        ThemeService.ThemeChanged += RefreshTheme;
        if (viewModel.IsProjectsDock)
        {
            _projectsControl = new ProjectsControl(Path.Combine(Path.GetDirectoryName(manager.WorkspacePath)!, "projects.json"));
            ProjectsHost.Content = _projectsControl;
            MinWidth = 420;
        }
        ApplyStructuralTheme();
    }

    public bool IsCollapsed => _viewModel.Zone.IsCollapsed;
    public double CollapsedVisualHeight => _viewModel.HeaderHeight + Frame.BorderThickness.Top + Frame.BorderThickness.Bottom;

    public void SetPeek(bool visible)
    {
        Topmost = false;
        if (!IsVisible)
        {
            Show();
        }

        DockWindowLayer.ShowNoActivate(this);
        _manager.ApplyDockLayering();
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        ApplyPlacement();
        RenderTabs();
        RenderMusicControls();
        ApplyExpansionEdgeLayout();
        ApplyCollapsedState();
        if (_viewModel.IsAgentFeedDock && !_viewModel.Zone.IsCollapsed)
        {
            Dispatcher.BeginInvoke(_viewModel.MarkSelectedAgentFeedRead);
        }
        Dispatcher.BeginInvoke(() => _manager.ApplyDockLayering());
    }

    private void Window_SourceInitialized(object sender, EventArgs e)
    {
        DockWindowLayer.ApplyDesktopOverlayStyles(this);
        TryAttachToDesktop();

        _manager.ApplyDockLayering();
    }

    private void Window_Deactivated(object? sender, EventArgs e)
    {
        _manager.ApplyDockLayering();
    }

    public bool MaintainDesktopOverlay(bool restoreHiddenWindow, bool refreshLayering)
    {
        if (!IsLoaded)
        {
            return false;
        }

        var needsRestore = WindowState == WindowState.Minimized || !IsVisible;
        if (!needsRestore && !refreshLayering)
        {
            return false;
        }

        DockWindowLayer.ApplyDesktopOverlayStyles(this);
        TryAttachToDesktop();
        if (WindowState == WindowState.Minimized)
        {
            if (!restoreHiddenWindow)
            {
                return false;
            }

            WindowState = WindowState.Normal;
        }

        if (!IsVisible)
        {
            if (!restoreHiddenWindow)
            {
                return false;
            }

            Show();
        }

        if (needsRestore && restoreHiddenWindow)
        {
            DockWindowLayer.ShowNoActivate(this);
        }

        return true;
    }

    private void Window_PlacementChanged(object sender, EventArgs e)
    {
        if (_isApplyingPlacement || !IsLoaded)
        {
            return;
        }

        if (WindowState == WindowState.Minimized)
        {
            return;
        }

        if (!_manager.IsCurrentDisplayVariantActive())
        {
            _manager.QueueDisplayVariantRefresh();
            return;
        }

        if (WindowState != WindowState.Normal || IsSnapSizedDock(Left, Top, Width, Height))
        {
            RestoreReasonableSize(save: true);
            return;
        }

        UpdateWindowMaximums(Left, Top, Width, Height);
        var currentBounds = GetCurrentWindowBounds();
        var normalizedBounds = NormalizeWindowBounds(currentBounds, restoreSnapSizedDock: false);
        if (Math.Abs(currentBounds.Width - normalizedBounds.Width) > 0.1 ||
            Math.Abs(currentBounds.Height - normalizedBounds.Height) > 0.1)
        {
            _isApplyingPlacement = true;
            try
            {
                ApplyWindowBounds(normalizedBounds);
                SaveCurrentWindowBoundsToModel();
            }
            finally
            {
                _isApplyingPlacement = false;
            }

            _manager.Save();
            return;
        }

        SaveCurrentWindowBoundsToModel();
        _manager.Save();
    }

    private void Window_StateChanged(object sender, EventArgs e)
    {
        if (_isApplyingPlacement || !IsLoaded || WindowState == WindowState.Normal)
        {
            return;
        }

        if (WindowState == WindowState.Minimized)
        {
            if (_manager.Workspace.Settings.StayVisibleOnShowDesktop && DockWindowLayer.IsDesktopExposed())
            {
                Dispatcher.BeginInvoke(() =>
                {
                    if (MaintainDesktopOverlay(restoreHiddenWindow: true, refreshLayering: true))
                    {
                        _manager.ApplyDockLayering();
                    }
                });
            }

            return;
        }

        Dispatcher.BeginInvoke(() => RestoreReasonableSize(save: true));
    }

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject source &&
            (FindAncestor<ButtonBase>(source) is not null ||
             FindAncestor<TextBox>(source) is not null ||
             FindAncestor<ComboBox>(source) is not null ||
             FindAncestor<Slider>(source) is not null))
        {
            return;
        }

        if (e.ClickCount == 2)
        {
            ToggleCollapsed();
            return;
        }

        if (_viewModel.Zone.IsLocked)
        {
            return;
        }

        try
        {
            DragMove();
        }
        catch
        {
            // DragMove throws if Windows cancels the drag. The next drag can still succeed.
        }

        _manager.ApplyDockLayering();
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e)
    {
        if (_projectsControl is not null)
        {
            await _projectsControl.RefreshAsync();
            return;
        }
        _viewModel.Refresh();
        _manager.Audio.PlaySoundEffect(_manager.Workspace, "refresh");
    }

    private void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.IsAgentFeedDock)
        {
            return;
        }

        OpenPath(_viewModel.SelectedFolderPath);
        _manager.Audio.PlaySoundEffect(_manager.Workspace, "item-open");
    }

    private void More_Click(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu { PlacementTarget = MoreButton, Placement = PlacementMode.Bottom };
        void Add(string title, RoutedEventHandler handler)
        {
            var item = new MenuItem { Header = title };
            item.Click += handler;
            menu.Items.Add(item);
        }
        Add("_Refresh", Refresh_Click);
        if (_viewModel.IsMusicDock)
        {
            Add("_Mute", MusicMute_Click);
            Add("Open _visualizer", MusicVisualizer_Click);
        }
        else if (!_viewModel.IsAgentFeedDock && !_viewModel.IsProjectsDock) Add("Open in _Explorer", OpenFolder_Click);
        menu.Items.Add(new Separator());
        Add("Pandora _settings…", (_, _) => _manager.ShowSettings());
        menu.IsOpen = true;
    }

    private void Collapse_Click(object sender, RoutedEventArgs e)
    {
        ToggleCollapsed();
    }

    private void Window_DragOver(object sender, System.Windows.DragEventArgs e)
    {
        if (_viewModel.IsProjectsDock) { e.Effects = DragDropEffects.None; e.Handled = true; return; }
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) || e.Data.GetDataPresent(OrbitDockItemFormat)
            ? DragDropEffects.Copy
            : DragDropEffects.None;
        e.Handled = true;
    }

    private void Window_Drop(object sender, System.Windows.DragEventArgs e)
    {
        HandleDrop(e, _viewModel.Items.Count);
    }

    private void ItemsList_DragOver(object sender, System.Windows.DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(OrbitDockItemFormat) || e.Data.GetDataPresent(DataFormats.FileDrop)
            ? DragDropEffects.Move
            : DragDropEffects.None;
        e.Handled = true;
    }

    private void ItemsList_Drop(object sender, System.Windows.DragEventArgs e)
    {
        HandleDrop(e, GetDropIndex(e));
    }

    private void HandleDrop(System.Windows.DragEventArgs e, int targetIndex)
    {
        if (_viewModel.IsProjectsDock)
        {
            e.Effects = DragDropEffects.None;
            e.Handled = true;
            return;
        }
        if (e.Data.GetDataPresent(OrbitDockItemFormat))
        {
            var path = e.Data.GetData(OrbitDockItemFormat) as string;
            var sourceDock = e.Data.GetData(OrbitDockSourceDockFormat) as string;
            var sourceTab = e.Data.GetData(OrbitDockSourceTabFormat) as string;
            if (!string.IsNullOrWhiteSpace(path) && _viewModel.MoveItemHere(path, sourceDock, sourceTab, targetIndex))
            {
                if (e.Data.GetData(OrbitDockDesktopPinFormat) is string pinId && !string.IsNullOrWhiteSpace(pinId))
                {
                    _manager.RemoveDesktopPin(pinId);
                }

                e.Effects = DragDropEffects.Move;
                _manager.ApplyDockLayering();
            }

            e.Handled = true;
            return;
        }

        if (!e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            return;
        }

        var files = (string[])e.Data.GetData(DataFormats.FileDrop);
        if (_viewModel.IsSmartDock)
        {
            foreach (var file in files.Reverse())
            {
                _viewModel.MoveItemHere(file, null, null, targetIndex);
            }
        }
        else
        {
            _viewModel.AddDroppedFiles(files, _manager.Workspace.Settings.DefaultDropAction);
            _manager.Save();
        }

        e.Handled = true;
    }

    private void ItemsList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (ItemsList.SelectedItem is FileItemViewModel item)
        {
            OpenPath(item.Path);
            ItemsList.SelectedItem = null;
            _manager.Audio.PlaySoundEffect(_manager.Workspace, "item-open");
            _manager.ApplyDockLayering();
        }
    }

    private void Search_Click(object sender, RoutedEventArgs e)
    {
        var willOpen = SearchBox.Visibility != Visibility.Visible;
        SearchBox.Visibility = willOpen ? Visibility.Visible : Visibility.Collapsed;
        DockTitlePanel.Visibility = willOpen ? Visibility.Collapsed : Visibility.Visible;
        if (willOpen)
        {
            SearchBox.Focus();
            SearchBox.SelectAll();
            _manager.Audio.PlaySoundEffect(_manager.Workspace, "search-open");
        }
        else
        {
            SearchBox.Text = string.Empty;
            _viewModel.ClearSearch();
            _manager.Audio.PlaySoundEffect(_manager.Workspace, "search-close");
        }
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        _viewModel.SearchQuery = SearchBox.Text;
        if (!string.IsNullOrWhiteSpace(SearchBox.Text))
        {
            _manager.Audio.PlaySoundEffect(_manager.Workspace, "search-typing");
        }
    }

    private void SearchBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape)
        {
            return;
        }

        SearchBox.Text = string.Empty;
        SearchBox.Visibility = Visibility.Collapsed;
        DockTitlePanel.Visibility = Visibility.Visible;
        _viewModel.ClearSearch();
        ItemsList.SelectedItem = null;
        _manager.Audio.PlaySoundEffect(_manager.Workspace, "search-close");
        e.Handled = true;
    }

    private void ItemsList_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            ItemsList.SelectedItem = null;
            e.Handled = true;
        }
    }

    private void ItemsList_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is not DependencyObject source)
        {
            return;
        }

        if (FindAncestor<ScrollBar>(source) is not null)
        {
            return;
        }

        var container = ItemsControl.ContainerFromElement(ItemsList, source) as ListBoxItem;
        if (container?.DataContext is FileItemViewModel item)
        {
            _dragStartPoint = e.GetPosition(ItemsList);
            _dragItem = item;
            return;
        }

        _dragItem = null;
        ItemsList.SelectedItem = null;
    }

    private void ItemsList_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (_dragItem is null || e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        var currentPosition = e.GetPosition(ItemsList);
        if (Math.Abs(currentPosition.X - _dragStartPoint.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(currentPosition.Y - _dragStartPoint.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        var data = new DataObject();
        data.SetData(OrbitDockItemFormat, _dragItem.Path);
        data.SetData(OrbitDockSourceDockFormat, _viewModel.DockId);
        if (!string.IsNullOrWhiteSpace(_viewModel.SelectedTabId))
        {
            data.SetData(OrbitDockSourceTabFormat, _viewModel.SelectedTabId);
        }

        data.SetData(DataFormats.FileDrop, new[] { _dragItem.Path });
        DragDrop.DoDragDrop(ItemsList, data, DragDropEffects.Copy | DragDropEffects.Move);
        _dragItem = null;
    }

    private void ItemsList_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is not DependencyObject source)
        {
            return;
        }

        var container = ItemsControl.ContainerFromElement(ItemsList, source) as ListBoxItem;
        if (container?.DataContext is FileItemViewModel item)
        {
            ItemsList.SelectedItem = item;
        }
    }

    private void OpenItem_Click(object sender, RoutedEventArgs e)
    {
        if (ItemsList.SelectedItem is FileItemViewModel item)
        {
            OpenPath(item.Path);
            ItemsList.SelectedItem = null;
            _manager.Audio.PlaySoundEffect(_manager.Workspace, "item-open");
            _manager.ApplyDockLayering();
        }
    }

    private void RemoveFromDock_Click(object sender, RoutedEventArgs e)
    {
        if (ItemsList.SelectedItem is not FileItemViewModel item)
        {
            return;
        }

        _viewModel.RemoveFromDock(item.Path);
        ItemsList.SelectedItem = null;
    }

    private void PinToDesktop_Click(object sender, RoutedEventArgs e)
    {
        if (ItemsList.SelectedItem is not FileItemViewModel item)
        {
            return;
        }

        _viewModel.PinToDesktop(item.Path);
        ItemsList.SelectedItem = null;
    }

    private void RevealItem_Click(object sender, RoutedEventArgs e)
    {
        if (ItemsList.SelectedItem is not FileItemViewModel item)
        {
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{item.Path}\"") { UseShellExecute = true });
        }
        catch
        {
            OpenPath(Path.GetDirectoryName(item.Path) ?? item.Path);
        }

        ItemsList.SelectedItem = null;
    }

    private void MusicPrevious_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.PlayPreviousTrack();
        _manager.Audio.PlaySoundEffect(_manager.Workspace, "music-previous");
    }

    private void MusicPlayPause_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.ToggleMusicPlayback();
        _manager.Audio.PlaySoundEffect(_manager.Workspace, "music-play");
    }

    private void MusicNext_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.PlayNextTrack();
        _manager.Audio.PlaySoundEffect(_manager.Workspace, "music-next");
    }

    private void MusicMute_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.MusicVolume = _viewModel.MusicVolume > 0 ? 0 : 0.35;
        _manager.Audio.PlaySoundEffect(_manager.Workspace, "music-mute");
    }

    private void MusicVisualizer_Click(object sender, RoutedEventArgs e)
    {
        if (MusicVisualizerLauncher.TryLaunch(out var error))
        {
            _manager.Audio.PlaySoundEffect(_manager.Workspace, "music-play");
            return;
        }

        MessageBox.Show(this, error, "Visualizer unavailable", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void MusicTracksList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        _viewModel.PlaySelectedTrack();
    }

    private void OpenMusicFolder_Click(object sender, RoutedEventArgs e)
    {
        var path = PathExpander.Expand(_manager.Workspace.Settings.Audio.MusicRootPath);
        try
        {
            Directory.CreateDirectory(path);
        }
        catch
        {
            // Explorer can still try to open the parent if creation fails.
        }

        OpenPath(path);
    }

    private void RepeatComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (RepeatComboBox.SelectedItem is MusicRepeatMode mode)
        {
            _viewModel.MusicRepeat = mode;
        }
    }

    private void DeleteRealFile_Click(object sender, RoutedEventArgs e)
    {
        if (ItemsList.SelectedItem is not FileItemViewModel item)
        {
            return;
        }

        var result = MessageBox.Show(
            this,
            $"Move the real item to the Recycle Bin?\n\n{item.Path}",
            "Move real file to Recycle Bin",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);
        if (result != MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            if (Directory.Exists(item.Path))
            {
                Microsoft.VisualBasic.FileIO.FileSystem.DeleteDirectory(
                    item.Path,
                    Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
                    Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
            }
            else if (File.Exists(item.Path))
            {
                Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(
                    item.Path,
                    Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
                    Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
            }

            _viewModel.RemoveFromDock(item.Path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or OperationCanceledException)
        {
            MessageBox.Show(this, ex.Message, "Recycle failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }

        ItemsList.SelectedItem = null;
    }

    protected override void OnClosed(EventArgs e)
    {
        ThemeService.ThemeChanged -= RefreshTheme;
        _projectsControl?.Dispose();
        _manager.MusicEnded -= Manager_MusicEnded;
        _viewModel.Dispose();
        base.OnClosed(e);
    }

    private void RefreshTheme(object? sender, EventArgs e)
    {
        var expandedBounds = IsLoaded ? GetCurrentWindowBounds() : _viewModel.Zone.Bounds;
        Icon = BrandIdentity.Image(_manager.Workspace.Settings.IconStyle);
        ApplyStructuralTheme();
        RenderTabs();
        var wasApplying = _isApplyingPlacement;
        _isApplyingPlacement = true;
        try { ApplyWindowBounds(expandedBounds); }
        finally { _isApplyingPlacement = wasApplying; }
    }

    private void ApplyStructuralTheme()
    {
        var profile = _viewModel.ThemeProfile;
        var detached = profile.SeparatedHeader;
        Frame.BorderThickness = detached ? new Thickness(0) : profile.FrameBorderThickness;
        HeaderBorder.BorderBrush = _viewModel.BorderBrush;
        HeaderBorder.BorderThickness = detached ? new Thickness(1) : new Thickness(0);
        BodyChrome.BorderThickness = detached ? new Thickness(1) : new Thickness(0);
        AccentRail.Width = profile.AccentRailWidth;
        AccentRail.CornerRadius = new CornerRadius(Math.Min(2, profile.CornerRadius));
        HeaderControlsChrome.Background = detached ? ResourceBrush("Pandora.ElevatedBrush") : Brushes.Transparent;
        HeaderControlsChrome.CornerRadius = new CornerRadius(profile.ControlCornerRadius);
        HeaderControlsChrome.Padding = detached ? new Thickness(4) : new Thickness(0);
        HeaderControlsChrome.BorderBrush = _viewModel.BorderBrush;
        HeaderControlsChrome.BorderThickness = profile.Id == "Meridian" ? new Thickness(1) : new Thickness(0);
        BrandTile.CornerRadius = new CornerRadius(profile.Id == "Halo" ? 16 : profile.Id == "Meridian" ? 3 : 10);
        BrandTile.Width = BrandTile.Height = _viewModel.BarMetrics.BrandSize;
        ApplyExpansionEdgeLayout();
    }

    private Brush ResourceBrush(string key) => TryFindResource(key) as Brush ?? Brushes.Transparent;

    private void ApplyPlacement()
    {
        _isApplyingPlacement = true;
        try
        {
            var bounds = NormalizeWindowBounds(_viewModel.Zone.Bounds, restoreSnapSizedDock: true);
            ApplyWindowBounds(bounds);
            SaveCurrentWindowBoundsToModel();
        }
        finally
        {
            _isApplyingPlacement = false;
        }
    }

    private void RenderTabs()
    {
        TabsPanel.Children.Clear();
        if (_viewModel.Tabs.Count <= 1)
        {
            return;
        }

        foreach (var tab in _viewModel.Tabs)
        {
            var button = new Button
            {
                Content = tab.Name,
                Tag = tab,
                Margin = new Thickness(0, 0, 5, 0),
                MinWidth = 44,
                MaxWidth = 110,
                Foreground = ResourceBrush(tab == _viewModel.SelectedTab ? "Pandora.AccentTextBrush" : "Pandora.MutedBrush"),
                Background = tab == _viewModel.SelectedTab ? ResourceBrush("Pandora.AccentBrush") : Brushes.Transparent,
                Style = (Style)FindResource("DockTabButton")
            };
            button.Click += Tab_Click;
            TabsPanel.Children.Add(button);
        }
    }

    private void Tab_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: ZoneTabDefinition tab })
        {
            _viewModel.SelectTab(tab);
            RenderTabs();
        }
    }

    private void ToggleCollapsed()
    {
        // Capture before changing logical state; the current window may represent either projection.
        var expandedBounds = GetCurrentWindowBounds();
        _viewModel.Zone.IsCollapsed = !_viewModel.Zone.IsCollapsed;
        _viewModel.Zone.Bounds = expandedBounds;
        ApplyCollapsedState();
        _manager.Save();
        _manager.Audio.PlaySoundEffect(_manager.Workspace, _viewModel.Zone.IsCollapsed ? "dock-close" : "dock-bloom");
        _manager.ApplyDockLayering();
    }

    private void ApplyCollapsedState()
    {
        _isApplyingPlacement = true;
        try
        {
            ApplyWindowBounds(_viewModel.Zone.Bounds);
            ApplyExpansionEdgeLayout();
            ApplyContentMode();
            SaveCurrentWindowBoundsToModel();
            if (!_viewModel.Zone.IsCollapsed && _viewModel.IsAgentFeedDock)
            {
                Dispatcher.BeginInvoke(_viewModel.MarkSelectedAgentFeedRead);
            }
        }
        finally
        {
            _isApplyingPlacement = false;
        }
    }

    private void TryAttachToDesktop()
    {
        if (_isDesktopAttached || !_manager.Workspace.Settings.AttachWindowsToDesktop)
        {
            return;
        }

        _isDesktopAttached = DesktopHost.TryAttach(this);
    }

    private void RestoreReasonableSize(bool save)
    {
        _isApplyingPlacement = true;
        try
        {
            if (WindowState != WindowState.Normal)
            {
                WindowState = WindowState.Normal;
            }

            var restoredBounds = CreateRestoredWindowBounds(GetCurrentWindowBounds());
            ApplyWindowBounds(restoredBounds);
            ApplyExpansionEdgeLayout();
            SaveCurrentWindowBoundsToModel();
        }
        finally
        {
            _isApplyingPlacement = false;
        }

        if (save)
        {
            _manager.Save();
            _manager.ApplyDockLayering();
        }
    }

    private ZoneBounds GetCurrentWindowBounds()
    {
        return DockBoundsProjection.ToExpanded(new ZoneBounds { X = Left, Y = Top, Width = Width, Height = Height },
            IsCollapsed, _viewModel.ExpansionEdge, Math.Max(_expandedHeight, MinimumExpandedHeight));
    }

    private void SaveCurrentWindowBoundsToModel()
    {
        var expanded = GetCurrentWindowBounds();
        _viewModel.Zone.Bounds = expanded;
        _expandedHeight = expanded.Height;
    }

    private void ApplyWindowBounds(ZoneBounds bounds)
    {
        UpdateWindowMaximums(bounds.X, bounds.Y, bounds.Width, bounds.Height);
        _expandedHeight = Math.Max(MinimumExpandedHeight, Math.Min(bounds.Height, MaxHeight));
        var expandedY = IsCollapsed && _viewModel.ExpansionEdge == DockExpansionEdge.Bottom
            ? bounds.Y + bounds.Height - _expandedHeight : bounds.Y;
        var expanded = new ZoneBounds { X = bounds.X, Y = expandedY, Width = Math.Max(MinWidth, Math.Min(bounds.Width, MaxWidth)), Height = _expandedHeight };
        var visible = DockBoundsProjection.ToVisible(expanded, IsCollapsed, _viewModel.ExpansionEdge, CollapsedVisualHeight);
        // A closed window is not resizable. Window normalization must never apply its virtual
        // expanded height to the physical frame, even transiently during DragMove events.
        MinHeight = IsCollapsed ? CollapsedVisualHeight : MinimumExpandedHeight;
        ResizeMode = IsCollapsed ? ResizeMode.NoResize : ResizeMode.CanResizeWithGrip;
        Width = visible.Width;
        Height = visible.Height;
        Left = visible.X;
        Top = visible.Y;
        ApplyExpansionEdgeLayout();
    }

    private ZoneBounds NormalizeWindowBounds(ZoneBounds source, bool restoreSnapSizedDock)
    {
        var area = GetWorkingArea(source.X, source.Y, source.Width, source.Height);
        var maxWidth = GetMaximumDockWidth(area);
        var maxHeight = GetMaximumDockHeight(area);
        var snapSized = IsSnapSized(source.Width, source.Height, area);
        var width = restoreSnapSizedDock && snapSized
            ? Math.Min(WorkspaceLayoutService.DefaultRestoredDockWidth, maxWidth)
            : Clamp(source.Width, MinWidth, maxWidth);
        var height = restoreSnapSizedDock && snapSized
            ? Math.Min(WorkspaceLayoutService.DefaultRestoredDockHeight, maxHeight)
            : Clamp(source.Height, MinimumExpandedHeight, maxHeight);
        var anchoredY = IsCollapsed && _viewModel.ExpansionEdge == DockExpansionEdge.Bottom
            ? source.Y + source.Height - height : source.Y;

        return new ZoneBounds
        {
            X = Clamp(
                source.X,
                area.Left + WorkspaceLayoutService.DockWorkAreaMargin,
                Math.Max(area.Left + WorkspaceLayoutService.DockWorkAreaMargin, area.Right - width - WorkspaceLayoutService.DockWorkAreaMargin)),
            Y = Clamp(
                anchoredY,
                area.Top + WorkspaceLayoutService.DockWorkAreaMargin,
                Math.Max(area.Top + WorkspaceLayoutService.DockWorkAreaMargin, area.Bottom - height - WorkspaceLayoutService.DockWorkAreaMargin)),
            Width = width,
            Height = height
        };
    }

    private ZoneBounds CreateRestoredWindowBounds(ZoneBounds source)
    {
        var area = GetWorkingArea(source.X, source.Y, source.Width, source.Height);
        var width = Math.Min(WorkspaceLayoutService.DefaultRestoredDockWidth, GetMaximumDockWidth(area));
        var height = Math.Min(WorkspaceLayoutService.DefaultRestoredDockHeight, GetMaximumDockHeight(area));
        var anchoredY = IsCollapsed && _viewModel.ExpansionEdge == DockExpansionEdge.Bottom
            ? source.Y + source.Height - height : source.Y;
        return new ZoneBounds
        {
            X = Clamp(
                source.X,
                area.Left + WorkspaceLayoutService.DockWorkAreaMargin,
                Math.Max(area.Left + WorkspaceLayoutService.DockWorkAreaMargin, area.Right - width - WorkspaceLayoutService.DockWorkAreaMargin)),
            Y = Clamp(
                anchoredY,
                area.Top + WorkspaceLayoutService.DockWorkAreaMargin,
                Math.Max(area.Top + WorkspaceLayoutService.DockWorkAreaMargin, area.Bottom - height - WorkspaceLayoutService.DockWorkAreaMargin)),
            Width = width,
            Height = height
        };
    }

    private void UpdateWindowMaximums(double x, double y, double width, double height)
    {
        var area = GetWorkingArea(x, y, width, height);
        MaxWidth = GetMaximumDockWidth(area);
        MaxHeight = GetMaximumDockHeight(area);
    }

    private bool IsSnapSizedDock(double x, double y, double width, double height)
    {
        var area = GetWorkingArea(x, y, width, height);
        return IsSnapSized(width, height, area);
    }

    private static bool IsSnapSized(double width, double height, Rect area)
    {
        return width >= area.Width * WorkspaceLayoutService.DockSnapRestoreRatio ||
               height >= area.Height * WorkspaceLayoutService.DockSnapRestoreRatio;
    }

    private static double GetMaximumDockWidth(Rect area)
    {
        return Math.Max(
            WorkspaceLayoutService.MinimumDockWidth,
            Math.Min(area.Width - WorkspaceLayoutService.DockWorkAreaMargin * 2, area.Width * WorkspaceLayoutService.DockMaxWorkAreaRatio));
    }

    private static double GetMaximumDockHeight(Rect area)
    {
        return Math.Max(
            MinimumExpandedHeight,
            Math.Min(area.Height - WorkspaceLayoutService.DockWorkAreaMargin * 2, area.Height * WorkspaceLayoutService.DockMaxWorkAreaRatio));
    }

    private Rect GetWorkingArea(double x, double y, double width, double height)
    {
        return DisplaySnapshotProvider.GetWorkingAreaForBounds(x, y, width, height, this);
    }

    private static void OpenPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch
        {
            // Bad shortcuts, offline paths, or shell restrictions should not break the zone.
        }
    }

    private static double Clamp(double value, double min, double max)
    {
        if (max < min)
        {
            return min;
        }

        return Math.Min(Math.Max(value, min), max);
    }

    private void ApplyExpansionEdgeLayout()
    {
        var profile = _viewModel.ThemeProfile;
        var bottom = _viewModel.ExpansionEdge == DockExpansionEdge.Bottom;
        var collapsed = IsCollapsed;
        var separateNavigation = _viewModel.Tabs.Count > 1;
        // Auto accommodates an overflow scrollbar without clipping tab labels on narrow docks.
        var navigationHeight = collapsed ? new GridLength(0) : separateNavigation ? GridLength.Auto : new GridLength(profile.HeaderGap);
        var radius = Math.Max(0, _viewModel.CornerRadius.TopLeft - (profile.SeparatedHeader ? 0 : 1));
        TopChromeRow.Height = new GridLength(bottom ? (collapsed ? 0 : profile.FooterHeight) : _viewModel.HeaderHeight);
        BottomChromeRow.Height = new GridLength(bottom ? _viewModel.HeaderHeight : (collapsed ? 0 : profile.FooterHeight));
        TopNavigationRow.Height = bottom ? new GridLength(0) : navigationHeight;
        BottomNavigationRow.Height = bottom ? navigationHeight : new GridLength(0);
        ContentRow.Height = collapsed ? new GridLength(0) : new GridLength(1, GridUnitType.Star);
        Grid.SetRow(HeaderBorder, bottom ? 4 : 0);
        Grid.SetRow(StatusBorder, bottom ? 0 : 4);
        Grid.SetRow(NavigationHost, bottom ? 3 : 1);
        Grid.SetRow(ContentHost, 2);
        Grid.SetRow(BodyChrome, profile.SeparatedHeader && bottom ? 0 : 1);
        Grid.SetRowSpan(BodyChrome, profile.SeparatedHeader ? 4 : 3);
        BodyChrome.Margin = profile.SeparatedHeader ? new Thickness(0, bottom ? 0 : profile.HeaderGap, 0, bottom ? profile.HeaderGap : 0) : new Thickness(0);
        BodyChrome.CornerRadius = profile.SeparatedHeader ? new CornerRadius(profile.CornerRadius) : new CornerRadius(0);
        HeaderBorder.CornerRadius = collapsed || profile.SeparatedHeader ? new CornerRadius(radius) : bottom ? new CornerRadius(0, 0, radius, radius) : new CornerRadius(radius, radius, 0, 0);
        StatusBorder.CornerRadius = bottom ? new CornerRadius(radius, radius, 0, 0) : new CornerRadius(0, 0, radius, radius);
        StatusBorder.Background = profile.SeparatedHeader ? Brushes.Transparent : _viewModel.HeaderBrush;
        NavigationHost.Margin = new Thickness(12, bottom ? 0 : profile.HeaderGap, 12, bottom ? profile.HeaderGap : 0);
        NavigationHost.Padding = new Thickness(0, 4, 0, 4);
        NavigationHost.MinHeight = _viewModel.BarMetrics.NavigationHeight;
        NavigationHost.BorderBrush = _viewModel.BorderBrush;
        NavigationHost.BorderThickness = profile.Id == "Meridian" ? new Thickness(0, bottom ? 1 : 0, 0, bottom ? 0 : 1) : new Thickness(0);
        NavigationHost.Visibility = !collapsed && separateNavigation ? Visibility.Visible : Visibility.Collapsed;
        ContentHost.Visibility = StatusBorder.Visibility = BodyChrome.Visibility = collapsed ? Visibility.Collapsed : Visibility.Visible;
        CollapseButton.Content = collapsed ? "\uE70E" : "\uE96E";
        CollapseButton.ToolTip = collapsed ? "Expand dock" : "Roll up";
    }

    private void ApplyContentMode()
    {
        if (_viewModel.Zone.IsCollapsed)
        {
            return;
        }

        ItemsList.Visibility = _viewModel.IsMusicDock || _viewModel.IsAgentFeedDock || _viewModel.IsProjectsDock ? Visibility.Collapsed : Visibility.Visible;
        ProjectsHost.Visibility = _viewModel.IsProjectsDock ? Visibility.Visible : Visibility.Collapsed;
        MusicPanel.Visibility = _viewModel.IsMusicDock ? Visibility.Visible : Visibility.Collapsed;
        AgentFeedPanel.Visibility = _viewModel.IsAgentFeedDock ? Visibility.Visible : Visibility.Collapsed;
        AgentFeedSelector.Visibility = _viewModel.AgentFeeds.Count > 1 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void RenderMusicControls()
    {
        MusicHeaderControls.Visibility = _viewModel.IsMusicDock ? Visibility.Visible : Visibility.Collapsed;
        SearchButton.Visibility = _viewModel.IsAgentFeedDock || _viewModel.IsProjectsDock ? Visibility.Collapsed : Visibility.Visible;
        RepeatComboBox.ItemsSource = Enum.GetValues(typeof(MusicRepeatMode));
        RepeatComboBox.SelectedItem = _viewModel.MusicRepeat;
    }

    private void AgentFeedSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_viewModel.IsAgentFeedDock && !_viewModel.Zone.IsCollapsed)
        {
            _viewModel.MarkSelectedAgentFeedRead();
        }
    }

    private void Manager_MusicEnded()
    {
        _viewModel.HandleMusicEnded();
    }

    private int GetDropIndex(System.Windows.DragEventArgs e)
    {
        if (e.OriginalSource is not DependencyObject source)
        {
            return _viewModel.Items.Count;
        }

        var container = ItemsControl.ContainerFromElement(ItemsList, source) as ListBoxItem;
        return container?.DataContext is FileItemViewModel item
            ? Math.Max(0, ItemsList.Items.IndexOf(item))
            : _viewModel.Items.Count;
    }

    private static T? FindAncestor<T>(DependencyObject source)
        where T : DependencyObject
    {
        var current = source;
        while (current is not null)
        {
            if (current is T match)
            {
                return match;
            }

            current = VisualTreeHelper.GetParent(current);
        }

        return null;
    }
}
