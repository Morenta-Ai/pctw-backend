using Rethread.Api.Data.Entities;

namespace Rethread.Api.Data.Seed;

/// <summary>Fixed seed tables, ported from the admin portal's <c>src/mock/seed/*</c>.</summary>
internal static class SeedRows
{
    public static readonly (string Code, string First, string Last, string Area, VehicleType Vehicle, string Plate,
        int CapacityKg, bool Online)[] Collectors =
        [
            ("COL-0457", "Ruwan", "Silva", "Dehiwala", VehicleType.ThreeWheeler, "WP ABC-1234", 120, true),
            ("COL-0312", "Chaminda", "Perera", "Wellawatte", VehicleType.ThreeWheeler, "WP QA-4521", 120, true),
            ("COL-0388", "Mohamed", "Rizwan", "Bambalapitiya", VehicleType.Van, "WP PH-2290", 400, true),
            ("COL-0421", "Saman", "Kumara", "Nugegoda", VehicleType.Motorbike, "WP BGT-7731", 40, true),
            ("COL-0466", "Dilshan", "Fernando", "Rajagiriya", VehicleType.ThreeWheeler, "WP AAK-5562", 120, true),
            ("COL-0473", "Tharindu", "Wickramasinghe", "Battaramulla", VehicleType.ThreeWheeler, "WP ABJ-1180", 120,
                false),
            ("COL-0480", "Nimal", "Rathnayake", "Mount Lavinia", VehicleType.Lorry, "WP LH-9034", 1000, true),
            ("COL-0502", "Priyantha", "Bandara", "Kotte", VehicleType.ThreeWheeler, "WP AAQ-3317", 120, false),
            ("COL-0515", "Lahiru", "Gunasekara", "Borella", VehicleType.Motorbike, "WP BJK-6620", 40, true),
            ("COL-0528", "Asanka", "de Silva", "Maharagama", VehicleType.ThreeWheeler, "WP ABP-2245", 120, false),
            ("COL-0533", "Kavindu", "Jayasuriya", "Kollupitiya", VehicleType.ThreeWheeler, "WP ACD-8891", 120, false),
            ("COL-0541", "Suresh", "Raj", "Wattala", VehicleType.Van, "WP PJ-4410", 400, false),
        ];

    public static readonly (string First, string Last)[] Customers =
    [
        ("Nadeesha", "Perera"), ("Mohamed", "Fazil"), ("Anjali", "Wijesinghe"), ("Roshan", "Dias"),
        ("Fathima", "Rinoza"), ("Kumudu", "Herath"), ("Thilini", "Abeysekara"), ("Ravi", "Shankar"),
        ("Sanduni", "Karunaratne"), ("Dinesh", "Mendis"), ("Hasini", "Jayawardena"), ("Pradeep", "Senanayake"),
        ("Malsha", "Gamage"), ("Arjun", "Pillai"), ("Ishani", "Ekanayake"), ("Shehan", "Weerasinghe"),
        ("Nirosha", "Samarasinghe"), ("Gayan", "Liyanage"), ("Ayesha", "Cassim"), ("Chathura", "Wanigasekara"),
    ];

    public static readonly string[] CustomerStreets =
        ["Galle Road", "Station Road", "Temple Road", "Park Avenue", "Lake Drive", "High Level Road"];

    public static readonly (string Name, FamilyRelation Relation, Gender Gender, AgeGroup Age)[] FamilyTemplates =
    [
        ("Spouse", FamilyRelation.Spouse, Gender.Male, AgeGroup.Adult),
        ("Son", FamilyRelation.Son, Gender.Male, AgeGroup.Kid),
        ("Daughter", FamilyRelation.Daughter, Gender.Female, AgeGroup.Teen),
        ("Mother", FamilyRelation.Mother, Gender.Female, AgeGroup.Adult),
    ];

    public static Staff[] Staff() =>
    [
        new()
        {
            Id = "stf_1", Name = "Nadeesha Perera", Email = "admin@rethread.lk", Role = StaffRole.Admin,
            LastSignInAt = Utc("2026-11-06T03:00:00Z"), CreatedAt = Utc("2025-02-01T04:00:00Z"),
        },
        new()
        {
            Id = "stf_2", Name = "Kasun Jayawardena", Email = "callcentre@rethread.lk", Role = StaffRole.CallCentre,
            LastSignInAt = Utc("2026-11-06T02:40:00Z"), CreatedAt = Utc("2025-02-01T04:00:00Z"),
        },
        new()
        {
            Id = "stf_3", Name = "Ishara Fernando", Email = "ishara@rethread.lk", Role = StaffRole.CallCentre,
            Status = AccountStatus.Suspended, LastSignInAt = Utc("2026-09-12T08:10:00Z"),
            CreatedAt = Utc("2025-06-01T04:00:00Z"),
        },
    ];

