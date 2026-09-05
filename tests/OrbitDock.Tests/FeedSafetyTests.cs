using System.Text;
using System.Text.Json;
using OrbitDock.Core;

internal static class FeedSafetyTests
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public static void Run()
    {
        WorkspaceRelativePaths();
        ReservedStateIdentity();
        LoadedIdentityMustMatch();
        StaleMutationsAreRejected();
        DuplicateItemIdentity();
        CorruptStateIsPreserved();
        StateCapacityIsExplicit();
        SerializedSizeLimits();
        BoundedReads();
        MutationsReadInsideLock();
        ConcurrentUpdatesArePreserved();
    }

    private static void WorkspaceRelativePaths()
    {
        // This is a pure path calculation; no current directory or user store is changed.
        var expected = Path.Combine(Path.GetDirectoryName(Path.GetFullPath("workspace.json"))!, "AgentFeeds");
        Assert(AgentFeedStore.ForWorkspace("workspace.json").FeedsDirectory == expected,
            "A basename workspace must use its local sibling feed directory, not current-user storage.");
    }

    private static void ReservedStateIdentity()
    {
        using var fixture = new Fixture();
        var store = fixture.Store;
        store.MarkUnread("legitimate");
        var before = File.ReadAllBytes(store.StatePath);
        foreach (var id in new[] { "state", "STATE", ".state.", "__State__", "-state-", "/state/", " state " })
        {
            var document = Feed(id);
            Reject(() => store.GetFeedPath(id), "Reserved identity must not resolve to a feed path.");
            Reject(() => store.LoadFeed(id), "Reserved identity must not load internal state as a feed.");
            Reject(() => store.SaveFeed(document), "Reserved identity must not overwrite internal state.");
            Reject(() => store.DeleteFeed(id), "Reserved identity must not delete internal state.");
            Reject(() => store.MarkRead(id), "Reserved identity must not mark state read.");
            Reject(() => store.MarkUnread(id), "Reserved identity must not create local state for itself.");
            Reject(() => store.SetItemState(id, "item", AgentFeedItemState.Done), "Reserved identity must not mutate checklist state.");
            Reject(() => store.IsUnread(document, new()), "Reserved identity must fail display lookups.");
            Reject(() => store.GetEffectiveItemState(new(), id, new()), "Reserved identity must fail checklist lookups.");
            Reject(() => store.CountOpenAttentionItems(document, new()), "Reserved identity must fail empty-feed attention lookups too.");
            Assert(AgentFeedStore.Validate(document).Count > 0, "Static validation must reject normalized reserved identities.");
            var imported = Path.Combine(fixture.Root, "import.json");
            File.WriteAllText(imported, JsonSerializer.Serialize(document, JsonOptions));
            Reject(() => store.LoadFeedFile(imported), "Imported documents cannot claim internal state identity.");
            Assert(store.ValidateFile(imported).Count > 0, "File validation must reject reserved identity.");
            Assert(File.ReadAllBytes(store.StatePath).SequenceEqual(before), "A rejected reserved operation changed internal state.");
        }

        File.WriteAllText(Path.Combine(store.FeedsDirectory, ".state..json"), "{}");
        Assert(!store.ListFeedIds().Any(id => id.Contains("state", StringComparison.OrdinalIgnoreCase)), "Feed enumeration must omit reserved aliases.");
        store.SaveFeed(Feed(" .My Feed. "));
        Assert(store.LoadFeed("My Feed")?.FeedId == "My-Feed", "Existing nonreserved filename normalization must remain compatible.");
    }

    private static void LoadedIdentityMustMatch()
    {
        using var fixture = new Fixture();
        var store = fixture.Store;
        File.WriteAllText(store.GetFeedPath("morning"), JsonSerializer.Serialize(Feed("evening"), JsonOptions));
        Reject(() => store.LoadFeed("morning"), "A feed file must not borrow another feed's local state identity.");
        Reject(() => store.MarkRead("morning"), "Mark-read must reject a mismatched source identity.");
        Assert(!File.Exists(store.StatePath), "A mismatched feed created local read state.");
        var document = Feed(" .MORNING. ");
        File.WriteAllText(store.GetFeedPath("morning"), JsonSerializer.Serialize(document, JsonOptions));
        Assert(store.LoadFeed("morning")?.FeedId == "MORNING", "Equivalent normalized identities should load case-insensitively.");
        document.FeedId = "";
        File.WriteAllText(store.GetFeedPath("morning"), JsonSerializer.Serialize(document, JsonOptions));
        Assert(store.LoadFeed("morning")?.FeedId == "morning", "Missing legacy identity should continue to inherit its requested filename.");
    }

    private static void DuplicateItemIdentity()
    {
        using var fixture = new Fixture();
        var document = Feed("daily");
        document.Sections =
        [
            new() { Id = "first", Items = [new() { Id = "Task", Text = "One" }] },
            new() { Id = "second", Items = [new() { Id = " task ", Text = "Two" }] }
        ];
        Assert(AgentFeedStore.Validate(document).Any(error => error.Contains("duplicate item", StringComparison.OrdinalIgnoreCase)),
            "Item identities must be unique across sections under case-insensitive local state semantics.");
        Reject(() => fixture.Store.SaveFeed(document), "Duplicate items must not be persisted.");
        Assert(!File.Exists(fixture.Store.GetFeedPath("daily")), "Duplicate-item rejection left a published document.");
        document.Sections[1].Items[0].Id = "other-task";
        fixture.Store.SaveFeed(document);
        Assert(fixture.Store.LoadFeed("daily")!.Sections.Count == 2, "Distinct item identities should remain valid.");
    }

    private static void StaleMutationsAreRejected()
    {
        using var fixture = new Fixture();
        var store = fixture.Store;
        var document = Feed("daily");
        store.SaveFeed(document);
        var revision = AgentFeedStore.GetRevisionKey(document);
        store.MarkRead("daily", revision);
        var before = File.ReadAllBytes(store.StatePath);
        document.Revision = "replacement-revision";
        store.SaveFeed(document);
        Reject(() => store.MarkRead("daily", revision), "An old card must not mark its replacement revision read.");
        Reject(() => store.SetItemState("daily", "item", AgentFeedItemState.Done, revision), "An old checklist callback must not mutate its replacement.");
        Reject(() => store.SetItemState("daily", "missing", AgentFeedItemState.Done), "An unknown item must not create orphan checklist state.");
        Assert(File.ReadAllBytes(store.StatePath).SequenceEqual(before), "A stale revision or item mutation changed local state.");
        File.WriteAllText(store.GetFeedPath("daily"), "{broken");
        Reject(() => store.SetItemState("daily", "item", AgentFeedItemState.Done), "Malformed current feed must prevent checklist writes.");
        store.DeleteFeed("daily");
        Reject(() => store.MarkRead("daily"), "Missing current feed must fail explicitly.");
        Reject(() => store.SetItemState("daily", "item", AgentFeedItemState.Done), "Missing current feed must prevent checklist writes.");
        Assert(File.ReadAllBytes(store.StatePath).SequenceEqual(before), "Malformed or missing current feed changed local state.");
    }

    private static void CorruptStateIsPreserved()
    {
        using var fixture = new Fixture();
        var store = fixture.Store;
        store.SaveFeed(Feed("daily"));
        Assert(store.LoadStateStrict().Feeds.Count == 0 && !File.Exists(store.StatePath), "Missing strict-read state must remain valid, read-only empty state.");
        store.MarkUnread("daily");
        Assert(store.LoadStateStrict().Feeds.Single().FeedId == "daily", "Strict reads must preserve valid saved state.");
        foreach (var json in new[]
        {
            "{broken", "null", "{\"schemaVersion\":1,\"feeds\":null}", "{\"schemaVersion\":2,\"feeds\":[]}",
            "{\"feeds\":[null]}", "{\"feeds\":[{\"feedId\":\"daily\",\"items\":null}]}",
            "{\"feeds\":[{\"feedId\":\"daily\"},{\"feedId\":\"DAILY\"}]}",
            "{\"feeds\":[{\"feedId\":\"daily\",\"items\":[{\"itemId\":\"one\"},{\"itemId\":\"ONE\"}]}]}",
            "{\"feeds\":[{\"feedId\":\"state\"}]}"
        })
        {
            File.WriteAllText(store.StatePath, json);
            var before = File.ReadAllBytes(store.StatePath);
            Assert(store.LoadState().Feeds.Count == 0, "Explicit best-effort state loading should retain its recoverable fallback.");
            Reject(() => store.LoadStateStrict(), "Strict state loading must expose corruption to the UI error boundary.");
            Reject(() => store.MarkRead("daily"), "Mark-read must not replace corrupt state.");
            Reject(() => store.MarkUnread("daily"), "Mark-unread must not replace corrupt state.");
            Reject(() => store.SetItemState("daily", "item", AgentFeedItemState.Done), "Checklist writes must not replace corrupt state.");
            Assert(File.ReadAllBytes(store.StatePath).SequenceEqual(before), "A failed mutation overwrote corrupt recovery data.");
        }
    }

    private static void StateCapacityIsExplicit()
    {
        using var fixture = new Fixture();
        var store = fixture.Store;
        var state = new AgentFeedStateDocument
        {
            Feeds = Enumerable.Range(0, AgentFeedStore.MaxStateFeeds).Select(index => new AgentFeedReadState { FeedId = "feed-" + index }).ToList()
        };
        WriteState(store, state);
        store.SaveFeed(Feed("new-feed"));
        var before = File.ReadAllBytes(store.StatePath);
        Reject(() => store.MarkUnread("new-feed"), "A new unread state at feed capacity must fail explicitly.");
        Reject(() => store.MarkRead("new-feed"), "A new read state at feed capacity must fail explicitly.");
        Reject(() => store.SetItemState("new-feed", "item", AgentFeedItemState.Done), "A new checklist feed at capacity must fail explicitly.");
        Assert(File.ReadAllBytes(store.StatePath).SequenceEqual(before), "Feed capacity failure changed existing state.");
        store.MarkUnread("FEED-0");
        Assert(store.LoadState().Feeds.Count == AgentFeedStore.MaxStateFeeds, "Existing feed updates must work at capacity.");

        state.Feeds = [new() { FeedId = "daily", Items = Enumerable.Range(0, AgentFeedStore.MaxItems)
            .Select(index => new AgentFeedItemLocalState { ItemId = "item-" + index }).ToList() }];
        var daily = Feed("daily");
        daily.Sections[0].Items = Enumerable.Range(0, AgentFeedStore.MaxItems - 1)
            .Select(index => new AgentFeedItem { Id = "item-" + index, Text = "Synthetic item" }).ToList();
        daily.Sections[0].Items.Add(new() { Id = "overflow", Text = "A newly published item" });
        store.SaveFeed(daily);
        WriteState(store, state);
        before = File.ReadAllBytes(store.StatePath);
        Reject(() => store.SetItemState("daily", "overflow", AgentFeedItemState.Done), "New checklist items must fail explicitly at capacity.");
        Assert(File.ReadAllBytes(store.StatePath).SequenceEqual(before), "Checklist capacity failure changed existing state.");
        store.SetItemState("DAILY", "ITEM-0", AgentFeedItemState.Done);
        var loaded = store.LoadState().Feeds.Single();
        Assert(loaded.Items.Count == AgentFeedStore.MaxItems && loaded.Items.Single(item => item.ItemId == "item-0").State == AgentFeedItemState.Done,
            "Updating an existing case-insensitive checklist item must work at capacity.");

        state.Feeds[0].Items.Add(new() { ItemId = "overflow" });
        WriteState(store, state);
        before = File.ReadAllBytes(store.StatePath);
        Reject(() => store.MarkUnread("daily"), "Preexisting excessive state must not be silently truncated.");
        Assert(File.ReadAllBytes(store.StatePath).SequenceEqual(before), "Over-capacity state was truncated during mutation.");
        state.Feeds = Enumerable.Range(0, AgentFeedStore.MaxStateFeeds + 1).Select(index => new AgentFeedReadState { FeedId = "feed-" + index }).ToList();
        WriteState(store, state);
        before = File.ReadAllBytes(store.StatePath);
        Reject(() => store.MarkUnread("feed-0"), "Preexisting excessive feed state must not be silently truncated.");
        Assert(File.ReadAllBytes(store.StatePath).SequenceEqual(before), "Over-capacity feed state was truncated during mutation.");
    }

    private static void SerializedSizeLimits()
    {
        using var fixture = new Fixture();
        var store = fixture.Store;
        store.SaveFeed(Feed("daily"));
        var before = File.ReadAllBytes(store.GetFeedPath("daily"));
        var oversized = Feed("daily");
        oversized.Sections = [new() { Id = "items", Items = Enumerable.Range(0, 200)
            .Select(index => new AgentFeedItem { Id = "item-" + index, Text = new string('\u00E9', AgentFeedStore.MaxItemTextLength) }).ToList() }];
        Assert(AgentFeedStore.Validate(oversized).Count == 0, "This fixture must satisfy field limits but exceed serialized UTF-8 limits.");
        Reject(() => store.SaveFeed(oversized), "Serialized feed size must be checked before replacement.");
        Assert(File.ReadAllBytes(store.GetFeedPath("daily")).SequenceEqual(before), "Oversized feed save destroyed the previous publication.");
        Assert(!File.Exists(store.GetFeedPath("daily") + ".tmp"), "Oversized feed save left a temporary file.");

        // A compact imported state is readable but would exceed the same ceiling after
        // model defaults and indented serialization. The write must fail, not poison its own reader.
        var compact = JsonSerializer.Serialize(new
        {
            schemaVersion = 1,
            feeds = Enumerable.Range(0, 32).Select(feed => new
            {
                feedId = "feed-" + feed,
                items = Enumerable.Range(0, AgentFeedStore.MaxItems).Select(item => new { itemId = "item-" + item, state = "open" })
            })
        });
        Assert(Encoding.UTF8.GetByteCount(compact) < AgentFeedStore.MaxStateFileBytes, "Compact state fixture must fit the read ceiling.");
        File.WriteAllText(store.StatePath, compact);
        Assert(store.LoadState().Feeds.Count == 32, "Compact state fixture must be valid.");
        before = File.ReadAllBytes(store.StatePath);
        Reject(() => store.MarkUnread("feed-0"), "Serialized state size must be checked before replacement.");
        Assert(File.ReadAllBytes(store.StatePath).SequenceEqual(before), "Oversized state serialization destroyed existing data.");
        Assert(!File.Exists(store.StatePath + ".tmp"), "Oversized state save left a temporary file.");
    }

    private static void BoundedReads()
    {
        using var fixture = new Fixture();
        var store = fixture.Store;
        var path = store.GetFeedPath("large");
        using (var stream = File.Create(path)) stream.SetLength(AgentFeedStore.MaxFeedFileBytes + 1L);
        Assert(Reject(() => store.LoadFeed("large"), "Oversized input must be rejected before parsing.").Message.Contains("too large", StringComparison.OrdinalIgnoreCase),
            "The byte ceiling must precede JSON parsing.");
        using (var stream = File.Create(store.StatePath)) stream.SetLength(AgentFeedStore.MaxStateFileBytes + 1L);
        Assert(store.LoadState().Feeds.Count == 0, "Oversized best-effort state should recover safely.");
        Reject(() => store.LoadStateStrict(), "Strict state reads must expose the size failure to their caller.");
        Reject(() => store.MarkUnread("daily"), "Oversized existing state must not be overwritten.");
        Assert(new FileInfo(store.StatePath).Length == AgentFeedStore.MaxStateFileBytes + 1L, "Oversized state was overwritten.");
        // Preserve the existing BOM-aware input compatibility within the byte ceiling.
        File.WriteAllText(path, JsonSerializer.Serialize(Feed("large"), JsonOptions), Encoding.Unicode);
        Assert(store.LoadFeed("large")?.FeedId == "large", "Bounded input should preserve supported BOM-aware decoding.");
    }

    private static void MutationsReadInsideLock()
    {
        using var fixture = new Fixture();
        var store = fixture.Store;
        store.SaveFeed(Feed("daily"));
        foreach (var mutate in new Action<AgentFeedStore>[]
        {
            current => current.MarkRead("daily"), current => current.MarkUnread("daily"),
            current => current.SetItemState("daily", "new-item", AgentFeedItemState.Done)
        })
        {
            WriteState(store, new());
            Exception? failure = null;
            using var heldLock = new FileStream(Path.Combine(store.FeedsDirectory, "agent-feeds.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            var worker = new Thread(() => { try { mutate(new AgentFeedStore(store.FeedsDirectory)); } catch (Exception ex) { failure = ex; } }) { IsBackground = true };
            worker.Start();
            // The dedicated worker's only managed wait is AcquireLock's retry sleep.
            // Once it reaches that wait, simulate the preceding lock owner's final write.
            var waiting = SpinWait.SpinUntil(() => (worker.ThreadState & ThreadState.WaitSleepJoin) != 0, TimeSpan.FromSeconds(2));
            try
            {
                WriteState(store, new() { Feeds = [new() { FeedId = "survivor" }] });
            }
            finally { heldLock.Dispose(); }
            Assert(worker.Join(TimeSpan.FromSeconds(8)), "State mutation did not finish after lock release.");
            Assert(waiting && failure is null, "State mutation did not wait cleanly for its lock: " + failure);
            Assert(store.LoadState().Feeds.Any(feed => feed.FeedId == "survivor"), "Mutation read state before acquiring its lock and lost the preceding writer's update.");
        }
    }

    private static void ConcurrentUpdatesArePreserved()
    {
        using var fixture = new Fixture();
        var document = Feed("shared");
        document.Sections[0].Items = Enumerable.Range(0, 12).Select(index => new AgentFeedItem { Id = "item-" + index, Text = "Concurrent fixture item" }).ToList();
        fixture.Store.SaveFeed(document);
        var tasks = Enumerable.Range(0, 12).Select(index => Task.Run(() =>
            new AgentFeedStore(fixture.Store.FeedsDirectory).SetItemState("shared", "item-" + index, AgentFeedItemState.Done))).ToArray();
        Assert(Task.WaitAll(tasks, TimeSpan.FromSeconds(15)), "Concurrent updates exceeded the fixture timeout.");
        Assert(fixture.Store.LoadState().Feeds.Single().Items.Count == tasks.Length, "Concurrent independent checklist updates were lost.");
    }

    private static AgentFeedDocument Feed(string id) => new()
    {
        FeedId = id, Title = "Synthetic feed", Revision = "revision-one",
        Sections = [new() { Id = "tasks", Items = [new() { Id = "item", Text = "Synthetic item" }, new() { Id = "new-item", Text = "Another synthetic item" }] }]
    };
    private static void WriteState(AgentFeedStore store, AgentFeedStateDocument state) =>
        File.WriteAllText(store.StatePath, JsonSerializer.Serialize(state, JsonOptions));

    private static Exception Reject(Action action, string message)
    {
        try { action(); }
        catch (Exception ex) when (ex is InvalidOperationException or JsonException) { return ex; }
        throw new InvalidOperationException(message);
    }

    private static void Assert(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }

    private sealed class Fixture : IDisposable
    {
        private readonly string _base = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "Pandora.FeedSafety.Tests"));
        private readonly string _id = Guid.NewGuid().ToString("N");
        public Fixture()
        {
            Root = Path.Combine(_base, _id);
            Directory.CreateDirectory(Root);
            Store = new AgentFeedStore(Root);
        }
        public string Root { get; }
        public AgentFeedStore Store { get; }
        public void Dispose()
        {
            var actual = Path.GetFullPath(Root);
            var expected = Path.Combine(Path.GetFullPath(Path.Combine(Path.GetTempPath(), "Pandora.FeedSafety.Tests")), _id);
            if (!string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase) || !actual.StartsWith(_base + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Refusing unsafe feed fixture cleanup.");
            Directory.Delete(actual, recursive: true);
        }
    }
}
