using System.Text.Json;
using System.Text.Json.Nodes;
using Pandora.Core;

/// <summary>Portable synthetic Metis fixtures; no user project or session data.</summary>
public static class MetisTests
{
    public static void Run()
    {
        ProjectionAndNulls(); Validation(); ExtractionBoundary(); PathBoundary(); Registry(); Portfolio().GetAwaiter().GetResult();
    }

    public static JsonObject Fixture() => JsonNode.Parse("""
    {
      "schema":"codex-director-dashboard/v1","dashboardId":"test-dashboard","projectId":"test-project","mode":"active-plan","directorSessionId":"director","templateMode":false,
      "project":{"name":"Synthetic project","root":null},
      "task":{"id":"task-one","title":"Ship the synthetic example","summary":"Test data only","currentPhaseId":"p2","nextManagerAction":"Review the next handoff"},
      "plan":{"id":"plan-one","status":"active","revision":2,"source":"ACTIVE_PLAN.md","contractHash":null,"updatedAt":"2026-09-04T12:00:00-05:00"},
      "phases":[
        {"id":"p1","title":"Foundation","description":"Establish the contract","status":"verified","accountableOwnerSessionId":"director","assignedSessionId":"manager-a","integrationOwnerSessionId":null,"executionMode":"single-lane","planRevision":1,
         "workPackages":[{"id":"w1","title":"Contract","ownerSessionId":"manager-a","dependsOn":[],"status":"verified"}],
         "criteria":[{"title":"Contract accepted","status":"verified","evidence":"Synthetic evidence"}]},
        {"id":"p2","title":"Implementation","description":"Two independent packages","status":"active","accountableOwnerSessionId":"director","assignedSessionId":null,"integrationOwnerSessionId":"manager-a","executionMode":"parallel-fan-in","planRevision":2,
         "workPackages":[{"id":"w2","title":"Renderer","ownerSessionId":"manager-a","dependsOn":["p1"],"status":"active"},{"id":"w3","title":"Checks","ownerSessionId":null,"dependsOn":["w1"],"status":"pending"}],
         "criteria":[{"title":"Renderer checked","status":"active","evidence":null},{"title":"Tests accepted","status":"pending"}]}
      ],
      "sessions":[{"id":"director","name":"Director","role":"director","status":"active","assignment":"Accept outcomes","subagentBudget":null,"children":[
        {"id":"manager-a","name":"Manager A","role":"manager","status":"active","assignment":"Renderer","subagentBudget":2,"children":[]},
        {"id":"manager-b","name":"Manager B","role":"manager","status":"pending","assignment":null,"subagentBudget":0,"children":[]}]}],
      "waits":[{"target":"manager-a","wakeCondition":"Evidence-ready handoff","since":"2026-09-04T11:30:00-05:00","livenessWindowMinutes":30}],
      "blockers":[{"title":"Synthetic dependency","detail":"This is test data","owner":null}],"dependencies":[],
      "activity":[{"timestamp":"2026-09-04T12:00:00-05:00","status":"active","title":"Phase started","detail":"Synthetic event","evidence":null}],
      "verifiedHistory":[{"timestamp":"2026-09-04T11:00:00Z","count":1}]
    }
    """)!.AsObject();

    public static string Html(JsonObject state) => "<!doctype html><html><body><h1>Test fixture</h1><script type=\"application/json\" id=\"dashboard-state\">" + state.ToJsonString().Replace("<", "\\u003c", StringComparison.Ordinal) + "</script></body></html>";

    private static void ProjectionAndNulls()
    {
        var data = Fixture(); data["futureV1Field"] = new JsonObject { ["text"] = "<script>alert(1)</script>" };
        var snapshot = MetisReader.Extract(Html(data));
        Assert(snapshot.CriteriaCount == 3 && snapshot.VerifiedCriteria == 1, "Criteria totals use verified criteria, not phase status or activity counts.");
        Assert(snapshot.Phases[0].BucketSize == 1 && snapshot.Phases[1].BucketSize == 2, "Phase widths use package counts.");
        Assert(snapshot.Sessions.Count == 3 && snapshot.DeclaredSubagentBudget == 2 && snapshot.DeclaredTeamSize == 5 && snapshot.UnknownBudgetCount == 1, "Primary and known budgets preserve null vs zero.");
        Assert(snapshot.Phases[1].AssignedSessionId is null && snapshot.SessionLabel(null) == "Unassigned", "Null ownership is never inferred.");
        Assert(snapshot.RawState.GetProperty("futureV1Field").GetProperty("text").GetString()!.Contains("<script>"), "Unknown v1 fields remain inert data.");
        data["phases"]![1]!["workPackages"] = new JsonArray();
        Assert(MetisReader.Extract(Html(data)).Phases[1].BucketSize == 2, "Criteria count is bucket fallback.");
        data["phases"]![1]!["criteria"] = new JsonArray();
        Assert(MetisReader.Extract(Html(data)).Phases[1].BucketSize == 1, "Empty phase receives a minimal visible bucket.");
    }

