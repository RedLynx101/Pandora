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
    Assert(workspace.SchemaVersion == WorkspaceMigrator.CurrentSchemaVersion, "Migrated workspace should be schema v2.");
    Assert(workspace.ActiveLayoutName == "Main", "Migration should create Main layout.");
    Assert(workspace.Layouts.Single().DockStates.Single().DockId == "legacy", "Migration should capture legacy dock state.");
    Assert(Directory.EnumerateFiles(directory, "*.migrated-v2.bak").Any(), "Migration should back up old JSON.");
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
    var pin = WorkspaceLayoutService.EnsureActiveLayout(loaded).DesktopPins.Single();
    Assert(WorkspaceLayoutService.PathsEqual(pin.Path, path), "Desktop pin path should persist.");
    Assert(Math.Abs(pin.X - 50) < 0.1 && Math.Abs(pin.Y - 60) < 0.1, "Desktop pin position should persist.");
}

static void CliValidation()
{
    var tempPath = Path.Combine(Path.GetTempPath(), "CustomFences.Tests", Guid.NewGuid().ToString("N"), "workspace.json");
    new WorkspaceStore(tempPath).Save(WorkspaceFactory.CreateDefault());
    var repoRoot = FindRepoRoot();
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
    startInfo.ArgumentList.Add(tempPath);
    startInfo.ArgumentList.Add("workspace");
    startInfo.ArgumentList.Add("validate");

    using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Could not start orbitdockctl test.");
    var output = process.StandardOutput.ReadToEnd();
    var error = process.StandardError.ReadToEnd();
    process.WaitForExit(30_000);
    Assert(process.ExitCode == 0, $"CLI validate failed: {output} {error}");
    Assert(output.Contains("OK", StringComparison.OrdinalIgnoreCase), "CLI validate should report OK.");
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
