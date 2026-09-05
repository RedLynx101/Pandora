namespace OrbitDock.Core;

public static class WorkspaceMigrator
{
    public const int CurrentSchemaVersion = 7;

    public static bool MigrateToCurrent(Workspace workspace)
    {
        WorkspaceValidation.ThrowIfInvalid(workspace);
        var changed = false;

        if (workspace.SchemaVersion < CurrentSchemaVersion)
        {
            changed = true;
        }

        if (workspace.Settings.Audio is null)
        {
            workspace.Settings.Audio = new AudioSettings();
            changed = true;
        }

        if (string.IsNullOrWhiteSpace(workspace.Settings.DockBarSize))
        {
            workspace.Settings.DockBarSize = "Standard";
            changed = true;
        }

        // Existing palette, dock geometry overrides and icon choice are not rewritten.
        if (string.IsNullOrWhiteSpace(workspace.Settings.DockTheme))
        {
            workspace.Settings.DockTheme = "Classic";
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

        foreach (var layout in workspace.Layouts)
        {
            if (layout.DisplayVariants.Count == 0)
            {
                layout.DisplayVariants.Add(new DisplayLayoutVariant
                {
                    Key = WorkspaceLayoutService.DefaultDisplayVariantKey,
                    DisplaySignature = WorkspaceLayoutService.DefaultDisplayVariantKey,
                    IsDefault = true,
                    DockStates = layout.DockStates.Select(state => new DockLayoutState
                    {
                        DockId = state.DockId,
                        IsVisible = state.IsVisible,
                        IsLocked = state.IsLocked,
                        IsCollapsed = state.IsCollapsed,
                        ExpansionEdge = state.ExpansionEdge,
                        ActiveTabId = state.ActiveTabId,
                        Bounds = new ZoneBounds
                        {
                            X = state.Bounds.X,
                            Y = state.Bounds.Y,
                            Width = state.Bounds.Width,
                            Height = state.Bounds.Height
                        }
                    }).ToList(),
                    DesktopPins = layout.DesktopPins.Select(pin => new DesktopPinDefinition
                    {
                        Id = pin.Id,
                        Path = pin.Path,
                        DisplayName = pin.DisplayName,
                        X = pin.X,
                        Y = pin.Y,
                        IconSize = pin.IconSize
                    }).ToList()
                });
                layout.ActiveDisplayVariantKey = WorkspaceLayoutService.DefaultDisplayVariantKey;
                changed = true;
            }

            if (string.IsNullOrWhiteSpace(layout.ActiveDisplayVariantKey))
            {
                layout.ActiveDisplayVariantKey = layout.DisplayVariants.FirstOrDefault(variant => variant.IsDefault)?.Key
                    ?? layout.DisplayVariants.First().Key;
                changed = true;
            }
        }

        var zoneCountBefore = workspace.Zones.Count;
        if (workspace.Settings.Audio.EnableMusicDock)
        {
            WorkspaceLayoutService.EnsureMusicDock(workspace);
        }

        WorkspaceLayoutService.EnsureAgentFeedDock(workspace);
        WorkspaceLayoutService.EnsureProjectsDock(workspace);
        if (workspace.Zones.Count != zoneCountBefore) changed = true;

        // Rename only known factory labels. User-authored dock names and persisted IDs stay intact.
        foreach (var zone in workspace.Zones)
        {
            var name = zone.Name switch
            {
                "Orbit Launchpad" => "Pandora Launchpad",
                "Orbit Brief" => "Brief",
                "Orbit Radio" => "Radio",
                _ => zone.Name
            };
            if (name != zone.Name) { zone.Name = name; changed = true; }
        }

        var activeBefore = workspace.ActiveLayoutName;
        var variantBefore = WorkspaceLayoutService.EnsureActiveLayout(workspace).ActiveDisplayVariantKey;
        WorkspaceLayoutService.EnsureActiveDisplayVariant(workspace);
        if (!string.Equals(activeBefore, workspace.ActiveLayoutName, StringComparison.Ordinal))
        {
            changed = true;
        }

        if (!string.Equals(variantBefore, WorkspaceLayoutService.EnsureActiveLayout(workspace).ActiveDisplayVariantKey, StringComparison.Ordinal))
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
