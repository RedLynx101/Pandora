using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace Pandora.App;

public static class StartupAppService
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string StartupApprovedRunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";
    private const string StartupApprovedFolderKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\StartupFolder";
    private const string ShortcutName = "Pandora.lnk";
    private static readonly string[] RegistrationNames = ["Pandora"];
    private static readonly byte[] EnabledStartupApprovedValue = [0x02, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00];

    /// <summary>
    /// A disabled registration still exists and must not be repaired
    /// into an enabled one merely because Pandora starts. Inspection failures
    /// conservatively count as registered so automatic repair remains read-only.
    /// </summary>
    public static bool IsRegistered()
    {
        try
        {
            using var scheduled = StartupScheduledTask.Open();
            if (scheduled is not null) return true;
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
            foreach (var name in RegistrationNames)
            {
                if (File.Exists(GetStartupShortcutPath(name)) || key?.GetValue(name) is not null)
                {
                    return true;
                }
            }
            return false;
        }
        catch
        {
            return true;
        }
    }

    /// <summary>
    /// Reports effective registration state, including Task Manager's approval
    /// flags. This method never edits the registration or its approval values.
    /// </summary>
    public static bool IsEnabled()
    {
        try
        {
            using var scheduled = StartupScheduledTask.Open();
            if (scheduled is not null) return scheduled.Enabled;
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
            foreach (var name in RegistrationNames)
            {
                if (File.Exists(GetStartupShortcutPath(name)) &&
                    ReadStartupApproval(StartupApprovedFolderKeyPath, name + ".lnk"))
                {
                    return true;
                }
                if (key?.GetValue(name) is string command && !string.IsNullOrWhiteSpace(command) &&
                    ReadStartupApproval(StartupApprovedRunKeyPath, name))
                {
                    return true;
                }
            }
            return false;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// An explicit preference change may remove or enable registrations. Do not
    /// call this on every settings save or for a disabled existing registration.
    /// Automatic repair may call it only after IsRegistered returned false.
    /// </summary>
    public static void SetEnabled(bool enabled, string? iconStyle = null)
    {
        // Complete read-only ownership checks before any mutation. A matching
        // display name alone never authorizes deleting another app's shortcut.
        var launch = ResolveLaunchInfo(iconStyle);
        var registrations = ReadOwnedRegistrations(launch);
        using var scheduled = StartupScheduledTask.Open();
        if (scheduled is not null) scheduled.Enabled = enabled;
        foreach (var registration in registrations)
        {
            if (registration.HasRunEntry)
            {
                DeleteRegistryValue(RunKeyPath, registration.Name);
                DeleteRegistryValue(StartupApprovedRunKeyPath, registration.Name);
            }
            if (registration.HasShortcut && (!enabled || scheduled is not null))
            {
                DeleteStartupShortcut(registration.Name);
                DeleteRegistryValue(StartupApprovedFolderKeyPath, registration.Name + ".lnk");
            }
        }
        if (enabled && scheduled is null)
        {
            CreateStartupShortcut(launch);
            SetStartupApprovedEnabled();
        }
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

    private static string GetStartupShortcutPath(string registrationName)
    {
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Startup), registrationName + ".lnk");
    }

    private static List<StartupRegistration> ReadOwnedRegistrations(LaunchInfo launch)
    {
        var result = new List<StartupRegistration>();
        var targets = GetOwnedExecutablePaths(launch);
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
        foreach (var name in RegistrationNames)
        {
            var shortcutPath = GetStartupShortcutPath(name);
            var hasShortcut = File.Exists(shortcutPath);
            var runValue = key?.GetValue(name);
            if (hasShortcut && !IsOwnedShortcut(shortcutPath, targets, launch) ||
                runValue is not null && (runValue is not string command || !IsOwnedCommand(command, targets, launch)))
            {
                throw new InvalidOperationException($"The startup entry '{name}' points outside this Pandora installation or could not be identified. It was left unchanged. Review it in Windows Startup apps before changing this option.");
            }
            result.Add(new StartupRegistration(name, hasShortcut, runValue is not null));
        }
        return result;
    }

    private static HashSet<string> GetOwnedExecutablePaths(LaunchInfo launch)
    {
        var targets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!string.Equals(Path.GetFileName(launch.TargetPath), "dotnet.exe", StringComparison.OrdinalIgnoreCase))
        {
            targets.Add(Path.GetFullPath(launch.TargetPath));
        }
        var appDirectory = new DirectoryInfo(AppContext.BaseDirectory);
        // Ownership is limited to this installation's exact executable path.
        targets.Add(Path.GetFullPath(Path.Combine(appDirectory.FullName, "Pandora.App.exe")));
        return targets;
    }

    private static bool IsOwnedCommand(string command, HashSet<string> targets, LaunchInfo launch)
    {
        var trimmed = command.Trim();
        foreach (var target in targets)
        {
            if (string.Equals(trimmed, Quote(target), StringComparison.OrdinalIgnoreCase) ||
                string.Equals(trimmed, target, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        // For a framework-dependent launch, match the full dotnet + assembly
        // command. Merely sharing dotnet.exe does not establish app ownership.
        return !string.IsNullOrWhiteSpace(launch.Arguments) &&
            string.Equals(trimmed, $"{Quote(launch.TargetPath)} {launch.Arguments}", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsOwnedShortcut(string path, HashSet<string> targets, LaunchInfo launch)
    {
        var shellType = Type.GetTypeFromProgID("WScript.Shell")
            ?? throw new InvalidOperationException("Windows Script Host is unavailable, so Pandora could not inspect a Startup shortcut.");
        object? shell = null;
        object? shortcut = null;
        try
        {
            shell = Activator.CreateInstance(shellType)
                ?? throw new InvalidOperationException("Could not create the Windows shortcut helper.");
            // Reading an existing shortcut does not save or modify it.
            shortcut = shellType.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, [path])
                ?? throw new InvalidOperationException("Could not inspect the Startup shortcut.");
            var shortcutType = shortcut.GetType();
            var target = shortcutType.InvokeMember("TargetPath", BindingFlags.GetProperty, null, shortcut, null) as string;
            var arguments = shortcutType.InvokeMember("Arguments", BindingFlags.GetProperty, null, shortcut, null) as string;
            return !string.IsNullOrWhiteSpace(target) && (targets.Contains(Path.GetFullPath(target)) ||
                !string.IsNullOrWhiteSpace(launch.Arguments) &&
                string.Equals(target, launch.TargetPath, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(arguments, launch.Arguments, StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            ReleaseComObject(shortcut);
            ReleaseComObject(shell);
        }
    }

    private static void CreateStartupShortcut(LaunchInfo launch)
    {
        var shortcutPath = GetStartupShortcutPath();
        Directory.CreateDirectory(Path.GetDirectoryName(shortcutPath)!);

        var shellType = Type.GetTypeFromProgID("WScript.Shell")
            ?? throw new InvalidOperationException("Windows Script Host is unavailable, so Pandora could not create a Startup shortcut.");
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
            shortcutType.InvokeMember("Description", BindingFlags.SetProperty, null, shortcut, ["Pandora desktop docks and project visibility"]);
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

    private static LaunchInfo ResolveLaunchInfo(string? iconStyle = null)
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
                ResolveIconLocation(processPath, iconStyle));
        }

        var assemblyPath = Assembly.GetEntryAssembly()?.Location;
        if (!string.IsNullOrWhiteSpace(processPath) && !string.IsNullOrWhiteSpace(assemblyPath))
        {
            return new LaunchInfo(
                processPath,
                Quote(assemblyPath),
                Path.GetDirectoryName(assemblyPath) ?? AppContext.BaseDirectory,
                ResolveIconLocation(assemblyPath, iconStyle));
        }

        throw new InvalidOperationException("Could not resolve the Pandora launch command.");
    }

    private static string ResolveIconLocation(string launchPath, string? iconStyle)
    {
        var iconPath = BrandIdentity.IconPath(iconStyle);
        return File.Exists(iconPath) ? iconPath : launchPath;
    }

    private static void DeleteStartupShortcut(string registrationName)
    {
        var shortcutPath = GetStartupShortcutPath(registrationName);
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
        using var key = Registry.CurrentUser.OpenSubKey(keyPath, writable: true);
        key?.DeleteValue(valueName, throwOnMissingValue: false);
    }

    private static bool ReadStartupApproval(string keyPath, string valueName)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(keyPath, writable: false);
            return IsApprovalEnabled(key?.GetValue(valueName));
        }
        catch
        {
            // Do not report an unreadable OS approval state as enabled.
            return false;
        }
    }

    private static bool IsApprovalEnabled(object? value)
    {
        // Without an override, Windows allows an existing startup entry. The
        // supported enabled encodings are 2 and 6; disabled encodings (3/7),
        // malformed values and unknown future states conservatively return false.
        return value is null || value is byte[] { Length: >= 4 } bytes &&
            BitConverter.ToUInt32(bytes, 0) is 2 or 6;
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
    private sealed record StartupRegistration(string Name, bool HasShortcut, bool HasRunEntry);
}
