using System.Text.Json;
using Pandora.Core;

public static class WorkspaceSafetyTests
{
    private const string LegacyJson = """
        {"schemaVersion":1,"settings":{},"zones":[{"id":"legacy","name":"Keep my dock","bounds":{"x":11,"y":22,"width":333,"height":222},"tabs":[]}],"rules":[]}
        """;

    public static void Run()
    {
        SnapshotConflicts(); ExternalRewriteAndDeletion(); ReadOnlyAndLegacyImport();
        InvalidInputsStayIntact(); WriteAndBackupFailures(); ConcurrentMigration(); AbsolutePathsAndBackups();
        DetachedSavesPreserveNewEdits(); RecoveryRotationAndExplicitRestore(); OversizedWorkspaceIsRejected();
    }

    private static void DetachedSavesPreserveNewEdits()
    {
        using var fixture = new Fixture();
        var store = new WorkspaceStore(fixture.Path);
        var source = store.LoadOrCreate();
        source.Settings.GlassOpacity = 0.71;
        var snapshot = store.CreateSaveSnapshot(source);
        source.Settings.GlassOpacity = 0.82;
        Task.Run(() => store.Save(snapshot)).GetAwaiter().GetResult();
        store.AcceptSavedSnapshot(source, snapshot);
        Assert(source.Settings.GlassOpacity == 0.82 && store.LoadReadOnly().Settings.GlassOpacity == 0.71,
            "Background persistence replaced an edit made after capture.");
        store.Save(source);
        Assert(store.LoadReadOnly().Settings.GlassOpacity == 0.82, "Follow-up save lost the latest edit.");
        Throws<WorkspaceConflictException>(() => store.AcceptSavedSnapshot(source, snapshot), "A saved snapshot must be accepted once only.");

        source.Settings.GlassOpacity = 0.83;
        snapshot = store.CreateSaveSnapshot(source);
        var external = store.LoadReadOnly();
        external.Settings.GlassOpacity = 0.91;
        store.Save(external);
        Throws<WorkspaceConflictException>(() => store.Save(snapshot), "A background snapshot overwrote an external edit.");
        Assert(store.LoadReadOnly().Settings.GlassOpacity == 0.91, "External edit was lost.");
    }

    private static void RecoveryRotationAndExplicitRestore()
    {
        using var fixture = new Fixture();
        var store = new WorkspaceStore(fixture.Path);
        var workspace = store.LoadOrCreate();
        var original = File.ReadAllBytes(fixture.Path);
        var manual = store.Backup("keep-manual");
        workspace.Settings.GlassOpacity = 0.67;
        store.Save(workspace);
        var first = Directory.GetFiles(store.RecoveryDirectory, "*.json").Single();
        Assert(File.ReadAllBytes(first).SequenceEqual(original), "First recovery slot did not preserve the prior workspace.");
        workspace.Settings.GlassOpacity = 0.68;
        store.Save(workspace);
        Assert(Directory.GetFiles(store.RecoveryDirectory, "*.json").Length == 1, "Rapid saves grew recovery history.");
        for (var i = 0; i < 8; i++)
        {
            foreach (var slot in Directory.GetFiles(store.RecoveryDirectory, "*.json"))
                File.SetLastWriteTimeUtc(slot, DateTime.UtcNow.AddMinutes(-20 - i));
            workspace.Settings.GlassOpacity = 0.70 + i * 0.01;
            store.Save(workspace);
        }
        Assert(Directory.GetFiles(store.RecoveryDirectory, "*.json").Length == 5 && File.Exists(manual), "Recovery rotation exceeded five slots or removed a manual backup.");
        foreach (var slot in Directory.GetFiles(store.RecoveryDirectory, "*.json")) new WorkspaceStore(slot).LoadReadOnly();
        var beforeRestore = File.ReadAllBytes(fixture.Path);
        var invalid = System.IO.Path.Combine(fixture.Root, "invalid.json");
        File.WriteAllText(invalid, "{broken");
        Throws<JsonException>(() => store.RestoreFromBackup(invalid), "Invalid recovery source was accepted.");
        Assert(File.ReadAllBytes(fixture.Path).SequenceEqual(beforeRestore), "Invalid restore changed the workspace.");
        var restored = store.RestoreFromBackup(manual);
        Assert(store.IsCurrent(restored) && File.ReadAllBytes(fixture.Path).SequenceEqual(original), "Explicit restore lost original content or persistence stamp.");
        Assert(fixture.Backups().Any(path => File.ReadAllBytes(path).SequenceEqual(beforeRestore)), "Restore did not retain the replaced workspace.");
    }

