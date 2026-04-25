namespace CustomFences.Core;

public static class WorkspaceFactory
{
    public static Workspace CreateDefault()
    {
        var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);

        var workspace = new Workspace
        {
            SchemaVersion = WorkspaceMigrator.CurrentSchemaVersion,
            ActiveLayoutName = WorkspaceLayoutService.DefaultLayoutName,
            Settings = new AppSettings
            {
                AttachWindowsToDesktop = false,
                HideDesktopIconsWhenRunning = true,
                DefaultDropAction = DropAction.Copy,
                EnableRuleAutomation = false
            },
            Zones =
            [
                CreateSmartZone(
                    "launchpad",
                    "Orbit Launchpad",
                    "#56D6FF",
                    980,
                    70,
                    390,
                    360,
                    [
                        ("All Apps", DesktopItemGroup.Apps),
                        ("Web", DesktopItemGroup.Web),
                        ("Utilities", DesktopItemGroup.Utilities)
                    ]),
                CreateSmartZone(
                    "build",
                    "Build Dock",
                    "#9F8CFF",
                    980,
                    455,
                    390,
                    360,
                    [
                        ("Dev", DesktopItemGroup.Dev),
                        ("Files", DesktopItemGroup.Files)
                    ]),
                CreateSmartZone(
                    "create",
                    "Create Dock",
                    "#F37BC3",
                    1395,
                    70,
                    390,
                    310,
                    [
                        ("Creative", DesktopItemGroup.Creative),
                        ("Web", DesktopItemGroup.Web)
                    ]),
                CreateSmartZone(
                    "play",
                    "Play Dock",
                    "#F2C94C",
                    1395,
                    405,
                    390,
                    360,
                    [
                        ("Games", DesktopItemGroup.Games),
                        ("Folders", DesktopItemGroup.Folders)
                    ])
            ]
        };

        workspace.Zones.Add(CreateFolderZone("desktop-inbox", "Desktop Inbox", desktop, "#5CC8A7", 980, 835, 805, 170));
        workspace.Zones.Add(CreateMusicZone());

        workspace.Rules =
        [
            new RuleDefinition
            {
                Id = "screenshots-to-inbox",
                Name = "Screenshots to Desktop Inbox",
                TargetZoneId = "desktop-inbox",
                Conditions =
                [
                    new RuleCondition { Field = RuleField.FileName, Match = RuleMatch.StartsWith, Value = "Screenshot" }
                ]
            },
            new RuleDefinition
            {
                Id = "pdfs-to-workspace",
                Name = "PDFs to Workspace",
                TargetZoneId = "workspace",
                Conditions =
                [
                    new RuleCondition { Field = RuleField.Extension, Match = RuleMatch.Equals, Value = "pdf" }
                ]
            }
        ];

        workspace.Layouts.Add(WorkspaceLayoutService.CreateProfileFromZones(WorkspaceLayoutService.DefaultLayoutName, workspace));
        return workspace;
    }

    private static ZoneDefinition CreateFolderZone(string id, string name, string path, string accentColor, double x, double y, double width, double height)
    {
        return new ZoneDefinition
        {
            Id = id,
            Name = name,
            Bounds = new ZoneBounds { X = x, Y = y, Width = width, Height = height },
            Appearance = new ZoneAppearance
            {
                AccentColor = accentColor,
                BackgroundColor = "#0B1018",
                Opacity = 0.82,
                CornerRadius = 22,
                IconSize = 42
            },
            Tabs =
            [
                new ZoneTabDefinition
                {
                    Id = $"{id}-main",
                    Name = name,
                    Path = PathExpander.CompressUserPath(path)
                }
            ]
        };
    }

    private static ZoneDefinition CreateSmartZone(
        string id,
        string name,
        string accentColor,
        double x,
        double y,
        double width,
        double height,
        IEnumerable<(string Name, DesktopItemGroup Group)> tabs)
    {
        return new ZoneDefinition
        {
            Id = id,
            Name = name,
            Bounds = new ZoneBounds { X = x, Y = y, Width = width, Height = height },
            Appearance = new ZoneAppearance
            {
                AccentColor = accentColor,
                BackgroundColor = "#090E16",
                Opacity = 0.76,
                CornerRadius = 24,
                IconSize = 48
            },
            Sort = ItemSort.TypeThenName,
            Tabs = tabs.Select(tab => new ZoneTabDefinition
            {
                Id = $"{id}-{tab.Group.ToString().ToLowerInvariant()}",
                Name = tab.Name,
                Source = ZoneTabSource.SmartDesktop,
                DesktopGroup = tab.Group
            }).ToList()
        };
    }

    private static ZoneDefinition CreateMusicZone()
    {
        return new ZoneDefinition
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
    }
}
