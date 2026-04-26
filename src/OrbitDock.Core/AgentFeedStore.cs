using System.Text.Json;
using System.Text.Json.Serialization;

namespace OrbitDock.Core;

public sealed class AgentFeedStore
{
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

        var document = JsonSerializer.Deserialize<AgentFeedDocument>(File.ReadAllText(path), _jsonOptions)
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
        var document = JsonSerializer.Deserialize<AgentFeedDocument>(File.ReadAllText(path), _jsonOptions)
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
            return JsonSerializer.Deserialize<AgentFeedStateDocument>(File.ReadAllText(StatePath), _jsonOptions)
                ?? new AgentFeedStateDocument();
        }
        catch (JsonException)
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
        if (string.IsNullOrWhiteSpace(itemId))
        {
            throw new InvalidOperationException("Checklist item id is required.");
        }

        var state = LoadState();
        var feedState = EnsureFeedState(state, feedId);
        var existing = feedState.Items.FirstOrDefault(item =>
            string.Equals(item.ItemId, itemId, StringComparison.OrdinalIgnoreCase));
        if (existing is null)
        {
            existing = new AgentFeedItemLocalState { ItemId = itemId };
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

        if (string.IsNullOrWhiteSpace(document.Title))
        {
            errors.Add("Agent feed title is required.");
        }

        foreach (var section in document.Sections)
        {
            if (string.IsNullOrWhiteSpace(section.Id))
            {
                errors.Add($"Section '{section.Title}' has an empty id.");
            }

            foreach (var item in section.Items)
            {
                if (string.IsNullOrWhiteSpace(item.Id))
                {
                    errors.Add($"Section '{section.Title}' has an item with an empty id.");
                }

                if (string.IsNullOrWhiteSpace(item.Text))
                {
                    errors.Add($"Checklist/item '{item.Id}' has empty text.");
                }
            }
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
        document.Sections ??= [];

        foreach (var section in document.Sections)
        {
            section.Id = string.IsNullOrWhiteSpace(section.Id) ? Guid.NewGuid().ToString("N") : section.Id.Trim();
            section.Title = (section.Title ?? string.Empty).Trim();
            section.Text = (section.Text ?? string.Empty).Trim();
            section.Items ??= [];
            foreach (var item in section.Items)
            {
                item.Id = string.IsNullOrWhiteSpace(item.Id) ? Guid.NewGuid().ToString("N") : item.Id.Trim();
                item.Text = (item.Text ?? string.Empty).Trim();
                item.Detail = (item.Detail ?? string.Empty).Trim();
                item.Source = (item.Source ?? string.Empty).Trim();
                item.Links ??= [];
                item.Metadata ??= [];
            }
        }
    }

    private static string SanitizeFeedId(string feedId)
    {
        var trimmed = string.IsNullOrWhiteSpace(feedId) ? "feed" : feedId.Trim();
        var chars = trimmed.Select(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_' or '.' ? ch : '-').ToArray();
        var sanitized = new string(chars).Trim('-', '.', '_');
        return string.IsNullOrWhiteSpace(sanitized) ? "feed" : sanitized;
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
