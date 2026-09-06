using System.Diagnostics;
using Pandora.Core;

var tests = new List<(string Name, Action Body)>
{
    ("PathExpander expands user profile tokens", PathExpansion),
    ("RuleEngine matches extensions without leading dot sensitivity", ExtensionRule),
    ("RuleEngine requires all conditions", CompoundRule),
    ("WorkspaceStore round-trips default workspace", WorkspaceRoundTrip),
    ("Default workspace includes smart desktop docks", SmartDesktopDefaults),
    ("WorkspaceStore migrates v1 workspace into a named layout", WorkspaceMigration),
    ("Schema migration preserves settings, custom labels and IDs", PandoraMigration),
    ("Layout switching restores dock state", LayoutSwitching),
    ("Item order overrides persist", ItemOrderPersistence),
    ("Dock membership overrides persist", DockMembershipPersistence),
    ("Desktop pins persist in active layout", DesktopPinPersistence),
    ("Display signatures and variants are stable", DisplayVariants),
    ("Display variant keys ignore work-area-only changes", DisplayVariantKeysIgnoreWorkAreaChanges),
    ("Legacy work-area display variants are reused", LegacyWorkAreaVariantReuse),
    ("Second-monitor dock bounds survive layout clamp", SecondMonitorDockBounds),
    ("Oversized dock bounds are repaired", OversizedDockBoundsRepair),
    ("Dock search matches name, extension, and path", DockSearch),
    ("Music scanner handles playlists and unsupported files", MusicScanner),
    ("Agent feed store persists read and checklist state", AgentFeedStorePersistence),
    ("Agent feed store enforces practical payload limits", AgentFeedLimits),
    ("Agent feed CLI publishes, validates, and updates state", AgentFeedCli),
    ("pandoractl validates a workspace", CliValidation)
};

tests.Add(("Metis reader, registry, and read-only portfolio boundaries", MetisTests.Run));
tests.Add(("Packaged and ordinary launches share data and fail closed before migration", UserDataPathTests.Run));
tests.Add(("Structural themes migrate and round-trip independently from palettes", StructuralThemeTests.Run));
tests.Add(("Per-dock header icons default safely and round-trip independently", DockIconTests.Run));
tests.Add(("CLI validation, isolation and content-stable checklist identities", CliSafetyTests.Run));
tests.Add(("Workspace recovery, shape validation and snapshot conflict safety", WorkspaceSafetyTests.Run));
tests.Add(("Agent feed identity, byte limits and state mutation safety", FeedSafetyTests.Run));
tests.Add(("Metis source isolation and registry persistence safety", ProjectSafetyTests.Run));
tests.Add(("Repair sizes preserves inactive display variants", LayoutSafetyTests.Run));
tests.Add(("File transfers and music scans refuse unsafe linked paths", TransferSafetyTests.Run));

var failed = 0;
foreach (var test in tests)
{
    try
    {
        test.Body();
        Console.WriteLine($"PASS {test.Name}");
    }
    catch (Exception ex)
    {
        failed++;
        Console.Error.WriteLine($"FAIL {test.Name}: {ex.Message}");
    }
}

return failed == 0 ? 0 : 1;

static void PathExpansion()
{
    var expanded = PathExpander.Expand("%USERPROFILE%\\Desktop");
    Assert(Directory.Exists(Path.GetPathRoot(expanded)!), "Expanded path should have a valid root.");
    Assert(!expanded.Contains("%USERPROFILE%", StringComparison.OrdinalIgnoreCase), "Environment token should be expanded.");
}

