using System.Diagnostics;
using CustomFences.Core;

var tests = new List<(string Name, Action Body)>
{
    ("PathExpander expands user profile tokens", PathExpansion),
    ("RuleEngine matches extensions without leading dot sensitivity", ExtensionRule),
    ("RuleEngine requires all conditions", CompoundRule),
    ("WorkspaceStore round-trips default workspace", WorkspaceRoundTrip),
    ("Default workspace includes smart desktop docks", SmartDesktopDefaults),
    ("WorkspaceStore migrates v1 workspace into a named layout", WorkspaceMigration),
    ("Layout switching restores dock state", LayoutSwitching),
    ("Item order overrides persist", ItemOrderPersistence),
    ("Dock membership overrides persist", DockMembershipPersistence),
    ("Desktop pins persist in active layout", DesktopPinPersistence),
    ("Display signatures and variants are stable", DisplayVariants),
    ("Second-monitor dock bounds survive layout clamp", SecondMonitorDockBounds),
    ("Oversized dock bounds are repaired", OversizedDockBoundsRepair),
    ("Dock search matches name, extension, and path", DockSearch),
    ("Music scanner handles playlists and unsupported files", MusicScanner),
    ("orbitdockctl validates a workspace", CliValidation)
};

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
    var tempPath = Path.Combine(Path.GetTempPath(), "CustomFences.Tests", Guid.NewGuid().ToString("N"), "workspace.json");
    var store = new WorkspaceStore(tempPath);
    var workspace = WorkspaceFactory.CreateDefault();
    store.Save(workspace);
    var loaded = store.LoadOrCreate();
    Assert(loaded.SchemaVersion == WorkspaceMigrator.CurrentSchemaVersion, "Workspace should be schema v2.");
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
}

static void WorkspaceMigration()
{
    var directory = Path.Combine(Path.GetTempPath(), "CustomFences.Tests", Guid.NewGuid().ToString("N"));
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
    Assert(workspace.SchemaVersion == WorkspaceMigrator.CurrentSchemaVersion, "Migrated workspace should be schema v3.");
    Assert(workspace.ActiveLayoutName == "Main", "Migration should create Main layout.");
    Assert(workspace.Layouts.Single().DisplayVariants.Single().DockStates.Single().DockId == "legacy", "Migration should capture legacy dock state.");
    Assert(Directory.EnumerateFiles(directory, "*.migrated-v3.bak").Any(), "Migration should back up old JSON.");
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
    var tempPath = Path.Combine(Path.GetTempPath(), "CustomFences.Tests", Guid.NewGuid().ToString("N"), "workspace.json");
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
    var tempPath = Path.Combine(Path.GetTempPath(), "CustomFences.Tests", Guid.NewGuid().ToString("N"), "workspace.json");
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
    var tempPath = Path.Combine(Path.GetTempPath(), "CustomFences.Tests", Guid.NewGuid().ToString("N"), "workspace.json");
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
    Assert(DockSearchMatcher.Matches("Tool", "exe", @"C:\Dev\OrbitDock\tool.exe", "orbitdock"), "Search should match path.");
    Assert(!DockSearchMatcher.Matches("Tool", "exe", @"C:\Dev\tool.exe", "music"), "Search should reject unrelated text.");
}

static void MusicScanner()
{
    var root = Path.Combine(Path.GetTempPath(), "CustomFences.Tests", Guid.NewGuid().ToString("N"), "Music");
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

static void CliValidation()
{
    var tempPath = Path.Combine(Path.GetTempPath(), "CustomFences.Tests", Guid.NewGuid().ToString("N"), "workspace.json");
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
    startInfo.ArgumentList.Add(Path.Combine(repoRoot, "src", "OrbitDock.Cli", "OrbitDock.Cli.csproj"));
    startInfo.ArgumentList.Add("--");
    startInfo.ArgumentList.Add("--workspace");
    startInfo.ArgumentList.Add(workspacePath);
    foreach (var arg in command)
    {
        startInfo.ArgumentList.Add(arg);
    }

    using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Could not start orbitdockctl test.");
    var output = process.StandardOutput.ReadToEnd();
    var error = process.StandardError.ReadToEnd();
    process.WaitForExit(30_000);
    Assert(process.ExitCode == 0, $"CLI command failed: {string.Join(' ', command)} {output} {error}");
    Assert(output.Contains(expectedOutput, StringComparison.OrdinalIgnoreCase), $"CLI output should contain {expectedOutput}.");
}

static string FindRepoRoot()
{
    var current = new DirectoryInfo(AppContext.BaseDirectory);
    while (current is not null)
    {
        if (File.Exists(Path.Combine(current.FullName, "CustomFences.sln")))
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
