using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using OrbitDock.App;
using OrbitDock.Core;

namespace Pandora.App.Tests;

internal static partial class Program
{
    private static void DockTransferFailurePreservesPin()
    {
        using var fixture = new ManagerFixture(Path.Combine(_fixturePath, "drop-safety"));
        var source = Path.Combine(fixture.Root, "source.txt");
        File.WriteAllText(source, "preserve this source");
        var blocked = Path.Combine(fixture.Root, "not-a-directory");
        File.WriteAllText(blocked, "blocked target sentinel");
        var zone = new ZoneDefinition { Id = "drop-target", IsVisible = false,
            Tabs = [new ZoneTabDefinition { Id = "target", Path = Path.Combine(blocked, "child") }] };
        fixture.Manager.Workspace.Zones.Add(zone);
        var pin = WorkspaceLayoutService.AddDesktopPin(fixture.Manager.Workspace, source, 100, 100);
        fixture.Manager.Save();
        using var vm = new ZoneViewModel(zone, fixture.Manager);
        var window = new ZoneWindow(vm, fixture.Manager);
        try
        {
            Assert(!InvokeResult<bool>(window, "TryMoveDroppedItem", source, null!, null!, pin.Id, 0), "A rejected transfer reported success.");
            Assert(WorkspaceLayoutService.EnsureActiveDisplayVariant(fixture.Manager.Workspace).DesktopPins.Any(p => p.Id == pin.Id),
                "A failed folder transfer removed the source desktop pin.");
            Assert(File.ReadAllText(source) == "preserve this source" && File.ReadAllText(blocked) == "blocked target sentinel",
                "Rejected transfer altered source or target sentinel.");
            zone.Tabs[0].Path = Path.Combine(fixture.Root, "valid-target");
            Assert(InvokeResult<bool>(window, "TryMoveDroppedItem", source, null!, null!, pin.Id, 0), "A normal transfer failed.");
            Assert(!WorkspaceLayoutService.EnsureActiveDisplayVariant(fixture.Manager.Workspace).DesktopPins.Any(p => p.Id == pin.Id)
                && File.ReadAllText(Path.Combine(zone.Tabs[0].Path, "source.txt")) == "preserve this source" && File.Exists(source),
                "Successful default copy did not preserve source bytes and remove only the transferred pin.");
        }
        finally { window.Close(); }
    }

    private static void MusicSelectionRefreshDoesNotWrite()
    {
        using var fixture = new ManagerFixture(Path.Combine(_fixturePath, "music-selection"));
        var music = fixture.Manager.Workspace.Settings.Audio.MusicRootPath;
        Directory.CreateDirectory(music);
        File.WriteAllText(Path.Combine(music, "one.mp3"), string.Empty);
        var second = Path.Combine(music, "two.mp3");
        File.WriteAllText(second, string.Empty);
        var state = WorkspaceLayoutService.EnsureActiveDisplayVariant(fixture.Manager.Workspace).Music;
        state.SelectedTrackPath = second;
        state.SelectedPlaylist = MusicLibraryScanner.AllTracksPlaylistId;
        fixture.Store.Save(fixture.Manager.Workspace);
        var original = File.ReadAllBytes(fixture.Store.WorkspacePath);
        var zone = new ZoneDefinition { Id = "music-state", Kind = ZoneKind.Music, IsVisible = false };
        using var vm = new ZoneViewModel(zone, fixture.Manager);
        var window = new ZoneWindow(vm, fixture.Manager);
        try
        {
            Invoke(window, "RenderMusicControls");
            Drain();
            vm.Refresh();
            Drain();
            Assert(vm.SelectedMusicTrack?.Path == second && state.SelectedTrackPath == second, "Refresh lost the nonfirst remembered track.");
            Assert(File.ReadAllBytes(fixture.Store.WorkspacePath).SequenceEqual(original), "Constructing/rendering/refreshing music wrote workspace state.");
        }
        finally { window.Close(); }
    }

    private static void WatcherRefreshStopsAfterDispose()
    {
        using var fixture = new ManagerFixture(Path.Combine(_fixturePath, "watcher-lifetime"));
        var zone = CreateFixtureDock(fixture, "Watcher lifetime", 1);
        var vm = new ZoneViewModel(zone, fixture.Manager);
        Assert(DockField<List<FileSystemWatcher>>(vm, "_watchers").Count == 1, "Expected one folder watcher.");
        for (var index = 0; index < 25; index++) Invoke(vm, "QueueRefresh");
        Assert(DockField<int>(vm, "_refreshQueued") == 1, "A burst should queue one dispatcher refresh.");
        Drain();
        Assert(DockField<DispatcherTimer>(vm, "_refreshTimer").IsEnabled, "Queued work did not start its debounce interval.");
        Invoke(vm, "QueueRefresh");
        vm.Dispose();
        Drain();
        Invoke(vm, "RefreshTimer_Tick", null!, EventArgs.Empty);
        vm.Refresh();
        Assert(DockField<List<FileSystemWatcher>>(vm, "_watchers").Count == 0 && !DockField<DispatcherTimer>(vm, "_refreshTimer").IsEnabled,
            "Queued work recreated watchers after disposal.");
    }