    private static void Validation()
    {
        Reject(data => data.Remove("directorSessionId"), "Missing fields fail closed.");
        Reject(data => data["schema"] = "codex-director-dashboard/v2", "Unknown versions are unsupported.");
        Reject(data => data["plan"]!["status"] = "done", "Unknown states cannot silently become verified.");
        Reject(data => data["plan"]!["updatedAt"] = "2026-09-04T12:00:00", "Offset is mandatory.");
        Reject(data => data["plan"]!["updatedAt"] = "2026-02-30T12:00:00Z", "Impossible dates fail.");
        Reject(data => data["directorSessionId"] = "missing", "Director foreign key resolves.");
        Reject(data => data["phases"]![1]!["workPackages"]![0]!["ownerSessionId"] = "missing", "Package foreign key resolves.");
        Reject(data => data["task"]!["currentPhaseId"] = "missing", "Current phase resolves.");
        Reject(data => data["waits"]![0]!["target"] = "missing", "Wait target resolves.");
        Reject(data => data["phases"]![1]!["workPackages"]![0]!["dependsOn"] = new JsonArray("missing"), "Dependencies resolve.");
        Reject(data => data["phases"]![1]!["workPackages"]![0]!["dependsOn"] = new JsonArray("p2"), "Self phase dependency cycle fails.");
        Reject(data => data["phases"]![1]!["workPackages"]![0]!["dependsOn"] = new JsonArray("w3", "w3"), "Duplicate dependency fails.");
        Reject(data => data["phases"]![1]!["id"] = "p1", "Duplicate phase IDs fail.");
        Reject(data => data["phases"]![1]!["workPackages"]![0]!["id"] = "p1", "Phase/package ID collision fails.");
        Reject(data => data["sessions"]![0]!["children"]![0]!["id"] = "director", "Duplicate session IDs fail.");
        Reject(data => data["phases"]![1]!["status"] = "verified", "Phase acceptance requires verified criteria and packages.");
        Reject(data => data["phases"]![1]!["status"] = "implemented", "Implemented phase cannot contain pending packages.");
        Reject(data => data["phases"]![1]!["planRevision"] = 3, "Assignments cannot have a future revision.");
        Reject(data => data["plan"]!["status"] = "verified", "Verified plan cannot include unaccepted phases.");
        Reject(data => data["verifiedHistory"]![0]!["extra"] = true, "History points follow the schema's closed timestamp/count shape.");
        Reject(data => data["sessions"]![0]!["subagentBudget"] = -1, "Negative budgets fail.");
        Reject(data => data["sessions"]![0]!.AsObject().Remove("subagentBudget"), "Missing budget is not null or zero.");
        Reject(data => data["sessions"]![0]!["children"] = new JsonArray(Enumerable.Range(0, 513).Select(i => (JsonNode)new JsonObject
        {
            ["id"] = "s" + i, ["name"] = "Synthetic", ["role"] = "manager", ["status"] = "pending", ["assignment"] = null, ["subagentBudget"] = null, ["children"] = new JsonArray()
        }).ToArray()), "Session bound is enforced.");
    }

