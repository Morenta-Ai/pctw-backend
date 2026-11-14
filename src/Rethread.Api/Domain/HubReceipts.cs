using Rethread.Api.Common;

namespace Rethread.Api.Domain;

/// <summary>Hub receipt numbers: <c>HUB-PLG-071126-0457</c> (hub, Colombo date, collector).</summary>
public static class HubReceipts
{
    public static string Number(string hubCode, DateTime deliveredAt, string collectorCode) =>
        $"HUB-{hubCode}-{Colombo.DateOf(deliveredAt).ToString("ddMMyy", System.Globalization.CultureInfo.InvariantCulture)}-{collectorCode[4..]}";
}
