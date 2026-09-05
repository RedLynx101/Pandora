using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Pandora.Core;

/// <summary>Regression checks use synthetic dashboards and isolated local filesystem fixtures only.</summary>
public static class ProjectSafetyTests
{
    private static readonly JsonSerializerOptions RegistryOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true };

    public static void Run()
    {
        IdentifierBounds();
        CheckpointIsolation();
        RegistryWriteCeiling();
        InitialOversizedSource().GetAwaiter().GetResult();
        DisallowedSourceIsolation().GetAwaiter().GetResult();
    }

    private static void IdentifierBounds()
    {
        Action<JsonObject>[] setIdentifiers =
        [
            state => state["dashboardId"] = new string('a', 65_536),
            state => state["projectId"] = new string('a', 65_536),
            state => state["task"]!["id"] = new string('a', 65_536),
            state => state["plan"]!["id"] = new string('a', 65_536),
            state => state["directorSessionId"] = new string('a', 65_536),
            state => state["task"]!["currentPhaseId"] = new string('a', 65_536),
            state => state["phases"]![1]!["workPackages"]![0]!["dependsOn"] = new JsonArray(new string('a', 65_536))
        ];
        foreach (var set in setIdentifiers)
        {
            var state = MetisTests.Fixture();
            set(state);
            var error = Throws<MetisValidationException>(() => MetisReader.Extract(MetisTests.Html(state)));
            Assert(error.Message.Contains("at most", StringComparison.Ordinal), "Identifier bounds precede reference resolution.");
        }
        var boundary = MetisTests.Fixture();
        boundary["dashboardId"] = new string('a', MetisReader.MaxIdentifierLength);
        Assert(MetisReader.Extract(MetisTests.Html(boundary)).DashboardId.Length == MetisReader.MaxIdentifierLength, "The documented identifier boundary remains valid.");
        boundary["dashboardId"] = "valid-looking\n";
        Throws<MetisValidationException>(() => MetisReader.Extract(MetisTests.Html(boundary)));
    }

    private static void CheckpointIsolation()
    {
        using var fixture = new LocalFixture();
        var store = new ProjectRegistryStore(fixture.File("projects.json"));
        var bad = store.Register(WriteDashboard(fixture.File("bad.html"), "bad"));
        var good = store.Register(WriteDashboard(fixture.File("good.html"), "good"));
        var checkpoint = ProjectRegistryStore.Checkpoint(MetisReader.Extract(MetisTests.Html(MetisTests.Fixture())));
        var oversized = checkpoint with { DashboardId = new string('a', 65_536) };
        var before = File.ReadAllBytes(store.RegistryPath);
        var rejected = store.Accept(new Dictionary<string, ProjectCheckpoint> { [bad.Id] = oversized });
        Assert(rejected.ContainsKey(bad.Id) && before.SequenceEqual(File.ReadAllBytes(store.RegistryPath)), "An invalid checkpoint leaves the original registry bytes untouched.");
        rejected = store.Accept(new Dictionary<string, ProjectCheckpoint> { [bad.Id] = oversized, [good.Id] = checkpoint });
        var entries = store.Load();
        Assert(rejected.ContainsKey(bad.Id) && !rejected.ContainsKey(good.Id), "An invalid checkpoint does not reject an independent valid checkpoint.");
        Assert(entries.Single(e => e.Id == bad.Id).LastAccepted is null && entries.Single(e => e.Id == good.Id).LastAccepted == checkpoint, "Only the valid checkpoint is persisted.");
    }

    private static void RegistryWriteCeiling()
    {
        using var fixture = new LocalFixture();
        var store = new ProjectRegistryStore(fixture.File("projects.json"));
        // Stored missing sources need only have valid lexical paths. No long path is created or read.
        var entries = Enumerable.Range(0, ProjectRegistryStore.MaxRegistrations).Select(index => new ProjectRegistration(
            Guid.NewGuid().ToString("N"), fixture.File($"missing-{index}-" + new string('a', 7_500) + ".html"), Expanded: true)).ToArray();
        byte[] Serialize() => JsonSerializer.SerializeToUtf8Bytes(new { version = 1, dashboards = entries }, RegistryOptions);
        var padding = ProjectRegistryStore.MaxRegistryBytes - Serialize().Length;
        Assert(padding > 0, "Synthetic registry starts below its byte ceiling.");
        entries[0] = entries[0] with { Path = entries[0].Path[..^5] + new string('a', padding) + ".html" };
        var before = Serialize();
        Assert(before.Length == ProjectRegistryStore.MaxRegistryBytes, "Fixture exactly reaches the serialized UTF-8 ceiling.");
        File.WriteAllBytes(store.RegistryPath, before);
        Assert(store.Load().Count == ProjectRegistryStore.MaxRegistrations, "A registry at the byte ceiling remains readable.");

        // JSON false is one byte longer than true; this public preference update exercises the final write guard.
        Throws<InvalidDataException>(() => store.SetExpanded(entries[0].Id, false));
        Assert(before.SequenceEqual(File.ReadAllBytes(store.RegistryPath)), "An oversized registry update preserves the exact prior bytes.");
        var checkpoint = ProjectRegistryStore.Checkpoint(MetisReader.Extract(MetisTests.Html(MetisTests.Fixture())));
        var rejected = store.Accept(new Dictionary<string, ProjectCheckpoint> { [entries[0].Id] = checkpoint });
        Assert(rejected.ContainsKey(entries[0].Id) && before.SequenceEqual(File.ReadAllBytes(store.RegistryPath)), "A checkpoint exceeding remaining registry capacity is rejected before replacement.");
        Assert(!Directory.EnumerateFiles(fixture.Root, "*.tmp").Any(), "Rejected updates leave no temporary replacement files.");
        store.Remove(entries[0].Id);
        Assert(store.Load().Count == entries.Length - 1, "Removing a registration remains available at the size ceiling.");
    }

    private static async Task InitialOversizedSource()
    {
        using var fixture = new LocalFixture();
        var store = new ProjectRegistryStore(fixture.File("projects.json"));
        var state = MetisTests.Fixture();
        var longId = new string('a', 65_536);
        state["dashboardId"] = longId; state["projectId"] = longId;
        state["task"]!["id"] = longId; state["plan"]!["id"] = longId;
        var badPath = fixture.File("oversized.html");
        File.WriteAllText(badPath, MetisTests.Html(state), new UTF8Encoding(false));
        var original = File.ReadAllBytes(badPath);
        Assert(original.Length < MetisReader.MaxStateCharacters, "Regression payload fits the overall input limits.");
        var bad = store.Register(badPath);
        var good = store.Register(WriteDashboard(fixture.File("good.html"), "good"));
        using var portfolio = new ProjectPortfolioService(store, TimeSpan.FromMinutes(10));
        await portfolio.RefreshAsync();
        Assert(portfolio.RegistryError is null, "A rejected source does not poison the shared registry.");
        Assert(portfolio.Entries.Single(e => e.Registration.Id == bad.Id).Status == MetisReadStatus.Invalid, "Oversized initial identifiers are an invalid source.");
        Assert(portfolio.Entries.Single(e => e.Registration.Id == good.Id).IsLive, "An independent valid source remains live.");
        Assert(store.Load().Single(e => e.Id == bad.Id).LastAccepted is null, "No oversized checkpoint reaches persistent state.");
        Assert(original.SequenceEqual(File.ReadAllBytes(badPath)), "Invalid dashboard source bytes remain unchanged.");
    }

    private static async Task DisallowedSourceIsolation()
    {
        using var fixture = new LocalFixture();
        var sourceDirectory = fixture.File("source");
        Directory.CreateDirectory(sourceDirectory);
        var sourcePath = WriteDashboard(Path.Combine(sourceDirectory, "dashboard.html"), "bad");
        var original = File.ReadAllBytes(sourcePath);
        var store = new ProjectRegistryStore(fixture.File("projects.json"));
        var bad = store.Register(sourcePath);
        var goodPath = WriteDashboard(fixture.File("good.html"), "good");
        var goodBytes = File.ReadAllBytes(goodPath);
        var good = store.Register(goodPath);
        using (var initial = new ProjectPortfolioService(store, TimeSpan.FromMinutes(10)))
        {
            await initial.RefreshAsync();
            Assert(initial.Entries.All(e => e.IsLive), "Both exact local sources initially read successfully.");
        }

        var targetDirectory = fixture.File("relocated-source");
        Directory.Move(sourceDirectory, targetDirectory);
        CreateDirectoryLink(sourceDirectory, targetDirectory);
        Assert((File.GetAttributes(sourceDirectory) & FileAttributes.ReparsePoint) != 0, "The fixture really replaced the source directory with a link/junction.");
        Assert(store.Load().Count == 2, "Filesystem policy drift does not invalidate registry structure.");
        Throws<IOException>(() => ProjectPath.Validate(sourcePath, requireExists: true));
        Throws<IOException>(() => store.Register(sourcePath));
        using (var portfolio = new ProjectPortfolioService(store, TimeSpan.FromMinutes(10)))
        {
            await portfolio.RefreshAsync();
            Assert(portfolio.RegistryError is null, "A disallowed source does not become a global registry error.");
            Assert(portfolio.Entries.Single(e => e.Registration.Id == bad.Id).Status == MetisReadStatus.ReadError, "The disallowed source reports a per-source read error.");
            Assert(portfolio.Entries.Single(e => e.Registration.Id == good.Id).IsLive, "The healthy source still reconciles.");
            store.SetExpanded(good.Id, true);
            store.Remove(bad.Id);
            Assert(store.Load().Single().Id == good.Id, "The disallowed source remains removable through the normal registry API.");
        }
        Directory.Delete(sourceDirectory, recursive: false); // Unlink only; never recurse through the junction.
        Assert(original.SequenceEqual(File.ReadAllBytes(Path.Combine(targetDirectory, "dashboard.html"))), "Neither reconciliation nor link cleanup changes the target dashboard.");
        Assert(goodBytes.SequenceEqual(File.ReadAllBytes(goodPath)), "The healthy source dashboard remains unchanged.");
    }

    private static string WriteDashboard(string path, string suffix)
    {
        var state = MetisTests.Fixture();
        state["dashboardId"] = "dashboard-" + suffix; state["projectId"] = "project-" + suffix;
        state["plan"]!["id"] = "plan-" + suffix; state["task"]!["id"] = "task-" + suffix;
        File.WriteAllText(path, MetisTests.Html(state), new UTF8Encoding(false));
        return path;
    }

    private static void CreateDirectoryLink(string path, string target)
    {
        if (!OperatingSystem.IsWindows()) { Directory.CreateSymbolicLink(path, target); return; }
        // Junction creation works without the elevated privilege Windows symbolic links can require.
        var info = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe"))
        { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        var command = "New-Item -ItemType Junction -ErrorAction Stop -Path '" + path.Replace("'", "''", StringComparison.Ordinal) +
            "' -Target '" + target.Replace("'", "''", StringComparison.Ordinal) + "' | Out-Null";
        info.ArgumentList.Add("-NoProfile"); info.ArgumentList.Add("-NonInteractive"); info.ArgumentList.Add("-EncodedCommand");
        info.ArgumentList.Add(Convert.ToBase64String(Encoding.Unicode.GetBytes(command)));
        using var process = Process.Start(info) ?? throw new InvalidOperationException("Could not start the fixture junction helper.");
        if (!process.WaitForExit(10_000))
        {
            process.Kill(entireProcessTree: true); process.WaitForExit();
            throw new InvalidOperationException("Fixture junction creation timed out.");
        }
        if (process.ExitCode != 0) throw new InvalidOperationException("Fixture junction creation failed: " + process.StandardError.ReadToEnd());
    }

    private static T Throws<T>(Action action) where T : Exception
    {
        try { action(); }
        catch (T exception) { return exception; }
        throw new InvalidOperationException("Expected " + typeof(T).Name + ".");
    }
    private static void Assert(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }

    private sealed class LocalFixture : IDisposable
    {
        private readonly string _base = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "Pandora.ProjectSafety.Tests"));
        public string Root { get; }
        public LocalFixture() { Root = Path.Combine(_base, Guid.NewGuid().ToString("N")); Directory.CreateDirectory(Root); }
        public string File(string name) => Path.Combine(Root, name);
        public void Dispose()
        {
            var actual = Path.GetFullPath(Root);
            if (!string.Equals(Path.GetDirectoryName(actual), _base, StringComparison.OrdinalIgnoreCase) ||
                !Guid.TryParseExact(Path.GetFileName(actual), "N", out _) ||
                (FileAttributes.ReparsePoint & System.IO.File.GetAttributes(actual)) != 0)
                throw new InvalidOperationException("Refusing unsafe project fixture cleanup.");
            DeleteWithoutFollowingLinks(actual);
        }
        private void DeleteWithoutFollowingLinks(string directory)
        {
            foreach (var entry in Directory.EnumerateFileSystemEntries(directory))
            {
                var actual = Path.GetFullPath(entry);
                if (!actual.StartsWith(Root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Fixture cleanup escaped its original root.");
                var attributes = System.IO.File.GetAttributes(actual);
                if ((attributes & FileAttributes.Directory) == 0) System.IO.File.Delete(actual);
                else if ((attributes & FileAttributes.ReparsePoint) != 0) Directory.Delete(actual, recursive: false);
                else DeleteWithoutFollowingLinks(actual);
            }
            Directory.Delete(directory, recursive: false);
        }
    }
}
