using System.Text.Json;
using System.Text.RegularExpressions;

namespace Pandora.Core;

/// <summary>Explicit registrations only. Debounced exact-file watchers supplement a bounded reconciliation timer.</summary>
public sealed class ProjectPortfolioService : IDisposable
{
    private readonly ProjectRegistryStore _store;
    private readonly object _refreshGuard = new();
    private Task? _refreshTask;
    private readonly CancellationTokenSource _stop = new();
    private readonly Timer _periodic;
    private readonly Timer _debounce;
    private readonly List<FileSystemWatcher> _watchers = [];
    private readonly object _watcherGuard = new();
    private IReadOnlyList<MetisProjectRead> _entries = [];
    private volatile bool _disposed;
    public event EventHandler? Changed;
    public IReadOnlyList<MetisProjectRead> Entries => Volatile.Read(ref _entries);
    public string? RegistryError { get; private set; }
    public bool HasRefreshed { get; private set; }
    public bool IsRefreshing { get; private set; }
    public DateTimeOffset? LastRefreshCompletedAt { get; private set; }
    public string RegistryPath => _store.RegistryPath;

    public ProjectPortfolioService(ProjectRegistryStore store, TimeSpan? reconciliationInterval = null)
    {
        _store = store;
        _debounce = new Timer(_ => QueueRefresh(), null, Timeout.Infinite, Timeout.Infinite);
        var interval = reconciliationInterval ?? TimeSpan.FromSeconds(30);
        if (interval < TimeSpan.FromSeconds(5)) throw new ArgumentOutOfRangeException(nameof(reconciliationInterval));
        _periodic = new Timer(_ => QueueRefresh(), null, interval, interval);
    }

    public Task RefreshAsync()
    {
        lock (_refreshGuard)
        {
            if (_disposed) return Task.CompletedTask;
            // Every caller awaits the same reconciliation, rather than returning
            // early with an empty/stale result when another refresh owns the gate.
            if (_refreshTask is { IsCompleted: false })
            {
                // A file may change after the in-flight pass read its registry.
                // Retain one debounced follow-up, without queueing more worker tasks.
                ScheduleRefresh();
                return _refreshTask;
            }
            return _refreshTask = Task.Run(RefreshCoreAsync);
        }
    }

