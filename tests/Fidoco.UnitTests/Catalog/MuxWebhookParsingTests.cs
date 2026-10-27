using Fidoco.Modules.Catalog.Domain;
using Fidoco.Modules.Catalog.Features.Webhooks;

namespace Fidoco.UnitTests.Catalog;

public sealed class MuxWebhookParsingTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 14, 18, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Valid_signature_is_accepted()
    {
        var timestamp = Now.ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture);
        var header = $"t={timestamp},v1={MuxSignature.Compute(timestamp, "{}", "secret")}";

        Assert.True(MuxSignature.IsValid("{}", header, "secret", Now));
    }

    [Fact]
    public void Wrong_secret_tampered_body_or_old_timestamp_is_rejected()
    {
        var timestamp = Now.ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture);
        var header = $"t={timestamp},v1={MuxSignature.Compute(timestamp, "{}", "secret")}";

        Assert.False(MuxSignature.IsValid("{}", header, "other", Now));
        Assert.False(MuxSignature.IsValid("{\"x\":1}", header, "secret", Now));
        Assert.False(MuxSignature.IsValid("{}", header, "secret", Now.AddMinutes(6)));
        Assert.False(MuxSignature.IsValid("{}", "", "secret", Now));
        Assert.False(MuxSignature.IsValid("{}", header, "", Now));
    }

    [Fact]
    public void Asset_webhook_is_parsed()
    {
        const string json = """
            {"type":"video.asset.ready","id":"evt-1","created_at":"2026-11-14T21:00:00.000000Z",
             "data":{"id":"asset-1","live_stream_id":"ls-1","duration":5400.5,
                     "playback_ids":[{"id":"pub","policy":"public"},{"id":"sig","policy":"signed"}]}}
            """;

        var webhook = MuxWebhook.Parse(json);

        Assert.Equal("evt-1", webhook.Id);
        Assert.Equal("asset-1", webhook.ObjectId);
        Assert.Equal("ls-1", webhook.LiveStreamId);
        Assert.Equal("sig", webhook.SignedPlaybackId);
        Assert.Equal(5400.5, webhook.DurationSeconds);
        Assert.Equal(new DateTimeOffset(2026, 11, 14, 21, 0, 0, TimeSpan.Zero), webhook.CreatedAt);
    }

    [Fact]
    public void Live_stream_webhook_uses_object_id_as_live_stream_id()
    {
        var webhook = MuxWebhook.Parse("""{"type":"video.live_stream.active","id":"evt-2","created_at":"2026-11-14T18:00:00Z","data":{"id":"ls-9"}}""");

        Assert.Equal("ls-9", webhook.LiveStreamId);
    }

    [Theory]
    [InlineData("Apex Fight Night 12", "apex-fight-night-12")]
    [InlineData("  Gala i Göteborg – Åre & Malmö! ", "gala-i-goteborg-are-malmo")]
    [InlineData("!!!", "event")]
    public void Slugs_are_url_safe(string title, string expected)
    {
        Assert.Equal(expected, Slugs.From(title));
        Assert.True(Slugs.IsValid(Slugs.From(title)));
    }
}
