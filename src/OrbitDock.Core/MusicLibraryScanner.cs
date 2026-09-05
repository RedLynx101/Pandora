namespace OrbitDock.Core;

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

        try
        {
            if (!SafeFileTransfer.RequireOrdinaryPath(expandedRoot).HasFlag(FileAttributes.Directory))
                return new MusicLibrary(expandedRoot, [], $"Music folder unavailable: {expandedRoot}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        { return new MusicLibrary(expandedRoot, [], $"Music folder unavailable: {ex.Message}"); }

        var files = SafeEnumerateFiles(expandedRoot, out var skipped, out var limited);
        var tracks = files
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
        if (skipped > 0) status += $" · {skipped} inaccessible, changed, or linked item(s) skipped";
        if (limited) status += " · Scan limit reached; remaining items skipped";
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

    private static IReadOnlyList<string> SafeEnumerateFiles(string root, out int skipped, out bool limited)
    {
        var files = new List<string>();
        var pending = new Stack<(string Path, int Depth)>();
        pending.Push((root, 0));
        skipped = 0;
        limited = false;
        var visited = 0;
        while (pending.TryPop(out var directory))
        {
            try
            {
                SafeFileTransfer.RequireOrdinaryPath(directory.Path);
                foreach (var path in Directory.EnumerateFileSystemEntries(directory.Path))
                {
                    if (++visited > SafeFileTransfer.MaximumEntries) { limited = true; return files; }
                    try
                    {
                        var attributes = SafeFileTransfer.RequireOrdinaryPath(path);
                        if (attributes.HasFlag(FileAttributes.Directory))
                        {
                            if (directory.Depth >= SafeFileTransfer.MaximumDepth) { limited = true; continue; }
                            pending.Push((path, directory.Depth + 1));
                        }
                        else if (IsSupported(path)) files.Add(path);
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    { skipped++; }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            { skipped++; }
        }
        return files;
    }
}

public sealed record MusicLibrary(string RootPath, IReadOnlyList<MusicPlaylist> Playlists, string StatusMessage);

public sealed record MusicPlaylist(string Id, string Name, IReadOnlyList<MusicTrack> Tracks);

public sealed record MusicTrack(string Path, string Title, string PlaylistId);
