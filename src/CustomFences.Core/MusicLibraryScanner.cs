namespace CustomFences.Core;

public static class MusicLibraryScanner
{
    public const string AllTracksPlaylistId = "all";

    private static readonly string[] SupportedExtensions =
    [
        ".mp3",
        ".wav",
        ".wma",
        ".m4a",
        ".flac"
    ];

    public static MusicLibrary Scan(string rootPath)
    {
        var expandedRoot = PathExpander.Expand(rootPath);
        if (string.IsNullOrWhiteSpace(expandedRoot))
        {
            expandedRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyMusic), "OrbitDock");
        }

        if (!Directory.Exists(expandedRoot))
        {
            return new MusicLibrary(expandedRoot, [], $"Music folder unavailable: {expandedRoot}");
        }

        var tracks = SafeEnumerateFiles(expandedRoot)
            .Where(IsSupported)
            .Select(path => CreateTrack(expandedRoot, path))
            .OrderBy(track => track.PlaylistId, StringComparer.OrdinalIgnoreCase)
            .ThenBy(track => track.Title, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var playlists = new List<MusicPlaylist>
        {
            new(AllTracksPlaylistId, "All Tracks", tracks)
        };

        playlists.AddRange(tracks
            .Where(track => !string.Equals(track.PlaylistId, AllTracksPlaylistId, StringComparison.OrdinalIgnoreCase))
            .GroupBy(track => track.PlaylistId, StringComparer.OrdinalIgnoreCase)
            .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
            .Select(group => new MusicPlaylist(group.Key, group.Key, group.ToArray())));

        var status = tracks.Length == 0
            ? "No supported music files found."
            : $"{tracks.Length} music track(s)";
        return new MusicLibrary(expandedRoot, playlists, status);
    }

    public static bool IsSupported(string path)
    {
        var extension = Path.GetExtension(path);
        return SupportedExtensions.Any(supported => supported.Equals(extension, StringComparison.OrdinalIgnoreCase));
    }

    public static IReadOnlyList<string> GetSupportedExtensions()
    {
        return SupportedExtensions;
    }

    private static MusicTrack CreateTrack(string root, string path)
    {
        var relativePath = Path.GetRelativePath(root, path);
        var directory = Path.GetDirectoryName(relativePath) ?? string.Empty;
        var playlistId = string.IsNullOrWhiteSpace(directory) || directory == "."
            ? AllTracksPlaylistId
            : directory.Replace(Path.DirectorySeparatorChar, '/').Replace(Path.AltDirectorySeparatorChar, '/');

        return new MusicTrack(
            path,
            DesktopItemCatalog.CleanDisplayName(Path.GetFileNameWithoutExtension(path)),
            playlistId);
    }

    private static IEnumerable<string> SafeEnumerateFiles(string root)
    {
        try
        {
            return Directory.EnumerateFiles(root, "*.*", SearchOption.AllDirectories);
        }
        catch
        {
            return [];
        }
    }
}

public sealed record MusicLibrary(string RootPath, IReadOnlyList<MusicPlaylist> Playlists, string StatusMessage);

public sealed record MusicPlaylist(string Id, string Name, IReadOnlyList<MusicTrack> Tracks);

public sealed record MusicTrack(string Path, string Title, string PlaylistId);
