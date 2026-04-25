using System;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using CustomFences.Core;

namespace CustomFences.App;

public partial class ZoneWindow : Window
{
    private const double CollapsedHeight = 54;
    private readonly DesktopZoneManager _manager;
    private readonly ZoneViewModel _viewModel;
    private bool _isApplyingPlacement;
    private double _expandedHeight;

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
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop)
            ? DragDropEffects.Copy
            : DragDropEffects.None;
        e.Handled = true;
    }

    private void Window_Drop(object sender, System.Windows.DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            return;
        }

        var files = (string[])e.Data.GetData(DataFormats.FileDrop);
        _viewModel.AddDroppedFiles(files, _manager.Workspace.Settings.DefaultDropAction);
        _manager.Save();
    }

    private void ItemsList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (ItemsList.SelectedItem is FileItemViewModel item)
        {
            OpenPath(item.Path);
            DockWindowLayer.SendBehindNormalWindows(this);
        }
    }

    private void OpenItem_Click(object sender, RoutedEventArgs e)
    {
        if (ItemsList.SelectedItem is FileItemViewModel item)
        {
            OpenPath(item.Path);
            DockWindowLayer.SendBehindNormalWindows(this);
        }
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
}
