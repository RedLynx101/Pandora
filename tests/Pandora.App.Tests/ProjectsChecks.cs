using System.IO;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Pandora.App;
using Pandora.Core;

namespace Pandora.App.Tests;

internal static partial class Program
{
    private static void ProjectStates()
    {
        var root = Path.Combine(_fixturePath, "project-states");
        Directory.CreateDirectory(root);
        var registryPath = Path.Combine(root, "projects.json");
        using var control = new ProjectsControl(registryPath);
        var surface = ProjectSurface(control);
        Complete(control.RefreshAsync());
        Capture(surface, "projects-empty", 760, 470);
        Assert(!File.Exists(registryPath), "Empty read unexpectedly created a registry.");

        var registry = new ProjectRegistryStore(registryPath);
        registry.Register(WriteDashboard(root, "live"));
        registry.Register(WriteDashboard(root, "sample", state => state["templateMode"] = true));
        var invalid = Path.Combine(root, "invalid.html");
        File.WriteAllText(invalid, "<script type=\"application/json\" id=\"dashboard-state\">{broken}</script>");
        registry.Register(invalid);
        registry.Register(WriteDashboard(root, "unsupported", state => state["schema"] = "future-dashboard/v99"));
        var missing = WriteDashboard(root, "missing");
        registry.Register(missing);
        File.Delete(missing); // This exact fixture was created by this test, beneath its unique run directory.
        Complete(control.RefreshAsync());
        Capture(surface, "projects-error-states", 760, 980);
        var text = ReadText(control);
        foreach (var state in new[] { "Sample", "Invalid", "Unsupported", "Missing" })
            Assert(text.Contains(state, StringComparison.OrdinalIgnoreCase), "Missing explicit project state: " + state);
        Assert(!text.Contains("stopped", StringComparison.OrdinalIgnoreCase), "Missing/error sources must not assert an agent stopped.");
        File.WriteAllText(Path.Combine(root, "live.html"), "<html>Temporary malformed fixture after a good read</html>");
        Complete(control.RefreshAsync());
        Capture(surface, "projects-last-good-stale", 760, 980);
        text = ReadText(control);
        Assert(text.Contains("STALE", StringComparison.Ordinal), "A failed read must label retained last-good content stale.");
        Assert(Find<TextBlock>(control, "ProjectsSummary").Text.StartsWith("0 live projects", StringComparison.Ordinal), "Stale data must not enter current valid-source totals.");
    }

    private static void ProjectDetails()
    {
        var root = Path.Combine(_fixturePath, "project-details");
        Directory.CreateDirectory(root);
        var registryPath = Path.Combine(root, "projects.json");
        var path = WriteDashboard(root, "one");
        var hash = SHA256.HashData(File.ReadAllBytes(path));
        var registry = new ProjectRegistryStore(registryPath);
        var registration = registry.Register(path);
        registry.SetExpanded(registration.Id, true);
        registry.Register(WriteDashboard(root, "two", state => state["project"]!["name"] = "Second independent project"));
        using var control = new ProjectsControl(registryPath);
        var surface = ProjectSurface(control);
        Complete(control.RefreshAsync());
        foreach (var theme in new[] { "LunarGlass", "Midnight", "Limestone" })
        {
            ThemeService.Apply(theme, 0.88, false);
            Capture(surface, "projects-expanded-" + theme, 920, 1120);
            Capture(surface, "projects-dock-" + theme, 540, 980);
            Capture(surface, "projects-small-" + theme, 420, 900);
        }
        Capture(surface, "projects-compact", 430, 900);
        Capture(surface, "projects-150pct-raster", 700, 900, 144);
        var text = ReadText(control);
        foreach (var expected in new[] { "Metis", "Selene", "Aster", "Unassigned", "Foundation", "Parallel implementation", "Acceptance" })
            Assert(text.Contains(expected, StringComparison.Ordinal), "Project details omit owner or phase: " + expected);
        var buckets = Descendants(control).OfType<Grid>().Where(g => Equals(g.Tag, "PhaseBuckets")).ToList();
        Assert(buckets.Count >= 1, "Phase bucket visual grid is missing.");
        foreach (var bar in buckets)
        {
            Assert(bar.ColumnDefinitions.Count == 3, "Expected exactly three phase buckets.");
            var weights = bar.ColumnDefinitions.Select(c => c.Width.Value).ToArray();
            Assert(weights.SequenceEqual(new double[] { 1, 2, 3 }), "Phase bucket weights must reflect package counts 1:2:3.");
            Assert(bar.ColumnDefinitions.All(c => c.Width.IsStar), "Phase weights must use proportional star sizing.");
        }
        Assert(SHA256.HashData(File.ReadAllBytes(path)).SequenceEqual(hash), "Project rendering or expansion changed source dashboard state.");
        // Expansion is a Pandora-local display preference, not a canonical Metis plan mutation.
        Assert(registry.Load().Single(r => r.Id == registration.Id).Expanded, "Expansion preference did not survive reload.");
        var projectExpander = Descendants(control).OfType<Expander>().Single(e => Equals(e.Tag, "dashboard-one"));
        var evidence = Descendants(projectExpander).OfType<Expander>().Single(e => Equals(e.Header, "Evidence & source details"));
        evidence.IsExpanded = true;
        Complete(control.RefreshAsync());
        Assert(ReferenceEquals(projectExpander, Descendants(control).OfType<Expander>().Single(e => Equals(e.Tag, "dashboard-one"))), "Unchanged refresh recreated the project visual tree.");
        Assert(evidence.IsExpanded, "Unchanged refresh collapsed a user-opened detail.");
        WriteDashboard(root, "one", state => state["plan"]!["revision"] = 5);
        Complete(control.RefreshAsync());
        projectExpander = Descendants(control).OfType<Expander>().Single(e => Equals(e.Tag, "dashboard-one"));
        Assert(Descendants(projectExpander).OfType<Expander>().Single(e => Equals(e.Header, "Evidence & source details")).IsExpanded,
            "A material plan revision reset the user's explicit nested expansion.");
        registry.SetExpanded(registration.Id, false);
        Complete(control.RefreshAsync());
        foreach (var theme in new[] { "LunarGlass", "Midnight", "Limestone" })
        {
            ThemeService.Apply(theme, 0.88, false);
            Capture(surface, "projects-overview-" + theme, 540, 740);
        }
    }

