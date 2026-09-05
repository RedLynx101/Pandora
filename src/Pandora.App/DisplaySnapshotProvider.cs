using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using Pandora.Core;
using Forms = System.Windows.Forms;

namespace Pandora.App;

public static class DisplaySnapshotProvider
{
    private const int MonitorDefaultToNearest = 2;

    public static IReadOnlyList<DisplayDescriptor> GetDisplays(Visual? visual = null)
    {
        var physicalDisplays = GetPhysicalDisplays();
        return ScreenCoordinatesNeedDipConversion(physicalDisplays)
            ? physicalDisplays.Select(ConvertDisplayToDip).ToArray()
            : physicalDisplays;
    }

    public static IReadOnlyList<DisplayDescriptor> GetPhysicalDisplays()
    {
        var displays = new List<DisplayDescriptor>();
        foreach (var screen in Forms.Screen.AllScreens)
        {
            displays.Add(new DisplayDescriptor
            {
                DeviceName = screen.DeviceName,
                IsPrimary = screen.Primary,
                BoundsX = screen.Bounds.X,
                BoundsY = screen.Bounds.Y,
                BoundsWidth = screen.Bounds.Width,
                BoundsHeight = screen.Bounds.Height,
                WorkAreaX = screen.WorkingArea.X,
                WorkAreaY = screen.WorkingArea.Y,
                WorkAreaWidth = screen.WorkingArea.Width,
                WorkAreaHeight = screen.WorkingArea.Height
            });
        }

        return displays;
    }

    public static Rect GetWorkingAreaForBounds(double x, double y, double width, double height, Visual? visual = null)
    {
        var displays = GetDisplays(visual);
        if (displays.Count == 0)
        {
            return SystemParameters.WorkArea;
        }

        var bounds = new ZoneBounds
        {
            X = x,
            Y = y,
            Width = width,
            Height = height
        };
        var display = displays
            .OrderByDescending(display => IntersectionArea(bounds, display))
            .ThenBy(display => DistanceFromDisplayCenter(bounds, display))
            .First();

        return new Rect(display.WorkAreaX, display.WorkAreaY, display.WorkAreaWidth, display.WorkAreaHeight);
    }

    private static bool ScreenCoordinatesNeedDipConversion(IReadOnlyList<DisplayDescriptor> physicalDisplays)
    {
        var primary = physicalDisplays.FirstOrDefault(display => display.IsPrimary);
        if (primary is null)
        {
            return false;
        }

        return Math.Abs(primary.BoundsWidth - SystemParameters.PrimaryScreenWidth) > 1.5 ||
               Math.Abs(primary.BoundsHeight - SystemParameters.PrimaryScreenHeight) > 1.5;
    }

    private static DisplayDescriptor ConvertDisplayToDip(DisplayDescriptor display)
    {
        var (scaleX, scaleY) = GetDpiScale(display);
        return new DisplayDescriptor
        {
            DeviceName = display.DeviceName,
            IsPrimary = display.IsPrimary,
            BoundsX = display.BoundsX / scaleX,
            BoundsY = display.BoundsY / scaleY,
            BoundsWidth = display.BoundsWidth / scaleX,
            BoundsHeight = display.BoundsHeight / scaleY,
            WorkAreaX = display.WorkAreaX / scaleX,
            WorkAreaY = display.WorkAreaY / scaleY,
            WorkAreaWidth = display.WorkAreaWidth / scaleX,
            WorkAreaHeight = display.WorkAreaHeight / scaleY
        };
    }

    private static (double ScaleX, double ScaleY) GetDpiScale(DisplayDescriptor display)
    {
        try
        {
            var point = new NativePoint
            {
                X = (int)Math.Round(display.BoundsX + Math.Max(1, display.BoundsWidth) / 2),
                Y = (int)Math.Round(display.BoundsY + Math.Max(1, display.BoundsHeight) / 2)
            };
            var monitor = MonitorFromPoint(point, MonitorDefaultToNearest);
            if (monitor != IntPtr.Zero &&
                GetDpiForMonitor(monitor, MonitorDpiType.EffectiveDpi, out var dpiX, out var dpiY) == 0 &&
                dpiX > 0 &&
                dpiY > 0)
            {
                return (dpiX / 96d, dpiY / 96d);
            }
        }
        catch
        {
            // Fall back to primary-screen scaling below on systems without shcore DPI APIs.
        }

        var primary = Forms.Screen.PrimaryScreen;
        if (primary is not null &&
            SystemParameters.PrimaryScreenWidth > 1 &&
            SystemParameters.PrimaryScreenHeight > 1)
        {
            return (
                Math.Max(1, primary.Bounds.Width / SystemParameters.PrimaryScreenWidth),
                Math.Max(1, primary.Bounds.Height / SystemParameters.PrimaryScreenHeight));
        }

        return (1, 1);
    }

    private static double IntersectionArea(ZoneBounds bounds, DisplayDescriptor display)
    {
        var left = Math.Max(bounds.X, display.WorkAreaX);
        var right = Math.Min(bounds.X + bounds.Width, display.WorkAreaX + display.WorkAreaWidth);
        var top = Math.Max(bounds.Y, display.WorkAreaY);
        var bottom = Math.Min(bounds.Y + bounds.Height, display.WorkAreaY + display.WorkAreaHeight);
        return Math.Max(0, right - left) * Math.Max(0, bottom - top);
    }

    private static double DistanceFromDisplayCenter(ZoneBounds bounds, DisplayDescriptor display)
    {
        var boundsCenterX = bounds.X + bounds.Width / 2;
        var boundsCenterY = bounds.Y + bounds.Height / 2;
        var displayCenterX = display.WorkAreaX + display.WorkAreaWidth / 2;
        var displayCenterY = display.WorkAreaY + display.WorkAreaHeight / 2;
        var dx = boundsCenterX - displayCenterX;
        var dy = boundsCenterY - displayCenterY;
        return dx * dx + dy * dy;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromPoint(NativePoint pt, int dwFlags);

    [DllImport("shcore.dll")]
    private static extern int GetDpiForMonitor(IntPtr hmonitor, MonitorDpiType dpiType, out uint dpiX, out uint dpiY);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    private enum MonitorDpiType
    {
        EffectiveDpi = 0
    }
}
