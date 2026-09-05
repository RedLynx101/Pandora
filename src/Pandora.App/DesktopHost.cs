using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace Pandora.App;

public static class DesktopHost
{
    private const int GwlStyle = -16;
    private const long WsChild = 0x40000000;
    private const long WsPopup = 0x80000000;
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpFrameChanged = 0x0020;

    public static bool TryAttach(Window window)
    {
        try
        {
            var helper = new WindowInteropHelper(window);
            var hwnd = helper.Handle;
            if (hwnd == IntPtr.Zero)
            {
                return false;
            }

            var host = FindDesktopHostWindow();
            if (host == IntPtr.Zero)
            {
                return false;
            }

            var oldStyle = GetWindowLongPtr(hwnd, GwlStyle).ToInt64();
            var childStyle = (oldStyle | WsChild) & ~WsPopup;
            SetWindowLongPtr(hwnd, GwlStyle, new IntPtr(childStyle));
            SetParent(hwnd, host);
            SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpNoActivate | SwpFrameChanged);

            if (GetParent(hwnd) == host)
            {
                return true;
            }

            SetWindowLongPtr(hwnd, GwlStyle, new IntPtr(oldStyle));
            SetParent(hwnd, IntPtr.Zero);
            SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpNoActivate | SwpFrameChanged);
            return false;
        }
        catch
        {
            return false;
        }
    }

    private static IntPtr FindDesktopHostWindow()
    {
        var progman = FindWindow("Progman", null);
        if (progman != IntPtr.Zero)
        {
            SendMessageTimeout(progman, 0x052C, IntPtr.Zero, IntPtr.Zero, 0, 1000, out _);
        }

        var worker = IntPtr.Zero;
        EnumWindows((topHandle, _) =>
        {
            var shellView = FindWindowEx(topHandle, IntPtr.Zero, "SHELLDLL_DefView", null);
            if (shellView != IntPtr.Zero)
            {
                worker = FindWindowEx(IntPtr.Zero, topHandle, "WorkerW", null);
            }

            return true;
        }, IntPtr.Zero);

        return worker != IntPtr.Zero ? worker : progman;
    }

    private delegate bool EnumWindowsProc(IntPtr topHandle, IntPtr parameter);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr FindWindow(string className, string? windowName);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr FindWindowEx(IntPtr parentHandle, IntPtr childAfter, string className, string? windowName);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc enumFunc, IntPtr parameter);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetParent(IntPtr childHandle, IntPtr newParentHandle);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr GetParent(IntPtr windowHandle);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(
        IntPtr windowHandle,
        IntPtr insertAfter,
        int x,
        int y,
        int cx,
        int cy,
        uint flags);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SendMessageTimeout(
        IntPtr windowHandle,
        uint message,
        IntPtr wParam,
        IntPtr lParam,
        uint flags,
        uint timeout,
        out IntPtr result);

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
}
