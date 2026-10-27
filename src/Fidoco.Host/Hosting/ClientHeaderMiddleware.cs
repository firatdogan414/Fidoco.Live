using Fidoco.SharedKernel.Http;

namespace Fidoco.Host.Hosting;

/// <summary>
/// CSRF defense in addition to SameSite=Lax cookies: state-changing API calls must carry the
/// "X-Fidoco-Client" header, which a cross-site form or image cannot send. Webhooks are exempt.
/// </summary>
internal sealed class ClientHeaderMiddleware(RequestDelegate next)
{
    public const string HeaderName = "X-Fidoco-Client";

    public Task InvokeAsync(HttpContext context)
    {
        var request = context.Request;
        var isUnsafe = !(HttpMethods.IsGet(request.Method) || HttpMethods.IsHead(request.Method) || HttpMethods.IsOptions(request.Method));
        if (isUnsafe
            && request.Path.StartsWithSegments("/api")
            && !request.Path.StartsWithSegments("/api/v1/webhooks")
            && !request.Headers.ContainsKey(HeaderName))
        {
            return ApiResults.BadRequest("missing_client_header").ExecuteAsync(context);
        }

        return next(context);
    }
}
