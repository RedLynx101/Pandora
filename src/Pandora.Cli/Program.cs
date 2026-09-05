using Pandora.Core;
using System.Text.Json;
using System.Text.Json.Serialization;

try
{
    var arguments = args.ToList();
    var workspacePath = TakeOption(arguments, "--workspace");
    var store = string.IsNullOrWhiteSpace(workspacePath)
        ? WorkspaceStore.ForCurrentUser()
        : new WorkspaceStore(workspacePath);
    var agentFeeds = AgentFeedStore.ForWorkspace(store.WorkspacePath);
    if (arguments.Count == 0)
    {
        PrintUsage();
        return 1;
    }

    var group = arguments[0].ToLowerInvariant();
    var rest = arguments.Skip(1).ToList();
    return group switch
    {
        "layout" => HandleLayout(store, rest),
        "dock" => HandleDock(store, rest),
        "item" => HandleItem(store, rest),
        "desktop-pin" => HandleDesktopPin(store, rest),
        "audio" => HandleAudio(store, rest),
        "agent-feed" => HandleAgentFeed(agentFeeds, rest),
        "project" => HandleProject(new ProjectRegistryStore(Path.Combine(Path.GetDirectoryName(store.WorkspacePath)!, "projects.json")), rest),
        "workspace" => HandleWorkspace(store, rest),
        _ => Unknown($"Unknown command group: {group}")
    };
}
catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException or JsonException or ArgumentException or NotSupportedException or MetisValidationException or OperationCanceledException or System.Text.RegularExpressions.RegexMatchTimeoutException)
{
    Console.Error.WriteLine(ex.Message);
    return 1;
}

static int HandleProject(ProjectRegistryStore store, List<string> args)
{
    var verb = RequireArg(args, 0, "project command").ToLowerInvariant();
    var jsonOptions = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true };
    switch (verb)
    {
        case "list":
            if (args.Count != 1) throw new InvalidOperationException("Usage: project list");
            Console.WriteLine(JsonSerializer.Serialize(store.Load(), jsonOptions));
            return 0;
        case "add":
        {
            var path = RequireArg(args, 1, "absolute dashboard HTML path");
            if (args.Count != 2) throw new InvalidOperationException("Usage: project add <absolute dashboard.html>");
            // A registration is created only after a bounded, JSON-only source validation.
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            MetisReader.ReadAsync(path, timeout.Token).GetAwaiter().GetResult();
            var registration = store.Register(path);
            Console.WriteLine(JsonSerializer.Serialize(registration, jsonOptions));
            return 0;
        }
        case "remove":
        {
            var id = RequireArg(args, 1, "registration ID");
            if (args.Count != 2 || !Guid.TryParseExact(id, "N", out var parsed))
                throw new InvalidOperationException("Usage: project remove <registration-id>");
            id = parsed.ToString("N");
            // Missing IDs are errors and must not create an otherwise absent registry.
            if (!store.Load().Any(entry => entry.Id == id) || !store.Remove(id))
                throw new InvalidOperationException($"Project registration '{id}' was not found.");
            Console.WriteLine($"Removed project registration '{id}'.");
            return 0;
        }
        default:
            return Unknown($"Unknown project command: {verb}");
    }
}

