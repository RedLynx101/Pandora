using CustomFences.Core;

var arguments = args.ToList();
var workspacePath = TakeOption(arguments, "--workspace");
var store = string.IsNullOrWhiteSpace(workspacePath)
    ? WorkspaceStore.ForCurrentUser()
    : new WorkspaceStore(workspacePath);

try
{
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
        "workspace" => HandleWorkspace(store, rest),
        _ => Unknown($"Unknown command group: {group}")
    };
}
catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException)
{
    Console.Error.WriteLine(ex.Message);
    return 1;
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
        default:
            return Unknown($"Unknown dock command: {verb}");
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
            foreach (var pin in WorkspaceLayoutService.EnsureActiveLayout(workspace).DesktopPins)
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
            var workspace = store.LoadOrCreate();
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
    return double.TryParse(value, out var parsed)
        ? parsed
        : throw new InvalidOperationException($"Invalid {name}: {value}");
}

static double ReadOptionDouble(List<string> args, string name)
{
    var value = RequireOption(args, name);
    return double.TryParse(value, out var parsed)
        ? parsed
        : throw new InvalidOperationException($"Invalid {name}: {value}");
}

static int Unknown(string message)
{
    Console.Error.WriteLine(message);
    PrintUsage();
    return 1;
}

static void PrintUsage()
{
    Console.Error.WriteLine("orbitdockctl [--workspace <path>] layout list|save <name>|switch <name>|duplicate <from> <to>|delete <name>");
    Console.Error.WriteLine("orbitdockctl [--workspace <path>] dock list|show <dock>|hide <dock>|set-bounds <dock> <x> <y> <w> <h>");
    Console.Error.WriteLine("orbitdockctl [--workspace <path>] item pin <path> --dock <dock>|unpin <path> --dock <dock>|move <path> --from <dock> --to <dock>|order <dock> <path...>");
    Console.Error.WriteLine("orbitdockctl [--workspace <path>] desktop-pin add <path> --x <x> --y <y>|remove <path-or-id>|list");
    Console.Error.WriteLine("orbitdockctl [--workspace <path>] workspace validate|backup");
}