    private static void OversizedWorkspaceIsRejected()
    {
        using var fixture = new Fixture();
        using (var stream = File.Create(fixture.Path)) stream.SetLength(8 * 1024 * 1024 + 1);
        Throws<WorkspaceValidationException>(() => new WorkspaceStore(fixture.Path).LoadReadOnly(), "Oversized source was not bounded before parsing.");
        Assert(new FileInfo(fixture.Path).Length == 8 * 1024 * 1024 + 1, "Rejected oversized input was modified.");
    }

    private static void SnapshotConflicts()
    {
        using var fixture = new Fixture();
        var store = new WorkspaceStore(fixture.Path);
        store.Save(WorkspaceFactory.CreateDefault());
        var first = store.LoadOrCreate();
        var second = store.LoadOrCreate(); // A stamp on the store rather than the object is insufficient.
        first.Settings.Theme = "Midnight";
        store.Save(first);
        var committed = File.ReadAllBytes(fixture.Path);
        second.Settings.ReduceMotion = true;
        Throws<WorkspaceConflictException>(() => store.Save(second), "A stale same-store snapshot must conflict.");
        Assert(committed.SequenceEqual(File.ReadAllBytes(fixture.Path)), "Conflict overwrote a successful edit.");
        Assert(store.IsCurrent(first) && !store.IsCurrent(second), "Self-write detection must use each snapshot's bytes.");
        first.Settings.ReduceMotion = true;
        store.Save(first);
        store.Save(first); // Successful writes advance the expected version.

        var otherStore = new WorkspaceStore(fixture.Path);
        var other = otherStore.LoadReadOnly();
        first.Settings.Theme = "Limestone";
        store.Save(first);
        Throws<WorkspaceConflictException>(() => otherStore.Save(other), "Independent stores must reject stale snapshots.");
        Throws<WorkspaceConflictException>(() => store.Save(WorkspaceFactory.CreateDefault()), "Untracked models must not overwrite existing data.");
    }

    private static void ExternalRewriteAndDeletion()
    {
        using var fixture = new Fixture();
        var store = new WorkspaceStore(fixture.Path);
        var loaded = store.LoadOrCreate();
        var writeTime = File.GetLastWriteTimeUtc(fixture.Path);
        File.AppendAllText(fixture.Path, " ");
        File.SetLastWriteTimeUtc(fixture.Path, writeTime);
        var external = File.ReadAllBytes(fixture.Path);
        Assert(!store.IsCurrent(loaded), "External edits with the same timestamp must be detected.");
        Throws<WorkspaceConflictException>(() => store.Save(loaded), "External bytes must not be overwritten.");
        Assert(external.SequenceEqual(File.ReadAllBytes(fixture.Path)), "External edit was lost.");
        loaded = store.LoadReadOnly();
        File.Delete(fixture.Path);
        Assert(!store.IsCurrent(loaded), "Deleted destination is no longer current.");
        Throws<WorkspaceConflictException>(() => store.Save(loaded), "A tracked deletion must not silently recreate old data.");
        Assert(!File.Exists(fixture.Path), "Conflict recreated an externally deleted workspace.");
    }

    private static void ReadOnlyAndLegacyImport()
    {
        using var fixture = new Fixture();
        File.WriteAllText(fixture.Path, LegacyJson);
        var original = File.ReadAllBytes(fixture.Path);
        var store = new WorkspaceStore(fixture.Path);
        var snapshot = store.LoadReadOnly();
        Assert(snapshot.SchemaVersion == WorkspaceMigrator.CurrentSchemaVersion && snapshot.Zones.Any(z => z.Id == "legacy"), "Read-only loading must prepare a usable migrated snapshot.");
        Assert(WorkspaceValidation.Validate(snapshot).Count == 0, "Prepared snapshot should validate.");
        Assert(original.SequenceEqual(File.ReadAllBytes(fixture.Path)), "Read-only load rewrote a legacy file.");
        Assert(Directory.GetFiles(fixture.Root).Length == 1, "Read-only load created a lock or backup.");
        store.Save(snapshot);
        Assert(fixture.Backups().Any(p => original.SequenceEqual(File.ReadAllBytes(p))), "Saving a read-only legacy snapshot must first preserve its original bytes.");

        var missingPath = System.IO.Path.Combine(fixture.Root, "absent", "workspace.json");
        var missingStore = new WorkspaceStore(missingPath, fixture.Path);
        Throws<IOException>(() => missingStore.LoadReadOnly(), "Read-only loading must not create or import a missing workspace.");
        Assert(!Directory.Exists(System.IO.Path.GetDirectoryName(missingPath)), "Read-only loading created its missing parent.");
        var legacyBytes = File.ReadAllBytes(fixture.Path);
        var imported = missingStore.LoadOrCreate();
        Assert(imported.Zones.Any(z => z.Id == "legacy") && legacyBytes.SequenceEqual(File.ReadAllBytes(fixture.Path)), "Legacy import changed its source or lost content.");

        var badLegacy = System.IO.Path.Combine(fixture.Root, "bad-legacy.json");
        File.WriteAllText(badLegacy, "{broken");
        var destination = System.IO.Path.Combine(fixture.Root, "new-workspace.json");
        Throws<JsonException>(() => new WorkspaceStore(destination, badLegacy).LoadOrCreate(), "Invalid legacy data must not cause a fresh workspace fallback.");
        Assert(!File.Exists(destination) && File.ReadAllText(badLegacy) == "{broken", "Invalid import changed original or destination data.");
    }

