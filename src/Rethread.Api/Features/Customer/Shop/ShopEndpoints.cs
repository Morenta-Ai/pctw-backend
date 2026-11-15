using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Rethread.Api.Common;
using Rethread.Api.Data;
using Rethread.Api.Data.Entities;

namespace Rethread.Api.Features.Customer.Shop;

public sealed record ShopCategory(string Id, string Name);

public sealed record ShopCategories(IReadOnlyList<ShopCategory> Items, IReadOnlyList<string> Sizes, long MaxPriceCents,
    int NewTodayCount);

/// <summary>A piece in the shop list. Rejuvenated garments are one of a kind, so it has one size.</summary>
public sealed record ShopProduct(
    string Id,
    string Name,
    string CategoryId,
    string Size,
    long PriceCents,
    string ConditionGrade,
    string Gender,
    int Stock,
    bool IsNewToday,
    string? ImageUrl)
{
    public static ShopProduct From(Product p, DateOnly today) => new(p.Id, p.Name, CamelCaseEnumConverter<ProductCategory>.ToText(p.Category),
        p.DefaultSize, p.PriceCents, p.ConditionGrade, p.GenderOrDefault, Math.Max(0, p.Stock), Colombo.DateOf(p.CreatedAt) == today,
        p.ImageUrls.FirstOrDefault());
}

public sealed record ShopProductDetail(
    string Id,
    string Name,
    string CategoryId,
    string Size,
    long PriceCents,
    string ConditionGrade,
    string Gender,
    int Stock,
    bool IsNewToday,
    IReadOnlyList<string> ImageUrls,
    IReadOnlyList<ShopProduct> Related,
    DateTime UploadedAt,
    string? Description,
    string? Material,
    ShopProductDetail.Garment? Measurements,
    string? RejuvenatedBy,
    decimal? ImpactKg)
{
    public sealed record Garment(int? ChestCm, int? LengthCm, int? WaistCm);
}

public sealed record ProductPage(IReadOnlyList<ShopProduct> Items, int Page, int PageSize, bool HasMore, int TotalCount);

public sealed record FranchiseVendor(string Id, string Name, string Address, double Lat, double Lng, string OpeningHours,
    string? Phone, double? DistanceKm);

/// <summary>The shop: published pieces, sold-out ones included so the app can show them as sold out.</summary>
public static class ShopEndpoints
{
    public static void MapShop(this RouteGroupBuilder api)
    {
        var shop = api.MapGroup("/shop");
        shop.MapGet("/categories", Categories);
        shop.MapGet("/products", List);
        shop.MapGet("/products/{id}", Get);
        shop.MapGet("/franchise-vendors", Vendors);
    }

    private static readonly Dictionary<ProductCategory, string> Names = new()
    {
        [ProductCategory.Women] = "Women",
        [ProductCategory.Men] = "Men",
        [ProductCategory.Kids] = "Kids",
        [ProductCategory.Accessories] = "Accessories",
    };

    private static async Task<Ok<ShopCategories>> Categories(AppDbContext db, TimeProvider time, CancellationToken ct)
    {
        var products = await db.Products.AsNoTracking().Where(p => p.Published).ToListAsync(ct);
        var today = time.Today();
        return TypedResults.Ok(new ShopCategories(
            Names.Select(n => new ShopCategory(CamelCaseEnumConverter<ProductCategory>.ToText(n.Key), n.Value)).ToList(),
            products.SelectMany(p => p.Sizes).Distinct().ToList(),
            products.Count == 0 ? 0 : products.Max(p => p.PriceCents),
            products.Count(p => Colombo.DateOf(p.CreatedAt) == today)));
    }

