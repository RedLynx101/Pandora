using System.IO;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Text.Json;
using Pandora.App;
using Pandora.Core;

namespace Pandora.App.Tests;

internal static partial class Program
{
    private static void LifecycleReloadSafety()
    {
        using var fixture = new ManagerFixture(Path.Combine(_fixturePath, "lifecycle-reload"));
        var manager = fixture.Manager;
        var original = manager.Workspace;
        var originalBytes = File.ReadAllBytes(fixture.Store.WorkspacePath);
        var closed = 0;
        var opened = 0;
        var prepared = 0;
        var errors = new List<string>();
        manager.StorageError += errors.Add;

        bool Reload(Action<Workspace>? prepare = null) => InvokeLifecycle<bool>(manager, "ReloadCore",
            (Func<Workspace>)fixture.Store.LoadReadOnly,
            prepare ?? (_ => prepared++),
            (Action)(() => closed++),
            (Action)(() => opened++));

        // Only this fixture is replaced. Failed reads must never close the old
        // windows, recreate the file, or apply even a partially read workspace.
        File.WriteAllText(fixture.Store.WorkspacePath, "{broken");
        Assert(!Reload() && !Reload(), "Malformed reload should fail recoverably.");
        Assert(ReferenceEquals(original, manager.Workspace) && closed == 0 && opened == 0 && prepared == 0,
            "Malformed reload touched the working workspace or windows.");
        Assert(errors.Count == 1 && manager.LastStorageError is not null, "Repeated identical reload errors should be reported once.");
        Assert(File.ReadAllText(fixture.Store.WorkspacePath) == "{broken", "Reload replaced invalid source with defaults.");

        File.WriteAllBytes(fixture.Store.WorkspacePath, originalBytes);
        Assert(!Reload(_ => throw new IOException("Fixture display-state save denied.")), "Preparation failure was reported as success.");
        Assert(ReferenceEquals(original, manager.Workspace) && closed == 0 && opened == 0,
            "Preparation failure tore down the previous workspace.");

        var candidate = fixture.Store.LoadReadOnly();
        candidate.Settings.GlassOpacity = 0.73;
        var steps = new List<string>();
        var succeeded = InvokeLifecycle<bool>(manager, "ReloadCore",
            (Func<Workspace>)(() => candidate),
            (Action<Workspace>)(replacement => { fixture.Store.Save(replacement); steps.Add("prepared"); }),
            (Action)(() => { Assert(ReferenceEquals(original, manager.Workspace), "Workspace switched before old-window teardown."); steps.Add("closed"); }),
            (Action)(() => { Assert(ReferenceEquals(candidate, manager.Workspace), "New windows would use the old workspace."); steps.Add("opened"); }));
        Assert(succeeded && steps.SequenceEqual(new[] { "prepared", "closed", "opened" }), "Reload did not complete in preparation/teardown/open order.");
        Assert(manager.LastStorageError is null, "Successful reload did not clear its recoverable error.");

        File.WriteAllText(fixture.Store.WorkspacePath, "{broken");
        var previousErrors = errors.Count;
        Assert(!Reload() && errors.Count == previousErrors + 1, "A failure after recovery was incorrectly suppressed.");
        File.Delete(fixture.Store.WorkspacePath);
        Assert(!Reload() && !File.Exists(fixture.Store.WorkspacePath), "Missing workspace reload silently recreated the file.");
        Assert(ReferenceEquals(candidate, manager.Workspace) && closed == 0 && opened == 0, "Missing file discarded the last working workspace.");

        var programmingFailure = new InvalidOperationException("Fixture programming error.");
        try
        {
            InvokeLifecycle<bool>(manager, "ReloadCore", (Func<Workspace>)(() => throw programmingFailure),
                (Action<Workspace>)(_ => { }), (Action)(() => { }), (Action)(() => { }));
            throw new Exception("Programming error was swallowed as a storage error.");
        }
        catch (InvalidOperationException ex) when (ReferenceEquals(ex, programmingFailure)) { }
    }

    private static void LifecyclePersistenceSafety()
    {
        using var fixture = new ManagerFixture(Path.Combine(_fixturePath, "lifecycle-persistence"));
        var manager = fixture.Manager;
        var diskBefore = File.ReadAllBytes(fixture.Store.WorkspacePath);
        var savedAt = manager.LastSuccessfulSaveUtc;
        manager.Workspace.Settings.GlassOpacity = 0.74;
        using (var guard = new FileStream(fixture.Store.WorkspacePath, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            ExpectLifecycleStorageFailure(manager.SaveAppearanceSettings);
            ExpectLifecycleStorageFailure(manager.Save);
            ExpectLifecycleStorageFailure(() => Invoke(manager, "ApplyCurrentDisplayVariant", manager.Workspace));
        }
        Assert(manager.LastSuccessfulSaveUtc == savedAt, "A failed direct save advanced last-success bookkeeping.");
        Assert(File.ReadAllBytes(fixture.Store.WorkspacePath).SequenceEqual(diskBefore), "A rejected save changed the destination.");

        manager.SaveAppearanceSettings();
        Assert(manager.LastSuccessfulSaveUtc > savedAt && fixture.Store.IsCurrent(manager.Workspace), "Successful save did not establish the current-content fingerprint.");
        var lastSuccess = manager.LastSuccessfulSaveUtc;
        var externalStore = new WorkspaceStore(fixture.Store.WorkspacePath);
        var external = externalStore.LoadReadOnly();
        external.Settings.GlassOpacity = 0.83;
        externalStore.Save(external);
        // Preserve the timestamp to demonstrate that watcher suppression is
        // content-based even for an external edit indistinguishable by time.
        File.SetLastWriteTimeUtc(fixture.Store.WorkspacePath, lastSuccess);
        Assert(!fixture.Store.IsCurrent(manager.Workspace), "External contents were mistaken for a local write.");
        ExpectLifecycleStorageFailure(manager.SaveAppearanceSettings);
        Assert(manager.LastSuccessfulSaveUtc == lastSuccess, "Conflicting save advanced success bookkeeping.");
        Assert(externalStore.LoadReadOnly().Settings.GlassOpacity == 0.83, "Stale in-memory state overwrote the external edit.");
    }

    private static void LifecycleExpectedStorageErrors()
    {
        var classifier = typeof(DesktopZoneManager).GetMethod("IsExpectedStorageFailure", BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Missing storage exception classifier.");
        bool Expected(Exception error) => (bool)classifier.Invoke(null, [error])!;
        Assert(Expected(new IOException()) && Expected(new UnauthorizedAccessException()) && Expected(new JsonException()),
            "Recoverable persistence errors must reach the controlled UI boundary.");
        Assert(!Expected(new NullReferenceException()) && !Expected(new InvalidOperationException()) && !Expected(new ArgumentException()),
            "Programming errors must not be hidden by the persistence boundary.");
    }

    private static T InvokeLifecycle<T>(object target, string method, params object[] arguments)
    {
        var member = target.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Missing lifecycle hook: " + method);
        try { return (T)member.Invoke(target, arguments)!; }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        {
            ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
            throw;
        }
    }

    private static void ExpectLifecycleStorageFailure(Action operation)
    {
        try { operation(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { return; }
        throw new InvalidOperationException("Expected a persistence failure to propagate to the caller.");
    }
}
