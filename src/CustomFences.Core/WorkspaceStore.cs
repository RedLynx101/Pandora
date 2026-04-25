using System.Text.Json;
using System.Text.Json.Serialization;

namespace CustomFences.Core;

public sealed class WorkspaceStore
{
    private const int LockRetryCount = 80;
    private const int LockRetryDelayMilliseconds = 50;

    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public WorkspaceStore(string workspacePath)
    {
        WorkspacePath = workspacePath;
    }

    public string WorkspacePath { get; }

    public Workspace LoadOrCreate()
    {
        if (!File.Exists(WorkspacePath))
        {
            var workspace = WorkspaceFactory.CreateDefault();
            Save(workspace);
            return workspace;
        }

        try
        {
            var json = File.ReadAllText(WorkspacePath);
            var loaded = JsonSerializer.Deserialize<Workspace>(json, _jsonOptions);
            if (loaded is null)
            {
                return CreateReplacementForInvalidWorkspace("empty");
            }

            var migrated = WorkspaceMigrator.MigrateToCurrent(loaded);
            WorkspaceLayoutService.ApplyActiveLayoutToZones(loaded);
            if (migrated)
            {
                Backup("migrated-v2");
                Save(loaded);
            }

            return loaded;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return CreateReplacementForInvalidWorkspace(ex.GetType().Name);
        }
    }

    public void Save(Workspace workspace)
    {
        WorkspaceMigrator.MigrateToCurrent(workspace);
        var directory = Path.GetDirectoryName(WorkspacePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        using var guard = AcquireLock();
        var temporaryPath = WorkspacePath + ".tmp";
        try
        {
            var json = JsonSerializer.Serialize(workspace, _jsonOptions);
            File.WriteAllText(temporaryPath, json);

            if (File.Exists(WorkspacePath))
            {
                File.Replace(temporaryPath, WorkspacePath, null);
            }
            else
            {
                File.Move(temporaryPath, WorkspacePath);
            }
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                TryDelete(temporaryPath);
            }
        }
    }

    public string Backup(string reason = "manual")
    {
        if (!File.Exists(WorkspacePath))
        {
            return string.Empty;
        }

        using var guard = AcquireLock();
        var timestamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        var backupPath = $"{WorkspacePath}.{timestamp}.{SanitizeReason(reason)}.bak";
        File.Copy(WorkspacePath, backupPath, overwrite: false);
        return backupPath;
    }

    public static WorkspaceStore ForCurrentUser()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var workspacePath = Path.Combine(appData, "OrbitDock", "workspace.json");
        TryImportLegacyWorkspace(workspacePath, Path.Combine(appData, "CustomFences", "workspace.json"));
        return new WorkspaceStore(workspacePath);
    }

    private Workspace CreateReplacementForInvalidWorkspace(string reason)
    {
        TryBackupInvalidWorkspace(reason);
        var workspace = WorkspaceFactory.CreateDefault();
        Save(workspace);
        return workspace;
    }

    private void TryBackupInvalidWorkspace(string reason)
    {
        if (!File.Exists(WorkspacePath))
        {
            return;
        }

        try
        {
            var timestamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
            var backupPath = $"{WorkspacePath}.{timestamp}.{reason}.bak";
            File.Copy(WorkspacePath, backupPath, overwrite: false);
        }
        catch
        {
            // Invalid configs should not block startup. Best effort backup is enough here.
        }
    }

    private FileStream AcquireLock()
    {
        var lockPath = WorkspacePath + ".lock";
        var directory = Path.GetDirectoryName(lockPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        Exception? lastError = null;
        for (var i = 0; i < LockRetryCount; i++)
        {
            try
            {
                return new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            }
            catch (IOException ex)
            {
                lastError = ex;
                Thread.Sleep(LockRetryDelayMilliseconds);
            }
            catch (UnauthorizedAccessException ex)
            {
                lastError = ex;
                Thread.Sleep(LockRetryDelayMilliseconds);
            }
        }

        throw new IOException($"Could not acquire workspace lock: {lockPath}", lastError);
    }

    private static void TryImportLegacyWorkspace(string workspacePath, string legacyPath)
    {
        if (File.Exists(workspacePath) || !File.Exists(legacyPath))
        {
            return;
        }

        try
        {
            var directory = Path.GetDirectoryName(workspacePath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.Copy(legacyPath, workspacePath, overwrite: false);
        }
        catch
        {
            // Import is a convenience. Startup can still create a fresh OrbitDock workspace.
        }
    }

    private static string SanitizeReason(string reason)
    {
        var value = string.IsNullOrWhiteSpace(reason) ? "manual" : reason.Trim();
        foreach (var invalid in Path.GetInvalidFileNameChars())
        {
            value = value.Replace(invalid, '-');
        }

        return value;
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch
        {
            // Temporary cleanup should not mask the original save failure.
        }
    }
}
