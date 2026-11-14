using System.Globalization;
using Rethread.Api.Data.Entities;

namespace Rethread.Api.Domain;

/// <summary>
/// Clothing sizes from body measurements, for family members. The user app shows them and they guide the
/// shop's size picker, so the server owns the table.
/// </summary>
public static class SizeGuide
{
    public static string? TopSize(FamilyMember m) => m.AgeGroup is AgeGroup.Toddler or AgeGroup.Child
        ? KidsSize(m.HeightCm)
        : m.ChestCm is not { } chest ? null
        : m.Gender == Gender.Female ? Band(chest, (80, "XS"), (84, "S"), (90, "M"), (96, "L"), (102, "XL"))
        : Band(chest, (86, "XS"), (92, "S"), (98, "M"), (104, "L"), (110, "XL"));

    public static string? BottomSize(FamilyMember m) => m.AgeGroup is AgeGroup.Toddler or AgeGroup.Child
        ? KidsSize(m.HeightCm)
        : m.WaistCm is not { } waist ? null
        // Waist in inches, as Sri Lankan trousers are sized.
        : ((int)Math.Round(waist / 2.54m, MidpointRounding.AwayFromZero)).ToString(CultureInfo.InvariantCulture);

    private static string? KidsSize(decimal? heightCm) => heightCm is not { } h ? null
        : h < 98 ? "2-3Y" : h < 110 ? "4-5Y" : h < 122 ? "6-7Y" : h < 134 ? "8-9Y" : h < 146 ? "10-11Y" : "12-13Y";

    private static string Band(decimal value, params (decimal Below, string Size)[] bands) =>
        bands.FirstOrDefault(b => value < b.Below).Size ?? "XXL";
}
