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

namespace CustomFences.App;

public partial class ZoneWindow : Window
{
    public const string OrbitDockItemFormat = "OrbitDock.Item.Path";
    public const string OrbitDockSourceDockFormat = "OrbitDock.Item.SourceDock";
    public const string OrbitDockSourceTabFormat = "OrbitDock.Item.SourceTab";
    public const string OrbitDockDesktopPinFormat = "OrbitDock.DesktopPin.Id";

    private const double CollapsedHeight = 54;
    private readonly DesktopZoneManager _manager;
    private readonly ZoneViewModel _viewModel;
    private bool _isApplyingPlacement;
    private double _expandedHeight;
    private Point _dragStartPoint;
    private FileItemViewModel? _dragItem;

    public ZoneWindow(ZoneViewModel viewModel, DesktopZoneManager manager)
    {
        InitializeComponent();
        _viewModel = viewModel;
        _manager = manager;
        DataContext = viewModel;
        _expandedHeight = Math.Max(viewModel.Zone.Bounds.Height, 180);
    }

    public void SetPeek(bool visible)
    {
        Topmost = false;
        Show();
        DockWindowLayer.SendBehindNormalWindows(this);
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        ApplyPlacement();
        RenderTabs();
        ApplyCollapsedState();
        Dispatcher.BeginInvoke(() => DockWindowLayer.SendBehindNormalWindows(this));
    }

    private void Window_SourceInitialized(object sender, EventArgs e)
    {
        if (_manager.Workspace.Settings.AttachWindowsToDesktop)
        {
            DesktopHost.TryAttach(this);
        }

        DockWindowLayer.SendBehindNormalWindows(this);
    }

    private void Window_Deactivated(object? sender, EventArgs e)
    {
        DockWindowLayer.SendBehindNormalWindows(this);
    }

    private void Window_PlacementChanged(object sender, EventArgs e)
    {
        if (_isApplyingPlacement || !IsLoaded)
        {
            return;
        }

        _viewModel.Zone.Bounds.X = Left;
        _viewModel.Zone.Bounds.Y = Top;
        _viewModel.Zone.Bounds.Width = Width;
        if (!_viewModel.Zone.IsCollapsed)
        {
            _viewModel.Zone.Bounds.Height = Height;
            _expandedHeight = Height;
        }

        _manager.Save();
    }

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
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

        DockWindowLayer.SendBehindNormalWindows(this);
    }

    private void Refresh_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.Refresh();
    }

    private void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        OpenPath(_viewModel.SelectedFolderPath);
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
                DockWindowLayer.SendBehindNormalWindows(this);
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
            DockWindowLayer.SendBehindNormalWindows(this);
        }
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
            DockWindowLayer.SendBehindNormalWindows(this);
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
        _viewModel.Dispose();
        base.OnClosed(e);
    }

    private void ApplyPlacement()
    {
        _isApplyingPlacement = true;
        try
        {
            var bounds = _viewModel.Zone.Bounds;
            Width = Math.Max(MinWidth, bounds.Width);
            Height = Math.Max(MinHeight, bounds.Height);
            Left = Clamp(bounds.X, SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth - Width);
            Top = Clamp(bounds.Y, SystemParameters.VirtualScreenTop, SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight - MinHeight);
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
    }

    private void ApplyCollapsedState()
    {
        _isApplyingPlacement = true;
        try
        {
            if (_viewModel.Zone.IsCollapsed)
            {
                _expandedHeight = Math.Max(_expandedHeight, _viewModel.Zone.Bounds.Height);
                Height = CollapsedHeight;
                ItemsList.Visibility = Visibility.Collapsed;
                CollapseButton.Content = "\uE70E";
            }
            else
            {
                Height = Math.Max(_expandedHeight, 180);
                ItemsList.Visibility = Visibility.Visible;
                CollapseButton.Content = "\uE96E";
            }
        }
        finally
        {
            _isApplyingPlacement = false;
        }
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
