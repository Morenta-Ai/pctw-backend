using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Rethread.Api.Auth;
using Rethread.Api.Common;
using Rethread.Api.Data;
using Rethread.Api.Data.Entities;
using Rethread.Api.Domain;

namespace Rethread.Api.Features.Admin.Catalogue;

public sealed record ProductDto(
    string Id,
    string Name,
    ProductCategory Category,
    long PriceCents,
    IReadOnlyList<string> Sizes,
    int Stock,
    bool Published,
    string Story,
    IReadOnlyList<string> ImageUrls,
    DateTime CreatedAt,
    DateTime UpdatedAt)
{
    public static ProductDto From(Product p) => new(p.Id, p.Name, p.Category, p.PriceCents, p.Sizes, p.Stock,
        p.Published, p.Story, p.ImageUrls, p.CreatedAt, p.UpdatedAt);
}

/// <summary>Create and update bodies; on update every field is optional.</summary>
public sealed record ProductRequest(
    string? Name,
    ProductCategory? Category,
    long? PriceCents,
    IReadOnlyList<string>? Sizes,
    int? Stock,
    bool? Published,
    string? Story,
    IReadOnlyList<string>? ImageUrls);

public sealed record UploadResult(string Id, string Url);

public sealed class ProductRequestValidator : AbstractValidator<ProductRequest>
{
    public ProductRequestValidator()
    {
        RuleFor(x => x.Name).Must(n => n!.Trim().Length is >= 2 and <= 80).When(x => x.Name is not null)
            .WithMessage("Use 2 to 80 characters.");
        RuleFor(x => x.PriceCents).GreaterThan(0).When(x => x.PriceCents is not null).WithMessage("Enter a price.");
        RuleFor(x => x.Sizes).Must(s => s!.Count > 0).When(x => x.Sizes is not null).WithMessage("Choose at least one size.");
        RuleFor(x => x.Stock).GreaterThanOrEqualTo(0).When(x => x.Stock is not null);
        RuleFor(x => x.Story).MaximumLength(500);
        RuleFor(x => x.ImageUrls).Must(i => i!.Count <= 5).When(x => x.ImageUrls is not null)
            .WithMessage("Up to 5 photos.");
    }
}

/// <summary>The shop's stock (Admin): rejuvenated garments, uploaded daily.</summary>
public static class CatalogueEndpoints
{
    private const string NotFoundCode = "PRODUCT_NOT_FOUND";
    private const long MaxUploadBytes = 5 * 1024 * 1024;

    public static void MapCatalogue(this RouteGroupBuilder admin)
    {
        var products = admin.MapGroup("/products").AdminOnly();
        products.MapGet("", List);
        products.MapGet("/{id}", Get);
        products.MapPost("", Create).Validate<ProductRequest>();
        products.MapPatch("/{id}", Update).Validate<ProductRequest>();
        admin.MapPost("/uploads", Upload).AdminOnly().DisableAntiforgery();
    }

    private static async Task<Ok<PagedList<ProductDto>>> List(string? category, string? status, DateOnly? uploadedOn,
        string? q, int? page, int? pageSize, AppDbContext db, CancellationToken ct)
    {
        var query = db.Products.AsNoTracking();
        if (category is not null)
        {
            var c = CamelCaseEnumConverter<ProductCategory>.TryParse(category)
                    ?? throw ApiException.Validation("category", "Use women, men, kids or accessories.");
            query = query.Where(p => p.Category == c);
        }

        query = status switch
        {
            null => query,
            "published" => query.Where(p => p.Published && p.Stock > 0),
            "hidden" => query.Where(p => !p.Published),
            "outOfStock" => query.Where(p => p.Published && p.Stock == 0),
            _ => throw ApiException.Validation("status", "Use published, hidden or outOfStock."),
        };

        if (uploadedOn is { } day)
        {
            var from = Colombo.StartOfDay(day);
            var to = Colombo.StartOfDay(day.AddDays(1));
            query = query.Where(p => p.CreatedAt >= from && p.CreatedAt < to);
        }

        if (!string.IsNullOrWhiteSpace(q)) query = query.Where(p => EF.Functions.ILike(p.Name, $"%{q.Trim()}%"));

        query = query.OrderByDescending(p => p.CreatedAt).ThenByDescending(p => p.Id);
        return TypedResults.Ok(await query.ToPageAsync(PageRequest.From(page, pageSize), ProductDto.From, ct));
    }

    private static async Task<Ok<ProductDto>> Get(string id, AppDbContext db, CancellationToken ct) =>
        TypedResults.Ok(ProductDto.From(await FindAsync(db, id, ct)));

