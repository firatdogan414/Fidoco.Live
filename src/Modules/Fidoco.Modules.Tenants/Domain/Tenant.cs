namespace Fidoco.Modules.Tenants.Domain;

internal enum TenantStatus
{
    Active,
    Suspended,
}

/// <summary>An event organizer. Platform data, so not itself tenant-filtered.</summary>
internal sealed class Tenant
{
    public Guid Id { get; set; }

    public required string Name { get; set; }

    public required string Slug { get; set; }

    public string? LogoUrl { get; set; }

    public string PrimaryColor { get; set; } = "#111827";

    public string AccentColor { get; set; } = "#e11d48";

    public string? SupportEmail { get; set; }

    public TenantStatus Status { get; set; } = TenantStatus.Active;

    /// <summary>Fidoco's commission in percent of revenue excluding VAT.</summary>
    public decimal CommissionPercent { get; set; } = 15m;

    /// <summary>Optional fixed commission per ticket, excluding VAT, in minor units.</summary>
    public long CommissionFixedAmountPerTicket { get; set; }

    /// <summary>Whether the organizer adds VAT when invoicing Fidoco for its share.</summary>
    public bool OrganizerVatRegistered { get; set; } = true;

    public DateTimeOffset CreatedAt { get; set; }

    public List<TenantDomain> Domains { get; set; } = [];
}

internal sealed class TenantDomain
{
    public Guid Id { get; set; }

    public Guid TenantId { get; set; }

    /// <summary>Lower-case host name without port, e.g. "stream.afnmma.se".</summary>
    public required string Host { get; set; }

    public bool IsPrimary { get; set; }
}
