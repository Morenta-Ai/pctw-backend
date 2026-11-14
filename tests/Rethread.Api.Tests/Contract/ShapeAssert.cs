using System.Text.Json;
using Xunit.Sdk;

namespace Rethread.Api.Tests.Contract;

/// <summary>
/// Checks a real response has the shape of a contract sample: the same keys at every level and compatible
/// JSON types. Values may differ, and any field may be <c>null</c> in the response (the samples don't say
/// which fields are nullable). A sample object <c>{ "...": "see …" }</c> matches anything.
/// </summary>
public static class ShapeAssert
{
    public static void Matches(JsonElement sample, JsonElement actual)
    {
        var problems = new List<string>();
        Compare(sample, actual, "$", problems);
        if (problems.Count > 0)
            throw new XunitException("Response doesn't match the contract:\n  " + string.Join("\n  ", problems));
    }

    public static void MatchesContract(this JsonElement actual, string contract, string endpoint) =>
        Matches(ContractSamples.Response(contract, endpoint), actual);

    private static void Compare(JsonElement sample, JsonElement actual, string path, List<string> problems)
    {
        if (actual.ValueKind == JsonValueKind.Null || sample.ValueKind == JsonValueKind.Null) return;

        switch (sample.ValueKind)
        {
            case JsonValueKind.Object:
                if (sample.TryGetProperty("...", out _)) return;
                if (actual.ValueKind != JsonValueKind.Object)
                {
                    problems.Add($"{path}: expected an object, got {actual.ValueKind}");
                    return;
                }

                var expectedKeys = sample.EnumerateObject().Select(p => p.Name).ToHashSet();
                var actualKeys = actual.EnumerateObject().Select(p => p.Name).ToHashSet();
                foreach (var missing in expectedKeys.Except(actualKeys)) problems.Add($"{path}.{missing}: missing");
                foreach (var extra in actualKeys.Except(expectedKeys)) problems.Add($"{path}.{extra}: not in the contract");
                foreach (var key in expectedKeys.Intersect(actualKeys))
                    Compare(sample.GetProperty(key), actual.GetProperty(key), $"{path}.{key}", problems);
                break;

            case JsonValueKind.Array:
                if (actual.ValueKind != JsonValueKind.Array)
                {
                    problems.Add($"{path}: expected an array, got {actual.ValueKind}");
                    return;
                }

                if (sample.GetArrayLength() == 0) return;
                var index = 0;
                foreach (var item in actual.EnumerateArray())
                    Compare(sample[0], item, $"{path}[{index++}]", problems);
                break;

            case JsonValueKind.True or JsonValueKind.False:
                if (actual.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                    problems.Add($"{path}: expected a boolean, got {actual.ValueKind}");
                break;

            default:
                if (actual.ValueKind != sample.ValueKind)
                    problems.Add($"{path}: expected {sample.ValueKind}, got {actual.ValueKind}");
                break;
        }
    }
}
