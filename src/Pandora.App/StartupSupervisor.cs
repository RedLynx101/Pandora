using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Threading;

namespace Pandora.App;

/// <summary>Small headless parent used only by the managed scheduled task.
/// Monitors its own child handle, not process-name polling. Exit 0 stays closed.</summary>
internal static class StartupSupervisor
{
    public static int Run()
    {
        using var mutex = new Mutex(true, "Pandora.Supervisor", out var first);
        if (!first) return 0;
        using var stop = new EventWaitHandle(false, EventResetMode.ManualReset, "Pandora.SupervisorStop");
        using var ready = new EventWaitHandle(false, EventResetMode.AutoReset, "Pandora.StartupReady");
        for (var attempt = 0; attempt <= 3; attempt++)
        {
            if (stop.WaitOne(0) || !IsEnabled()) return 0;
            ready.Reset();
            Record("starting", attempt);
            int exitCode;
            try
            {
                using var child = Process.Start(new ProcessStartInfo(Environment.ProcessPath!, "--scheduled")
                {
                    UseShellExecute = false,
                    WorkingDirectory = AppContext.BaseDirectory
                }) ?? throw new InvalidOperationException("Could not start Pandora.");
                // Holding the Process handle preserves the real exit code even
                // for a fast crash. A stop request never force-kills user data.
                Record("loading", attempt, child.Id);
                var started = Stopwatch.StartNew();
                var reportedReady = false;
                var reportedSlow = false;
                while (!child.WaitForExit(500))
                {
                    if (!reportedReady && ready.WaitOne(0))
                    {
                        reportedReady = true;
                        Record("ready", attempt, child.Id);
                    }
                    if (!reportedReady && !reportedSlow && started.Elapsed > TimeSpan.FromSeconds(30))
                    {
                        reportedSlow = true;
                        Record("startup-not-ready", attempt, child.Id);
                    }
                }
                exitCode = child.ExitCode;
            }
            catch (Exception ex) { Debug.WriteLine(ex); exitCode = 1; }
            if (exitCode == 0 || stop.WaitOne(0) || !IsEnabled())
            {
                Record("stopped", attempt, exitCode: exitCode);
                return 0;
            }
            if (attempt == 3)
            {
                Record("retries-exhausted", attempt, exitCode: exitCode);
                return 0; // do not let Scheduler reset the budget
            }
            Record("retry-pending", attempt, exitCode: exitCode);
            if (stop.WaitOne(TimeSpan.FromMinutes(1))) { Record("stopped", attempt); return 0; }
        }
        return 0;
    }

    private static bool IsEnabled()
    {
        try { using var task = StartupScheduledTask.Open(); return task?.Enabled == true; }
        catch { return false; } // changed/unreadable task stops recovery
    }

    private static void Record(string state, int retriesUsed, int? childPid = null, int? exitCode = null)
    {
        // One bounded local status file, separate from workspace diagnostics;
        // useful even when user-data initialization has failed. No telemetry.
        try
        {
            var directory = Path.Combine(AppContext.BaseDirectory, "Diagnostics");
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, "startup-recovery.json");
            File.WriteAllText(path, JsonSerializer.Serialize(new {
                at = DateTimeOffset.UtcNow, state, supervisorPid = Environment.ProcessId,
                childPid, retriesUsed, maximumRetries = 3, retryDelaySeconds = 60, exitCode,
                userDataPath = Pandora.Core.UserDataPaths.Root,
                appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData)
            }, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Debug.WriteLine(ex); }
    }
}
