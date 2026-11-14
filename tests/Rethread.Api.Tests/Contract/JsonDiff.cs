using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Xunit.Sdk;

namespace Rethread.Api.Tests.Contract;

/// <summary>Value-by-value comparison of two JSON trees. Numbers compare as numbers (8 equals 8.00).</summary>
public static class JsonDiff
{
    /// <param name="ignore">Paths to skip, such as <c>$.collectors[*].latitude</c> (use <c>*</c> for any index).</param>
    public static void AssertSame(JsonNode? expected, JsonNode? actual, params string[] ignore)
    {
        var problems = new List<string>();
        Compare(expected, actual, "$", problems, ignore);
        if (problems.Count > 0)
            throw new XunitException($"{problems.Count} differences:\n  " + string.Join("\n  ", problems.Take(25)));
    }

    public static JsonNode ToNode(JsonElement element) => JsonNode.Parse(element.GetRawText())!;

    /// <summary>Sorts an array of objects by a key, so lists whose order isn't part of the contract compare.</summary>
    public static JsonArray SortedBy(JsonNode array, string key) =>
        new(array.AsArray().OrderBy(n => n![key]!.ToJsonString(), StringComparer.Ordinal).Select(n => n!.DeepClone()).ToArray());

    public static void Compare(JsonNode? expected, JsonNode? actual, string path, List<string> problems,
        IReadOnlyCollection<string>? ignore = null)
    {
        if (ignore is { Count: > 0 } && ignore.Any(pattern => Matches(pattern, path))) return;
        switch (expected)
        {
            case null:
                if (actual is not null) problems.Add($"{path}: expected null, got {actual.ToJsonString()}");
                return;
            case JsonObject eo:
                if (actual is not JsonObject ao) { problems.Add($"{path}: expected an object, got {actual?.ToJsonString() ?? "null"}"); return; }
                foreach (var (key, value) in eo) Compare(value, ao[key], $"{path}.{key}", problems, ignore);
                return;
            case JsonArray ea:
                if (actual is not JsonArray aa) { problems.Add($"{path}: expected an array"); return; }
                if (ea.Count != aa.Count) problems.Add($"{path}: expected {ea.Count} items, got {aa.Count}");
                for (var i = 0; i < Math.Min(ea.Count, aa.Count); i++) Compare(ea[i], aa[i], $"{path}[{i}]", problems, ignore);
                return;
            default:
                var ev = expected.AsValue();
                if (actual is null) { problems.Add($"{path}: expected {ev.ToJsonString()}, got null"); return; }
                if (ev.GetValueKind() == JsonValueKind.Number && actual.GetValueKind() == JsonValueKind.Number)
                {
                    var a = double.Parse(actual.ToJsonString(), CultureInfo.InvariantCulture);
                    var e = double.Parse(ev.ToJsonString(), CultureInfo.InvariantCulture);
                    if (Math.Abs(a - e) > 1e-9) problems.Add($"{path}: expected {e}, got {a}");
                }
                else if (ev.ToJsonString() != actual.ToJsonString())
                {
                    problems.Add($"{path}: expected {ev.ToJsonString()}, got {actual.ToJsonString()}");
                }

                return;
        }
    }

    private static bool Matches(string pattern, string path)
    {
        var regex = "^" + System.Text.RegularExpressions.Regex.Escape(pattern).Replace(@"\[\*]", @"\[\d+]", StringComparison.Ordinal) + "$";
        return System.Text.RegularExpressions.Regex.IsMatch(path, regex);
    }
}
