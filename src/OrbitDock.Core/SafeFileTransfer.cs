namespace OrbitDock.Core;

/// <summary>
/// Bounded, non-overwriting file transfers. Rejects reparse points before writing and checks
/// paths again before each operation. These pathname checks cannot eliminate filesystem races.
/// A failed copy may leave partial destination files; source files are never cleaned up on failure.
/// </summary>
public static class SafeFileTransfer
{
    public const int MaximumEntries = 20_000;
    public const int MaximumDepth = 128;

    public static string Transfer(string sourcePath, string targetDirectory, DropAction action)
    {
        var source = Path.TrimEndingDirectorySeparator(Path.GetFullPath(sourcePath));
        var target = Path.TrimEndingDirectorySeparator(Path.GetFullPath(targetDirectory));
        if (string.IsNullOrWhiteSpace(Path.GetFileName(source)))
            throw new IOException("A filesystem root cannot be transferred.");

        var sourceAttributes = RequireOrdinaryPath(source);
        var isDirectory = sourceAttributes.HasFlag(FileAttributes.Directory);
        if (isDirectory && IsSameOrChild(target, source))
            throw new IOException("Cannot copy or move a folder into itself.");
        ValidateDestination(target);
        var entries = Preflight(source, isDirectory);
        var destination = UniqueDestination(target, Path.GetFileName(source));

        // Do not create even the target folder until the complete source tree passed preflight.
        ValidateDestination(target);
        Directory.CreateDirectory(target);
        RequireOrdinaryPath(target);
        ValidateDestination(destination);
        RequireOrdinaryPath(source);

        if (action == DropAction.Move)
        {
            foreach (var entry in entries) RequireOrdinaryPath(entry.Source);
            if (isDirectory) Directory.Move(source, destination);
            else File.Move(source, destination);
            return destination;
        }

        foreach (var entry in entries)
        {
            var output = entry.RelativePath.Length == 0 ? destination : Path.Combine(destination, entry.RelativePath);
            var attributes = RequireOrdinaryPath(entry.Source);
            if (attributes.HasFlag(FileAttributes.Directory) != entry.IsDirectory)
                throw new IOException("Source changed during the transfer.");
            ValidateDestination(output);
            if (TryAttributes(output, out _)) throw new IOException("Destination changed during the transfer.");
            if (entry.IsDirectory) Directory.CreateDirectory(output);
            else File.Copy(entry.Source, output, overwrite: false);
        }
        return destination;
    }

    /// <summary>Checks an existing path and every ancestor without following reparse points.</summary>
    public static FileAttributes RequireOrdinaryPath(string path)
    {
        var fullPath = Path.GetFullPath(path);
        var attributes = File.GetAttributes(fullPath);
        CheckReparse(attributes);
        var parent = Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(fullPath));
        while (!string.IsNullOrEmpty(parent))
        {
            var parentAttributes = File.GetAttributes(parent);
            CheckReparse(parentAttributes);
            if (!parentAttributes.HasFlag(FileAttributes.Directory)) throw new IOException("A path ancestor is not a folder.");
            parent = Path.GetDirectoryName(parent);
        }
        return attributes;
    }

    private static List<Entry> Preflight(string source, bool isDirectory)
    {
        var entries = new List<Entry> { new(source, string.Empty, isDirectory) };
        if (!isDirectory) return entries;
        var pending = new Stack<(string Path, int Depth)>();
        pending.Push((source, 0));
        while (pending.TryPop(out var directory))
        {
            RequireOrdinaryPath(directory.Path);
            // Enumerate lazily so the entry limit also bounds very wide directories.
            foreach (var path in Directory.EnumerateFileSystemEntries(directory.Path))
            {
                if (entries.Count >= MaximumEntries || directory.Depth + 1 > MaximumDepth)
                    throw new IOException($"Transfer exceeds the limit of {MaximumEntries} entries or {MaximumDepth} levels.");
                var attributes = RequireOrdinaryPath(path);
                var childIsDirectory = attributes.HasFlag(FileAttributes.Directory);
                entries.Add(new Entry(path, Path.GetRelativePath(source, path), childIsDirectory));
                if (childIsDirectory) pending.Push((path, directory.Depth + 1));
            }
        }
        // Parents must exist before children when copying a preflighted tree.
        return entries.OrderBy(entry => entry.RelativePath.Count(ch => ch == Path.DirectorySeparatorChar || ch == Path.AltDirectorySeparatorChar))
            .ThenBy(entry => entry.RelativePath.Length == 0 ? 0 : 1).ToList();
    }

    private static void ValidateDestination(string path)
    {
        var current = Path.GetFullPath(path);
        var leaf = true;
        while (!string.IsNullOrEmpty(current))
        {
            if (TryAttributes(current, out var attributes))
            {
                CheckReparse(attributes);
                if (!leaf && !attributes.HasFlag(FileAttributes.Directory)) throw new IOException("A destination ancestor is not a folder.");
            }
            leaf = false;
            current = Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(current));
        }
    }

    private static string UniqueDestination(string target, string name)
    {
        for (var index = 0; index < 10_000; index++)
        {
            var filename = index == 0 ? name : $"{Path.GetFileNameWithoutExtension(name)} ({index}){Path.GetExtension(name)}";
            var candidate = Path.Combine(target, filename);
            if (!TryAttributes(candidate, out _)) return candidate;
        }
        throw new IOException($"Could not find a unique name for {name}.");
    }

    private static bool TryAttributes(string path, out FileAttributes attributes)
    {
        try { attributes = File.GetAttributes(path); return true; }
        catch (FileNotFoundException) { attributes = default; return false; }
        catch (DirectoryNotFoundException) { attributes = default; return false; }
    }

    private static void CheckReparse(FileAttributes attributes)
    {
        if (attributes.HasFlag(FileAttributes.ReparsePoint))
            throw new IOException("Transfers through symbolic links, junctions, or other reparse points are not supported.");
    }

    private static bool IsSameOrChild(string candidate, string parent) =>
        string.Equals(candidate, parent, StringComparison.OrdinalIgnoreCase) ||
        candidate.StartsWith(parent + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    private sealed record Entry(string Source, string RelativePath, bool IsDirectory);
}
