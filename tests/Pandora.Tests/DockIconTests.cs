using System.Text.Json.Nodes;
using Pandora.Core;

internal static class DockIconTests
{
    public static void Run()
    {
        var root = Path.Combine(Path.GetTempPath(), "Pandora.DockIcon.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var store = new WorkspaceStore(Path.Combine(root, "workspace.json"));
        var workspace = WorkspaceFactory.CreateDefault();
        workspace.Zones[0].Name = "My lunar artwork";
        var customPath = Path.Combine(root, "My lunar artwork.png");
        File.WriteAllText(customPath, "A user-owned sentinel; Core must not read or alter image contents.");
        workspace.Zones[0].Appearance.HeaderIcon = DockHeaderIcon.Custom;
        workspace.Zones[0].Appearance.HeaderIconPath = customPath;
        workspace.Zones[1].Appearance.HeaderIcon = DockHeaderIcon.None;
        workspace.Settings.IconStyle = "Selene";
        store.Save(workspace);
        var loaded = store.LoadReadOnly();
        Assert(loaded.Zones[0].Appearance.HeaderIcon == DockHeaderIcon.Custom && loaded.Zones[0].Appearance.HeaderIconPath == customPath,
            "Custom header icon did not round-trip exactly.");
        Assert(loaded.Zones[1].Appearance.HeaderIcon == DockHeaderIcon.None && loaded.Zones[2].Appearance.HeaderIcon == DockHeaderIcon.Pandora,
            "Icon choices leaked between docks.");
        Assert(loaded.Settings.IconStyle == "Selene" && loaded.Zones[0].Name == "My lunar artwork" &&
            File.ReadAllText(customPath).StartsWith("A user-owned sentinel", StringComparison.Ordinal), "Icon settings rewrote unrelated product settings, custom labels, or image data.");
        loaded.Zones[0].Appearance.HeaderIcon = (DockHeaderIcon)999;
        Assert(WorkspaceValidation.Validate(loaded).Any(error => error.Contains("headerIcon", StringComparison.Ordinal)), "Unknown icon mode was not validated.");

        // A workspace from before this additive preference must retain the existing Pandora icon.
        var json = JsonNode.Parse(File.ReadAllText(store.WorkspacePath))!.AsObject();
        foreach (var zone in json["zones"]!.AsArray())
        {
            var appearance = zone!["appearance"]!.AsObject();
            appearance.Remove("headerIcon");
            appearance.Remove("headerIconPath");
        }
        var oldPath = Path.Combine(root, "missing-icon-fields.json");
        File.WriteAllText(oldPath, json.ToJsonString());
        var oldBytes = File.ReadAllBytes(oldPath);
        var old = new WorkspaceStore(oldPath).LoadReadOnly();
        Assert(old.Zones.All(zone => zone.Appearance.HeaderIcon == DockHeaderIcon.Pandora && zone.Appearance.HeaderIconPath is null),
            "Missing header preferences did not use the backward-compatible Pandora default.");
        Assert(File.ReadAllBytes(oldPath).SequenceEqual(oldBytes), "Reading missing icon preferences wrote the old workspace.");
    }

    private static void Assert(bool value, string message)
    { if (!value) throw new InvalidOperationException(message); }
}
