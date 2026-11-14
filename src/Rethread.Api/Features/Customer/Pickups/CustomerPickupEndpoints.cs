using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Rethread.Api.Auth;
using Rethread.Api.Common;
using Rethread.Api.Data;
using Rethread.Api.Data.Entities;
using Rethread.Api.Domain;

namespace Rethread.Api.Features.Customer.Pickups;

public sealed record GarmentTypeDto(string Id, decimal EstimatedKg);

public sealed record BookPickupRequest(
    PickupMethod Method,
    BulkMode? BulkMode,
    decimal? DeclaredWeightKg,
    int? DeclaredPieces,
    IReadOnlyList<BookPickupRequest.ItemRequest>? Items,
    DateOnly ScheduledDate,
    Slot Slot,
    string AddressId,
    string? Notes,
    bool FallbackToCallCentre)
{
    public sealed record ItemRequest(string GarmentType, string? MemberId, string? Size, ItemCondition Condition,
        int Quantity);
}

public sealed class BookPickupRequestValidator : AbstractValidator<BookPickupRequest>
{
    public BookPickupRequestValidator()
    {
        RuleFor(x => x.AddressId).NotEmpty().WithMessage("Choose an address.");
        RuleFor(x => x.Notes).MaximumLength(500);
        When(x => x.Method == PickupMethod.Bulk, () =>
        {
            RuleFor(x => x.BulkMode).NotNull().WithMessage("Say whether you're giving a weight or a number of pieces.");
            RuleFor(x => x.DeclaredWeightKg).NotNull().InclusiveBetween(0.5m, 500m)
                .When(x => x.BulkMode == BulkMode.Weight).WithMessage("Enter a weight between 0.5 and 500 kg.");
            RuleFor(x => x.DeclaredPieces).NotNull().InclusiveBetween(1, 2000)
                .When(x => x.BulkMode == BulkMode.Pieces).WithMessage("Enter how many pieces.");
        });
        When(x => x.Method == PickupMethod.Itemised, () =>
        {
            RuleFor(x => x.Items).NotEmpty().WithMessage("Add at least one item.");
            RuleForEach(x => x.Items).ChildRules(item =>
            {
                item.RuleFor(i => i.GarmentType).NotEmpty();
                item.RuleFor(i => i.Quantity).InclusiveBetween(1, 200);
                item.RuleFor(i => i.Size).MaximumLength(12);
            });
        });
    }
}

public static class CustomerPickupEndpoints
{
    private const int PageSize = 20;

    public static void MapCustomerPickups(this RouteGroupBuilder api)
    {
        var pickups = api.MapGroup("/pickups");
        pickups.MapGet("/garment-types", GarmentTypes);
        pickups.MapGet("", List);
        pickups.MapPost("", Book).Validate<BookPickupRequest>();
        pickups.MapGet("/{id}", Get);
        pickups.MapPost("/{id}/cancel", Cancel);
    }

    private static async Task<Ok<ItemList<GarmentTypeDto>>> GarmentTypes(AppDbContext db, CancellationToken ct)
    {
        var types = await db.GarmentTypes.OrderBy(t => t.SortOrder).ToListAsync(ct);
        return TypedResults.Ok(new ItemList<GarmentTypeDto>(types.Select(t => new GarmentTypeDto(t.Id, t.EstimatedKg)).ToList()));
    }

    private static async Task<Ok<CursorPage<CustomerPickupDto>>> List(string? group, string? cursor, CurrentUser user,
        AppDbContext db, CancellationToken ct)
    {
        var customer = await user.CustomerAsync(ct);
        PickupStatus[] statuses = group switch
        {
            null or "upcoming" => PickupWorkflow.Open,
            "completed" => [PickupStatus.Collected, PickupStatus.DeliveredToHub],
            "cancelled" => [PickupStatus.Cancelled],
            _ => throw ApiException.Validation("group", "Use upcoming, completed or cancelled."),
        };
        var names = await GarmentNamesAsync(db, ct);
        var page = await WithDetails(db.Pickups)
            .Where(p => p.CustomerId == customer.Id && statuses.Contains(p.Status))
            .ToCursorPageAsync(cursor, PageSize, p => CustomerPickupDto.From(p, names), ct);
        return TypedResults.Ok(page);
    }

