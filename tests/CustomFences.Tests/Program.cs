using CustomFences.Core;

var tests = new List<(string Name, Action Body)>
{
    ("PathExpander expands user profile tokens", PathExpansion),
    ("RuleEngine matches extensions without leading dot sensitivity", ExtensionRule),
    ("RuleEngine requires all conditions", CompoundRule),
    ("WorkspaceStore round-trips default workspace", WorkspaceRoundTrip),
    ("Default workspace includes smart desktop docks", SmartDesktopDefaults)
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

static void Assert(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}
