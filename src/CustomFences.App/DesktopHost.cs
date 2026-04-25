using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace CustomFences.App;

public static class DesktopHost
{
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

            SetParent(hwnd, host);
            return true;
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
    private static extern IntPtr SendMessageTimeout(
        IntPtr windowHandle,
        uint message,
        IntPtr wParam,
        IntPtr lParam,
        uint flags,
        uint timeout,
        out IntPtr result);
}
