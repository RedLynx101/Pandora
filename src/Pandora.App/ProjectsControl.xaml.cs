using System;
using System.Diagnostics;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;
using Pandora.Core;

namespace Pandora.App;

/// <summary>Read-only Metis portfolio. User actions only register files, open a selected file, or change local presentation.</summary>
public partial class ProjectsControl : UserControl, IDisposable
{
    private readonly ProjectRegistryStore _store;
    private readonly ProjectPortfolioService _portfolio;
    private bool _disposed;
    private string? _actionError;
    private string? _renderKey;
    private readonly Dictionary<string, TextBlock> _readLabels = new(StringComparer.Ordinal);
    private readonly Dictionary<string, bool> _detailExpansion = new(StringComparer.Ordinal);

    public ProjectsControl(string registryPath)
    {
        InitializeComponent();
        _store = new ProjectRegistryStore(registryPath);
        _portfolio = new ProjectPortfolioService(_store);
        _portfolio.Changed += PortfolioChanged;
        Loaded += async (_, _) => await RefreshAsync();
        Render();
    }

    public async Task RefreshAsync()
    {
        if (_disposed) return;
        await _portfolio.RefreshAsync();
        if (!_disposed) await Dispatcher.InvokeAsync(Render);
    }

    private void PortfolioChanged(object? sender, EventArgs args)
    {
        if (!_disposed && !Dispatcher.HasShutdownStarted) _ = Dispatcher.InvokeAsync(Render);
    }

