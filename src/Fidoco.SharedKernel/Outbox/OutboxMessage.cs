namespace Fidoco.SharedKernel.Outbox;

/// <summary>
/// One pending delivery of a domain event to one handler. Written in the same transaction as the change
/// that raised the event; processed by <see cref="OutboxDispatcher{TContext}"/>.
/// </summary>
public sealed class OutboxMessage
{
    public Guid Id { get; set; }

    /// <summary>Tenant the event was raised for; handlers run with this tenant set.</summary>
    public Guid? TenantId { get; set; }

    public required string EventType { get; set; }

    public required string HandlerType { get; set; }

    public required string Payload { get; set; }

    public DateTimeOffset OccurredAt { get; set; }

    public int Attempts { get; set; }

    public DateTimeOffset NextAttemptAt { get; set; }

    public string? LockedBy { get; set; }

    public DateTimeOffset? LockedUntil { get; set; }

    public DateTimeOffset? ProcessedAt { get; set; }

    /// <summary>Set when the message gave up after <see cref="OutboxOptions.MaxAttempts"/>.</summary>
    public DateTimeOffset? FailedAt { get; set; }

    public string? LastError { get; set; }
}