static int HandleAgentFeed(AgentFeedStore store, List<string> args)
{
    var verb = RequireArg(args, 0, "agent-feed command").ToLowerInvariant();
    switch (verb)
    {
        case "list":
        {
            var state = store.LoadState();
            foreach (var feedId in store.ListFeedIds())
            {
                var document = store.LoadFeed(feedId);
                if (document is null)
                {
                    continue;
                }

                var unread = store.IsUnread(document, state) ? "unread" : "read";
                var count = store.CountOpenAttentionItems(document, state);
                Console.WriteLine($"{document.FeedId}\t{document.Title}\t{document.Status}\t{unread}\topen={count}\tupdated={document.UpdatedUtc:o}");
            }

            return 0;
        }
        case "show":
        {
            var feedId = RequireArg(args, 1, "feed id");
            var document = store.LoadFeed(feedId) ?? throw new InvalidOperationException($"Agent feed '{feedId}' was not found.");
            Console.WriteLine(SerializeFeed(document));
            return 0;
        }
        case "write":
        {
            var feedId = RequireArg(args, 1, "feed id");
            var file = RequireOption(args, "--file");
            var document = store.LoadFeedFile(file, feedId);
            document.FeedId = feedId;
            store.SaveFeed(document);
            Console.WriteLine($"Wrote agent feed '{document.FeedId}'.");
            return 0;
        }
        case "publish":
        {
            var feedId = RequireArg(args, 1, "feed id");
            var title = RequireOption(args, "--title");
            var summary = RequireOption(args, "--summary");
            var markdownFile = TakeOption(args, "--markdown-file");
            var checklistFile = TakeOption(args, "--checklist-file");
            var statusText = TakeOption(args, "--status") ?? "quiet";
            if (!Enum.TryParse<AgentFeedStatus>(statusText, ignoreCase: true, out var status) || !Enum.IsDefined(status))
            {
                throw new InvalidOperationException("Status must be quiet, attention, actionNeeded, or error.");
            }

            var document = new AgentFeedDocument
            {
                FeedId = feedId,
                Title = title,
                SourceAgent = TakeOption(args, "--source") ?? "pandoractl",
                Status = status,
                UpdatedUtc = DateTime.UtcNow,
                Revision = DateTime.UtcNow.ToString("O") + "-" + Guid.NewGuid().ToString("N")[..8],
                Summary = summary
            };
            document.Sections.Add(new AgentFeedSection
            {
                Id = "summary",
                Title = "Summary",
                Kind = AgentFeedSectionKind.Summary,
                Text = summary
            });

            if (!string.IsNullOrWhiteSpace(checklistFile))
            {
                document.Sections.Add(new AgentFeedSection
                {
                    Id = "checklist",
                    Title = "What Needs Attention",
                    Kind = AgentFeedSectionKind.Checklist,
                    Items = ReadChecklistItems(checklistFile)
                });
            }

            if (!string.IsNullOrWhiteSpace(markdownFile))
            {
                document.Markdown = ReadTextFileBounded(markdownFile, AgentFeedStore.MaxMarkdownLength, "Markdown file");
                document.Sections.Add(new AgentFeedSection
                {
                    Id = "markdown",
                    Title = "Full Brief",
                    Kind = AgentFeedSectionKind.Markdown,
                    Text = document.Markdown
                });
            }

            store.SaveFeed(document);
            Console.WriteLine($"Published agent feed '{document.FeedId}'.");
            return 0;
        }
        case "clear":
            store.DeleteFeed(RequireArg(args, 1, "feed id"));
            Console.WriteLine("Agent feed cleared.");
            return 0;
        case "mark-read":
            store.MarkRead(RequireArg(args, 1, "feed id"));
            Console.WriteLine("Agent feed marked read.");
            return 0;
        case "mark-unread":
            store.MarkUnread(RequireArg(args, 1, "feed id"));
            Console.WriteLine("Agent feed marked unread.");
            return 0;
        case "complete":
            store.SetItemState(RequireArg(args, 1, "feed id"), RequireArg(args, 2, "item id"), AgentFeedItemState.Done);
            Console.WriteLine("Agent feed item completed.");
            return 0;
        case "reopen":
            store.SetItemState(RequireArg(args, 1, "feed id"), RequireArg(args, 2, "item id"), AgentFeedItemState.Open);
            Console.WriteLine("Agent feed item reopened.");
            return 0;
        case "validate":
        {
            var file = RequireArg(args, 1, "file");
            var errors = store.ValidateFile(file);
            if (errors.Count == 0)
            {
                Console.WriteLine("OK");
                return 0;
            }

            foreach (var error in errors)
            {
                Console.Error.WriteLine(error);
            }

            return 1;
        }
        default:
            return Unknown($"Unknown agent-feed command: {verb}");
    }
}

