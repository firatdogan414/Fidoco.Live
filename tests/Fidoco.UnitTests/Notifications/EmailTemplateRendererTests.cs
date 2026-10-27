using Fidoco.Contracts.Notifications;
using Fidoco.Modules.Notifications.Email;

namespace Fidoco.UnitTests.Notifications;

public sealed class EmailTemplateRendererTests
{
    private static readonly EmailBranding Branding = new("Apex Fight Night", null, "#0f172a", "#dc2626");

    [Fact]
    public void Receipt_contains_subject_rows_and_link()
    {
        var renderer = new EmailTemplateRenderer();
        var request = new EmailRequest(
            "kund@example.com",
            EmailTemplates.Receipt,
            new Dictionary<string, string> { ["eventTitle"] = "AFN 12", ["link"] = "https://afn.test/auth/verifiera?token=abc" },
            [new EmailRow("Livestream", "199,00 kr"), new EmailRow("Totalt", "199,00 kr", Emphasize: true)]);

        var email = renderer.Render("sv", request, Branding);

        Assert.Equal("Kvitto: AFN 12", email.Subject);
        Assert.Contains("https://afn.test/auth/verifiera?token=abc", email.Html);
        Assert.Contains("199,00 kr", email.Html);
        Assert.Contains("Apex Fight Night", email.Html);
        Assert.Contains("Livestream: 199,00 kr", email.Text);
    }

    [Fact]
    public void Values_are_html_encoded()
    {
        var renderer = new EmailTemplateRenderer();
        var request = new EmailRequest("kund@example.com", EmailTemplates.Receipt,
            new Dictionary<string, string> { ["eventTitle"] = "<script>alert(1)</script>", ["link"] = "https://x.test/?a=1&b=2" });

        var email = renderer.Render("sv", request, Branding);

        Assert.DoesNotContain("<script>", email.Html);
        Assert.Contains("&lt;script&gt;", email.Html);
        Assert.Contains("a=1&amp;b=2", email.Html);
    }

    [Fact]
    public void Unknown_language_falls_back_to_swedish()
    {
        var renderer = new EmailTemplateRenderer();
        var request = new EmailRequest("kund@example.com", EmailTemplates.LoginLink, new Dictionary<string, string> { ["link"] = "https://x.test" });

        Assert.Equal("Logga in hos Apex Fight Night", renderer.Render("de", request, Branding).Subject);
    }
}
