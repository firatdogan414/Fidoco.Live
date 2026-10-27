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

        // A non-2xx answer (or an exception) makes Stripe retry the delivery with backoff, for up to three days.
        return await processor.ProcessAsync(webhook, cancellationToken)
            ? Results.Ok()
            : ApiResults.Problem(StatusCodes.Status503ServiceUnavailable, "retry_later");
    }
}
