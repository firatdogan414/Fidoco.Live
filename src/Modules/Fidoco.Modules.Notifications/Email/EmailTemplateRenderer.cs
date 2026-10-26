using System.Net;
using System.Text;
using System.Text.Json;
using Fidoco.Contracts.Notifications;

namespace Fidoco.Modules.Notifications.Email;

internal sealed record EmailBranding(string TenantName, string? LogoUrl, string PrimaryColor, string AccentColor);

internal sealed record RenderedEmail(string Subject, string Html, string Text);

/// <summary>
/// Renders localized templates (Templates/{language}.json) into a simple branded HTML layout.
/// Placeholders like {eventTitle} are replaced with HTML-encoded values.
/// </summary>
internal sealed class EmailTemplateRenderer
{
    private readonly Dictionary<string, Dictionary<string, Dictionary<string, string>>> _languages = [];

    public EmailTemplateRenderer()
    {
        var assembly = typeof(EmailTemplateRenderer).Assembly;
        foreach (var resource in assembly.GetManifestResourceNames().Where(n => n.Contains(".Templates.", StringComparison.Ordinal)))
        {
            using var stream = assembly.GetManifestResourceStream(resource)!;
            var language = resource.Split('.')[^2];
            _languages[language] = JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, string>>>(stream)!;
        }
    }

    public RenderedEmail Render(string language, EmailRequest request, EmailBranding branding)
    {
        var texts = _languages.TryGetValue(language, out var found) ? found : _languages["sv"];
        if (!texts.TryGetValue(request.Template, out var template))
        {
            throw new InvalidOperationException($"Unknown email template '{request.Template}'.");
        }

        var values = new Dictionary<string, string>(request.Values) { ["tenant"] = branding.TenantName };
        string Text(Dictionary<string, string> source, string key) => Fill(source.GetValueOrDefault(key, ""), values);

        var subject = Text(template, "subject");
        var heading = Text(template, "heading");
        var intro = Text(template, "intro");
        var button = Text(template, "button");
        var outro = Text(template, "outro");
        var footer = Text(texts["layout"], "footer");
        var buttonFallback = Text(texts["layout"], "buttonFallback");
        values.TryGetValue("link", out var link);

        var html = new StringBuilder();
        html.Append("<!doctype html><html lang=\"").Append(Encode(language)).Append("\"><body style=\"margin:0;background:#f4f4f5;font-family:Arial,Helvetica,sans-serif;color:#18181b\">");
        html.Append("<table role=\"presentation\" width=\"100%\" cellpadding=\"0\" cellspacing=\"0\"><tr><td align=\"center\" style=\"padding:24px 12px\">");
        html.Append("<table role=\"presentation\" width=\"100%\" style=\"max-width:560px;background:#ffffff;border-radius:10px;overflow:hidden\">");
        html.Append("<tr><td style=\"background:").Append(Encode(branding.PrimaryColor)).Append(";color:#ffffff;padding:20px 24px;font-size:20px;font-weight:bold\">");
        if (!string.IsNullOrEmpty(branding.LogoUrl))
        {
            html.Append("<img src=\"").Append(Encode(branding.LogoUrl)).Append("\" alt=\"\" height=\"32\" style=\"vertical-align:middle;margin-right:12px\">");
        }

        html.Append(Encode(branding.TenantName)).Append("</td></tr><tr><td style=\"padding:24px\">");
        html.Append("<h1 style=\"font-size:22px;margin:0 0 12px\">").Append(Encode(heading)).Append("</h1>");
        AppendParagraph(html, intro);

        if (request.Rows is { Count: > 0 })
        {
            html.Append("<table role=\"presentation\" width=\"100%\" style=\"border-collapse:collapse;margin:16px 0\">");
            foreach (var row in request.Rows)
            {
                var weight = row.Emphasize ? "bold" : "normal";
                html.Append("<tr><td style=\"padding:6px 0;border-bottom:1px solid #e4e4e7;font-weight:").Append(weight).Append("\">").Append(Encode(row.Label))
                    .Append("</td><td align=\"right\" style=\"padding:6px 0;border-bottom:1px solid #e4e4e7;font-weight:").Append(weight).Append("\">").Append(Encode(row.Value)).Append("</td></tr>");
            }

            html.Append("</table>");
        }

        if (!string.IsNullOrEmpty(link) && !string.IsNullOrEmpty(button))
        {
            html.Append("<p style=\"margin:24px 0\"><a href=\"").Append(Encode(link)).Append("\" style=\"background:").Append(Encode(branding.AccentColor))
                .Append(";color:#ffffff;text-decoration:none;padding:12px 20px;border-radius:8px;display:inline-block;font-weight:bold\">").Append(Encode(button)).Append("</a></p>");
            html.Append("<p style=\"font-size:12px;color:#71717a\">").Append(Encode(buttonFallback)).Append("<br><span style=\"word-break:break-all\">").Append(Encode(link)).Append("</span></p>");
        }

        AppendParagraph(html, outro);
        html.Append("</td></tr><tr><td style=\"padding:16px 24px;font-size:12px;color:#71717a;border-top:1px solid #e4e4e7\">").Append(Encode(footer)).Append("</td></tr>");
        html.Append("</table></td></tr></table></body></html>");

        var text = new StringBuilder();
        text.AppendLine(heading).AppendLine().AppendLine(intro);
        if (request.Rows is { Count: > 0 })
        {
            text.AppendLine();
            foreach (var row in request.Rows)
            {
                text.Append(row.Label).Append(": ").AppendLine(row.Value);
            }
        }

        if (!string.IsNullOrEmpty(link))
        {
            text.AppendLine().Append(button).Append(": ").AppendLine(link);
        }

        if (!string.IsNullOrEmpty(outro))
        {
            text.AppendLine().AppendLine(outro);
        }

        text.AppendLine().AppendLine("--").AppendLine(footer);
        return new RenderedEmail(subject, html.ToString(), text.ToString());
    }

    private static void AppendParagraph(StringBuilder html, string text)
    {
        if (!string.IsNullOrEmpty(text))
        {
            html.Append("<p style=\"font-size:16px;line-height:1.5;margin:0 0 12px\">").Append(Encode(text)).Append("</p>");
        }
    }

    private static string Fill(string text, Dictionary<string, string> values)
    {
        foreach (var (key, value) in values)
        {
            text = text.Replace("{" + key + "}", value, StringComparison.Ordinal);
        }

        return text;
    }

    private static string Encode(string value) => WebUtility.HtmlEncode(value);
}
