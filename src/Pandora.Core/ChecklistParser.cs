using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Pandora.Core;

/// <summary>Simple checklist identities follow content, never the row number of a different task.</summary>
public static class ChecklistParser
{
    public static List<AgentFeedItem> Parse(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return [];
        if (Encoding.UTF8.GetByteCount(text) > AgentFeedStore.MaxFeedFileBytes)
            throw new InvalidOperationException("Checklist input exceeds the feed size limit.");

        var trimmed = text.TrimStart();
        if (trimmed.StartsWith('[') || trimmed.StartsWith('{'))
        {
            // An intended JSON document that is malformed must not silently become task text.
            using var document = JsonDocument.Parse(text);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
                throw new InvalidOperationException("Checklist JSON must be an array of strings or item objects.");
            if (document.RootElement.EnumerateArray().All(item => item.ValueKind == JsonValueKind.String))
                return FromText(document.RootElement.EnumerateArray().Select(item => item.GetString()!));
            if (document.RootElement.EnumerateArray().Any(item => item.ValueKind != JsonValueKind.Object))
                throw new InvalidOperationException("Checklist JSON must contain only strings or only item objects.");
            var options = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
            };
            return JsonSerializer.Deserialize<List<AgentFeedItem>>(text, options) ?? [];
        }

        return FromText(text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim().TrimStart('-', '*', ' ')));
    }

    private static List<AgentFeedItem> FromText(IEnumerable<string> lines)
    {
        var occurrences = new Dictionary<string, int>(StringComparer.Ordinal);
        var result = new List<AgentFeedItem>();
        foreach (var line in lines)
        {
            var text = line.Trim();
            if (text.Length == 0) continue;
            var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
            var occurrence = occurrences.GetValueOrDefault(hash) + 1;
            occurrences[hash] = occurrence;
            result.Add(new AgentFeedItem { Id = $"text-{hash}-{occurrence}", Text = text, Priority = AgentFeedPriority.P2 });
        }
        return result;
    }
}
