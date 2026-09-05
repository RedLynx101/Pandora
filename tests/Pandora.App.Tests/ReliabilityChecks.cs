using System.IO;
using System.Reflection;
using System.Windows.Controls;
using Pandora.App;
using Pandora.Core;

namespace Pandora.App.Tests;

internal static partial class Program
{
    private static void ProjectStartupLifecycle()
    {
        var root = Path.Combine(_fixturePath, "project-startup");
        Directory.CreateDirectory(root);
        var registryPath = Path.Combine(root, "projects.json");
        var registry = new ProjectRegistryStore(registryPath);
        var source = WriteDashboard(root, "startup");
        var sourceBytes = File.ReadAllBytes(source);
        registry.Register(source);
        using var control = new ProjectsControl(registryPath);
        Assert(!control.IsLoaded, "Startup fixture must not rely on a Loaded event.");
        var portfolio = (ProjectPortfolioService)typeof(ProjectsControl).GetField("_portfolio", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(control)!;
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!portfolio.HasRefreshed && DateTime.UtcNow < deadline) Complete(Task.Delay(25));
        Drain();
        Assert(portfolio.HasRefreshed && portfolio.Entries.Count(e => e.IsLive) == 1,
            "A never-loaded Projects host did not reconcile its registered source.");
        Assert(Find<TextBlock>(control, "ProjectsSummary").Text.StartsWith("1 live project", StringComparison.Ordinal),
            "Startup result did not reach the control through Changed.");

        var registryBytes = File.ReadAllBytes(registryPath);
        File.WriteAllText(registryPath, "{broken");
        Complete(control.RefreshAsync());
        Assert(Find<TextBlock>(control, "ProjectsSummary").Text.Contains("refresh failed", StringComparison.Ordinal),
            "A registry failure looked like zero live projects.");
        Capture(ProjectSurface(control), "projects-registry-unavailable", 680, 600);
        File.WriteAllBytes(registryPath, registryBytes);
        Complete(control.RefreshAsync());
        Assert(Find<TextBlock>(control, "ProjectsSummary").Text.StartsWith("1 live project", StringComparison.Ordinal),
            "Projects did not recover after registry repair.");
        Assert(File.ReadAllBytes(source).SequenceEqual(sourceBytes), "Lifecycle refresh changed its read-only dashboard.");
        // Multiple callers must all observe completion, including timer/UI races.
        Complete(Task.WhenAll(Enumerable.Range(0, 12).Select(_ => portfolio.RefreshAsync())));
        Assert(!portfolio.IsRefreshing && portfolio.RegistryError is null, "Concurrent refresh callers returned before reconciliation finished.");
    }

    private static void PlacementSaveLifecycle()
    {
        using var fixture = new ManagerFixture(Path.Combine(_fixturePath, "placement-save"));
        var manager = fixture.Manager;
        var zone = manager.Workspace.Zones[0];
        zone.IsCollapsed = true;
        var originalHeight = zone.Bounds.Height;
        var bytes = File.ReadAllBytes(fixture.Store.WorkspacePath);
        manager.BeginPlacementGesture();
        for (var i = 0; i < 100; i++) { zone.Bounds.X = 30 + i; manager.SavePlacement(); }
        Complete(Task.Delay(400));
        Assert(File.ReadAllBytes(fixture.Store.WorkspacePath).SequenceEqual(bytes), "A movement event wrote the workspace during the gesture.");
        manager.EndPlacementGesture();
        Complete(Task.Delay(400));
        Invoke(manager, "FinishPlacementSave");
        var saved = fixture.Store.LoadReadOnly().Zones.Single(z => z.Id == zone.Id);
        Assert(saved.Bounds.X == 129 && saved.IsCollapsed && saved.Bounds.Height == originalHeight,
            "Gesture save lost final placement, collapsed state, or expanded height.");

        // Deterministically pause a worker write, edit the owning UI model, then
        // prove accepting the saved snapshot advances metadata without replacing it.
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var hook = typeof(WorkspaceStore).GetProperty("BeforeReplaceForTests", BindingFlags.Instance | BindingFlags.NonPublic)!;
        hook.SetValue(fixture.Store, (Action)(() => { entered.Set(); if (!release.Wait(TimeSpan.FromSeconds(10))) throw new IOException("Fixture write release timed out."); }));
        try
        {
            zone.Bounds.X = 160;
            manager.SavePlacement();
            Invoke(manager, "StartPlacementSave");
            Assert(entered.Wait(TimeSpan.FromSeconds(5)), "Worker did not reach the controlled write boundary.");
            zone.Bounds.X = 190;
            manager.SavePlacement();
            release.Set();
            Invoke(manager, "FinishPlacementSave");
            Assert(zone.Bounds.X == 190, "Background completion replaced newer UI geometry.");
            manager.Workspace.Settings.GlassOpacity = 0.79;
            manager.Save(); // A synchronous editor serializes behind movement persistence.
            saved = fixture.Store.LoadReadOnly().Zones.Single(z => z.Id == zone.Id);
            Assert(saved.Bounds.X == 190 && fixture.Store.LoadReadOnly().Settings.GlassOpacity == 0.79,
                "Synchronous settings and pending movement were saved out of order.");
        }
        finally { release.Set(); hook.SetValue(fixture.Store, null); }

        using var settings = new SettingsFixtureForRecovery(manager);
    }

    private sealed class SettingsFixtureForRecovery : IDisposable
    {
        private readonly SettingsWindow _window;
        public SettingsFixtureForRecovery(DesktopZoneManager manager)
        {
            _window = new SettingsWindow(manager);
            var navigation = Find<ListBox>(_window, "SettingsNavigation");
            navigation.SelectedItem = navigation.Items.Cast<ListBoxItem>().Single(item => Equals(item.Tag, "Desktop"));
            Capture(_window.Content as System.Windows.FrameworkElement ?? throw new InvalidOperationException("Missing settings content."), "settings-recovery-full", 960, 980);
            var scroll = Find<ScrollViewer>(_window, "SettingsScrollViewer");
            scroll.ScrollToBottom();
            Drain();
            Capture(_window.Content as System.Windows.FrameworkElement ?? throw new InvalidOperationException("Missing settings content."), "settings-recovery-scrolled", 960, 740);
        }
        public void Dispose() => _window.Close();
    }
}
