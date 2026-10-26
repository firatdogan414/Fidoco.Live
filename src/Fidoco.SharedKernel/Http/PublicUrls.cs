using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace Fidoco.SharedKernel.Http;

public sealed class PublicUrlOptions
{
    public const string SectionName = "PublicUrls";

    /// <summary>Absolute URL template for a host. Production: "https://{host}". Development: "http://{host}:5173".</summary>
    public string Template { get; set; } = "https://{host}";
}

/// <summary>Builds absolute links (emails, Stripe return URLs) for a tenant host.</summary>
public sealed class PublicUrls(IOptions<PublicUrlOptions> options)
{
    public string For(string host, string pathAndQuery)
    {
        var baseUrl = options.Value.Template.Replace("{host}", host, StringComparison.Ordinal).TrimEnd('/');
        return baseUrl + (pathAndQuery.StartsWith('/') ? pathAndQuery : "/" + pathAndQuery);
    }

    /// <summary>Uses the host the request came in on (keeps custom domains intact).</summary>
    public string For(HttpRequest request, string pathAndQuery) => For(request.Host.Host, pathAndQuery);

    /// <summary>Accepts only local paths ("/e/afn-12") to avoid open redirects.</summary>
    public static string? SafeReturnPath(string? path) =>
        !string.IsNullOrEmpty(path) && path.StartsWith('/') && !path.StartsWith("//", StringComparison.Ordinal) && !path.Contains('\\', StringComparison.Ordinal) && path.Length <= 500
            ? path
            : null;
}
