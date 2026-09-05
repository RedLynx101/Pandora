using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Pandora.App;
using Pandora.Core;

namespace Pandora.App.Tests;

internal static partial class Program
{
    private static readonly string[] Structures = ["Classic", "Halo", "Meridian"];
    private static readonly string[] Palettes = ["LunarGlass", "Midnight", "Limestone", "Aegean", "System"];

    private static void StructuralThemeContract()
    {
        Assert(DockThemeCatalog.All.Select(t => t.Id).SequenceEqual(Structures), "The structural catalog must expose Classic, Halo, and Meridian.");
        Assert(DockThemeCatalog.Normalize(" unknown ") == "Classic" && DockThemeCatalog.Normalize(null) == "Classic", "Unknown structure must fall back to Classic.");
        Assert(DockThemeCatalog.Normalize(" hALo ") == "Halo", "Structural names must normalize casing and whitespace.");
        var classic = DockThemeCatalog.Get("Classic");
        var halo = DockThemeCatalog.Get("Halo");
        var meridian = DockThemeCatalog.Get("Meridian");
        Assert(halo.SeparatedHeader && halo.HeaderGap > 0 && halo.CornerRadius > classic.CornerRadius, "Halo must change header separation and geometry, not only colors.");
        Assert(meridian.AccentRailWidth > 0 && meridian.CornerRadius < classic.CornerRadius && meridian.ItemGap < halo.ItemGap, "Meridian must expose its compact rail-based geometry.");
        foreach (var profile in DockThemeCatalog.All)
            Assert(profile.HeaderHeight >= 32 && profile.FooterHeight >= 0 && profile.CornerRadius is >= 0 and <= 40 && profile.ShadowOpacity is >= 0 and <= 1,
                "Catalog dimensions must remain finite and in sensible UI bounds.");

        foreach (var value in new string?[] { null, "", "   " })
            Assert(ThemeService.TryNormalizeCustomColor(value, out var normalized) && normalized is null, "Blank custom color must mean palette default.");
        Assert(ThemeService.TryNormalizeCustomColor(" #aBcD09 ", out var valid) && valid == "#ABCD09", "Valid custom hex must normalize to opaque uppercase RGB.");
        foreach (var invalid in new[] { "red", "#FFF", "#00112233", "123456", "#GGCC22", "#12345;", "rgb(1,2,3)", "url(file:///test)", new string('A', 2048) })
            Assert(!ThemeService.TryNormalizeCustomColor(invalid, out _), "Unexpectedly accepted custom color: " + invalid[..Math.Min(invalid.Length, 30)]);

        var contrast = new List<object>();
        foreach (var structure in Structures)
        foreach (var palette in Palettes)
        foreach (var surface in new string?[] { null, "#FFFFFF", "#000000", "#777777", "#F7ECDF", "#102E3A" })
        foreach (var accent in new string?[] { null, "#FFFFFF", "#000000", "#777777", "#D4A857" })
        {
            ThemeService.Apply(palette, 0.8, true, structure, accent, surface);
            Assert(ThemeService.EffectiveDockTheme == structure, "A palette choice must not reset the structural theme.");
            Assert(ThemeService.EffectiveTheme == ThemeService.EffectivePalette, "Legacy EffectiveTheme must remain a palette alias.");
            if (palette != "System") Assert(ThemeService.EffectivePalette == palette, "Concrete palette selection changed unexpectedly.");
            foreach (var foreground in new[] { "Text", "Muted" })
            foreach (var background in new[] { "Window", "Surface", "Elevated" })
            {
                var ratio = Contrast(Brush(foreground).Color, Brush(background).Color);
                contrast.Add(new { structure, palette, surface, accent, foreground, background, ratio });
                Assert(ratio >= 4.5, $"{structure}/{palette} custom {surface ?? "default"}: {foreground}/{background} contrast {ratio:F2} < 4.5.");
            }
            Assert(Contrast(Brush("AccentText").Color, Brush("Accent").Color) >= 4.5, $"{palette} custom accent {accent ?? "default"} text failed contrast.");
            Assert(Contrast(Brush("SelectionText").Color, Brush("Selection").Color) >= 4.5, $"{palette} selection text failed contrast.");
            foreach (var background in new[] { "Window", "Surface", "Elevated" })
                Assert(Contrast(Brush("Focus").Color, Brush(background).Color) >= 3, $"{palette} keyboard focus on {background} failed 3:1 contrast.");
            Assert(ThemeService.DockOpacity is >= 0.55 and <= 1 && ThemeService.ReduceMotion, "Custom palette must retain opacity/accessibility bounds.");
        }
        ThemeService.Apply("Aegean", double.PositiveInfinity, false, "invalid", "nothex", "#00000000");
        Assert(ThemeService.EffectiveDockTheme == "Classic" && ThemeService.EffectiveCustomAccentColor is null && ThemeService.EffectiveCustomSurfaceColor is null,
            "Invalid runtime appearance inputs must recover without persisting malformed colors.");
        Assert(ThemeService.DockOpacity is >= 0.55 and <= 1, "Nonfinite opacity must not enter WPF brushes.");
        File.WriteAllText(Path.Combine(_runPath, "structural-custom-contrast.json"), JsonSerializer.Serialize(contrast, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static void StructuralSettingsBehavior()
    {
        using var fixture = new ManagerFixture(Path.Combine(_fixturePath, "structural-settings"));
        var window = new SettingsWindow(fixture.Manager);
        var defaults = SettingsJson(fixture.Manager.Workspace.Settings);
        var originalBytes = File.ReadAllBytes(fixture.Store.WorkspacePath);
        var list = Find<ListBox>(window, "DockThemeListBox");
        var accent = Find<TextBox>(window, "CustomAccentTextBox");
        var surface = Find<TextBox>(window, "CustomSurfaceTextBox");
        SelectStructure(list, "Halo");
        SelectTag(Find<ComboBox>(window, "ThemeComboBox"), "Aegean");
        accent.Text = "#d4a857";
        surface.Text = "#102e3a";
        Assert(ThemeService.EffectiveDockTheme == "Halo" && ThemeService.EffectivePalette == "Aegean", "Structure and palette did not preview independently.");
        Assert(ThemeService.EffectiveCustomAccentColor == "#D4A857" && ThemeService.EffectiveCustomSurfaceColor == "#102E3A", "Custom color preview did not normalize RGB.");
        Assert(SettingsJson(fixture.Manager.Workspace.Settings) == defaults && File.ReadAllBytes(fixture.Store.WorkspacePath).SequenceEqual(originalBytes), "Preview changed saved appearance state.");
        accent.Text = "#oops";
        Assert(!Find<Button>(window, "ApplyAppearanceButton").IsEnabled, "Invalid custom accent must disable Apply.");
        Assert(!string.IsNullOrWhiteSpace(Find<TextBlock>(window, "AppearanceValidationText").Text), "Invalid colors require a visible explanation.");
        SelectStructure(list, "Meridian");
        SelectTag(Find<ComboBox>(window, "ThemeComboBox"), "Limestone");
        Assert(ThemeService.EffectiveDockTheme == "Meridian" && ThemeService.EffectivePalette == "Limestone" && ThemeService.EffectiveCustomAccentColor == "#D4A857",
            "An invalid HEX draft should retain its last valid color while other appearance controls remain responsive.");
        surface.Text = "#bad-draft";
        accent.Text = "#FFFFFF";
        Assert(ThemeService.EffectiveCustomAccentColor == "#FFFFFF" && ThemeService.EffectiveCustomSurfaceColor == "#102E3A", "One invalid draft should not block a different valid custom-color preview.");
        Invoke(window, "SaveAppearance_Click", window, new RoutedEventArgs());
        Assert(File.ReadAllBytes(fixture.Store.WorkspacePath).SequenceEqual(originalBytes), "Invoking Apply with invalid input must not bypass validation.");
        Invoke(window, "RevertTheme_Click", window, new RoutedEventArgs());
        Assert(ThemeService.EffectiveDockTheme == "Classic" && ThemeService.EffectiveCustomAccentColor is null && ThemeService.EffectiveCustomSurfaceColor is null,
            "Revert failed to restore structural/color defaults.");

        var scenario = 0;
        foreach (var structure in Structures)
        foreach (var palette in Palettes)
        {
            SelectStructure(list, structure);
            SelectTag(Find<ComboBox>(window, "ThemeComboBox"), palette);
            accent.Text = "";
            surface.Text = "";
            var icon = new[] { "Aperture", "Selene", "Aster" }[scenario++ % 3];
            SelectTag(Find<ComboBox>(window, "IconStyleComboBox"), icon);
            Find<Slider>(window, "GlassOpacitySlider").Value = 79;
            Find<CheckBox>(window, "ReduceMotionCheckBox").IsChecked = true;
            Invoke(window, "SaveAppearance_Click", window, new RoutedEventArgs());
            var saved = fixture.Store.LoadOrCreate().Settings;
            Assert(saved.DockTheme == structure && saved.Theme == palette && saved.IconStyle == icon, "Saved appearance axes were conflated or reset.");
            Assert(saved.CustomAccentColor is null && saved.CustomSurfaceColor is null && Math.Abs(saved.GlassOpacity - 0.79) < 0.001 && saved.ReduceMotion,
                "Saved color reset/opacity/accessibility state did not round-trip.");
            if (palette is "LunarGlass" or "Aegean")
            {
                // Exercise all retained icons above, but keep public review renders on the default identity.
                SelectTag(Find<ComboBox>(window, "IconStyleComboBox"), "Aperture");
                Capture((FrameworkElement)window.Content, $"settings-structure-{structure}-{palette}", 1280, 960);
                var customizeLabel = Descendants(Find<Expander>(window, "CustomColorsExpander")).OfType<TextBlock>().First(t => t.Text == "Customize colors");
                Assert(customizeLabel.Foreground is SolidColorBrush headerText && Contrast(headerText.Color, Brush("Window").Color) >= 4.5,
                    "Custom-colors expander header must remain readable on each palette.");
            }
        }
        SelectStructure(list, "Halo");
        SelectTag(Find<ComboBox>(window, "ThemeComboBox"), "Aegean");
        SelectTag(Find<ComboBox>(window, "IconStyleComboBox"), "Aperture");
        accent.Text = "#D4A857";
        surface.Text = "#102E3A";
        Invoke(window, "SaveAppearance_Click", window, new RoutedEventArgs());
        var committed = SettingsJson(fixture.Store.LoadOrCreate().Settings);
        SelectStructure(list, "Meridian");
        accent.Text = "#FFFFFF";
        surface.Text = "#EEEEEE";
        window.Close();
        Drain();
        Assert(SettingsJson(fixture.Store.LoadOrCreate().Settings) == committed, "Closing a preview wrote new appearance preferences.");
        Assert(ThemeService.EffectiveDockTheme == "Halo" && ThemeService.EffectivePalette == "Aegean" && ThemeService.EffectiveCustomAccentColor == "#D4A857" && ThemeService.EffectiveCustomSurfaceColor == "#102E3A",
            "Closing must restore all committed appearance axes.");
        var reopened = new SettingsWindow(fixture.Manager);
        Assert(((ListBoxItem)Find<ListBox>(reopened, "DockThemeListBox").SelectedItem).Tag as string == "Halo", "Reopening settings forgot the saved structural selection.");
        Assert(Find<TextBox>(reopened, "CustomAccentTextBox").Text == "#D4A857" && Find<TextBox>(reopened, "CustomSurfaceTextBox").Text == "#102E3A",
            "Reopening settings forgot custom color controls.");
        Capture((FrameworkElement)reopened.Content, "settings-custom-colors", 1100, 900);
        reopened.Close();
    }

    private static void StructuralSettingsSaveFailure()
    {
        using var fixture = new ManagerFixture(Path.Combine(_fixturePath, "appearance-save-failure"));
        var window = new SettingsWindow(fixture.Manager);
        var original = SettingsJson(fixture.Manager.Workspace.Settings);
        var bytes = File.ReadAllBytes(fixture.Store.WorkspacePath);
        SelectStructure(Find<ListBox>(window, "DockThemeListBox"), "Meridian");
        SelectTag(Find<ComboBox>(window, "ThemeComboBox"), "Aegean");
        SelectTag(Find<ComboBox>(window, "IconStyleComboBox"), "Aster");
        Find<TextBox>(window, "CustomAccentTextBox").Text = "#D4A857";
        Find<TextBox>(window, "CustomSurfaceTextBox").Text = "#102E3A";
        Find<Slider>(window, "GlassOpacitySlider").Value = 69;
        Find<CheckBox>(window, "ReduceMotionCheckBox").IsChecked = true;
        // Lock only this run's fixture destination against replacement to exercise rollback.
        using (var guard = new FileStream(fixture.Store.WorkspacePath, FileMode.Open, FileAccess.Read, FileShare.Read))
            Invoke(window, "SaveAppearance_Click", window, new RoutedEventArgs());
        Assert(SettingsJson(fixture.Manager.Workspace.Settings) == original, "Save failure left new preferences in canonical in-memory settings.");
        Assert(File.ReadAllBytes(fixture.Store.WorkspacePath).SequenceEqual(bytes), "Save failure altered persisted settings.");
        Assert(ThemeService.EffectiveDockTheme == "Classic" && ThemeService.EffectivePalette == "LunarGlass" && ThemeService.EffectiveCustomAccentColor is null && ThemeService.EffectiveCustomSurfaceColor is null,
            "Save failure did not restore every live appearance axis.");
        Assert(Find<TextBlock>(window, "StatusText").Text.Contains("not saved", StringComparison.OrdinalIgnoreCase), "Save failure must be visible to the user.");
        window.Close();
    }

    private static void StructuralDockRenders()
    {
        using var fixture = new ManagerFixture(Path.Combine(_fixturePath, "structural-docks"));
        var zone = CreateFixtureDock(fixture, "Theme laboratory", 2);
        using var vm = new ZoneViewModel(zone, fixture.Manager);
        var window = new ZoneWindow(vm, fixture.Manager);
        Invoke(window, "RenderTabs");
        var geometry = new List<object>();
        foreach (var palette in Palettes)
        {
            var signatures = new HashSet<string>();
            var imageHashes = new HashSet<string>();
            foreach (var structure in Structures)
            {
                ThemeService.Apply(palette, 0.88, false, structure, null, null);
                Capture((FrameworkElement)window.Content, $"structural-{structure}-{palette}", 540, 380);
                Capture((FrameworkElement)window.Content, $"structural-narrow-{structure}-{palette}", 300, 360);
                var frame = Find<Border>(window, "Frame");
                var header = Find<Border>(window, "HeaderBorder");
                var body = Find<Border>(window, "BodyChrome");
                var rail = Find<Border>(window, "AccentRail");
                var profile = DockThemeCatalog.Get(structure);
                Assert(Near(rail.Width, profile.AccentRailWidth), "The actual dock rail must follow the selected structure.");
                Assert(body.CornerRadius == new CornerRadius(profile.SeparatedHeader ? profile.CornerRadius : 0), "The actual body chrome must reflect Halo's separated geometry.");
                Assert(Near(body.BorderThickness.Left, profile.SeparatedHeader ? 1 : 0), "Only separated Halo body chrome should draw its own frame.");
                var metrics = new { structure, palette, frameRadius = frame.CornerRadius.ToString(), headerRadius = header.CornerRadius.ToString(), headerMargin = header.Margin.ToString(), headerPadding = header.Padding.ToString(), headerHeight = vm.HeaderHeight, bodyRadius = body.CornerRadius.ToString(), bodyMargin = body.Margin.ToString(), accentRailWidth = rail.Width };
                geometry.Add(metrics);
                signatures.Add($"{metrics.frameRadius}/{metrics.headerRadius}/{metrics.headerMargin}/{metrics.headerPadding}/{metrics.headerHeight}");
                imageHashes.Add(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(_runPath, $"structural-{structure}-{palette}.png")))));
                Assert(vm.Items.Count == 3, "Changing structural theme must preserve dock items.");
                Assert(Find<FrameworkElement>(window, "ContentHost").ActualWidth > 0, "Structural frame consumed the whole narrow content width.");
                Assert(Descendants(window.Content as DependencyObject).OfType<Button>().Where(b => b.IsVisible).All(b => b.ActualHeight > 0), "A structural theme collapsed a visible control.");
            }
            Assert(signatures.Count == 3, "The three structural themes must change live geometry at the same palette.");
            Assert(imageHashes.Count == 3, "The three structural themes rendered identical pixels at the same palette.");
        }
        File.WriteAllText(Path.Combine(_runPath, "structural-geometry.json"), JsonSerializer.Serialize(geometry, new JsonSerializerOptions { WriteIndented = true }));
        window.Close();
    }

