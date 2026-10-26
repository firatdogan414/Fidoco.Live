using System.Text;
using Fidoco.Modules.Payments.Infrastructure.Gateway;
using Fidoco.SharedKernel.Http;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Fidoco.Modules.Payments.Features;

internal static class StripeWebhookEndpoint
{
    public static void Map(IEndpointRouteBuilder api) =>
        api.MapPost("/webhooks/stripe", Handle).AllowAnonymous();

    private static async Task<IResult> Handle(
        HttpRequest request,
        IPaymentWebhookParser parser,
        PaymentWebhookProcessor processor,
        CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(request.Body, Encoding.UTF8);
        var payload = await reader.ReadToEndAsync(cancellationToken);

        PaymentWebhookEvent webhook;
        try
        {
            webhook = parser.Parse(payload, request.Headers["Stripe-Signature"].ToString());
        }
        catch (InvalidWebhookSignatureException)
        {
            return ApiResults.BadRequest("invalid_signature");
        }

        // An exception here returns 500 and Stripe retries the delivery (with backoff, for days).
        await processor.ProcessAsync(webhook, cancellationToken);
        return Results.Ok();
    }
}