static int HandleLayout(WorkspaceStore store, List<string> args)
{
    var verb = RequireArg(args, 0, "layout command").ToLowerInvariant();
    var workspace = store.LoadOrCreate();

    switch (verb)
    {
        case "list":
            foreach (var layout in workspace.Layouts.OrderBy(layout => layout.Name, StringComparer.OrdinalIgnoreCase))
            {
                var marker = string.Equals(layout.Name, workspace.ActiveLayoutName, StringComparison.OrdinalIgnoreCase) ? "*" : " ";
                Console.WriteLine($"{marker} {layout.Name}");
            }
            return 0;
        case "save":
            WorkspaceLayoutService.SaveCurrentLayoutAs(workspace, RequireArg(args, 1, "layout name"));
            store.Save(workspace);
            Console.WriteLine($"Saved layout '{workspace.ActiveLayoutName}'.");
            return 0;
        case "switch":
            WorkspaceLayoutService.SwitchLayout(workspace, RequireArg(args, 1, "layout name"));
            store.Save(workspace);
            Console.WriteLine($"Switched to layout '{workspace.ActiveLayoutName}'.");
            return 0;
        case "duplicate":
            WorkspaceLayoutService.DuplicateLayout(workspace, RequireArg(args, 1, "source layout"), RequireArg(args, 2, "target layout"));
            store.Save(workspace);
            Console.WriteLine("Duplicated layout.");
            return 0;
        case "delete":
            WorkspaceLayoutService.DeleteLayout(workspace, RequireArg(args, 1, "layout name"));
            store.Save(workspace);
            Console.WriteLine("Deleted layout.");
            return 0;
        case "variants":
        {
            var layout = WorkspaceLayoutService.EnsureActiveLayout(workspace);
            foreach (var variant in layout.DisplayVariants.OrderBy(variant => variant.Key, StringComparer.OrdinalIgnoreCase))
            {
                var marker = string.Equals(layout.ActiveDisplayVariantKey, variant.Key, StringComparison.OrdinalIgnoreCase) ? "*" : " ";
                Console.WriteLine($"{marker} {variant.Key}\tdefault={variant.IsDefault}\tlastSeenUtc={variant.LastSeenUtc:o}\t{variant.DisplaySignature}");
            }

            return 0;
        }
        case "use-variant":
        {
            var key = RequireArg(args, 1, "display variant key");
            if (string.Equals(key, WorkspaceLayoutService.DefaultDisplayVariantKey, StringComparison.OrdinalIgnoreCase))
            {
                WorkspaceLayoutService.UseDefaultDisplayVariant(workspace);
            }
            else
            {
                WorkspaceLayoutService.UseDisplayVariant(workspace, key, key, []);
            }

            store.Save(workspace);
            Console.WriteLine($"Using display variant '{WorkspaceLayoutService.EnsureActiveLayout(workspace).ActiveDisplayVariantKey}'.");
            return 0;
        }
        default:
            return Unknown($"Unknown layout command: {verb}");
    }
}

