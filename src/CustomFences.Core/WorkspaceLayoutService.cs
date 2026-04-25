namespace CustomFences.Core;

public static class WorkspaceLayoutService
{
    public const string DefaultLayoutName = "Main";

    public static LayoutProfile EnsureActiveLayout(Workspace workspace)
    {
        if (workspace.Layouts.Count == 0)
        {
            workspace.Layouts.Add(CreateProfileFromZones(DefaultLayoutName, workspace));
        }

        if (string.IsNullOrWhiteSpace(workspace.ActiveLayoutName))
        {
            workspace.ActiveLayoutName = workspace.Layouts[0].Name;
        }

        var layout = workspace.Layouts.FirstOrDefault(profile =>
            string.Equals(profile.Name, workspace.ActiveLayoutName, StringComparison.OrdinalIgnoreCase));

        if (layout is null)
        {
            layout = workspace.Layouts[0];
            workspace.ActiveLayoutName = layout.Name;
        }

        EnsureDockStates(layout, workspace.Zones);
        return layout;
    }

    public static LayoutProfile CreateProfileFromZones(string name, Workspace workspace)
    {
        var profile = new LayoutProfile
        {
            Name = string.IsNullOrWhiteSpace(name) ? DefaultLayoutName : name.Trim(),
            HideDesktopIconsWhenRunning = workspace.Settings.HideDesktopIconsWhenRunning
        };

        foreach (var zone in workspace.Zones)
        {
            profile.DockStates.Add(CreateDockState(zone));
        }

        return profile;
    }

    public static void ApplyActiveLayoutToZones(Workspace workspace)
    {
        var layout = EnsureActiveLayout(workspace);
        workspace.Settings.HideDesktopIconsWhenRunning = layout.HideDesktopIconsWhenRunning;

        foreach (var zone in workspace.Zones)
        {
            var state = EnsureDockState(layout, zone);
            zone.IsVisible = state.IsVisible;
            zone.IsLocked = state.IsLocked;
            zone.IsCollapsed = state.IsCollapsed;
            zone.Bounds = CloneBounds(state.Bounds);
        }
    }

    public static void CaptureAllZoneStates(Workspace workspace)
    {
        var layout = EnsureActiveLayout(workspace);
        layout.HideDesktopIconsWhenRunning = workspace.Settings.HideDesktopIconsWhenRunning;

        foreach (var zone in workspace.Zones)
        {
            CaptureZoneState(layout, zone, null);
        }
    }

    public static void CaptureZoneState(Workspace workspace, ZoneDefinition zone, string? activeTabId)
    {
        var layout = EnsureActiveLayout(workspace);
        CaptureZoneState(layout, zone, activeTabId);
    }

    public static void CaptureActiveTab(Workspace workspace, ZoneDefinition zone, string? activeTabId)
    {
        var layout = EnsureActiveLayout(workspace);
        var state = EnsureDockState(layout, zone);
        state.ActiveTabId = activeTabId;
    }

    public static string? GetActiveTabId(Workspace workspace, ZoneDefinition zone)
    {
        var layout = EnsureActiveLayout(workspace);
        return EnsureDockState(layout, zone).ActiveTabId;
    }

    public static void SaveCurrentLayoutAs(Workspace workspace, string name)
    {
        var layoutName = NormalizeLayoutName(name);
        var source = CloneLayout(EnsureActiveLayout(workspace), layoutName);
        source.DockStates.Clear();
        foreach (var zone in workspace.Zones)
        {
            source.DockStates.Add(CreateDockState(zone));
        }

        ReplaceOrAddLayout(workspace, source);
        workspace.ActiveLayoutName = layoutName;
        ApplyActiveLayoutToZones(workspace);
    }

    public static void SwitchLayout(Workspace workspace, string name)
    {
        var layout = FindLayout(workspace, name) ?? throw new InvalidOperationException($"Layout '{name}' was not found.");
        workspace.ActiveLayoutName = layout.Name;
        ApplyActiveLayoutToZones(workspace);
    }

    public static void DuplicateLayout(Workspace workspace, string fromName, string toName)
    {
        var source = FindLayout(workspace, fromName) ?? throw new InvalidOperationException($"Layout '{fromName}' was not found.");
        var target = NormalizeLayoutName(toName);
        if (FindLayout(workspace, target) is not null)
        {
            throw new InvalidOperationException($"Layout '{target}' already exists.");
        }

        workspace.Layouts.Add(CloneLayout(source, target));
    }