    /// <summary>Sign-in passwords for the seeded staff (documented in the README; Development only).</summary>
    public static readonly Dictionary<string, string> StaffPasswords = new()
    {
        ["admin@rethread.lk"] = "rethread-admin",
        ["callcentre@rethread.lk"] = "rethread-callcentre",
        ["ishara@rethread.lk"] = "rethread-callcentre",
    };

    public static Hub[] Hubs() =>
    [
        new() { Id = "hub_plg", Code = "PLG", Name = "Central Hub — Peliyagoda", ClosesAt = "18:00", Latitude = 6.9622, Longitude = 79.8829 },
        new() { Id = "hub_mrt", Code = "MRT", Name = "South Hub — Moratuwa", ClosesAt = "17:30", Latitude = 6.7733, Longitude = 79.8826 },
    ];

    public static Vendor[] Vendors() =>
    [
        new() { Id = "vnd_nugegoda", Name = "ReThread Point – Nugegoda", Address = "88 High Level Rd, Nugegoda", Latitude = 6.8649, Longitude = 79.8997 },
        new() { Id = "vnd_kollupitiya", Name = "ReThread Point – Kollupitiya", Address = "21 Duplication Rd, Colombo 03", Latitude = 6.91, Longitude = 79.85 },
    ];

    public static GarmentType[] GarmentTypes() =>
    [
        new() { Id = "tShirt", Name = "T-shirt", Icon = "tShirt", SizeBasis = "chest", EstimatedKg = 0.2m, SortOrder = 1 },
        new() { Id = "shirt", Name = "Shirt", Icon = "shirt", SizeBasis = "chest", EstimatedKg = 0.25m, SortOrder = 2 },
        new() { Id = "trousers", Name = "Trousers", Icon = "trousers", SizeBasis = "waist", EstimatedKg = 0.5m, SortOrder = 3 },
        new() { Id = "saree", Name = "Saree", Icon = "saree", SizeBasis = "none", EstimatedKg = 0.6m, SortOrder = 4 },
        new() { Id = "dress", Name = "Dress", Icon = "dress", SizeBasis = "chest", EstimatedKg = 0.4m, SortOrder = 5 },
        new() { Id = "kidsWear", Name = "Kids wear", Icon = "kidsWear", SizeBasis = "height", EstimatedKg = 0.15m, SortOrder = 6 },
        new() { Id = "bedsheet", Name = "Bedsheet", Icon = "bedsheet", SizeBasis = "none", EstimatedKg = 0.9m, SortOrder = 7 },
        new() { Id = "other", Name = "Other", Icon = "other", SizeBasis = "none", EstimatedKg = 0.3m, SortOrder = 8 },
    ];

    public static VoucherBrand[] VoucherBrands() =>
    [
        new() { Id = "vb_lanka_fresh", Name = "Lanka Fresh Supermarkets", Description = "Groceries & household essentials", CodePrefix = "LF", ValuesCents = [50000, 100000, 200000], SortOrder = 1 },
        new() { Id = "vb_ceylon_book", Name = "Ceylon Book Hub", Description = "Books, stationery & school supplies", CodePrefix = "CB", ValuesCents = [50000, 100000, 200000], SortOrder = 2 },
        new() { Id = "vb_galle_face", Name = "Galle Face Cinemas", Description = "Movie tickets & snacks", CodePrefix = "GF", ValuesCents = [50000, 100000, 200000], SortOrder = 3 },
        new() { Id = "vb_island_care", Name = "Island Care Pharmacy", Description = "Health & personal care", CodePrefix = "IC", ValuesCents = [50000, 100000, 200000], SortOrder = 4 },
        new() { Id = "vb_odel", Name = "Odel", Description = "Fashion & lifestyle", CodePrefix = "OD", ValuesCents = [50000, 100000, 250000], SortOrder = 5 },
    ];

    public static RateSet[] RateSets() =>
    [
        new() { Id = "rate_1", CustomerRatePerKgCents = 10_000, CollectorRatePerKgCents = 3_500, HomeDeliveryChargeCents = 30_000, ClickAndCollectChargeCents = 20_000, EffectiveFrom = new DateOnly(2025, 3, 1), CreatedAt = Utc("2025-02-20T04:00:00Z"), CreatedBy = "Nadeesha Perera" },
        new() { Id = "rate_2", CustomerRatePerKgCents = 11_000, CollectorRatePerKgCents = 3_800, HomeDeliveryChargeCents = 35_000, ClickAndCollectChargeCents = 25_000, EffectiveFrom = new DateOnly(2025, 11, 1), CreatedAt = Utc("2025-10-18T05:30:00Z"), CreatedBy = "Nadeesha Perera" },
        new() { Id = "rate_3", CustomerRatePerKgCents = 12_000, CollectorRatePerKgCents = 4_000, HomeDeliveryChargeCents = 35_000, ClickAndCollectChargeCents = 25_000, EffectiveFrom = new DateOnly(2026, 10, 1), CreatedAt = Utc("2026-09-24T08:15:00Z"), CreatedBy = "Nadeesha Perera" },
    ];

