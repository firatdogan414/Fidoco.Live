using Fidoco.SharedKernel.Domain;

namespace Fidoco.Contracts.Catalog;

public enum EventStatus
{
    Draft,
    Scheduled,
    Live,
    Ended,
}

/// <summary>A ticket type as the checkout sees it. <see cref="NotOnSaleReason"/> is null when it can be bought.</summary>
public sealed record PurchasableTicket(
    Guid TicketTypeId,
    Guid EventId,
    string EventSlug,
    string EventTitle,
    string TicketName,
    long PriceAmount,
    string Currency,
    decimal VatRatePercent,
    bool IncludesLive,
    int? ReplayAccessDays,
    string? NotOnSaleReason)
{
    public bool OnSale => NotOnSaleReason is null;
}

/// <summary>What Playback needs to decide access and pick a playback id.</summary>
public sealed record EventPlaybackInfo(
    Guid EventId,
    EventStatus Status,
    DateTimeOffset StartsAt,
    DateTimeOffset? EndedAt,
    string? LivePlaybackId,
    bool StreamActive,
    string? ReplayPlaybackId);

public sealed record EventSummary(Guid EventId, string Slug, string Title, DateTimeOffset StartsAt);

/// <summary>Read access to the catalog for other modules (current tenant only).</summary>
public interface ICatalogQueries
{
    Task<PurchasableTicket?> GetPurchasableTicketAsync(Guid ticketTypeId, CancellationToken cancellationToken);

    /// <summary>Cached for a few seconds; called on every playback session start.</summary>
    Task<EventPlaybackInfo?> GetPlaybackInfoAsync(Guid eventId, CancellationToken cancellationToken);

    Task<EventSummary?> GetEventSummaryAsync(Guid eventId, CancellationToken cancellationToken);
}

public sealed record ReservationResult(bool Succeeded, Guid? ReservationId, string? FailureCode)
{
    public static ReservationResult Unlimited { get; } = new(true, null, null);
}

/// <summary>
/// Capacity seam in the checkout flow. Phase 1 has no capacity (always succeeds without a reservation);
/// physical tickets with a maximum will reserve here and confirm/release from the payment webhooks.
/// </summary>
public interface ITicketAvailability
{
    Task<ReservationResult> TryReserveAsync(Guid ticketTypeId, int quantity, DateTimeOffset expiresAt, CancellationToken cancellationToken);

    Task ConfirmAsync(Guid reservationId, CancellationToken cancellationToken);

    Task ReleaseAsync(Guid reservationId, CancellationToken cancellationToken);
}

public sealed record EventPublished(Guid EventId) : IDomainEvent;

public sealed record StreamWentLive(Guid EventId, Guid StreamId, DateTimeOffset At) : IDomainEvent;

public sealed record StreamWentIdle(Guid EventId, Guid StreamId, DateTimeOffset At) : IDomainEvent;

public sealed record EventEnded(Guid EventId, DateTimeOffset EndedAt) : IDomainEvent;

public sealed record RecordingReady(Guid EventId, Guid StreamId, Guid RecordingId) : IDomainEvent;