static void PandoraMigration()
{
    var workspace = WorkspaceFactory.CreateDefault();
    workspace.SchemaVersion = 4;
    var launchpad = workspace.Zones.First(zone => zone.Id == "launchpad");
    launchpad.Name = "My launch tools";
    launchpad.Appearance.BackgroundColor = "#123456";
    workspace.Settings.Audio.MusicRootPath = @"C:\My Music";
    workspace.Settings.Theme = "Midnight";
    workspace.Settings.IconStyle = "Selene";
    workspace.Settings.GlassOpacity = 0.93;
    workspace.Settings.ReduceMotion = true;
    workspace.Zones.RemoveAll(zone => zone.Kind == ZoneKind.Projects);
    workspace.Zones.Add(new ZoneDefinition { Id = "projects", Name = "Personal files", Kind = ZoneKind.Standard });
    WorkspaceMigrator.MigrateToCurrent(workspace);
    Assert(workspace.SchemaVersion == WorkspaceMigrator.CurrentSchemaVersion, "Pandora schema should be current.");
    Assert(workspace.Zones.Count(zone => zone.Kind == ZoneKind.Projects) == 1, "Projects dock should be added once.");
    Assert(workspace.Zones.Single(zone => zone.Kind == ZoneKind.Projects).Id != "projects", "An existing unrelated ID must not be taken over.");
    Assert(launchpad.Name == "My launch tools" && launchpad.Appearance.BackgroundColor == "#123456", "Custom dock identity/appearance must survive.");
    Assert(workspace.Settings.Audio.MusicRootPath == @"C:\My Music", "Saved music path must survive schema migration.");
    WorkspaceMigrator.MigrateToCurrent(workspace);
    Assert(workspace.Zones.Count(zone => zone.Kind == ZoneKind.Projects) == 1, "Repeated migration must be idempotent.");
    var testRoot = Path.Combine(Path.GetTempPath(), "Pandora.Tests", Guid.NewGuid().ToString("N"));
    var store = new WorkspaceStore(Path.Combine(testRoot, "workspace.json"));
    store.Save(workspace);
    var loaded = store.LoadOrCreate();
    Assert(loaded.Settings.Theme == "Midnight" && loaded.Settings.IconStyle == "Selene", "Theme/icon must persist.");
    Assert(loaded.Settings.ReduceMotion && Math.Abs(loaded.Settings.GlassOpacity - 0.93) < 0.00001, "Accessibility/opacity must persist.");
}

static void ExtensionRule()
{
    var rule = new RuleCondition { Field = RuleField.Extension, Match = RuleMatch.Equals, Value = ".PDF" };
    var candidate = new RuleCandidate(@"C:\Temp\Report.pdf");
    Assert(RuleEngine.Matches(rule, candidate), "Extension rule should ignore case and leading dot.");
}

static void CompoundRule()
{
    var workspace = new Workspace
    {
        Rules =
        [
            new RuleDefinition
            {
                TargetZoneId = "docs",
                Conditions =
                [
                    new RuleCondition { Field = RuleField.Extension, Match = RuleMatch.Equals, Value = "docx" },
                    new RuleCondition { Field = RuleField.FileName, Match = RuleMatch.Contains, Value = "Proposal" }
                ]
            }
        ]
    };

    var matches = RuleEngine.FindMatches(workspace, new RuleCandidate(@"C:\Temp\Client Proposal.docx"));
    Assert(matches.Count == 1, "Expected compound rule to match.");

    var misses = RuleEngine.FindMatches(workspace, new RuleCandidate(@"C:\Temp\Client Notes.docx"));
    Assert(misses.Count == 0, "Expected compound rule to require all conditions.");
}

static void WorkspaceRoundTrip()
{
    var tempPath = Path.Combine(Path.GetTempPath(), "Pandora.Tests", Guid.NewGuid().ToString("N"), "workspace.json");
    var store = new WorkspaceStore(tempPath);
    var workspace = WorkspaceFactory.CreateDefault();
    store.Save(workspace);
    var loaded = store.LoadOrCreate();
    Assert(loaded.SchemaVersion == WorkspaceMigrator.CurrentSchemaVersion, "Workspace should be on the current schema.");
    Assert(loaded.Layouts.Count >= 1, "Workspace should include at least one layout.");
    Assert(loaded.Zones.Count >= 3, "Default workspace should include starter zones.");
    Assert(loaded.Rules.Count >= 2, "Default workspace should include starter rule templates.");
}

static void SmartDesktopDefaults()
{
    var workspace = WorkspaceFactory.CreateDefault();
    Assert(workspace.Settings.HideDesktopIconsWhenRunning, "Default workspace should enable clean desktop mode.");
    Assert(workspace.Settings.StayVisibleOnShowDesktop, "Default workspace should keep dock overlays visible when showing the desktop.");
    Assert(workspace.Zones.Any(zone => zone.Tabs.Any(tab => tab.Source == ZoneTabSource.SmartDesktop)), "Expected smart desktop tabs.");
    Assert(workspace.Zones.Any(zone => zone.Name.Contains("Launchpad", StringComparison.OrdinalIgnoreCase)), "Expected a launchpad zone.");
    Assert(workspace.Zones.Any(zone => zone.Kind == ZoneKind.AgentFeed && zone.AgentFeed.FeedIds.Contains("morning-brief")), "Expected a default morning-brief agent feed dock.");
}

