using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Fidoco.Modules.Playback.Infrastructure;

internal sealed record PlaybackTokens(string Playback, string Thumbnail, string Storyboard, DateTimeOffset ExpiresAt);

/// <summary>Signs short-lived tokens for signed playback. Local cryptography only: no network call.</summary>
internal interface IPlaybackTokenSigner
{
    PlaybackTokens Sign(string playbackId, DateTimeOffset expiresAt);
}

internal sealed class MuxSigningOptions
{
    public const string SectionName = "Mux";

    /// <summary>Signing key id (Mux → Settings → Signing Keys).</summary>
    public string SigningKeyId { get; set; } = "";

    /// <summary>The base64-encoded PEM private key that Mux returns when the signing key is created.</summary>
    public string SigningKeyPrivate { get; set; } = "";

    public bool IsConfigured => !string.IsNullOrEmpty(SigningKeyId) && !string.IsNullOrEmpty(SigningKeyPrivate);
}

/// <summary>Mux signed playback JWT: RS256, kid header, sub = playback id, aud = v/t/s, exp.</summary>
internal sealed class MuxPlaybackTokenSigner : IPlaybackTokenSigner
{
    private readonly SigningCredentials _credentials;
    private readonly JsonWebTokenHandler _handler = new() { SetDefaultTimesOnTokenCreation = false };

    public MuxPlaybackTokenSigner(IOptions<MuxSigningOptions> options)
    {
        var settings = options.Value;
        var rsa = RSA.Create();
        rsa.ImportFromPem(Encoding.UTF8.GetString(Convert.FromBase64String(settings.SigningKeyPrivate)));
        _credentials = new SigningCredentials(new RsaSecurityKey(rsa) { KeyId = settings.SigningKeyId }, SecurityAlgorithms.RsaSha256);
    }

    public PlaybackTokens Sign(string playbackId, DateTimeOffset expiresAt) =>
        new(Create(playbackId, "v", expiresAt), Create(playbackId, "t", expiresAt), Create(playbackId, "s", expiresAt), expiresAt);

    private string Create(string playbackId, string audience, DateTimeOffset expiresAt) =>
        _handler.CreateToken(new SecurityTokenDescriptor
        {
            Claims = new Dictionary<string, object>
            {
                ["sub"] = playbackId,
                ["aud"] = audience,
                ["exp"] = expiresAt.ToUnixTimeSeconds(),
            },
            SigningCredentials = _credentials,
        });
}

/// <summary>Development/testing signer used when no Mux signing key is configured.</summary>
internal sealed class FakePlaybackTokenSigner : IPlaybackTokenSigner
{
    public PlaybackTokens Sign(string playbackId, DateTimeOffset expiresAt) =>
        new($"fake-v-{playbackId}", $"fake-t-{playbackId}", $"fake-s-{playbackId}", expiresAt);
}
