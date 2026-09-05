using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace Pandora.App;

/// <summary>Bounded, local-only breadcrumbs. Never records dashboard contents or sends telemetry.</summary>
internal static class RuntimeDiagnostics
{
    private static readonly object Guard = new();
    private static readonly Dictionary<string, object> State = new(StringComparer.Ordinal);
    private static string? _directory;

    public static void Initialize(string workspacePath)
    {
        lock (Guard) _directory = Path.Combine(Path.GetDirectoryName(workspacePath)!, "Diagnostics");
        Record("application", new { version = typeof(App).Assembly.GetName().Version?.ToString(),
            build = System.Reflection.CustomAttributeExtensions.GetCustomAttribute<System.Reflection.AssemblyInformationalVersionAttribute>(typeof(App).Assembly)?.InformationalVersion,
            startedAt = DateTimeOffset.UtcNow, processId = Environment.ProcessId, workspacePath });
    }

    public static void Record(string category, object value)
    {
        lock (Guard)
        {
            if (_directory is null) return; // Offscreen tests never touch user diagnostics.
            try
            {
                Directory.CreateDirectory(_directory);
                State[category] = value;
                var path = Path.Combine(_directory, "runtime.json");
                var temporary = path + ".tmp";
                var bytes = JsonSerializer.SerializeToUtf8Bytes(new { updatedAt = DateTimeOffset.UtcNow, state = State }, new JsonSerializerOptions { WriteIndented = true });
                if (bytes.Length > 128 * 1024) return;
                File.WriteAllBytes(temporary, bytes);
                if (File.Exists(path)) File.Replace(temporary, path, null);
                else File.Move(temporary, path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { /* Diagnostics cannot break the app. */ }
        }
    }

    public static void RecordFailure(Exception error)
    {
        lock (Guard)
        {
            if (_directory is null) return;
            try
            {
                Directory.CreateDirectory(_directory);
                var path = Path.Combine(_directory, "errors.log");
                if (File.Exists(path) && new FileInfo(path).Length > 128 * 1024) File.Move(path, path + ".previous", overwrite: true);
                var detail = error.ToString();
                if (detail.Length > 12 * 1024) detail = detail[..(12 * 1024)];
                File.AppendAllText(path, $"{DateTimeOffset.UtcNow:O} {detail}{Environment.NewLine}");
            }
            catch { /* Best effort, including during low-memory shutdown. */ }
        }
    }
}
