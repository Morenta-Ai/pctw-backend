using System.Text.Json;
using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Rethread.Api.Auth;
using Rethread.Api.Common;
using Rethread.Api.Data;
using Rethread.Api.Data.Entities;
using Rethread.Api.Domain;

namespace Rethread.Api.Features.Customer.Pickups;

public sealed record PickupConfigDto(int MinItems, int MinPieces, int MaxPieces, long RatePerKgCents, string Currency,
    int PreferredDatesRequired);

public sealed record AvailabilityDay(DateOnly Date, IReadOnlyList<AvailabilityDay.SlotState> Slots)
{
    public sealed record SlotState(string Id, string Label, string Start, string End, bool Available);
}

public sealed record BookPickupRequest(
    PickupMethod Mode,
    int? PieceCount,
    int? BagCount,
    IReadOnlyList<BookPickupRequest.ItemRequest>? Items,
    IReadOnlyList<BookPickupRequest.PreferredDateRequest>? PreferredDates,
    string? AddressId,
    string? Notes,
    bool FallbackToCallCentre)
{
    public sealed record ItemRequest(string GarmentTypeId, string? FamilyMemberId, string? Size, ItemCondition Condition,
        int Quantity);

    public sealed record PreferredDateRequest(int Priority, DateOnly Date, string SlotId);
}

public sealed record CancelPickupRequest(string Reason);

public sealed record PickupPage(IReadOnlyList<CustomerPickupDto> Items, int Page, int PageSize, bool HasMore);

public sealed record Tracking(CustomerPickupStatus Status, Tracking.Location? CollectorLocation, int? EtaMinutes)
{
    public sealed record Location(double Lat, double Lng, DateTime UpdatedAt);
}

public sealed class BookPickupRequestValidator : AbstractValidator<BookPickupRequest>
{
    public BookPickupRequestValidator()
    {
        RuleFor(x => x.Notes).MaximumLength(500);
        RuleFor(x => x.BagCount).InclusiveBetween(1, 50).When(x => x.BagCount is not null);
        RuleForEach(x => x.Items).ChildRules(item =>
        {
            item.RuleFor(i => i.GarmentTypeId).NotEmpty();
            item.RuleFor(i => i.Quantity).InclusiveBetween(1, 200);
            item.RuleFor(i => i.Size).MaximumLength(12);
        });
    }
}

/// <summary>
/// The user app's pickups: the customer describes the clothes and offers three dates; a collector confirms
/// one and weighs at the door.
/// </summary>
public static class CustomerPickupEndpoints
{
    public const int MinItems = 5;
    public const int MinPieces = 5;
    public const int MaxPieces = 300;
    public const int PreferredDatesRequired = 3;
    private static readonly string[] CancelReasons = ["changedMind", "notReady", "wrongDetails", "tooLong", "other"];

    public static void MapCustomerPickups(this RouteGroupBuilder api)
    {
        var pickups = api.MapGroup("/pickups");
        pickups.MapGet("/config", Config);
        pickups.MapGet("/availability", Availability);
        pickups.MapGet("", List);
        pickups.MapPost("", Book).Validate<BookPickupRequest>();
        pickups.MapGet("/{id}", Get);
        pickups.MapGet("/{id}/tracking", Track);
        pickups.MapPost("/{id}/cancel", Cancel);
    }

    private static async Task<Ok<PickupConfigDto>> Config(RateBook rates, CancellationToken ct) =>
        TypedResults.Ok(new PickupConfigDto(MinItems, MinPieces, MaxPieces,
            (await rates.CurrentAsync(ct)).CustomerRatePerKgCents, "LKR", PreferredDatesRequired));

    private static async Task<Ok<ItemList<AvailabilityDay>>> Availability(DateOnly? from, int? days, PickupSlots slots,
        TimeProvider time, CancellationToken ct)
    {
        var start = from ?? time.Today();
        var count = Math.Clamp(days ?? PickupSlots.BookingDays, 1, 31);
        var open = await slots.AvailabilityAsync(start, count, ct);
        return TypedResults.Ok(new ItemList<AvailabilityDay>(Enumerable.Range(0, count).Select(i => start.AddDays(i))
            .Select(date => new AvailabilityDay(date, PickupSlots.All.Select(s => new AvailabilityDay.SlotState(
                CamelCaseEnumConverter<Slot>.ToText(s.Id), s.Label, s.Start, s.End, open[(date, s.Id)])).ToList()))
            .ToList()));
    }

