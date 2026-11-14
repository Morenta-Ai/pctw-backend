using System.Globalization;
using Rethread.Api.Common;
using Rethread.Api.Data.Entities;

namespace Rethread.Api.Domain;

/// <summary>
/// Server-written notification text. English for now; <c>Accept-Language</c> is read by the clients'
/// requests, so Sinhala and Tamil can be added here without touching the callers.
/// </summary>
public static class NotificationTexts
{
    /// <summary><c>9.5</c>, <c>12</c>: how the portal's alerts write an estimate.</summary>
    public static string Kg(decimal kg) => kg.ToString("0.##", CultureInfo.InvariantCulture);

    public static (string Title, string Body) PickupRequested(Pickup p) =>
        ($"New pickup {p.Id}", $"{p.Area} · about {Kg(p.EstimatedKg)} kg");

    public static (string Title, string Body) PickupWaiting(Pickup p) =>
        ($"{p.Id} has waited 30 minutes", $"{p.Area} · about {Kg(p.EstimatedKg)} kg · no collector yet");

    public static (string Title, string Body) OfferDeclined(Assignment a, string collectorName) =>
        a.Status == AssignmentStatus.Declined
            ? ($"{collectorName} declined {a.PickupId}",
                $"Reason: {a.DeclineReason ?? "none given"}. Offer it to someone else.")
            : ($"No answer on {a.PickupId}",
                $"{collectorName} didn't answer in 45 seconds. Offer it to someone else.");

    public static (string Title, string Body) OrderPlaced(Order o, string? customerFirstName) =>
        ($"New order {o.Id}",
            $"{customerFirstName ?? "A customer"} · {(o.FulfilmentType == FulfilmentType.HomeDelivery ? "home delivery" : "click & collect")}");

    public static (string Title, string Body) TransferRequested(Redemption r, string? customerName) =>
        ("Bank transfer requested", $"{(string.IsNullOrWhiteSpace(customerName) ? "A customer" : customerName)} · {Money.Format(r.AmountCents)}");

    public static (string Title, string Body) WeightUpdated(Pickup p) =>
        ("Weight updated by collector",
            $"Declared {Weight.Format(p.EstimatedKg)} → Actual {Weight.Format(p.ActualWeightKg ?? 0)}. Earnings {Money.Format(p.CustomerPayoutCents ?? 0)}.");

    public static (string Title, string Body) PaymentCredited(Pickup p) =>
        ("Payment credited", $"{Money.Format(p.CustomerPayoutCents ?? 0)} added to your wallet for {p.Id}.");

    public static (string Title, string Body) CollectorEnRoute(Pickup p, string collectorName) =>
        ("Collector on the way", $"{collectorName} is coming for {p.Id}.");

    public static (string Title, string Body) HubArrival(Pickup p, string hubName) =>
        ("Your clothes reached the hub", $"{p.Id} arrived at {hubName}. Thank you for recycling!");

    public static (string Title, string Body) OrderUpdate(Order o) => o.Status switch
    {
        OrderStatus.Confirmed => ($"Order {o.Id} confirmed", "We're packing your order."),
        OrderStatus.OutForDelivery => ($"Order {o.Id} is on its way", "Your order is out for delivery."),
        OrderStatus.ReadyAtVendor => ($"Order {o.Id} is ready", $"Collect it from {o.Destination}."),
        OrderStatus.Completed => ($"Order {o.Id} completed", "Enjoy your rejuvenated clothes!"),
        OrderStatus.Cancelled => ($"Order {o.Id} cancelled",
            o.PaymentStatus == PaymentStatus.Refunded
                ? $"{Money.Format(o.TotalCents)} is back in your wallet."
                : "Your order was cancelled."),
        _ => ($"Order {o.Id} placed", "We'll let you know when it's confirmed."),
    };

    public static (string Title, string Body) DonationThanks(long amountCents, string projectName) =>
        ("Thank you for donating", $"{Money.Format(amountCents)} to {projectName}.");

    public static (string Title, string Body) TransferPaid(Redemption r) =>
        ("Bank transfer sent", $"{Money.Format(r.AmountCents)} is on its way to your bank account.");

    public static (string Title, string Body) TransferRejected(Redemption r) =>
        ("Bank transfer not sent", $"{r.RejectReason} {Money.Format(r.AmountCents)} is back in your wallet.");

    public static (string Title, string Body) NewJob(Pickup p, double distanceKm) =>
        ("New job near you",
            $"{p.Area} · {distanceKm.ToString("0.0", CultureInfo.InvariantCulture)} km · {(p.Method == PickupMethod.Bulk ? "Bulk" : "Itemised")} ~{Kg(p.EstimatedKg)} kg");

    public static (string Title, string Body) Assignment(Pickup p, double distanceKm) =>
        ("Call centre assignment",
            $"{p.Area} · {distanceKm.ToString("0.0", CultureInfo.InvariantCulture)} km · {(p.Method == PickupMethod.Bulk ? "Bulk" : "Itemised")} ~{Kg(p.EstimatedKg)} kg. Tap to answer.");

    public static (string Title, string Body) JobCancelled(Pickup p) =>
        ("Job cancelled", $"{p.Id} in {p.Area} was cancelled. You don't need to go.");

    public static (string Title, string Body) HubReminder(Hub hub, decimal loadKg, int jobCount) =>
        ($"{hub.Name} closes at {hub.ClosesAt}",
            $"You're carrying {Weight.Format(loadKg)} from {jobCount} jobs. Drop them off before closing.");

    public static (string Title, string Body) Payout(long amountCents) =>
        ("Weekly payout sent", $"{Money.Format(amountCents)} sent to your bank account.");
}
