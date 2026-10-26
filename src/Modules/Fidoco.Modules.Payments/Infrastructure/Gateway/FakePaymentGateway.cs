namespace Fidoco.Modules.Payments.Infrastructure.Gateway;

/// <summary>
/// Development-only gateway: redirects to a local fake checkout page instead of Stripe, so the whole
/// purchase flow works without a Stripe account or network access.
/// </summary>
internal sealed class FakePaymentGateway : IPaymentGateway
{
    public Task<CheckoutCreated> CreateCheckoutAsync(CheckoutRequest request, CancellationToken cancellationToken)
    {
        var sessionId = $"fake_cs_{Guid.NewGuid():N}";
        var query = $"success={Uri.EscapeDataString(request.SuccessUrl)}&cancel={Uri.EscapeDataString(request.CancelUrl)}&email={Uri.EscapeDataString(request.CustomerEmail ?? "")}&amount={request.UnitAmount * request.Quantity}";
        return Task.FromResult(new CheckoutCreated(sessionId, $"/api/v1/dev/fake-checkout/{sessionId}?{query}"));
    }
}