    private static void InvalidInputsStayIntact()
    {
        using var fixture = new Fixture();
        var invalidDocuments = new[]
        {
            "null", "{broken", "{\"schemaVersion\":8,\"future\":{\"keep\":true}}",
            "{\"settings\":null}", "{\"zones\":null}", "{\"rules\":null}", "{\"layouts\":null}",
            "{\"zones\":[null]}", "{\"zones\":[{\"bounds\":null}]}", "{\"zones\":[{\"tabs\":null}]}",
            "{\"zones\":[{\"tabs\":[null]}]}", "{\"layouts\":[null]}",
            "{\"layouts\":[{\"displayVariants\":null}]}", "{\"layouts\":[{\"displayVariants\":[null]}]}",
            "{\"layouts\":[{\"displayVariants\":[{\"dockStates\":null}]}]}",
            "{\"layouts\":[{\"displayVariants\":[{\"music\":null}]}]}",
            "{\"zones\":[{\"id\":\"same\"},{\"id\":\"SAME\"}]}",
            "{\"zones\":[{\"bounds\":{\"width\":-1}}]}"
        };
        foreach (var json in invalidDocuments)
        {
            File.WriteAllText(fixture.Path, json);
            var original = File.ReadAllBytes(fixture.Path);
            var filesBefore = Directory.GetFiles(fixture.Root).Order().ToArray();
            ThrowsExpectedDataError(() => new WorkspaceStore(fixture.Path).LoadReadOnly(), json);
            Assert(filesBefore.SequenceEqual(Directory.GetFiles(fixture.Root).Order()), "Invalid read-only load created filesystem entries.");
            ThrowsExpectedDataError(() => new WorkspaceStore(fixture.Path).LoadOrCreate(), json);
            Assert(original.SequenceEqual(File.ReadAllBytes(fixture.Path)), "Invalid input was replaced: " + json);
        }
        Assert(fixture.Backups().Length == 0 && !Directory.GetFiles(fixture.Root, "*.tmp").Any(), "Invalid reads created backups or temporary replacements.");
        var future = new Workspace { SchemaVersion = WorkspaceMigrator.CurrentSchemaVersion + 1 };
        Throws<WorkspaceValidationException>(() => WorkspaceMigrator.MigrateToCurrent(future), "Direct migration must reject future schemas.");
        Assert(future.SchemaVersion == WorkspaceMigrator.CurrentSchemaVersion + 1, "Future model was downgraded before rejection.");
        var numeric = WorkspaceFactory.CreateDefault(); numeric.Zones[0].Bounds.X = double.NaN;
        Throws<WorkspaceValidationException>(() => new WorkspaceStore(fixture.Path).Save(numeric), "Nonfinite geometry must fail before serialization.");
        // Existing optional migration defaults still work.
        var old = new Workspace { SchemaVersion = 1 }; old.Settings.Audio = null!; old.Settings.DockTheme = null!;
        WorkspaceMigrator.MigrateToCurrent(old);
        Assert(old.Settings.Audio is not null && old.Settings.DockTheme == "Classic", "Supported optional omissions stopped migrating.");
    }