static int HandleDock(WorkspaceStore store, List<string> args)
{
    var verb = RequireArg(args, 0, "dock command").ToLowerInvariant();
    var workspace = store.LoadOrCreate();

    switch (verb)
    {
        case "list":
            foreach (var zone in workspace.Zones.OrderBy(zone => zone.Name, StringComparer.OrdinalIgnoreCase))
            {
                Console.WriteLine($"{zone.Id}\t{zone.Name}\tvisible={zone.IsVisible}\tx={zone.Bounds.X:0}\ty={zone.Bounds.Y:0}\tw={zone.Bounds.Width:0}\th={zone.Bounds.Height:0}");
            }
            return 0;
        case "show":
            WorkspaceLayoutService.SetDockVisibility(workspace, RequireArg(args, 1, "dock"), true);
            store.Save(workspace);
            Console.WriteLine("Dock shown.");
            return 0;
        case "hide":
            WorkspaceLayoutService.SetDockVisibility(workspace, RequireArg(args, 1, "dock"), false);
            store.Save(workspace);
            Console.WriteLine("Dock hidden.");
            return 0;
        case "set-bounds":
            WorkspaceLayoutService.SetDockBounds(
                workspace,
                RequireArg(args, 1, "dock"),
                ReadDouble(args, 2, "x"),
                ReadDouble(args, 3, "y"),
                ReadDouble(args, 4, "width"),
                ReadDouble(args, 5, "height"));
            store.Save(workspace);
            Console.WriteLine("Dock bounds saved.");
            return 0;
        case "set-expansion":
        {
            var dock = RequireArg(args, 1, "dock");
            var edgeText = RequireArg(args, 2, "edge");
            if (!Enum.TryParse<DockExpansionEdge>(edgeText, ignoreCase: true, out var edge))
            {
                throw new InvalidOperationException("Expansion edge must be top or bottom.");
            }

            WorkspaceLayoutService.SetExpansionEdge(workspace, dock, edge);
            store.Save(workspace);
            Console.WriteLine($"Dock expansion set to {edge.ToString().ToLowerInvariant()}.");
            return 0;
        }
        default:
            return Unknown($"Unknown dock command: {verb}");
    }
}

static int HandleAudio(WorkspaceStore store, List<string> args)
{
    var verb = RequireArg(args, 0, "audio command").ToLowerInvariant();
    var workspace = store.LoadOrCreate();
    switch (verb)
    {
        case "sfx":
            workspace.Settings.Audio.EnableSoundEffects = ParseOnOff(RequireArg(args, 1, "on|off"));
            store.Save(workspace);
            Console.WriteLine($"Sound effects {(workspace.Settings.Audio.EnableSoundEffects ? "enabled" : "disabled")}.");
            return 0;
        case "music":
        {
            workspace.Settings.Audio.EnableMusicDock = ParseOnOff(RequireArg(args, 1, "on|off"));
            var musicDock = WorkspaceLayoutService.EnsureMusicDock(workspace);
            WorkspaceLayoutService.SetDockVisibility(workspace, musicDock.Id, workspace.Settings.Audio.EnableMusicDock);
            store.Save(workspace);
            Console.WriteLine($"Music dock {(workspace.Settings.Audio.EnableMusicDock ? "enabled" : "disabled")}.");
            return 0;
        }
        case "set-music-folder":
            workspace.Settings.Audio.MusicRootPath = PathExpander.CompressUserPath(RequireArg(args, 1, "path"));
            store.Save(workspace);
            Console.WriteLine($"Music folder set to {workspace.Settings.Audio.MusicRootPath}.");
            return 0;
        default:
            return Unknown($"Unknown audio command: {verb}");
    }
}

static int HandleItem(WorkspaceStore store, List<string> args)
{
    var verb = RequireArg(args, 0, "item command").ToLowerInvariant();
    var workspace = store.LoadOrCreate();

    switch (verb)
    {
        case "pin":
        {
            var path = RequireArg(args, 1, "path");
            var dock = RequireOption(args, "--dock");
            var tab = TakeOption(args, "--tab");
            WorkspaceLayoutService.AddOrShowItem(workspace, path, dock, tab);
            store.Save(workspace);
            Console.WriteLine("Item pinned to dock.");
            return 0;
        }
        case "unpin":
        {
            var path = RequireArg(args, 1, "path");
            var dock = RequireOption(args, "--dock");
            var tab = TakeOption(args, "--tab");
            WorkspaceLayoutService.HideItemInDock(workspace, path, dock, tab);
            store.Save(workspace);
            Console.WriteLine("Item removed from dock.");
            return 0;
        }
        case "move":
        {
            var path = RequireArg(args, 1, "path");
            var from = RequireOption(args, "--from");
            var to = RequireOption(args, "--to");
            var fromTab = TakeOption(args, "--from-tab");
            var toTab = TakeOption(args, "--to-tab") ?? TakeOption(args, "--tab");
            WorkspaceLayoutService.MoveItem(workspace, path, from, fromTab, to, toTab);
            store.Save(workspace);
            Console.WriteLine("Item moved between docks.");
            return 0;
        }
        case "order":
        {
            var dock = RequireArg(args, 1, "dock");
            var paths = args.Skip(2).ToArray();
            if (paths.Length == 0)
            {
                throw new InvalidOperationException("item order requires at least one path.");
            }

            WorkspaceLayoutService.SetItemOrder(workspace, dock, null, paths);
            store.Save(workspace);
            Console.WriteLine("Item order saved.");
            return 0;
        }
        default:
            return Unknown($"Unknown item command: {verb}");
    }
}

