namespace Pandora.Core;

/// <summary>One per-user location shared by packaged tools and the unpackaged desktop app.
/// AppData is intentionally avoided: Windows can redirect it into a caller's MSIX container.</summary>
public static class UserDataPaths
{
    // Restricted launchers can run under a temporary OS account while deliberately
    // preserving the desktop user's USERPROFILE. Match our existing path-token contract.
    public static string Profile => Environment.GetEnvironmentVariable("USERPROFILE") is { Length: > 0 } profile
        ? profile : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    public static string Root => ForProfile(Profile);

    public static string ForProfile(string profileDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(profileDirectory);
        if (!Path.IsPathFullyQualified(profileDirectory))
            throw new ArgumentException("User profile must be an absolute path.", nameof(profileDirectory));
        return Path.Combine(Path.GetFullPath(profileDirectory), ".pandora");
    }

    internal static WorkspaceStore WorkspaceFor(string profileDirectory, string applicationDataDirectory) =>
        new(Path.Combine(ForProfile(profileDirectory), "workspace.json"))
        {
            MigrationRequiredFrom = Path.Combine(applicationDataDirectory, "Pandora", "workspace.json")
        };
}