    public static void DeleteLayout(Workspace workspace, string name)
    {
        var layout = FindLayout(workspace, name) ?? throw new InvalidOperationException($"Layout '{name}' was not found.");
        if (workspace.Layouts.Count <= 1)
        {
            throw new InvalidOperationException("Cannot delete the only layout.");
        }

        workspace.Layouts.Remove(layout);
        if (string.Equals(workspace.ActiveLayoutName, layout.Name, StringComparison.OrdinalIgnoreCase))
        {
            workspace.ActiveLayoutName = workspace.Layouts[0].Name;
            ApplyActiveLayoutToZones(workspace);
        }
    }

    public static void SetDockVisibility(Workspace workspace, string dockId, bool isVisible)
    {
        var zone = FindZone(workspace, dockId);
        var state = EnsureDockState(EnsureActiveLayout(workspace), zone);
        state.IsVisible = isVisible;
        zone.IsVisible = isVisible;
    }

    public static void SetDockBounds(Workspace workspace, string dockId, double x, double y, double width, double height)
    {
        var zone = FindZone(workspace, dockId);
        zone.Bounds = new ZoneBounds
        {
            X = x,
            Y = y,
            Width = Math.Max(120, width),
            Height = Math.Max(54, height)
        };

        CaptureZoneState(workspace, zone, null);
    }

    public static void AddOrShowItem(Workspace workspace, string path, string dockId, string? tabId, int? order = null, string? displayName = null)
    {
        var normalized = NormalizePath(path);
        var zone = FindZone(workspace, dockId);
        var resolvedTabId = ResolveTabId(zone, tabId);
        var layout = EnsureActiveLayout(workspace);
        var existing = FindItemOverride(layout, normalized, zone.Id, resolvedTabId);
        if (existing is null)
        {
            existing = new DockItemOverride
            {
                Path = normalized,
                DockId = zone.Id,
                TabId = resolvedTabId
            };
            layout.ItemOverrides.Add(existing);
        }

        existing.IsHidden = false;
        existing.Order = order ?? GetNextOrder(layout, zone.Id, resolvedTabId);
        existing.DisplayName = string.IsNullOrWhiteSpace(displayName) ? existing.DisplayName : displayName.Trim();
    }

    public static void HideItemInDock(Workspace workspace, string path, string dockId, string? tabId)
    {
        var normalized = NormalizePath(path);
        var zone = FindZone(workspace, dockId);
        var resolvedTabId = ResolveTabId(zone, tabId);
        var layout = EnsureActiveLayout(workspace);
        var existing = FindItemOverride(layout, normalized, zone.Id, resolvedTabId);
        if (existing is null)
        {
            existing = new DockItemOverride
            {
                Path = normalized,
                DockId = zone.Id,
                TabId = resolvedTabId,
                Order = GetNextOrder(layout, zone.Id, resolvedTabId)
            };
            layout.ItemOverrides.Add(existing);
        }

        existing.IsHidden = true;
    }

    public static void MoveItem(Workspace workspace, string path, string fromDockId, string? fromTabId, string toDockId, string? toTabId, int? order = null)
    {
        HideItemInDock(workspace, path, fromDockId, fromTabId);
        AddOrShowItem(workspace, path, toDockId, toTabId, order);
    }

    public static void SetItemOrder(Workspace workspace, string dockId, string? tabId, IReadOnlyList<string> orderedPaths)
    {
        var zone = FindZone(workspace, dockId);
        var resolvedTabId = ResolveTabId(zone, tabId);
        var layout = EnsureActiveLayout(workspace);

        for (var i = 0; i < orderedPaths.Count; i++)
        {
            var normalized = NormalizePath(orderedPaths[i]);
            var existing = FindItemOverride(layout, normalized, zone.Id, resolvedTabId);
            if (existing is null)
            {
                existing = new DockItemOverride
                {
                    Path = normalized,
                    DockId = zone.Id,
                    TabId = resolvedTabId
                };
                layout.ItemOverrides.Add(existing);
            }

            existing.Order = i;
            existing.IsHidden = false;
        }
    }