static int HandleDesktopPin(WorkspaceStore store, List<string> args)
{
    var verb = RequireArg(args, 0, "desktop-pin command").ToLowerInvariant();
    var workspace = store.LoadOrCreate();

    switch (verb)
    {
        case "add":
        {
            var path = RequireArg(args, 1, "path");
            var x = ReadOptionDouble(args, "--x");
            var y = ReadOptionDouble(args, "--y");
            var sizeText = TakeOption(args, "--size");
            var size = double.TryParse(sizeText, out var parsedSize) ? parsedSize : 52;
            var pin = WorkspaceLayoutService.AddDesktopPin(workspace, path, x, y, size);
            store.Save(workspace);
            Console.WriteLine($"{pin.Id}\t{pin.Path}\tx={pin.X:0}\ty={pin.Y:0}");
            return 0;
        }
        case "remove":
            WorkspaceLayoutService.RemoveDesktopPin(workspace, RequireArg(args, 1, "path-or-id"));
            store.Save(workspace);
            Console.WriteLine("Desktop pin removed.");
            return 0;
        case "list":
            foreach (var pin in WorkspaceLayoutService.EnsureActiveDisplayVariant(workspace).DesktopPins)
            {
                Console.WriteLine($"{pin.Id}\t{pin.Path}\tx={pin.X:0}\ty={pin.Y:0}\tsize={pin.IconSize:0}");
            }
            return 0;
        default:
            return Unknown($"Unknown desktop-pin command: {verb}");
    }
}

static int HandleWorkspace(WorkspaceStore store, List<string> args)
{
    var verb = RequireArg(args, 0, "workspace command").ToLowerInvariant();
    switch (verb)
    {
        case "validate":
        {
            var workspace = store.LoadReadOnly();
            var errors = WorkspaceLayoutService.Validate(workspace);
            if (errors.Count == 0)
            {
                Console.WriteLine($"OK {store.WorkspacePath}");
                return 0;
            }

            foreach (var error in errors)
            {
                Console.Error.WriteLine(error);
            }

            return 1;
        }
        case "backup":
        {
            var path = store.Backup();
            Console.WriteLine(string.IsNullOrWhiteSpace(path) ? "No workspace file to back up." : path);
            return 0;
        }
        default:
            return Unknown($"Unknown workspace command: {verb}");
    }
}

static string SerializeFeed(AgentFeedDocument document)
{
    var options = new JsonSerializerOptions
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };
    return JsonSerializer.Serialize(document, options);
}

static List<AgentFeedItem> ReadChecklistItems(string path)
{
    return ChecklistParser.Parse(ReadTextFileBounded(path, AgentFeedStore.MaxFeedFileBytes, "Checklist file"));
}

static string ReadTextFileBounded(string path, long maxBytes, string label)
{
    using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
    using var buffer = new MemoryStream();
    var chunk = new byte[16 * 1024];
    int count;
    while ((count = stream.Read(chunk)) > 0)
    {
        if (buffer.Length + count > maxBytes)
            throw new InvalidOperationException($"{label} is too large. Limit is {maxBytes} bytes.");
        buffer.Write(chunk, 0, count);
    }
    buffer.Position = 0;
    using var reader = new StreamReader(buffer, detectEncodingFromByteOrderMarks: true);
    return reader.ReadToEnd();
}

