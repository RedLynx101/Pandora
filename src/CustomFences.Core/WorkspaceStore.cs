using System.Text.Json;
using System.Text.Json.Serialization;

namespace CustomFences.Core;

public sealed class WorkspaceStore
{
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
            return loaded ?? CreateReplacementForInvalidWorkspace("empty");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return CreateReplacementForInvalidWorkspace(ex.GetType().Name);
        }
    }

    public void Save(Workspace workspace)
    {
        var directory = Path.GetDirectoryName(WorkspacePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var temporaryPath = WorkspacePath + ".tmp";
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

    public static WorkspaceStore ForCurrentUser()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return new WorkspaceStore(Path.Combine(appData, "CustomFences", "workspace.json"));
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
}
