namespace CustomFences.Core;

public static class WorkspaceMigrator
{
    public const int CurrentSchemaVersion = 2;

    public static bool MigrateToCurrent(Workspace workspace)
    {
        var changed = false;

        if (workspace.SchemaVersion < CurrentSchemaVersion)
        {
            changed = true;
        }

        if (workspace.Layouts.Count == 0)
        {
            workspace.Layouts.Add(WorkspaceLayoutService.CreateProfileFromZones(WorkspaceLayoutService.DefaultLayoutName, workspace));
            workspace.ActiveLayoutName = WorkspaceLayoutService.DefaultLayoutName;
            changed = true;
        }

        if (string.IsNullOrWhiteSpace(workspace.ActiveLayoutName))
        {
            workspace.ActiveLayoutName = workspace.Layouts[0].Name;
            changed = true;
        }

        var activeBefore = workspace.ActiveLayoutName;
        WorkspaceLayoutService.EnsureActiveLayout(workspace);
        if (!string.Equals(activeBefore, workspace.ActiveLayoutName, StringComparison.Ordinal))
        {
            changed = true;
        }

        if (workspace.SchemaVersion != CurrentSchemaVersion)
        {
            workspace.SchemaVersion = CurrentSchemaVersion;
            changed = true;
        }

        return changed;
    }
}