    private static async Task<Created<ProductDto>> Create(ProductRequest request, AppDbContext db, TimeProvider time,
        CancellationToken ct)
    {
        var missing = new Dictionary<string, string>();
        if (request.Name is null) missing["name"] = "Enter a name.";
        if (request.Category is null) missing["category"] = "Choose a category.";
        if (request.PriceCents is null) missing["priceCents"] = "Enter a price.";
        if (request.Sizes is null) missing["sizes"] = "Choose at least one size.";
        if (missing.Count > 0) throw ApiException.Validation(missing);

        var now = time.UtcNow();
        var product = new Product
        {
            Id = Ids.New("prd"),
            Name = request.Name!.Trim(),
            Category = request.Category!.Value,
            PriceCents = request.PriceCents!.Value,
            Sizes = [.. request.Sizes!.Select(s => s.Trim()).Distinct()],
            Stock = request.Stock ?? 0,
            Published = request.Published ?? true,
            Story = request.Story?.Trim() ?? "",
            ImageUrls = [.. request.ImageUrls ?? []],
            CreatedAt = now,
            UpdatedAt = now,
        };
        db.Products.Add(product);
        await db.SaveChangesAsync(ct);
        return TypedResults.Created($"/api/v1/admin/products/{product.Id}", ProductDto.From(product));
    }

    private static async Task<Ok<ProductDto>> Update(string id, ProductRequest request, AppDbContext db,
        TimeProvider time, CancellationToken ct)
    {
        var product = await FindAsync(db, id, ct);
        if (request.Name is not null) product.Name = request.Name.Trim();
        if (request.Category is { } category) product.Category = category;
        if (request.PriceCents is { } price) product.PriceCents = price;
        if (request.Sizes is not null) product.Sizes = [.. request.Sizes.Select(s => s.Trim()).Distinct()];
        if (request.Stock is { } stock) product.Stock = stock;
        if (request.Published is { } published) product.Published = published;
        if (request.Story is not null) product.Story = request.Story.Trim();
        if (request.ImageUrls is not null) product.ImageUrls = [.. request.ImageUrls];
        product.UpdatedAt = time.UtcNow();
        await db.SaveChangesAsync(ct);
        return TypedResults.Ok(ProductDto.From(product));
    }

    /// <summary>A catalogue photo: JPEG, PNG or WebP up to 5 MB, checked from its bytes.</summary>
    private static async Task<Created<UploadResult>> Upload(IFormFile file, HttpRequest http, CurrentUser user,
        AppDbContext db, IFileStore files, IConfiguration config, CancellationToken ct)
    {
        var staff = await user.StaffAsync(ct);
        if (file.Length > MaxUploadBytes)
            throw new ApiException(StatusCodes.Status413PayloadTooLarge, "FILE_TOO_LARGE", "Photos can be up to 5 MB.");
        var type = await ImageSniffer.DetectAsync(file, ct)
                   ?? throw new ApiException(StatusCodes.Status415UnsupportedMediaType, "UNSUPPORTED_FILE_TYPE",
                       "Use a JPEG, PNG or WebP photo.");

        await using var stream = file.OpenReadStream();
        var stored = await files.SaveAsync("img", stream, type, staff.Id, ct);
        db.Files.Add(stored);
        await db.SaveChangesAsync(ct);
        var baseUrl = config["Storage:PublicBaseUrl"] is { Length: > 0 } configured
            ? configured.TrimEnd('/')
            : $"{http.Scheme}://{http.Host}";
        return TypedResults.Created($"{baseUrl}/files/{stored.Id}", new UploadResult(stored.Id, $"{baseUrl}/files/{stored.Id}"));
    }

    private static async Task<Product> FindAsync(AppDbContext db, string id, CancellationToken ct) =>
        await db.Products.SingleOrDefaultAsync(p => p.Id == id, ct)
        ?? throw ApiException.NotFound(NotFoundCode, "Product not found.");
}

/// <summary>Catalogue photos are public: the shop shows them to anyone. Collection photos are not served.</summary>
public static class FileEndpoints
{
    public static void MapFiles(this WebApplication app) =>
        app.MapGet("/files/{id}", async Task<IResult> (string id, AppDbContext db, IFileStore files, CancellationToken ct) =>
        {
            var file = await db.Files.AsNoTracking().SingleOrDefaultAsync(f => f.Id == id && f.Id.StartsWith("img_"), ct);
            if (file is null) return Results.NotFound();
            return Results.Stream(files.OpenRead(file), file.ContentType,
                lastModified: new DateTimeOffset(file.CreatedAt, TimeSpan.Zero));
        }).ExcludeFromDescription().AllowAnonymous();
}
