using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using CustomFences.Core;
using Forms = System.Windows.Forms;
using DrawingRectangle = System.Drawing.Rectangle;

namespace CustomFences.App;

public partial class ZoneWindow : Window
{
    public const string OrbitDockItemFormat = "OrbitDock.Item.Path";
    public const string OrbitDockSourceDockFormat = "OrbitDock.Item.SourceDock";
    public const string OrbitDockSourceTabFormat = "OrbitDock.Item.SourceTab";
    public const string OrbitDockDesktopPinFormat = "OrbitDock.DesktopPin.Id";

    private const double CollapsedHeight = 54;
    private const double MinimumExpandedHeight = 180;
    private readonly DesktopZoneManager _manager;
    private readonly ZoneViewModel _viewModel;
    private bool _isApplyingPlacement;
    private bool _isDesktopAttached;
    private double _expandedHeight;
    private Point _dragStartPoint;
    private FileItemViewModel? _dragItem;

    public ZoneWindow(ZoneViewModel viewModel, DesktopZoneManager manager)
    {
        InitializeComponent();
        _viewModel = viewModel;
        _manager = manager;
        DataContext = viewModel;
        _expandedHeight = Math.Max(viewModel.Zone.Bounds.Height, MinimumExpandedHeight);
        _manager.MusicEnded += Manager_MusicEnded;
    }

    public bool IsCollapsed => _viewModel.Zone.IsCollapsed;

    public void SetPeek(bool visible)
    {
        Topmost = false;
        Show();
        _manager.ApplyDockLayering();
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        ApplyPlacement();
        RenderTabs();
        RenderMusicControls();
        ApplyExpansionEdgeLayout();
        ApplyCollapsedState();
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

    public void EnsureDesktopOverlayVisible()
    {
        if (!IsLoaded)
        {
            return;
        }

        DockWindowLayer.ApplyDesktopOverlayStyles(this);
        TryAttachToDesktop();
        if (WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Normal;
        }

        if (!IsVisible)
        {
            Show();
        }

        _manager.ApplyDockLayering();
    }

    private void Window_PlacementChanged(object sender, EventArgs e)
    {
        if (_isApplyingPlacement || !IsLoaded)
        {
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

    private void Refresh_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.Refresh();
        _manager.Audio.PlaySoundEffect(_manager.Workspace, "refresh");
    }

    private void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        OpenPath(_viewModel.SelectedFolderPath);
        _manager.Audio.PlaySoundEffect(_manager.Workspace, "item-open");
    }

    private void Collapse_Click(object sender, RoutedEventArgs e)
    {
        ToggleCollapsed();
    }

    private void Window_DragOver(object sender, System.Windows.DragEventArgs e)
    {
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
            $"Delete the real item from disk?\n\n{item.Path}",
            "Delete real file",
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
                Directory.Delete(item.Path, recursive: true);
            }
            else if (File.Exists(item.Path))
            {
                File.Delete(item.Path);
            }

            _viewModel.RemoveFromDock(item.Path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(this, ex.Message, "Delete failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }

        ItemsList.SelectedItem = null;
    }

    protected override void OnClosed(EventArgs e)
    {
        _manager.MusicEnded -= Manager_MusicEnded;
        _viewModel.Dispose();
        base.OnClosed(e);
    }

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
                Foreground = Brushes.White,
                Background = tab == _viewModel.SelectedTab ? _viewModel.AccentBrush : Brushes.Transparent,
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
        _viewModel.Zone.IsCollapsed = !_viewModel.Zone.IsCollapsed;
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
            if (_viewModel.Zone.IsCollapsed)
            {
                var bottom = Top + Height;
                _expandedHeight = Math.Max(_expandedHeight, _viewModel.Zone.Bounds.Height);
                Height = CollapsedHeight;
                if (_viewModel.ExpansionEdge == DockExpansionEdge.Bottom)
                {
                    Top = bottom - Height;
                }

                ContentHost.Visibility = Visibility.Collapsed;
                StatusBorder.Visibility = Visibility.Collapsed;
                CollapseButton.Content = "\uE70E";
            }
            else
            {
                var bottom = Top + Height;
                UpdateWindowMaximums(Left, Top, Width, Math.Max(_expandedHeight, MinimumExpandedHeight));
                Height = Math.Min(Math.Max(_expandedHeight, MinimumExpandedHeight), MaxHeight);
                _expandedHeight = Height;
                if (_viewModel.ExpansionEdge == DockExpansionEdge.Bottom)
                {
                    Top = bottom - Height;
                }

                ContentHost.Visibility = Visibility.Visible;
                StatusBorder.Visibility = Visibility.Visible;
                CollapseButton.Content = "\uE96E";
            }

            ApplyContentMode();
            SaveCurrentWindowBoundsToModel();
        }
        finally
        {
            _isApplyingPlacement = false;
        }
    }

