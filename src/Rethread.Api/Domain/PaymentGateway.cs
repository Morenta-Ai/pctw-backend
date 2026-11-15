namespace Rethread.Api.Domain;

/// <summary>
/// Confirms card payments with the gateway. The app's word that a payment succeeded is never proof on its
/// own; only the gateway's record is.
/// </summary>
public interface IPaymentGateway
{
    Task<bool> HasPaymentAsync(string reference, long amountCents, CancellationToken ct);
}

/// <summary>
/// Until a gateway is chosen. Outside Production it accepts the user app's mock gateway (<c>mock_…</c>
/// references) so checkout can be tested end to end; in Production nothing is confirmed.
/// </summary>
public sealed class DevelopmentPaymentGateway(IHostEnvironment environment) : IPaymentGateway
{
    public Task<bool> HasPaymentAsync(string reference, long amountCents, CancellationToken ct) =>
        Task.FromResult(!environment.IsProduction() &&
                        (reference.StartsWith("mock_", StringComparison.Ordinal) || reference.StartsWith("pay_", StringComparison.Ordinal)));
}