    private static void ExtractionBoundary()
    {
        var html = Html(Fixture());
        Throws(() => MetisReader.Extract(html + html), "Duplicate state blocks fail.");
        Throws(() => MetisReader.Extract(html.Replace("id=\"dashboard-state\"", "data-id=\"dashboard-state\"", StringComparison.Ordinal)), "data-id is not id.");
        Throws(() => MetisReader.Extract(html.Replace("application/json", "text/javascript", StringComparison.Ordinal)), "Executable script types fail.");
        Throws(() => MetisReader.ParseState(Fixture().ToJsonString().Replace("\"revision\":2", "\"revision\":2,\"revision\":3", StringComparison.Ordinal)), "Duplicate JSON fields fail.");
        Throws(() => MetisReader.Extract(new string(' ', MetisReader.MaxHtmlBytes + 1)), "HTML limit precedes parsing.");
        Throws(() => MetisReader.ParseState(new string(' ', MetisReader.MaxStateCharacters + 1)), "JSON limit precedes parsing.");
        var supported = false;
        try { var state = Fixture(); state["schema"] = "other/v1"; MetisReader.Extract(Html(state)); }
        catch (MetisValidationException ex) { supported = ex.Unsupported; }
        Assert(supported, "Unsupported versions retain their distinct error type.");
    }

    private static void PathBoundary()
    {
        foreach (var path in new[] { "https://example.invalid/dashboard.html", "relative.html", @"\\server\share\dashboard.html", @"\\?\C:\dashboard.html", "C:\\dashboard.html:stream.html", "C:\\dashboard.exe" })
            Throws(() => ProjectPath.Validate(path, false), "Unsafe or non-HTML registration rejected: " + path);
    }

    private static void Registry()
    {
        using var temporary = new TestDirectory();
        var path = temporary.File("dashboard.html"); File.WriteAllText(path, Html(Fixture()));
        var registryPath = temporary.File("projects.json");
        var first = new ProjectRegistryStore(registryPath); var second = new ProjectRegistryStore(registryPath);
        var registration = first.Register(path);
        Assert(first.Register(path).Id == registration.Id && first.Load().Count == 1, "Registration is idempotent.");
        second.SetExpanded(registration.Id, true);
        var snapshot = MetisReader.Extract(Html(Fixture()));
        first.Accept(new Dictionary<string, ProjectCheckpoint> { [registration.Id] = ProjectRegistryStore.Checkpoint(snapshot) });
        Assert(second.Load()[0].Expanded && second.Load()[0].LastAccepted!.Revision == 2, "Independent stores preserve view state and accepted watermarks.");
        var newer = ProjectRegistryStore.Checkpoint(snapshot) with { Revision = 3 };
        second.Accept(new Dictionary<string, ProjectCheckpoint> { [registration.Id] = newer });
        var rejected = first.Accept(new Dictionary<string, ProjectCheckpoint> { [registration.Id] = ProjectRegistryStore.Checkpoint(snapshot) });
        Assert(rejected.ContainsKey(registration.Id) && second.Load()[0].LastAccepted!.Revision == 3, "A stale cross-dock acceptance explicitly reports rejection instead of publishing the old revision as live.");
        second.Remove(registration.Id);
        Assert(first.Accept(new Dictionary<string, ProjectCheckpoint> { [registration.Id] = newer }).ContainsKey(registration.Id), "A concurrently removed registration cannot be accepted.");
        Assert(first.Load().Count == 0 && File.Exists(path), "Removal never deletes the source dashboard.");
        File.WriteAllText(registryPath, "{broken registry");
        Throws(() => first.Load(), "Corrupt registry isn't silently reset.");
        Throws(() => first.Register(path), "Corrupt registry isn't overwritten by registration.");
        Assert(File.ReadAllText(registryPath) == "{broken registry", "Failed mutations leave corrupt source available for recovery.");
    }

