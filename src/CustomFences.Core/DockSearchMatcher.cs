namespace CustomFences.Core;

public static class DockSearchMatcher
{
    public static bool Matches(string displayName, string extension, string path, string query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return true;
        }

        var trimmed = query.Trim();
        return displayName.Contains(trimmed, StringComparison.OrdinalIgnoreCase) ||
               extension.Contains(trimmed, StringComparison.OrdinalIgnoreCase) ||
               path.Contains(trimmed, StringComparison.OrdinalIgnoreCase);
    }
}
