using Fidoco.SharedKernel.Tenancy;

namespace Fidoco.Modules.Support.Domain;

internal enum SupportAction
{
    /// <summary>An admin opened a customer's purchases and tickets (at most once per 10 minutes is logged).</summary>
    CustomerViewed,

    LoginLinkSent,

    CompGranted,

    CompRevoked,
}

/// <summary>
/// Audit trail of what the organizer's admins did for customers: who, what and when. Kept 12 months;
/// the link to the customer is removed when the customer's data is erased.
/// </summary>
internal sealed class SupportLogEntry : ITenantOwned
{
    public Guid Id { get; set; }

    public Guid TenantId { get; set; }

    public Guid AdminId { get; set; }

    public required string AdminEmail { get; set; }

    public SupportAction Action { get; set; }

    public Guid? CustomerId { get; set; }

    public string? CustomerEmail { get; set; }

    public Guid? EventId { get; set; }

    public string? EventTitle { get; set; }

    /// <summary>Free text, e.g. the ticket type and note of a comp.</summary>
    public string? Details { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
