using System.Text.RegularExpressions;

namespace CustomFences.Core;

public static class RuleEngine
{
    public static IReadOnlyList<RuleDefinition> FindMatches(Workspace workspace, RuleCandidate candidate)
    {
        return workspace.Rules
            .Where(rule => rule.IsEnabled && rule.Conditions.Count > 0)
            .Where(rule => rule.Conditions.All(condition => Matches(condition, candidate)))
            .ToArray();
    }

    public static bool Matches(RuleCondition condition, RuleCandidate candidate)
    {
        var actual = condition.Field switch
        {
            RuleField.Extension => candidate.Extension,
            RuleField.FileName => candidate.FileName,
            RuleField.ParentPath => candidate.ParentDirectory,
            _ => string.Empty
        };

        var expected = condition.Value ?? string.Empty;
        return condition.Match switch
        {
            RuleMatch.Equals => string.Equals(actual, NormalizeExpected(condition, expected), StringComparison.OrdinalIgnoreCase),
            RuleMatch.Contains => actual.Contains(expected, StringComparison.OrdinalIgnoreCase),
            RuleMatch.StartsWith => actual.StartsWith(expected, StringComparison.OrdinalIgnoreCase),
            RuleMatch.EndsWith => actual.EndsWith(expected, StringComparison.OrdinalIgnoreCase),
            RuleMatch.Regex => Regex.IsMatch(actual, expected, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(250)),
            _ => false
        };
    }

    private static string NormalizeExpected(RuleCondition condition, string expected)
    {
        return condition.Field == RuleField.Extension
            ? expected.TrimStart('.')
            : expected;
    }
}