    private static async Task<Created<CustomerPickupDto>> Book(BookPickupRequest request, CurrentUser user,
        AppDbContext db, PickupWorkflow workflow, TimeProvider time, CancellationToken ct)
    {
        var customer = await user.CustomerAsync(ct);
        var address = await db.Addresses.SingleOrDefaultAsync(a => a.Id == request.AddressId && a.CustomerId == customer.Id, ct)
                      ?? throw ApiException.Validation("addressId", "Choose one of your addresses.");

        var today = time.Today();
        if (request.ScheduledDate < today || request.ScheduledDate > today.AddDays(14))
            throw ApiException.Validation("scheduledDate", "Choose a day in the next two weeks.");
        var (_, endHour) = PickupWorkflow.SlotHours(request.Slot);
        if (Colombo.At(request.ScheduledDate, endHour) <= time.UtcNow())
            throw ApiException.Validation("slot", "That time slot has already passed. Choose a later one.");

        var items = new List<PickupWorkflow.ItemDraft>();
        if (request.Method == PickupMethod.Itemised)
        {
            var types = await db.GarmentTypes.ToDictionaryAsync(t => t.Id, ct);
            var members = await db.FamilyMembers.Where(m => m.CustomerId == customer.Id).Select(m => m.Id).ToListAsync(ct);
            var errors = new Dictionary<string, string>();
            for (var i = 0; i < request.Items!.Count; i++)
            {
                var item = request.Items[i];
                if (!types.ContainsKey(item.GarmentType)) errors[$"items[{i}].garmentType"] = "Unknown garment type.";
                if (item.MemberId is not null && !members.Contains(item.MemberId))
                    errors[$"items[{i}].memberId"] = "Choose one of your family members.";
            }

            if (errors.Count > 0) throw ApiException.Validation(errors);
            items.AddRange(request.Items.Select(i =>
                new PickupWorkflow.ItemDraft(types[i.GarmentType], i.MemberId, i.Size, i.Condition, i.Quantity)));
        }

        var pickup = await workflow.BookAsync(new PickupWorkflow.Booking(customer, address, request.Method,
            request.BulkMode, request.DeclaredWeightKg, request.DeclaredPieces, items, request.ScheduledDate,
            request.Slot, request.Notes, request.FallbackToCallCentre), ct);
        return TypedResults.Created($"/api/v1/pickups/{pickup.Id}",
            CustomerPickupDto.From(pickup, await GarmentNamesAsync(db, ct)));
    }

    private static async Task<Ok<CustomerPickupDto>> Get(string id, CurrentUser user, AppDbContext db,
        CancellationToken ct)
    {
        var pickup = await FindAsync(id, user, db, ct);
        return TypedResults.Ok(CustomerPickupDto.From(pickup, await GarmentNamesAsync(db, ct)));
    }

    private static async Task<Ok<CustomerPickupDto>> Cancel(string id, CurrentUser user, AppDbContext db,
        PickupWorkflow workflow, CancellationToken ct)
    {
        var pickup = await FindAsync(id, user, db, ct);
        await workflow.CancelAsync(pickup, "Customer cancelled in the app.", actor: null, ct);
        return TypedResults.Ok(CustomerPickupDto.From(await FindAsync(id, user, db, ct), await GarmentNamesAsync(db, ct)));
    }

    private static IQueryable<Pickup> WithDetails(IQueryable<Pickup> pickups) =>
        pickups.Include(p => p.Collector).Include(p => p.Items).Include(p => p.Events).AsSplitQuery();

    /// <summary>A customer's own pickup, or 404: other people's pickups don't exist as far as they know.</summary>
    private static async Task<Pickup> FindAsync(string id, CurrentUser user, AppDbContext db, CancellationToken ct)
    {
        var customer = await user.CustomerAsync(ct);
        return await WithDetails(db.Pickups).SingleOrDefaultAsync(p => p.Id == id && p.CustomerId == customer.Id, ct)
               ?? throw ApiException.NotFound(PickupWorkflow.PickupNotFound, "Pickup not found.");
    }

    private static async Task<Dictionary<string, string>> GarmentNamesAsync(AppDbContext db, CancellationToken ct) =>
        await db.GarmentTypes.ToDictionaryAsync(t => t.Name, t => t.Id, ct);
}
