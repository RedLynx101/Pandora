using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Pandora.App;
using Pandora.Core;

namespace Pandora.App.Tests;

internal static partial class Program
{
    // Marketing images are actual controls with explicit synthetic content, never desktop captures.
    private static void PublicPresentationRenders()
    {
        using var fixture = new ManagerFixture(Path.Combine(_fixturePath, "public-presentation"));
        var folder = Path.Combine(fixture.Root, "Project files");
        Directory.CreateDirectory(folder);
        foreach (var name in new[] { "Design notes.md", "Release checklist.md", "Ideas.txt" })
            File.WriteAllText(Path.Combine(folder, name), "Public sample content. No user documents.");
        var zone = new ZoneDefinition { Id = "presentation-files", Name = "Project files", IsVisible = false,
            Tabs = [new ZoneTabDefinition { Id = "active", Name = "Active", Path = folder }, new ZoneTabDefinition { Id = "reference", Name = "Reference", Path = folder }] };
        fixture.Manager.Workspace.Zones.Add(zone);
        using var filesVm = new ZoneViewModel(zone, fixture.Manager);
        var filesWindow = new ZoneWindow(filesVm, fixture.Manager);
        Invoke(filesWindow, "RenderTabs");
        var feed = new AgentFeedDocument { FeedId = "presentation-brief", Title = "Today", SourceAgent = "Local brief", Revision = "sample-1",
            Summary = "Keep the next few actions in view.", Status = AgentFeedStatus.Attention,
            Sections = [new AgentFeedSection { Id = "next", Title = "Next up", Kind = AgentFeedSectionKind.Checklist,
                Items = [new AgentFeedItem { Id = "review", Text = "Review the design notes" }, new AgentFeedItem { Id = "build", Text = "Check the latest build" }, new AgentFeedItem { Id = "plan", Text = "Plan tomorrow's work" }] }] };
        fixture.Manager.AgentFeeds.SaveFeed(feed);
        var feedZone = new ZoneDefinition { Id = "presentation-brief", Name = "Daily brief", Kind = ZoneKind.AgentFeed, IsVisible = false,
            AgentFeed = new AgentFeedDockSettings { FeedIds = [feed.FeedId] } };
        using var feedVm = new ZoneViewModel(feedZone, fixture.Manager);
        var feedWindow = new ZoneWindow(feedVm, fixture.Manager);
        Invoke(feedWindow, "ApplyContentMode");
        try
        {
            ThemeService.Apply("LunarGlass", 0.88, true, "Halo", null, null, null);
            var canvas = new Canvas { Width = 1200, Height = 660, Background = new LinearGradientBrush(Color.FromRgb(11, 16, 28), Color.FromRgb(27, 34, 51), 25) };
            Place(new Image { Source = BrandIdentity.Image("Aperture"), Width = 62, Height = 62 }, 44, 40);
            Label("Pandora", 124, 38, 48, Colors.White, FontWeights.SemiBold);
            Label("Files, tools, and projects. On your desktop.", 48, 116, 28, Color.FromRgb(206, 211, 229));
            Label("WINDOWS  /  LOCAL-FIRST  /  OPEN SOURCE", 48, 172, 13, Color.FromRgb(183, 174, 245));
            var left = (FrameworkElement)filesWindow.Content;
            var right = (FrameworkElement)feedWindow.Content;
            // Keep logical Window parents: item styles also bind to ancestor Window.
            // Compose frozen control snapshots so overview layout cannot remeasure the live controls.
            Capture(left, "presentation-files", 540, 346);
            Capture(right, "presentation-brief", 540, 346);
            Place(Snapshot("presentation-files"), 48, 222);
            Place(Snapshot("presentation-brief"), 612, 222);
            Label("Actual Pandora controls · sample content · illustrative layout", 48, 606, 15, Color.FromRgb(179, 188, 207));
            Capture(canvas, "pandora-overview", 1200, 660);
            Assert(ReadText(left).Contains("Project files") && ReadText(left).Contains("Design notes") &&
                ReadText(right).Contains("Daily brief") && ReadText(right).Contains("Review the design notes"),
                "Presentation must include actual rendered titles, files, and checklist content.");
            foreach (var structure in Structures)
            {
                ThemeService.Apply("LunarGlass", 0.88, true, structure, null, null, null);
                Capture(left, "pandora-theme-" + structure.ToLowerInvariant(), 540, 340, backdrop: Brush("Window"));
            }
            void Place(UIElement element, double x, double y) { Canvas.SetLeft(element, x); Canvas.SetTop(element, y); canvas.Children.Add(element); }
            Image Snapshot(string name) => new() { Width = 540, Height = 346, Stretch = Stretch.None,
                Source = BitmapFrame.Create(new Uri(Path.Combine(_runPath, name + ".png")), BitmapCreateOptions.IgnoreColorProfile, BitmapCacheOption.OnLoad) };
            void Label(string text, double x, double y, double size, Color color, FontWeight? weight = null) =>
                Place(new TextBlock { Text = text, FontFamily = new FontFamily("Segoe UI"), FontSize = size, Foreground = new SolidColorBrush(color), FontWeight = weight ?? FontWeights.Normal }, x, y);
        }
        finally { filesWindow.Close(); feedWindow.Close(); }

        var registryPath = Path.Combine(fixture.Root, "projects.json");
        var registry = new ProjectRegistryStore(registryPath);
        foreach (var project in new[] { ("docs", "Documentation site", "Publish the project guide"), ("notes", "Research notebook", "Organize the next study") })
            registry.Register(WriteDashboard(fixture.Root, project.Item1, state =>
            {
                state["project"]!["name"] = project.Item2;
                state["task"]!["title"] = project.Item3;
            }));
        using var projects = new ProjectsControl(registryPath);
        ThemeService.Apply("LunarGlass", 0.88, true, "Halo", null, null, null);
        Complete(projects.RefreshAsync());
        Capture(ProjectSurface(projects), "pandora-projects", 540, 680);
        Assert(ReadText(projects).Contains("Documentation site"), "Project presentation omitted its synthetic content.");
    }
}
