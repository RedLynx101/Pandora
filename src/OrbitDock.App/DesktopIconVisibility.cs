using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace OrbitDock.App;

public static class DesktopIconVisibility
{
    private const int SwHide = 0;
    private const int SwShow = 5;

    public static bool TrySetVisible(bool visible)
    {
        var handles = FindDesktopListViews();
        var changed = false;
        foreach (var handle in handles)
        {
            changed |= ShowWindow(handle, visible ? SwShow : SwHide);
        }

        return changed;
    }

    private static IReadOnlyList<IntPtr> FindDesktopListViews()
    {
        var results = new List<IntPtr>();
        AddListViewFromParent(FindWindow("Progman", null), results);

        EnumWindows((topHandle, _) =>
        {
            AddListViewFromParent(topHandle, results);
            return true;
        }, IntPtr.Zero);

        return results;
    }

    private static void AddListViewFromParent(IntPtr parent, List<IntPtr> results)
    {
        if (parent == IntPtr.Zero)
        {
            return;
        }

        var shellView = FindWindowEx(parent, IntPtr.Zero, "SHELLDLL_DefView", null);
        if (shellView == IntPtr.Zero)
        {
            return;
        }

        var listView = FindWindowEx(shellView, IntPtr.Zero, "SysListView32", "FolderView");
        if (listView != IntPtr.Zero && !results.Contains(listView))
        {
            results.Add(listView);
        }
    }

    private delegate bool EnumWindowsProc(IntPtr topHandle, IntPtr parameter);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr FindWindow(string className, string? windowName);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr FindWindowEx(IntPtr parentHandle, IntPtr childAfter, string className, string? windowName);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc enumFunc, IntPtr parameter);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr windowHandle, int command);
}