    public static IReadOnlyList<DockItemOverride> GetOverrides(LayoutProfile layout, string dockId, string? tabId)
    {
        return layout.ItemOverrides
            .Where(item => string.Equals(item.DockId, dockId, StringComparison.OrdinalIgnoreCase))
            .Where(item => string.IsNullOrWhiteSpace(item.TabId) || string.IsNullOrWhiteSpace(tabId) ||
                           string.Equals(item.TabId, tabId, StringComparison.OrdinalIgnoreCase))
            .OrderBy(item => item.Order)
            .ThenBy(item => item.Path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public static int? GetItemOrder(LayoutProfile layout, string dockId, string? tabId, string path)
    {
        var normalized = NormalizePath(path);
        var item = GetOverrides(layout, dockId, tabId)
            .FirstOrDefault(candidate => PathsEqual(candidate.Path, normalized) && !candidate.IsHidden);
        return item?.Order;
    }

    public static bool IsHidden(LayoutProfile layout, string dockId, string? tabId, string path)
    {
        var normalized = NormalizePath(path);
        return GetOverrides(layout, dockId, tabId)
            .Any(candidate => candidate.IsHidden && PathsEqual(candidate.Path, normalized));
    }

    public static DesktopPinDefinition AddDesktopPin(Workspace workspace, string path, double x, double y, double iconSize = 52, string? displayName = null)
    {
        var layout = EnsureActiveLayout(workspace);
        var normalized = NormalizePath(path);
        var existing = layout.DesktopPins.FirstOrDefault(pin => PathsEqual(pin.Path, normalized));
        if (existing is null)
        {
            existing = new DesktopPinDefinition
            {
                Id = Guid.NewGuid().ToString("N"),
                Path = normalized
            };
            layout.DesktopPins.Add(existing);
        }

        existing.X = x;
        existing.Y = y;
        existing.IconSize = Math.Clamp(iconSize, 24, 128);
        existing.DisplayName = string.IsNullOrWhiteSpace(displayName) ? existing.DisplayName : displayName.Trim();
        return existing;
    }

    public static void RemoveDesktopPin(Workspace workspace, string pathOrId)
    {
        var layout = EnsureActiveLayout(workspace);
        var normalized = NormalizePath(pathOrId);
        var pin = layout.DesktopPins.FirstOrDefault(candidate =>
            string.Equals(candidate.Id, pathOrId, StringComparison.OrdinalIgnoreCase) ||
            PathsEqual(candidate.Path, normalized));
        if (pin is not null)
        {
            layout.DesktopPins.Remove(pin);
        }
    }

    public static IReadOnlyList<string> Validate(Workspace workspace)
    {
        var errors = new List<string>();
        if (workspace.Zones.Count == 0)
        {
            errors.Add("Workspace has no docks.");
        }

        var layoutNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var layout in workspace.Layouts)
        {
            if (string.IsNullOrWhiteSpace(layout.Name))
            {
                errors.Add("A layout has an empty name.");
                continue;
            }

            if (!layoutNames.Add(layout.Name))
            {
                errors.Add($"Duplicate layout name: {layout.Name}");
            }
        }

        var zoneIds = workspace.Zones.Select(zone => zone.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var layout in workspace.Layouts)
        {
            foreach (var state in layout.DockStates)
            {
                if (!zoneIds.Contains(state.DockId))
                {
                    errors.Add($"Layout '{layout.Name}' references missing dock '{state.DockId}'.");
                }
            }

            foreach (var item in layout.ItemOverrides)
            {
                if (!zoneIds.Contains(item.DockId))
                {
                    errors.Add($"Layout '{layout.Name}' item '{item.Path}' references missing dock '{item.DockId}'.");
                }
            }
        }

        if (FindLayout(workspace, workspace.ActiveLayoutName) is null)
        {
            errors.Add($"Active layout '{workspace.ActiveLayoutName}' does not exist.");
        }

        return errors;
    }

    public static string NormalizePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        var expanded = PathExpander.Expand(Environment.ExpandEnvironmentVariables(path.Trim()));
        try
        {
            return Path.GetFullPath(expanded);
        }
        catch
        {
            return expanded;
        }
    }

    public static bool PathsEqual(string left, string right)
    {
        return string.Equals(NormalizePath(left), NormalizePath(right), StringComparison.OrdinalIgnoreCase);
    }

    private static void CaptureZoneState(LayoutProfile layout, ZoneDefinition zone, string? activeTabId)
    {
        var state = EnsureDockState(layout, zone);
        state.IsVisible = zone.IsVisible;
        state.IsLocked = zone.IsLocked;
        state.IsCollapsed = zone.IsCollapsed;
        state.Bounds = CloneBounds(zone.Bounds);
        if (!string.IsNullOrWhiteSpace(activeTabId))
        {
            state.ActiveTabId = activeTabId;
        }
    }

    private static DockLayoutState EnsureDockState(LayoutProfile layout, ZoneDefinition zone)
    {
        var state = layout.DockStates.FirstOrDefault(candidate =>
            string.Equals(candidate.DockId, zone.Id, StringComparison.OrdinalIgnoreCase));
        if (state is not null)
        {
            return state;
        }

        state = CreateDockState(zone);
        layout.DockStates.Add(state);
        return state;
    }

