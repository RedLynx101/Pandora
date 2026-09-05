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
        FeedsDirectory = Path.GetFullPath(feedsDirectory);
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
        var directory = Path.GetDirectoryName(Path.GetFullPath(workspacePath))
            ?? throw new ArgumentException("Workspace path must name a file.", nameof(workspacePath));
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
            .Where(id => !string.Equals(NormalizeFeedId(id), "state", StringComparison.OrdinalIgnoreCase))
            .OrderBy(id => id, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public AgentFeedDocument? LoadFeed(string feedId)
    {
        var normalizedFeedId = SanitizeFeedId(feedId);
        var path = GetFeedPath(normalizedFeedId);
        if (!File.Exists(path))
        {
            return null;
        }

        var document = JsonSerializer.Deserialize<AgentFeedDocument>(
                ReadAllTextBounded(path, MaxFeedFileBytes, "Agent feed file"),
                _jsonOptions)
            ?? throw new InvalidOperationException($"Agent feed '{feedId}' is empty.");
        NormalizeDocument(document, normalizedFeedId);
        if (!string.Equals(document.FeedId, normalizedFeedId, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Agent feed '{normalizedFeedId}' contains a different feedId '{document.FeedId}'.");
        }

        var errors = Validate(document);
        if (errors.Count > 0)
        {
            throw new InvalidOperationException(string.Join(Environment.NewLine, errors));
        }

        return document;
    }

    public AgentFeedDocument LoadFeedFile(string path, string? fallbackFeedId = null)
    {
        if (fallbackFeedId is not null)
        {
            fallbackFeedId = SanitizeFeedId(fallbackFeedId);
        }

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
        WriteJsonAtomically(GetFeedPath(document.FeedId), document, MaxFeedFileBytes, "Agent feed file");
    }

    public void DeleteFeed(string feedId)
    {
        var path = GetFeedPath(feedId);
        Directory.CreateDirectory(FeedsDirectory);
        using var guard = AcquireLock();
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    /// <summary>Best-effort read for callers that explicitly accept empty state on failure.</summary>
    public AgentFeedStateDocument LoadState()
    {
        try
        {
            return LoadStateForMutation();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException)
        {
            return new AgentFeedStateDocument();
        }
    }

    /// <summary>
    /// Reads local state without hiding invalid or unreadable content. A missing file is
    /// valid empty state; UI callers can catch other failures and display an error card.
    /// </summary>
    public AgentFeedStateDocument LoadStateStrict() => LoadStateForMutation();

    public void MarkRead(string feedId, string? expectedRevision = null)
    {
        var normalizedFeedId = SanitizeFeedId(feedId);
        using var guard = AcquireLock();
        var document = LoadFeedForMutation(normalizedFeedId, expectedRevision);
        var state = LoadStateForMutation();
        var feedState = EnsureFeedState(state, document.FeedId);
        feedState.LastReadRevision = GetRevisionKey(document);
        feedState.LastReadUpdatedUtc = document.UpdatedUtc;
        SaveStateUnderLock(state);
    }

    public void MarkUnread(string feedId)
    {
        var normalizedFeedId = SanitizeFeedId(feedId);
        using var guard = AcquireLock();
        var state = LoadStateForMutation();
        var feedState = EnsureFeedState(state, normalizedFeedId);
        feedState.LastReadRevision = string.Empty;
        feedState.LastReadUpdatedUtc = null;
        SaveStateUnderLock(state);
    }

    public void SetItemState(string feedId, string itemId, AgentFeedItemState itemState, string? expectedRevision = null)
    {
        var normalizedFeedId = SanitizeFeedId(feedId);
        var normalizedItemId = itemId?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(normalizedItemId))
        {
            throw new InvalidOperationException("Checklist item id is required.");
        }

        if (normalizedItemId.Length > MaxIdentifierLength)
        {
            throw new InvalidOperationException($"Checklist item id cannot exceed {MaxIdentifierLength} characters.");
        }

        if (!Enum.IsDefined(itemState))
        {
            throw new InvalidOperationException("Checklist item state is invalid.");
        }

        using var guard = AcquireLock();
        var document = LoadFeedForMutation(normalizedFeedId, expectedRevision);
        if (!document.Sections.SelectMany(section => section.Items)
            .Any(item => string.Equals(item.Id, normalizedItemId, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException($"Checklist item '{normalizedItemId}' is no longer present in agent feed '{normalizedFeedId}'.");
        }

        var state = LoadStateForMutation();
        var feedState = EnsureFeedState(state, normalizedFeedId);
        var existing = feedState.Items.FirstOrDefault(item =>
            string.Equals(item.ItemId, normalizedItemId, StringComparison.OrdinalIgnoreCase));
        if (existing is null)
        {
            if (feedState.Items.Count >= MaxItems)
            {
                throw new InvalidOperationException($"Agent feed state cannot contain more than {MaxItems} items per feed.");
            }

            existing = new AgentFeedItemLocalState { ItemId = normalizedItemId };
            feedState.Items.Add(existing);
        }

        existing.State = itemState;
        existing.UpdatedUtc = DateTime.UtcNow;
        SaveStateUnderLock(state);
    }

    public AgentFeedItemState GetEffectiveItemState(AgentFeedStateDocument state, string feedId, AgentFeedItem item)
    {
        var normalizedFeedId = SanitizeFeedId(feedId);
        var local = state.Feeds
            .FirstOrDefault(feed => string.Equals(feed.FeedId, normalizedFeedId, StringComparison.OrdinalIgnoreCase))
            ?.Items
            .FirstOrDefault(localItem => string.Equals(localItem.ItemId, item.Id, StringComparison.OrdinalIgnoreCase));
        return local?.State ?? item.State;
    }

    public bool IsUnread(AgentFeedDocument document, AgentFeedStateDocument state)
    {
        var normalizedFeedId = SanitizeFeedId(document.FeedId);
        var feedState = state.Feeds.FirstOrDefault(feed =>
            string.Equals(feed.FeedId, normalizedFeedId, StringComparison.OrdinalIgnoreCase));
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
        var normalizedFeedId = SanitizeFeedId(document.FeedId);
        return document.Sections
            .SelectMany(section => section.Items)
            .Count(item =>
            {
                var effective = GetEffectiveItemState(state, normalizedFeedId, item);
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

        if (string.Equals(NormalizeFeedId(document.FeedId), "state", StringComparison.OrdinalIgnoreCase))
        {
            errors.Add("Agent feed feedId 'state' is reserved for local state.");
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
        var itemIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
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
                else if (!itemIds.Add(item.Id.Trim()))
                {
                    errors.Add($"Agent feed contains duplicate item id '{item.Id}'. Item ids must be unique across all sections.");
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

    private AgentFeedDocument LoadFeedForMutation(string feedId, string? expectedRevision)
    {
        var document = LoadFeed(feedId)
            ?? throw new InvalidOperationException($"Agent feed '{feedId}' is no longer available.");
        if (expectedRevision is not null && !string.Equals(GetRevisionKey(document), expectedRevision, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Agent feed '{feedId}' has changed. Refresh it before updating local state.");
        }

        return document;
    }

    private AgentFeedStateDocument LoadStateForMutation()
    {
        if (!File.Exists(StatePath))
        {
            return new AgentFeedStateDocument();
        }

        // Unlike the display-only fallback, mutations must never replace corrupt state.
        var state = JsonSerializer.Deserialize<AgentFeedStateDocument>(
                ReadAllTextBounded(StatePath, MaxStateFileBytes, "Agent feed state file"), _jsonOptions)
            ?? throw new InvalidOperationException("Agent feed state file is empty.");
        NormalizeState(state);
        return state;
    }

    private void SaveStateUnderLock(AgentFeedStateDocument state)
    {
        NormalizeState(state);
        WriteJsonAtomically(StatePath, state, MaxStateFileBytes, "Agent feed state file");
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

    private void WriteJsonAtomically<T>(string path, T value, int maxBytes, string label)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(value, _jsonOptions);
        if (bytes.Length > maxBytes)
        {
            throw new InvalidOperationException($"{label} is too large. Limit is {maxBytes} bytes.");
        }

        var temporaryPath = path + ".tmp";
        try
        {
            File.WriteAllBytes(temporaryPath, bytes);
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

        if (state.Feeds.Count >= MaxStateFeeds)
        {
            throw new InvalidOperationException($"Agent feed state cannot contain more than {MaxStateFeeds} feeds.");
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
        if (state.SchemaVersion != 1 || state.Feeds is null)
        {
            throw new InvalidOperationException("Agent feed state must have schemaVersion 1 and a feeds collection.");
        }

        if (state.Feeds.Count > MaxStateFeeds)
        {
            throw new InvalidOperationException($"Agent feed state cannot contain more than {MaxStateFeeds} feeds.");
        }

        var feedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var feed in state.Feeds)
        {
            if (feed is null || string.IsNullOrWhiteSpace(feed.FeedId) || feed.Items is null)
            {
                throw new InvalidOperationException("Agent feed state contains an invalid feed entry.");
            }

            feed.FeedId = SanitizeFeedId(feed.FeedId);
            if (!feedIds.Add(feed.FeedId))
            {
                throw new InvalidOperationException($"Agent feed state contains duplicate feed id '{feed.FeedId}'.");
            }

            feed.LastReadRevision = (feed.LastReadRevision ?? string.Empty).Trim();
            if (feed.Items.Count > MaxItems)
            {
                throw new InvalidOperationException($"Agent feed state cannot contain more than {MaxItems} items per feed.");
            }

            var itemIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in feed.Items)
            {
                if (item is null || string.IsNullOrWhiteSpace(item.ItemId) || item.ItemId.Trim().Length > MaxIdentifierLength || !Enum.IsDefined(item.State))
                {
                    throw new InvalidOperationException("Agent feed state contains an invalid checklist item.");
                }

                item.ItemId = item.ItemId.Trim();
                if (!itemIds.Add(item.ItemId))
                {
                    throw new InvalidOperationException($"Agent feed state contains duplicate item id '{item.ItemId}'.");
                }
            }
        }
    }

    private static string SanitizeFeedId(string feedId)
    {
        var normalized = NormalizeFeedId(feedId);
        if (string.Equals(normalized, "state", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Agent feed feedId 'state' is reserved for local state.");
        }

        return normalized;
    }

    private static string NormalizeFeedId(string feedId)
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

    private static string ReadAllTextBounded(string path, int maxBytes, string label)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var contents = new MemoryStream();
        var buffer = new byte[8192];
        while (true)
        {
            // Read at most one byte beyond the ceiling, even if a producer grows the file.
            var count = stream.Read(buffer, 0, Math.Min(buffer.Length, maxBytes - (int)contents.Length + 1));
            if (count == 0)
            {
                break;
            }

            if (contents.Length + count > maxBytes)
            {
                throw new InvalidOperationException($"{label} is too large. Limit is {maxBytes} bytes.");
            }

            contents.Write(buffer, 0, count);
        }

        contents.Position = 0;
        using var reader = new StreamReader(contents, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        var text = reader.ReadToEnd();
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
