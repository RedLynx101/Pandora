using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using OrbitDock.Core;

namespace OrbitDock.App;

public static class ManagedShortcutRepairService
{
    private const string AiVirtualFolderSuffix = @"OrbitDock\VirtualTabs\AI";

    private static readonly StoreAppShortcut[] StoreAiShortcuts =
    [
        new("Codex", "OpenAI.Codex", "OpenAI.Codex_2p2nqsd0c76g0!App", ["app\\Codex.exe"]),
        new("Claude", "Claude", "Claude_pzs8sxrjxfjjc!Claude", ["app\\claude.exe"]),
        new("ChatGPT", "OpenAI.ChatGPT-Desktop", "OpenAI.ChatGPT-Desktop_2p2nqsd0c76g0!ChatGPT", ["app\\ChatGPT.exe"]),
        new("Manus", "ManusAI.Manus", "ManusAI.Manus_vajzd2mq3s8wj!ManusApp", ["assets\\icon-win.ico", "assets\\icon.ico", "Manus.exe"])
    ];

    public static void RepairWorkspaceVirtualShortcuts(Workspace workspace)
    {
        foreach (var folder in GetManagedAiFolders(workspace))
        {
            RepairAiFolder(folder);
        }
    }

    private static void RepairAiFolder(string folder)
    {
        if (!Directory.Exists(folder))
        {
            return;
        }

        var shellType = Type.GetTypeFromProgID("WScript.Shell");
        if (shellType is null)
        {
            return;
        }

        object? shell = null;
        try
        {
            shell = Activator.CreateInstance(shellType);
            if (shell is null)
            {
                return;
            }

            foreach (var shortcut in StoreAiShortcuts)
            {
                RepairStoreShortcut(shellType, shell, folder, shortcut);
            }

            foreach (var shortcutPath in Directory.EnumerateFiles(folder, "*.lnk"))
            {
                EnsureShortcutHasIcon(shellType, shell, shortcutPath);
            }
        }
        catch
        {
            // Shortcut repair is a convenience. Broken shortcuts should not block dock startup.
        }
        finally
        {
            ReleaseComObject(shell);
        }
    }

    private static string[] GetManagedAiFolders(Workspace workspace)
    {
        return workspace.Zones
            .SelectMany(zone => zone.Tabs)
            .Where(tab => tab.Source == ZoneTabSource.Folder)
            .Select(tab => PathExpander.Expand(Environment.ExpandEnvironmentVariables(tab.Path)))
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Where(path => path.EndsWith(AiVirtualFolderSuffix, StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static void RepairStoreShortcut(Type shellType, object shell, string folder, StoreAppShortcut shortcut)
    {
        var installLocation = ResolveAppxInstallLocation(shortcut.PackageName);
        if (string.IsNullOrWhiteSpace(installLocation))
        {
            return;
        }

        var iconPath = shortcut.IconRelativePaths
            .Select(relativePath => Path.Combine(installLocation, relativePath))
            .FirstOrDefault(File.Exists);
        if (string.IsNullOrWhiteSpace(iconPath))
        {
            return;
        }

        var shortcutPath = Path.Combine(folder, shortcut.Name + ".lnk");
        var link = CreateShortcut(shellType, shell, shortcutPath);
        if (link is null)
        {
            return;
        }

        try
        {
            SetProperty(link, "TargetPath", "explorer.exe");
            SetProperty(link, "Arguments", "shell:AppsFolder\\" + shortcut.AppUserModelId);
            SetProperty(link, "WorkingDirectory", Environment.GetFolderPath(Environment.SpecialFolder.Windows));
            SetProperty(link, "IconLocation", iconPath + ",0");
            SetProperty(link, "Description", "Launch " + shortcut.Name);
            SaveShortcut(link);
        }
        finally
        {
            ReleaseComObject(link);
        }
    }

    private static void EnsureShortcutHasIcon(Type shellType, object shell, string shortcutPath)
    {
        var link = CreateShortcut(shellType, shell, shortcutPath);
        if (link is null)
        {
            return;
        }

        try
        {
            var targetPath = GetProperty(link, "TargetPath") as string;
            if (string.IsNullOrWhiteSpace(targetPath) || !File.Exists(targetPath))
            {
                return;
            }

            var iconLocation = GetProperty(link, "IconLocation") as string;
            var iconPath = ExtractIconPath(iconLocation);
            if (!string.IsNullOrWhiteSpace(iconPath) && File.Exists(iconPath))
            {
                return;
            }

            SetProperty(link, "IconLocation", targetPath + ",0");
            SaveShortcut(link);
        }
        finally
        {
            ReleaseComObject(link);
        }
    }

    private static string? ResolveAppxInstallLocation(string packageName)
    {
        try
        {
            using var process = new Process();
            process.StartInfo = new ProcessStartInfo
            {
                FileName = ResolvePowerShellPath(),
                Arguments = "-NoProfile -ExecutionPolicy Bypass -Command " +
                            QuoteForPowerShell($"(Get-AppxPackage -Name '{packageName}' | Sort-Object Version -Descending | Select-Object -First 1).InstallLocation"),
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            process.Start();
            var output = process.StandardOutput.ReadToEnd().Trim();
            if (!process.WaitForExit(5000))
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch
                {
                    // Best effort only.
                }

                return null;
            }

            return Directory.Exists(output) ? output : null;
        }
        catch
        {
            return null;
        }
    }

    private static string ResolvePowerShellPath()
    {
        var systemPowerShell = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            "System32",
            "WindowsPowerShell",
            "v1.0",
            "powershell.exe");
        return File.Exists(systemPowerShell) ? systemPowerShell : "powershell.exe";
    }

    private static string QuoteForPowerShell(string command)
    {
        return "\"" + command.Replace("\"", "`\"", StringComparison.Ordinal) + "\"";
    }

    private static object? CreateShortcut(Type shellType, object shell, string shortcutPath)
    {
        return shellType.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, [shortcutPath]);
    }

    private static object? GetProperty(object target, string propertyName)
    {
        return target.GetType().InvokeMember(propertyName, BindingFlags.GetProperty, null, target, []);
    }

    private static void SetProperty(object target, string propertyName, object value)
    {
        target.GetType().InvokeMember(propertyName, BindingFlags.SetProperty, null, target, [value]);
    }

    private static void SaveShortcut(object shortcut)
    {
        shortcut.GetType().InvokeMember("Save", BindingFlags.InvokeMethod, null, shortcut, []);
    }

    private static string ExtractIconPath(string? iconLocation)
    {
        if (string.IsNullOrWhiteSpace(iconLocation))
        {
            return string.Empty;
        }

        var value = iconLocation.Trim();
        var commaIndex = value.LastIndexOf(',');
        return commaIndex > 0 ? value[..commaIndex] : value;
    }

    private static void ReleaseComObject(object? value)
    {
        if (value is not null && Marshal.IsComObject(value))
        {
            Marshal.FinalReleaseComObject(value);
        }
    }

    private sealed record StoreAppShortcut(
        string Name,
        string PackageName,
        string AppUserModelId,
        string[] IconRelativePaths);
}
