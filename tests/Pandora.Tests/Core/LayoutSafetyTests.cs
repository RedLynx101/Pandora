using System.Text.Json;
using Pandora.Core;

internal static class LayoutSafetyTests
{
    public static void Run()
    {
        var workspace = WorkspaceFactory.CreateDefault();
        var layout = WorkspaceLayoutService.EnsureActiveLayout(workspace);
        var unplugged = WorkspaceLayoutService.UseDisplayVariant(workspace, "unplugged", "wide-desktop", []);
        unplugged.DockStates[0].Bounds = new ZoneBounds { X = 2800, Y = 1500, Width = 900, Height = 700 };
        unplugged.DockStates[0].IsCollapsed = true;
        unplugged.DesktopPins.Add(new DesktopPinDefinition { Id = "remote-monitor-pin", Path = "fixture.txt", X = 3000, Y = 1700 });

        var current = WorkspaceLayoutService.UseDisplayVariant(workspace, "current", "small-desktop", []);
        current.DockStates[0].Bounds = new ZoneBounds { X = 5000, Y = 3000, Width = 3000, Height = 2000 };
        current.DockStates[0].IsCollapsed = true;
        var savedOtherVariants = layout.DisplayVariants.Where(v => !ReferenceEquals(v, current))
            .ToDictionary(v => v.Key, v => JsonSerializer.Serialize(v));
        var displays = new[] { new DisplayDescriptor
        {
            DeviceName = "Fixture", IsPrimary = true, BoundsWidth = 1280, BoundsHeight = 800,
            WorkAreaWidth = 1280, WorkAreaHeight = 760
        } };

        Assert(WorkspaceLayoutService.RepairOversizedDockBounds(workspace, displays) == 1, "Expected exactly the active variant to be repaired.");
        Assert(current.DockStates[0].Bounds.Width <= 1280 && current.DockStates[0].Bounds.Height <= 760,
            "Active dock bounds were not repaired for the current display.");
        Assert(current.DockStates[0].IsCollapsed, "Repair should preserve collapsed state.");
        foreach (var variant in layout.DisplayVariants.Where(v => !ReferenceEquals(v, current)))
            Assert(JsonSerializer.Serialize(variant) == savedOtherVariants[variant.Key], "Repair changed an inactive display variant: " + variant.Key);
        Assert(WorkspaceLayoutService.RepairOversizedDockBounds(workspace, displays) == 0, "Repair should be idempotent.");
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