    private static async Task<Ok<PickupPage>> List(string? tab, int? page, int? pageSize, CurrentUser user,
        AppDbContext db, CancellationToken ct)
    {
        var customer = await user.CustomerAsync(ct);
        var paging = PageRequest.From(page, pageSize ?? 20);
        var mine = WithDetails(db.Pickups).Where(p => p.CustomerId == customer.Id);
        var query = (tab ?? "upcoming") switch
        {
            "upcoming" => mine.Where(p => PickupWorkflow.Open.Contains(p.Status) || p.Status == PickupStatus.Collected)
                .OrderBy(p => p.ScheduledDate).ThenBy(p => p.WindowStart).ThenBy(p => p.Id),
            "completed" => mine.Where(p => p.Status == PickupStatus.DeliveredToHub)
                .OrderByDescending(p => p.DeliveredAt).ThenByDescending(p => p.Id),
            "cancelled" => mine.Where(p => p.Status == PickupStatus.Cancelled)
                .OrderByDescending(p => p.CreatedAt).ThenByDescending(p => p.Id),
            _ => throw ApiException.Validation("tab", "Use upcoming, completed or cancelled."),
        };
        var rows = await query.Skip(paging.Skip).Take(paging.PageSize + 1).ToListAsync(ct);
        var names = await GarmentNamesAsync(db, ct);
        return TypedResults.Ok(new PickupPage(rows.Take(paging.PageSize).Select(p => CustomerPickupDto.From(p, names)).ToList(),
            paging.Page, paging.PageSize, rows.Count > paging.PageSize));
    }

    private static async Task<IResult> Book(BookPickupRequest request,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey, CurrentUser user, AppDbContext db,
        PickupWorkflow workflow, PickupSlots slots, TimeProvider time, CancellationToken ct)
    {
        var customer = await user.CustomerAsync(ct);
        if (string.IsNullOrWhiteSpace(idempotencyKey))
            throw ApiException.BadRequest(ErrorCodes.BadRequest, "Send an Idempotency-Key header.");
        var hash = Secrets.Hash(JsonSerializer.Serialize(request, Json.Options));
        var replay = await Idempotency.FindAsync(db, customer.Id, "pickup", idempotencyKey, hash, ct);
        if (replay is not null) return TypedResults.Created($"/api/v1/pickups/{replay}", await DtoAsync(db, replay, ct));

        var address = request.AddressId is null ? null
            : await db.Addresses.SingleOrDefaultAsync(a => a.Id == request.AddressId && a.CustomerId == customer.Id, ct);
        if (address is null) throw ApiException.Unprocessable("ADDRESS_REQUIRED", "Choose one of your addresses.");

        var dates = await PreferredDatesAsync(request, slots, time, ct);
        var items = new List<PickupWorkflow.ItemDraft>();
        if (request.Mode == PickupMethod.Bulk)
        {
            if (request.PieceCount is not { } pieces || pieces < MinPieces || pieces > MaxPieces)
                throw ApiException.Unprocessable("BELOW_MINIMUM", $"Give between {MinPieces} and {MaxPieces} pieces.",
                    new { minPieces = MinPieces, maxPieces = MaxPieces });
        }
        else
        {
            var lines = request.Items ?? [];
            if (lines.Sum(i => i.Quantity) < MinItems)
                throw ApiException.Unprocessable("BELOW_MINIMUM", $"Add at least {MinItems} garments.", new { minItems = MinItems });
            var types = await db.GarmentTypes.ToDictionaryAsync(t => t.Id, ct);
            var members = await db.FamilyMembers.Where(m => m.CustomerId == customer.Id).Select(m => m.Id).ToListAsync(ct);
            var errors = new Dictionary<string, string>();
            for (var i = 0; i < lines.Count; i++)
            {
                if (!types.ContainsKey(lines[i].GarmentTypeId)) errors[$"items[{i}].garmentTypeId"] = "Unknown garment type.";
                if (lines[i].FamilyMemberId is { } member && !members.Contains(member))
                    errors[$"items[{i}].familyMemberId"] = "Choose one of your family members.";
            }

            if (errors.Count > 0) throw ApiException.Validation(errors);
            items.AddRange(lines.Select(i => new PickupWorkflow.ItemDraft(types[i.GarmentTypeId], i.FamilyMemberId, i.Size,
                i.Condition, i.Quantity)));
        }

        var pickup = await workflow.BookAsync(new PickupWorkflow.Booking(customer, address, request.Mode,
            request.PieceCount, request.BagCount, items, dates, request.Notes, request.FallbackToCallCentre), ct);
        await Idempotency.SaveAsync(db, customer.Id, "pickup", idempotencyKey, hash, pickup.Id, time, ct);
        return TypedResults.Created($"/api/v1/pickups/{pickup.Id}", await DtoAsync(db, pickup.Id, ct));
    }

