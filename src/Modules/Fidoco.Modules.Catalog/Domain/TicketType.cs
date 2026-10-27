using Fidoco.Contracts.Catalog;
using Fidoco.SharedKernel.Tenancy;

namespace Fidoco.Modules.Catalog.Domain;

internal enum TicketKind
{
    /// <summary>Access to the event's streams (live and/or replay).</summary>
    DigitalAccess,

    /// <summary>Physical admission (future: capacity, reservations, QR tickets).</summary>
    PhysicalAdmission,
}

internal enum TicketAccessMode
{
    Paid,

    // Future: FreeRegistration, AccessCode.
}

/// <summary>A way to get access to an event. Phase 1: one paid digital ticket per event, no capacity.</summary>
internal sealed class TicketType : ITenantOwned
{
    public const string DefaultCurrency = "SEK";
    public const decimal DefaultVatRatePercent = 25m;

    /// <summary>Stripe's minimum charge in SEK is 3.00.</summary>
    public const long MinimumPaidAmount = 300;

    public Guid Id { get; set; }

    public Guid TenantId { get; set; }

    public Guid EventId { get; set; }

    public required string Name { get; set; }

    public int SortOrder { get; set; }

    public TicketKind Kind { get; set; } = TicketKind.DigitalAccess;

    public TicketAccessMode AccessMode { get; set; } = TicketAccessMode.Paid;

    /// <summary>Price including VAT in minor units (öre).</summary>
    public long PriceAmount { get; set; }

    public string Currency { get; set; } = DefaultCurrency;

    public decimal VatRatePercent { get; set; } = DefaultVatRatePercent;

    public bool IncludesLive { get; set; } = true;

    /// <summary>Days after the event ends that replay is available; null = no replay.</summary>
    public int? ReplayAccessDays { get; set; }

    /// <summary>Maximum number of tickets; null = unlimited (phase 1).</summary>
    public int? Capacity { get; set; }

    public DateTimeOffset? SalesStartsAt { get; set; }

    public DateTimeOffset? SalesEndsAt { get; set; }

    public bool IsActive { get; set; } = true;
}

internal static class TicketSaleRules
{
    /// <summary>Why the ticket cannot be bought right now, or null when it can.</summary>
    public static string? NotOnSaleReason(Event ev, TicketType ticket, DateTimeOffset now)
    {
        if (ev.Status == EventStatus.Draft)
        {
            return "event_not_published";
        }

        if (!ticket.IsActive)
        {
            return "ticket_inactive";
        }

        if (ticket.SalesStartsAt is { } starts && now < starts)
        {
            return "sales_not_started";
        }

        if (ticket.SalesEndsAt is { } ends && now >= ends)
        {
            return "sales_ended";
        }

        if (ev.Status == EventStatus.Ended)
        {
            var replayOpen = ticket.ReplayAccessDays is { } days && ev.EndedAt is { } endedAt && now < endedAt.AddDays(days);
            return replayOpen ? null : "event_ended";
        }

        if (ev.Status == EventStatus.Live && !ticket.IncludesLive && ticket.ReplayAccessDays is null)
        {
            return "ticket_inactive";
        }

        return null;
    }
}
