using Pandora.Core;

internal static class UserDataPathTests
{
    public static void Run()
    {
        var inheritedProfile = Environment.GetEnvironmentVariable("USERPROFILE");
        if (!string.IsNullOrWhiteSpace(inheritedProfile) && UserDataPaths.Root != Path.Combine(inheritedProfile, ".pandora"))
            throw new Exception("Tools must honor the inherited desktop profile, not a temporary sandbox account.");
        try { UserDataPaths.ForProfile("relative-profile"); throw new Exception("Relative profiles must be rejected."); }
        catch (ArgumentException) { }
        var root = Path.Combine(Path.GetTempPath(), "Pandora.Tests", Guid.NewGuid().ToString("N"));
        var profile = Path.Combine(root, "profile");
        var ordinary = UserDataPaths.WorkspaceFor(profile, Path.Combine(root, "real-roaming"));
        var packaged = UserDataPaths.WorkspaceFor(profile, Path.Combine(root, "package-roaming"));
        if (ordinary.WorkspacePath != packaged.WorkspacePath || Directory.Exists(root))
            throw new Exception("Path resolution must converge without any filesystem writes.");
        Directory.CreateDirectory(Path.GetDirectoryName(ordinary.MigrationRequiredFrom!)!);
        File.WriteAllText(ordinary.MigrationRequiredFrom!, "existing user data");
        try { ordinary.LoadOrCreate(); throw new Exception("Missing migration must not create a blank workspace."); }
        catch (WorkspaceValidationException) { }
        if (Directory.Exists(UserDataPaths.ForProfile(profile)))
            throw new Exception("Migration guard must run before lock/directory creation.");
        // Explicit fixture paths remain independent of the current user's legacy data.
        new WorkspaceStore(ordinary.WorkspacePath).LoadOrCreate();
        if (ordinary.LoadReadOnly().Zones.Count != packaged.LoadReadOnly().Zones.Count)
            throw new Exception("Packaged and ordinary readers must see the same snapshot.");
        if (AgentFeedStore.ForCurrentUser().FeedsDirectory != Path.Combine(UserDataPaths.Root, "AgentFeeds"))
            throw new Exception("Feeds must share the common data root.");
    }
}
