using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace OrbitDock.Core;

/// <summary>Bounded JSON-only reader. It never loads a browser, executes HTML, follows evidence links, or changes source files.</summary>
public static class MetisReader
{
    public const int MaxHtmlBytes = 4 * 1024 * 1024;
    public const int MaxStateCharacters = 2 * 1024 * 1024;
    public const string SupportedSchema = "codex-director-dashboard/v1";
    private const long MaxSafeInteger = 9_007_199_254_740_991;
    private static readonly Regex Script = new(@"<script\b(?<attributes>[^>]{0,8192})>(?<content>[\s\S]*?)</script\s*>", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(2));
    private static readonly Regex Attribute = new("(?<name>[^\\s=<>/'\"]+)\\s*=\\s*(?:\"(?<double>[^\"]*)\"|'(?<single>[^']*)'|(?<bare>[^\\s>]+))", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
    private static readonly Regex Identifier = new(@"^[A-Za-z0-9][A-Za-z0-9._:-]*$", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
    private static readonly Regex Timestamp = new(@"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(?:\.\d+)?(?:Z|[+-]\d{2}:\d{2})$", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
    private static readonly HashSet<string> Statuses = ["pending", "active", "blocked", "implemented", "verified"];

    public static async Task<MetisSnapshot> ReadAsync(string path, CancellationToken cancellationToken = default)
    {
        var localPath = ProjectPath.Validate(path, requireExists: true);
        await using var stream = new FileStream(localPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete,
            64 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        if (stream.Length > MaxHtmlBytes) throw new MetisValidationException("Dashboard exceeds the 4 MiB read limit.");
        using var buffer = new MemoryStream();
        var bytes = new byte[64 * 1024];
        int count;
        while ((count = await stream.ReadAsync(bytes, cancellationToken)) > 0)
        {
            if (buffer.Length + count > MaxHtmlBytes) throw new MetisValidationException("Dashboard grew beyond the 4 MiB read limit.");
            buffer.Write(bytes, 0, count);
        }
        var html = new UTF8Encoding(false, true).GetString(buffer.ToArray());
        return Extract(html);
    }

    public static MetisSnapshot Extract(string html)
    {
        if (html.Length > MaxHtmlBytes) throw new MetisValidationException("Dashboard exceeds the read limit.");
        string? content = null;
        var matches = 0;
        foreach (Match script in Script.Matches(html))
        {
            var attributes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (Match attribute in Attribute.Matches(script.Groups["attributes"].Value))
            {
                var name = attribute.Groups["name"].Value;
                var value = attribute.Groups["double"].Success ? attribute.Groups["double"].Value :
                    attribute.Groups["single"].Success ? attribute.Groups["single"].Value : attribute.Groups["bare"].Value;
                if (!attributes.TryAdd(name, value) && (name.Equals("id", StringComparison.OrdinalIgnoreCase) || name.Equals("type", StringComparison.OrdinalIgnoreCase)))
                    throw new MetisValidationException("Ambiguous script attributes.");
            }
            if (!attributes.TryGetValue("id", out var id) || id != "dashboard-state") continue;
            matches++;
            if (!attributes.TryGetValue("type", out var type) || !type.Equals("application/json", StringComparison.OrdinalIgnoreCase))
                throw new MetisValidationException("dashboard-state must be application/json.");
            content = script.Groups["content"].Value;
        }
        if (matches != 1 || content is null) throw new MetisValidationException("Expected exactly one application/json script#dashboard-state.");
        return ParseState(content);
    }

    public static MetisSnapshot ParseState(string json)
    {
        if (json.Length > MaxStateCharacters) throw new MetisValidationException("Dashboard state exceeds the 2 MiB limit.");
        using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 40 });
        var root = document.RootElement;
        var nodes = 0;
        CheckStructure(root, ref nodes);
        if (Text(root, "schema") != SupportedSchema) throw new MetisValidationException("Unsupported Metis schema. Supported: " + SupportedSchema, unsupported: true);
        if (Text(root, "mode") != "active-plan") throw new MetisValidationException("Expected active-plan mode.");
        var dashboardId = Id(root, "dashboardId");
        var projectId = Id(root, "projectId");
        var directorId = NullableId(root, "directorSessionId");
        var sample = false;
        if (root.TryGetProperty("templateMode", out var template))
        {
            if (template.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) Fail("templateMode must be boolean.");
            sample = template.GetBoolean();
        }
        var project = Object(root, "project");
        var task = Object(root, "task");
        var plan = Object(root, "plan");
        var revision = Integer(plan, "revision");
        OptionalNullableText(plan, "contractHash");
        var sessions = new List<MetisSession>();
        foreach (var session in Array(root, "sessions")) ReadSession(session, sessions);
        if (sessions.Count > 512) Fail("At most 512 primary sessions can be displayed.");
        var sessionIds = sessions.Select(s => s.Id).ToHashSet(StringComparer.Ordinal);
        if (sessionIds.Count != sessions.Count) Fail("Primary session IDs must be unique.");
        void SessionReference(string? id) { if (id is not null && !sessionIds.Contains(id)) Fail("Unknown primary session reference: " + id); }
        SessionReference(directorId);
        var phases = new List<MetisPhase>();
        foreach (var p in Array(root, "phases"))
        {
            var packages = Array(p, "workPackages").Select(w => new MetisPackage(Id(w, "id"), Text(w, "title", nonempty: true), NullableId(w, "ownerSessionId"), Status(w), IdArray(w, "dependsOn"))).ToList();
            var criteria = Array(p, "criteria").Select(c => new MetisCriterion(Text(c, "title", nonempty: true), Status(c), OptionalNullableText(c, "evidence"))).ToList();
            var mode = Text(p, "executionMode");
            if (mode is not ("single-lane" or "sequential-relay" or "parallel-teams" or "parallel-fan-in")) Fail("Unknown phase executionMode.");
            var phase = new MetisPhase(Id(p, "id"), Text(p, "title", nonempty: true), Text(p, "description"), Status(p), NullableId(p, "accountableOwnerSessionId"),
                NullableId(p, "assignedSessionId"), NullableId(p, "integrationOwnerSessionId"), mode, Integer(p, "planRevision"), packages, criteria);
            SessionReference(phase.AccountableOwnerSessionId); SessionReference(phase.AssignedSessionId); SessionReference(phase.IntegrationOwnerSessionId);
            if (phase.PlanRevision > revision) Fail("Phase assignment revision exceeds plan revision.");
            if (phase.Status == "verified" && (criteria.Any(c => c.Status != "verified") || packages.Any(w => w.Status != "verified"))) Fail("Verified phase contains unverified criteria or packages.");
            if (phase.Status == "implemented" && packages.Any(w => w.Status is not ("implemented" or "verified"))) Fail("Implemented phase contains unfinished packages.");
            phases.Add(phase);
        }
        if (phases.Count > 256) Fail("At most 256 phases can be displayed.");
        var phaseIds = phases.Select(p => p.Id).ToHashSet(StringComparer.Ordinal);
        var allIds = phases.Select(p => p.Id).Concat(phases.SelectMany(p => p.WorkPackages.Select(w => w.Id))).ToList();
        if (allIds.Count != allIds.Distinct(StringComparer.Ordinal).Count()) Fail("Phase and package IDs must be unique.");
        var knownIds = allIds.ToHashSet(StringComparer.Ordinal);
        var edges = phases.ToDictionary(p => p.Id, p => p.WorkPackages.Select(w => w.Id).ToArray(), StringComparer.Ordinal);
        foreach (var package in phases.SelectMany(p => p.WorkPackages))
        {
            SessionReference(package.OwnerSessionId);
            if (package.DependsOn.Any(d => !knownIds.Contains(d))) Fail("Unknown package dependency.");
            edges.Add(package.Id, package.DependsOn.ToArray());
        }
        CheckCycles(edges);
        var currentPhaseId = NullableText(task, "currentPhaseId");
        if (currentPhaseId is not null && !phaseIds.Contains(currentPhaseId)) Fail("Current phase does not resolve.");
        var waits = OptionalArray(root, "waits").Select(w => new MetisWait(Id(w, "target"), Text(w, "wakeCondition"), Time(w, "since"), Integer(w, "livenessWindowMinutes", 1))).ToList();
        foreach (var wait in waits) SessionReference(wait.Target);
        var activity = OptionalArray(root, "activity").Select(a => new MetisActivity(Time(a, "timestamp"), Status(a), Text(a, "title"), Text(a, "detail"), OptionalNullableText(a, "evidence"))).ToList();
        foreach (var point in OptionalArray(root, "verifiedHistory"))
        {
            Time(point, "timestamp"); Integer(point, "count");
            if (point.EnumerateObject().Any(p => p.Name is not ("timestamp" or "count"))) Fail("verifiedHistory points only allow timestamp and count.");
        }
        var planStatus = Status(plan);
        if (planStatus == "verified" && phases.Any(p => p.Status != "verified")) Fail("Verified plan contains a phase awaiting acceptance.");
        return new MetisSnapshot(dashboardId, projectId, Text(project, "name", nonempty: true), NullableText(project, "root"), Id(task, "id"), Text(task, "title", nonempty: true),
            Text(task, "summary"), currentPhaseId, NullableText(task, "nextManagerAction"), Id(plan, "id"), planStatus, revision,
            Text(plan, "source", nonempty: true), Time(plan, "updatedAt"), directorId, sample, phases, sessions,
            Notices(root, "blockers"), Notices(root, "dependencies"), waits, activity, root.Clone());
    }

    private static void ReadSession(JsonElement s, List<MetisSession> result)
    {
        if (result.Count >= 512) Fail("At most 512 primary sessions can be displayed.");
        var budget = Required(s, "subagentBudget");
        result.Add(new MetisSession(Id(s, "id"), Text(s, "name", nonempty: true), Text(s, "role"), Status(s), NullableText(s, "assignment"),
            budget.ValueKind == JsonValueKind.Null ? null : Integer(s, "subagentBudget")));
        foreach (var child in Array(s, "children")) ReadSession(child, result);
    }
    private static IReadOnlyList<MetisNotice> Notices(JsonElement root, string name) => OptionalArray(root, name)
        .Select(n => new MetisNotice(Text(n, "title"), Text(n, "detail"), NullableText(n, "owner"))).ToList();
    private static void CheckCycles(Dictionary<string, string[]> edges)
    {
        // Iterative DFS avoids stack exhaustion on long package chains.
        var done = new HashSet<string>(StringComparer.Ordinal);
        var active = new HashSet<string>(StringComparer.Ordinal);
        foreach (var start in edges.Keys)
        {
            var stack = new Stack<(string Id, bool Exit)>();
            stack.Push((start, false));
            while (stack.TryPop(out var frame))
            {
                if (frame.Exit) { active.Remove(frame.Id); done.Add(frame.Id); continue; }
                if (done.Contains(frame.Id)) continue;
                if (!active.Add(frame.Id)) Fail("Dependency cycle detected.");
                stack.Push((frame.Id, true));
                foreach (var dependency in edges[frame.Id].Reverse()) stack.Push((dependency, false));
            }
        }
    }
    private static void CheckStructure(JsonElement value, ref int count)
    {
        if (++count > 40000) Fail("Dashboard state exceeds the structural limit.");
        if (value.ValueKind == JsonValueKind.Object)
        {
            var keys = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in value.EnumerateObject())
            {
                if (!keys.Add(property.Name)) Fail("Duplicate JSON property: " + property.Name);
                CheckStructure(property.Value, ref count);
            }
        }
        else if (value.ValueKind == JsonValueKind.Array)
            foreach (var item in value.EnumerateArray()) CheckStructure(item, ref count);
    }
    private static JsonElement Required(JsonElement obj, string key)
    {
        if (obj.ValueKind != JsonValueKind.Object || !obj.TryGetProperty(key, out var value)) throw new MetisValidationException("Missing required field: " + key);
        return value;
    }
    private static JsonElement Object(JsonElement obj, string key)
    {
        var value = Required(obj, key);
        if (value.ValueKind != JsonValueKind.Object) Fail(key + " must be an object.");
        return value;
    }
    private static JsonElement[] Array(JsonElement obj, string key)
    {
        var value = Required(obj, key);
        if (value.ValueKind != JsonValueKind.Array) Fail(key + " must be an array.");
        return value.EnumerateArray().ToArray();
    }
    private static JsonElement[] OptionalArray(JsonElement obj, string key) => obj.TryGetProperty(key, out _) ? Array(obj, key) : [];
    private static string Text(JsonElement obj, string key, bool nonempty = false)
    {
        var value = Required(obj, key);
        if (value.ValueKind != JsonValueKind.String || (nonempty && value.GetString()!.Length == 0)) throw new MetisValidationException(key + " must be " + (nonempty ? "a nonempty string." : "a string."));
        return value.GetString()!;
    }
    private static string? NullableText(JsonElement obj, string key) => Required(obj, key).ValueKind == JsonValueKind.Null ? null : Text(obj, key);
    private static string? OptionalNullableText(JsonElement obj, string key) => obj.TryGetProperty(key, out _) ? NullableText(obj, key) : null;
    private static string Id(JsonElement obj, string key)
    {
        var value = Text(obj, key, true);
        if (!Identifier.IsMatch(value)) Fail(key + " is not a valid ID.");
        return value;
    }
    private static string? NullableId(JsonElement obj, string key) => Required(obj, key).ValueKind == JsonValueKind.Null ? null : Id(obj, key);
    private static string[] IdArray(JsonElement obj, string key)
    {
        var values = Array(obj, key).Select(v => v.ValueKind == JsonValueKind.String ? v.GetString()! : "").ToArray();
        if (values.Any(v => !Identifier.IsMatch(v)) || values.Distinct(StringComparer.Ordinal).Count() != values.Length) Fail(key + " must contain unique IDs.");
        return values;
    }
    private static string Status(JsonElement obj)
    {
        var value = Text(obj, "status");
        if (!Statuses.Contains(value)) Fail("Unknown status: " + value);
        return value;
    }
    private static long Integer(JsonElement obj, string key, long minimum = 0)
    {
        var value = Required(obj, key);
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt64(out var result) || result < minimum || result > MaxSafeInteger) throw new MetisValidationException(key + " must be a nonnegative safe integer.");
        return result;
    }
    private static DateTimeOffset Time(JsonElement obj, string key)
    {
        var text = Text(obj, key);
        if (!Timestamp.IsMatch(text) || !DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out var value)) throw new MetisValidationException(key + " requires an ISO timestamp with an explicit timezone.");
        return value;
    }
    private static void Fail(string message) => throw new MetisValidationException(message);
}