    /// <summary>Exactly three different, open days ranked 1 to 3; a full or closed slot is SLOT_UNAVAILABLE.</summary>
    private static async Task<List<PickupWorkflow.PreferredDate>> PreferredDatesAsync(BookPickupRequest request,
        PickupSlots slots, TimeProvider time, CancellationToken ct)
    {
        var given = request.PreferredDates ?? [];
        ApiException Invalid() => ApiException.Unprocessable("INVALID_PREFERRED_DATES",
            "Choose three different days, in order of preference.", new { required = PreferredDatesRequired });
        if (given.Count != PreferredDatesRequired || given.Select(d => d.Date).Distinct().Count() != PreferredDatesRequired ||
            !given.Select(d => d.Priority).Order().SequenceEqual(Enumerable.Range(1, PreferredDatesRequired)))
            throw Invalid();

        var parsed = given.Select(d => new PickupWorkflow.PreferredDate(d.Priority, d.Date,
            CamelCaseEnumConverter<Slot>.TryParse(d.SlotId) ?? throw Invalid())).ToList();
        var today = time.Today();
        var open = await slots.AvailabilityAsync(today, PickupSlots.BookingDays, ct);
        foreach (var date in parsed)
        {
            // A day with nothing open can't be offered at all; a day with other slots open just needs another slot.
            if (!PickupSlots.All.Any(s => open.GetValueOrDefault((date.Date, s.Id)))) throw Invalid();
            if (!open[(date.Date, date.Slot)])
            {
                throw ApiException.Conflict("SLOT_UNAVAILABLE", "That slot just filled up or closed.",
                    new { date = date.Date, slotId = CamelCaseEnumConverter<Slot>.ToText(date.Slot) });
            }
        }

        return parsed;
    }

    private static async Task<Ok<CustomerPickupDto>> Get(string id, CurrentUser user, AppDbContext db, CancellationToken ct)
    {
        var pickup = await FindAsync(id, user, db, ct);
        return TypedResults.Ok(CustomerPickupDto.From(pickup, await GarmentNamesAsync(db, ct)));
    }

    /// <summary>Small, because the app polls it while the tracking screen is open.</summary>
    private static async Task<Ok<Tracking>> Track(string id, CurrentUser user, AppDbContext db, CancellationToken ct)
    {
        var pickup = await FindAsync(id, user, db, ct);
        var status = CustomerPickupDto.StatusFor(pickup.Status);
        if (status != CustomerPickupStatus.EnRoute || pickup.Collector is not { Latitude: { } lat, Longitude: { } lng } c)
            return TypedResults.Ok(new Tracking(status, null, null));
        return TypedResults.Ok(new Tracking(status, new Tracking.Location(lat, lng, c.LastSeenAt ?? pickup.CreatedAt),
            Geo.EtaMinutes(Geo.DistanceKm(lat, lng, pickup.Latitude, pickup.Longitude))));
    }

    private static async Task<Ok<CustomerPickupDto>> Cancel(string id, CancelPickupRequest? request, CurrentUser user,
        AppDbContext db, PickupWorkflow workflow, CancellationToken ct)
    {
        var reason = request?.Reason ?? "other";
        if (!CancelReasons.Contains(reason))
            throw ApiException.Validation("reason", "Use changedMind, notReady, wrongDetails, tooLong or other.");
        var pickup = await FindAsync(id, user, db, ct);
        if (!PickupWorkflow.CustomerCancellable.Contains(pickup.Status))
            throw ApiException.Conflict(PickupWorkflow.PickupNotCancellable, "The collector is already on the way.");
        pickup.CustomerCancelReason = reason;
        await workflow.CancelAsync(pickup, $"Customer cancelled in the app ({reason}).", actor: null, ct);
        return TypedResults.Ok(await DtoAsync(db, id, ct));
    }

    public static IQueryable<Pickup> WithDetails(IQueryable<Pickup> pickups) =>
        pickups.Include(p => p.Collector).Include(p => p.Items).Include(p => p.Events).Include(p => p.PreferredDates)
            .AsSplitQuery();

    private static async Task<CustomerPickupDto> DtoAsync(AppDbContext db, string id, CancellationToken ct)
    {
        db.ChangeTracker.Clear();
        var pickup = await WithDetails(db.Pickups).AsNoTracking().SingleAsync(p => p.Id == id, ct);
        return CustomerPickupDto.From(pickup, await GarmentNamesAsync(db, ct));
    }

    /// <summary>A customer's own pickup, or 404: other people's pickups don't exist as far as they know.</summary>
    private static async Task<Pickup> FindAsync(string id, CurrentUser user, AppDbContext db, CancellationToken ct)
    {
        var customer = await user.CustomerAsync(ct);
        return await WithDetails(db.Pickups).SingleOrDefaultAsync(p => p.Id == id && p.CustomerId == customer.Id, ct)
               ?? throw ApiException.NotFound(PickupWorkflow.PickupNotFound, "Pickup not found.");
    }

    public static async Task<Dictionary<string, string>> GarmentNamesAsync(AppDbContext db, CancellationToken ct) =>
        await db.GarmentTypes.ToDictionaryAsync(t => t.Name, t => t.Id, ct);
}