static void WorkspaceMigration()
{
    var directory = Path.Combine(Path.GetTempPath(), "Pandora.Tests", Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(directory);
    var tempPath = Path.Combine(directory, "workspace.json");
    File.WriteAllText(tempPath, """
        {
          "schemaVersion": 1,
          "settings": {
            "hideDesktopIconsWhenRunning": true
          },
          "zones": [
            {
              "id": "legacy",
              "name": "Legacy Dock",
              "isVisible": true,
              "bounds": { "x": 11, "y": 22, "width": 333, "height": 222 },
              "appearance": { "accentColor": "#56D6FF", "backgroundColor": "#090E16", "opacity": 0.76, "cornerRadius": 20, "iconSize": 48, "columns": 4, "tabStyle": "segmented" },
              "tabs": [
                { "id": "legacy-main", "name": "Main", "source": "folder", "path": "%USERPROFILE%\\Desktop", "allowNavigation": true }
              ]
            }
          ],
          "rules": []
        }
        """);

    var workspace = new WorkspaceStore(tempPath).LoadOrCreate();
    Assert(workspace.SchemaVersion == WorkspaceMigrator.CurrentSchemaVersion, "Migrated workspace should be on the current schema.");
    Assert(workspace.ActiveLayoutName == "Main", "Migration should create Main layout.");
    var dockStates = workspace.Layouts.Single().DisplayVariants.Single().DockStates;
    Assert(dockStates.Any(state => state.DockId == "legacy"), "Migration should capture legacy dock state.");
    Assert(dockStates.Any(state => state.DockId == "brief"), "Migration should add agent feed dock state.");
    Assert(Directory.EnumerateFiles(directory, $"*.migrated-v{WorkspaceMigrator.CurrentSchemaVersion}.bak").Any(), "Migration should back up old JSON.");
}

static void LayoutSwitching()
{
    var workspace = WorkspaceFactory.CreateDefault();
    var zone = workspace.Zones[0];
    WorkspaceLayoutService.DuplicateLayout(workspace, "Main", "Wide");
    WorkspaceLayoutService.SwitchLayout(workspace, "Wide");
    WorkspaceLayoutService.SetDockBounds(workspace, zone.Id, 222, 333, 444, 555);
    WorkspaceLayoutService.SwitchLayout(workspace, "Main");
    Assert(Math.Abs(zone.Bounds.X - 222) > 0.1, "Main layout should keep its original dock position.");
    WorkspaceLayoutService.SwitchLayout(workspace, "Wide");
    Assert(Math.Abs(zone.Bounds.X - 222) < 0.1, "Wide layout should restore modified dock position.");
}

static void ItemOrderPersistence()
{
    var tempPath = Path.Combine(Path.GetTempPath(), "Pandora.Tests", Guid.NewGuid().ToString("N"), "workspace.json");
    var store = new WorkspaceStore(tempPath);
    var workspace = WorkspaceFactory.CreateDefault();
    var zone = workspace.Zones.First(zone => zone.Tabs.Any(tab => tab.Source == ZoneTabSource.SmartDesktop));
    var tab = zone.Tabs[0];
    var first = Path.Combine(Path.GetTempPath(), "first.lnk");
    var second = Path.Combine(Path.GetTempPath(), "second.lnk");

    WorkspaceLayoutService.SetItemOrder(workspace, zone.Id, tab.Id, [second, first]);
    store.Save(workspace);
    var loaded = store.LoadOrCreate();
    var layout = WorkspaceLayoutService.EnsureActiveLayout(loaded);
    var overrides = WorkspaceLayoutService.GetOverrides(layout, zone.Id, tab.Id);
    Assert(overrides[0].Path.EndsWith("second.lnk", StringComparison.OrdinalIgnoreCase), "First order override should be second path.");
    Assert(overrides[1].Path.EndsWith("first.lnk", StringComparison.OrdinalIgnoreCase), "Second order override should be first path.");
}

static void DockMembershipPersistence()
{
    var tempPath = Path.Combine(Path.GetTempPath(), "Pandora.Tests", Guid.NewGuid().ToString("N"), "workspace.json");
    var store = new WorkspaceStore(tempPath);
    var workspace = WorkspaceFactory.CreateDefault();
    var source = workspace.Zones.First(zone => zone.Id == "launchpad");
    var target = workspace.Zones.First(zone => zone.Id == "build");
    var path = Path.Combine(Path.GetTempPath(), "agent-tool.lnk");

    WorkspaceLayoutService.MoveItem(workspace, path, source.Id, source.Tabs[0].Id, target.Id, target.Tabs[0].Id);
    store.Save(workspace);
    var loaded = store.LoadOrCreate();
    var layout = WorkspaceLayoutService.EnsureActiveLayout(loaded);
    Assert(WorkspaceLayoutService.IsHidden(layout, source.Id, source.Tabs[0].Id, path), "Source dock should hide moved item.");
    Assert(WorkspaceLayoutService.GetOverrides(layout, target.Id, target.Tabs[0].Id).Any(item => WorkspaceLayoutService.PathsEqual(item.Path, path) && !item.IsHidden), "Target dock should contain moved item.");
}

static void DesktopPinPersistence()
{
    var tempPath = Path.Combine(Path.GetTempPath(), "Pandora.Tests", Guid.NewGuid().ToString("N"), "workspace.json");
    var store = new WorkspaceStore(tempPath);
    var workspace = WorkspaceFactory.CreateDefault();
    var path = Path.Combine(Path.GetTempPath(), "desktop-pin.lnk");
    WorkspaceLayoutService.AddDesktopPin(workspace, path, 50, 60, 64);
    store.Save(workspace);

    var loaded = store.LoadOrCreate();
    var pin = WorkspaceLayoutService.EnsureActiveDisplayVariant(loaded).DesktopPins.Single();
    Assert(WorkspaceLayoutService.PathsEqual(pin.Path, path), "Desktop pin path should persist.");
    Assert(Math.Abs(pin.X - 50) < 0.1 && Math.Abs(pin.Y - 60) < 0.1, "Desktop pin position should persist.");
}

static void DisplayVariants()
{
    var workspace = WorkspaceFactory.CreateDefault();
    var display = new DisplayDescriptor
    {
        DeviceName = @"\\.\DISPLAY1",
        IsPrimary = true,
        BoundsX = 0,
        BoundsY = 0,
        BoundsWidth = 800,
        BoundsHeight = 600,
        WorkAreaX = 0,
        WorkAreaY = 0,
        WorkAreaWidth = 800,
        WorkAreaHeight = 560
    };
    var signature = WorkspaceLayoutService.ComputeDisplaySignature([display]);
    Assert(signature == WorkspaceLayoutService.ComputeDisplaySignature([display]), "Display signature should be stable.");
    var key = WorkspaceLayoutService.ComputeDisplayVariantKey(signature);
    workspace.Zones[0].Bounds.X = 10_000;
    workspace.Zones[0].Bounds.Y = 10_000;
    WorkspaceLayoutService.CaptureZoneState(workspace, workspace.Zones[0], null);
    var variant = WorkspaceLayoutService.UseDisplayVariant(workspace, key, signature, [display]);
    var state = variant.DockStates.First(state => state.DockId == workspace.Zones[0].Id);
    Assert(state.Bounds.X < 800 && state.Bounds.Y < 600, "Unknown display variant should clamp dock bounds.");
    Assert(WorkspaceLayoutService.EnsureActiveLayout(workspace).DisplayVariants.Count >= 2, "Using a monitor signature should create a reusable display variant.");
}

static void DisplayVariantKeysIgnoreWorkAreaChanges()
{
    var fullWorkArea = new DisplayDescriptor
    {
        DeviceName = @"\\.\DISPLAY1",
        IsPrimary = true,
        BoundsX = 0,
        BoundsY = 0,
        BoundsWidth = 1920,
        BoundsHeight = 1080,
        WorkAreaX = 0,
        WorkAreaY = 0,
        WorkAreaWidth = 1920,
        WorkAreaHeight = 1080
    };
    var taskbarWorkArea = new DisplayDescriptor
    {
        DeviceName = @"\\.\DISPLAY1",
        IsPrimary = true,
        BoundsX = 0,
        BoundsY = 0,
        BoundsWidth = 1920,
        BoundsHeight = 1080,
        WorkAreaX = 0,
        WorkAreaY = 0,
        WorkAreaWidth = 1920,
        WorkAreaHeight = 1020
    };

    var fullSignature = WorkspaceLayoutService.ComputeDisplaySignature([fullWorkArea]);
    var taskbarSignature = WorkspaceLayoutService.ComputeDisplaySignature([taskbarWorkArea]);
    Assert(fullSignature != taskbarSignature, "Signatures should still record work-area details.");
    Assert(
        WorkspaceLayoutService.ComputeDisplayVariantKey(fullSignature) == WorkspaceLayoutService.ComputeDisplayVariantKey(taskbarSignature),
        "Variant key should stay stable when only the working area changes.");
}

static void LegacyWorkAreaVariantReuse()
{
    var workspace = WorkspaceFactory.CreateDefault();
    var zone = workspace.Zones[0];
    var layout = WorkspaceLayoutService.EnsureActiveLayout(workspace);
    var oldDisplays = new[]
    {
        new DisplayDescriptor { DeviceName = @"\\.\DISPLAY1", IsPrimary = true, BoundsX = 0, BoundsY = 0, BoundsWidth = 1920, BoundsHeight = 1080, WorkAreaX = 0, WorkAreaY = 0, WorkAreaWidth = 1920, WorkAreaHeight = 1020 },
        new DisplayDescriptor { DeviceName = @"\\.\DISPLAY5", IsPrimary = false, BoundsX = 1920, BoundsY = 0, BoundsWidth = 1920, BoundsHeight = 1080, WorkAreaX = 1920, WorkAreaY = 0, WorkAreaWidth = 1920, WorkAreaHeight = 1020 }
    };
    var newDisplays = new[]
    {
        new DisplayDescriptor { DeviceName = @"\\.\DISPLAY1", IsPrimary = true, BoundsX = 0, BoundsY = 0, BoundsWidth = 1920, BoundsHeight = 1080, WorkAreaX = 0, WorkAreaY = 0, WorkAreaWidth = 1920, WorkAreaHeight = 1080 },
        new DisplayDescriptor { DeviceName = @"\\.\DISPLAY5", IsPrimary = false, BoundsX = 1920, BoundsY = 0, BoundsWidth = 1920, BoundsHeight = 1080, WorkAreaX = 1920, WorkAreaY = 0, WorkAreaWidth = 1920, WorkAreaHeight = 1080 }
    };
    var oldSignature = WorkspaceLayoutService.ComputeDisplaySignature(oldDisplays);
    var newSignature = WorkspaceLayoutService.ComputeDisplaySignature(newDisplays);
    var newKey = WorkspaceLayoutService.ComputeDisplayVariantKey(newSignature);

    layout.DisplayVariants.Add(new DisplayLayoutVariant
    {
        Key = "display-old-workarea-key",
        DisplaySignature = oldSignature,
        IsDefault = false,
        LastSeenUtc = DateTime.UtcNow.AddMinutes(-10),
        DockStates =
        [
            new DockLayoutState
            {
                DockId = zone.Id,
                Bounds = new ZoneBounds { X = 2200, Y = 80, Width = 390, Height = 320 }
            }
        ]
    });

    var variant = WorkspaceLayoutService.UseDisplayVariant(workspace, newKey, newSignature, newDisplays);
    var state = variant.DockStates.First(state => state.DockId == zone.Id);
    Assert(variant.Key == newKey, "Legacy work-area variant should be promoted to the stable display key.");
    Assert(state.Bounds.X >= 1920, "Two-screen dock position should be reused instead of cloning a one-screen default.");
}

static void SecondMonitorDockBounds()
{
    var workspace = WorkspaceFactory.CreateDefault();
    var displays = new[]
    {
        new DisplayDescriptor
        {
            DeviceName = @"\\.\DISPLAY1",
            IsPrimary = true,
            BoundsX = 0,
            BoundsY = 0,
            BoundsWidth = 1536,
            BoundsHeight = 864,
            WorkAreaX = 0,
            WorkAreaY = 0,
            WorkAreaWidth = 1536,
            WorkAreaHeight = 816
        },
        new DisplayDescriptor
        {
            DeviceName = @"\\.\DISPLAY5",
            IsPrimary = false,
            BoundsX = 1536,
            BoundsY = 0,
            BoundsWidth = 1536,
            BoundsHeight = 864,
            WorkAreaX = 1536,
            WorkAreaY = 0,
            WorkAreaWidth = 1536,
            WorkAreaHeight = 816
        }
    };
    var signature = WorkspaceLayoutService.ComputeDisplaySignature(displays);
    var key = WorkspaceLayoutService.ComputeDisplayVariantKey(signature);
    var zone = workspace.Zones[0];
    zone.Bounds = new ZoneBounds { X = 1980, Y = 40, Width = 390, Height = 320 };
    WorkspaceLayoutService.CaptureZoneState(workspace, zone, null);

    var variant = WorkspaceLayoutService.UseDisplayVariant(workspace, key, signature, displays);
    var state = variant.DockStates.First(state => state.DockId == zone.Id);

    Assert(state.Bounds.X >= 1536, "Dock should remain on the secondary display after clamping.");
    Assert(Math.Abs(state.Bounds.X - 1980) < 0.1, "Valid secondary-display X coordinate should not drift during reload.");
}

static void OversizedDockBoundsRepair()
{
    var workspace = WorkspaceFactory.CreateDefault();
    var display = new DisplayDescriptor
    {
        DeviceName = @"\\.\DISPLAY1",
        IsPrimary = true,
        BoundsX = 0,
        BoundsY = 0,
        BoundsWidth = 1920,
        BoundsHeight = 1080,
        WorkAreaX = 0,
        WorkAreaY = 0,
        WorkAreaWidth = 1920,
        WorkAreaHeight = 1020
    };
    var signature = WorkspaceLayoutService.ComputeDisplaySignature([display]);
    var key = WorkspaceLayoutService.ComputeDisplayVariantKey(signature);
    var zone = workspace.Zones[0];
    zone.Bounds = new ZoneBounds { X = 0, Y = 0, Width = 1920, Height = 1020 };
    WorkspaceLayoutService.CaptureZoneState(workspace, zone, null);

    var variant = WorkspaceLayoutService.UseDisplayVariant(workspace, key, signature, [display]);
    var state = variant.DockStates.First(state => state.DockId == zone.Id);

    Assert(state.Bounds.Width <= WorkspaceLayoutService.DefaultRestoredDockWidth + 0.1, "Fullscreen-width dock should restore to a normal width.");
    Assert(state.Bounds.Height <= WorkspaceLayoutService.DefaultRestoredDockHeight + 0.1, "Fullscreen-height dock should restore to a normal height.");
    Assert(workspace.Zones[0].Bounds.Width <= WorkspaceLayoutService.DefaultRestoredDockWidth + 0.1, "Applied zone bounds should also be repaired.");
}

static void DockSearch()
{
    Assert(DockSearchMatcher.Matches("Visual Studio Code", "lnk", @"C:\Users\Noah\Desktop\Code.lnk", "studio"), "Search should match display name.");
    Assert(DockSearchMatcher.Matches("Report", "pdf", @"C:\Temp\Report.pdf", "PDF"), "Search should match extension.");
    Assert(DockSearchMatcher.Matches("Tool", "exe", @"C:\Dev\Pandora\tool.exe", "pandora"), "Search should match path.");
    Assert(!DockSearchMatcher.Matches("Tool", "exe", @"C:\Dev\tool.exe", "music"), "Search should reject unrelated text.");
}

static void MusicScanner()
{
    var root = Path.Combine(Path.GetTempPath(), "Pandora.Tests", Guid.NewGuid().ToString("N"), "Music");
    Directory.CreateDirectory(root);
    Directory.CreateDirectory(Path.Combine(root, "Focus", "Deep"));
    File.WriteAllText(Path.Combine(root, "root.mp3"), string.Empty);
    File.WriteAllText(Path.Combine(root, "Focus", "Deep", "space.wav"), string.Empty);
    File.WriteAllText(Path.Combine(root, "Focus", "notes.txt"), string.Empty);

    var library = MusicLibraryScanner.Scan(root);
    Assert(library.Playlists.Any(playlist => playlist.Id == MusicLibraryScanner.AllTracksPlaylistId), "Scanner should include All Tracks.");
    Assert(library.Playlists.Any(playlist => playlist.Id == "Focus/Deep"), "Scanner should include nested playlist folder.");
    Assert(library.Playlists.Single(playlist => playlist.Id == MusicLibraryScanner.AllTracksPlaylistId).Tracks.Count == 2, "Scanner should skip unsupported files.");
}

static void AgentFeedStorePersistence()
{
    var root = Path.Combine(Path.GetTempPath(), "Pandora.Tests", Guid.NewGuid().ToString("N"), "AgentFeeds");
    var store = new AgentFeedStore(root);
    var document = new AgentFeedDocument
    {
        FeedId = "morning-brief",
        Title = "Morning Brief",
        SourceAgent = "test",
        Status = AgentFeedStatus.Attention,
        Revision = "rev-1",
        UpdatedUtc = DateTime.UtcNow,
        Summary = "Check the day.",
        Sections =
        [
            new AgentFeedSection
            {
                Id = "tasks",
                Title = "What Needs Attention",
                Kind = AgentFeedSectionKind.Checklist,
                Items =
                [
                    new AgentFeedItem { Id = "email-1", Text = "Review important email", Priority = AgentFeedPriority.P1 }
                ]
            }
        ]
    };

    store.SaveFeed(document);
    var loaded = store.LoadFeed("morning-brief")!;
    var state = store.LoadState();
    Assert(store.IsUnread(loaded, state), "New feed should be unread.");
    Assert(store.CountOpenAttentionItems(loaded, state) == 1, "Open attention item should count.");

    store.MarkRead("morning-brief");
    state = store.LoadState();
    Assert(!store.IsUnread(loaded, state), "Mark read should clear unread state.");

    store.SetItemState("morning-brief", "email-1", AgentFeedItemState.Done);
    state = store.LoadState();
    Assert(store.CountOpenAttentionItems(loaded, state) == 0, "Completed checklist item should stop counting as open attention.");
}

static void AgentFeedLimits()
{
    var oversizedSummary = new AgentFeedDocument
    {
        FeedId = "morning-brief",
        Title = "Morning Brief",
        Summary = new string('x', AgentFeedStore.MaxSummaryLength + 1)
    };
    var errors = AgentFeedStore.Validate(oversizedSummary);
    Assert(errors.Any(error => error.Contains("summary", StringComparison.OrdinalIgnoreCase)), "Oversized summary should fail validation.");

    var tooManyItems = new AgentFeedDocument
    {
        FeedId = "morning-brief",
        Title = "Morning Brief",
        Sections =
        [
            new AgentFeedSection
            {
                Id = "tasks",
                Title = "Tasks",
                Items = Enumerable.Range(0, AgentFeedStore.MaxItems + 1)
                    .Select(index => new AgentFeedItem { Id = $"item-{index}", Text = "Review item" })
                    .ToList()
            }
        ]
    };
    errors = AgentFeedStore.Validate(tooManyItems);
    Assert(errors.Any(error => error.Contains("total items", StringComparison.OrdinalIgnoreCase)), "Too many feed items should fail validation.");

    var root = Path.Combine(Path.GetTempPath(), "Pandora.Tests", Guid.NewGuid().ToString("N"), "AgentFeeds");
    Directory.CreateDirectory(root);
    var store = new AgentFeedStore(root);
    AssertThrows<InvalidOperationException>(
        () => store.SetItemState("morning-brief", new string('x', AgentFeedStore.MaxIdentifierLength + 1), AgentFeedItemState.Done),
        "Oversized local checklist item ids should be rejected.");

    var hugeFeedPath = Path.Combine(root, "huge-feed.json");
    using (var stream = File.Create(hugeFeedPath))
    {
        stream.SetLength(AgentFeedStore.MaxFeedFileBytes + 1L);
    }

    errors = store.ValidateFile(hugeFeedPath);
    Assert(errors.Any(error => error.Contains("too large", StringComparison.OrdinalIgnoreCase)), "Oversized feed file should fail validation before parsing.");

    var malformedFeedPath = Path.Combine(root, "malformed-but-bounded.json");
    File.WriteAllText(malformedFeedPath, """
        {
          "feedId": "morning-brief",
          "title": "Morning Brief",
          "sections": [
            null,
            {
              "id": "tasks",
              "title": "Tasks",
              "items": [
                null,
                {
                  "id": "one",
                  "text": "Review the day",
                  "links": [null, { "label": "Open", "target": "https://example.com" }]
                }
              ]
            }
          ]
        }
        """);
    var loaded = store.LoadFeedFile(malformedFeedPath);
    Assert(loaded.Sections.Count == 1, "Malformed null sections should be filtered.");
    Assert(loaded.Sections[0].Items.Count == 1, "Malformed null items should be filtered.");
    Assert(loaded.Sections[0].Items[0].Links.Count == 1, "Malformed null links should be filtered.");

    Directory.CreateDirectory(root);
    File.WriteAllText(store.StatePath, """{ "schemaVersion": 1, "feeds": null }""");
    Assert(store.LoadState().Feeds.Count == 0, "Malformed state feeds should not crash state loading.");
}

static void AgentFeedCli()
{
    var directory = Path.Combine(Path.GetTempPath(), "Pandora.Tests", Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(directory);
    var workspacePath = Path.Combine(directory, "workspace.json");
    new WorkspaceStore(workspacePath).Save(WorkspaceFactory.CreateDefault());
    var checklistPath = Path.Combine(directory, "checklist.json");
    File.WriteAllText(checklistPath, """["Review Needs Review queue","Check calendar conflicts"]""");
    var repoRoot = FindRepoRoot();

    RunCli(repoRoot, workspacePath, ["agent-feed", "publish", "morning-brief", "--title", "Morning Brief", "--summary", "Two things need attention.", "--checklist-file", checklistPath, "--status", "attention"], "Published");
    RunCli(repoRoot, workspacePath, ["agent-feed", "list"], "morning-brief");
    RunCli(repoRoot, workspacePath, ["agent-feed", "show", "morning-brief"], "Two things need attention.");
    RunCli(repoRoot, workspacePath, ["agent-feed", "mark-read", "morning-brief"], "marked read");
    var firstItemId = new AgentFeedStore(Path.Combine(directory, "AgentFeeds")).LoadFeed("morning-brief")!.Sections.Single(s => s.Kind == AgentFeedSectionKind.Checklist).Items[0].Id;
    RunCli(repoRoot, workspacePath, ["agent-feed", "complete", "morning-brief", firstItemId], "completed");
    RunCli(repoRoot, workspacePath, ["agent-feed", "validate", Path.Combine(directory, "AgentFeeds", "morning-brief.json")], "OK");
}

static void CliValidation()
{
    var tempPath = Path.Combine(Path.GetTempPath(), "Pandora.Tests", Guid.NewGuid().ToString("N"), "workspace.json");
    new WorkspaceStore(tempPath).Save(WorkspaceFactory.CreateDefault());
    var repoRoot = FindRepoRoot();
    RunCli(repoRoot, tempPath, ["workspace", "validate"], "OK");
    RunCli(repoRoot, tempPath, ["dock", "set-expansion", "build", "bottom"], "bottom");
    RunCli(repoRoot, tempPath, ["layout", "variants"], "default");
    RunCli(repoRoot, tempPath, ["audio", "sfx", "off"], "disabled");
    RunCli(repoRoot, tempPath, ["audio", "music", "off"], "disabled");
}

static void RunCli(string repoRoot, string workspacePath, string[] command, string expectedOutput)
{
    var startInfo = new ProcessStartInfo("dotnet")
    {
        WorkingDirectory = repoRoot,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false
    };
    startInfo.ArgumentList.Add("run");
    startInfo.ArgumentList.Add("--project");
    startInfo.ArgumentList.Add(Path.Combine(repoRoot, "src", "Pandora.Cli", "Pandora.Cli.csproj"));
    startInfo.ArgumentList.Add("--");
    startInfo.ArgumentList.Add("--workspace");
    startInfo.ArgumentList.Add(workspacePath);
    foreach (var arg in command)
    {
        startInfo.ArgumentList.Add(arg);
    }

    using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Could not start pandoractl test.");
    var outputTask = process.StandardOutput.ReadToEndAsync();
    var errorTask = process.StandardError.ReadToEndAsync();
    if (!process.WaitForExit(30_000))
    {
        process.Kill(entireProcessTree: true);
        process.WaitForExit(5000);
        throw new TimeoutException("CLI fixture did not exit within 30 seconds.");
    }
    var output = outputTask.GetAwaiter().GetResult();
    var error = errorTask.GetAwaiter().GetResult();
    Assert(process.ExitCode == 0, $"CLI command failed: {string.Join(' ', command)} {output} {error}");
    Assert(output.Contains(expectedOutput, StringComparison.OrdinalIgnoreCase), $"CLI output should contain {expectedOutput}.");
}

static string FindRepoRoot()
{
    var current = new DirectoryInfo(AppContext.BaseDirectory);
    while (current is not null)
    {
        if (File.Exists(Path.Combine(current.FullName, "Pandora.sln")))
        {
            return current.FullName;
        }

        current = current.Parent;
    }

    throw new InvalidOperationException("Could not find repository root.");
}

static void Assert(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}

static void AssertThrows<TException>(Action body, string message)
    where TException : Exception
{
    try
    {
        body();
    }
    catch (TException)
    {
        return;
    }

    throw new InvalidOperationException(message);
}
