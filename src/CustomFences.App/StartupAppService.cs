using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace CustomFences.App;

public static class StartupAppService
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string StartupApprovedRunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";
    private const string StartupApprovedFolderKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\StartupFolder";
    private const string ValueName = "OrbitDock";
    private const string ShortcutName = "OrbitDock.lnk";
    private static readonly byte[] EnabledStartupApprovedValue = [0x02, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00];

    public static bool IsEnabled()
    {
        if (File.Exists(GetStartupShortcutPath()))
        {
            return true;
        }

        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
            return key?.GetValue(ValueName) is string command && !string.IsNullOrWhiteSpace(command);
        }
        catch
        {
            return false;
        }
    }

    public static void SetEnabled(bool enabled)
    {
        if (!enabled)
        {
            DeleteStartupShortcut();
            DeleteRegistryValue(RunKeyPath, ValueName);
            DeleteRegistryValue(StartupApprovedRunKeyPath, ValueName);
            DeleteRegistryValue(StartupApprovedFolderKeyPath, ShortcutName);
            return;
        }

        DeleteRegistryValue(RunKeyPath, ValueName);
        CreateStartupShortcut();
        SetStartupApprovedEnabled();
    }

    public static string BuildLaunchCommand()
    {
        var launch = ResolveLaunchInfo();
        return string.IsNullOrWhiteSpace(launch.Arguments)
            ? Quote(launch.TargetPath)
            : $"{Quote(launch.TargetPath)} {launch.Arguments}";
    }

    public static string GetStartupShortcutPath()
    {
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Startup), ShortcutName);
    }

    private static void CreateStartupShortcut()
    {
        var launch = ResolveLaunchInfo();
        var shortcutPath = GetStartupShortcutPath();
        Directory.CreateDirectory(Path.GetDirectoryName(shortcutPath)!);

        var shellType = Type.GetTypeFromProgID("WScript.Shell")
            ?? throw new InvalidOperationException("Windows Script Host is unavailable, so OrbitDock could not create a Startup shortcut.");
        object? shell = null;
        object? shortcut = null;
        try
        {
            shell = Activator.CreateInstance(shellType)
                ?? throw new InvalidOperationException("Could not create the Windows shortcut helper.");
            shortcut = shellType.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, [shortcutPath])
                ?? throw new InvalidOperationException("Could not create the Startup shortcut.");
            var shortcutType = shortcut.GetType();
            shortcutType.InvokeMember("TargetPath", BindingFlags.SetProperty, null, shortcut, [launch.TargetPath]);
            shortcutType.InvokeMember("Arguments", BindingFlags.SetProperty, null, shortcut, [launch.Arguments]);
            shortcutType.InvokeMember("WorkingDirectory", BindingFlags.SetProperty, null, shortcut, [launch.WorkingDirectory]);
            shortcutType.InvokeMember("Description", BindingFlags.SetProperty, null, shortcut, ["OrbitDock desktop organizer"]);
            if (!string.IsNullOrWhiteSpace(launch.IconLocation))
            {
                shortcutType.InvokeMember("IconLocation", BindingFlags.SetProperty, null, shortcut, [launch.IconLocation]);
            }

            shortcutType.InvokeMember("Save", BindingFlags.InvokeMethod, null, shortcut, []);
        }
        finally
        {
            ReleaseComObject(shortcut);
            ReleaseComObject(shell);
        }
    }

    private static LaunchInfo ResolveLaunchInfo()
    {
        var processPath = Environment.ProcessPath;
        if (!string.IsNullOrWhiteSpace(processPath) &&
            processPath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(Path.GetFileName(processPath), "dotnet.exe", StringComparison.OrdinalIgnoreCase))
        {
            return new LaunchInfo(
                processPath,
                string.Empty,
                Path.GetDirectoryName(processPath) ?? AppContext.BaseDirectory,
                ResolveIconLocation(processPath));
        }

        var assemblyPath = Assembly.GetEntryAssembly()?.Location;
        if (!string.IsNullOrWhiteSpace(processPath) && !string.IsNullOrWhiteSpace(assemblyPath))
        {
            return new LaunchInfo(
                processPath,
                Quote(assemblyPath),
                Path.GetDirectoryName(assemblyPath) ?? AppContext.BaseDirectory,
                ResolveIconLocation(assemblyPath));
        }

        throw new InvalidOperationException("Could not resolve the OrbitDock launch command.");
    }

    private static string ResolveIconLocation(string launchPath)
    {
        var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "Brand", "OrbitDock.ico");
        return File.Exists(iconPath) ? iconPath : launchPath;
    }

    private static void DeleteStartupShortcut()
    {
        var shortcutPath = GetStartupShortcutPath();
        if (File.Exists(shortcutPath))
        {
            File.Delete(shortcutPath);
        }
    }

    private static void SetStartupApprovedEnabled()
    {
        using var key = Registry.CurrentUser.CreateSubKey(StartupApprovedFolderKeyPath, writable: true);
        key?.SetValue(ShortcutName, EnabledStartupApprovedValue, RegistryValueKind.Binary);
    }

    private static void DeleteRegistryValue(string keyPath, string valueName)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(keyPath, writable: true);
            key?.DeleteValue(valueName, throwOnMissingValue: false);
        }
        catch
        {
            // Best-effort cleanup only.
        }
    }

    private static void ReleaseComObject(object? value)
    {
        if (value is not null && Marshal.IsComObject(value))
        {
            Marshal.FinalReleaseComObject(value);
        }
    }

    private static string Quote(string value)
    {
        return $"\"{value.Replace("\"", "\\\"", StringComparison.Ordinal)}\"";
    }

    private sealed record LaunchInfo(string TargetPath, string Arguments, string WorkingDirectory, string IconLocation);
}
