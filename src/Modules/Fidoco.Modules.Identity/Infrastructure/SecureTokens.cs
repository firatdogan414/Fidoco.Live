using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.WebUtilities;

namespace Fidoco.Modules.Identity.Infrastructure;

internal static class SecureTokens
{
    /// <summary>A random URL-safe token (256 bits) and the hash to store.</summary>
    public static (string Token, string Hash) Create()
    {
        var token = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
        return (token, Hash(token));
    }

    public static string Hash(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))).ToLowerInvariant();
}
