namespace Pandora.Core;

public static class PathExpander
{
    public static string Expand(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        var expanded = Environment.ExpandEnvironmentVariables(path.Trim());
        if (expanded.StartsWith("~/", StringComparison.Ordinal) ||
            expanded.StartsWith("~\\", StringComparison.Ordinal))
        {
            expanded = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                expanded[2..]);
        }

        try
        {
            return Path.GetFullPath(expanded);
        }
        catch
        {
            return expanded;
        }
    }

    public static string CompressUserPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var fullPath = Expand(path);
        if (!string.IsNullOrWhiteSpace(userProfile) &&
            fullPath.StartsWith(userProfile, StringComparison.OrdinalIgnoreCase))
        {
            return "%USERPROFILE%" + fullPath[userProfile.Length..];
        }

        return fullPath;
    }
}
