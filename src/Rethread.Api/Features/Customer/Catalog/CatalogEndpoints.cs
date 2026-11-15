using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Rethread.Api.Common;
using Rethread.Api.Data;
using Rethread.Api.Data.Entities;
using Rethread.Api.Domain;

namespace Rethread.Api.Features.Customer.Catalog;

public sealed record GarmentTypeDto(string Id, string Name, string Icon, decimal AvgWeightKg, string SizeBasis);

public sealed record SizeChartDto(string SizeBasis, AgeGroup AgeGroup, IReadOnlyList<SizeCharts.Range> Ranges);

/// <summary>Reference data for the itemised flow. The app caches it for a day.</summary>
public static class CatalogEndpoints
{
    public static void MapCatalog(this RouteGroupBuilder api)
    {
        api.MapGet("/catalog/garment-types", async Task<Ok<ItemList<GarmentTypeDto>>> (AppDbContext db, CancellationToken ct) =>
        {
            var types = await db.GarmentTypes.AsNoTracking().OrderBy(t => t.SortOrder).ToListAsync(ct);
            return TypedResults.Ok(new ItemList<GarmentTypeDto>(types
                .Select(t => new GarmentTypeDto(t.Id, t.Name, t.Icon, t.EstimatedKg, t.SizeBasis)).ToList()));
        });
        api.MapGet("/catalog/size-charts", () => TypedResults.Ok(new ItemList<SizeChartDto>(
            SizeCharts.All.Select(c => new SizeChartDto(c.SizeBasis, c.AgeGroup, c.Ranges)).ToList())));
    }
}
