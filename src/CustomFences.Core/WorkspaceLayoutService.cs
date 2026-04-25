using System.Security.Cryptography;
using System.Text;

namespace CustomFences.Core;

public static class WorkspaceLayoutService
{
    public const string DefaultLayoutName = "Main";
    public const string DefaultDisplayVariantKey = "default";
    public const double MinimumDockWidth = 120;
    public const double MinimumDockHeight = 54;
    public const double DefaultRestoredDockWidth = 390;
    public const double DefaultRestoredDockHeight = 320;
    public const double DockWorkAreaMargin = 12;
    public const double DockMaxWorkAreaRatio = 0.72;
    public const double DockSnapRestoreRatio = 0.92;

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

        EnsureDisplayVariants(layout, workspace.Zones);
        return layout;
    }

    public static DisplayLayoutVariant EnsureActiveDisplayVariant(Workspace workspace)
    {
        var layout = EnsureActiveLayout(workspace);
        EnsureDisplayVariants(layout, workspace.Zones);
        var key = string.IsNullOrWhiteSpace(layout.ActiveDisplayVariantKey)
            ? DefaultDisplayVariantKey
            : layout.ActiveDisplayVariantKey;
        var variant = layout.DisplayVariants.FirstOrDefault(candidate =>
            string.Equals(candidate.Key, key, StringComparison.OrdinalIgnoreCase));

        if (variant is not null)
        {
            EnsureDockStates(variant.DockStates, workspace.Zones);
            return variant;
        }

        variant = layout.DisplayVariants.First(candidate => candidate.IsDefault);
        layout.ActiveDisplayVariantKey = variant.Key;
        EnsureDockStates(variant.DockStates, workspace.Zones);
        return variant;
    }

    public static LayoutProfile CreateProfileFromZones(string name, Workspace workspace)
    {
        var profile = new LayoutProfile
        {
            Name = string.IsNullOrWhiteSpace(name) ? DefaultLayoutName : name.Trim(),
            ActiveDisplayVariantKey = DefaultDisplayVariantKey,
            HideDesktopIconsWhenRunning = workspace.Settings.HideDesktopIconsWhenRunning
        };

        var variant = new DisplayLayoutVariant
        {
            Key = DefaultDisplayVariantKey,
            DisplaySignature = DefaultDisplayVariantKey,
            IsDefault = true
        };

        foreach (var zone in workspace.Zones)
        {
            variant.DockStates.Add(CreateDockState(zone));
        }

        profile.DisplayVariants.Add(variant);
        return profile;
    }

    public static void ApplyActiveLayoutToZones(Workspace workspace)
    {
        var layout = EnsureActiveLayout(workspace);
        var variant = EnsureActiveDisplayVariant(workspace);
        workspace.Settings.HideDesktopIconsWhenRunning = layout.HideDesktopIconsWhenRunning;

        foreach (var zone in workspace.Zones)
        {
            var state = EnsureDockState(variant.DockStates, zone);
            zone.IsVisible = state.IsVisible;
            zone.IsLocked = state.IsLocked;
            zone.IsCollapsed = state.IsCollapsed;
            zone.Bounds = CloneBounds(state.Bounds);
        }
    }

    public static DisplayLayoutVariant UseDisplayVariant(
        Workspace workspace,
        string key,
        string displaySignature,
        IReadOnlyList<DisplayDescriptor> displays)
    {
        var layout = EnsureActiveLayout(workspace);
        EnsureDisplayVariants(layout, workspace.Zones);
        var normalizedKey = string.IsNullOrWhiteSpace(key) ? DefaultDisplayVariantKey : key.Trim();
        var variant = layout.DisplayVariants.FirstOrDefault(candidate =>
            string.Equals(candidate.Key, normalizedKey, StringComparison.OrdinalIgnoreCase));

        if (variant is null)
        {
            var source = layout.DisplayVariants.First(candidate => candidate.IsDefault);
            variant = CloneDisplayVariant(source, normalizedKey, displaySignature, isDefault: false);
            layout.DisplayVariants.Add(variant);
        }

        variant.DisplaySignature = string.IsNullOrWhiteSpace(displaySignature) ? normalizedKey : displaySignature;
        variant.LastSeenUtc = DateTime.UtcNow;
        layout.ActiveDisplayVariantKey = variant.Key;
        EnsureDockStates(variant.DockStates, workspace.Zones);
        ClampVariantToDisplays(variant, displays);
        ApplyActiveLayoutToZones(workspace);
        return variant;
    }

    public static DisplayLayoutVariant UseDefaultDisplayVariant(Workspace workspace)
    {
        var layout = EnsureActiveLayout(workspace);
        var variant = layout.DisplayVariants.First(candidate => candidate.IsDefault);
        layout.ActiveDisplayVariantKey = variant.Key;
        ApplyActiveLayoutToZones(workspace);
        return variant;
    }

    public static string ComputeDisplaySignature(IEnumerable<DisplayDescriptor> displays)
    {
        var parts = displays
            .OrderByDescending(display => display.IsPrimary)
            .ThenBy(display => display.DeviceName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(display => display.BoundsX)
            .Select(display =>
                $"{display.DeviceName}|primary={display.IsPrimary}|b={display.BoundsX:0},{display.BoundsY:0},{display.BoundsWidth:0},{display.BoundsHeight:0}|w={display.WorkAreaX:0},{display.WorkAreaY:0},{display.WorkAreaWidth:0},{display.WorkAreaHeight:0}");

        var signature = string.Join(";", parts);
        return string.IsNullOrWhiteSpace(signature) ? DefaultDisplayVariantKey : signature;
    }

    public static string ComputeDisplayVariantKey(string displaySignature)
    {
        if (string.IsNullOrWhiteSpace(displaySignature) ||
            string.Equals(displaySignature, DefaultDisplayVariantKey, StringComparison.OrdinalIgnoreCase))
        {
            return DefaultDisplayVariantKey;
        }

        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(displaySignature));
        return "display-" + Convert.ToHexString(bytes, 0, 8).ToLowerInvariant();
    }

    public static void CaptureAllZoneStates(Workspace workspace)
    {
        var layout = EnsureActiveLayout(workspace);
        var variant = EnsureActiveDisplayVariant(workspace);
        layout.HideDesktopIconsWhenRunning = workspace.Settings.HideDesktopIconsWhenRunning;

        foreach (var zone in workspace.Zones)
        {
            CaptureZoneState(variant, zone, null);
        }
    }

    public static void CaptureZoneState(Workspace workspace, ZoneDefinition zone, string? activeTabId)
    {
        var variant = EnsureActiveDisplayVariant(workspace);
        CaptureZoneState(variant, zone, activeTabId);
    }

    public static void CaptureActiveTab(Workspace workspace, ZoneDefinition zone, string? activeTabId)
    {
        var variant = EnsureActiveDisplayVariant(workspace);
        var state = EnsureDockState(variant.DockStates, zone);
        state.ActiveTabId = activeTabId;
    }

    public static string? GetActiveTabId(Workspace workspace, ZoneDefinition zone)
    {
        var variant = EnsureActiveDisplayVariant(workspace);
        return EnsureDockState(variant.DockStates, zone).ActiveTabId;
    }

    public static DockExpansionEdge GetExpansionEdge(Workspace workspace, ZoneDefinition zone)
    {
        var variant = EnsureActiveDisplayVariant(workspace);
        return EnsureDockState(variant.DockStates, zone).ExpansionEdge;
    }

    public static void SetExpansionEdge(Workspace workspace, string dockId, DockExpansionEdge edge)
    {
        var zone = FindZone(workspace, dockId);
        var state = EnsureDockState(EnsureActiveDisplayVariant(workspace).DockStates, zone);
        state.ExpansionEdge = edge;
    }

    public static void SaveCurrentLayoutAs(Workspace workspace, string name)
    {
        var layoutName = NormalizeLayoutName(name);
        var source = CloneLayout(EnsureActiveLayout(workspace), layoutName);
        source.ActiveDisplayVariantKey = EnsureActiveDisplayVariant(workspace).Key;
        ReplaceOrAddLayout(workspace, source);
        workspace.ActiveLayoutName = layoutName;
        CaptureAllZoneStates(workspace);
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
        var state = EnsureDockState(EnsureActiveDisplayVariant(workspace).DockStates, zone);
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
            Width = Math.Max(MinimumDockWidth, width),
            Height = Math.Max(MinimumDockHeight, height)
        };

        CaptureZoneState(workspace, zone, null);
    }

    public static bool RestoreDockBounds(Workspace workspace, string dockId, IReadOnlyList<DisplayDescriptor> displays)
    {
        var zone = FindZone(workspace, dockId);
        var variant = EnsureActiveDisplayVariant(workspace);
        var state = EnsureDockState(variant.DockStates, zone);
        var display = FindBestDisplay(displays, state.Bounds);
        var restored = CreateRestoredDockBounds(state.Bounds, display);

        zone.Bounds = CloneBounds(restored);
        zone.IsCollapsed = false;
        state.Bounds = CloneBounds(restored);
        state.IsCollapsed = false;
        return true;
    }

    public static int RepairOversizedDockBounds(Workspace workspace, IReadOnlyList<DisplayDescriptor> displays)
    {
        var changed = 0;
        var layout = EnsureActiveLayout(workspace);
        foreach (var variant in layout.DisplayVariants)
        {
            if (ClampVariantToDisplays(variant, displays))
            {
                changed++;
            }
        }

        ApplyActiveLayoutToZones(workspace);
        return changed;
    }

    public static ZoneDefinition EnsureMusicDock(Workspace workspace)
    {
        var zone = workspace.Zones.FirstOrDefault(candidate =>
            candidate.Kind == ZoneKind.Music ||
            string.Equals(candidate.Id, "music", StringComparison.OrdinalIgnoreCase));
        if (zone is null)
        {
            zone = new ZoneDefinition
            {
                Id = "music",
                Name = "Orbit Radio",
                Kind = ZoneKind.Music,
                IsVisible = false,
                Bounds = new ZoneBounds { X = 980, Y = 640, Width = 420, Height = 300 },
                Appearance = new ZoneAppearance
                {
                    AccentColor = "#7DDCFF",
                    BackgroundColor = "#070D16",
                    Opacity = 0.80,
                    CornerRadius = 24,
                    IconSize = 44
                },
                Tabs = []
            };
            workspace.Zones.Add(zone);
        }

        var layout = EnsureActiveLayout(workspace);
        foreach (var variant in layout.DisplayVariants)
        {
            EnsureDockState(variant.DockStates, zone);
        }

        return zone;
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
        var variant = EnsureActiveDisplayVariant(workspace);
        var normalized = NormalizePath(path);
        var existing = variant.DesktopPins.FirstOrDefault(pin => PathsEqual(pin.Path, normalized));
        if (existing is null)
        {
            existing = new DesktopPinDefinition
            {
                Id = Guid.NewGuid().ToString("N"),
                Path = normalized
            };
            variant.DesktopPins.Add(existing);
        }

        existing.X = x;
        existing.Y = y;
        existing.IconSize = Math.Clamp(iconSize, 24, 128);
        existing.DisplayName = string.IsNullOrWhiteSpace(displayName) ? existing.DisplayName : displayName.Trim();
        return existing;
    }

    public static void RemoveDesktopPin(Workspace workspace, string pathOrId)
    {
        var variant = EnsureActiveDisplayVariant(workspace);
        var normalized = NormalizePath(pathOrId);
        var pin = variant.DesktopPins.FirstOrDefault(candidate =>
            string.Equals(candidate.Id, pathOrId, StringComparison.OrdinalIgnoreCase) ||
            PathsEqual(candidate.Path, normalized));
        if (pin is not null)
        {
            variant.DesktopPins.Remove(pin);
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

            if (layout.DisplayVariants.Count == 0)
            {
                errors.Add($"Layout '{layout.Name}' has no display variants.");
            }
        }

        var zoneIds = workspace.Zones.Select(zone => zone.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var layout in workspace.Layouts)
        {
            foreach (var variant in layout.DisplayVariants)
            {
                if (string.IsNullOrWhiteSpace(variant.Key))
                {
                    errors.Add($"Layout '{layout.Name}' has an empty display variant key.");
                }

                foreach (var state in variant.DockStates)
                {
                    if (!zoneIds.Contains(state.DockId))
                    {
                        errors.Add($"Layout '{layout.Name}' variant '{variant.Key}' references missing dock '{state.DockId}'.");
                    }
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

    private static void EnsureDisplayVariants(LayoutProfile layout, IEnumerable<ZoneDefinition> zones)
    {
        if (layout.DisplayVariants.Count == 0)
        {
            layout.DisplayVariants.Add(new DisplayLayoutVariant
            {
                Key = DefaultDisplayVariantKey,
                DisplaySignature = DefaultDisplayVariantKey,
                IsDefault = true,
                DockStates = layout.DockStates.Select(CloneDockState).ToList(),
                DesktopPins = layout.DesktopPins.Select(CloneDesktopPinPreservingId).ToList()
            });
        }

        if (!layout.DisplayVariants.Any(variant => variant.IsDefault))
        {
            layout.DisplayVariants[0].IsDefault = true;
            layout.DisplayVariants[0].Key = string.IsNullOrWhiteSpace(layout.DisplayVariants[0].Key)
                ? DefaultDisplayVariantKey
                : layout.DisplayVariants[0].Key;
        }

        foreach (var variant in layout.DisplayVariants)
        {
            EnsureDockStates(variant.DockStates, zones);
        }
    }

    private static void CaptureZoneState(DisplayLayoutVariant variant, ZoneDefinition zone, string? activeTabId)
    {
        var state = EnsureDockState(variant.DockStates, zone);
        state.IsVisible = zone.IsVisible;
        state.IsLocked = zone.IsLocked;
        state.IsCollapsed = zone.IsCollapsed;
        state.Bounds = CloneBounds(zone.Bounds);
        if (!string.IsNullOrWhiteSpace(activeTabId))
        {
            state.ActiveTabId = activeTabId;
        }
    }

    private static DockLayoutState EnsureDockState(List<DockLayoutState> dockStates, ZoneDefinition zone)
    {
        var state = dockStates.FirstOrDefault(candidate =>
            string.Equals(candidate.DockId, zone.Id, StringComparison.OrdinalIgnoreCase));
        if (state is not null)
        {
            return state;
        }

        state = CreateDockState(zone);
        dockStates.Add(state);
        return state;
    }

    private static void EnsureDockStates(List<DockLayoutState> dockStates, IEnumerable<ZoneDefinition> zones)
    {
        foreach (var zone in zones)
        {
            EnsureDockState(dockStates, zone);
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
            ExpansionEdge = DockExpansionEdge.Top,
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
            ActiveDisplayVariantKey = source.ActiveDisplayVariantKey,
            HideDesktopIconsWhenRunning = source.HideDesktopIconsWhenRunning,
            DisplayVariants = source.DisplayVariants.Select(variant => CloneDisplayVariant(variant, variant.Key, variant.DisplaySignature, variant.IsDefault)).ToList(),
            ItemOverrides = source.ItemOverrides.Select(item => new DockItemOverride
            {
                Path = item.Path,
                DockId = item.DockId,
                TabId = item.TabId,
                Order = item.Order,
                IsHidden = item.IsHidden,
                DisplayName = item.DisplayName
            }).ToList()
        };
    }

    private static DisplayLayoutVariant CloneDisplayVariant(DisplayLayoutVariant source, string key, string signature, bool isDefault)
    {
        return new DisplayLayoutVariant
        {
            Key = key,
            DisplaySignature = signature,
            IsDefault = isDefault,
            LastSeenUtc = DateTime.UtcNow,
            DockStates = source.DockStates.Select(CloneDockState).ToList(),
            DesktopPins = source.DesktopPins.Select(pin => new DesktopPinDefinition
            {
                Id = Guid.NewGuid().ToString("N"),
                Path = pin.Path,
                DisplayName = pin.DisplayName,
                X = pin.X,
                Y = pin.Y,
                IconSize = pin.IconSize
            }).ToList(),
            Music = new MusicDockState
            {
                SelectedPlaylist = source.Music.SelectedPlaylist,
                SelectedTrackPath = source.Music.SelectedTrackPath,
                Shuffle = source.Music.Shuffle,
                Repeat = source.Music.Repeat,
                Volume = source.Music.Volume
            }
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
            ExpansionEdge = source.ExpansionEdge,
            ActiveTabId = source.ActiveTabId,
            Bounds = CloneBounds(source.Bounds)
        };
    }

    private static DesktopPinDefinition CloneDesktopPinPreservingId(DesktopPinDefinition source)
    {
        return new DesktopPinDefinition
        {
            Id = source.Id,
            Path = source.Path,
            DisplayName = source.DisplayName,
            X = source.X,
            Y = source.Y,
            IconSize = source.IconSize
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

    private static bool ClampVariantToDisplays(DisplayLayoutVariant variant, IReadOnlyList<DisplayDescriptor> displays)
    {
        if (displays.Count == 0)
        {
            return false;
        }

        var minX = displays.Min(display => display.WorkAreaX);
        var minY = displays.Min(display => display.WorkAreaY);
        var maxX = displays.Max(display => display.WorkAreaX + display.WorkAreaWidth);
        var maxY = displays.Max(display => display.WorkAreaY + display.WorkAreaHeight);
        var changed = false;

        foreach (var state in variant.DockStates)
        {
            var display = FindBestDisplay(displays, state.Bounds);
            var normalized = NormalizeDockBoundsForDisplay(state.Bounds, display, restoreSnapSizedDock: true);
            if (!BoundsEqual(state.Bounds, normalized))
            {
                state.Bounds = normalized;
                changed = true;
            }
        }

        foreach (var pin in variant.DesktopPins)
        {
            var x = Math.Clamp(pin.X, minX, Math.Max(minX, maxX - pin.IconSize));
            var y = Math.Clamp(pin.Y, minY, Math.Max(minY, maxY - pin.IconSize));
            if (Math.Abs(pin.X - x) > 0.1 || Math.Abs(pin.Y - y) > 0.1)
            {
                pin.X = x;
                pin.Y = y;
                changed = true;
            }
        }

        return changed;
    }

    private static ZoneBounds NormalizeDockBoundsForDisplay(
        ZoneBounds source,
        DisplayDescriptor display,
        bool restoreSnapSizedDock)
    {
        var maxWidth = GetMaximumDockWidth(display);
        var maxHeight = GetMaximumDockHeight(display);
        var snapSized = IsSnapSized(source, display);
        var width = restoreSnapSizedDock && snapSized
            ? Math.Min(DefaultRestoredDockWidth, maxWidth)
            : Math.Clamp(source.Width, MinimumDockWidth, maxWidth);
        var height = restoreSnapSizedDock && snapSized
            ? Math.Min(DefaultRestoredDockHeight, maxHeight)
            : Math.Clamp(source.Height, MinimumDockHeight, maxHeight);

        return new ZoneBounds
        {
            X = Math.Clamp(
                source.X,
                display.WorkAreaX + DockWorkAreaMargin,
                Math.Max(display.WorkAreaX + DockWorkAreaMargin, display.WorkAreaX + display.WorkAreaWidth - width - DockWorkAreaMargin)),
            Y = Math.Clamp(
                source.Y,
                display.WorkAreaY + DockWorkAreaMargin,
                Math.Max(display.WorkAreaY + DockWorkAreaMargin, display.WorkAreaY + display.WorkAreaHeight - height - DockWorkAreaMargin)),
            Width = width,
            Height = height
        };
    }

    private static ZoneBounds CreateRestoredDockBounds(ZoneBounds source, DisplayDescriptor display)
    {
        var width = Math.Min(DefaultRestoredDockWidth, GetMaximumDockWidth(display));
        var height = Math.Min(DefaultRestoredDockHeight, GetMaximumDockHeight(display));
        return new ZoneBounds
        {
            X = Math.Clamp(
                source.X,
                display.WorkAreaX + DockWorkAreaMargin,
                Math.Max(display.WorkAreaX + DockWorkAreaMargin, display.WorkAreaX + display.WorkAreaWidth - width - DockWorkAreaMargin)),
            Y = Math.Clamp(
                source.Y,
                display.WorkAreaY + DockWorkAreaMargin,
                Math.Max(display.WorkAreaY + DockWorkAreaMargin, display.WorkAreaY + display.WorkAreaHeight - height - DockWorkAreaMargin)),
            Width = width,
            Height = height
        };
    }

    private static double GetMaximumDockWidth(DisplayDescriptor display)
    {
        return Math.Max(
            MinimumDockWidth,
            Math.Min(display.WorkAreaWidth - DockWorkAreaMargin * 2, display.WorkAreaWidth * DockMaxWorkAreaRatio));
    }

    private static double GetMaximumDockHeight(DisplayDescriptor display)
    {
        return Math.Max(
            MinimumDockHeight,
            Math.Min(display.WorkAreaHeight - DockWorkAreaMargin * 2, display.WorkAreaHeight * DockMaxWorkAreaRatio));
    }

    private static bool IsSnapSized(ZoneBounds bounds, DisplayDescriptor display)
    {
        return bounds.Width >= display.WorkAreaWidth * DockSnapRestoreRatio ||
               bounds.Height >= display.WorkAreaHeight * DockSnapRestoreRatio;
    }

    private static DisplayDescriptor FindBestDisplay(IReadOnlyList<DisplayDescriptor> displays, ZoneBounds bounds)
    {
        if (displays.Count == 0)
        {
            return new DisplayDescriptor
            {
                WorkAreaX = 0,
                WorkAreaY = 0,
                WorkAreaWidth = 1920,
                WorkAreaHeight = 1040,
                BoundsWidth = 1920,
                BoundsHeight = 1080
            };
        }

        return displays
            .OrderByDescending(display => IntersectionArea(bounds, display))
            .ThenBy(display => DistanceFromDisplayCenter(bounds, display))
            .First();
    }

    private static double IntersectionArea(ZoneBounds bounds, DisplayDescriptor display)
    {
        var left = Math.Max(bounds.X, display.WorkAreaX);
        var right = Math.Min(bounds.X + bounds.Width, display.WorkAreaX + display.WorkAreaWidth);
        var top = Math.Max(bounds.Y, display.WorkAreaY);
        var bottom = Math.Min(bounds.Y + bounds.Height, display.WorkAreaY + display.WorkAreaHeight);
        return Math.Max(0, right - left) * Math.Max(0, bottom - top);
    }

    private static double DistanceFromDisplayCenter(ZoneBounds bounds, DisplayDescriptor display)
    {
        var boundsCenterX = bounds.X + bounds.Width / 2;
        var boundsCenterY = bounds.Y + bounds.Height / 2;
        var displayCenterX = display.WorkAreaX + display.WorkAreaWidth / 2;
        var displayCenterY = display.WorkAreaY + display.WorkAreaHeight / 2;
        var dx = boundsCenterX - displayCenterX;
        var dy = boundsCenterY - displayCenterY;
        return dx * dx + dy * dy;
    }

    private static bool BoundsEqual(ZoneBounds left, ZoneBounds right)
    {
        return Math.Abs(left.X - right.X) < 0.1 &&
               Math.Abs(left.Y - right.Y) < 0.1 &&
               Math.Abs(left.Width - right.Width) < 0.1 &&
               Math.Abs(left.Height - right.Height) < 0.1;
    }
}
