using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Rethread.Api.Common;
using Rethread.Api.Data.Entities;
using Rethread.Api.Domain;

namespace Rethread.Api.Data.Seed;

/// <summary>
/// Development and test data: a port of the admin portal's mock world (<c>src/mock</c>), with the IDs the
/// apps use (<c>RT-24817</c>, <c>COL-0457</c>, <c>ORD-5134</c>). Times are relative to "now", so today's
/// queue always looks like today. Never runs in Production.
/// </summary>
public sealed class DevSeeder(
    AppDbContext db,
    IPasswordHasher<Staff> passwords,
    BankAccountProtector bankAccounts,
    TimeProvider time,
    ILogger<DevSeeder> logger)
{
    private const int Minute = 1;
    private static readonly PickupStatus[] Flow =
    [
        PickupStatus.Pending, PickupStatus.Assigned, PickupStatus.EnRoute, PickupStatus.Arrived,
        PickupStatus.Collected, PickupStatus.DeliveredToHub,
    ];

    private DateTime _now;
    private DateOnly _today;
    private DateTime _dayStart;
    private SeedRandom _random = new(24_841);

    /// <summary>Seeds an empty database. Returns false, doing nothing, if it already has data.</summary>
    public async Task<bool> SeedAsync(CancellationToken ct = default)
    {
        if (await db.Staff.AnyAsync(ct)) return false;

        _now = time.UtcNow();
        _today = Colombo.DateOf(_now);
        _dayStart = Colombo.StartOfDay(_today);
        _random = new SeedRandom(24_841);

        var staff = SeedRows.Staff();
        foreach (var member in staff)
            member.PasswordHash = passwords.HashPassword(member, SeedRows.StaffPasswords[member.Email]);
        var rateSets = SeedRows.RateSets();
        var rates = rateSets[^1];
        var hubs = SeedRows.Hubs();
        var vendors = SeedRows.Vendors();

        var collectors = Collectors();
        var (customers, familyCounts) = Customers();
        var pickups = Pickups(customers, collectors, rates);
        var products = Products();
        var projects = SeedRows.DonationProjects();
        var orders = Orders(customers, products, vendors, rates);
        var redemptions = Redemptions(customers, pickups);

        db.Staff.AddRange(staff);
        db.RateSets.AddRange(rateSets);
        db.ImpactMultipliers.AddRange(SeedRows.Multipliers());
        db.Hubs.AddRange(hubs);
        db.Vendors.AddRange(vendors);
        db.GarmentTypes.AddRange(SeedRows.GarmentTypes());
        db.VoucherBrands.AddRange(SeedRows.VoucherBrands());
        db.Collectors.AddRange(collectors);
        db.Customers.AddRange(customers);
        db.FamilyMembers.AddRange(FamilyMembers(customers, familyCounts));
        db.Pickups.AddRange(pickups);
        db.HubDeliveries.AddRange(HubDeliveries(pickups, collectors, hubs));
        db.Products.AddRange(products);

        foreach (var (project, baseRaised, baseDonors) in projects)
        {
            var donations = redemptions.Where(r => r.ProjectId == project.Id).ToList();
            project.RaisedCents = baseRaised + donations.Sum(r => r.AmountCents);
            project.DonorCount = baseDonors + donations.Select(r => r.CustomerId).Distinct().Count();
            db.DonationProjects.Add(project);
        }

        db.Redemptions.AddRange(redemptions);
        db.BankDetails.AddRange(BankDetailsFor(redemptions));
        db.WalletTransactions.AddRange(Ledger(pickups, redemptions, orders, projects.Select(p => p.Project)));
        db.Orders.AddRange(orders);
        db.Notifications.AddRange(Notifications(pickups, orders, redemptions, customers, collectors, hubs));

        await db.SaveChangesAsync(ct);
        logger.LogInformation("Seeded {Pickups} pickups, {Customers} customers and {Collectors} collectors",
            pickups.Count, customers.Count, collectors.Count);
        return true;
    }

    private DateTime Ago(double minutes)
    {
        var floor = _dayStart.AddMinutes(5);
        var at = _now.AddMinutes(-minutes);
        return at > floor ? at : floor;
    }

    // ── People ───────────────────────────────────────────────────────────────────────────────────────

    private List<Collector> Collectors() =>
        SeedRows.Collectors.Select((row, i) =>
        {
            var area = Areas.Find(row.Area)!;
            var minutesAgo = row.Online ? _random.Int(0, 4) : _random.Int(90, 60 * 20);
            var latitude = area.Latitude + (_random.Next() - 0.5) * 0.02;
            var longitude = area.Longitude + (_random.Next() - 0.5) * 0.02;
            var rating = SeedRandom.JsRound((4.3 + _random.Next() * 0.7) * 10) / 10m;
            var memberSince = new DateOnly(2024 + i % 2, 1 + i % 9, 1);
            var digits = row.Code[4..];
            return new Collector
            {
                Id = $"col_{(digits.StartsWith('0') ? digits[1..] : digits)}",
                Code = row.Code,
                FirstName = row.First,
                LastName = row.Last,
                Mobile = $"+9477{(1234567 + i * 7919).ToString(System.Globalization.CultureInfo.InvariantCulture)[..7]}",
                Status = row.Code == "COL-0541" ? AccountStatus.Suspended : AccountStatus.Active,
                StatusReason = row.Code == "COL-0541" ? "Vehicle licence expired." : null,
                Online = row.Online,
                Latitude = latitude,
                Longitude = longitude,
                LastSeenAt = _now.AddMinutes(-minutesAgo),
                Area = row.Area,
                Region = area.Region,
                Suburbs = Areas.SuburbsFor(row.Area),
                HubId = area.Latitude < 6.86 ? "hub_mrt" : "hub_plg",
                VehicleType = row.Vehicle,
                VehiclePlate = row.Plate,
                CapacityKg = row.CapacityKg,
                Rating = rating,
                MemberSince = memberSince,
                ScaleName = row.Code == "COL-0457" ? "Scale BT-200" : null,
                ScalePaired = row.Code == "COL-0457",
                CreatedAt = Colombo.At(memberSince, 9),
            };
        }).ToList();

    private (List<Customer>, Dictionary<string, int>) Customers()
    {
        var customers = new List<Customer>();
        var familyCounts = new Dictionary<string, int>();
        for (var i = 0; i < SeedRows.Customers.Length; i++)
        {
            var (first, last) = SeedRows.Customers[i];
            var area = Areas.All[i % Areas.All.Count];
            var daysAgo = _random.Int(20, 400);
            var email = _random.Chance(0.6) ? $"{first.ToLowerInvariant()}.{last.ToLowerInvariant()}@example.lk" : null;
            var line = $"{_random.Int(2, 180)}/{_random.Int(1, 9)} {_random.Pick(SeedRows.CustomerStreets)}, {area.Name}";
            var latitude = area.Latitude + (_random.Next() - 0.5) * 0.015;
            var longitude = area.Longitude + (_random.Next() - 0.5) * 0.015;
            var id = $"cus_{i + 1}";
            familyCounts[id] = _random.Int(0, 4);
            var joined = _now.AddDays(-daysAgo);
            customers.Add(new Customer
            {
                Id = id,
                FirstName = first,
                LastName = last,
                Mobile = $"+9476{(4821190 + i * 3571).ToString(System.Globalization.CultureInfo.InvariantCulture)[..7]}",
                Email = email,
                Status = i == 17 ? AccountStatus.Suspended : AccountStatus.Active,
                StatusReason = i == 17 ? "Repeated fake bookings." : null,
                CreatedAt = joined,
                Addresses =
                [
                    new Address
                    {
                        Id = $"adr_{i + 1}", CustomerId = id, Label = AddressLabel.Home, Line = line,
                        Area = area.Name, Latitude = latitude, Longitude = longitude, IsDefault = true,
                        CreatedAt = joined,
                    },
                ],
            });
        }

        return (customers, familyCounts);
    }

    private static readonly HashSet<string> FemaleNames =
        ["Nadeesha", "Anjali", "Fathima", "Kumudu", "Thilini", "Sanduni", "Hasini", "Malsha", "Ishani", "Nirosha", "Ayesha"];

    private static readonly string[] FamilyFirstNames = ["Chamara", "Sahan", "Dulani", "Kamala"];

    private static IEnumerable<FamilyMember> FamilyMembers(List<Customer> customers, Dictionary<string, int> counts)
    {
        foreach (var customer in customers)
        {
            for (var k = 0; k < counts[customer.Id]; k++)
            {
                var self = k == 0;
                var female = self ? FemaleNames.Contains(customer.FirstName!) : k is 3 or 4;
                var (relation, gender, age) = self
                    ? (FamilyRelation.Self, female ? Gender.Female : Gender.Male, AgeGroup.Adult)
                    : (SeedRows.FamilyTemplates[k - 1].Relation, SeedRows.FamilyTemplates[k - 1].Gender,
                        SeedRows.FamilyTemplates[k - 1].Age);
                var child = age is AgeGroup.Child or AgeGroup.Teen;
                yield return new FamilyMember
                {
                    Id = $"fam_{customer.Id[4..]}_{k + 1}",
                    CustomerId = customer.Id,
                    Name = self ? customer.FullName : $"{FamilyFirstNames[k - 1]} {customer.LastName}",
                    Relation = relation,
                    Gender = gender,
                    AgeGroup = age,
                    HeightCm = child ? 140 : gender == Gender.Female ? 160 : 172,
                    ChestCm = child ? 72 : gender == Gender.Female ? 86 : 98,
                    WaistCm = child ? 62 : gender == Gender.Female ? 71 : 84,
                    HipCm = child ? 76 : gender == Gender.Female ? 94 : 98,
                    ShoeSize = child ? "36" : gender == Gender.Female ? "38" : "42",
                    CreatedAt = customer.CreatedAt,
                };
            }
        }
    }

    // ── Pickups ──────────────────────────────────────────────────────────────────────────────────────

    private sealed record Draft(
        string Id,
        Customer Customer,
        Collector? Collector,
        PickupStatus Status,
        PickupSource Source,
        PickupMethod Method,
        decimal EstimatedKg,
        int? ItemCount,
        int BagCount,
        decimal? ActualWeightKg,
        DateOnly Date,
        int StartHour,
        DateTime CreatedAt,
        DateTime[] StepTimes,
        string? Note = null,
        string? CancelReason = null);

    private List<Pickup> Pickups(List<Customer> customers, List<Collector> collectors, RateSet rates)
    {
        Customer C(int n) => customers[n - 1];
        Collector Col(string code) => collectors.Single(c => c.Code == code);
        DateTime[] Steps(params double[] minutesAgo) => minutesAgo.Select(Ago).ToArray();

        var drafts = new List<Draft>
        {
            new("RT-24817", C(1), Col("COL-0457"), PickupStatus.EnRoute, PickupSource.CallCentre, PickupMethod.Bulk, 8, null, 3, null, _today, 10, Ago(150), Steps(120, 12),
                "Bags are next to the side gate. Please ring twice — gate code 1144."),
            new("RT-24822", C(2), null, PickupStatus.Pending, PickupSource.App, PickupMethod.Itemised, 5.5m, 14, 2, null, _today, 11, Ago(55), []),
            new("RT-24830", C(3), null, PickupStatus.Pending, PickupSource.App, PickupMethod.Bulk, 12, null, 4, null, _today, 13, Ago(35), []),
            new("RT-24835", C(5), null, PickupStatus.Pending, PickupSource.App, PickupMethod.Itemised, 3.2m, 9, 1, null, _today, 14, Ago(20), []),
            new("RT-24841", C(6), null, PickupStatus.Pending, PickupSource.CallCentre, PickupMethod.Bulk, 9.5m, null, 3, null, _today, 15, Ago(42), [],
                "Customer called in: no collector accepted in the app."),
            new("RT-24844", C(7), null, PickupStatus.Pending, PickupSource.App, PickupMethod.Bulk, 6, null, 2, null, _today, 16, Ago(8), []),
            new("RT-24826", C(9), Col("COL-0466"), PickupStatus.Assigned, PickupSource.App, PickupMethod.Itemised, 4.4m, 11, 2, null, _today, 12, Ago(90), Steps(70)),
            new("RT-24819", C(8), Col("COL-0480"), PickupStatus.Arrived, PickupSource.App, PickupMethod.Bulk, 18, null, 5, null, _today, 10, Ago(200), Steps(180, 30, 6)),
        };

        // RT-24790/96/803 are the collector app's sample load: 20.5 kg in all.
        foreach (var (id, n, kg, minutes) in new[] { ("RT-24790", 10, 6.2m, 260), ("RT-24796", 11, 7.1m, 200), ("RT-24803", 12, 7.2m, 140) })
        {
            drafts.Add(new Draft(id, C(n), Col("COL-0457"), PickupStatus.Collected, PickupSource.App, PickupMethod.Bulk,
                SeedRandom.JsRound((double)kg), null, 2, kg, _today, 8, Ago(minutes + 120),
                Steps(minutes + 90, minutes + 30, minutes + 10, minutes)));
        }

        drafts.AddRange(
        [
            new("RT-24806", C(12), Col("COL-0312"), PickupStatus.DeliveredToHub, PickupSource.App, PickupMethod.Bulk, 10, null, 3, 9.8m, _today, 8, Ago(400), Steps(380, 300, 280, 260, 60)),
            new("RT-24811", C(13), Col("COL-0388"), PickupStatus.Collected, PickupSource.CallCentre, PickupMethod.Bulk, 15, null, 5, 14.3m, _today, 9, Ago(320), Steps(300, 200, 180, 165)),
            new("RT-24813", C(14), Col("COL-0515"), PickupStatus.Collected, PickupSource.App, PickupMethod.Itemised, 3, 8, 1, 3.4m, _today, 9, Ago(280), Steps(260, 150, 130, 115)),
            new("RT-24815", C(15), null, PickupStatus.Cancelled, PickupSource.App, PickupMethod.Bulk, 5, null, 2, null, _today, 11, Ago(240), Steps(190),
                CancelReason: "Customer cancelled in the app."),
        ]);

        // History: RT-24789 downwards, 13 days back.
        var active = collectors.Where(c => c.Status == AccountStatus.Active).ToList();
        string[] cancelReasons = ["Customer cancelled in the app.", "Customer asked the call centre to cancel."];
        var number = 24789;
        for (var back = 1; back <= 13; back++)
        {
            var date = _today.AddDays(-back);
            var count = _random.Int(6, 13);
            for (var i = 0; i < count; i++)
            {
                var startHour = _random.Int(8, 17);
                var created = Colombo.At(date, Math.Max(6, startHour - 3), _random.Int(0, 59));
                var cancelled = _random.Chance(0.08);
                var kg = _random.Kg(2, 16);
                var estimate = Math.Max(1, SeedRandom.JsRound((double)kg + (_random.Next() - 0.5) * 3));
                var itemised = _random.Chance(0.4);
                DateTime T(int minutes) => created.AddMinutes(minutes);

                var customer = _random.Pick(customers);
                var collector = cancelled ? null : _random.Pick(active);
                var status = cancelled ? PickupStatus.Cancelled
                    : back == 1 && _random.Chance(0.2) ? PickupStatus.Collected : PickupStatus.DeliveredToHub;
                var source = _random.Chance(0.25) ? PickupSource.CallCentre : PickupSource.App;
                int? itemCount = itemised ? _random.Int(5, 20) : null;
                var bagCount = _random.Int(1, 5);
                var steps = cancelled
                    ? [T(_random.Int(20, 120))]
                    : new[] { T(15), T(160), T(185), T(200), T(200 + _random.Int(120, 400)) };
                var reason = cancelled ? _random.Pick(cancelReasons) : null;

                drafts.Add(new Draft($"RT-{number--}", customer, collector, status, source,
                    itemised ? PickupMethod.Itemised : PickupMethod.Bulk, estimate, itemCount, bagCount,
                    cancelled ? null : kg, date, startHour, created, steps, CancelReason: reason));
            }
        }

        return drafts.Select(d => Build(d, rates)).ToList();
    }

    private static readonly (string Name, ItemCondition Condition)[] BulkBags =
        [("T-shirts & shirts", ItemCondition.Good), ("Trousers & denim", ItemCondition.Fair), ("Bedsheets & towels", ItemCondition.Worn)];

    private static readonly string[] ItemNames = ["Shirt", "T-shirt", "Trousers", "Saree", "Dress", "Kids wear"];
    private static readonly string[] ItemSizes = ["S", "M", "L", "XL"];
    private static readonly ItemCondition[] Conditions = [ItemCondition.Good, ItemCondition.Fair, ItemCondition.Worn];

    private Pickup Build(Draft d, RateSet rates)
    {
        var events = new List<PickupEvent>
        {
            new() { PickupId = d.Id, Status = PickupStatus.Pending, OccurredAt = d.CreatedAt, Actor = d.Source == PickupSource.CallCentre ? "Call centre" : null },
        };
        if (d.Status == PickupStatus.Cancelled)
        {
            events.Add(new PickupEvent
            {
                PickupId = d.Id,
                Status = PickupStatus.Cancelled,
                OccurredAt = d.StepTimes.Length > 0 ? d.StepTimes[0] : d.CreatedAt,
                Actor = d.CancelReason?.StartsWith("Customer", StringComparison.Ordinal) == true ? null : "Call centre",
            });
        }
        else
        {
            var reached = Array.IndexOf(Flow, d.Status);
            for (var i = 1; i <= reached; i++)
            {
                events.Add(new PickupEvent
                {
                    PickupId = d.Id,
                    Status = Flow[i],
                    OccurredAt = d.StepTimes[i - 1],
                    Actor = d.Collector?.FullName,
                });
            }
        }

        var items = new List<PickupItem>();
        if (d.Method == PickupMethod.Bulk)
        {
            for (var i = 0; i < Math.Min(d.BagCount, 3); i++)
            {
                items.Add(new PickupItem
                {
                    Id = $"itm_{d.Id[3..]}_{i + 1}",
                    PickupId = d.Id,
                    Name = BulkBags[i].Name,
                    BagLabel = $"Bag {i + 1}",
                    Condition = BulkBags[i].Condition,
                    SortOrder = i,
                });
            }
        }
        else
        {
            var left = d.ItemCount ?? 4;
            while (left > 0)
            {
                var quantity = Math.Min(left, _random.Int(1, 4));
                var name = _random.Pick(ItemNames);
                var size = _random.Pick(ItemSizes);
                var condition = _random.Pick(Conditions);
                items.Add(new PickupItem
                {
                    Id = $"itm_{d.Id[3..]}_{items.Count + 1}",
                    PickupId = d.Id,
                    Name = name,
                    Size = size,
                    Quantity = quantity,
                    Condition = condition,
                    SortOrder = items.Count,
                });
                left -= quantity;
            }
        }

        var address = d.Customer.Addresses[0];
        var weighed = d.ActualWeightKg;
        DateTime? At(PickupStatus status) => events.FirstOrDefault(e => e.Status == status)?.OccurredAt;
        return new Pickup
        {
            Id = d.Id,
            CustomerId = d.Customer.Id,
            CollectorId = d.Collector?.Id,
            Status = d.Status,
            Source = d.Source,
            Method = d.Method,
            BulkMode = d.Method == PickupMethod.Bulk ? BulkMode.Weight : null,
            DeclaredWeightKg = d.Method == PickupMethod.Bulk ? d.EstimatedKg : null,
            EstimatedKg = d.EstimatedKg,
            ItemCount = d.ItemCount,
            BagCount = d.BagCount,
            ActualWeightKg = weighed,
            WeightSource = weighed is null ? null : WeightSource.Scale,
            ScheduledDate = d.Date,
            Slot = d.StartHour < 12 ? Slot.Morning : d.StartHour < 16 ? Slot.Afternoon : Slot.Evening,
            WindowStart = Colombo.At(d.Date, d.StartHour),
            WindowEnd = Colombo.At(d.Date, d.StartHour + 1),
            AddressId = address.Id,
            AddressLine = address.Line,
            Area = address.Area,
            Latitude = address.Latitude,
            Longitude = address.Longitude,
            Note = d.Note,
            CustomerRatePerKgCents = rates.CustomerRatePerKgCents,
            CollectorRatePerKgCents = rates.CollectorRatePerKgCents,
            CustomerPayoutCents = weighed is null ? null : Money.Payout(weighed.Value, rates.CustomerRatePerKgCents),
            CollectorPayoutCents = weighed is null ? null : Money.Payout(weighed.Value, rates.CollectorRatePerKgCents),
            HubId = d.Status == PickupStatus.DeliveredToHub ? d.Collector?.HubId ?? "hub_plg" : null,
            CancelReason = d.CancelReason,
            CreatedAt = d.CreatedAt,
            CollectedAt = At(PickupStatus.Collected),
            DeliveredAt = At(PickupStatus.DeliveredToHub),
            Items = items,
            Events = events,
        };
    }

    private static List<HubDelivery> HubDeliveries(List<Pickup> pickups, List<Collector> collectors, Hub[] hubs)
    {
        var deliveries = new List<HubDelivery>();
        foreach (var pickup in pickups.Where(p => p.Status == PickupStatus.DeliveredToHub).OrderBy(p => p.DeliveredAt))
        {
            var collector = collectors.Single(c => c.Id == pickup.CollectorId);
            var hub = hubs.Single(h => h.Id == pickup.HubId);
            var receipt = HubReceipts.Number(hub.Code, pickup.DeliveredAt!.Value, collector.Code);
            var id = receipt;
            for (var n = 2; deliveries.Any(x => x.Id == id); n++) id = $"{receipt}-{n}";
            pickup.HubDeliveryId = id;
            deliveries.Add(new HubDelivery
            {
                Id = id,
                HubId = hub.Id,
                CollectorId = collector.Id,
                JobCount = 1,
                WeightKg = pickup.ActualWeightKg ?? 0,
                CreatedAt = pickup.DeliveredAt.Value,
            });
        }

        return deliveries;
    }

    // ── Shop ─────────────────────────────────────────────────────────────────────────────────────────

    private List<Product> Products() =>
        SeedRows.Products.Select((row, i) =>
        {
            var randomDaysAgo = _random.Int(0, 30);
            var created = i < 2
                ? _now.AddMinutes(-(i + 1) * 20 * Minute)
                : _now.AddDays(-Math.Max(1, randomDaysAgo)).AddMinutes(-i);
            return new Product
            {
                Id = $"prd_{i + 1}",
                Name = row.Name,
                Category = row.Category,
                Sizes = [.. row.Sizes],
                PriceCents = row.Rupees * 100,
                Stock = row.Stock,
                Published = i != 8,
                Story = row.Story,
                CreatedAt = created,
                UpdatedAt = created,
            };
        }).ToList();

    private List<Order> Orders(List<Customer> customers, List<Product> products, Vendor[] vendors, RateSet rates)
    {
        (int DaysAgo, int MinutesAgo, FulfilmentType Type, int? Progress)[] plan =
        [
            (0, 14, FulfilmentType.HomeDelivery, 0), (0, 48, FulfilmentType.ClickAndCollect, 0),
            (0, 130, FulfilmentType.HomeDelivery, 1), (1, 0, FulfilmentType.HomeDelivery, 2),
            (1, 0, FulfilmentType.ClickAndCollect, 2), (2, 0, FulfilmentType.HomeDelivery, 3),
            (2, 0, FulfilmentType.ClickAndCollect, null), (3, 0, FulfilmentType.ClickAndCollect, 3),
            (4, 0, FulfilmentType.HomeDelivery, 3), (6, 0, FulfilmentType.HomeDelivery, 3),
            (8, 0, FulfilmentType.ClickAndCollect, 3), (9, 0, FulfilmentType.HomeDelivery, 3),
        ];
        OrderStatus[] deliveryFlow = [OrderStatus.Placed, OrderStatus.Confirmed, OrderStatus.OutForDelivery, OrderStatus.Completed];
        OrderStatus[] collectFlow = [OrderStatus.Placed, OrderStatus.Confirmed, OrderStatus.ReadyAtVendor, OrderStatus.Completed];
        var published = products.Where(p => p.Published).ToList();

        var number = 5134;
        var waybill = 12;
        var orders = new List<Order>();
        foreach (var (daysAgo, minutesAgo, type, progress) in plan)
        {
            var customer = _random.Pick(customers);
            var lineCount = _random.Int(1, 3);
            var id = $"ORD-{number--}";
            var lines = new List<OrderItem>();
            for (var l = 0; l < lineCount; l++)
            {
                var product = _random.Pick(published);
                lines.Add(new OrderItem
                {
                    OrderId = id,
                    ProductId = product.Id,
                    Name = product.Name,
                    Size = _random.Pick(product.Sizes),
                    Quantity = _random.Chance(0.8) ? 1 : 2,
                    UnitPriceCents = product.PriceCents,
                });
            }

            var subtotal = lines.Sum(l => l.UnitPriceCents * l.Quantity);
            var transport = type == FulfilmentType.HomeDelivery ? rates.HomeDeliveryChargeCents : rates.ClickAndCollectChargeCents;
            var vendor = type == FulfilmentType.ClickAndCollect ? _random.Pick(vendors) : null;
            var created = daysAgo == 0
                ? Ago(minutesAgo)
                : Colombo.At(_today.AddDays(-daysAgo), _random.Int(8, 20), _random.Int(0, 59));

            var flow = type == FulfilmentType.HomeDelivery ? deliveryFlow : collectFlow;
            var reached = progress ?? 0;
            var events = flow.Take(reached + 1).Select((status, i) => new OrderEvent
            {
                OrderId = id,
                Status = status,
                // 18 hours a step, but never in the future.
                OccurredAt = Min(created.AddHours(18 * i), _now.AddMinutes(-(reached - i))),
                Actor = i == 0 ? null : "Kasun Jayawardena",
            }).ToList();
            if (progress is null)
            {
                events.Add(new OrderEvent { OrderId = id, Status = OrderStatus.Cancelled, OccurredAt = created.AddMinutes(90) });
            }

            var payment = _random.Chance(0.6) ? PaymentMethod.Wallet : PaymentMethod.Card;
            var dateCode = Colombo.DateOf(created).ToString("yyMMdd", System.Globalization.CultureInfo.InvariantCulture);
            orders.Add(new Order
            {
                Id = id,
                CustomerId = customer.Id,
                FulfilmentType = type,
                Destination = vendor?.Address ?? customer.Addresses[0].Line,
                AddressId = vendor is null ? customer.Addresses[0].Id : null,
                VendorId = vendor?.Id,
                PaymentMethod = payment,
                PaymentStatus = progress is null ? PaymentStatus.Refunded : PaymentStatus.Paid,
                SubtotalCents = subtotal,
                TransportChargeCents = transport,
                TotalCents = subtotal + transport,
                Status = events[^1].Status,
                WaybillNumber = reached >= 1 && progress is not null ? $"WB-{dateCode}-{waybill++:0000}" : null,
                CreatedAt = created,
                EstimatedAt = created.AddDays(type == FulfilmentType.HomeDelivery ? 3 : 2),
                Items = lines,
                Events = events,
            });
        }

        return orders;
    }

    private static DateTime Min(DateTime a, DateTime b) => a < b ? a : b;

    // ── Wallet ───────────────────────────────────────────────────────────────────────────────────────

    private List<Redemption> Redemptions(List<Customer> customers, List<Pickup> pickups)
    {
        long Earned(string id) => pickups.Where(p => p.CustomerId == id).Sum(p => p.CustomerPayoutCents ?? 0);
        var rich = customers.Where(c => c.Status == AccountStatus.Active).OrderByDescending(c => Earned(c.Id)).ToList();
        Customer C(int n) => rich[n % rich.Count];
        DateTime HoursAgo(double h) => _now.AddHours(-h);

        Redemption Cash(string id, int who, int bank, long amount, double hoursAgo) => new()
        {
            Id = id,
            CustomerId = C(who).Id,
            Type = RedemptionType.Cash,
            Status = RedemptionStatus.Pending,
            AmountCents = amount,
            BankName = SeedRows.Banks[bank % 4].Bank,
            Branch = SeedRows.Banks[bank % 4].Branch,
            AccountName = C(who).FullName,
            AccountNumberProtected = bankAccounts.Protect((8_001_234_000L + bank * 7_919L).ToString(System.Globalization.CultureInfo.InvariantCulture)),
            RequestedAt = HoursAgo(hoursAgo),
        };

        var paid = Cash("red_4", 3, 4, 50_000, 80);
        paid.Status = RedemptionStatus.Paid;
        paid.ProcessedAt = HoursAgo(70);
        paid.ProcessedBy = "Nadeesha Perera";
        paid.Reference = "CEFT 1103-55120";
        var rejected = Cash("red_5", 4, 5, 60_000, 120);
        rejected.Status = RedemptionStatus.Rejected;
        rejected.ProcessedAt = HoursAgo(110);
        rejected.ProcessedBy = "Kasun Jayawardena";
        rejected.RejectReason = "Account name doesn't match the customer.";

        Redemption Voucher(string id, int who, long amount, string brandId, string brand, string code, double h) => new()
        {
            Id = id,
            CustomerId = C(who).Id,
            Type = RedemptionType.Voucher,
            Status = RedemptionStatus.Completed,
            AmountCents = amount,
            VoucherBrandId = brandId,
            VoucherBrandName = brand,
            VoucherCode = code,
            RequestedAt = HoursAgo(h),
            ProcessedAt = HoursAgo(h),
        };
        Redemption Donation(string id, int who, long amount, string project, double h) => new()
        {
            Id = id,
            CustomerId = C(who).Id,
            Type = RedemptionType.Donation,
            Status = RedemptionStatus.Completed,
            AmountCents = amount,
            ProjectId = project,
            RequestedAt = HoursAgo(h),
            ProcessedAt = HoursAgo(h),
        };

        return
        [
            Cash("red_1", 0, 1, 200_000, 26), Cash("red_2", 1, 2, 75_000, 9), Cash("red_3", 2, 3, 120_000, 2),
            paid, rejected,
            Voucher("red_6", 1, 100_000, "vb_lanka_fresh", "Lanka Fresh Supermarkets", "LF-7KQ2-4821", 30),
            Voucher("red_7", 5, 50_000, "vb_odel", "Odel", "OD-2MX9-1130", 55),
            Donation("red_8", 2, 50_000, "dp_monaragala", 5), Donation("red_9", 6, 25_000, "dp_jaffna_library", 48),
        ];
    }

    private List<BankDetails> BankDetailsFor(List<Redemption> redemptions) =>
        redemptions.Where(r => r.Type == RedemptionType.Cash)
            .GroupBy(r => r.CustomerId)
            .Select(g => g.OrderByDescending(r => r.RequestedAt).First())
            .Select(r =>
            {
                var number = bankAccounts.Unprotect(r.AccountNumberProtected!);
                return new BankDetails
                {
                    CustomerId = r.CustomerId,
                    BankName = r.BankName!,
                    Branch = r.Branch!,
                    AccountName = r.AccountName!,
                    AccountNumberProtected = r.AccountNumberProtected!,
                    AccountLast4 = BankAccountProtector.Last4(number),
                    UpdatedAt = r.RequestedAt,
                };
            }).ToList();

    private static List<WalletTransaction> Ledger(List<Pickup> pickups, List<Redemption> redemptions,
        List<Order> orders, IEnumerable<DonationProject> projects)
    {
        var projectNames = projects.ToDictionary(p => p.Id, p => p.Name);
        var ledger = new List<WalletTransaction>();
        foreach (var p in pickups.Where(p => p.CustomerPayoutCents is not null))
        {
            ledger.Add(new WalletTransaction
            {
                Id = $"txn_{p.Id}",
                CustomerId = p.CustomerId,
                Type = TransactionType.PickupEarning,
                Status = TransactionStatus.Completed,
                Title = $"Pickup #{p.Id}",
                AmountCents = p.CustomerPayoutCents!.Value,
                OccurredAt = p.CollectedAt!.Value,
                PickupId = p.Id,
                WeightKg = p.ActualWeightKg,
                RatePerKgCents = p.CustomerRatePerKgCents,
            });
        }

        foreach (var r in redemptions)
        {
            ledger.Add(new WalletTransaction
            {
                Id = $"txn_{r.Id}",
                CustomerId = r.CustomerId,
                RedemptionId = r.Id,
                AmountCents = -r.AmountCents,
                OccurredAt = r.RequestedAt,
                Type = r.Type switch
                {
                    RedemptionType.Cash => TransactionType.BankTransfer,
                    RedemptionType.Voucher => TransactionType.Voucher,
                    _ => TransactionType.Donation,
                },
                Status = r.Status switch
                {
                    RedemptionStatus.Pending => TransactionStatus.Pending,
                    RedemptionStatus.Rejected => TransactionStatus.Failed,
                    _ => TransactionStatus.Completed,
                },
                Title = r.Type switch
                {
                    RedemptionType.Cash => "Bank transfer",
                    RedemptionType.Voucher => $"Voucher · {r.VoucherBrandName}",
                    _ => $"Donation · {projectNames[r.ProjectId!]}",
                },
            });
        }

        // Wallet-paid orders, oldest first, while the customer can afford them; anything else was by card.
        var balances = ledger.Where(t => t.Status != TransactionStatus.Failed)
            .GroupBy(t => t.CustomerId).ToDictionary(g => g.Key, g => g.Sum(t => t.AmountCents));
        foreach (var o in orders.Where(o => o.PaymentMethod == PaymentMethod.Wallet).OrderBy(o => o.CreatedAt))
        {
            var balance = balances.GetValueOrDefault(o.CustomerId);
            if (balance < o.TotalCents)
            {
                o.PaymentMethod = PaymentMethod.Card;
                continue;
            }

            ledger.Add(new WalletTransaction
            {
                Id = $"txn_{o.Id}",
                CustomerId = o.CustomerId,
                Type = TransactionType.ShopOrder,
                Status = TransactionStatus.Completed,
                Title = $"Order {o.Id}",
                AmountCents = -o.TotalCents,
                OccurredAt = o.CreatedAt,
                OrderId = o.Id,
            });
            balances[o.CustomerId] = balance - o.TotalCents;
            if (o.Status == OrderStatus.Cancelled)
            {
                ledger.Add(new WalletTransaction
                {
                    Id = $"txn_{o.Id}_refund",
                    CustomerId = o.CustomerId,
                    Type = TransactionType.ShopOrder,
                    Status = TransactionStatus.Completed,
                    Title = $"Refund for {o.Id}",
                    AmountCents = o.TotalCents,
                    OccurredAt = o.Events[^1].OccurredAt,
                    OrderId = o.Id,
                });
                balances[o.CustomerId] += o.TotalCents;
            }
        }

        return ledger;
    }

    // ── Notifications ────────────────────────────────────────────────────────────────────────────────

    /// <summary>The alerts the server would have sent over the last two days.</summary>
    private List<Notification> Notifications(List<Pickup> pickups, List<Order> orders,
        List<Redemption> redemptions, List<Customer> customers, List<Collector> collectors, Hub[] hubs)
    {
        var since = _now.AddDays(-2);
        bool Recent(DateTime at) => at >= since && at <= _now;
        var list = new List<Notification>();

        void Staff(string type, string subject, (string Title, string Body) text, DateTime at, string targetType) =>
            list.Add(new Notification
            {
                Id = NotificationService.StaffId(type, subject),
                Audience = Audience.Staff,
                Type = type,
                Title = text.Title,
                Body = text.Body,
                TargetType = targetType,
                TargetId = subject,
                CreatedAt = at,
            });

        void Person(Audience audience, string recipient, string type, (string Title, string Body) text,
            string? targetType, string? targetId, DateTime at) =>
            list.Add(new Notification
            {
                Id = $"ntf_{type}_{targetId ?? recipient}_{list.Count + 1}",
                Audience = audience,
                RecipientId = recipient,
                Type = type,
                Title = text.Title,
                Body = text.Body,
                TargetType = targetType,
                TargetId = targetId,
                CreatedAt = at,
                // Anything older than today has been seen.
                ReadAt = at < _dayStart ? at.AddMinutes(30) : null,
            });

        foreach (var p in pickups)
        {
            if (Recent(p.CreatedAt) && p.Source == PickupSource.App)
                Staff(NotificationService.StaffTypes.PickupRequested, p.Id, NotificationTexts.PickupRequested(p), p.CreatedAt, "pickup");
            var waitingSince = p.CreatedAt.AddMinutes(30);
            if (p.Status == PickupStatus.Pending && waitingSince <= _now && waitingSince >= since)
                Staff(NotificationService.StaffTypes.PickupWaiting, p.Id, NotificationTexts.PickupWaiting(p), waitingSince, "pickup");

            var collector = collectors.FirstOrDefault(c => c.Id == p.CollectorId);
            foreach (var e in p.Events.Where(e => Recent(e.OccurredAt)))
            {
                switch (e.Status)
                {
                    case PickupStatus.EnRoute when collector is not null:
                        Person(Audience.Customer, p.CustomerId, NotificationService.CustomerTypes.CollectorEnRoute,
                            NotificationTexts.CollectorEnRoute(p, collector.FullName), "pickup", p.Id, e.OccurredAt);
                        break;
                    case PickupStatus.Collected:
                        Person(Audience.Customer, p.CustomerId, NotificationService.CustomerTypes.WeightUpdated,
                            NotificationTexts.WeightUpdated(p), "pickup", p.Id, e.OccurredAt);
                        Person(Audience.Customer, p.CustomerId, NotificationService.CustomerTypes.PaymentCredited,
                            NotificationTexts.PaymentCredited(p), "wallet", null, e.OccurredAt);
                        break;
                    case PickupStatus.DeliveredToHub:
                        Person(Audience.Customer, p.CustomerId, NotificationService.CustomerTypes.HubArrival,
                            NotificationTexts.HubArrival(p, hubs.Single(h => h.Id == p.HubId).Name), "pickup", p.Id, e.OccurredAt);
                        break;
                }
            }
        }

        foreach (var o in orders.Where(o => Recent(o.CreatedAt)))
        {
            var firstName = customers.Single(c => c.Id == o.CustomerId).FirstName;
            Staff(NotificationService.StaffTypes.OrderPlaced, o.Id, NotificationTexts.OrderPlaced(o, firstName), o.CreatedAt, "order");
        }

        foreach (var r in redemptions.Where(r => r.Type == RedemptionType.Cash && Recent(r.RequestedAt)))
        {
            var name = customers.Single(c => c.Id == r.CustomerId).FullName;
            Staff(NotificationService.StaffTypes.TransferRequested, r.Id, NotificationTexts.TransferRequested(r, name), r.RequestedAt, "redemption");
        }

        // The collector app's sample inbox for COL-0457.
        var ruwan = collectors.Single(c => c.Code == "COL-0457");
        var load = pickups.Where(p => p.CollectorId == ruwan.Id && p.Status == PickupStatus.Collected).ToList();
        var hub = hubs.Single(h => h.Id == ruwan.HubId);
        foreach (var p in pickups.Where(p => p.Status == PickupStatus.Pending && p.ScheduledDate == _today))
        {
            var km = Geo.DistanceKm(ruwan.Latitude!.Value, ruwan.Longitude!.Value, p.Latitude, p.Longitude);
            if (km <= ruwan.RadiusKm)
                Person(Audience.Collector, ruwan.Id, NotificationService.CollectorTypes.NewJob, NotificationTexts.NewJob(p, km), "job", p.Id, p.CreatedAt);
        }

        Person(Audience.Collector, ruwan.Id, NotificationService.CollectorTypes.HubReminder,
            NotificationTexts.HubReminder(hub, load.Sum(p => p.ActualWeightKg ?? 0), load.Count), "load", null, Ago(30));
        var weekStart = Colombo.At(Colombo.StartOfWeek(_today), 9);
        if (weekStart < _now)
        {
            var lastWeek = pickups.Where(p => p.CollectorId == ruwan.Id && p.CollectedAt >= weekStart.AddDays(-7) && p.CollectedAt < weekStart)
                .Sum(p => p.CollectorPayoutCents ?? 0);
            Person(Audience.Collector, ruwan.Id, NotificationService.CollectorTypes.Payout, NotificationTexts.Payout(lastWeek), "earnings", null, weekStart);
        }

        return list;
    }
}
