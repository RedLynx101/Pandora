using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Interop;

namespace OrbitDock.App;

public static class DockWindowLayer
{
    private static readonly IntPtr HwndTop = IntPtr.Zero;
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpFrameChanged = 0x0020;
    private const uint SwpNoOwnerZOrder = 0x0200;
    private const int GwlStyle = -16;
    private const int GwlExStyle = -20;
    private const int GwOwner = 4;
    private const long WsVisible = 0x10000000;
    private const long WsExToolWindow = 0x00000080;
    private const long WsExAppWindow = 0x00040000;
    private const long WsExNoActivate = 0x08000000;
    private const int SwShownoactivate = 4;
    private const int DwmwaCloaked = 14;

    public static void ApplyDesktopOverlayStyles(Window window)
    {
        try
        {
            var handle = new WindowInteropHelper(window).Handle;
            if (handle == IntPtr.Zero)
            {
                return;
            }

            var extendedStyle = GetWindowLongPtr(handle, GwlExStyle).ToInt64();
            var desiredExtendedStyle = extendedStyle | WsExToolWindow;
            desiredExtendedStyle &= ~WsExAppWindow;
            if (desiredExtendedStyle == extendedStyle)
            {
                return;
            }

            SetWindowLongPtr(handle, GwlExStyle, new IntPtr(desiredExtendedStyle));
            SetWindowPos(handle, IntPtr.Zero, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpNoZOrder | SwpNoActivate | SwpNoOwnerZOrder | SwpFrameChanged);
        }
        catch
        {
            // Task View exclusion is best-effort; docks remain usable if Windows rejects the style update.
        }
    }

    public static bool IsDesktopExposed()
    {
        return IsForegroundDesktopShell() || !HasVisibleUserApplicationWindow();
    }

    public static void ShowNoActivate(Window window)
    {
        try
        {
            var handle = new WindowInteropHelper(window).Handle;
            if (handle == IntPtr.Zero)
            {
                return;
            }

            ShowWindow(handle, SwShownoactivate);
        }
        catch
        {
            // Visibility recovery should not bring down the dock.
        }
    }

    public static void SendBehindNormalWindows(Window window)
    {
        try
        {
            window.Topmost = false;
            var handle = new WindowInteropHelper(window).Handle;
            if (handle == IntPtr.Zero)
            {
                return;
            }

            var anchor = FindBottomMostLayerAnchorWindow(handle);
            var insertAfter = anchor == IntPtr.Zero ? HwndTop : anchor;
            SetWindowPos(handle, insertAfter, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpNoActivate | SwpNoOwnerZOrder);
        }
        catch
        {
            // Z-order fallback failure should not affect dock usability.
        }
    }

    private static IntPtr FindBottomMostLayerAnchorWindow(IntPtr excludedHandle)
    {
        var anchor = IntPtr.Zero;
        EnumWindows((handle, _) =>
        {
            if (handle != excludedHandle && IsLayerAnchorWindow(handle))
            {
                anchor = handle;
            }

            return true;
        }, IntPtr.Zero);
        return anchor;
    }

    private static bool HasVisibleUserApplicationWindow()
    {
        var found = false;
        EnumWindows((handle, _) =>
        {
            if (IsUserApplicationWindow(handle))
            {
                found = true;
                return false;
            }

            return true;
        }, IntPtr.Zero);
        return found;
    }

    private static bool IsLayerAnchorWindow(IntPtr handle)
    {
        // Layering needs a broad anchor set so docks sit below helper-owned app surfaces too.
        if (!IsWindowVisible(handle) || IsIconic(handle))
        {
            return false;
        }

        var className = GetClassName(handle);
        if (IsShellClass(className))
        {
            return false;
        }

        var extendedStyle = GetWindowLongPtr(handle, GwlExStyle).ToInt64();
        if (IsOrbitDockOverlay(handle, extendedStyle) || IsWindowCloaked(handle))
        {
            return false;
        }

        return true;
    }

