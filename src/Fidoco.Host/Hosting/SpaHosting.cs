using System.Text;
using System.Text.RegularExpressions;
using Fidoco.Contracts.Catalog;
using Fidoco.Contracts.Tenants;
using Microsoft.Extensions.Caching.Memory;

namespace Fidoco.Host.Hosting;

/// <summary>Values for the link preview of a page (Open Graph and Twitter card tags).</summary>
internal sealed record PageMeta(string Title, string? Description, string? ImageUrl, string Url, string SiteName);

/// <summary>
/// Serves the SPA's index.html and injects Open Graph tags for the requested page, so links shared on
/// social media get a title, description and image (crawlers do not run JavaScript).
/// </summary>
internal static partial class SpaHosting
{
    public const string Placeholder = "<!--fidoco:meta-->";

    public static void MapSpaFallback(this WebApplication app) =>
        app.MapFallback(async (HttpContext context, IWebHostEnvironment environment, IMemoryCache cache, ICatalogQueries catalog) =>
        {
            var template = await LoadTemplateAsync(environment, cache);
            if (template is null)
            {
                return Results.NotFound();
            }

            var meta = await BuildMetaAsync(context, catalog);
            context.Response.Headers.CacheControl = "no-cache";
            return Results.Content(meta is null ? template : Render(template, meta), "text/html; charset=utf-8");
        });

    /// <summary>Replaces the placeholder (and the default title) with encoded meta tags.</summary>
    public static string Render(string template, PageMeta meta)
    {
        var tags = new StringBuilder();
        tags.Append("<title>").Append(Encode(meta.Title)).Append("</title>");
        AppendMeta(tags, "property", "og:type", "website");
        AppendMeta(tags, "property", "og:site_name", meta.SiteName);
        AppendMeta(tags, "property", "og:title", meta.Title);
        AppendMeta(tags, "property", "og:url", meta.Url);
        AppendMeta(tags, "name", "twitter:card", meta.ImageUrl is null ? "summary" : "summary_large_image");
        AppendMeta(tags, "name", "twitter:title", meta.Title);
        if (!string.IsNullOrWhiteSpace(meta.Description))
        {
            AppendMeta(tags, "name", "description", meta.Description);
            AppendMeta(tags, "property", "og:description", meta.Description);
            AppendMeta(tags, "name", "twitter:description", meta.Description);
        }

        if (meta.ImageUrl is not null)
        {
            AppendMeta(tags, "property", "og:image", meta.ImageUrl);
            AppendMeta(tags, "name", "twitter:image", meta.ImageUrl);
        }

        var withoutTitle = TitleTag().Replace(template, string.Empty, 1);
        return withoutTitle.Replace(Placeholder, tags.ToString(), StringComparison.Ordinal);
    }

    private static void AppendMeta(StringBuilder tags, string attribute, string key, string value) =>
        tags.Append("<meta ").Append(attribute).Append("=\"").Append(key).Append("\" content=\"").Append(Encode(value)).Append("\">");

    /// <summary>Escapes only what HTML requires, keeping å/ä/ö readable (the page is UTF-8).</summary>
    public static string Encode(string value) =>
        value.Replace("&", "&amp;", StringComparison.Ordinal)
            .Replace("<", "&lt;", StringComparison.Ordinal)
            .Replace(">", "&gt;", StringComparison.Ordinal)
            .Replace("\"", "&quot;", StringComparison.Ordinal)
            .Replace("'", "&#39;", StringComparison.Ordinal);

    private static async Task<PageMeta?> BuildMetaAsync(HttpContext context, ICatalogQueries catalog)
    {
        var tenant = context.GetTenant();
        if (tenant is null)
        {
            return null;
        }

        var request = context.Request;
        var origin = $"{request.Scheme}://{request.Host}";
        string? Absolute(string? url) => url is null ? null : url.StartsWith('/') ? origin + url : url;

        var match = EventPath().Match(request.Path.Value ?? "");
        if (match.Success && await catalog.GetShareInfoAsync(match.Groups["slug"].Value, context.RequestAborted) is { } ev)
        {
            var description = Truncate(ev.Description, 200);
            return new PageMeta($"{ev.Title} – {tenant.Name}", description, Absolute(ev.ImageUrl ?? tenant.LogoUrl), $"{origin}/e/{ev.Slug}", tenant.Name);
        }

        return new PageMeta(tenant.Name, null, Absolute(tenant.LogoUrl), origin + request.Path, tenant.Name);
    }

    private static async Task<string?> LoadTemplateAsync(IWebHostEnvironment environment, IMemoryCache cache)
    {
        // Cached in production; re-read in development so a rebuilt SPA shows up immediately.
        if (!environment.IsDevelopment() && cache.TryGetValue("spa:index", out string? cached))
        {
            return cached;
        }

        var file = environment.WebRootFileProvider.GetFileInfo("index.html");
        if (!file.Exists)
        {
            return null;
        }

        await using var stream = file.CreateReadStream();
        using var reader = new StreamReader(stream, Encoding.UTF8);
        var html = await reader.ReadToEndAsync();
        cache.Set("spa:index", html);
        return html;
    }

    private static string? Truncate(string? text, int max)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var singleLine = Whitespace().Replace(text, " ").Trim();
        return singleLine.Length <= max ? singleLine : singleLine[..(max - 1)].TrimEnd() + "…";
    }

    [GeneratedRegex("^/e/(?<slug>[a-z0-9-]+)(/[a-z]+)?/?$")]
    private static partial Regex EventPath();

    [GeneratedRegex("<title>.*?</title>", RegexOptions.Singleline)]
    private static partial Regex TitleTag();

    [GeneratedRegex("\\s+")]
    private static partial Regex Whitespace();
}
