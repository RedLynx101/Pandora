namespace CustomFences.Core;

public sealed class Workspace
{
    public int SchemaVersion { get; set; } = 1;
    public AppSettings Settings { get; set; } = new();
    public List<ZoneDefinition> Zones { get; set; } = [];
    public List<RuleDefinition> Rules { get; set; } = [];
}

public sealed class AppSettings
{
    public bool AttachWindowsToDesktop { get; set; }
    public bool HideDesktopIconsWhenRunning { get; set; } = true;
    public bool StartWithWindows { get; set; }
    public string PeekHotkey { get; set; } = "Ctrl+Alt+Space";
    public string Theme { get; set; } = "Graphite";
    public DropAction DefaultDropAction { get; set; } = DropAction.Copy;
    public bool EnableRuleAutomation { get; set; }
}

public sealed class ZoneDefinition
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "New Zone";
    public bool IsVisible { get; set; } = true;
    public bool IsLocked { get; set; }
    public bool IsCollapsed { get; set; }
    public ZoneBounds Bounds { get; set; } = new();
    public ZoneAppearance Appearance { get; set; } = new();
    public ItemSort Sort { get; set; } = ItemSort.NameAscending;
    public List<ZoneTabDefinition> Tabs { get; set; } = [];
}

public sealed class ZoneTabDefinition
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "Files";
    public ZoneTabSource Source { get; set; } = ZoneTabSource.Folder;
    public DesktopItemGroup DesktopGroup { get; set; } = DesktopItemGroup.All;
    public string Path { get; set; } = string.Empty;
    public bool AllowNavigation { get; set; } = true;
}

public sealed class ZoneBounds
{
    public double X { get; set; } = 80;
    public double Y { get; set; } = 80;
    public double Width { get; set; } = 360;
    public double Height { get; set; } = 280;
}

public sealed class ZoneAppearance
{
    public string AccentColor { get; set; } = "#4FB3FF";
    public string BackgroundColor { get; set; } = "#121821";
    public double Opacity { get; set; } = 0.88;
    public double CornerRadius { get; set; } = 18;
    public double IconSize { get; set; } = 42;
    public int Columns { get; set; } = 4;
    public TabStyle TabStyle { get; set; } = TabStyle.Segmented;
}

public sealed class RuleDefinition
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "New Rule";
    public bool IsEnabled { get; set; } = true;
    public string TargetZoneId { get; set; } = string.Empty;
    public string? TargetTabId { get; set; }
    public List<RuleCondition> Conditions { get; set; } = [];
}

public sealed class RuleCondition
{
    public RuleField Field { get; set; } = RuleField.Extension;
    public RuleMatch Match { get; set; } = RuleMatch.Equals;
    public string Value { get; set; } = string.Empty;
}

public sealed class RuleCandidate
{
    public RuleCandidate(string path)
    {
        OriginalPath = path;
        FileName = System.IO.Path.GetFileName(path);
        Extension = System.IO.Path.GetExtension(path).TrimStart('.');
        ParentDirectory = System.IO.Path.GetDirectoryName(path) ?? string.Empty;
    }

    public string OriginalPath { get; }
    public string FileName { get; }
    public string Extension { get; }
    public string ParentDirectory { get; }
}

public enum DropAction
{
    Copy,
    Move,
    Shortcut
}

public enum ItemSort
{
    NameAscending,
    NameDescending,
    NewestFirst,
    OldestFirst,
    TypeThenName
}

public enum TabStyle
{
    MenuOnly,
    Flat,
    Segmented,
    Rounded
}

public enum ZoneTabSource
{
    Folder,
    SmartDesktop
}

public enum DesktopItemGroup
{
    All,
    Apps,
    Dev,
    Creative,
    Games,
    Web,
    Utilities,
    Folders,
    Files
}

public enum RuleField
{
    Extension,
    FileName,
    ParentPath
}

public enum RuleMatch
{
    Equals,
    Contains,
    StartsWith,
    EndsWith,
    Regex
}
