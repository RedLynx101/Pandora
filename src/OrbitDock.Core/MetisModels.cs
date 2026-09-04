using System.Text.Json;

namespace OrbitDock.Core;

public sealed record MetisCriterion(string Title, string Status, string? Evidence);
public sealed record MetisPackage(string Id, string Title, string? OwnerSessionId, string Status, IReadOnlyList<string> DependsOn);
public sealed record MetisPhase(string Id, string Title, string Description, string Status,
    string? AccountableOwnerSessionId, string? AssignedSessionId, string? IntegrationOwnerSessionId,
    string ExecutionMode, long PlanRevision, IReadOnlyList<MetisPackage> WorkPackages, IReadOnlyList<MetisCriterion> Criteria)
{
    public int BucketSize => Math.Max(1, WorkPackages.Count > 0 ? WorkPackages.Count : Criteria.Count);
    public int VerifiedCriteria => Criteria.Count(c => c.Status == "verified");
}
public sealed record MetisSession(string Id, string Name, string Role, string Status, string? Assignment, long? SubagentBudget);
public sealed record MetisNotice(string Title, string Detail, string? Owner);
public sealed record MetisWait(string Target, string WakeCondition, DateTimeOffset Since, long LivenessWindowMinutes);
public sealed record MetisActivity(DateTimeOffset Timestamp, string Status, string Title, string Detail, string? Evidence);

/// <summary>Validated, read-only projection; RawState retains unknown v1 fields without interpreting them.</summary>
public sealed record MetisSnapshot(string DashboardId, string ProjectId, string ProjectName, string? ProjectRoot,
    string TaskId, string TaskTitle, string Summary, string? CurrentPhaseId, string? NextAction,
    string PlanId, string PlanStatus, long Revision, string PlanSource, DateTimeOffset UpdatedAt,
    string? DirectorSessionId, bool IsSample, IReadOnlyList<MetisPhase> Phases, IReadOnlyList<MetisSession> Sessions,
    IReadOnlyList<MetisNotice> Blockers, IReadOnlyList<MetisNotice> Dependencies, IReadOnlyList<MetisWait> Waits,
    IReadOnlyList<MetisActivity> Activity, JsonElement RawState)
{
    public string ContentFingerprint { get; } = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(RawState.GetRawText())));
    public int CriteriaCount => Phases.Sum(p => p.Criteria.Count);
    public int VerifiedCriteria => Phases.Sum(p => p.VerifiedCriteria);
    public long DeclaredSubagentBudget => Sessions.Sum(s => s.SubagentBudget ?? 0);
    public int UnknownBudgetCount => Sessions.Count(s => s.SubagentBudget is null);
    public long DeclaredTeamSize => Sessions.Count + DeclaredSubagentBudget;
    public MetisPhase? CurrentPhase => Phases.FirstOrDefault(p => p.Id == CurrentPhaseId);
    public string SessionLabel(string? id)
    {
        if (id is null) return "Unassigned";
        var session = Sessions.First(s => s.Id == id);
        return $"{session.Name} ({session.Id})";
    }
}

public enum MetisReadStatus { Ready, Sample, Missing, Unsupported, Invalid, Duplicate, Regressed, ReadError }

public sealed record MetisProjectRead(ProjectRegistration Registration, MetisSnapshot? Snapshot,
    MetisReadStatus Status, string? Error, DateTimeOffset LastReadAt, DateTimeOffset? LastSuccessfulReadAt)
{
    public bool IsLive => Status == MetisReadStatus.Ready && Snapshot is { IsSample: false };
    public bool IsStale => Snapshot is not null && Status is not (MetisReadStatus.Ready or MetisReadStatus.Sample);
}

public sealed class MetisValidationException(string message, bool unsupported = false) : Exception(message)
{
    public bool Unsupported { get; } = unsupported;
}
