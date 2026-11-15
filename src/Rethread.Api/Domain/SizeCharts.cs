using Rethread.Api.Data.Entities;

namespace Rethread.Api.Domain;

/// <summary>
/// Size charts for suggesting family members' sizes (<c>GET /catalog/size-charts</c>). One chart per
/// measurement and age group; a range covers <c>minCm</c> up to, not including, <c>maxCm</c>. Adults and teens
/// take letter sizes; kids take age sizes by height.
/// </summary>
public static class SizeCharts
{
    public sealed record Range(string Size, int MinCm, int MaxCm);

    public sealed record Chart(string SizeBasis, AgeGroup AgeGroup, IReadOnlyList<Range> Ranges);

    private static Chart Letters(string basis, AgeGroup group, params int[] bounds)
    {
        string[] sizes = ["XS", "S", "M", "L", "XL", "XXL"];
        var ranges = new List<Range>();
        for (var i = 0; i <= bounds.Length; i++)
            ranges.Add(new Range(sizes[i], i == 0 ? 0 : bounds[i - 1], i == bounds.Length ? 999 : bounds[i]));
        return new Chart(basis, group, ranges);
    }

    public static readonly IReadOnlyList<Chart> All =
    [
        Letters("chest", AgeGroup.Adult, 82, 88, 96, 104, 112),
        Letters("chest", AgeGroup.Teen, 72, 78, 84, 90, 96),
        Letters("waist", AgeGroup.Adult, 66, 72, 80, 88, 96),
        Letters("waist", AgeGroup.Teen, 58, 64, 70, 76, 82),
        new("height", AgeGroup.Kid,
        [
            new("2–3Y", 0, 98), new("4–5Y", 98, 110), new("6–7Y", 110, 122), new("8–9Y", 122, 134),
            new("10–11Y", 134, 146), new("12–13Y", 146, 999),
        ]),
    ];

    /// <summary>The user app's rule: the chart for the basis and age group (kids use height), then the range.</summary>
    public static string? Suggest(string sizeBasis, FamilyMember member)
    {
        if (sizeBasis == "none") return null;
        var chart = All.FirstOrDefault(c => c.SizeBasis == sizeBasis && c.AgeGroup == member.AgeGroup)
                    ?? (member.AgeGroup == AgeGroup.Kid ? All.First(c => c.SizeBasis == "height") : null);
        var cm = chart?.SizeBasis switch
        {
            "chest" => member.ChestCm,
            "waist" => member.WaistCm,
            "height" => member.HeightCm,
            _ => null,
        };
        if (chart is null || cm is null) return null;
        return chart.Ranges.FirstOrDefault(r => cm < r.MaxCm)?.Size ?? chart.Ranges[^1].Size;
    }
}
