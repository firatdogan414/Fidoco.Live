using Fidoco.SharedKernel.Outbox;
using Microsoft.EntityFrameworkCore;

namespace Fidoco.UnitTests.SharedKernel;

public sealed class OutboxTests
{
    private static readonly Guid Tenant = Guid.NewGuid();

    [Fact]
    public async Task Domain_event_is_written_once_per_handler_in_the_same_save()
    {
        await using var host = new TestModuleHost();

        await host.InTenant(Tenant, db => db.AddAndSave(Item.Create("a", host.Time.GetUtcNow())));

        var rows = await host.InTenant(null, db => db.OutboxMessages.ToListAsync());
        Assert.Equal(2, rows.Count);
        Assert.All(rows, r => Assert.Equal(Tenant, r.TenantId));
        Assert.Equal(2, rows.Select(r => r.HandlerType).Distinct().Count());
    }

    [Fact]
    public async Task Dispatcher_runs_handlers_with_the_events_tenant_and_marks_rows_processed()
    {
        await using var host = new TestModuleHost();
        await host.InTenant(Tenant, db => db.AddAndSave(Item.Create("a", host.Time.GetUtcNow())));

        var handled = await host.Dispatcher.ProcessPendingAsync(CancellationToken.None);

        Assert.Equal(2, handled);
        Assert.Contains(("recording", "a"), host.Log.Calls);
        Assert.Contains(("flaky", "a"), host.Log.Calls);
        Assert.Equal([Tenant], host.Log.TenantsSeen);
        Assert.Equal(0, await PendingCount(host));
    }

    [Fact]
    public async Task Failing_handler_is_retried_with_backoff_without_rerunning_other_handlers()
    {
        await using var host = new TestModuleHost();
        host.Log.FailuresLeft = 2;
        await host.InTenant(Tenant, db => db.AddAndSave(Item.Create("a", host.Time.GetUtcNow())));

        await host.Dispatcher.ProcessPendingAsync(CancellationToken.None);
        var failed = await host.InTenant(null, db => db.OutboxMessages.SingleAsync(m => m.ProcessedAt == null));
        Assert.Equal(1, failed.Attempts);
        Assert.Equal(host.Time.GetUtcNow().AddSeconds(2), failed.NextAttemptAt);
        Assert.NotNull(failed.LastError);

        // Not due yet: nothing happens.
        Assert.Equal(0, await host.Dispatcher.ProcessPendingAsync(CancellationToken.None));

        host.Time.Advance(TimeSpan.FromSeconds(2));
        await host.Dispatcher.ProcessPendingAsync(CancellationToken.None);
        failed = await host.InTenant(null, db => db.OutboxMessages.SingleAsync(m => m.ProcessedAt == null));
        Assert.Equal(2, failed.Attempts);
        Assert.Equal(host.Time.GetUtcNow().AddSeconds(4), failed.NextAttemptAt);

        host.Time.Advance(TimeSpan.FromSeconds(4));
        await host.Dispatcher.ProcessPendingAsync(CancellationToken.None);

        Assert.Equal(0, await PendingCount(host));
        Assert.Equal(1, host.Log.Calls.Count(c => c.Handler == "recording"));
        Assert.Equal(3, host.Log.Calls.Count(c => c.Handler == "flaky"));
    }

    [Fact]
    public async Task Message_is_marked_failed_after_max_attempts()
    {
        await using var host = new TestModuleHost(maxAttempts: 3);
        host.Log.FailuresLeft = 100;
        await host.InTenant(Tenant, db => db.AddAndSave(Item.Create("a", host.Time.GetUtcNow())));

        for (var i = 0; i < 5; i++)
        {
            await host.Dispatcher.ProcessPendingAsync(CancellationToken.None);
            host.Time.Advance(TimeSpan.FromMinutes(11));
        }

        var message = await host.InTenant(null, db => db.OutboxMessages.SingleAsync(m => m.ProcessedAt == null));
        Assert.Equal(3, message.Attempts);
        Assert.NotNull(message.FailedAt);
        Assert.Equal(3, host.Log.Calls.Count(c => c.Handler == "flaky"));
    }

    [Fact]
    public async Task Message_locked_by_another_instance_is_skipped_until_the_lease_expires()
    {
        await using var host = new TestModuleHost();
        await host.InTenant(Tenant, db => db.AddAndSave(Item.Create("a", host.Time.GetUtcNow())));
        var lockedUntil = host.Time.GetUtcNow().AddSeconds(60);
        await host.InTenant(null, db => db.OutboxMessages.ExecuteUpdateAsync(s => s
            .SetProperty(m => m.LockedBy, "other-instance")
            .SetProperty(m => m.LockedUntil, lockedUntil)));

        Assert.Equal(0, await host.Dispatcher.ProcessPendingAsync(CancellationToken.None));

        host.Time.Advance(TimeSpan.FromSeconds(61));
        Assert.Equal(2, await host.Dispatcher.ProcessPendingAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Concurrent_dispatchers_never_process_the_same_message_twice()
    {
        await using var host = new TestModuleHost();
        await host.InTenant(Tenant, async db =>
        {
            for (var i = 0; i < 10; i++)
            {
                db.Items.Add(Item.Create($"item-{i}", host.Time.GetUtcNow()));
            }

            await db.SaveChangesAsync();
        });

        var results = await Task.WhenAll(Enumerable.Range(0, 4)
            .Select(_ => Task.Run(() => host.Dispatcher.ProcessPendingAsync(CancellationToken.None))));

        Assert.Equal(20, results.Sum());
        Assert.Equal(10, host.Log.Calls.Count(c => c.Handler == "recording"));
        Assert.Equal(10, host.Log.Calls.Count(c => c.Handler == "flaky"));
    }

    [Theory]
    [InlineData(1, 2)]
    [InlineData(2, 4)]
    [InlineData(3, 8)]
    [InlineData(9, 512)]
    [InlineData(10, 600)]
    [InlineData(30, 600)]
    public void Backoff_doubles_and_is_capped(int attempts, int expectedSeconds)
    {
        Assert.Equal(TimeSpan.FromSeconds(expectedSeconds), OutboxBackoff.Delay(attempts, TimeSpan.FromMinutes(10)));
    }

    private static Task<int> PendingCount(TestModuleHost host) =>
        host.InTenant(null, db => db.OutboxMessages.CountAsync(m => m.ProcessedAt == null));
}