    public static ImpactMultiplier[] Multipliers()
    {
        var at = Utc("2026-09-24T08:20:00Z");
        (string Key, string Unit, decimal PerKg)[] rows =
        [
            ("ghgSaved", "kgCo2e", 3.5m), ("resourcesSaved", "kg", 7m), ("landfillCostSaved", "lkrCents", 2500m),
            ("forexSaved", "usdCents", 80m), ("jobsGenerated", "workDays", 0.27m),
            ("entrepreneursCreated", "microBusinesses", 0.02m), ("garmentsDonated", "garments", 0.72m),
            ("donationValue", "lkrCents", 2400m),
        ];
        return rows.Select((r, i) => new ImpactMultiplier
        {
            Key = r.Key,
            Unit = r.Unit,
            PerKg = r.PerKg,
            SortOrder = i + 1,
            UpdatedAt = at,
            UpdatedBy = "Nadeesha Perera",
        }).ToArray();
    }

    /// <summary>Projects with what they'd raised before the app; seeded donations add to it.</summary>
    public static (DonationProject Project, long BaseRaised, int BaseDonors)[] DonationProjects() =>
    [
        (new() { Id = "dp_monaragala", Name = "School uniforms for Monaragala kids", Summary = "Education · 420 children", GoalCents = 50_000_000, Active = true, EndsOn = new DateOnly(2026, 12, 31), CreatedAt = Utc("2026-08-01T04:00:00Z") }, 33_950_000, 811),
        (new() { Id = "dp_jaffna_library", Name = "Reading corner for Jaffna library", Summary = "Education · 3 schools", GoalCents = 20_000_000, Active = true, CreatedAt = Utc("2026-09-15T04:00:00Z") }, 8_400_000, 230),
        (new() { Id = "dp_flood_relief", Name = "Kelani flood relief clothing", Summary = "Relief · 600 families", GoalCents = 30_000_000, Active = false, EndsOn = new DateOnly(2026, 7, 31), CreatedAt = Utc("2026-06-05T04:00:00Z") }, 30_000_000, 1_204),
    ];

    public static readonly (string Name, ProductCategory Category, long Rupees, string[] Sizes, int Stock, string Story)[]
        Products =
        [
            ("Patchwork Denim Jacket", ProductCategory.Women, 3450, ["XS", "S", "M", "L", "XL"], 6, "Cut from 4 pairs of collected jeans, re-stitched in Moratuwa."),
            ("Batik Panel Shirt", ProductCategory.Men, 2200, ["S", "M", "L", "XL"], 9, "Old batik sarongs given a second life as shirt panels."),
            ("Saree Silk Scarf", ProductCategory.Accessories, 950, ["One size"], 24, "Hemmed from the borders of donated silk sarees."),
            ("Reworked Linen Trousers", ProductCategory.Men, 2800, ["30", "32", "34", "36"], 5, "Linen trousers re-tailored with new waistbands."),
            ("Kids' Upcycled Dungarees", ProductCategory.Kids, 1650, ["2-3Y", "4-5Y", "6-7Y"], 11, "Made from adult denim offcuts."),
            ("Wrap Dress — Sunset Print", ProductCategory.Women, 2950, ["S", "M", "L"], 4, "Two cotton dresses combined into one wrap."),
            ("Tote Bag — Denim Mix", ProductCategory.Accessories, 750, ["One size"], 30, "Leftover denim and canvas, lined with shirt cotton."),
            ("Kids' T-shirt Bundle (3)", ProductCategory.Kids, 1200, ["4-5Y", "6-7Y", "8-9Y"], 14, "Sorted, washed and repaired tees."),
            ("Oversized Flannel Shirt", ProductCategory.Women, 2400, ["S", "M", "L"], 0, "Brushed flannel with mended elbows."),
            ("Denim Bucket Hat", ProductCategory.Accessories, 890, ["One size"], 18, "From jean legs too worn for trousers."),
        ];

    public static readonly (string Bank, string Branch)[] Banks =
        [("Commercial Bank", "Kollupitiya"), ("Bank of Ceylon", "Wellawatte"), ("Sampath Bank", "Nugegoda"), ("HNB", "Dehiwala")];

    public static DateTime Utc(string iso) =>
        DateTime.Parse(iso, System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal);
}
