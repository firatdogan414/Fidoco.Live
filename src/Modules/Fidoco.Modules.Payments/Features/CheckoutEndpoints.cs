using System.Security.Cryptography;
using System.Text;
using Fidoco.Contracts.Catalog;
using Fidoco.Contracts.Identity;
using Fidoco.Contracts.Tenants;
using Fidoco.Modules.Payments.Domain;
using Fidoco.Modules.Payments.Infrastructure;
using Fidoco.Modules.Payments.Infrastructure.Gateway;
using Fidoco.SharedKernel.Http;
using Fidoco.SharedKernel.Tenancy;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Fidoco.Modules.Payments.Features;

/// <summary>
/// Purchase flow: start a hosted checkout, then poll its status from the thank-you page. When the
/// browser that started the checkout returns (nonce cookie), the buyer is signed in right away.
/// </summary>
internal static class CheckoutEndpoints
{
    public const string NonceCookie = "fidoco.checkout";
    public static readonly TimeSpan DirectSignInWindow = TimeSpan.FromHours(1);

    public sealed record StartCheckoutRequest(Guid TicketTypeId);

    public sealed record StartCheckoutResponse(Guid CheckoutId, string RedirectUrl);

    public sealed record CheckoutStatusResponse(Guid CheckoutId, string Status, Guid EventId, string EventSlug, bool SignedIn);

    public static void Map(IEndpointRouteBuilder api)
    {
        api.MapPost("/checkout", Start).RequireRateLimiting(RateLimitPolicies.Checkout);
        api.MapGet("/checkout/{checkoutId:guid}", GetStatus);
    }