    private static Border ProjectSurface(ProjectsControl control)
    {
        var surface = new Border { Child = control, Padding = new Thickness(14) };
        surface.SetResourceReference(Border.BackgroundProperty, "Pandora.WindowBrush");
        return surface;
    }

    private static string WriteDashboard(string root, string suffix, Action<JsonObject>? edit = null)
    {
        var state = JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "dashboard-state.json")))!.AsObject();
        state["dashboardId"] = "dashboard-" + suffix;
        state["projectId"] = "project-" + suffix;
        state["task"]!["id"] = "task-" + suffix;
        state["plan"]!["id"] = "plan-" + suffix;
        edit?.Invoke(state);
        var path = Path.Combine(root, suffix + ".html");
        File.WriteAllText(path, "<!doctype html><html><body><script type=\"application/json\" id=\"dashboard-state\">" + state.ToJsonString() + "</script></body></html>");
        return path;
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject? root)
    {
        if (root is null) yield break;
        var seen = new HashSet<DependencyObject>();
        var pending = new Stack<DependencyObject>();
        pending.Push(root);
        while (pending.TryPop(out var current))
        {
            if (!seen.Add(current)) continue;
            yield return current;
            if (current is Visual or System.Windows.Media.Media3D.Visual3D)
                for (var i = 0; i < VisualTreeHelper.GetChildrenCount(current); i++) pending.Push(VisualTreeHelper.GetChild(current, i));
            foreach (var child in LogicalTreeHelper.GetChildren(current).OfType<DependencyObject>()) pending.Push(child);
        }
    }

    private static string ReadText(DependencyObject root) => string.Join("\n", Descendants(root).Select(node => node switch
    {
        TextBlock text => text.Text,
        ContentControl { Content: string content } => content,
        _ => string.Empty
    }));

    private static void Complete(Task task)
    {
        if (!task.IsCompleted)
        {
            var frame = new DispatcherFrame();
            var dispatcher = Dispatcher.CurrentDispatcher;
            var timedOut = false;
            var timer = new DispatcherTimer(DispatcherPriority.Send, dispatcher) { Interval = TimeSpan.FromSeconds(20) };
            timer.Tick += (_, _) => { timedOut = true; frame.Continue = false; };
            _ = task.ContinueWith(_ => dispatcher.BeginInvoke(() => frame.Continue = false), TaskScheduler.Default);
            timer.Start();
            Dispatcher.PushFrame(frame);
            timer.Stop();
            Assert(!timedOut, "Project refresh did not finish within the bounded 20-second verification window.");
        }
        task.GetAwaiter().GetResult();
        Drain();
    }
}
