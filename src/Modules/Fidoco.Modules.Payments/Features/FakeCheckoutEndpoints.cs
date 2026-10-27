using System.Net;
using Fidoco.Modules.Payments.Infrastructure.Gateway;
using Fidoco.SharedKernel.Http;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Fidoco.Modules.Payments.Features;

/// <summary>
/// Development only (Payments:Provider=Fake): a minimal checkout page that "pays" by running the same
/// webhook processing as Stripe would trigger.
/// </summary>
internal static class FakeCheckoutEndpoints
{
    /// <summary>Any promotion code gives 20 % off, so discounts can be tried without Stripe.</summary>
    public const decimal FakeDiscountPercent = 20m;

    public sealed record FakePayRequest(string? Email, long Amount, string? PromotionCode);

    public static void Map(IEndpointRouteBuilder api)
    {
        api.MapGet("/dev/fake-checkout/{sessionId}", (string sessionId, string success, string cancel, string? email, long amount) =>
            Results.Content(Page(sessionId, success, cancel, email, amount), "text/html; charset=utf-8"));

        api.MapPost("/dev/fake-checkout/{sessionId}/pay", async (string sessionId, FakePayRequest request, PaymentWebhookProcessor processor, CancellationToken cancellationToken) =>
        {
            var errors = new ValidationErrors().Email("email", request.Email);
            if (errors.HasErrors)
            {
                return errors.ToResult();
            }

            var code = string.IsNullOrWhiteSpace(request.PromotionCode) ? null : request.PromotionCode.Trim().ToUpperInvariant();
            var discount = code is null ? 0 : (long)Math.Round(request.Amount * FakeDiscountPercent / 100m, MidpointRounding.AwayFromZero);
            await processor.ProcessAsync(
                new PaymentWebhookEvent($"fake_evt_{Guid.NewGuid():N}", PaymentWebhookType.CheckoutCompleted, sessionId, Paid: true,
                    PaymentIntentId: $"fake_pi_{Guid.NewGuid():N}", Email: request.Email, AmountTotal: request.Amount - discount,
                    AmountDiscount: discount, PromotionCodeId: code),
                cancellationToken);
            return Results.NoContent();
        });
    }

    private static string Page(string sessionId, string success, string cancel, string? email, long amount)
    {
        string E(string? value) => WebUtility.HtmlEncode(value ?? "");
        return $$"""
            <!doctype html><html lang="sv"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
            <title>Fake checkout</title></head>
            <body style="font-family:system-ui;max-width:420px;margin:40px auto;padding:16px">
            <h1>Fake checkout (utveckling)</h1>
            <p>Belopp: {{amount / 100m:0.00}} kr</p>
            <label>E-post<br><input id="email" type="email" value="{{E(email)}}" style="width:100%;padding:8px"></label>
            <p><label>Rabattkod (valfri, alla koder ger {{FakeDiscountPercent:0}} %)<br><input id="code" style="width:100%;padding:8px"></label></p>
            <p><button id="pay" style="padding:10px 16px">Betala</button> <a href="{{E(cancel)}}">Avbryt</a></p>
            <script>
            document.getElementById('pay').onclick = async () => {
              const res = await fetch('/api/v1/dev/fake-checkout/{{E(sessionId)}}/pay', {
                method: 'POST', headers: { 'Content-Type': 'application/json', 'X-Fidoco-Client': 'web' },
                body: JSON.stringify({ email: document.getElementById('email').value, amount: {{amount}}, promotionCode: document.getElementById('code').value })
              });
              if (res.ok) location.href = {{System.Text.Json.JsonSerializer.Serialize(success)}};
              else alert('Ogiltig e-post');
            };
            </script></body></html>
            """;
    }
}