static string? TakeOption(List<string> args, string name)
{
    var index = args.FindIndex(arg => string.Equals(arg, name, StringComparison.OrdinalIgnoreCase));
    if (index < 0)
    {
        return null;
    }

    if (index + 1 >= args.Count)
    {
        throw new InvalidOperationException($"Missing value for {name}.");
    }

    var value = args[index + 1];
    args.RemoveAt(index + 1);
    args.RemoveAt(index);
    return value;
}

static string RequireOption(List<string> args, string name)
{
    return TakeOption(args, name) ?? throw new InvalidOperationException($"Missing required option {name}.");
}

static string RequireArg(IReadOnlyList<string> args, int index, string name)
{
    if (index >= args.Count || string.IsNullOrWhiteSpace(args[index]))
    {
        throw new InvalidOperationException($"Missing {name}.");
    }

    return args[index];
}

static double ReadDouble(IReadOnlyList<string> args, int index, string name)
{
    var value = RequireArg(args, index, name);
    return double.TryParse(value, out var parsed) && double.IsFinite(parsed)
        ? parsed
        : throw new InvalidOperationException($"Invalid {name}: {value}");
}

static double ReadOptionDouble(List<string> args, string name)
{
    var value = RequireOption(args, name);
    return double.TryParse(value, out var parsed) && double.IsFinite(parsed)
        ? parsed
        : throw new InvalidOperationException($"Invalid {name}: {value}");
}

static bool ParseOnOff(string value)
{
    if (string.Equals(value, "on", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(value, "true", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(value, "1", StringComparison.OrdinalIgnoreCase))
    {
        return true;
    }

    if (string.Equals(value, "off", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(value, "false", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(value, "0", StringComparison.OrdinalIgnoreCase))
    {
        return false;
    }

    throw new InvalidOperationException("Expected on or off.");
}

static int Unknown(string message)
{
    Console.Error.WriteLine(message);
    PrintUsage();
    return 1;
}

static void PrintUsage()
{
    Console.Error.WriteLine("pandoractl [--workspace <path>] project list|add <absolute dashboard.html>|remove <registration-id>");
    Console.Error.WriteLine("pandoractl [--workspace <path>] layout list|save <name>|switch <name>|duplicate <from> <to>|delete <name>");
    Console.Error.WriteLine("pandoractl [--workspace <path>] layout variants|use-variant <display-signature|default>");
    Console.Error.WriteLine("pandoractl [--workspace <path>] dock list|show <dock>|hide <dock>|set-bounds <dock> <x> <y> <w> <h>|set-expansion <dock> top|bottom");
    Console.Error.WriteLine("pandoractl [--workspace <path>] item pin <path> --dock <dock>|unpin <path> --dock <dock>|move <path> --from <dock> --to <dock>|order <dock> <path...>");
    Console.Error.WriteLine("pandoractl [--workspace <path>] desktop-pin add <path> --x <x> --y <y>|remove <path-or-id>|list");
    Console.Error.WriteLine("pandoractl [--workspace <path>] audio sfx on|off|music on|off|set-music-folder <path>");
    Console.Error.WriteLine("pandoractl [--workspace <path>] agent-feed list|show <feed>|write <feed> --file <json>|publish <feed> --title <text> --summary <text> [--markdown-file <path>] [--checklist-file <json>] [--status quiet|attention|actionNeeded|error]");
    Console.Error.WriteLine("pandoractl [--workspace <path>] agent-feed clear <feed>|mark-read <feed>|mark-unread <feed>|complete <feed> <item>|reopen <feed> <item>|validate <file>");
    Console.Error.WriteLine("pandoractl [--workspace <path>] workspace validate|backup");
}
