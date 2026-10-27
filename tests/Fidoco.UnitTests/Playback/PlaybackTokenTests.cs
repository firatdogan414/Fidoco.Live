using System.Security.Cryptography;
using System.Text;
using Fidoco.Modules.Playback.Infrastructure;
using Fidoco.Modules.Playback.Sessions;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Fidoco.UnitTests.Playback;

public sealed class PlaybackTokenTests
{
    [Fact]
    public async Task Mux_tokens_are_rs256_with_kid_subject_audience_and_expiry()
    {
        using var rsa = RSA.Create(2048);
        var options = Options.Create(new MuxSigningOptions
        {
            SigningKeyId = "key-123",
            SigningKeyPrivate = Convert.ToBase64String(Encoding.UTF8.GetBytes(rsa.ExportRSAPrivateKeyPem())),
        });
        var expires = DateTimeOffset.UtcNow.AddMinutes(15);

        var tokens = new MuxPlaybackTokenSigner(options).Sign("playback-abc", expires);

        foreach (var (token, audience) in new[] { (tokens.Playback, "v"), (tokens.Thumbnail, "t"), (tokens.Storyboard, "s") })
        {
            var result = await new JsonWebTokenHandler().ValidateTokenAsync(token, new TokenValidationParameters
            {
                IssuerSigningKey = new RsaSecurityKey(rsa.ExportParameters(false)),
                ValidAudience = audience,
                ValidateIssuer = false,
                ValidateLifetime = true,
            });
            Assert.True(result.IsValid, result.Exception?.Message);
            var jwt = (JsonWebToken)result.SecurityToken;
            Assert.Equal("key-123", jwt.Kid);
            Assert.Equal("RS256", jwt.Alg);
            Assert.Equal("playback-abc", jwt.Subject);
            Assert.Equal(expires.ToUnixTimeSeconds(), new DateTimeOffset(jwt.ValidTo).ToUnixTimeSeconds());
        }
    }

    [Fact]
    public void Presence_counts_sessions_seen_within_the_timeout()
    {
        var presence = new InMemoryViewerPresence(Options.Create(new PlaybackOptions { SessionTimeoutSeconds = 75 }));
        var eventId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        presence.Seen(eventId, "a", now);
        presence.Seen(eventId, "b", now.AddSeconds(-80));
        presence.Seen(eventId, "c", now);
        presence.Left(eventId, "c");

        Assert.Equal(1, presence.Count(eventId, now));
        Assert.Equal(0, presence.Count(Guid.NewGuid(), now));
    }
}
