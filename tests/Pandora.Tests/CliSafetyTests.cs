using System.Diagnostics;
using System.Text.Json;
using Pandora.Core;

internal static class CliSafetyTests
{
    public static void Run()
    {
        var before = ChecklistParser.Parse("[\"Review invoice\",\"Call supplier\"]");
        var reordered = ChecklistParser.Parse("Call supplier\nReview invoice");
        Check(before[0].Id == reordered[1].Id && before[1].Id == reordered[0].Id, "Simple IDs must follow content after reordering.");
        Check(ChecklistParser.Parse("New task")[0].Id != before[0].Id, "Unrelated task must not inherit prior completion.");
        foreach (var separator in new[] { "\n", "\r\n", "\r" })
            Check(ChecklistParser.Parse("Review invoice" + separator + "Call supplier").Select(i => i.Id).SequenceEqual(before.Select(i => i.Id)), "All ordinary newline formats must agree.");
        var duplicates = ChecklistParser.Parse("Same task\nSame task");
        Check(duplicates[0].Id != duplicates[1].Id, "Duplicate simple tasks need independent IDs.");
        Check(ChecklistParser.Parse("[{\"id\":\"explicit-id\",\"text\":\"New description\"}]")[0].Id == "explicit-id", "Explicit producer IDs must remain stable.");
        try { ChecklistParser.Parse("[invalid"); throw new Exception("Malformed JSON was accepted as task text."); }
        catch (JsonException) { }

        var root = Path.Combine(Path.GetTempPath(), "Pandora.CliSafety", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var repo = FindRepo();
        var workspace = Path.Combine(root, "workspace.json");
        foreach (var input in new[] { "{", "null", "{\"settings\":null}", "{\"schemaVersion\":999,\"futureData\":\"keep\"}" })
        {
            File.WriteAllText(workspace, input);
            var files = Directory.GetFiles(root).Order().ToArray();
            var result = RunCli(repo, root, "--workspace", "workspace.json", "workspace", "validate");
            Check(result.ExitCode != 0, "Invalid/unsupported workspace validation must fail.");
            Check(File.ReadAllText(workspace) == input && Directory.GetFiles(root).Order().SequenceEqual(files), "Validation must not write, import, lock, back up or replace data.");
        }
        // All test targets are generated children, never the real user store.
        var missing = Path.Combine(root, "missing", "workspace.json");
        Check(RunCli(repo, root, "--workspace", missing, "workspace", "validate").ExitCode != 0 && !Directory.Exists(Path.GetDirectoryName(missing)), "Missing validation must not create a directory.");
        Check(RunCli(repo, root, "--workspace").ExitCode != 0, "Missing option value must produce a clean CLI failure.");

        var isolated = Path.Combine(root, "isolated");
        Directory.CreateDirectory(isolated);
        var publish = RunCli(repo, isolated, "--workspace", "workspace.json", "agent-feed", "publish", "fixture", "--title", "Fixture", "--summary", "Local only");
        Check(publish.ExitCode == 0 && File.Exists(Path.Combine(isolated, "AgentFeeds", "fixture.json")), "Basename workspace must keep feeds in its explicit working directory.");
        var stateStore = new AgentFeedStore(Path.Combine(isolated, "AgentFeeds"));
        stateStore.MarkRead("fixture");
        var stateBytes = File.ReadAllBytes(stateStore.StatePath);
        Check(RunCli(repo, isolated, "--workspace", "workspace.json", "agent-feed", "clear", "STATE").ExitCode != 0, "Reserved state ID must fail through CLI.");
        Check(File.ReadAllBytes(stateStore.StatePath).SequenceEqual(stateBytes), "Rejected CLI clear must preserve state.");
        Check(RunCli(repo, isolated, "--workspace", "workspace.json", "agent-feed", "publish", "fixture", "--title", "Fixture", "--summary", "Local", "--status", "99").ExitCode != 0, "Numeric undefined status must be rejected.");
        ProjectRegistrationCommands(repo, root);
    }

    private static void ProjectRegistrationCommands(string repo, string root)
    {
        var directory = Path.Combine(root, "project-commands");
        var workspace = Path.Combine(directory, "workspace.json");
        var registryPath = Path.Combine(directory, "projects.json");
        var list = RunCli(repo, root, "--workspace", workspace, "project", "list");
        Check(list.ExitCode == 0 && JsonDocument.Parse(list.Output).RootElement.GetArrayLength() == 0, "An absent project registry must list as an empty JSON array.");
        Check(!Directory.Exists(directory), "Project listing must not create a directory, registry or lock.");
        Check(RunCli(repo, root, "--workspace", workspace, "project", "remove", Guid.NewGuid().ToString("N")).ExitCode != 0 && !Directory.Exists(directory), "Removing a missing ID must fail without creating a registry.");
        Check(RunCli(repo, root, "--workspace", workspace, "project", "remove", "not-an-id").ExitCode != 0 && !Directory.Exists(directory), "Invalid registration IDs must fail without mutation.");

        var invalidPath = Path.Combine(root, "invalid-dashboard.html");
        File.WriteAllText(invalidPath, "<html>No dashboard state</html>");
        Check(RunCli(repo, root, "--workspace", workspace, "project", "add", invalidPath).ExitCode != 0, "Invalid dashboard HTML must be rejected before registration.");
        Check(!Directory.Exists(directory) && File.ReadAllText(invalidPath) == "<html>No dashboard state</html>", "Invalid project add must not create registry data or edit its source.");

        var dashboard = Path.Combine(root, "dashboard.html");
        File.WriteAllText(dashboard, MetisTests.Html(MetisTests.Fixture()));
        var sourceBytes = File.ReadAllBytes(dashboard);
        Check(RunCli(repo, root, "--workspace", workspace, "project", "add", "dashboard.html").ExitCode != 0 && !Directory.Exists(directory), "Project add requires an absolute source path.");
        var added = RunCli(repo, root, "--workspace", workspace, "project", "add", dashboard);
        Check(added.ExitCode == 0, "Valid dashboard registration failed: " + added.Output);
        using var registration = JsonDocument.Parse(added.Output);
        var id = registration.RootElement.GetProperty("id").GetString()!;
        Check(Guid.TryParseExact(id, "N", out _) && registration.RootElement.GetProperty("path").GetString() == dashboard, "Project add must print the registration JSON.");
        Check(File.Exists(registryPath) && !File.Exists(workspace), "Project registration belongs beside the explicit workspace and does not create a workspace.");
        var repeated = RunCli(repo, root, "--workspace", workspace, "project", "add", dashboard);
        Check(repeated.ExitCode == 0 && JsonDocument.Parse(repeated.Output).RootElement.GetProperty("id").GetString() == id, "Repeated registration must remain idempotent.");
        list = RunCli(repo, directory, "--workspace", "workspace.json", "project", "list");
        Check(list.ExitCode == 0 && JsonDocument.Parse(list.Output).RootElement.GetArrayLength() == 1, "Relative and absolute workspace arguments must share the same explicit registry.");
        var anotherWorkspace = Path.Combine(root, "other-projects", "workspace.json");
        Check(JsonDocument.Parse(RunCli(repo, root, "--workspace", anotherWorkspace, "project", "list").Output).RootElement.GetArrayLength() == 0 &&
            !Directory.Exists(Path.GetDirectoryName(anotherWorkspace)), "An unrelated workspace must remain isolated and uncreated.");
        var registryBytes = File.ReadAllBytes(registryPath);
        Check(RunCli(repo, root, "--workspace", workspace, "project", "remove", Guid.NewGuid().ToString("N")).ExitCode != 0 &&
            registryBytes.SequenceEqual(File.ReadAllBytes(registryPath)), "Missing removal must preserve an existing registry.");
        Check(RunCli(repo, root, "--workspace", workspace, "project", "remove", id).ExitCode == 0, "Registered project removal failed.");
        Check(new ProjectRegistryStore(registryPath).Load().Count == 0 && sourceBytes.SequenceEqual(File.ReadAllBytes(dashboard)), "Removing a registration must leave its source dashboard byte-identical.");
    }

    private static (int ExitCode, string Output) RunCli(string repo, string workingDirectory, params string[] arguments)
    {
        var info = new ProcessStartInfo("dotnet") { WorkingDirectory = workingDirectory, UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        info.ArgumentList.Add("run");
        info.ArgumentList.Add("--project");
        info.ArgumentList.Add(Path.Combine(repo, "src", "Pandora.Cli", "Pandora.Cli.csproj"));
        info.ArgumentList.Add("--no-restore");
        info.ArgumentList.Add("--");
        foreach (var argument in arguments) info.ArgumentList.Add(argument);
        using var process = Process.Start(info) ?? throw new InvalidOperationException("Could not start CLI test.");
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(30_000))
        {
            process.Kill(entireProcessTree: true);
            process.WaitForExit(5000);
            throw new TimeoutException("CLI safety fixture exceeded 30 seconds.");
        }
        return (process.ExitCode, output.GetAwaiter().GetResult() + error.GetAwaiter().GetResult());
    }

    private static string FindRepo()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "Pandora.sln"))) return directory.FullName;
        throw new InvalidOperationException("Could not locate Pandora solution for CLI tests.");
    }
    private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
