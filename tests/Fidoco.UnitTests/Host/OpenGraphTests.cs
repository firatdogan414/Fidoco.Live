using Fidoco.Host.Hosting;

namespace Fidoco.UnitTests.Host;

public sealed class OpenGraphTests
{
    private const string Template = "<html><head><title>Fidoco Live</title><!--fidoco:meta--></head><body></body></html>";

    [Fact]
    public void Tags_replace_the_placeholder_and_the_default_title()
    {
        var html = SpaHosting.Render(Template, new PageMeta("AFN 12 – Apex Fight Night", "Kvällens gala", "https://afn.test/files/a.png", "https://afn.test/e/afn-12", "Apex Fight Night"));

        Assert.DoesNotContain("<!--fidoco:meta-->", html);
        Assert.DoesNotContain("<title>Fidoco Live</title>", html);
        Assert.Contains("<title>AFN 12 – Apex Fight Night</title>", html);
        Assert.Contains("<meta property=\"og:image\" content=\"https://afn.test/files/a.png\">", html);
        Assert.Contains("<meta property=\"og:description\" content=\"Kvällens gala\">", html);
        Assert.Contains("<meta name=\"twitter:card\" content=\"summary_large_image\">", html);
    }

    [Fact]
    public void Values_are_html_encoded()
    {
        var html = SpaHosting.Render(Template, new PageMeta("\"><script>alert(1)</script>", "a & b", null, "https://x.test/", "X"));

        Assert.DoesNotContain("<script>", html);
        Assert.Contains("&quot;&gt;&lt;script&gt;", html);
        Assert.Contains("a &amp; b", html);
        Assert.Contains("<meta name=\"twitter:card\" content=\"summary\">", html);
    }
}
