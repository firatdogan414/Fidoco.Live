using System.Text.Json;
using Fidoco.SharedKernel.Domain;
using Fidoco.SharedKernel.Observability;
using Fidoco.SharedKernel.Persistence;
using Fidoco.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Fidoco.SharedKernel.Outbox;

/// <summary>Processes one module's outbox. Used by the background processor and by tests.</summary>
public interface IOutboxDispatcher
{
    string ModuleName { get; }

    /// <summary>Claims and processes due messages; returns how many were handled (successfully or not).</summary>
    Task<int> ProcessPendingAsync(CancellationToken cancellationToken);

    /// <summary>Pending, overdue (due for more than 5 minutes) and permanently failed messages.</summary>
    Task<OutboxStats> GetStatsAsync(CancellationToken cancellationToken);
}

public sealed class OutboxDispatcher<TContext>(
    IServiceScopeFactory scopeFactory,
    DomainEventRegistry registry,
    TimeProvider time,
    IOptions<OutboxOptions> options,
    ILogger<OutboxDispatcher<TContext>> logger) : IOutboxDispatcher
    where TContext : ModuleDbContext
{
    private static readonly string InstanceId = $"{Environment.MachineName}:{Guid.NewGuid():N}";

    public string ModuleName => typeof(TContext).Name.Replace("DbContext", string.Empty, StringComparison.Ordinal);

    public async Task<int> ProcessPendingAsync(CancellationToken cancellationToken)
    {
        var settings = options.Value;
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TContext>();

        var now = time.GetUtcNow();
        var candidates = await db.OutboxMessages
            .Where(m => m.ProcessedAt == null && m.FailedAt == null && m.NextAttemptAt <= now
                && (m.LockedUntil == null || m.LockedUntil < now))
            .OrderBy(m => m.NextAttemptAt)
            .Select(m => m.Id)
            .Take(settings.BatchSize)
            .ToListAsync(cancellationToken);

        var handled = 0;
        foreach (var id in candidates)
        {
            // Atomic claim: only one instance can move LockedUntil forward for a given row.
            var lockUntil = now + settings.LeaseDuration;
            var claimed = await db.OutboxMessages
                .Where(m => m.Id == id && m.ProcessedAt == null && m.FailedAt == null
                    && (m.LockedUntil == null || m.LockedUntil < now))
                .ExecuteUpdateAsync(s => s
                    .SetProperty(m => m.LockedBy, InstanceId)
                    .SetProperty(m => m.LockedUntil, lockUntil), cancellationToken);
            if (claimed == 0)
            {
                continue;
            }

            var message = await db.OutboxMessages.AsNoTracking().SingleAsync(m => m.Id == id, cancellationToken);
            await ProcessOneAsync(db, message, settings, cancellationToken);
            handled++;
        }

        return handled;
    }

    public async Task<OutboxStats> GetStatsAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TContext>();
        var overdueBefore = time.GetUtcNow().AddMinutes(-5);
        var pending = await db.OutboxMessages.CountAsync(m => m.ProcessedAt == null && m.FailedAt == null, cancellationToken);
        var overdue = await db.OutboxMessages.CountAsync(m => m.ProcessedAt == null && m.FailedAt == null && m.NextAttemptAt < overdueBefore, cancellationToken);
        var failed = await db.OutboxMessages.CountAsync(m => m.FailedAt != null, cancellationToken);
        return new OutboxStats(pending, overdue, failed);
    }

    private async Task ProcessOneAsync(TContext db, OutboxMessage message, OutboxOptions settings, CancellationToken cancellationToken)
    {
        try
        {
            var registration = registry.FindHandler(message.HandlerType)
                ?? throw new InvalidOperationException($"No handler registered with name '{message.HandlerType}'.");
            var domainEvent = (IDomainEvent?)JsonSerializer.Deserialize(message.Payload, registration.EventType, OutboxSerializer.Options)
                ?? throw new InvalidOperationException($"Outbox message {message.Id} has an empty payload.");

            using var handlerScope = scopeFactory.CreateScope();
            using var tenant = handlerScope.ServiceProvider.GetRequiredService<ITenantContext>().Use(message.TenantId);
            await registration.InvokeAsync(handlerScope.ServiceProvider, domainEvent, cancellationToken);

            var processedAt = time.GetUtcNow();
            await db.OutboxMessages.Where(m => m.Id == message.Id).ExecuteUpdateAsync(s => s
                .SetProperty(m => m.ProcessedAt, processedAt)
                .SetProperty(m => m.LockedBy, (string?)null)
                .SetProperty(m => m.LockedUntil, (DateTimeOffset?)null), cancellationToken);
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            var attempts = message.Attempts + 1;
            var failedNow = time.GetUtcNow();
            var giveUp = attempts >= settings.MaxAttempts;
            DateTimeOffset? failedAt = giveUp ? failedNow : null;
            var nextAttempt = failedNow + OutboxBackoff.Delay(attempts, settings.MaxBackoff);
            var error = ex.ToString();
            if (error.Length > 4000)
            {
                error = error[..4000];
            }

            await db.OutboxMessages.Where(m => m.Id == message.Id).ExecuteUpdateAsync(s => s
                .SetProperty(m => m.Attempts, attempts)
                .SetProperty(m => m.NextAttemptAt, nextAttempt)
                .SetProperty(m => m.FailedAt, failedAt)
                .SetProperty(m => m.LastError, error)
                .SetProperty(m => m.LockedBy, (string?)null)
                .SetProperty(m => m.LockedUntil, (DateTimeOffset?)null), CancellationToken.None);

            if (giveUp)
            {
                FidocoTelemetry.OutboxFailed.Add(1);
                logger.LogError(ex, "Outbox message {MessageId} ({EventType} -> {Handler}) failed permanently after {Attempts} attempts",
                    message.Id, message.EventType, message.HandlerType, attempts);
            }
            else
            {
                logger.LogWarning(ex, "Outbox message {MessageId} ({EventType} -> {Handler}) failed, attempt {Attempts}; retrying at {NextAttempt}",
                    message.Id, message.EventType, message.HandlerType, attempts, nextAttempt);
            }
        }
    }
}

public static class OutboxSerializer
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
}