    private static void WriteAndBackupFailures()
    {
        using var fixture = new Fixture();
        File.WriteAllText(fixture.Path, LegacyJson);
        var original = File.ReadAllBytes(fixture.Path);
        using (var unavailable = new FileStream(fixture.Path, FileMode.Open, FileAccess.Read, FileShare.None))
            Throws<IOException>(() => new WorkspaceStore(fixture.Path).LoadOrCreate(), "Temporary read sharing failures must surface without recovery.");
        Assert(original.SequenceEqual(File.ReadAllBytes(fixture.Path)), "Read sharing failure replaced valid data.");
        var store = new WorkspaceStore(fixture.Path) { BeforeBackupForTests = () => throw new IOException("Fixture backup failure") };
        Throws<IOException>(() => store.LoadOrCreate(), "Backup failure must surface.");
        Assert(original.SequenceEqual(File.ReadAllBytes(fixture.Path)), "Backup failure replaced valid data.");
        store.BeforeBackupForTests = null;
        store.BeforeReplaceForTests = () => throw new IOException("Fixture replacement failure");
        Throws<IOException>(() => store.LoadOrCreate(), "Migration replacement failure must surface.");
        Assert(original.SequenceEqual(File.ReadAllBytes(fixture.Path)), "Migration replacement failure reset valid data.");
        Assert(fixture.Backups().All(p => original.SequenceEqual(File.ReadAllBytes(p))), "Backup did not preserve original bytes.");
        Assert(!Directory.GetFiles(fixture.Root, "*.tmp").Any(), "Failed migration left a temporary replacement.");
        store.BeforeReplaceForTests = null;
        var loaded = store.LoadOrCreate();
        var saved = File.ReadAllBytes(fixture.Path);
        loaded.Settings.ReduceMotion = true;
        store.BeforeReplaceForTests = () => throw new UnauthorizedAccessException("Fixture write denial");
        Throws<UnauthorizedAccessException>(() => store.Save(loaded), "Normal write denial must surface.");
        Assert(saved.SequenceEqual(File.ReadAllBytes(fixture.Path)) && store.IsCurrent(loaded), "Failed save changed disk or advanced its stamp.");
        store.BeforeReplaceForTests = null;
        store.Save(loaded);
        Assert(store.LoadReadOnly().Settings.ReduceMotion, "An I/O failure with unchanged disk must remain retryable.");
    }

    private static void ConcurrentMigration()
    {
        using var fixture = new Fixture();
        File.WriteAllText(fixture.Path, LegacyJson);
        var original = File.ReadAllBytes(fixture.Path);
        var tasks = Enumerable.Range(0, 4).Select(_ => Task.Run(() => new WorkspaceStore(fixture.Path).LoadOrCreate())).ToArray();
        Task.WhenAll(tasks).GetAwaiter().GetResult();
        var verifier = new WorkspaceStore(fixture.Path);
        Assert(tasks.All(t => t.Result.Zones.Any(z => z.Id == "legacy") && verifier.IsCurrent(t.Result)), "Concurrent migration produced stale/default snapshots.");
        Assert(fixture.Backups().Length == 1 && original.SequenceEqual(File.ReadAllBytes(fixture.Backups()[0])), "Concurrent migration must preserve the original exactly once.");
    }

    private static void AbsolutePathsAndBackups()
    {
        using var fixture = new Fixture();
        var previousDirectory = Environment.CurrentDirectory;
        try
        {
            Environment.CurrentDirectory = fixture.Root;
            var relative = new WorkspaceStore("workspace.json");
            Assert(relative.WorkspacePath == fixture.Path && relative.WorkspacePath == new WorkspaceStore(System.IO.Path.Combine(".", "workspace.json")).WorkspacePath, "Workspace basenames must resolve to an absolute local path.");
            relative.LoadOrCreate();
            var first = relative.Backup(); var second = relative.Backup();
            Assert(first != second && File.ReadAllBytes(first).SequenceEqual(File.ReadAllBytes(second)), "Repeated backups must be distinct and preserve identical source bytes.");
        }
        finally { Environment.CurrentDirectory = previousDirectory; }
    }

    private static void ThrowsExpectedDataError(Action action, string description)
    {
        try { action(); }
        catch (Exception ex) when (ex is WorkspaceValidationException or JsonException) { return; }
        throw new InvalidOperationException("Expected a descriptive data error: " + description);
    }
    private static void Throws<T>(Action action, string message) where T : Exception
    {
        try { action(); } catch (T) { return; }
        throw new InvalidOperationException(message);
    }
    private static void Assert(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }

    private sealed class Fixture : IDisposable
    {
        private readonly string _base = System.IO.Path.GetFullPath(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "Pandora.WorkspaceSafety.Tests"));
        private readonly string _id = Guid.NewGuid().ToString("N");
        public string Root { get; }
        public string Path => System.IO.Path.Combine(Root, "workspace.json");
        public Fixture() { Root = System.IO.Path.Combine(_base, _id); Directory.CreateDirectory(Root); }
        public string[] Backups() => Directory.GetFiles(Root, "*.bak");
        public void Dispose()
        {
            var canonical = System.IO.Path.GetFullPath(Root);
            var expected = System.IO.Path.GetFullPath(System.IO.Path.Combine(_base, _id));
            if (canonical != expected || !canonical.StartsWith(_base + System.IO.Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || !Guid.TryParseExact(System.IO.Path.GetFileName(canonical), "N", out _))
                throw new InvalidOperationException("Refusing unsafe fixture cleanup.");
            Directory.Delete(canonical, recursive: true);
        }
    }
}
