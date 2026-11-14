using System.Globalization;
using System.Text;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Rethread.Api.Auth;
using Rethread.Api.Common;
using Rethread.Api.Data;
using Rethread.Api.Data.Entities;

namespace Rethread.Api.Features.Customer.Shop;

public sealed record ShopProduct(
    string Id,
    string Name,
    ProductCategory Category,
    long PriceCents,
    string DefaultSize,
    IReadOnlyList<string> Sizes,
    string Story,
    IReadOnlyList<string> ImageUrls,
    bool IsAvailable)
{
    public static ShopProduct From(Product p) => new(p.Id, p.Name, p.Category, p.PriceCents,
        p.Sizes.Count == 0 ? "" : p.Sizes[p.Sizes.Count / 2], p.Sizes, p.Story, p.ImageUrls, p.Published && p.Stock > 0);
}

public sealed record FulfilmentOptions(long HomeDeliveryChargeCents, long ClickAndCollectChargeCents,
    IReadOnlyList<FulfilmentOptions.VendorOption> Vendors)
{
    public sealed record VendorOption(string Id, string Name, string Address, double DistanceKm);
}

/// <summary>The shop: published products that are in stock.</summary>
public static class ShopEndpoints
{
    private const int PageSize = 20;

    public static void MapShop(this RouteGroupBuilder api)
    {
        api.MapGet("/products", List);
        api.MapGet("/products/{id}", Get);
        api.MapGet("/fulfilment-options", Fulfilment);
    }

    private static async Task<Ok<CursorPage<ShopProduct>>> List(string? category, long? maxPriceCents, string? sort,
        string? cursor, AppDbContext db, CancellationToken ct)
    {
        var query = db.Products.AsNoTracking().Where(p => p.Published && p.Stock > 0);
        if (category is not null)
        {
            var c = CamelCaseEnumConverter<ProductCategory>.TryParse(category)
                    ?? throw ApiException.Validation("category", "Use women, men, kids or accessories.");
            query = query.Where(p => p.Category == c);
        }

        if (maxPriceCents is { } max) query = query.Where(p => p.PriceCents <= max);

        switch (sort ?? "newest")
        {
            case "newest":
                return TypedResults.Ok(await query.ToCursorPageAsync(cursor, PageSize, ShopProduct.From, ct));
            case "priceAsc" or "priceDesc":
                // Price order changes as stock sells out, so these pages use a plain offset.
                var offset = DecodeOffset(cursor);
                query = sort == "priceAsc"
                    ? query.OrderBy(p => p.PriceCents).ThenBy(p => p.Id)
                    : query.OrderByDescending(p => p.PriceCents).ThenBy(p => p.Id);
                var rows = await query.Skip(offset).Take(PageSize + 1).ToListAsync(ct);
                return TypedResults.Ok(new CursorPage<ShopProduct>(rows.Take(PageSize).Select(ShopProduct.From).ToList(),
                    rows.Count > PageSize ? EncodeOffset(offset + PageSize) : null));
            default:
                throw ApiException.Validation("sort", "Use newest, priceAsc or priceDesc.");
        }
    }

    private static async Task<Ok<ShopProduct>> Get(string id, AppDbContext db, CancellationToken ct)
    {
        var product = await db.Products.AsNoTracking().SingleOrDefaultAsync(p => p.Id == id && p.Published, ct)
                      ?? throw ApiException.NotFound("PRODUCT_NOT_FOUND", "Product not found.");
        return TypedResults.Ok(ShopProduct.From(product));
    }

    /// <summary>Charges from the rate book, and collect points by distance from the customer's default address.</summary>
    private static async Task<Ok<FulfilmentOptions>> Fulfilment(CurrentUser user, AppDbContext db, Domain.RateBook rates,
        CancellationToken ct)
    {
        var customer = await user.CustomerAsync(ct);
        var home = await db.Addresses.AsNoTracking().FirstOrDefaultAsync(a => a.CustomerId == customer.Id && a.IsDefault, ct);
        var rate = await rates.CurrentAsync(ct);
        var vendors = await db.Vendors.AsNoTracking().ToListAsync(ct);
        return TypedResults.Ok(new FulfilmentOptions(rate.HomeDeliveryChargeCents, rate.ClickAndCollectChargeCents,
            vendors.Select(v => new FulfilmentOptions.VendorOption(v.Id, v.Name, v.Address,
                    home is null ? 0 : Geo.DistanceKm(home.Latitude, home.Longitude, v.Latitude, v.Longitude)))
                .OrderBy(v => v.DistanceKm).ToList()));
    }

    private static string EncodeOffset(int offset) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes($"offset|{offset.ToString(CultureInfo.InvariantCulture)}"));

    private static int DecodeOffset(string? cursor)
    {
        if (string.IsNullOrEmpty(cursor)) return 0;
        try
        {
            var text = Encoding.UTF8.GetString(Convert.FromBase64String(cursor));
            return text.StartsWith("offset|", StringComparison.Ordinal)
                ? int.Parse(text[7..], CultureInfo.InvariantCulture)
                : throw new FormatException();
        }
        catch (FormatException)
        {
            throw ApiException.BadRequest(Cursor.InvalidCursor, "The cursor isn't valid. Start from the first page.");
        }
    }
}