    private static void FeedRefreshAndActionRaces()
    {
        using var fixture = new ManagerFixture(Path.Combine(_fixturePath, "feed-races"));
        var feed = new AgentFeedDocument { FeedId = "test-feed", Title = "Synthetic checklist", Revision = "one",
            Sections = [new AgentFeedSection { Id = "tasks", Kind = AgentFeedSectionKind.Checklist,
                Items = [new AgentFeedItem { Id = "item", Text = "Fixture task" }] }] };
        fixture.Manager.AgentFeeds.SaveFeed(feed);
        var zone = new ZoneDefinition { Id = "feed-test", Kind = ZoneKind.AgentFeed, IsVisible = false,
            AgentFeed = new AgentFeedDockSettings { FeedIds = [feed.FeedId] } };
        using var vm = new ZoneViewModel(zone, fixture.Manager);
        var oldItem = vm.SelectedAgentFeed!.Sections[0].ChecklistItems[0];
        feed.Revision = "two";
        fixture.Manager.AgentFeeds.SaveFeed(feed);
        oldItem.IsChecked = true;
        Assert(!vm.SelectedAgentFeed!.Sections[0].ChecklistItems[0].IsChecked && vm.StatusMessage.Contains("Could not save", StringComparison.Ordinal),
            "A stale checklist callback wrote the newer feed or retained a false tick.");
        feed.Revision = "three";
        fixture.Manager.AgentFeeds.SaveFeed(feed);
        vm.MarkSelectedAgentFeedRead();
        Assert(vm.SelectedAgentFeed!.IsUnread && vm.StatusMessage.Contains("Could not mark", StringComparison.Ordinal),
            "A stale read callback marked an unseen revision read.");
        for (var index = 0; index < 8; index++)
        {
            vm.SelectedAgentFeed!.Sections[0].ChecklistItems[0].IsChecked = index % 2 == 0;
            Assert(DockField<List<FileSystemWatcher>>(vm, "_watchers").Count == 1, "Checklist interactions accumulated feed watchers.");
        }
        var feedPath = fixture.Manager.AgentFeeds.GetFeedPath(feed.FeedId);
        File.WriteAllText(feedPath, "{malformed");
        vm.MarkSelectedAgentFeedRead();
        Assert(vm.SelectedAgentFeed!.IsFallback && vm.SelectedAgentFeed.StatusText == "Error", "Malformed feed race escaped the recoverable error card.");
        vm.Refresh();
        Assert(vm.SelectedAgentFeed!.IsFallback && DockField<List<FileSystemWatcher>>(vm, "_watchers").Count == 1,
            "Malformed feed refresh failed or duplicated its watcher.");
        fixture.Manager.AgentFeeds.SaveFeed(feed);
        File.WriteAllText(fixture.Manager.AgentFeeds.StatePath, "{malformed state");
        vm.Refresh();
        Assert(vm.SelectedAgentFeed!.IsFallback, "Malformed local state must not invent a fresh unchecked checklist.");
    }

    private static void MusicHeaderFitsNarrowBars()
    {
        using var fixture = new ManagerFixture(Path.Combine(_fixturePath, "music-header-fit"));
        var zone = new ZoneDefinition { Id = "music-fit", Name = "Radio", Kind = ZoneKind.Music, IsVisible = false };
        using var vm = new ZoneViewModel(zone, fixture.Manager);
        var window = new ZoneWindow(vm, fixture.Manager);
        try
        {
            Invoke(window, "RenderMusicControls");
            foreach (var structure in Structures)
            foreach (var size in new[] { "Compact", "Standard", "Large" })
            foreach (var width in new[] { 230.0, 300, 560 })
            foreach (var collapsed in new[] { false, true })
            {
                zone.IsCollapsed = collapsed;
                ThemeService.Apply("LunarGlass", 0.73, true, structure, null, null, size);
                Invoke(window, "ApplyWindowBounds", new ZoneBounds { X = 150, Y = 140, Width = width, Height = 380 });
                var content = (FrameworkElement)window.Content;
                var height = collapsed ? window.CollapsedVisualHeight : 380;
                for (var pass = 0; pass < 2; pass++)
                {
                    content.InvalidateMeasure();
                    content.Measure(new Size(width, height));
                    content.Arrange(new Rect(0, 0, width, height));
                    content.UpdateLayout();
                    Drain();
                }
                var header = Find<Border>(window, "HeaderBorder");
                var transport = Find<StackPanel>(window, "MusicHeaderControls");
                var buttons = new List<Button> { Find<Button>(window, "SearchButton"), Find<Button>(window, "MoreButton"), Find<Button>(window, "CollapseButton") };
                if (transport.Visibility == Visibility.Visible) buttons.AddRange(transport.Children.OfType<Button>());
                foreach (var button in buttons)
                {
                    var point = button.TranslatePoint(new Point(0, 0), header);
                    Assert(point.X >= -0.1 && point.X + button.ActualWidth <= header.ActualWidth + 0.1,
                        $"Music header control clips horizontally: {structure}/{size}/{width}/{collapsed}.");
                }
                Assert(width != 230 || transport.Visibility == Visibility.Collapsed, "Narrow music transport must overflow into Dock actions.");
                if (collapsed && width == 230 && size == "Large") Capture(content, "music-header-narrow-" + structure, width, height);
            }
        }
        finally { window.Close(); }
    }

    private static T DockField<T>(object target, string name) => (T)(target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(target)
        ?? throw new InvalidOperationException("Missing test observation field: " + name));
}
