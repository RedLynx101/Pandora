using System.Text;
using System.Text.Json;

namespace OrbitDock.Core;

public sealed record ProjectCheckpoint(string DashboardId, string ProjectId, string PlanId, string TaskId, long Revision, DateTimeOffset UpdatedAt);
public sealed record ProjectRegistration(string Id, string Path, bool Expanded = false, ProjectCheckpoint? LastAccepted = null);

/// <summary>The app owns registration, display preferences, and anti-regression watermarks, never the dashboards.</summary>
public sealed class ProjectRegistryStore(string registryPath)
{
    public const int MaxRegistrations = 32;
    public const int MaxRegistryBytes = 256 * 1024;
    private static readonly JsonSerializerOptions Options = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true };
    public string RegistryPath { get; } = System.IO.Path.GetFullPath(registryPath);
    private sealed record Registry(int Version, IReadOnlyList<ProjectRegistration> Dashboards);

    public IReadOnlyList<ProjectRegistration> Load()
    {
        if (!File.Exists(RegistryPath)) return [];
        if ((File.GetAttributes(RegistryPath) & FileAttributes.ReparsePoint) != 0) throw new IOException("Project registry must be a regular file.");
        using var stream = new FileStream(RegistryPath, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
        if (stream.Length > MaxRegistryBytes) throw new InvalidDataException("Project registry exceeds its size limit.");
        var registry = JsonSerializer.Deserialize<Registry>(stream, Options) ?? throw new InvalidDataException("Project registry is empty.");
        if (registry.Version != 1 || registry.Dashboards is null || registry.Dashboards.Count > MaxRegistrations) throw new InvalidDataException("Unsupported or oversized project registry.");
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var registration in registry.Dashboards)
        {
            if (registration is null || !Guid.TryParseExact(registration.Id, "N", out _) || !ids.Add(registration.Id)) throw new InvalidDataException("Invalid or duplicate registration ID.");
            // A source can become unavailable or disallowed after registration.
            // Keep its registration readable/removable; filesystem policy belongs to each source read.
            var path = ProjectPath.ValidateSyntax(registration.Path);
            if (!paths.Add(path)) throw new InvalidDataException("Dashboard path is registered more than once.");
            if (registration.LastAccepted is { } checkpoint && (checkpoint.Revision < 0 || string.IsNullOrEmpty(checkpoint.DashboardId) || string.IsNullOrEmpty(checkpoint.PlanId) || string.IsNullOrEmpty(checkpoint.ProjectId) || string.IsNullOrEmpty(checkpoint.TaskId)))
                throw new InvalidDataException("Invalid project checkpoint.");
        }
        return registry.Dashboards;
    }

    public ProjectRegistration Register(string htmlPath)
    {
        var path = ProjectPath.Validate(htmlPath, requireExists: true);
        ProjectRegistration? result = null;
        Mutate(entries =>
        {
            result = entries.FirstOrDefault(e => string.Equals(e.Path, path, StringComparison.OrdinalIgnoreCase));
            if (result is not null) return entries;
            if (entries.Count >= MaxRegistrations) throw new InvalidOperationException($"At most {MaxRegistrations} dashboard files can be registered.");
            result = new ProjectRegistration(Guid.NewGuid().ToString("N"), path);
            return [.. entries, result];
        });
        return result!;
    }

    public void Remove(string registrationId) => Mutate(entries => entries.Where(e => e.Id != registrationId).ToArray());
    public void SetExpanded(string registrationId, bool expanded) => Mutate(entries => entries.Select(e => e.Id == registrationId ? e with { Expanded = expanded } : e).ToArray());

    public IReadOnlyDictionary<string, string> Accept(IReadOnlyDictionary<string, ProjectCheckpoint> checkpoints)
    {
        var rejected = new Dictionary<string, string>(StringComparer.Ordinal);
        Mutate(entries =>
        {
            foreach (var id in checkpoints.Keys)
                if (!entries.Any(e => e.Id == id)) rejected[id] = "This registration was removed during the read.";
            var nextEntries = entries.ToArray();
            for (var index = 0; index < nextEntries.Length; index++)
            {
                var entry = nextEntries[index];
                if (!checkpoints.TryGetValue(entry.Id, out var next)) continue;
                if (CheckpointError(next) is { } validationError)
                {
                    rejected[entry.Id] = validationError;
                    continue;
                }
                if (entry.LastAccepted is { } previous && Regression(previous, next) is { } error)
                {
                    rejected[entry.Id] = "A newer registration checkpoint superseded this read. " + error;
                    continue;
                }
                nextEntries[index] = entry with { LastAccepted = next };
                if (Encoding.UTF8.GetByteCount(Serialize(nextEntries)) > MaxRegistryBytes)
                {
                    nextEntries[index] = entry;
                    rejected[entry.Id] = "This checkpoint would exceed the project registry size limit.";
                }
            }
            return nextEntries;
        });
        return rejected;
    }

    public static ProjectCheckpoint Checkpoint(MetisSnapshot snapshot) => new(snapshot.DashboardId, snapshot.ProjectId, snapshot.PlanId, snapshot.TaskId, snapshot.Revision, snapshot.UpdatedAt);
    private static string? CheckpointError(ProjectCheckpoint? checkpoint) =>
        checkpoint is null || checkpoint.Revision < 0 ||
        !MetisReader.IsValidIdentifier(checkpoint.DashboardId) || !MetisReader.IsValidIdentifier(checkpoint.ProjectId) ||
        !MetisReader.IsValidIdentifier(checkpoint.PlanId) || !MetisReader.IsValidIdentifier(checkpoint.TaskId)
            ? $"Checkpoint identifiers must be valid IDs of at most {MetisReader.MaxIdentifierLength} characters, with a nonnegative revision."
            : null;
    public static string? Regression(ProjectCheckpoint previous, ProjectCheckpoint next)
    {
        if (previous.DashboardId != next.DashboardId || previous.ProjectId != next.ProjectId || previous.PlanId != next.PlanId || previous.TaskId != next.TaskId)
            return "Dashboard identity changed. Remove and register this file again to explicitly switch plans.";
        if (next.Revision < previous.Revision || next.UpdatedAt < previous.UpdatedAt) return "Plan revision or material update time moved backwards.";
        return null;
    }

    private void Mutate(Func<IReadOnlyList<ProjectRegistration>, IReadOnlyList<ProjectRegistration>> change)
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(RegistryPath)!);
        using var guard = AcquireLock();
        var previous = Load(); // Reload under a cross-process lock; two docks cannot overwrite one another's registrations.
        var next = change(previous);
        var json = Serialize(next);
        if (Encoding.UTF8.GetByteCount(json) > MaxRegistryBytes)
            throw new InvalidDataException("Project registry update exceeds its size limit. The previous registry was left unchanged.");
        if (File.Exists(RegistryPath) && json == Serialize(previous)) return;
        var temporary = RegistryPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (var writer = new StreamWriter(stream, new System.Text.UTF8Encoding(false)))
            {
                writer.Write(json); writer.Flush(); stream.Flush(flushToDisk: true);
            }
            File.Move(temporary, RegistryPath, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private static string Serialize(IReadOnlyList<ProjectRegistration> entries) => JsonSerializer.Serialize(new Registry(1, entries), Options);

    private FileStream AcquireLock()
    {
        for (var attempt = 0; ; attempt++)
        {
            try { return new FileStream(RegistryPath + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
            catch (IOException) when (attempt < 19) { Thread.Sleep(50); }
        }
    }
}

public static class ProjectPath
{
    /// <summary>Validate stored path syntax without touching a potentially unavailable or replaced source.</summary>
    public static string ValidateSyntax(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !System.IO.Path.IsPathFullyQualified(path) || path.StartsWith(@"\\", StringComparison.Ordinal) || path.StartsWith("//", StringComparison.Ordinal))
            throw new ArgumentException("Choose an absolute local dashboard HTML file, not a URL or network path.");
        var full = System.IO.Path.GetFullPath(path);
        var root = System.IO.Path.GetPathRoot(full)!;
        if (full[root.Length..].Contains(':')) throw new ArgumentException("Alternate data streams are not dashboard files.");
        var extension = System.IO.Path.GetExtension(full);
        if (!extension.Equals(".html", StringComparison.OrdinalIgnoreCase) && !extension.Equals(".htm", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Dashboard file must have an .html or .htm extension.");
        return full;
    }

    /// <summary>Only exact local regular .html/.htm files; no URLs, UNC/device paths, ADS, or links/junctions.</summary>
    public static string Validate(string path, bool requireExists)
    {
        var full = ValidateSyntax(path);
        var root = System.IO.Path.GetPathRoot(full)!;
        if (OperatingSystem.IsWindows() && new DriveInfo(root).DriveType == DriveType.Network)
            throw new ArgumentException("Mapped network drives are not local dashboard sources.");
        // Recheck on every read and explicit Open, not just at registration.
        for (var current = full; !string.IsNullOrEmpty(current); current = System.IO.Path.GetDirectoryName(current))
            if ((File.Exists(current) || Directory.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Dashboard paths cannot traverse a symbolic link or junction.");
        if (requireExists && !File.Exists(full)) throw new FileNotFoundException("Registered dashboard file is missing.", full);
        return full;
    }
}