    private async Task RefreshCoreAsync()
    {
        IsRefreshing = true;
        try
        {
            if (!_disposed) Changed?.Invoke(this, EventArgs.Empty);
            var registrations = await Task.Run(_store.Load, _stop.Token);
            RegistryError = null;
            var old = Entries.ToDictionary(e => e.Registration.Id, StringComparer.Ordinal);
            using var concurrency = new SemaphoreSlim(4);
            var reads = await Task.WhenAll(registrations.Select(registration => Task.Run(async () =>
            {
                await concurrency.WaitAsync(_stop.Token);
                try { return await ReadOne(registration, old.GetValueOrDefault(registration.Id)); }
                finally { concurrency.Release(); }
            }, _stop.Token)));

            var duplicates = reads.Where(e => e.Status is MetisReadStatus.Ready or MetisReadStatus.Sample)
                .GroupBy(e => e.Snapshot!.DashboardId, StringComparer.Ordinal).Where(g => g.Count() > 1).SelectMany(g => g.Select(e => e.Registration.Id)).ToHashSet(StringComparer.Ordinal);
            foreach (var group in reads.Where(e => e.Status == MetisReadStatus.Ready)
                .GroupBy(e => (e.Snapshot!.ProjectId, e.Snapshot.PlanId)).Where(g => g.Count() > 1))
                foreach (var item in group) duplicates.Add(item.Registration.Id);
            for (var i = 0; i < reads.Length; i++)
                if (duplicates.Contains(reads[i].Registration.Id)) reads[i] = reads[i] with { Status = MetisReadStatus.Duplicate, Error = "Duplicate dashboard or project/plan identity. Both registrations are excluded from live totals." };

            var checkpoints = reads.Where(r => r.IsLive).ToDictionary(r => r.Registration.Id, r => ProjectRegistryStore.Checkpoint(r.Snapshot!), StringComparer.Ordinal);
            if (checkpoints.Count > 0)
            {
                var rejected = await Task.Run(() => _store.Accept(checkpoints), _stop.Token);
                for (var i = 0; i < reads.Length; i++)
                    if (rejected.TryGetValue(reads[i].Registration.Id, out var error))
                    {
                        var previous = old.GetValueOrDefault(reads[i].Registration.Id);
                        reads[i] = reads[i] with { Status = MetisReadStatus.Regressed, Error = error, Snapshot = previous?.Snapshot, LastSuccessfulReadAt = previous?.LastSuccessfulReadAt };
                    }
            }
            Volatile.Write(ref _entries, reads);
            UpdateWatchers(registrations);
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or ArgumentException or InvalidDataException)
        {
            RegistryError = "Project registry could not be reconciled: " + ex.Message;
            // A failed refresh never silently presents the previous entries as current.
            Volatile.Write(ref _entries, Entries.Select(e => e with { Status = MetisReadStatus.ReadError, Error = RegistryError }).ToArray());
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // Timer-triggered reconciliation must not fault invisibly. Expose a
            // diagnostic error, never call old entries live after an internal failure.
            RegistryError = $"Project refresh failed ({ex.GetType().Name}): {ex.Message}";
            Volatile.Write(ref _entries, Entries.Select(e => e with { Status = MetisReadStatus.ReadError, Error = RegistryError }).ToArray());
        }
        finally
        {
            IsRefreshing = false;
            HasRefreshed = true;
            LastRefreshCompletedAt = DateTimeOffset.UtcNow;
            if (!_disposed) Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    private async Task<MetisProjectRead> ReadOne(ProjectRegistration registration, MetisProjectRead? previous)
    {
        var now = DateTimeOffset.UtcNow;
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
            timeout.CancelAfter(TimeSpan.FromSeconds(5));
            var snapshot = await MetisReader.ReadAsync(registration.Path, timeout.Token);
            var checkpoint = registration.LastAccepted ?? (previous is { IsLive: true } ? ProjectRegistryStore.Checkpoint(previous.Snapshot!) : null);
            var regression = checkpoint is null ? null : snapshot.IsSample ? "A live registration cannot become sample data. Remove and register again to reset it." : ProjectRegistryStore.Regression(checkpoint, ProjectRegistryStore.Checkpoint(snapshot));
            if (regression is not null) return new(registration, previous?.Snapshot, MetisReadStatus.Regressed, regression, now, previous?.LastSuccessfulReadAt);
            return new(registration, snapshot, snapshot.IsSample ? MetisReadStatus.Sample : MetisReadStatus.Ready, null, now, DateTimeOffset.UtcNow);
        }
        catch (OperationCanceledException) when (!_stop.IsCancellationRequested) { return Failed(MetisReadStatus.ReadError, "Dashboard read timed out."); }
        catch (FileNotFoundException) { return Failed(MetisReadStatus.Missing, "Registered dashboard file is missing."); }
        catch (DirectoryNotFoundException) { return Failed(MetisReadStatus.Missing, "Registered dashboard directory is missing."); }
        catch (MetisValidationException ex) { return Failed(ex.Unsupported ? MetisReadStatus.Unsupported : MetisReadStatus.Invalid, ex.Message); }
        catch (JsonException ex) { return Failed(MetisReadStatus.Invalid, "Invalid dashboard JSON: " + ex.Message); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or RegexMatchTimeoutException or System.Text.DecoderFallbackException)
        { return Failed(MetisReadStatus.ReadError, ex.Message); }
        MetisProjectRead Failed(MetisReadStatus status, string error) => new(registration, previous?.Snapshot, status, error, now, previous?.LastSuccessfulReadAt);
    }

    private void UpdateWatchers(IReadOnlyList<ProjectRegistration> registrations)
    {
        lock (_watcherGuard)
        {
            if (_disposed) return;
            foreach (var watcher in _watchers) watcher.Dispose();
            _watchers.Clear();
            var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { _store.RegistryPath };
            foreach (var registration in registrations)
            {
                try { paths.Add(ProjectPath.Validate(registration.Path, requireExists: false)); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
                { /* A disallowed source must not create a watcher through its new target. */ }
            }
            foreach (var directory in paths.Select(Path.GetDirectoryName).Where(d => d is not null && Directory.Exists(d)).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                try
                {
                    var watcher = new FileSystemWatcher(directory!) { IncludeSubdirectories = false, NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size };
                    FileSystemEventHandler changed = (_, args) => { if (paths.Contains(args.FullPath)) ScheduleRefresh(); };
                    watcher.Changed += changed; watcher.Created += changed; watcher.Deleted += changed;
                    watcher.Renamed += (_, args) => { if (paths.Contains(args.FullPath) || paths.Contains(args.OldFullPath)) ScheduleRefresh(); };
                    watcher.Error += (_, _) => ScheduleRefresh();
                    watcher.EnableRaisingEvents = true;
                    _watchers.Add(watcher);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
                { /* Periodic reconciliation remains available when a watch cannot be installed. */ }
            }
        }
    }
    private void QueueRefresh() { if (!_disposed) _ = RefreshAsync(); }
    private void ScheduleRefresh()
    {
        lock (_watcherGuard) { if (!_disposed) _debounce.Change(500, Timeout.Infinite); }
    }
    public void Dispose()
    {
        lock (_watcherGuard)
        {
            if (_disposed) return;
            _disposed = true; _stop.Cancel(); _periodic.Dispose(); _debounce.Dispose();
            foreach (var watcher in _watchers) watcher.Dispose();
            _watchers.Clear();
        }
        // Do not dispose the token source while an in-flight async read unwinds.
    }
}
