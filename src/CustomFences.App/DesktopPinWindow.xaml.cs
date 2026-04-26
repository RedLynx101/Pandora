using System;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Input;
using CustomFences.Core;

namespace CustomFences.App;

public partial class DesktopPinWindow : Window
{
    private readonly DesktopZoneManager _manager;
    private readonly DesktopPinDefinition _pin;
    private bool _isApplyingPlacement;
    private bool _isDesktopAttached;

    public DesktopPinWindow(DesktopPinDefinition pin, DesktopZoneManager manager)
    {
        InitializeComponent();
        _pin = pin;
        _manager = manager;
        PinIcon.Source = FileIconService.GetIcon(pin.Path);
        PinIcon.Width = Math.Clamp(pin.IconSize, 24, 128);
        PinIcon.Height = Math.Clamp(pin.IconSize, 24, 128);
        PinName.Text = string.IsNullOrWhiteSpace(pin.DisplayName)
            ? DesktopItemCatalog.CleanDisplayName(Path.GetFileName(pin.Path))
            : pin.DisplayName;
        Width = Math.Max(82, pin.IconSize + 42);
        Height = Math.Max(82, pin.IconSize + 42);
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        ApplyPlacement();
        Dispatcher.BeginInvoke(() => DockWindowLayer.SendBehindNormalWindows(this));
    }

    private void Window_SourceInitialized(object sender, EventArgs e)
    {
        DockWindowLayer.ApplyDesktopOverlayStyles(this);
        TryAttachToDesktop();

        DockWindowLayer.SendBehindNormalWindows(this);
    }

    private void Window_Deactivated(object? sender, EventArgs e)
    {
        DockWindowLayer.SendBehindNormalWindows(this);
    }

    public void MaintainDesktopOverlay(bool restoreHiddenWindow)
    {
        if (!IsLoaded)
        {
            return;
        }

        DockWindowLayer.ApplyDesktopOverlayStyles(this);
        TryAttachToDesktop();
        if (WindowState == WindowState.Minimized)
        {
            if (!restoreHiddenWindow)
            {
                return;
            }

            WindowState = WindowState.Normal;
        }

        if (!IsVisible)
        {
            if (!restoreHiddenWindow)
            {
                return;
            }

            Show();
        }

        if (restoreHiddenWindow)
        {
            DockWindowLayer.ShowNoActivate(this);
        }

        DockWindowLayer.SendBehindNormalWindows(this);
    }

    private void Window_PlacementChanged(object sender, EventArgs e)
    {
        if (_isApplyingPlacement || !IsLoaded)
        {
            return;
        }

        _pin.X = Left;
        _pin.Y = Top;
        _manager.Save();
    }

    private void Frame_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            OpenPath(_pin.Path);
            DockWindowLayer.SendBehindNormalWindows(this);
            return;
        }

        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            var data = new DataObject();
            data.SetData(ZoneWindow.OrbitDockItemFormat, _pin.Path);
            data.SetData(ZoneWindow.OrbitDockDesktopPinFormat, _pin.Id);
            data.SetData(DataFormats.FileDrop, new[] { _pin.Path });
            DragDrop.DoDragDrop(this, data, DragDropEffects.Copy | DragDropEffects.Move);
            return;
        }

        try
        {
            DragMove();
        }
        catch
        {
            // Windows can cancel a drag if the pointer state changes.
        }

        DockWindowLayer.SendBehindNormalWindows(this);
    }

    private void Open_Click(object sender, RoutedEventArgs e)
    {
        OpenPath(_pin.Path);
        DockWindowLayer.SendBehindNormalWindows(this);
    }

    private void SendToDock_Click(object sender, RoutedEventArgs e)
    {
        _manager.SendDesktopPinToDock(_pin);
    }

    private void Reveal_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{_pin.Path}\"") { UseShellExecute = true });
        }
        catch
        {
            OpenPath(Path.GetDirectoryName(_pin.Path) ?? _pin.Path);
        }
    }

    private void Remove_Click(object sender, RoutedEventArgs e)
    {
        _manager.RemoveDesktopPin(_pin);
    }

    private void ApplyPlacement()
    {
        _isApplyingPlacement = true;
        try
        {
            Left = Clamp(_pin.X, SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth - Width);
            Top = Clamp(_pin.Y, SystemParameters.VirtualScreenTop, SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight - Height);
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
            // Missing targets should not disrupt the desktop overlay.
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
