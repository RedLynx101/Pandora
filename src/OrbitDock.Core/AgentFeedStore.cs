using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace OrbitDock.Core;

public sealed class AgentFeedStore
{
    public const int MaxFeedFileBytes = 1_048_576;
    public const int MaxStateFileBytes = 1_048_576;
    public const int MaxFeedIdLength = 80;
    public const int MaxIdentifierLength = 120;
    public const int MaxTitleLength = 160;
    public const int MaxSourceLength = 160;
    public const int MaxIconLength = 16;
    public const int MaxSummaryLength = 4_000;
    public const int MaxMarkdownLength = 20_000;
    public const int MaxSectionTextLength = 8_000;
    public const int MaxItemTextLength = 1_000;
    public const int MaxItemDetailLength = 2_000;
    public const int MaxSections = 32;
    public const int MaxItems = 500;
    public const int MaxLinksPerItem = 8;
    public const int MaxMetadataEntriesPerItem = 32;
    public const int MaxStateFeeds = 64;

    private const int LockRetryCount = 80;
    private const int LockRetryDelayMilliseconds = 50;
    private const string StateFileName = "state.json";

    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public AgentFeedStore(string feedsDirectory)
    {
        FeedsDirectory = feedsDirectory;
    }

    public string FeedsDirectory { get; }
    public string StatePath => Path.Combine(FeedsDirectory, StateFileName);