    private static async Task<IResult> Start(
        StartCheckoutRequest request,
        HttpContext context,
        PaymentsDbContext db,
        ICatalogQueries catalog,
        ITicketAvailability availability,
        ICustomerAccounts customers,
        IPaymentGateway gateway,
        ITenantContext tenant,
        ITenantDirectory tenants,
        PublicUrls urls,
        IOptions<PaymentsOptions> options,
        IHostEnvironment environment,
        TimeProvider time,
        ILoggerFactory loggers,
        CancellationToken cancellationToken)
    {
        var ticket = await catalog.GetPurchasableTicketAsync(request.TicketTypeId, cancellationToken);
        if (ticket is null)
        {
            return ApiResults.NotFound("ticket_not_found");
        }

        if (!ticket.OnSale)
        {
            return ApiResults.Conflict(ticket.NotOnSaleReason!);
        }

        // A signed-in buyer who already has a valid ticket for the event goes to the watch page instead.
        var customerId = context.User.GetCustomerId();
        string? email = null;
        if (customerId is not null)
        {
            var alreadyBought = await db.PurchaseLines
                .Where(l => l.EventId == ticket.EventId)
                .Join(db.Purchases, l => l.PurchaseId, p => p.Id, (l, p) => p)
                .AnyAsync(p => p.CustomerId == customerId && p.Status == PurchaseStatus.Completed, cancellationToken);
            if (alreadyBought)
            {
                return ApiResults.Conflict("already_purchased");
            }

            email = (await customers.GetAsync(customerId.Value, cancellationToken))?.Email;
        }

        var now = time.GetUtcNow();
        var expiresAt = now + options.Value.CheckoutLifetime;

        // Capacity seam: phase 1 tickets are unlimited, so this always succeeds without a reservation.
        var reservation = await availability.TryReserveAsync(ticket.TicketTypeId, quantity: 1, expiresAt, cancellationToken);
        if (!reservation.Succeeded)
        {
            return ApiResults.Conflict(reservation.FailureCode ?? "sold_out");
        }

        var terms = await tenants.GetCommercialTermsAsync(tenant.RequiredTenantId, cancellationToken);
        var nonce = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
        var checkout = new CheckoutSession
        {
            Id = Guid.CreateVersion7(now),
            CustomerId = customerId,
            BrowserNonceHash = Hash(nonce),
            ReservationId = reservation.ReservationId,
            EventId = ticket.EventId,
            EventSlug = ticket.EventSlug,
            EventTitle = ticket.EventTitle,
            TicketTypeId = ticket.TicketTypeId,
            TicketTypeName = ticket.TicketName,
            UnitPrice = ticket.PriceAmount,
            Currency = ticket.Currency,
            VatRatePercent = ticket.VatRatePercent,
            IncludesLive = ticket.IncludesLive,
            ReplayAccessDays = ticket.ReplayAccessDays,
            CommissionPercent = terms.CommissionPercent,
            CommissionFixedAmountPerTicket = terms.CommissionFixedAmountPerTicket,
            ExpiresAt = expiresAt,
            CreatedAt = now,
        };

        CheckoutCreated created;
        try
        {
            created = await gateway.CreateCheckoutAsync(
                new CheckoutRequest(
                    checkout.Id,
                    tenant.RequiredTenantId,
                    $"{ticket.EventTitle} – {ticket.TicketName}",
                    ticket.PriceAmount,
                    ticket.Currency,
                    checkout.Quantity,
                    email,
                    urls.For(context.Request, $"/e/{ticket.EventSlug}/tack?c={checkout.Id}"),
                    urls.For(context.Request, $"/e/{ticket.EventSlug}"),
                    expiresAt),
                cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            loggers.CreateLogger(typeof(CheckoutEndpoints)).LogError(ex, "Creating checkout failed");
            if (reservation.ReservationId is { } reservationId)
            {
                await availability.ReleaseAsync(reservationId, cancellationToken);
            }

            return ApiResults.Problem(StatusCodes.Status502BadGateway, "payment_provider_unavailable");
        }

        checkout.ProviderSessionId = created.ProviderSessionId;
        db.CheckoutSessions.Add(checkout);
        await db.SaveChangesAsync(cancellationToken);

        context.Response.Cookies.Append(NonceCookie, nonce, new CookieOptions
        {
            HttpOnly = true,
            // TLS usually ends at the load balancer, so the request scheme says nothing outside Development.
            Secure = !environment.IsDevelopment() || context.Request.IsHttps,
            SameSite = SameSiteMode.Lax,
            Path = "/api/v1/checkout",
            MaxAge = TimeSpan.FromHours(2),
        });
        return Results.Ok(new StartCheckoutResponse(checkout.Id, created.RedirectUrl));
    }

    private static async Task<IResult> GetStatus(
        Guid checkoutId,
        HttpContext context,
        PaymentsDbContext db,
        ICustomerSignIn signIn,
        IHostEnvironment environment,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        var checkout = await db.CheckoutSessions.SingleOrDefaultAsync(c => c.Id == checkoutId, cancellationToken);
        if (checkout is null)
        {
            return ApiResults.NotFound("checkout_not_found");
        }

        var lineIds = checkout.PurchaseId is { } purchaseId
            ? await db.PurchaseLines.Where(l => l.PurchaseId == purchaseId).Select(l => l.Id).ToListAsync(cancellationToken)
            : [];
        var signedIn = CanWatch(context.User, checkout.CustomerId, lineIds);
        var now = time.GetUtcNow();
        if (!signedIn
            && checkout.Status == CheckoutStatus.Completed
            && checkout.CustomerId is { } customerId
            && lineIds.Count > 0
            && checkout.NonceUsedAt is null
            && checkout.CompletedAt is { } completedAt && now - completedAt <= DirectSignInWindow
            && context.Request.Cookies.TryGetValue(NonceCookie, out var nonce)
            && checkout.BrowserNonceHash is not null
            && CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(Hash(nonce)), Encoding.ASCII.GetBytes(checkout.BrowserNonceHash)))
        {
            checkout.NonceUsedAt = now;
            await db.SaveChangesAsync(cancellationToken);
            // The e-mail typed on the payment page is unverified, so this browser may only watch what it paid
            // for. Account access (purchases, e-mail, erasure) needs the magic link sent to that address.
            await signIn.SignInForPurchaseAsync(context, customerId, lineIds, cancellationToken);
            context.Response.Cookies.Delete(NonceCookie, new CookieOptions
            {
                Path = "/api/v1/checkout",
                Secure = !environment.IsDevelopment() || context.Request.IsHttps,
            });
            signedIn = true;
        }

        return Results.Ok(new CheckoutStatusResponse(
            checkout.Id, checkout.Status.ToString().ToLowerInvariant(), checkout.EventId, checkout.EventSlug, signedIn));
    }

    /// <summary>The account itself, or a purchase session that covers this checkout's purchase.</summary>
    private static bool CanWatch(System.Security.Claims.ClaimsPrincipal user, Guid? checkoutCustomerId, IReadOnlyList<Guid> lineIds)
    {
        if (checkoutCustomerId is null || user.GetViewerCustomerId() != checkoutCustomerId)
        {
            return false;
        }

        var scope = user.GetPurchaseLineScope();
        return scope is null || (lineIds.Count > 0 && lineIds.All(scope.Contains));
    }

    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}
