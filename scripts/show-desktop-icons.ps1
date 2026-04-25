$ErrorActionPreference = "Stop"

$code = @"
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

public static class DesktopIcons {
    private const int SW_SHOW = 5;

    public static void Show() {
        foreach (var handle in FindDesktopListViews()) {
            ShowWindow(handle, SW_SHOW);
        }
    }

    private static List<IntPtr> FindDesktopListViews() {
        var results = new List<IntPtr>();
        AddListViewFromParent(FindWindow("Progman", null), results);
        EnumWindows((topHandle, parameter) => {
            AddListViewFromParent(topHandle, results);
            return true;
        }, IntPtr.Zero);
        return results;
    }

    private static void AddListViewFromParent(IntPtr parent, List<IntPtr> results) {
        if (parent == IntPtr.Zero) return;
        var shellView = FindWindowEx(parent, IntPtr.Zero, "SHELLDLL_DefView", null);
        if (shellView == IntPtr.Zero) return;
        var listView = FindWindowEx(shellView, IntPtr.Zero, "SysListView32", "FolderView");
        if (listView != IntPtr.Zero && !results.Contains(listView)) results.Add(listView);
    }

    private delegate bool EnumWindowsProc(IntPtr topHandle, IntPtr parameter);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr FindWindow(string className, string windowName);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr FindWindowEx(IntPtr parentHandle, IntPtr childAfter, string className, string windowName);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc enumFunc, IntPtr parameter);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr windowHandle, int command);
}
"@

Add-Type $code -ErrorAction SilentlyContinue
[DesktopIcons]::Show()
Write-Host "Desktop icons shown."