    private static void CollapsedDockRegression()
    {
        using var fixture = new ManagerFixture(Path.Combine(_fixturePath, "collapsed-placement"));
        var observations = new List<object>();
        foreach (var structure in Structures)
        foreach (var edge in new[] { DockExpansionEdge.Top, DockExpansionEdge.Bottom })
        foreach (var tabCount in new[] { 1, 2 })
        foreach (var initiallyCollapsed in new[] { false, true })
        {
            ThemeService.Apply("LunarGlass", 0.88, true, structure, null, null);
            var zone = CreateFixtureDock(fixture, structure + " anchor check", tabCount);
            zone.IsCollapsed = initiallyCollapsed;
            WorkspaceLayoutService.SetExpansionEdge(fixture.Manager.Workspace, zone.Id, edge);
            using var vm = new ZoneViewModel(zone, fixture.Manager);
            var window = new ZoneWindow(vm, fixture.Manager);
            var expanded = InvokeResult<ZoneBounds>(window, "NormalizeWindowBounds", CopyBounds(zone.Bounds), false);
            Invoke(window, "ApplyWindowBounds", expanded);
            Invoke(window, "RenderTabs");
            if (!initiallyCollapsed) Invoke(window, "ApplyCollapsedState");
            zone.IsCollapsed = true;
            Invoke(window, "ApplyCollapsedState");
            var collapsedHeight = window.Height;
            Assert(Near(collapsedHeight, window.CollapsedVisualHeight), "WPF Height/MinHeight coercion conflicts with the canonical collapsed height.");
            Assert(collapsedHeight >= vm.HeaderHeight && collapsedHeight < 130, "Collapsed frame must fit its header without an expanded empty body.");
            AssertCollapsedProjection(window, vm, expanded, collapsedHeight, edge);
            var topBefore = window.Top;
            Invoke(window, "ApplyCollapsedState");
            Assert(Near(window.Top, topBefore) && Near(window.Height, collapsedHeight), "Repeated collapse application must be idempotent, including bottom anchoring.");
            if (edge == DockExpansionEdge.Bottom)
            {
                AssertBottomAnchorHeightClamping(window, vm, expanded, collapsedHeight, observations, structure, tabCount, initiallyCollapsed);
                Invoke(window, "ApplyWindowBounds", expanded);
                Invoke(window, "SaveCurrentWindowBoundsToModel");
            }

            // Simulate movement through the same in-process geometry/model helpers used by
            // placement events. No native mouse event, HWND, Window.Show, or desktop layer.
            window.Left += 19;
            window.Top += 23;
            Invoke(window, "SaveCurrentWindowBoundsToModel");
            expanded = InvokeResult<ZoneBounds>(window, "GetCurrentWindowBounds");
            AssertCollapsedProjection(window, vm, expanded, collapsedHeight, edge);
            foreach (var correction in new[] { "width", "expanded-height" })
            {
                var oversized = CopyBounds(expanded);
                if (correction == "width") oversized.Width = 1;
                else oversized.Height = 100000;
                var corrected = InvokeResult<ZoneBounds>(window, "NormalizeWindowBounds", oversized, false);
                Assert(correction == "width" ? corrected.Width > oversized.Width : corrected.Height < oversized.Height, "Fixture failed to force a geometry normalization.");
                Invoke(window, "ApplyWindowBounds", corrected);
                Invoke(window, "SaveCurrentWindowBoundsToModel");
                expanded = InvokeResult<ZoneBounds>(window, "GetCurrentWindowBounds");
                AssertCollapsedProjection(window, vm, corrected, collapsedHeight, edge);
                Assert(Near(expanded.Height, corrected.Height), "Normalization must update remembered expanded height, not physical collapsed height.");
                observations.Add(new { structure, edge = edge.ToString(), tabCount, initiallyCollapsed, correction, physicalHeight = window.Height, rememberedExpandedHeight = expanded.Height, visibleTop = window.Top, rememberedTop = expanded.Y });
            }
            if (initiallyCollapsed)
                Capture((FrameworkElement)window.Content, $"collapsed-{structure}-{edge}-{tabCount}tabs", window.Width, collapsedHeight);
            Assert(Find<FrameworkElement>(window, "ContentHost").ActualHeight == 0 || Find<FrameworkElement>(window, "ContentHost").Visibility == Visibility.Collapsed,
                "A collapsed dock must not retain a visible empty content region.");
            Invoke(window, "RestoreReasonableSize", false);
            expanded = InvokeResult<ZoneBounds>(window, "GetCurrentWindowBounds");
            AssertCollapsedProjection(window, vm, expanded, collapsedHeight, edge);
            zone.IsCollapsed = false;
            Invoke(window, "ApplyCollapsedState");
            Assert(Near(window.Height, expanded.Height) && Near(window.Top, expanded.Y), "Re-expanding after normalization must restore the saved expanded bounds and anchor.");
            Assert(Find<FrameworkElement>(window, "ContentHost").Visibility == Visibility.Visible, "Re-expansion must restore the actual content.");
            Invoke(window, "SaveCurrentWindowBoundsToModel");
            WorkspaceLayoutService.CaptureAllZoneStates(fixture.Manager.Workspace);
            fixture.Store.Save(fixture.Manager.Workspace);
            var reloadedZone = fixture.Store.LoadOrCreate().Zones.Single(z => z.Id == zone.Id);
            Assert(Near(reloadedZone.Bounds.Height, expanded.Height) && Near(reloadedZone.Bounds.Y, expanded.Y), "Persistence replaced remembered expanded bounds with the collapsed strip.");
            window.Close();
        }
        File.WriteAllText(Path.Combine(_runPath, "collapsed-placement-regression.json"), JsonSerializer.Serialize(observations, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static void AssertBottomAnchorHeightClamping(ZoneWindow window, ZoneViewModel vm, ZoneBounds original, double collapsedHeight,
        List<object> observations, string structure, int tabCount, bool initiallyCollapsed)
    {
        var area = InvokeResult<Rect>(window, "GetWorkingArea", original.X, original.Y, original.Width, original.Height);
        var margin = WorkspaceLayoutService.DockWorkAreaMargin;
        var originalBottom = area.Bottom - margin;
        var oversized = new ZoneBounds { X = area.Left + margin, Y = originalBottom - area.Height * 1.2, Width = original.Width, Height = area.Height * 1.2 };
        var chosenArea = InvokeResult<Rect>(window, "GetWorkingArea", oversized.X, oversized.Y, oversized.Width, oversized.Height);
        Assert(chosenArea == area, "The original-bottom fixture must remain on its intended read-only work area.");

        // Exercise direct bounds application as well as the normalizer: either helper can
        // clamp remembered height and used to move a bottom-anchored closed header by that delta.
        Invoke(window, "ApplyWindowBounds", oversized);
        Assert(window.Height < oversized.Height && Near(window.Top + window.Height, originalBottom),
            "Direct height clamping moved a bottom anchor that already fit inside the work area.");
        var corrected = InvokeResult<ZoneBounds>(window, "NormalizeWindowBounds", oversized, false);
        Assert(corrected.Height < oversized.Height && Near(corrected.Y + corrected.Height, originalBottom),
            "Normalizing oversized remembered height must preserve the original valid bottom, not the original top.");
        Invoke(window, "ApplyWindowBounds", corrected);
        AssertCollapsedProjection(window, vm, corrected, collapsedHeight, DockExpansionEdge.Bottom);
        Assert(Near(window.Top + window.Height, originalBottom), "Applying normalized bounds moved the valid original bottom.");
        var restored = InvokeResult<ZoneBounds>(window, "CreateRestoredWindowBounds", oversized);
        Assert(Near(restored.Y + restored.Height, originalBottom), "Restore-size projection must preserve an already valid bottom anchor.");
        observations.Add(new { structure, edge = "Bottom", tabCount, initiallyCollapsed, correction = "height-clamp-valid-original-bottom", originalBottom, correctedBottom = corrected.Y + corrected.Height, physicalBottom = window.Top + window.Height, physicalHeight = window.Height });

        // Unlike a valid anchor, an out-of-work-area bottom must move to the allowed boundary.
        var outside = new ZoneBounds { X = area.Left + margin, Y = area.Top + margin, Width = original.Width, Height = area.Height * 1.2 };
        var outsideArea = InvokeResult<Rect>(window, "GetWorkingArea", outside.X, outside.Y, outside.Width, outside.Height);
        var clamped = InvokeResult<ZoneBounds>(window, "NormalizeWindowBounds", outside, false);
        Assert(outside.Y + outside.Height > outsideArea.Bottom - margin, "The offscreen-boundary fixture must start beyond its selected work area.");
        Assert(Near(clamped.Y + clamped.Height, outsideArea.Bottom - margin) && clamped.Y >= outsideArea.Top + margin - 0.05,
            "An unavoidable boundary correction must move the bottom inside the working area.");
        Invoke(window, "ApplyWindowBounds", clamped);
        AssertCollapsedProjection(window, vm, clamped, collapsedHeight, DockExpansionEdge.Bottom);
        observations.Add(new { structure, edge = "Bottom", tabCount, initiallyCollapsed, correction = "offscreen-bottom-boundary", originalBottom = outside.Y + outside.Height, allowedBottom = outsideArea.Bottom - margin, physicalBottom = window.Top + window.Height, physicalHeight = window.Height });
    }

    private static void AssertCollapsedProjection(ZoneWindow window, ZoneViewModel vm, ZoneBounds expanded, double collapsedHeight, DockExpansionEdge edge)
    {
        Assert(vm.Zone.IsCollapsed, "A placement correction silently changed collapsed state.");
        Assert(Near(window.Height, collapsedHeight), "Placement correction expanded the physical frame while content remained collapsed.");
        Assert(Find<FrameworkElement>(window, "ContentHost").Visibility == Visibility.Collapsed && Find<FrameworkElement>(window, "StatusBorder").Visibility == Visibility.Collapsed,
            "Collapsed content/footer must remain hidden through placement correction.");
        Assert(edge == DockExpansionEdge.Bottom ? Near(window.Top + window.Height, expanded.Y + expanded.Height) : Near(window.Top, expanded.Y),
            "Collapsed placement lost its declared top/bottom anchor.");
        Assert(Find<RowDefinition>(window, "ContentRow").Height.Value == 0, "Collapsed content grid row must occupy zero height.");
        Assert(Find<RowDefinition>(window, "TopNavigationRow").Height.Value == 0 && Find<RowDefinition>(window, "BottomNavigationRow").Height.Value == 0,
            "Collapsed navigation rows must not leave an empty strip.");
        var hiddenFooterRow = edge == DockExpansionEdge.Top ? "BottomChromeRow" : "TopChromeRow";
        Assert(Find<RowDefinition>(window, hiddenFooterRow).Height.Value == 0, "Hidden footer row must occupy zero height.");
    }

    private static bool Near(double actual, double expected) => double.IsFinite(actual) && Math.Abs(actual - expected) < 0.05;
    private static ZoneBounds CopyBounds(ZoneBounds bounds) => new() { X = bounds.X, Y = bounds.Y, Width = bounds.Width, Height = bounds.Height };

    private static ZoneDefinition CreateFixtureDock(ManagerFixture fixture, string name, int tabCount)
    {
        var path = Path.Combine(fixture.Root, "dock-files");
        Directory.CreateDirectory(path);
        foreach (var filename in new[] { "Interface contract.md", "Phase evidence.txt", "Review checklist.md" })
            File.WriteAllText(Path.Combine(path, filename), "Synthetic WPF verification fixture; no live project data.");
        var zone = new ZoneDefinition { Id = "structural-fixture-" + Guid.NewGuid().ToString("N"), Name = name, IsVisible = false, Bounds = new ZoneBounds { X = 150, Y = 140, Width = 540, Height = 380 } };
        for (var i = 0; i < tabCount; i++) zone.Tabs.Add(new ZoneTabDefinition { Id = "tab-" + i, Name = i == 0 ? "Workspace" : "Evidence", Path = path });
        fixture.Manager.Workspace.Zones.Add(zone);
        WorkspaceLayoutService.EnsureActiveDisplayVariant(fixture.Manager.Workspace);
        return zone;
    }

    private static void SelectStructure(ListBox list, string id) => list.SelectedItem = list.Items.OfType<ListBoxItem>().Single(item => Equals(item.Tag, id));
    private static string SettingsJson(AppSettings settings) => JsonSerializer.Serialize(settings);
    private static T InvokeResult<T>(object target, string method, params object[] args)
    {
        var info = target.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic) ?? throw new InvalidOperationException("Missing placement helper: " + method);
        try { return (T)info.Invoke(target, args)!; }
        catch (TargetInvocationException ex) { throw ex.InnerException ?? ex; }
    }
}
