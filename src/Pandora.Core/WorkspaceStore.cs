using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

[assembly: InternalsVisibleTo("Pandora.Tests")]

namespace Pandora.Core;

public sealed class WorkspaceStore
{
    private const int LockRetryCount = 80;
    private const int LockRetryDelayMilliseconds = 50;
    private static readonly ConditionalWeakTable<Workspace, PersistenceStamp> Stamps = new();
    private static readonly ConditionalWeakTable<Workspace, SnapshotOrigin> SnapshotOrigins = new();
    private const int MaximumWorkspaceBytes = 8 * 1024 * 1024;
    private readonly string? _legacyWorkspacePath;
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public WorkspaceStore(string workspacePath) : this(workspacePath, null) { }

    internal WorkspaceStore(string workspacePath, string? legacyWorkspacePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspacePath);
        WorkspacePath = Path.GetFullPath(workspacePath);
        _legacyWorkspacePath = legacyWorkspacePath is null ? null : Path.GetFullPath(legacyWorkspacePath);
    }

    public string WorkspacePath { get; }
    public string RecoveryDirectory => WorkspacePath + ".recovery";

    // Deterministic failure injection for fixture tests; never set by product callers.
    internal Action? BeforeBackupForTests { get; set; }
    internal Action? BeforeReplaceForTests { get; set; }

    /// <summary>Read and migrate a snapshot in memory, without creating locks, directories, backups or files.</summary>
    public Workspace LoadReadOnly()
    {
        var bytes = ReadExistingBytes(WorkspacePath);
        return PrepareSnapshot(bytes);
    }

    /// <summary>Compare this snapshot with disk without locks or other filesystem writes.</summary>
    public bool IsCurrent(Workspace workspace)
    {
        if (!Stamps.TryGetValue(workspace, out var expected) ||
            !string.Equals(expected.Path, WorkspacePath, StringComparison.OrdinalIgnoreCase)) return false;
        var bytes = ReadBytesIfPresent(WorkspacePath);
        return bytes is not null && string.Equals(expected.Fingerprint, Fingerprint(bytes), StringComparison.Ordinal);
    }

    public Workspace LoadOrCreate()
    {
        using var guard = AcquireLock();
        var bytes = ReadBytesIfPresent(WorkspacePath);
        if (bytes is null)
        {
            // Legacy import is a startup operation, never a side effect of resolving the user path.
            var legacy = _legacyWorkspacePath is null ? null : ReadBytesIfPresent(_legacyWorkspacePath);
            var created = legacy is null ? WorkspaceFactory.CreateDefault() : PrepareSnapshot(legacy, attachStamp: false);
            WorkspaceMigrator.MigrateToCurrent(created);
            WorkspaceLayoutService.ApplyActiveLayoutToZones(created);
            WriteCore(created, Serialize(created), destinationExists: false);
            return created;
        }

        var loaded = Parse(bytes);
        var originalVersion = loaded.SchemaVersion;
        var migrated = WorkspaceMigrator.MigrateToCurrent(loaded);
        WorkspaceLayoutService.ApplyActiveLayoutToZones(loaded);
        Stamp(loaded, bytes, originalVersion);
        if (migrated)
        {
            var serialized = Serialize(loaded);
            BackupCore(bytes, $"migrated-v{WorkspaceMigrator.CurrentSchemaVersion}");
            WriteCore(loaded, serialized, destinationExists: true);
        }
        return loaded;
    }

    public void Save(Workspace workspace)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        WorkspaceMigrator.MigrateToCurrent(workspace);
        var bytes = Serialize(workspace);
        using var guard = AcquireLock();
        var current = ReadBytesIfPresent(WorkspacePath);
        Stamps.TryGetValue(workspace, out var expected);
        if (expected is null ? current is not null :
            !string.Equals(expected.Path, WorkspacePath, StringComparison.OrdinalIgnoreCase) ||
            current is null || !string.Equals(expected.Fingerprint, Fingerprint(current), StringComparison.Ordinal))
        {
            throw new WorkspaceConflictException(
                $"Workspace changed on disk. This change was not saved. Reload '{WorkspacePath}' and reapply the change.");
        }

        if (current is not null && expected!.SchemaVersion < WorkspaceMigrator.CurrentSchemaVersion)
            BackupCore(current, $"migrated-v{WorkspaceMigrator.CurrentSchemaVersion}");
        if (current is not null && current.AsSpan().SequenceEqual(bytes)) return;
        if (current is not null) PreserveRecoverySnapshot(current);
        WriteCore(workspace, bytes, destinationExists: current is not null);
    }

    /// <summary>Capture on the model's owning thread; save only this detached object on a worker.</summary>
    public Workspace CreateSaveSnapshot(Workspace source)
    {
        if (!Stamps.TryGetValue(source, out var stamp) || stamp.Path != WorkspacePath)
            throw new WorkspaceConflictException("Cannot capture an untracked workspace.");
        var snapshot = Parse(Serialize(source)); // Do not reapply a layout over the latest geometry.
        Stamps.Add(snapshot, stamp);
        SnapshotOrigins.Add(snapshot, new SnapshotOrigin(source, stamp));
        return snapshot;
    }

    /// <summary>Advance only persistence metadata, never overwrite edits made during the save.</summary>
    public void AcceptSavedSnapshot(Workspace source, Workspace snapshot)
    {
        if (!SnapshotOrigins.TryGetValue(snapshot, out var origin) || !ReferenceEquals(origin.Source, source) ||
            !Stamps.TryGetValue(source, out var before) || before != origin.Stamp ||
            !Stamps.TryGetValue(snapshot, out var after) || after.Path != WorkspacePath)
            throw new WorkspaceConflictException("The workspace changed while accepting a background save.");
        Stamps.Remove(source);
        Stamps.Add(source, after);
        SnapshotOrigins.Remove(snapshot);
    }

    /// <summary>Explicit recovery only. Validate first and retain the current bytes before replacement.</summary>
    public Workspace RestoreFromBackup(string backupPath)
    {
        var candidate = PrepareSnapshot(ReadExistingBytes(Path.GetFullPath(backupPath)), attachStamp: false);
        var bytes = Serialize(candidate);
        using var guard = AcquireLock();
        var current = ReadBytesIfPresent(WorkspacePath);
        if (current is not null) BackupCore(current, "before-restore");
        WriteCore(candidate, bytes, current is not null);
        return candidate;
    }

    private void PreserveRecoverySnapshot(byte[] bytes)
    {
        // Five fixed slots, no more than one per five minutes. Manual/migration
        // backups are separate and are never pruned by this rotation.
        Parse(bytes);
        Directory.CreateDirectory(RecoveryDirectory);
        var slots = Enumerable.Range(1, 5).Select(i => Path.Combine(RecoveryDirectory, $"workspace-{i}.json")).ToArray();
        var newest = slots.Where(File.Exists).Select(File.GetLastWriteTimeUtc).DefaultIfEmpty(DateTime.MinValue).Max();
        if (DateTime.UtcNow - newest < TimeSpan.FromMinutes(5)) return;
        var target = slots.OrderBy(path => File.Exists(path) ? File.GetLastWriteTimeUtc(path) : DateTime.MinValue).First();
        var temporary = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            { stream.Write(bytes); stream.Flush(flushToDisk: true); }
            if (File.Exists(target)) File.Replace(temporary, target, null);
            else File.Move(temporary, target);
        }
        finally { TryDelete(temporary); }
    }

    public string Backup(string reason = "manual")
    {
        using var guard = AcquireLock();
        var bytes = ReadBytesIfPresent(WorkspacePath);
        return bytes is null ? string.Empty : BackupCore(bytes, reason);
    }

    /// <summary>Resolve Pandora user storage only; creation requires LoadOrCreate.</summary>
    public static WorkspaceStore ForCurrentUser()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return new WorkspaceStore(Path.Combine(appData, "Pandora", "workspace.json"));
    }

    private Workspace PrepareSnapshot(byte[] bytes, bool attachStamp = true)
    {
        var workspace = Parse(bytes);
        var originalVersion = workspace.SchemaVersion;
        WorkspaceMigrator.MigrateToCurrent(workspace);
        WorkspaceLayoutService.ApplyActiveLayoutToZones(workspace);
        if (attachStamp) Stamp(workspace, bytes, originalVersion);
        return workspace;
    }

    private Workspace Parse(byte[] bytes)
    {
        // StreamReader preserves support for existing BOM-marked UTF-8/UTF-16 workspaces.
        using var reader = new StreamReader(new MemoryStream(bytes), Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        var workspace = JsonSerializer.Deserialize<Workspace>(reader.ReadToEnd(), _jsonOptions)
            ?? throw new WorkspaceValidationException("Workspace must be a JSON object, not null.");
        WorkspaceValidation.ThrowIfInvalid(workspace);
        return workspace;
    }

    private byte[] Serialize(Workspace workspace)
    {
        WorkspaceValidation.ThrowIfInvalid(workspace);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(workspace, _jsonOptions);
        if (bytes.Length > MaximumWorkspaceBytes) throw new WorkspaceValidationException("Workspace exceeds the 8 MiB safety limit.");
        return bytes;
    }

    private void WriteCore(Workspace workspace, byte[] bytes, bool destinationExists)
    {
        var temporaryPath = WorkspacePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                stream.Write(bytes);
                stream.Flush(flushToDisk: true);
            }
            BeforeReplaceForTests?.Invoke();
            if (destinationExists) File.Replace(temporaryPath, WorkspacePath, null);
            else File.Move(temporaryPath, WorkspacePath);
            Stamp(workspace, bytes, workspace.SchemaVersion);
        }
        finally { TryDelete(temporaryPath); }
    }

    private string BackupCore(byte[] bytes, string reason)
    {
        BeforeBackupForTests?.Invoke();
        var timestamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmssfff");
        var backupPath = $"{WorkspacePath}.{timestamp}.{Guid.NewGuid():N}.{SanitizeReason(reason)}.bak";
        using var stream = new FileStream(backupPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        stream.Write(bytes);
        stream.Flush(flushToDisk: true);
        return backupPath;
    }

    private void Stamp(Workspace workspace, byte[] bytes, int schemaVersion)
    {
        Stamps.Remove(workspace);
        Stamps.Add(workspace, new PersistenceStamp(WorkspacePath, Fingerprint(bytes), schemaVersion));
    }

    private static string Fingerprint(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));

    private static byte[] ReadExistingBytes(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
        if (stream.Length > MaximumWorkspaceBytes) throw new WorkspaceValidationException("Workspace exceeds the 8 MiB safety limit.");
        using var buffer = new MemoryStream((int)stream.Length);
        var chunk = new byte[16 * 1024];
        int count;
        while ((count = stream.Read(chunk)) > 0)
        {
            if (buffer.Length + count > MaximumWorkspaceBytes) throw new WorkspaceValidationException("Workspace exceeds the 8 MiB safety limit.");
            buffer.Write(chunk, 0, count);
        }
        return buffer.ToArray();
    }

    private static byte[]? ReadBytesIfPresent(string path)
    {
        try { return ReadExistingBytes(path); }
        catch (FileNotFoundException) { return null; }
        catch (DirectoryNotFoundException) { return null; }
    }

    private FileStream AcquireLock()
    {
        var lockPath = WorkspacePath + ".lock";
        Directory.CreateDirectory(Path.GetDirectoryName(lockPath)!);
        Exception? lastError = null;
        for (var i = 0; i < LockRetryCount; i++)
        {
            try { return new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
            catch (IOException ex) { lastError = ex; Thread.Sleep(LockRetryDelayMilliseconds); }
            catch (UnauthorizedAccessException ex) { lastError = ex; Thread.Sleep(LockRetryDelayMilliseconds); }
        }
        throw new IOException($"Could not acquire workspace lock: {lockPath}", lastError);
    }

    private static string SanitizeReason(string reason)
    {
        var value = string.IsNullOrWhiteSpace(reason) ? "manual" : reason.Trim();
        foreach (var invalid in Path.GetInvalidFileNameChars()) value = value.Replace(invalid, '-');
        return value.Length > 80 ? value[..80] : value;
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private sealed record PersistenceStamp(string Path, string Fingerprint, int SchemaVersion);
    private sealed record SnapshotOrigin(Workspace Source, PersistenceStamp Stamp);
}