    private static void EnsureDockStates(LayoutProfile layout, IEnumerable<ZoneDefinition> zones)
    {
        foreach (var zone in zones)
        {
            EnsureDockState(layout, zone);
        }
    }

    private static DockLayoutState CreateDockState(ZoneDefinition zone)
    {
        return new DockLayoutState
        {
            DockId = zone.Id,
            IsVisible = zone.IsVisible,
            IsLocked = zone.IsLocked,
            IsCollapsed = zone.IsCollapsed,
            ActiveTabId = zone.Tabs.FirstOrDefault()?.Id,
            Bounds = CloneBounds(zone.Bounds)
        };
    }

    private static ZoneDefinition FindZone(Workspace workspace, string dockIdOrName)
    {
        return workspace.Zones.FirstOrDefault(zone =>
                   string.Equals(zone.Id, dockIdOrName, StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(zone.Name, dockIdOrName, StringComparison.OrdinalIgnoreCase))
               ?? throw new InvalidOperationException($"Dock '{dockIdOrName}' was not found.");
    }

    private static LayoutProfile? FindLayout(Workspace workspace, string name)
    {
        return workspace.Layouts.FirstOrDefault(layout =>
            string.Equals(layout.Name, name, StringComparison.OrdinalIgnoreCase));
    }

    private static string ResolveTabId(ZoneDefinition zone, string? tabIdOrName)
    {
        if (!string.IsNullOrWhiteSpace(tabIdOrName))
        {
            var tab = zone.Tabs.FirstOrDefault(candidate =>
                string.Equals(candidate.Id, tabIdOrName, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(candidate.Name, tabIdOrName, StringComparison.OrdinalIgnoreCase));
            if (tab is not null)
            {
                return tab.Id;
            }
        }

        return zone.Tabs.FirstOrDefault()?.Id ?? string.Empty;
    }

    private static DockItemOverride? FindItemOverride(LayoutProfile layout, string normalizedPath, string dockId, string? tabId)
    {
        return GetOverrides(layout, dockId, tabId)
            .FirstOrDefault(candidate => PathsEqual(candidate.Path, normalizedPath));
    }

    private static int GetNextOrder(LayoutProfile layout, string dockId, string? tabId)
    {
        var matching = GetOverrides(layout, dockId, tabId);
        return matching.Count == 0 ? 0 : matching.Max(item => item.Order) + 1;
    }

    private static void ReplaceOrAddLayout(Workspace workspace, LayoutProfile layout)
    {
        var existing = FindLayout(workspace, layout.Name);
        if (existing is not null)
        {
            var index = workspace.Layouts.IndexOf(existing);
            workspace.Layouts[index] = layout;
        }
        else
        {
            workspace.Layouts.Add(layout);
        }
    }

    private static LayoutProfile CloneLayout(LayoutProfile source, string name)
    {
        return new LayoutProfile
        {
            Name = NormalizeLayoutName(name),
            HideDesktopIconsWhenRunning = source.HideDesktopIconsWhenRunning,
            DockStates = source.DockStates.Select(CloneDockState).ToList(),
            ItemOverrides = source.ItemOverrides.Select(item => new DockItemOverride
            {
                Path = item.Path,
                DockId = item.DockId,
                TabId = item.TabId,
                Order = item.Order,
                IsHidden = item.IsHidden,
                DisplayName = item.DisplayName
            }).ToList(),
            DesktopPins = source.DesktopPins.Select(pin => new DesktopPinDefinition
            {
                Id = Guid.NewGuid().ToString("N"),
                Path = pin.Path,
                DisplayName = pin.DisplayName,
                X = pin.X,
                Y = pin.Y,
                IconSize = pin.IconSize
            }).ToList()
        };
    }

    private static DockLayoutState CloneDockState(DockLayoutState source)
    {
        return new DockLayoutState
        {
            DockId = source.DockId,
            IsVisible = source.IsVisible,
            IsLocked = source.IsLocked,
            IsCollapsed = source.IsCollapsed,
            ActiveTabId = source.ActiveTabId,
            Bounds = CloneBounds(source.Bounds)
        };
    }

    private static ZoneBounds CloneBounds(ZoneBounds bounds)
    {
        return new ZoneBounds
        {
            X = bounds.X,
            Y = bounds.Y,
            Width = bounds.Width,
            Height = bounds.Height
        };
    }

    private static string NormalizeLayoutName(string name)
    {
        var trimmed = string.IsNullOrWhiteSpace(name) ? DefaultLayoutName : name.Trim();
        return trimmed.Length == 0 ? DefaultLayoutName : trimmed;
    }
}
