using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Fidoco.Modules.Catalog.Domain;

internal static partial class Slugs
{
    public const int MaxLength = 80;

    /// <summary>"Apex Fight Night 12 – Göteborg" → "apex-fight-night-12-goteborg".</summary>
    public static string From(string text)
    {
        var decomposed = text.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder();
        foreach (var c in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
            {
                builder.Append(c);
            }
        }

        var slug = NonAlphanumeric().Replace(builder.ToString(), "-").Trim('-');
        if (slug.Length > MaxLength)
        {
            slug = slug[..MaxLength].TrimEnd('-');
        }

        return slug.Length == 0 ? "event" : slug;
    }

    public static bool IsValid(string slug) => slug.Length <= MaxLength && ValidSlug().IsMatch(slug);

    [GeneratedRegex("[^a-z0-9]+")]
    private static partial Regex NonAlphanumeric();

    [GeneratedRegex("^[a-z0-9]+(-[a-z0-9]+)*$")]
    private static partial Regex ValidSlug();
}
