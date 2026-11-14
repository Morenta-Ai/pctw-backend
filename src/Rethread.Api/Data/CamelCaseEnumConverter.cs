using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Rethread.Api.Data;

/// <summary>Stores enums as the same camelCase strings the API sends (<c>enRoute</c>, <c>callCentre</c>).</summary>
public sealed class CamelCaseEnumConverter<T>() : ValueConverter<T, string>(
    value => ToText(value),
    text => FromText(text)) where T : struct, Enum
{
    private static readonly ConcurrentDictionary<T, string> Names = new();
    private static readonly Dictionary<string, T> Values = Enum.GetValues<T>()
        .ToDictionary(v => JsonNamingPolicy.CamelCase.ConvertName(v.ToString()), v => v);

    public static string ToText(T value) =>
        Names.GetOrAdd(value, v => JsonNamingPolicy.CamelCase.ConvertName(v.ToString()));

    public static T FromText(string text) => Values[text];

    /// <summary>Parses a query or form value such as <c>afternoon</c>; null when it isn't one of the values.</summary>
    public static T? TryParse(string? text) => text is not null && Values.TryGetValue(text, out var value) ? value : null;
}
