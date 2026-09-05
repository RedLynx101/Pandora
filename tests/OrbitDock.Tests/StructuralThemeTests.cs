using System.Text.Json;
using OrbitDock.Core;

internal static class StructuralThemeTests
{
    /// <summary>Pure model/migration checks: no current-user workspace or file-system access.</summary>
    public static void Run()
    {
        var jsonOptions = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        var legacy = JsonSerializer.Deserialize<Workspace>("""
            {
              "schemaVersion": 5,
              "settings": { "theme": "Midnight", "glassOpacity": 0.73, "iconStyle": "Selene" },
              "zones": [{ "id": "custom", "name": "My exact custom dock", "bounds": { "x": 112, "y": 144, "width": 487, "height": 315 }, "appearance": { "backgroundColor": "#334455", "accentColor": "#DDBB55", "opacity": 0.67, "cornerRadius": 11, "iconSize": 51 } }]
            }
            """, jsonOptions)!;
        Assert(legacy.Settings.DockTheme == "Classic", "Old palette-only JSON must default to the Classic structural theme.");
        Assert(legacy.Settings.DockBarSize == "Standard", "Old workspaces must default to Standard bar sizing.");
        Assert(WorkspaceMigrator.MigrateToCurrent(legacy), "The old schema should report a migration.");
        Assert(legacy.SchemaVersion == WorkspaceMigrator.CurrentSchemaVersion, "Migration must use the current schema.");
        Assert(legacy.Settings.Theme == "Midnight", "Structural migration must not rewrite the existing palette.");
        Assert(legacy.Settings.IconStyle == "Selene" && legacy.Settings.GlassOpacity == 0.73, "Migration must retain icon and opacity choices.");
        var dock = legacy.Zones.Single(z => z.Id == "custom");
        Assert(dock.Name == "My exact custom dock" && dock.Bounds.X == 112 && dock.Bounds.Y == 144 && dock.Bounds.Width == 487 && dock.Bounds.Height == 315,
            "Migration must preserve custom identity and remembered bounds.");
        Assert(dock.Appearance.BackgroundColor == "#334455" && dock.Appearance.AccentColor == "#DDBB55" && dock.Appearance.Opacity == 0.67 && dock.Appearance.CornerRadius == 11 && dock.Appearance.IconSize == 51,
            "Structural migration must not rewrite deliberate per-dock appearance overrides.");
        Assert(!WorkspaceMigrator.MigrateToCurrent(legacy), "A second migration should be idempotent.");

        foreach (var structure in new[] { "Classic", "Halo", "Meridian" })
        foreach (var palette in new[] { "LunarGlass", "Midnight", "Limestone", "Aegean", "System" })
        foreach (var icon in new[] { "Aperture", "Selene", "Aster" })
        foreach (var barSize in new[] { "Compact", "Standard", "Large" })
        {
            var settings = new AppSettings
            {
                Theme = palette, DockTheme = structure, IconStyle = icon, DockBarSize = barSize,
                CustomAccentColor = "#D4A857", CustomSurfaceColor = "#142B32", GlassOpacity = 0.79, ReduceMotion = true
            };
            var reloaded = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(settings, jsonOptions), jsonOptions)!;
            Assert(reloaded.DockTheme == structure && reloaded.Theme == palette && reloaded.IconStyle == icon, "Structure, palette, and icon must round-trip as independent axes.");
            Assert(reloaded.DockBarSize == barSize, "Bar size must round-trip independently.");
            Assert(reloaded.CustomAccentColor == settings.CustomAccentColor && reloaded.CustomSurfaceColor == settings.CustomSurfaceColor && reloaded.GlassOpacity == 0.79 && reloaded.ReduceMotion,
                "Custom colors, opacity, and accessibility choices must round-trip.");
        }

        legacy.Settings.DockTheme = null!;
        legacy.Settings.DockBarSize = null!;
        Assert(WorkspaceMigrator.MigrateToCurrent(legacy) && legacy.Settings.DockTheme == "Classic", "Explicit null old structural state must recover to Classic.");
        Assert(legacy.Settings.DockBarSize == "Standard", "Explicit null bar size must recover to Standard.");
        legacy.Settings.DockTheme = "  ";
        Assert(WorkspaceMigrator.MigrateToCurrent(legacy) && legacy.Settings.DockTheme == "Classic", "Blank structural state must recover to Classic.");

        foreach (var edge in new[] { DockExpansionEdge.Top, DockExpansionEdge.Bottom })
        foreach (var collapsed in new[] { false, true })
        foreach (var headerHeight in new[] { 46.0, 60.0, 66.0 })
        foreach (var origin in new[] { -1700.0, 0, 135.5, 2100.0 })
        {
            var expanded = new ZoneBounds { X = origin, Y = origin / 2, Width = 360, Height = 320 };
            var before = JsonSerializer.Serialize(expanded);
            var visible = DockBoundsProjection.ToVisible(expanded, collapsed, edge, headerHeight);
            Assert(visible.Height == (collapsed ? headerHeight : expanded.Height), "Visible projection must not carry remembered expanded height when collapsed.");
            Assert(edge != DockExpansionEdge.Bottom || !collapsed || visible.Y + visible.Height == expanded.Y + expanded.Height, "Bottom projection lost its anchor.");
            Assert(edge != DockExpansionEdge.Top || visible.Y == expanded.Y, "Top projection lost its anchor.");
            var roundTrip = DockBoundsProjection.ToExpanded(visible, collapsed, edge, expanded.Height);
            Assert(JsonSerializer.Serialize(roundTrip) == before && JsonSerializer.Serialize(expanded) == before, "Projection must round-trip without mutating source geometry.");
            visible.X += 18;
            visible.Y -= 27;
            var moved = DockBoundsProjection.ToExpanded(visible, collapsed, edge, expanded.Height);
            Assert(moved.X == expanded.X + 18 && moved.Y == expanded.Y - 27 && moved.Height == expanded.Height, "Movement must update position without corrupting remembered expanded size.");
        }
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
