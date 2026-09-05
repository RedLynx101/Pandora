using System.Diagnostics;
using System.Text;
using OrbitDock.Core;

/// <summary>All writes stay in a new temporary fixture directory; evidence is retained.</summary>
internal static class TransferSafetyTests
{
    public static void Run()
    {
        var root = Path.Combine(Path.GetTempPath(), "Pandora.Transfer.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        OrdinaryTransfers(root);
        PreflightLimit(root);
        ReparseBoundaries(root);
        MusicEnumeration(root);
    }

    private static void OrdinaryTransfers(string root)
    {
        var source = Path.Combine(root, "source");
        Directory.CreateDirectory(Path.Combine(source, "child"));
        File.WriteAllText(Path.Combine(source, "child", "notes.txt"), "source sentinel");
        var target = Path.Combine(root, "copies");
        var copy = SafeFileTransfer.Transfer(source, target, DropAction.Copy);
        Assert(File.ReadAllText(Path.Combine(copy, "child", "notes.txt")) == "source sentinel", "Nested copy lost its content.");
        var second = SafeFileTransfer.Transfer(source, target, DropAction.Copy);
        Assert(second != copy && Directory.Exists(copy) && Directory.Exists(source), "Copy must use a unique destination and preserve source.");
        var file = Path.Combine(root, "move.txt");
        File.WriteAllText(file, "move sentinel");
        var movedFile = SafeFileTransfer.Transfer(file, target, DropAction.Move);
        Assert(!File.Exists(file) && File.ReadAllText(movedFile) == "move sentinel", "Normal file move failed.");
        var movedFolder = SafeFileTransfer.Transfer(source, Path.Combine(root, "moves"), DropAction.Move);
        Assert(!Directory.Exists(source) && File.Exists(Path.Combine(movedFolder, "child", "notes.txt")), "Normal directory move failed.");
        Reject(() => SafeFileTransfer.Transfer(copy, Path.Combine(copy, "inside"), DropAction.Copy));
        Assert(!Directory.Exists(Path.Combine(copy, "inside")), "Self-copy rejection wrote a destination.");
    }

    private static void PreflightLimit(string root)
    {
        var source = Path.Combine(root, "deep");
        var current = source;
        for (var index = 0; index <= SafeFileTransfer.MaximumDepth + 1; index++)
        {
            Directory.CreateDirectory(current);
            current = Path.Combine(current, "d");
        }
        var target = Path.Combine(root, "deep-target");
        Reject(() => SafeFileTransfer.Transfer(source, target, DropAction.Copy));
        Assert(!Directory.Exists(target), "Bounded preflight must finish before any target writes.");
        Reject(() => SafeFileTransfer.Transfer(source, target, DropAction.Move));
        Assert(Directory.Exists(source) && !Directory.Exists(target), "Rejected move changed source/destination.");
    }

    private static void ReparseBoundaries(string root)
    {
        var outside = Path.Combine(root, "outside");
        Directory.CreateDirectory(outside);
        var sentinel = Path.Combine(outside, "sentinel.txt");
        File.WriteAllText(sentinel, "must remain unchanged");
        var source = Path.Combine(root, "linked-source");
        Directory.CreateDirectory(source);
        File.WriteAllText(Path.Combine(source, "first.txt"), "ordinary entry before the link");
        var nestedLink = Path.Combine(source, "nested");
        if (TryLink(nestedLink, outside, directory: true))
        {
            var target = Path.Combine(root, "blocked-copy");
            Reject(() => SafeFileTransfer.Transfer(source, target, DropAction.Copy));
            Reject(() => SafeFileTransfer.Transfer(source, target, DropAction.Move));
            Assert(!Directory.Exists(target) && Directory.Exists(source), "Nested directory link must fail the whole preflight before writes.");
            Reject(() => SafeFileTransfer.Transfer(nestedLink, target, DropAction.Copy));
            Reject(() => SafeFileTransfer.Transfer(nestedLink, target, DropAction.Move));
            Reject(() => SafeFileTransfer.Transfer(Path.Combine(nestedLink, "sentinel.txt"), target, DropAction.Copy));
            Reject(() => SafeFileTransfer.Transfer(Path.Combine(nestedLink, "sentinel.txt"), target, DropAction.Move));
            Assert(!Directory.Exists(target) && Directory.Exists(nestedLink), "Source link rejection changed the source or destination.");
            var ordinary = Path.Combine(root, "ordinary.txt");
            File.WriteAllText(ordinary, "ordinary");
            Reject(() => SafeFileTransfer.Transfer(ordinary, Path.Combine(nestedLink, "new-child"), DropAction.Copy));
            Reject(() => SafeFileTransfer.Transfer(ordinary, Path.Combine(nestedLink, "new-child"), DropAction.Move));
            Assert(File.Exists(ordinary) && !Directory.Exists(Path.Combine(outside, "new-child")), "Destination-ancestor link changed the source or allowed an outside write.");
        }
        var fileSource = Path.Combine(root, "linked-file-source");
        Directory.CreateDirectory(fileSource);
        var fileLink = Path.Combine(fileSource, "linked.txt");
        if (TryLink(fileLink, sentinel, directory: false))
        {
            var target = Path.Combine(root, "blocked-file-copy");
            Reject(() => SafeFileTransfer.Transfer(fileLink, target, DropAction.Copy));
            Reject(() => SafeFileTransfer.Transfer(fileSource, target, DropAction.Copy));
            Assert(!Directory.Exists(target), "Nested file link was followed or target was written before preflight.");
        }
        Assert(File.ReadAllText(sentinel) == "must remain unchanged", "Rejected transfers changed the external fixture sentinel.");
    }

    private static void MusicEnumeration(string root)
    {
        var music = Path.Combine(root, "music");
        Directory.CreateDirectory(Path.Combine(music, "Focus"));
        File.WriteAllText(Path.Combine(music, "Focus", "accessible.mp3"), string.Empty);
        File.WriteAllText(Path.Combine(music, "notes.txt"), "not music");
        var linked = TryLink(Path.Combine(music, "loop"), music, directory: true);
        var library = MusicLibraryScanner.Scan(music);
        Assert(library.Playlists.Single(p => p.Id == MusicLibraryScanner.AllTracksPlaylistId).Tracks.Count == 1,
            "Music scan must retain accessible tracks and avoid linked traversal.");
        if (linked) Assert(library.StatusMessage.Contains("skipped", StringComparison.OrdinalIgnoreCase), "Skipped music paths need a visible status.");
        Assert(MusicLibraryScanner.Scan(Path.Combine(root, "absent-music")).StatusMessage.Contains("unavailable", StringComparison.OrdinalIgnoreCase),
            "A missing music folder must produce a recoverable status.");
    }

    private static bool TryLink(string path, string target, bool directory)
    {
        string symbolicLinkFailure;
        try
        {
            if (directory) Directory.CreateSymbolicLink(path, target);
            else File.CreateSymbolicLink(path, target);
            RequireReparsePoint(path);
            return true;
        }
        catch (Exception ex)
        {
            symbolicLinkFailure = ex.Message;
        }

        if (directory && OperatingSystem.IsWindows())
        {
            try
            {
                CreateDirectoryJunction(path, target);
                RequireReparsePoint(path);
                Console.WriteLine("Directory reparse fixture uses a junction: " + path);
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine("SKIP directory reparse fixture: symbolic link unavailable: " + symbolicLinkFailure +
                    "; junction unavailable: " + ex.Message);
                return false;
            }
        }

        Console.WriteLine("SKIP " + (directory ? "directory" : "file") +
            " symbolic-link fixture: link creation unavailable: " + symbolicLinkFailure);
        return false;
    }

    private static void CreateDirectoryJunction(string path, string target)
    {
        // Junctions exercise the same directory boundary without requiring symbolic-link privileges.
        var command = "New-Item -ItemType Junction -ErrorAction Stop -Path '" + path.Replace("'", "''", StringComparison.Ordinal) +
            "' -Target '" + target.Replace("'", "''", StringComparison.Ordinal) + "' | Out-Null";
        var info = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe"))
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        info.ArgumentList.Add("-NoProfile");
        info.ArgumentList.Add("-NonInteractive");
        info.ArgumentList.Add("-EncodedCommand");
        info.ArgumentList.Add(Convert.ToBase64String(Encoding.Unicode.GetBytes(command)));
        using var process = Process.Start(info) ?? throw new InvalidOperationException("Could not start the fixture junction helper.");
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(10_000))
        {
            try { process.Kill(entireProcessTree: true); process.WaitForExit(2_000); }
            catch (Exception) { /* A fixture-helper failure must not hide the timeout or abort unrelated checks. */ }
            throw new InvalidOperationException("Fixture junction creation timed out.");
        }
        if (!Task.WhenAll(output, error).Wait(2_000))
            throw new InvalidOperationException("Fixture junction helper output did not close.");
        if (process.ExitCode != 0)
            throw new InvalidOperationException("Fixture junction creation failed: " + error.GetAwaiter().GetResult());
    }

    private static void RequireReparsePoint(string path)
    {
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) == 0)
            throw new IOException("Fixture link was not created as a reparse point.");
    }

    private static void Reject(Action action)
    {
        try { action(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return; }
        throw new InvalidOperationException("Unsafe transfer unexpectedly succeeded.");
    }

    private static void Assert(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); }
}
