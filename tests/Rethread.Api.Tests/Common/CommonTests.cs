using System.Text.Json;
using Rethread.Api.Common;

namespace Rethread.Api.Tests.Common;

public sealed class CommonTests
{
    private enum Sample { EnRoute, ClickAndCollect }

    private sealed record Payload(DateTime At, DateTime? Maybe, Sample Status, DateOnly Day);

    [Fact]
    public void Json_uses_camel_case_utc_z_times_and_camel_case_enums()
    {
        var json = JsonSerializer.Serialize(
            new Payload(new DateTime(2026, 11, 7, 4, 30, 0, 123, DateTimeKind.Utc), null, Sample.ClickAndCollect,
                new DateOnly(2026, 11, 7)), Json.Options);
        Assert.Equal(
            """{"at":"2026-11-07T04:30:00Z","maybe":null,"status":"clickAndCollect","day":"2026-11-07"}""", json);

        var back = JsonSerializer.Deserialize<Payload>(
            """{"at":"2026-11-07T10:00:00+05:30","maybe":"2026-11-07T04:30:00Z","status":"enRoute","day":"2026-11-07"}""",
            Json.Options)!;
        Assert.Equal(new DateTime(2026, 11, 7, 4, 30, 0, DateTimeKind.Utc), back.At);
        Assert.Equal(DateTimeKind.Utc, back.At.Kind);
        Assert.Equal(Sample.EnRoute, back.Status);
    }

    [Fact]
    public void Colombo_days_turn_at_local_midnight()
    {
        Assert.Equal(new DateOnly(2026, 11, 7), Colombo.DateOf(new DateTime(2026, 11, 6, 18, 30, 0, DateTimeKind.Utc)));
        Assert.Equal(new DateOnly(2026, 11, 6), Colombo.DateOf(new DateTime(2026, 11, 6, 18, 29, 59, DateTimeKind.Utc)));
        Assert.Equal(new DateTime(2026, 11, 7, 2, 30, 0, DateTimeKind.Utc), Colombo.At(new DateOnly(2026, 11, 7), 8));
        Assert.Equal(new DateOnly(2026, 11, 2), Colombo.StartOfWeek(new DateOnly(2026, 11, 8)));
        Assert.Equal(new DateOnly(2026, 11, 2), Colombo.StartOfWeek(new DateOnly(2026, 11, 2)));
    }

    [Theory]
    [InlineData("7.4", 12000, 88800)]
    [InlineData("0.01", 4000, 40)]
    [InlineData("2.345", 100, 235)]
    public void Payout_is_weight_times_rate_in_whole_cents(string kg, long rate, long expected) =>
        Assert.Equal(expected, Money.Payout(decimal.Parse(kg, System.Globalization.CultureInfo.InvariantCulture), rate));

    [Fact]
    public void Money_and_weight_format_for_server_text()
    {
        Assert.Equal("Rs. 4,860.00", Money.Format(486000));
        Assert.Equal("7.4 kg", Weight.Format(7.4m));
    }

    [Fact]
    public void Distance_is_a_road_estimate_with_an_eta()
    {
        // Dehiwala to Wellawatte, about 2.6 km in a straight line.
        var km = Geo.DistanceKm(6.8511, 79.8655, 6.8742, 79.8606);
        Assert.InRange(km, 3.0, 4.0);
        Assert.Equal(11, Geo.EtaMinutes(3.6));
        Assert.Equal(1, Geo.EtaMinutes(0));
    }

    [Fact]
    public void Cursors_round_trip_and_reject_garbage()
    {
        var at = new DateTime(2026, 11, 7, 4, 30, 0, DateTimeKind.Utc).AddTicks(1230);
        Assert.Equal((at, "RT-24817"), Cursor.Decode(Cursor.Encode(at, "RT-24817")));
        Assert.Null(Cursor.Decode(null));
        var error = Assert.Throws<ApiException>(() => Cursor.Decode("not a cursor"));
        Assert.Equal(Cursor.InvalidCursor, error.Code);
    }

    [Fact]
    public void Validation_field_names_match_json()
    {
        Assert.Equal("vehicle.plate", ValidationFilter<object>.FieldName("Vehicle.Plate"));
        Assert.Equal("items[0].quantity", ValidationFilter<object>.FieldName("Items[0].Quantity"));
    }

    [Fact]
    public void Page_requests_are_clamped()
    {
        Assert.Equal(new PageRequest(1, 25), PageRequest.From(null, null));
        Assert.Equal(new PageRequest(1, 100), PageRequest.From(0, 500));
        Assert.Equal(50, PageRequest.From(3, 25).Skip);
    }
}