    private static async Task Portfolio()
    {
        using var temporary = new TestDirectory();
        var path = temporary.File("live.html"); var state = Fixture(); File.WriteAllText(path, Html(state));
        var original = File.ReadAllBytes(path);
        var store = new ProjectRegistryStore(temporary.File("projects.json")); var registration = store.Register(path);
        using var service = new ProjectPortfolioService(store, TimeSpan.FromMinutes(10));
        await service.RefreshAsync();
        Assert(service.Entries.Single().IsLive && original.SequenceEqual(File.ReadAllBytes(path)), "Refresh is read-only and accepts valid live data.");
        var samplePath = temporary.File("sample.html"); var sample = Fixture(); sample["templateMode"] = true; sample["dashboardId"] = "sample-dashboard"; sample["plan"]!["id"] = "sample-plan";
        File.WriteAllText(samplePath, Html(sample)); var sampleRegistration = store.Register(samplePath);
        await service.RefreshAsync();
        Assert(service.Entries.Count(e => e.IsLive) == 1 && service.Entries.Count(e => e.Status == MetisReadStatus.Sample) == 1, "Samples never contribute to live totals.");
        var otherPath = temporary.File("other.html"); var other = Fixture(); other["dashboardId"] = "other-dashboard"; other["plan"]!["id"] = "other-plan"; other["task"]!["id"] = "other-task";
        File.WriteAllText(otherPath, Html(other)); var otherRegistration = store.Register(otherPath);
        await service.RefreshAsync();
        Assert(service.Entries.Count(e => e.IsLive) == 2 && service.Entries.Where(e => e.IsLive).Select(e => e.Snapshot!.ProjectId).Distinct().Count() == 1, "One project can retain two separately identified plans.");
        var duplicatePath = temporary.File("duplicate.html"); File.WriteAllText(duplicatePath, Html(state)); var duplicate = store.Register(duplicatePath);
        await service.RefreshAsync();
        Assert(service.Entries.Count(e => e.Status == MetisReadStatus.Duplicate) == 2 && service.Entries.Count(e => e.IsLive) == 1, "Duplicate identities exclude both sources, not just the later read.");
        store.Remove(duplicate.Id); store.Remove(otherRegistration.Id); store.Remove(sampleRegistration.Id);
        await service.RefreshAsync();
        File.WriteAllText(path, "<script type=\"application/json\" id=\"dashboard-state\">{broken</script>");
        await service.RefreshAsync();
        Assert(service.Entries.Single() is { Status: MetisReadStatus.Invalid, IsStale: true, IsLive: false }, "Invalid update retains a labeled last-good snapshot.");
        File.Delete(path); await service.RefreshAsync();
        Assert(service.Entries.Single() is { Status: MetisReadStatus.Missing, IsStale: true }, "Missing source retains stale in-session evidence.");
        state["plan"]!["revision"] = 1; state["phases"]![1]!["planRevision"] = 1; File.WriteAllText(path, Html(state));
        await service.RefreshAsync();
        Assert(service.Entries.Single() is { Status: MetisReadStatus.Regressed, IsStale: true }, "Revision rollback cannot replace the last-good snapshot.");
        using var restarted = new ProjectPortfolioService(new ProjectRegistryStore(store.RegistryPath), TimeSpan.FromMinutes(10));
        await restarted.RefreshAsync();
        Assert(restarted.Entries.Single() is { Status: MetisReadStatus.Regressed, Snapshot: null }, "Persisted watermark rejects regression after restart without inventing cached content.");
        state = Fixture(); state["dashboardId"] = "switched"; File.WriteAllText(path, Html(state)); await service.RefreshAsync();
        Assert(service.Entries.Single().Status == MetisReadStatus.Regressed, "Identity changes need explicit re-registration.");
        state = Fixture(); state["schema"] = "codex-director-dashboard/v2"; File.WriteAllText(path, Html(state)); await service.RefreshAsync();
        Assert(service.Entries.Single().Status == MetisReadStatus.Unsupported, "Unsupported schema is distinguished from missing or invalid.");
        service.Dispose(); await service.RefreshAsync();
    }

    private static void Reject(Action<JsonObject> edit, string message) { var data = Fixture(); edit(data); Throws(() => MetisReader.Extract(Html(data)), message); }
    private static void Throws(Action action, string message)
    {
        try { action(); }
        catch (Exception ex) when (ex is MetisValidationException or JsonException or ArgumentException or IOException or InvalidOperationException) { return; }
        throw new InvalidOperationException(message);
    }
    private static void Assert(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private sealed class TestDirectory : IDisposable
    {
        private readonly string _base = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "Pandora.Metis.Tests"));
        private readonly string _directory;
        public TestDirectory() { _directory = Path.Combine(_base, Guid.NewGuid().ToString("N")); Directory.CreateDirectory(_directory); }
        public string File(string name) => Path.Combine(_directory, name);
        public void Dispose()
        {
            var actual = Path.GetFullPath(_directory);
            if (!actual.StartsWith(_base + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || Path.GetFileName(actual).Length != 32) throw new InvalidOperationException("Refusing unsafe fixture cleanup.");
            Directory.Delete(actual, recursive: true);
        }
    }
}