    public static AgentFeedStore ForCurrentUser()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return new AgentFeedStore(Path.Combine(appData, "OrbitDock", "AgentFeeds"));
    }

    public static AgentFeedStore ForWorkspace(string workspacePath)
    {
        var directory = Path.GetDirectoryName(workspacePath);
        if (string.IsNullOrWhiteSpace(directory))
        {
            return ForCurrentUser();
        }

        return new AgentFeedStore(Path.Combine(directory, "AgentFeeds"));
    }

    public IReadOnlyList<string> ListFeedIds()
    {
        if (!Directory.Exists(FeedsDirectory))
        {
            return [];
        }

        return Directory.EnumerateFiles(FeedsDirectory, "*.json", SearchOption.TopDirectoryOnly)
            .Where(path => !string.Equals(Path.GetFileName(path), StateFileName, StringComparison.OrdinalIgnoreCase))
            .Select(path => Path.GetFileNameWithoutExtension(path))
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .OrderBy(id => id, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public AgentFeedDocument? LoadFeed(string feedId)
    {
        var path = GetFeedPath(feedId);
        if (!File.Exists(path))
        {
            return null;
        }

        var document = JsonSerializer.Deserialize<AgentFeedDocument>(
                ReadAllTextBounded(path, MaxFeedFileBytes, "Agent feed file"),
                _jsonOptions)
            ?? throw new InvalidOperationException($"Agent feed '{feedId}' is empty.");
        NormalizeDocument(document, feedId);
        var errors = Validate(document);
        if (errors.Count > 0)
        {
            throw new InvalidOperationException(string.Join(Environment.NewLine, errors));
        }

        return document;
    }

    public AgentFeedDocument LoadFeedFile(string path, string? fallbackFeedId = null)
    {
        var document = JsonSerializer.Deserialize<AgentFeedDocument>(
                ReadAllTextBounded(path, MaxFeedFileBytes, "Agent feed file"),
                _jsonOptions)
            ?? throw new InvalidOperationException("Agent feed file is empty.");
        NormalizeDocument(document, fallbackFeedId ?? document.FeedId);
        var errors = Validate(document);
        if (errors.Count > 0)
        {
            throw new InvalidOperationException(string.Join(Environment.NewLine, errors));
        }

        return document;
    }

    public void SaveFeed(AgentFeedDocument document)
    {
        NormalizeDocument(document, document.FeedId);
        var errors = Validate(document);
        if (errors.Count > 0)
        {
            throw new InvalidOperationException(string.Join(Environment.NewLine, errors));
        }

        Directory.CreateDirectory(FeedsDirectory);
        using var guard = AcquireLock();
        WriteJsonAtomically(GetFeedPath(document.FeedId), document);
    }

    public void DeleteFeed(string feedId)
    {
        Directory.CreateDirectory(FeedsDirectory);
        using var guard = AcquireLock();
        var path = GetFeedPath(feedId);
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    public AgentFeedStateDocument LoadState()
    {
        if (!File.Exists(StatePath))
        {
            return new AgentFeedStateDocument();
        }

        try
        {
            var state = JsonSerializer.Deserialize<AgentFeedStateDocument>(
                    ReadAllTextBounded(StatePath, MaxStateFileBytes, "Agent feed state file"),
                    _jsonOptions)
                ?? new AgentFeedStateDocument();
            NormalizeState(state);
            return state;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException)
        {
            return new AgentFeedStateDocument();
        }
    }

    public void MarkRead(string feedId)
    {
        var document = LoadFeed(feedId);
        if (document is null)
        {
            return;
        }

        var state = LoadState();
        var feedState = EnsureFeedState(state, document.FeedId);
        feedState.LastReadRevision = GetRevisionKey(document);
        feedState.LastReadUpdatedUtc = document.UpdatedUtc;
        SaveState(state);
    }

    public void MarkUnread(string feedId)
    {
        var state = LoadState();
        var feedState = EnsureFeedState(state, feedId);
        feedState.LastReadRevision = string.Empty;
        feedState.LastReadUpdatedUtc = null;
        SaveState(state);
    }

    public void SetItemState(string feedId, string itemId, AgentFeedItemState itemState)
    {
        var normalizedItemId = itemId?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(normalizedItemId))
        {
            throw new InvalidOperationException("Checklist item id is required.");
        }

        if (normalizedItemId.Length > MaxIdentifierLength)
        {
            throw new InvalidOperationException($"Checklist item id cannot exceed {MaxIdentifierLength} characters.");
        }

        var state = LoadState();
        var feedState = EnsureFeedState(state, feedId);
        var existing = feedState.Items.FirstOrDefault(item =>
            string.Equals(item.ItemId, normalizedItemId, StringComparison.OrdinalIgnoreCase));
        if (existing is null)
        {
            existing = new AgentFeedItemLocalState { ItemId = normalizedItemId };
            feedState.Items.Add(existing);
        }

        existing.State = itemState;
        existing.UpdatedUtc = DateTime.UtcNow;
        SaveState(state);
    }

    public AgentFeedItemState GetEffectiveItemState(AgentFeedStateDocument state, string feedId, AgentFeedItem item)
    {
        var local = state.Feeds
            .FirstOrDefault(feed => string.Equals(feed.FeedId, feedId, StringComparison.OrdinalIgnoreCase))
            ?.Items
            .FirstOrDefault(localItem => string.Equals(localItem.ItemId, item.Id, StringComparison.OrdinalIgnoreCase));
        return local?.State ?? item.State;
    }

    public bool IsUnread(AgentFeedDocument document, AgentFeedStateDocument state)
    {
        var feedState = state.Feeds.FirstOrDefault(feed =>
            string.Equals(feed.FeedId, document.FeedId, StringComparison.OrdinalIgnoreCase));
        if (feedState is null)
        {
            return true;
        }

        var revision = GetRevisionKey(document);
        if (!string.IsNullOrWhiteSpace(revision))
        {
            return !string.Equals(feedState.LastReadRevision, revision, StringComparison.Ordinal);
        }

        return feedState.LastReadUpdatedUtc is null || document.UpdatedUtc > feedState.LastReadUpdatedUtc.Value;
    }

    public int CountOpenAttentionItems(AgentFeedDocument document, AgentFeedStateDocument state)
    {
        return document.Sections
            .SelectMany(section => section.Items)
            .Count(item =>
            {
                var effective = GetEffectiveItemState(state, document.FeedId, item);
                return effective == AgentFeedItemState.Open &&
                       (item.Priority is AgentFeedPriority.P1 or AgentFeedPriority.P2 ||
                        document.Status is AgentFeedStatus.Attention or AgentFeedStatus.ActionNeeded or AgentFeedStatus.Error);
            });
    }

    public IReadOnlyList<string> ValidateFile(string path)
    {
        try
        {
            var document = LoadFeedFile(path);
            return Validate(document);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException)
        {
            return [ex.Message];
        }
    }

    public string GetFeedPath(string feedId)
    {
        return Path.Combine(FeedsDirectory, SanitizeFeedId(feedId) + ".json");
    }

    public static IReadOnlyList<string> Validate(AgentFeedDocument document)
    {
        var errors = new List<string>();
        if (document.SchemaVersion != 1)
        {
            errors.Add("Agent feed schemaVersion must be 1.");
        }

        if (string.IsNullOrWhiteSpace(document.FeedId))
        {
            errors.Add("Agent feed feedId is required.");
        }

        AddLengthError(errors, "Agent feed feedId", document.FeedId, MaxFeedIdLength);
        AddLengthError(errors, "Agent feed title", document.Title, MaxTitleLength);
        AddLengthError(errors, "Agent feed sourceAgent", document.SourceAgent, MaxSourceLength);
        AddLengthError(errors, "Agent feed icon", document.Icon, MaxIconLength);
        AddLengthError(errors, "Agent feed revision", document.Revision, MaxIdentifierLength);
        AddLengthError(errors, "Agent feed summary", document.Summary, MaxSummaryLength);
        AddLengthError(errors, "Agent feed markdown", document.Markdown, MaxMarkdownLength);

        if (string.IsNullOrWhiteSpace(document.Title))
        {
            errors.Add("Agent feed title is required.");
        }

        var sections = document.Sections ?? [];
        if (sections.Count > MaxSections)
        {
            errors.Add($"Agent feed cannot contain more than {MaxSections} sections.");
        }

        var itemCount = 0;
        foreach (var section in sections)
        {
            if (section is null)
            {
                errors.Add("Agent feed cannot contain empty sections.");
                continue;
            }

            if (string.IsNullOrWhiteSpace(section.Id))
            {
                errors.Add($"Section '{section.Title}' has an empty id.");
            }

            AddLengthError(errors, $"Section '{section.Title}' id", section.Id, MaxIdentifierLength);
            AddLengthError(errors, $"Section '{section.Title}' title", section.Title, MaxTitleLength);
            AddLengthError(errors, $"Section '{section.Title}' text", section.Text, MaxSectionTextLength);

            var items = section.Items ?? [];
            itemCount += items.Count;
            foreach (var item in items)
            {
                if (item is null)
                {
                    errors.Add($"Section '{section.Title}' cannot contain empty items.");
                    continue;
                }

                if (string.IsNullOrWhiteSpace(item.Id))
                {
                    errors.Add($"Section '{section.Title}' has an item with an empty id.");
                }

                if (string.IsNullOrWhiteSpace(item.Text))
                {
                    errors.Add($"Checklist/item '{item.Id}' has empty text.");
                }

                AddLengthError(errors, $"Checklist/item '{item.Id}' id", item.Id, MaxIdentifierLength);
                AddLengthError(errors, $"Checklist/item '{item.Id}' text", item.Text, MaxItemTextLength);
                AddLengthError(errors, $"Checklist/item '{item.Id}' detail", item.Detail, MaxItemDetailLength);
                AddLengthError(errors, $"Checklist/item '{item.Id}' source", item.Source, MaxSourceLength);

                var links = item.Links ?? [];
                var metadata = item.Metadata ?? [];

                if (links.Count > MaxLinksPerItem)
                {
                    errors.Add($"Checklist/item '{item.Id}' cannot contain more than {MaxLinksPerItem} links.");
                }

                if (metadata.Count > MaxMetadataEntriesPerItem)
                {
                    errors.Add($"Checklist/item '{item.Id}' cannot contain more than {MaxMetadataEntriesPerItem} metadata entries.");
                }

                foreach (var link in links)
                {
                    if (link is null)
                    {
                        errors.Add($"Checklist/item '{item.Id}' cannot contain empty links.");
                        continue;
                    }

                    AddLengthError(errors, $"Checklist/item '{item.Id}' link label", link.Label, MaxTitleLength);
                    AddLengthError(errors, $"Checklist/item '{item.Id}' link target", link.Target, MaxItemDetailLength);
                }

                foreach (var entry in metadata)
                {
                    AddLengthError(errors, $"Checklist/item '{item.Id}' metadata key", entry.Key, MaxIdentifierLength);
                    AddLengthError(errors, $"Checklist/item '{item.Id}' metadata value", entry.Value, MaxItemDetailLength);
                }
            }
        }

        if (itemCount > MaxItems)
        {
            errors.Add($"Agent feed cannot contain more than {MaxItems} total items.");
        }

        return errors;
    }

    public static string GetRevisionKey(AgentFeedDocument document)
    {
        return string.IsNullOrWhiteSpace(document.Revision)
            ? document.UpdatedUtc.ToUniversalTime().ToString("O")
            : document.Revision.Trim();
    }

    private void SaveState(AgentFeedStateDocument state)
    {
        NormalizeState(state);
        Directory.CreateDirectory(FeedsDirectory);
        using var guard = AcquireLock();
        WriteJsonAtomically(StatePath, state);
    }

    private FileStream AcquireLock()
    {
        var lockPath = Path.Combine(FeedsDirectory, "agent-feeds.lock");
        Directory.CreateDirectory(FeedsDirectory);

        Exception? lastError = null;
        for (var i = 0; i < LockRetryCount; i++)
        {
            try
            {
                return new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            }
            catch (IOException ex)
            {
                lastError = ex;
                Thread.Sleep(LockRetryDelayMilliseconds);
            }
            catch (UnauthorizedAccessException ex)
            {
                lastError = ex;
                Thread.Sleep(LockRetryDelayMilliseconds);
            }
        }

        throw new IOException($"Could not acquire agent feed lock: {lockPath}", lastError);
    }

    private void WriteJsonAtomically<T>(string path, T value)
    {
        var temporaryPath = path + ".tmp";
        try
        {
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(value, _jsonOptions));
            if (File.Exists(path))
            {
                File.Replace(temporaryPath, path, null);
            }
            else
            {
                File.Move(temporaryPath, path);
            }
        }
        finally
        {
            TryDelete(temporaryPath);
        }
    }

    private static AgentFeedReadState EnsureFeedState(AgentFeedStateDocument state, string feedId)
    {
        var normalized = SanitizeFeedId(feedId);
        var feedState = state.Feeds.FirstOrDefault(feed =>
            string.Equals(feed.FeedId, normalized, StringComparison.OrdinalIgnoreCase));
        if (feedState is not null)
        {
            return feedState;
        }

        feedState = new AgentFeedReadState { FeedId = normalized };
        state.Feeds.Add(feedState);
        return feedState;
    }

    private static void NormalizeDocument(AgentFeedDocument document, string? fallbackFeedId)
    {
        document.SchemaVersion = document.SchemaVersion == 0 ? 1 : document.SchemaVersion;
        document.FeedId = SanitizeFeedId(string.IsNullOrWhiteSpace(document.FeedId) ? fallbackFeedId ?? string.Empty : document.FeedId);
        document.Title = string.IsNullOrWhiteSpace(document.Title) ? document.FeedId : document.Title.Trim();
        document.SourceAgent = (document.SourceAgent ?? string.Empty).Trim();
        document.Icon = string.IsNullOrWhiteSpace(document.Icon) ? "\uE9D9" : document.Icon.Trim();
        document.UpdatedUtc = document.UpdatedUtc == default ? DateTime.UtcNow : document.UpdatedUtc.ToUniversalTime();
        document.Revision = string.IsNullOrWhiteSpace(document.Revision)
            ? document.UpdatedUtc.ToUniversalTime().ToString("yyyyMMddHHmmss")
            : document.Revision.Trim();
        document.Summary = (document.Summary ?? string.Empty).Trim();
        document.Markdown = (document.Markdown ?? string.Empty).Trim();
        document.Sections = (document.Sections ?? [])
            .Where(section => section is not null)
            .ToList();

        foreach (var section in document.Sections)
        {
            section.Id = string.IsNullOrWhiteSpace(section.Id) ? Guid.NewGuid().ToString("N") : section.Id.Trim();
            section.Title = (section.Title ?? string.Empty).Trim();
            section.Text = (section.Text ?? string.Empty).Trim();
            section.Items = (section.Items ?? [])
                .Where(item => item is not null)
                .ToList();
            foreach (var item in section.Items)
            {
                item.Id = string.IsNullOrWhiteSpace(item.Id) ? Guid.NewGuid().ToString("N") : item.Id.Trim();
                item.Text = (item.Text ?? string.Empty).Trim();
                item.Detail = (item.Detail ?? string.Empty).Trim();
                item.Source = (item.Source ?? string.Empty).Trim();
                item.Links = (item.Links ?? [])
                    .Where(link => link is not null)
                    .ToList();
                foreach (var link in item.Links)
                {
                    link.Label = (link.Label ?? string.Empty).Trim();
                    link.Target = (link.Target ?? string.Empty).Trim();
                }

                item.Metadata = (item.Metadata ?? [])
                    .Where(entry => !string.IsNullOrWhiteSpace(entry.Key))
                    .GroupBy(entry => entry.Key.Trim(), StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(
                        group => group.Key,
                        group => (group.Last().Value ?? string.Empty).Trim(),
                        StringComparer.OrdinalIgnoreCase);
            }
        }
    }

    private static void NormalizeState(AgentFeedStateDocument state)
    {
        state.SchemaVersion = state.SchemaVersion == 0 ? 1 : state.SchemaVersion;
        state.Feeds = (state.Feeds ?? [])
            .Where(feed => feed is not null)
            .Take(MaxStateFeeds)
            .ToList();

        foreach (var feed in state.Feeds)
        {
            feed.FeedId = SanitizeFeedId(feed.FeedId);
            feed.LastReadRevision = (feed.LastReadRevision ?? string.Empty).Trim();
            feed.Items = (feed.Items ?? [])
                .Where(item => item is not null && !string.IsNullOrWhiteSpace(item.ItemId))
                .GroupBy(item => item.ItemId.Trim(), StringComparer.OrdinalIgnoreCase)
                .Select(group => group.Last())
                .Take(MaxItems)
                .ToList();

            foreach (var item in feed.Items)
            {
                item.ItemId = item.ItemId.Trim();
            }
        }
    }

    private static string SanitizeFeedId(string feedId)
    {
        var trimmed = string.IsNullOrWhiteSpace(feedId) ? "feed" : feedId.Trim();
        var chars = trimmed.Select(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_' or '.' ? ch : '-').ToArray();
        var sanitized = new string(chars).Trim('-', '.', '_');
        if (sanitized.Length > MaxFeedIdLength)
        {
            sanitized = sanitized[..MaxFeedIdLength].Trim('-', '.', '_');
        }

        return string.IsNullOrWhiteSpace(sanitized) ? "feed" : sanitized;
    }

    private static string ReadAllTextBounded(string path, long maxBytes, string label)
    {
        var info = new FileInfo(path);
        if (info.Exists && info.Length > maxBytes)
        {
            throw new InvalidOperationException($"{label} is too large. Limit is {maxBytes} bytes.");
        }

        var text = File.ReadAllText(path);
        if (Encoding.UTF8.GetByteCount(text) > maxBytes)
        {
            throw new InvalidOperationException($"{label} is too large. Limit is {maxBytes} bytes.");
        }

        return text;
    }

    private static void AddLengthError(List<string> errors, string label, string? value, int maxLength)
    {
        if ((value ?? string.Empty).Length > maxLength)
        {
            errors.Add($"{label} cannot exceed {maxLength} characters.");
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // Temporary cleanup should not mask the original save failure.
        }
    }
}
