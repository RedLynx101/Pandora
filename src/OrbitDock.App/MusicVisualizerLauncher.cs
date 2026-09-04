using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;

namespace OrbitDock.App;

internal static class MusicVisualizerLauncher
{
    private const int MaxParentSearchDepth = 10;

    public static bool TryLaunch(out string error)
    {
        var scriptPath = FindLaunchScript();
        if (scriptPath is null)
        {
            error = "Silk Current launcher was not found under tools\\SilkCurrentVisualizer.";
            return false;
        }

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                UseShellExecute = false,
                CreateNoWindow = true
            };
            startInfo.ArgumentList.Add("-NoProfile");
            startInfo.ArgumentList.Add("-ExecutionPolicy");
            startInfo.ArgumentList.Add("Bypass");
            startInfo.ArgumentList.Add("-File");
            startInfo.ArgumentList.Add(scriptPath);
            Process.Start(startInfo);
            error = string.Empty;
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or Win32Exception)
        {
            error = ex.Message;
            return false;
        }
    }

    private static string? FindLaunchScript()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        for (var depth = 0; directory is not null && depth < MaxParentSearchDepth; depth++)
        {
            var candidate = Path.Combine(directory.FullName, "tools", "SilkCurrentVisualizer", "start-visualizer.ps1");
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        return null;
    }
}