    private static bool IsUserApplicationWindow(IntPtr handle)
    {
        // Show Desktop recovery is intentionally stricter so shell/helper windows do not suppress dock restore.
        if (!IsWindowVisible(handle) || IsIconic(handle) || GetWindow(handle, GwOwner) != IntPtr.Zero)
        {
            return false;
        }

        var className = GetClassName(handle);
        if (IsShellClass(className))
        {
            return false;
        }

        var extendedStyle = GetWindowLongPtr(handle, GwlExStyle).ToInt64();
        var style = GetWindowLongPtr(handle, GwlStyle).ToInt64();
        if ((style & WsVisible) == 0 ||
            string.IsNullOrWhiteSpace(GetWindowTitle(handle)) ||
            (extendedStyle & WsExToolWindow) != 0 ||
            (extendedStyle & WsExNoActivate) != 0 ||
            IsOrbitDockOverlay(handle, extendedStyle) ||
            IsWindowCloaked(handle))
        {
            return false;
        }

        return true;
    }

    private static bool IsOrbitDockOverlay(IntPtr handle, long extendedStyle)
    {
        return (extendedStyle & WsExToolWindow) != 0 &&
               (string.Equals(GetWindowTitle(handle), "Pandora Dock", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(GetWindowTitle(handle), "Pandora Desktop Pin", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(GetWindowTitle(handle), "OrbitDock Zone", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(GetWindowTitle(handle), "OrbitDock Desktop Pin", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsForegroundDesktopShell()
    {
        var foreground = GetForegroundWindow();
        if (foreground == IntPtr.Zero)
        {
            return false;
        }

        return IsShellClass(GetClassName(foreground)) ||
               FindWindowEx(foreground, IntPtr.Zero, "SHELLDLL_DefView", null) != IntPtr.Zero;
    }

    private static bool IsShellClass(string className)
    {
        return string.Equals(className, "Progman", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(className, "WorkerW", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(className, "SHELLDLL_DefView", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(className, "Shell_TrayWnd", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(className, "Shell_SecondaryTrayWnd", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsWindowCloaked(IntPtr handle)
    {
        try
        {
            var result = DwmGetWindowAttribute(handle, DwmwaCloaked, out var cloaked, Marshal.SizeOf<int>());
            return result == 0 && cloaked != 0;
        }
        catch
        {
            return false;
        }
    }

    private static string GetClassName(IntPtr handle)
    {
        var builder = new StringBuilder(256);
        GetClassName(handle, builder, builder.Capacity);
        return builder.ToString();
    }

    private static string GetWindowTitle(IntPtr handle)
    {
        var builder = new StringBuilder(256);
        GetWindowText(handle, builder, builder.Capacity);
        return builder.ToString();
    }

    private delegate bool EnumWindowsProc(IntPtr windowHandle, IntPtr parameter);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc enumFunc, IntPtr parameter);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr windowHandle);

    [DllImport("user32.dll")]
    private static extern bool IsIconic(IntPtr windowHandle);

    [DllImport("user32.dll")]
    private static extern IntPtr GetWindow(IntPtr windowHandle, int command);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int GetClassName(IntPtr windowHandle, StringBuilder className, int maxCount);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int GetWindowText(IntPtr windowHandle, StringBuilder text, int maxCount);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr FindWindowEx(IntPtr parentHandle, IntPtr childAfter, string className, string? windowName);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr windowHandle, int command);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(
        IntPtr windowHandle,
        IntPtr insertAfter,
        int x,
        int y,
        int cx,
        int cy,
        uint flags);

    private static IntPtr GetWindowLongPtr(IntPtr windowHandle, int index)
    {
        return IntPtr.Size == 8
            ? GetWindowLongPtr64(windowHandle, index)
            : new IntPtr(GetWindowLong32(windowHandle, index));
    }

    private static IntPtr SetWindowLongPtr(IntPtr windowHandle, int index, IntPtr value)
    {
        return IntPtr.Size == 8
            ? SetWindowLongPtr64(windowHandle, index, value)
            : new IntPtr(SetWindowLong32(windowHandle, index, value.ToInt32()));
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtr", SetLastError = true)]
    private static extern IntPtr GetWindowLongPtr64(IntPtr windowHandle, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtr", SetLastError = true)]
    private static extern IntPtr SetWindowLongPtr64(IntPtr windowHandle, int index, IntPtr value);

    [DllImport("user32.dll", EntryPoint = "GetWindowLong", SetLastError = true)]
    private static extern int GetWindowLong32(IntPtr windowHandle, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLong", SetLastError = true)]
    private static extern int SetWindowLong32(IntPtr windowHandle, int index, int value);

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmGetWindowAttribute(IntPtr windowHandle, int attribute, out int value, int size);
}
