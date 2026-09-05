namespace Pandora.Core;

public static class DesktopItemCatalog
{
    private static readonly string[] DevKeywords =
    [
        "visual studio", "code", "cursor", "windsurf", "github", "docker", "postman",
        "mysql", "mongo", "postgres", "pgadmin", "db browser", "sqlite", "oracle",
        "virtualbox", "azure data", "linear", "replit", "lm studio", "terminus",
        "filezilla", "zed", "robocode"
    ];

    private static readonly string[] CreativeKeywords =
    [
        "canva", "vegas", "magix", "sound forge", "music", "obs", "google docs",
        "google sheets", "google slides", "grammarly", "obsidian", "tiled",
        "tableau", "agentfit", "comet"
    ];

    private static readonly string[] GamesKeywords =
    [
        "steam", "ea", "epic", "curseforge", "vortex", "lunar", "minecraft",
        "pokemon", "starfield", "paradox", "playstation", "game", "games",
        "robocode", "cheat engine"
    ];

    private static readonly string[] WebKeywords =
    [
        "browser", "edge", "chrome", "tor", "lockdown", "zoom", "slack",
        "discord", "google drive", "webull", "tunnelbear"
    ];

    private static readonly string[] UtilityKeywords =
    [
        "formatter", "eraser", "nvidia", "secure", "vpn", "browser", "db browser",
        "travel maps", "mod organizer", "random name", "pothole", "sd card",
        "recycle bin", "tmp", "terminal", "termius"
    ];

    private static readonly string[] IgnoredNames =
    [
        "pandora", "pandora settings", "desktop.ini"
    ];

    public static IReadOnlyList<string> GetDesktopDirectories()
    {
        var directories = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory)
        };

        return directories
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(PathExpander.Expand)
            .Where(Directory.Exists)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public static IReadOnlyList<FileSystemInfo> Enumerate(DesktopItemGroup group)
    {
        return GetDesktopDirectories()
            .SelectMany(path => SafeEnumerate(path))
            .Where(info => !ShouldIgnore(info))
            .Where(info => MatchesGroup(info, group))
            .GroupBy(info => CleanDisplayName(info.Name), StringComparer.OrdinalIgnoreCase)
            .Select(grouping => grouping.First())
            .OrderBy(info => info.Attributes.HasFlag(FileAttributes.Directory) ? 0 : 1)
            .ThenBy(info => CleanDisplayName(info.Name), StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public static string CleanDisplayName(string name)
    {
        var extension = Path.GetExtension(name);
        if (extension.Equals(".lnk", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".url", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".appref-ms", StringComparison.OrdinalIgnoreCase))
        {
            return Path.GetFileNameWithoutExtension(name);
        }

        return name;
    }

    public static DesktopItemGroup Classify(FileSystemInfo info)
    {
        if (info.Attributes.HasFlag(FileAttributes.Directory))
        {
            return DesktopItemGroup.Folders;
        }

        if (!IsLauncher(info))
        {
            return DesktopItemGroup.Files;
        }

        var name = CleanDisplayName(info.Name);
        if (ContainsAny(name, DevKeywords))
        {
            return DesktopItemGroup.Dev;
        }

        if (ContainsAny(name, CreativeKeywords))
        {
            return DesktopItemGroup.Creative;
        }

        if (ContainsAny(name, GamesKeywords))
        {
            return DesktopItemGroup.Games;
        }

        if (ContainsAny(name, WebKeywords))
        {
            return DesktopItemGroup.Web;
        }

        if (ContainsAny(name, UtilityKeywords))
        {
            return DesktopItemGroup.Utilities;
        }

        return DesktopItemGroup.Apps;
    }

    private static bool MatchesGroup(FileSystemInfo info, DesktopItemGroup group)
    {
        return group switch
        {
            DesktopItemGroup.All => true,
            DesktopItemGroup.Apps => IsLauncher(info),
            DesktopItemGroup.Folders => info.Attributes.HasFlag(FileAttributes.Directory),
            DesktopItemGroup.Files => !info.Attributes.HasFlag(FileAttributes.Directory) && !IsLauncher(info),
            _ => Classify(info) == group
        };
    }

    private static IEnumerable<FileSystemInfo> SafeEnumerate(string directory)
    {
        IEnumerable<string> paths;
        try
        {
            paths = Directory.EnumerateFileSystemEntries(directory);
        }
        catch
        {
            return [];
        }

        return paths
            .Select(CreateFileSystemInfo)
            .Where(info => info is not null)
            .Cast<FileSystemInfo>();
    }

    private static FileSystemInfo? CreateFileSystemInfo(string path)
    {
        if (Directory.Exists(path))
        {
            return new DirectoryInfo(path);
        }

        return File.Exists(path) ? new FileInfo(path) : null;
    }

    private static bool ShouldIgnore(FileSystemInfo info)
    {
        if (info.Attributes.HasFlag(FileAttributes.Hidden) || info.Attributes.HasFlag(FileAttributes.System))
        {
            return true;
        }

        var cleaned = CleanDisplayName(info.Name);
        return IgnoredNames.Any(ignored => cleaned.Contains(ignored, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsLauncher(FileSystemInfo info)
    {
        if (info.Attributes.HasFlag(FileAttributes.Directory))
        {
            return false;
        }

        var extension = Path.GetExtension(info.Name);
        return extension.Equals(".lnk", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".url", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".appref-ms", StringComparison.OrdinalIgnoreCase);
    }

    private static bool ContainsAny(string value, IEnumerable<string> keywords)
    {
        return keywords.Any(keyword => value.Contains(keyword, StringComparison.OrdinalIgnoreCase));
    }
}
