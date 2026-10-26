using Fidoco.SharedKernel.Domain;
using Fidoco.SharedKernel.Tenancy;

namespace Fidoco.Modules.Identity.Domain;

/// <summary>A buyer/viewer. One account per e-mail and tenant.</summary>
internal sealed class Customer : AggregateRoot, ITenantOwned
{
    public Guid Id { get; set; }

    public Guid TenantId { get; set; }

    /// <summary>Normalized (trimmed, lower-case).</summary>
    public required string Email { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? LastLoginAt { get; set; }
}

internal enum LoginTokenPurpose
{
    /// <summary>Requested from the login page: single use, 15 minutes.</summary>
    Login,

    /// <summary>Sent in a receipt: reusable until the ticket's access ends.</summary>
    Receipt,
}

/// <summary>Magic link token. Only the SHA-256 hash is stored.</summary>
internal sealed class LoginToken : ITenantOwned
{
    public Guid Id { get; set; }

    public Guid TenantId { get; set; }

    public Guid CustomerId { get; set; }

    public required string TokenHash { get; set; }

    public LoginTokenPurpose Purpose { get; set; }

    public string? ReturnPath { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset ExpiresAt { get; set; }

    public DateTimeOffset? ConsumedAt { get; set; }

    public bool IsUsable(DateTimeOffset now) =>
        now < ExpiresAt && (Purpose == LoginTokenPurpose.Receipt || ConsumedAt is null);
}

/// <summary>Organizer staff with access to the tenant admin.</summary>
internal sealed class AdminUser : ITenantOwned
{
    public Guid Id { get; set; }

    public Guid TenantId { get; set; }

    public required string Email { get; set; }

    public string? PasswordHash { get; set; }

    public required string Role { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? LastLoginAt { get; set; }
}
