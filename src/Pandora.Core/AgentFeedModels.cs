namespace Pandora.Core;

public sealed class AgentFeedDocument
{
    public int SchemaVersion { get; set; } = 1;
    public string FeedId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string SourceAgent { get; set; } = string.Empty;
    public string Icon { get; set; } = "\uE9D9";
    public AgentFeedStatus Status { get; set; } = AgentFeedStatus.Quiet;
    public string Revision { get; set; } = string.Empty;
    public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;
    public DateTime? ExpiresUtc { get; set; }
    public string Summary { get; set; } = string.Empty;
    public string Markdown { get; set; } = string.Empty;
    public List<AgentFeedSection> Sections { get; set; } = [];
}

public sealed class AgentFeedSection
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Title { get; set; } = string.Empty;
    public AgentFeedSectionKind Kind { get; set; } = AgentFeedSectionKind.Items;
    public string Text { get; set; } = string.Empty;
    public List<AgentFeedItem> Items { get; set; } = [];
}

public sealed class AgentFeedItem
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Text { get; set; } = string.Empty;
    public string Detail { get; set; } = string.Empty;
    public AgentFeedPriority Priority { get; set; } = AgentFeedPriority.P2;
    public AgentFeedItemState State { get; set; } = AgentFeedItemState.Open;
    public DateTime? DueUtc { get; set; }
    public string Source { get; set; } = string.Empty;
    public List<AgentFeedLink> Links { get; set; } = [];
    public Dictionary<string, string> Metadata { get; set; } = [];
}

public sealed class AgentFeedLink
{
    public string Label { get; set; } = string.Empty;
    public string Target { get; set; } = string.Empty;
}

public sealed class AgentFeedStateDocument
{
    public int SchemaVersion { get; set; } = 1;
    public List<AgentFeedReadState> Feeds { get; set; } = [];
}

public sealed class AgentFeedReadState
{
    public string FeedId { get; set; } = string.Empty;
    public string LastReadRevision { get; set; } = string.Empty;
    public DateTime? LastReadUpdatedUtc { get; set; }
    public List<AgentFeedItemLocalState> Items { get; set; } = [];
}

public sealed class AgentFeedItemLocalState
{
    public string ItemId { get; set; } = string.Empty;
    public AgentFeedItemState State { get; set; } = AgentFeedItemState.Open;
    public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;
}

public enum AgentFeedStatus
{
    Quiet,
    Attention,
    ActionNeeded,
    Error
}

public enum AgentFeedSectionKind
{
    Summary,
    Checklist,
    Agenda,
    Items,
    Markdown
}

public enum AgentFeedPriority
{
    P1,
    P2,
    P3
}

public enum AgentFeedItemState
{
    Open,
    Done,
    Dismissed
}
