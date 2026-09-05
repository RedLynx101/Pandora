namespace Pandora.Core;

/// <summary>Validate the runtime model before migration or UI code dereferences external JSON.</summary>
public static class WorkspaceValidation
{
    public static IReadOnlyList<string> Validate(Workspace workspace)
    {
        var errors = new List<string>();
        if (workspace is null) return ["Workspace must be a JSON object, not null."];
        if (workspace.SchemaVersion < 1 || workspace.SchemaVersion > WorkspaceMigrator.CurrentSchemaVersion)
            errors.Add($"Unsupported workspace schemaVersion {workspace.SchemaVersion}. Supported versions are 1 through {WorkspaceMigrator.CurrentSchemaVersion}; the file was not changed.");

        if (workspace.Settings is not { } settings) errors.Add("settings must be an object.");
        else
        {
            Number(settings.GlassOpacity, "settings.glassOpacity", 0, 1);
            EnumValue(settings.DefaultDropAction, "settings.defaultDropAction");
            // Missing audio, structure and bar settings have existing migration defaults.
            if (settings.Audio is { } audio) Number(audio.SoundEffectsVolume, "settings.audio.soundEffectsVolume", 0, 1);
        }

        var zoneIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var zone in RequiredItems(workspace.Zones, "zones"))
        {
            var label = $"Dock '{zone.Id}'";
            UniqueId(zone.Id, zoneIds, "Dock ID");
            EnumValue(zone.Kind, label + " kind");
            EnumValue(zone.Sort, label + " sort");
            Bounds(zone.Bounds, label + " bounds");
            if (zone.Appearance is not { } appearance) errors.Add(label + " appearance must be an object.");
            else
            {
                Number(appearance.Opacity, label + " opacity", 0, 1);
                Number(appearance.CornerRadius, label + " cornerRadius", 0);
                Number(appearance.IconSize, label + " iconSize", double.Epsilon);
                if (appearance.Columns < 1) errors.Add(label + " columns must be positive.");
                EnumValue(appearance.TabStyle, label + " tabStyle");
                EnumValue(appearance.HeaderIcon, label + " headerIcon");
            }
            var tabIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var tab in RequiredItems(zone.Tabs, label + " tabs"))
            {
                UniqueId(tab.Id, tabIds, label + " tab ID");
                EnumValue(tab.Source, label + " tab source");
                EnumValue(tab.DesktopGroup, label + " desktop group");
            }
            if (zone.AgentFeed is not { } feed) errors.Add(label + " agentFeed must be an object.");
            else
            {
                EnumValue(feed.DisplayMode, label + " feed display mode");
                foreach (var id in RequiredItems(feed.FeedIds, label + " feed IDs"))
                    if (string.IsNullOrWhiteSpace(id)) errors.Add(label + " feed IDs cannot be blank.");
            }
        }

        foreach (var rule in RequiredItems(workspace.Rules, "rules"))
            foreach (var condition in RequiredItems(rule.Conditions, $"Rule '{rule.Id}' conditions"))
            {
                EnumValue(condition.Field, "Rule condition field");
                EnumValue(condition.Match, "Rule condition match");
            }

        var layoutNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var layout in RequiredItems(workspace.Layouts, "layouts"))
        {
            UniqueId(layout.Name, layoutNames, "Layout name");
            var label = $"Layout '{layout.Name}'";
            States(layout.DockStates, label + " legacy dockStates");
            Pins(layout.DesktopPins, label + " legacy desktopPins");
            foreach (var item in RequiredItems(layout.ItemOverrides, label + " itemOverrides"))
                if (string.IsNullOrWhiteSpace(item.DockId)) errors.Add(label + " item override requires a dock ID.");
            var variantKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var variant in RequiredItems(layout.DisplayVariants, label + " displayVariants"))
            {
                UniqueId(variant.Key, variantKeys, label + " display variant key");
                States(variant.DockStates, label + " dockStates");
                Pins(variant.DesktopPins, label + " desktopPins");
                if (variant.Music is not { } music) errors.Add(label + " music must be an object.");
                else
                {
                    Number(music.Volume, label + " music volume", 0, 1);
                    EnumValue(music.Repeat, label + " music repeat");
                }
            }
        }
        return errors;

        IEnumerable<T> RequiredItems<T>(IEnumerable<T>? values, string label) where T : class
        {
            if (values is null) { errors.Add(label + " must be an array."); yield break; }
            foreach (var value in values)
            {
                if (value is null) errors.Add(label + " cannot contain null entries.");
                else yield return value;
            }
        }
        void UniqueId(string? value, HashSet<string> seen, string label)
        {
            if (string.IsNullOrWhiteSpace(value)) errors.Add(label + " cannot be blank.");
            else if (!seen.Add(value)) errors.Add($"Duplicate {label}: {value}");
        }
        void Number(double value, string label, double minimum = double.MinValue, double maximum = double.MaxValue)
        {
            if (!double.IsFinite(value) || value < minimum || value > maximum) errors.Add(label + " is outside its supported numeric range.");
        }
        void EnumValue<T>(T value, string label) where T : struct, Enum
        {
            if (!Enum.IsDefined(value)) errors.Add(label + " has an unsupported value.");
        }
        void Bounds(ZoneBounds? bounds, string label)
        {
            if (bounds is null) { errors.Add(label + " must be an object."); return; }
            Number(bounds.X, label + " x"); Number(bounds.Y, label + " y");
            Number(bounds.Width, label + " width", double.Epsilon); Number(bounds.Height, label + " height", double.Epsilon);
        }
        void States(IEnumerable<DockLayoutState>? values, string label)
        {
            var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var state in RequiredItems(values, label))
            {
                UniqueId(state.DockId, ids, label + " dock ID");
                Bounds(state.Bounds, label + " bounds");
                EnumValue(state.ExpansionEdge, label + " expansion edge");
            }
        }
        void Pins(IEnumerable<DesktopPinDefinition>? values, string label)
        {
            var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var pin in RequiredItems(values, label))
            {
                UniqueId(pin.Id, ids, label + " pin ID");
                Number(pin.X, label + " x"); Number(pin.Y, label + " y"); Number(pin.IconSize, label + " iconSize", double.Epsilon);
            }
        }
    }

    internal static void ThrowIfInvalid(Workspace workspace)
    {
        var errors = Validate(workspace);
        if (errors.Count != 0) throw new WorkspaceValidationException(string.Join(Environment.NewLine, errors));
    }
}

public sealed class WorkspaceValidationException(string message) : IOException(message);
