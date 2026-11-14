using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Rethread.Api.Tests.Contract;

/// <summary>
/// Reads the JSON samples from <c>docs/contracts/*.md</c>, the copies of the clients' API contracts, so tests
/// can check real responses against the documented shapes.
/// </summary>
public static partial class ContractSamples
{
    public sealed record Sample(string Label, JsonElement Json);

    private static readonly Dictionary<string, string[]> Cache = [];

    /// <summary>The response sample for an endpoint heading such as <c>GET /pickups/{id}</c>.</summary>
    public static JsonElement Response(string contract, string endpoint)
    {
        var samples = Section(contract, endpoint);
        var isGet = endpoint.StartsWith("GET ", StringComparison.Ordinal);
        var match = samples.FirstOrDefault(s => s.Label.Length > 0 && char.IsDigit(s.Label[0]))
            ?? samples.FirstOrDefault(s => s.Label == "response")
            ?? (isGet ? samples.FirstOrDefault(s => s.Label == "") : null);
        return match?.Json ?? throw new InvalidOperationException(
            $"No response sample for {endpoint} in {contract}.md.");
    }

    /// <summary>The request sample for an endpoint (the block or segment marked <c>// request</c>).</summary>
    public static JsonElement Request(string contract, string endpoint)
    {
        var samples = Section(contract, endpoint);
        var match = samples.FirstOrDefault(s => s.Label == "request")
            ?? samples.FirstOrDefault(s => s.Label == "");
        return match?.Json ?? throw new InvalidOperationException(
            $"No request sample for {endpoint} in {contract}.md.");
    }

    /// <summary>Every JSON sample under a heading (an endpoint, or a section like <c>Catalogue (Admin)</c>).</summary>
    public static IReadOnlyList<Sample> Section(string contract, string heading)
    {
        var lines = Lines(contract);
        var start = Array.FindIndex(lines, line => IsHeading(line, heading));
        if (start < 0) throw new InvalidOperationException($"No heading '{heading}' in {contract}.md.");
        var level = lines[start].TakeWhile(c => c == '#').Count();

        var samples = new List<Sample>();
        for (var i = start + 1; i < lines.Length; i++)
        {
            var hashes = lines[i].TakeWhile(c => c == '#').Count();
            if (hashes > 0 && hashes <= level && lines[i].Length > hashes && lines[i][hashes] == ' ') break;
            if (!lines[i].StartsWith("```json", StringComparison.Ordinal)) continue;

            var prose = PreviousProse(lines, i);
            var block = new List<string>();
            for (i++; i < lines.Length && !lines[i].StartsWith("```", StringComparison.Ordinal); i++)
                block.Add(lines[i]);
            samples.AddRange(Split(block, prose.Contains("Response", StringComparison.Ordinal) ? "response" : ""));
        }

        return samples;
    }

    /// <summary>All JSON samples in a contract, for checking the documents themselves parse.</summary>
    public static IEnumerable<(string Heading, Sample Sample)> All(string contract)
    {
        string heading = "";
        var lines = Lines(contract);
        for (var i = 0; i < lines.Length; i++)
        {
            if (lines[i].StartsWith('#')) heading = lines[i];
            if (!lines[i].StartsWith("```json", StringComparison.Ordinal)) continue;
            var block = new List<string>();
            for (i++; i < lines.Length && !lines[i].StartsWith("```", StringComparison.Ordinal); i++)
                block.Add(lines[i]);
            foreach (var sample in Split(block, "")) yield return (heading, sample);
        }
    }

    private static bool IsHeading(string line, string heading)
    {
        if (!line.StartsWith('#')) return false;
        var text = line.TrimStart('#').Trim();
        if (text == heading) return true;
        var code = CodeSpan().Match(text);
        if (!code.Success) return false;
        var endpoint = code.Groups[1].Value;
        return endpoint == heading || endpoint.StartsWith(heading + "?", StringComparison.Ordinal);
    }

    private static string PreviousProse(string[] lines, int index)
    {
        for (var i = index - 1; i >= 0 && i >= index - 3; i--)
            if (lines[i].Trim().Length > 0) return lines[i];
        return "";
    }

    /// <summary>Splits a block on <c>// label</c> comment lines; each part is one JSON value.</summary>
    private static IEnumerable<Sample> Split(List<string> block, string defaultLabel)
    {
        var label = defaultLabel;
        var buffer = new StringBuilder();
        foreach (var line in block)
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith("//", StringComparison.Ordinal))
            {
                if (buffer.Length > 0) yield return Parse(label, buffer);
                buffer.Clear();
                label = trimmed[2..].Trim();
                continue;
            }

            buffer.AppendLine(line);
        }

        if (buffer.ToString().Trim().Length > 0) yield return Parse(label, buffer);
    }

    private static Sample Parse(string label, StringBuilder buffer)
    {
        var text = buffer.ToString();
        try
        {
            return new Sample(label, JsonDocument.Parse(text).RootElement.Clone());
        }
        catch (JsonException e)
        {
            throw new InvalidOperationException($"Contract sample '{label}' isn't valid JSON: {e.Message}\n{text}");
        }
    }

    private static string[] Lines(string contract)
    {
        lock (Cache)
        {
            if (Cache.TryGetValue(contract, out var cached)) return cached;
            var path = Path.Combine(AppContext.BaseDirectory, "contracts", $"{contract}.md");
            return Cache[contract] = File.ReadAllLines(path);
        }
    }

    [GeneratedRegex("^`([^`]+)`")]
    private static partial Regex CodeSpan();
}