    private void TryAttachToDesktop()
    {
        if (_isDesktopAttached || (!_manager.Workspace.Settings.StayVisibleOnShowDesktop && !_manager.Workspace.Settings.AttachWindowsToDesktop))
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

            _viewModel.Zone.IsCollapsed = false;
            var restoredBounds = CreateRestoredWindowBounds(GetCurrentWindowBounds());
            ApplyWindowBounds(restoredBounds);
            ContentHost.Visibility = Visibility.Visible;
            StatusBorder.Visibility = Visibility.Visible;
            CollapseButton.Content = "\uE96E";
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
        return new ZoneBounds
        {
            X = Left,
            Y = _viewModel.Zone.IsCollapsed && _viewModel.ExpansionEdge == DockExpansionEdge.Bottom
                ? Top + Height - _expandedHeight
                : Top,
            Width = Width,
            Height = _viewModel.Zone.IsCollapsed ? Math.Max(_expandedHeight, MinimumExpandedHeight) : Height
        };
    }

    private void SaveCurrentWindowBoundsToModel()
    {
        _viewModel.Zone.Bounds.X = Left;
        _viewModel.Zone.Bounds.Y = _viewModel.Zone.IsCollapsed && _viewModel.ExpansionEdge == DockExpansionEdge.Bottom
            ? Top + Height - _expandedHeight
            : Top;
        _viewModel.Zone.Bounds.Width = Width;
        if (!_viewModel.Zone.IsCollapsed)
        {
            _viewModel.Zone.Bounds.Height = Height;
            _expandedHeight = Height;
        }
    }

    private void ApplyWindowBounds(ZoneBounds bounds)
    {
        UpdateWindowMaximums(bounds.X, bounds.Y, bounds.Width, bounds.Height);
        Width = Math.Max(MinWidth, Math.Min(bounds.Width, MaxWidth));
        Height = Math.Max(MinHeight, Math.Min(bounds.Height, MaxHeight));
        Left = bounds.X;
        Top = bounds.Y;
        if (!_viewModel.Zone.IsCollapsed)
        {
            _expandedHeight = Height;
        }
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
            : Clamp(source.Height, MinHeight, maxHeight);

        return new ZoneBounds
        {
            X = Clamp(
                source.X,
                area.Left + WorkspaceLayoutService.DockWorkAreaMargin,
                Math.Max(area.Left + WorkspaceLayoutService.DockWorkAreaMargin, area.Right - width - WorkspaceLayoutService.DockWorkAreaMargin)),
            Y = Clamp(
                source.Y,
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
        return new ZoneBounds
        {
            X = Clamp(
                source.X,
                area.Left + WorkspaceLayoutService.DockWorkAreaMargin,
                Math.Max(area.Left + WorkspaceLayoutService.DockWorkAreaMargin, area.Right - width - WorkspaceLayoutService.DockWorkAreaMargin)),
            Y = Clamp(
                source.Y,
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

    private static Rect GetWorkingArea(double x, double y, double width, double height)
    {
        var rectangle = new DrawingRectangle(
            (int)Math.Round(x),
            (int)Math.Round(y),
            Math.Max(1, (int)Math.Round(width)),
            Math.Max(1, (int)Math.Round(height)));
        var workingArea = Forms.Screen.FromRectangle(rectangle).WorkingArea;
        return new Rect(workingArea.X, workingArea.Y, workingArea.Width, workingArea.Height);
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
        if (_viewModel.ExpansionEdge == DockExpansionEdge.Bottom)
        {
            TopChromeRow.Height = new GridLength(26);
            BottomChromeRow.Height = new GridLength(_viewModel.HeaderHeight);
            Grid.SetRow(StatusBorder, 0);
            Grid.SetRow(ContentHost, 1);
            Grid.SetRow(HeaderBorder, 2);
            HeaderBorder.CornerRadius = new CornerRadius(0, 0, 17, 17);
            StatusBorder.CornerRadius = new CornerRadius(17, 17, 0, 0);
            return;
        }

        TopChromeRow.Height = new GridLength(_viewModel.HeaderHeight);
        BottomChromeRow.Height = new GridLength(26);
        Grid.SetRow(HeaderBorder, 0);
        Grid.SetRow(ContentHost, 1);
        Grid.SetRow(StatusBorder, 2);
        HeaderBorder.CornerRadius = new CornerRadius(17, 17, 0, 0);
        StatusBorder.CornerRadius = new CornerRadius(0, 0, 17, 17);
    }

    private void ApplyContentMode()
    {
        if (_viewModel.Zone.IsCollapsed)
        {
            return;
        }

        ItemsList.Visibility = _viewModel.IsMusicDock ? Visibility.Collapsed : Visibility.Visible;
        MusicPanel.Visibility = _viewModel.IsMusicDock ? Visibility.Visible : Visibility.Collapsed;
    }

    private void RenderMusicControls()
    {
        MusicHeaderControls.Visibility = _viewModel.IsMusicDock ? Visibility.Visible : Visibility.Collapsed;
        OpenFolderButtonVisibility();
        RepeatComboBox.ItemsSource = Enum.GetValues(typeof(MusicRepeatMode));
        RepeatComboBox.SelectedItem = _viewModel.MusicRepeat;
    }

    private void OpenFolderButtonVisibility()
    {
        // Music docks expose their folder from the expanded music panel.
        HeaderButtons.Children.OfType<Button>()
            .Where(button => Equals(button.ToolTip, "Open in Explorer"))
            .ToList()
            .ForEach(button => button.Visibility = _viewModel.IsMusicDock ? Visibility.Collapsed : Visibility.Visible);
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
