using System.Security.Claims;
using Fidoco.Contracts.Identity;
using Fidoco.Modules.Support.Domain;
using Fidoco.Modules.Support.Infrastructure;
using Fidoco.SharedKernel.Domain;
using Fidoco.SharedKernel.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Fidoco.Modules.Support.Integration;

/// <summary>Records support actions with the signed-in admin as the actor.</summary>
internal sealed class SupportLog(SupportDbContext db, TimeProvider time)
{
    /// <summary>Repeated views of the same customer by the same admin within this window are logged once.</summary>
    public static readonly TimeSpan ViewDedupeWindow = TimeSpan.FromMinutes(10);

    public async Task RecordAsync(
        ClaimsPrincipal admin,
        SupportAction action,
        Guid? customerId,
        string? customerEmail,
        Guid? eventId = null,
        string? eventTitle = null,
        string? details = null,
        CancellationToken cancellationToken = default)
    {
        var adminId = admin.GetRequiredUserId();
        var now = time.GetUtcNow();
        if (action == SupportAction.CustomerViewed)
        {
            var since = now - ViewDedupeWindow;
            if (await db.Log.AnyAsync(e => e.AdminId == adminId && e.CustomerId == customerId && e.Action == action && e.CreatedAt > since, cancellationToken))
            {
                return;
            }
        }

        db.Log.Add(new SupportLogEntry
        {
            Id = Guid.CreateVersion7(now),
            AdminId = adminId,
            AdminEmail = admin.FindFirstValue(FidocoClaims.Email) ?? adminId.ToString(),
            Action = action,
            CustomerId = customerId,
            CustomerEmail = customerEmail,
            EventId = eventId,
            EventTitle = Truncate(eventTitle, 200),
            Details = Truncate(details, 500),
            CreatedAt = now,
        });
        await db.SaveChangesAsync(cancellationToken);
    }

    private static string? Truncate(string? value, int max) => value is null || value.Length <= max ? value : value[..max];
}

/// <summary>GDPR: an erased customer's e-mail and id are removed from the log; what the admins did stays.</summary>
internal sealed class AnonymizeSupportLogOnCustomerDeleted(SupportDbContext db) : IDomainEventHandler<CustomerDeleted>
{
    public Task HandleAsync(CustomerDeleted domainEvent, CancellationToken cancellationToken) =>
        db.Log.Where(e => e.CustomerId == domainEvent.CustomerId)
            .ExecuteUpdateAsync(s => s.SetProperty(e => e.CustomerId, (Guid?)null).SetProperty(e => e.CustomerEmail, (string?)null), cancellationToken);
}

/// <summary>Support log entries are kept 12 months.</summary>
internal sealed class SupportLogRetention(SupportDbContext db) : IDataRetentionTask
{
    public static readonly TimeSpan KeepFor = TimeSpan.FromDays(365);

    public Task<int> RunAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        var cutoff = now - KeepFor;
        return db.Log.IgnoreQueryFilters().Where(e => e.CreatedAt < cutoff).ExecuteDeleteAsync(cancellationToken);
    }
}