    private async void AddDashboard_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Register a Metis dashboard", Filter = "Metis HTML dashboard (*.html;*.htm)|*.html;*.htm",
            CheckFileExists = true, Multiselect = false, DereferenceLinks = false
        };
        var owner = Window.GetWindow(this);
        var selected = owner is null ? dialog.ShowDialog() : dialog.ShowDialog(owner);
        if (selected == true) await ActionAsync(() => _store.Register(dialog.FileName));
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e) { _actionError = null; await RefreshAsync(); }

    private async Task ActionAsync(Action action)
    {
        try { await Task.Run(action); _actionError = null; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or System.Text.Json.JsonException)
        { _actionError = ex.Message; }
        await RefreshAsync();
    }

    private void Render()
    {
        if (_disposed) return;
        var entries = _portfolio.Entries;
        var key = string.Join("|", entries.Select(e => $"{e.Registration.Id}:{e.Registration.Expanded}:{e.Status}:{e.Error}:{e.Snapshot?.ContentFingerprint}")) + _portfolio.RegistryError + _actionError;
        if (key == _renderKey)
        {
            foreach (var entry in entries)
                if (_readLabels.TryGetValue(entry.Registration.Id, out var label)) label.Text = ReadTimes(entry);
            return; // An unchanged poll must not collapse details, reset keyboard focus, or replace the scroll tree.
        }
        _renderKey = key;
        var scrollOffset = ProjectsScroll.VerticalOffset;
        _readLabels.Clear();
        var live = entries.Where(e => e.IsLive).ToArray();
        var projects = live.Select(e => e.Snapshot!.ProjectId).Distinct(StringComparer.Ordinal).Count();
        var verified = live.Sum(e => e.Snapshot!.VerifiedCriteria);
        var total = live.Sum(e => e.Snapshot!.CriteriaCount);
        ProjectsSummary.Text = entries.Count == 0 ? "Your projects, one quiet overview" :
            $"{projects} live project{Plural(projects)} · {live.Length} plan{Plural(live.Length)} · {verified}/{total} criteria verified";
        var errors = entries.Count(e => e.Status is not (MetisReadStatus.Ready or MetisReadStatus.Sample));
        var samples = entries.Count(e => e.Status == MetisReadStatus.Sample);
        ProjectsStatus.Text = _actionError ?? _portfolio.RegistryError ?? (entries.Count == 0
            ? "Add a trusted Metis dashboard HTML file. Nothing is discovered automatically, and project files stay read-only."
            : $"{errors} source issue{Plural(errors)} · {samples} sample{Plural(samples)} excluded. Source snapshots, not live agent telemetry. Refreshes every 30 seconds.");
        ProjectsStatus.SetResourceReference(TextBlock.ForegroundProperty, _actionError is not null || _portfolio.RegistryError is not null ? "Pandora.DangerBrush" : "Pandora.MutedBrush");
        ProjectsList.Children.Clear();
        if (entries.Count == 0)
        {
            var empty = Panel();
            empty.Children.Add(Label("A view, not a second manager", 16, bold: true));
            empty.Children.Add(Label("Follow multiple plans without merging their authority. Directors keep the plan and acceptance gates; Pandora shows the state they publish.", 12, muted: true));
            empty.Children.Add(Label("No approval buttons, automatic agents, or checklist writes. You choose every source.", 11, muted: true));
            ProjectsList.Children.Add(Card(empty));
            return;
        }
        foreach (var group in entries.GroupBy(e => e.Snapshot?.ProjectId ?? "unavailable:" + e.Registration.Id)
            .OrderByDescending(g => g.Max(e => e.Snapshot?.UpdatedAt ?? DateTimeOffset.MinValue)))
        {
            var first = group.First().Snapshot;
            ProjectsList.Children.Add(Label(first is null ? "UNAVAILABLE SOURCE" : first.ProjectName, 13, bold: true));
            if (first is not null) ProjectsList.Children.Add(Label(first.ProjectId + (group.Count() > 1 ? $" · {group.Count()} separate plans" : ""), 10, muted: true));
            foreach (var entry in group.OrderByDescending(e => e.Snapshot?.UpdatedAt)) ProjectsList.Children.Add(ProjectCard(entry));
        }
        _ = Dispatcher.InvokeAsync(() => ProjectsScroll.ScrollToVerticalOffset(scrollOffset), System.Windows.Threading.DispatcherPriority.Loaded);
    }

    private FrameworkElement ProjectCard(MetisProjectRead entry)
    {
        var snapshot = entry.Snapshot;
        var content = Panel();
        if (entry.IsStale) content.Children.Add(Label(entry.Status == MetisReadStatus.Duplicate
            ? "DUPLICATE · Source content shown for inspection; excluded from live totals"
            : "STALE · Last valid snapshot retained; excluded from live totals", 11, "Pandora.DangerBrush", bold: true));
        if (entry.Status == MetisReadStatus.Sample) content.Children.Add(Label("SAMPLE DATA · Excluded from live totals", 11, "Pandora.AccentBrush", bold: true));
        if (entry.Error is not null) content.Children.Add(Label(entry.Status.ToString().ToUpperInvariant() + " · " + entry.Error, 11, "Pandora.DangerBrush"));
        var title = snapshot?.TaskTitle ?? Path.GetFileName(entry.Registration.Path);
        var header = Panel();
        header.Children.Add(Label(title, 14, bold: true));
        if (snapshot is not null)
        {
            header.Children.Add(Label($"{snapshot.PlanStatus.ToUpperInvariant()} · {snapshot.PlanId} · revision {snapshot.Revision}", 10, muted: true));
            header.Children.Add(Label($"{snapshot.CurrentPhase?.Title ?? "No current phase"} · Director: {snapshot.SessionLabel(snapshot.DirectorSessionId)}", 11));
            header.Children.Add(Label($"{snapshot.VerifiedCriteria}/{snapshot.CriteriaCount} acceptance criteria verified", 11, "Pandora.AccentBrush"));
            header.Children.Add(PhaseBuckets(snapshot));
            header.Children.Add(Label("Material update " + Stamp(snapshot.UpdatedAt), 10, muted: true));
        }
        var expander = new Expander { Header = header, IsExpanded = entry.Registration.Expanded, Tag = snapshot?.DashboardId, HorizontalContentAlignment = HorizontalAlignment.Stretch };
        expander.SetResourceReference(ForegroundProperty, "Pandora.TextBrush");
        var body = Panel();
        expander.Content = body;
        void Fill()
        {
            if (body.Children.Count > 0) return;
            var readLabel = Label(ReadTimes(entry), 10, muted: true);
            _readLabels[entry.Registration.Id] = readLabel;
            var source = Panel();
            source.Children.Add(readLabel);
            source.Children.Add(Label(entry.Registration.Path, 10, muted: true));
            var actions = new WrapPanel { Margin = new Thickness(0, 5, 0, 8) };
            actions.Children.Add(Button("Open dashboard ↗", () => OpenDashboard(entry.Registration), "Opens this registered local HTML file in your default browser. Open only files you trust."));
            actions.Children.Add(Button("Remove", async () => await ActionAsync(() => _store.Remove(entry.Registration.Id)), "Removes this registration only. The dashboard file is not deleted."));
            body.Children.Add(actions);
            if (snapshot is not null) AddDetails(body, snapshot, entry.Registration.Id, source);
            else
            {
                body.Children.Add(source);
                if (entry.Registration.LastAccepted is { } checkpoint)
                    body.Children.Add(Label($"Previously accepted {checkpoint.PlanId} r{checkpoint.Revision}, material update {Stamp(checkpoint.UpdatedAt)}. Snapshot contents are not cached across app restarts.", 11, muted: true));
            }
        }
        if (expander.IsExpanded) Fill();
        expander.Expanded += async (sender, args) =>
        {
            if (!ReferenceEquals(args.OriginalSource, expander)) return;
            Fill(); await SaveExpansion(entry.Registration.Id, true);
        };
        expander.Collapsed += async (sender, args) =>
        {
            if (ReferenceEquals(args.OriginalSource, expander)) await SaveExpansion(entry.Registration.Id, false);
        };
        content.Children.Add(expander);
        return Card(content);
    }

    private async Task SaveExpansion(string id, bool expanded)
    {
        try { await Task.Run(() => _store.SetExpanded(id, expanded)); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or System.Text.Json.JsonException)
        { _actionError = "View preference was not saved: " + ex.Message; ProjectsStatus.Text = _actionError; }
    }

    private static Grid PhaseBuckets(MetisSnapshot snapshot)
    {
        var grid = new Grid { Tag = "PhaseBuckets", Height = 10, Margin = new Thickness(0, 5, 0, 4) };
        for (var i = 0; i < snapshot.Phases.Count; i++)
        {
            var phase = snapshot.Phases[i];
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(phase.BucketSize, GridUnitType.Star) });
            var bar = new ProgressBar { Minimum = 0, Maximum = Math.Max(1, phase.Criteria.Count), Value = phase.VerifiedCriteria, Height = 8, Margin = new Thickness(0, 0, 2, 0),
                ToolTip = $"{phase.Title}: {phase.VerifiedCriteria}/{phase.Criteria.Count} criteria verified · {phase.Status}\nWidth: {phase.BucketSize} {(phase.WorkPackages.Count > 0 ? "work packages" : "criteria (minimum 1)")}" };
            bar.SetResourceReference(BackgroundProperty, "Pandora.BorderBrush");
            bar.SetResourceReference(ForegroundProperty, phase.Status == "verified" ? "Pandora.SuccessBrush" : "Pandora.AccentBrush");
            Grid.SetColumn(bar, i); grid.Children.Add(bar);
        }
        return grid;
    }

    private void AddDetails(StackPanel body, MetisSnapshot snapshot, string registrationId, StackPanel source)
    {
        if (!string.IsNullOrWhiteSpace(snapshot.NextAction))
        {
            body.Children.Add(Label("NEXT DIRECTOR / MANAGER ACTION", 10, "Pandora.AccentBrush", true));
            body.Children.Add(Label(snapshot.NextAction, 12));
        }
        if (snapshot.Summary.Length > 0) body.Children.Add(Label(snapshot.Summary, 11, muted: true));
        body.Children.Add(Label($"{snapshot.Sessions.Count} primary sessions + {snapshot.DeclaredSubagentBudget} explicitly budgeted subagents = {snapshot.DeclaredTeamSize} declared slots", 12, bold: true));
        body.Children.Add(Label($"{snapshot.UnknownBudgetCount} unspecified budget{Plural(snapshot.UnknownBudgetCount)}. Budgets are declarations, not enforcement or current agent usage.", 10, muted: true));

        if (snapshot.Blockers.Count > 0)
        {
            body.Children.Add(Label("ATTENTION", 10, "Pandora.DangerBrush", true));
            foreach (var notice in snapshot.Blockers.Take(50)) body.Children.Add(Label($"{notice.Title} · {notice.Owner ?? "Unassigned"}\n{notice.Detail}", 11));
            Overflow(body, snapshot.Blockers.Count, 50, "blockers");
        }
        if (snapshot.Waits.Count > 0)
        {
            body.Children.Add(Label("DECLARED WAITS", 10, "Pandora.AccentBrush", true));
            foreach (var wait in snapshot.Waits.Take(50)) body.Children.Add(Label($"{snapshot.SessionLabel(wait.Target)} · since {Stamp(wait.Since)}\nWake: {wait.WakeCondition}\nReported check window: {wait.LivenessWindowMinutes} min. No live agent signal; elapsed time alone is not drift.", 11));
            Overflow(body, snapshot.Waits.Count, 50, "waits");
        }
        body.Children.Add(Label("PHASES & OWNERSHIP", 10, "Pandora.AccentBrush", true));
        foreach (var phase in snapshot.Phases.Take(64))
        {
            var phaseBody = Panel();
            phaseBody.Children.Add(Label($"Accountable: {snapshot.SessionLabel(phase.AccountableOwnerSessionId)}\nExecution lead: {snapshot.SessionLabel(phase.AssignedSessionId)}\nIntegration: {snapshot.SessionLabel(phase.IntegrationOwnerSessionId)}", 11));
            phaseBody.Children.Add(Label($"{phase.ExecutionMode} · assigned at revision {phase.PlanRevision}\n{phase.Description}", 10, muted: true));
            foreach (var package in phase.WorkPackages.Take(100))
            {
                phaseBody.Children.Add(Label($"{package.Title} · {package.Status}", 11, bold: true));
                phaseBody.Children.Add(Label($"Owner: {snapshot.SessionLabel(package.OwnerSessionId)}\n{package.Id}" + (package.DependsOn.Count > 0 ? " · depends on " + string.Join(", ", package.DependsOn) : " · no declared dependencies"), 10, muted: true));
            }
            Overflow(phaseBody, phase.WorkPackages.Count, 100, "packages");
            foreach (var criterion in phase.Criteria.Take(200))
            {
                phaseBody.Children.Add(Label($"{(criterion.Status == "verified" ? "✓" : "○")} {criterion.Title} · {criterion.Status}", 11));
                if (criterion.Evidence is not null) phaseBody.Children.Add(Label("Evidence: " + criterion.Evidence, 10, muted: true));
            }
            Overflow(phaseBody, phase.Criteria.Count, 200, "criteria");
            var phaseHeader = Panel();
            phaseHeader.Children.Add(Label($"{phase.Title} · {phase.Status} · {phase.VerifiedCriteria}/{phase.Criteria.Count}", 12, bold: true));
            phaseHeader.Children.Add(Label("Accountable: " + snapshot.SessionLabel(phase.AccountableOwnerSessionId), 10, muted: true));
            phaseHeader.Children.Add(Label("Packages: " + (phase.WorkPackages.Count == 0 ? "None" : string.Join("; ", phase.WorkPackages.Take(12).Select(p => p.Title + " → " + snapshot.SessionLabel(p.OwnerSessionId))) + (phase.WorkPackages.Count > 12 ? "; …" : "")), 10, muted: true));
            var expander = new Expander { Header = phaseHeader, Content = phaseBody, Margin = new Thickness(0, 3, 0, 5), HorizontalContentAlignment = HorizontalAlignment.Stretch };
            RememberExpansion(expander, registrationId + "/phase/" + phase.Id, phase.Id == snapshot.CurrentPhaseId);
            expander.SetResourceReference(ForegroundProperty, "Pandora.TextBrush"); body.Children.Add(expander);
        }
        Overflow(body, snapshot.Phases.Count, 64, "phases");

        var sessions = Panel();
        foreach (var session in snapshot.Sessions)
            sessions.Children.Add(Label($"{session.Name} ({session.Id}) · {session.Role} · {session.Status}\n{session.Assignment ?? "Unassigned"} · subagent budget: {(session.SubagentBudget is { } budget ? budget.ToString() : "Unspecified")}", 11));
        var sessionDetails = Details("Primary sessions · " + snapshot.Sessions.Count, sessions);
        RememberExpansion(sessionDetails, registrationId + "/sessions"); body.Children.Add(sessionDetails);
        var evidence = Panel();
        evidence.Children.Add(source);
        evidence.Children.Add(Label("Canonical plan: " + snapshot.PlanSource, 10, muted: true));
        evidence.Children.Add(Label("Project root: " + (snapshot.ProjectRoot ?? "Not recorded") + "\nDashboard: " + snapshot.DashboardId, 10, muted: true));
        foreach (var dependency in snapshot.Dependencies.Take(50)) evidence.Children.Add(Label($"Dependency: {dependency.Title} · {dependency.Owner ?? "Unassigned"}\n{dependency.Detail}", 11));
        foreach (var activity in snapshot.Activity.OrderByDescending(a => a.Timestamp).Take(30))
            evidence.Children.Add(Label($"{Stamp(activity.Timestamp)} · {activity.Status} · {activity.Title}\n{activity.Detail}" + (activity.Evidence is not null ? "\nEvidence: " + activity.Evidence : ""), 11));
        Overflow(evidence, snapshot.Dependencies.Count, 50, "dependencies"); Overflow(evidence, snapshot.Activity.Count, 30, "events");
        evidence.Children.Add(Label("Evidence and paths are reported text, not independently verified by Pandora. Open the dashboard for the full snapshot.", 10, muted: true));
        var evidenceDetails = Details("Evidence & source details", evidence);
        RememberExpansion(evidenceDetails, registrationId + "/evidence"); body.Children.Add(evidenceDetails);
    }

    private void RememberExpansion(Expander expander, string key, bool defaultExpanded = false)
    {
        expander.IsExpanded = _detailExpansion.TryGetValue(key, out var expanded) ? expanded : defaultExpanded;
        expander.Expanded += (_, args) => { if (ReferenceEquals(args.OriginalSource, expander)) _detailExpansion[key] = true; };
        expander.Collapsed += (_, args) => { if (ReferenceEquals(args.OriginalSource, expander)) _detailExpansion[key] = false; };
    }
    private static string ReadTimes(MetisProjectRead entry) => "Read attempt " + Stamp(entry.LastReadAt) + "\nSuccessful read " + (entry.LastSuccessfulReadAt is { } success ? Stamp(success) : "Not yet in this app session");

    private void OpenDashboard(ProjectRegistration registration)
    {
        try
        {
            // Reconfirm membership and the exact local file immediately before this explicit action.
            var current = _store.Load().SingleOrDefault(r => r.Id == registration.Id && r.Path == registration.Path)
                ?? throw new InvalidOperationException("This dashboard is no longer registered.");
            var path = ProjectPath.Validate(current.Path, requireExists: true);
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception or System.Text.Json.JsonException)
        { _actionError = "Could not open dashboard: " + ex.Message; Render(); }
    }

    private static Expander Details(string title, UIElement body)
    {
        var details = new Expander { Header = title, Content = body, Margin = new Thickness(0, 6, 0, 3), HorizontalContentAlignment = HorizontalAlignment.Stretch };
        details.SetResourceReference(ForegroundProperty, "Pandora.TextBrush"); return details;
    }
    private static StackPanel Panel() => new() { Margin = new Thickness(2, 2, 2, 2) };
    private static Border Card(UIElement content)
    {
        var border = new Border { BorderThickness = new Thickness(1), Child = content };
        border.SetResourceReference(Border.CornerRadiusProperty, "Pandora.ProjectCardCornerRadius");
        border.SetResourceReference(Border.PaddingProperty, "Pandora.ProjectCardPadding");
        border.SetResourceReference(Border.MarginProperty, "Pandora.ProjectCardMargin");
        border.SetResourceReference(Border.BackgroundProperty, "Pandora.SurfaceBrush"); border.SetResourceReference(Border.BorderBrushProperty, "Pandora.BorderBrush"); return border;
    }
    private static TextBlock Label(string text, double size = 12, string? resource = null, bool bold = false, bool muted = false)
    {
        var label = new TextBlock { Text = text.Length > 5000 ? text[..5000] + "… [open dashboard for full text]" : text, FontSize = size,
            FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 5) };
        label.SetResourceReference(TextBlock.ForegroundProperty, resource ?? (muted ? "Pandora.MutedBrush" : "Pandora.TextBrush")); return label;
    }
    private static Button Button(string title, Action action, string tooltip)
    {
        var button = new Button { Content = title, ToolTip = tooltip, Margin = new Thickness(0, 0, 6, 4), Padding = new Thickness(8, 4, 8, 4) };
        button.Click += (_, _) => action(); return button;
    }
    private static void Overflow(StackPanel panel, int total, int limit, string noun)
    { if (total > limit) panel.Children.Add(Label($"Showing {limit} of {total} {noun}; open dashboard for the full snapshot. Totals include all validated items.", 10, muted: true)); }
    private static string Stamp(DateTimeOffset value) => value.ToLocalTime().ToString("MMM d, HH:mm:ss zzz");
    private static string Plural(int count) => count == 1 ? "" : "s";

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true; _portfolio.Changed -= PortfolioChanged; _portfolio.Dispose();
    }
}