    private static async Task<Ok<ProductPage>> List(string? search, string? category, string? sizes, long? minPriceCents,
        long? maxPriceCents, string? condition, string? gender, string? sort, bool? newToday, int? page, int? pageSize,
        AppDbContext db, TimeProvider time, CancellationToken ct)
    {
        var today = time.Today();
        IEnumerable<Product> products = await db.Products.AsNoTracking().Where(p => p.Published).ToListAsync(ct);
        if (!string.IsNullOrWhiteSpace(search))
            products = products.Where(p => p.Name.Contains(search.Trim(), StringComparison.OrdinalIgnoreCase));
        if (category is not null)
        {
            var c = CamelCaseEnumConverter<ProductCategory>.TryParse(category)
                    ?? throw ApiException.Validation("category", "That isn't a category.");
            products = products.Where(p => p.Category == c);
        }

        if (!string.IsNullOrWhiteSpace(sizes))
        {
            var wanted = sizes.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            products = products.Where(p => p.Sizes.Intersect(wanted).Any());
        }

        if (minPriceCents is { } min) products = products.Where(p => p.PriceCents >= min);
        if (maxPriceCents is { } max) products = products.Where(p => p.PriceCents <= max);
        if (!string.IsNullOrWhiteSpace(condition))
        {
            var grades = condition.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            products = products.Where(p => grades.Contains(p.ConditionGrade));
        }

        if (!string.IsNullOrWhiteSpace(gender))
            products = products.Where(p => p.GenderOrDefault == gender || (gender is "women" or "men" && p.GenderOrDefault == "unisex"));
        if (newToday == true) products = products.Where(p => Colombo.DateOf(p.CreatedAt) == today);

        products = (sort ?? "newest") switch
        {
            "newest" => products.OrderByDescending(p => p.CreatedAt).ThenBy(p => p.Id),
            "priceAsc" => products.OrderBy(p => p.PriceCents).ThenBy(p => p.Id),
            "priceDesc" => products.OrderByDescending(p => p.PriceCents).ThenBy(p => p.Id),
            _ => throw ApiException.Validation("sort", "Use newest, priceAsc or priceDesc."),
        };
        var all = products.ToList();
        var paging = PageRequest.From(page, pageSize ?? 20);
        return TypedResults.Ok(new ProductPage(all.Skip(paging.Skip).Take(paging.PageSize).Select(p => ShopProduct.From(p, today)).ToList(),
            paging.Page, paging.PageSize, paging.Skip + paging.PageSize < all.Count, all.Count));
    }

    private static async Task<Ok<ShopProductDetail>> Get(string id, AppDbContext db, TimeProvider time, CancellationToken ct)
    {
        var p = await db.Products.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id && x.Published, ct)
                ?? throw ApiException.NotFound("PRODUCT_NOT_FOUND", "Product not found.");
        var today = time.Today();
        var related = await db.Products.AsNoTracking()
            .Where(x => x.Published && x.Stock > 0 && x.Category == p.Category && x.Id != p.Id)
            .OrderByDescending(x => x.CreatedAt).Take(6).ToListAsync(ct);
        return TypedResults.Ok(new ShopProductDetail(p.Id, p.Name, CamelCaseEnumConverter<ProductCategory>.ToText(p.Category),
            p.DefaultSize, p.PriceCents, p.ConditionGrade, p.GenderOrDefault, Math.Max(0, p.Stock),
            Colombo.DateOf(p.CreatedAt) == today, p.ImageUrls, related.Select(r => ShopProduct.From(r, today)).ToList(),
            p.CreatedAt, string.IsNullOrEmpty(p.Story) ? null : p.Story, p.Material,
            p.ChestCm is null && p.LengthCm is null && p.WaistCm is null ? null : new ShopProductDetail.Garment(p.ChestCm, p.LengthCm, p.WaistCm),
            p.RejuvenatedBy, p.ImpactKg));
    }

    /// <summary>Click &amp; collect points taking collections, nearest first when the app sends a position.</summary>
    private static async Task<Ok<ItemList<FranchiseVendor>>> Vendors(double? lat, double? lng, AppDbContext db,
        CancellationToken ct)
    {
        var vendors = await db.Vendors.AsNoTracking().Where(v => v.Active).ToListAsync(ct);
        var items = vendors.Select(v => new FranchiseVendor(v.Id, v.Name, v.Address, v.Latitude, v.Longitude, v.OpeningHours,
            v.Phone, lat is { } a && lng is { } b ? Geo.DistanceKm(a, b, v.Latitude, v.Longitude) : null));
        return TypedResults.Ok(new ItemList<FranchiseVendor>((lat is null ? items.OrderBy(v => v.Name) : items.OrderBy(v => v.DistanceKm)).ToList()));
    }
}
