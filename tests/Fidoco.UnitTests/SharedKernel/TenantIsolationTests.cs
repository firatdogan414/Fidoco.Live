using Fidoco.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Fidoco.UnitTests.SharedKernel;

public sealed class TenantIsolationTests
{
    private static readonly Guid TenantA = Guid.NewGuid();
    private static readonly Guid TenantB = Guid.NewGuid();

    [Fact]
    public async Task Query_filter_returns_only_current_tenants_rows()
    {
        await using var host = new TestModuleHost();
        await host.InTenant(TenantA, db => db.AddAndSave(Item.Create("a", host.Time.GetUtcNow())));
        await host.InTenant(TenantB, db => db.AddAndSave(Item.Create("b", host.Time.GetUtcNow())));

        var seenByA = await host.InTenant(TenantA, db => db.Items.Select(i => i.Name).ToListAsync());
        var seenByB = await host.InTenant(TenantB, db => db.Items.Select(i => i.Name).ToListAsync());

        Assert.Equal(["a"], seenByA);
        Assert.Equal(["b"], seenByB);
    }

    [Fact]
    public async Task Query_without_tenant_returns_nothing()
    {
        await using var host = new TestModuleHost();
        await host.InTenant(TenantA, db => db.AddAndSave(Item.Create("a", host.Time.GetUtcNow())));

        var seen = await host.InTenant(null, db => db.Items.CountAsync());

        Assert.Equal(0, seen);
    }

    [Fact]
    public async Task Tenant_id_is_stamped_on_insert()
    {
        await using var host = new TestModuleHost();
        var item = Item.Create("a", host.Time.GetUtcNow());

        await host.InTenant(TenantA, db => db.AddAndSave(item));

        Assert.Equal(TenantA, item.TenantId);
    }

    [Fact]
    public async Task Writing_without_tenant_throws()
    {
        await using var host = new TestModuleHost();

        await Assert.ThrowsAsync<TenantIsolationException>(() =>
            host.InTenant(null, db => db.AddAndSave(Item.Create("a", host.Time.GetUtcNow()))));
    }

    [Fact]
    public async Task Writing_another_tenants_row_throws()
    {
        await using var host = new TestModuleHost();
        var item = Item.Create("a", host.Time.GetUtcNow());
        item.TenantId = TenantB;

        await Assert.ThrowsAsync<TenantIsolationException>(() => host.InTenant(TenantA, db => db.AddAndSave(item)));
    }

    [Fact]
    public async Task Moving_a_row_to_another_tenant_throws()
    {
        await using var host = new TestModuleHost();
        var item = Item.Create("a", host.Time.GetUtcNow());
        await host.InTenant(TenantA, db => db.AddAndSave(item));

        await Assert.ThrowsAsync<TenantIsolationException>(() => host.InTenant(TenantA, async db =>
        {
            var loaded = await db.Items.SingleAsync();
            loaded.TenantId = TenantB;
            await db.SaveChangesAsync();
        }));
    }

    [Fact]
    public async Task DateTimeOffset_can_be_filtered_and_sorted_on_sqlite()
    {
        await using var host = new TestModuleHost();
        var t0 = host.Time.GetUtcNow();
        await host.InTenant(TenantA, async db =>
        {
            db.Items.Add(Item.Create("late", t0.AddHours(2)));
            db.Items.Add(new Item { Name = "early", CreatedAt = t0.AddHours(1), ArchivedAt = t0 });
            db.Items.Add(Item.Create("old", t0.AddHours(-1)));
            await db.SaveChangesAsync();
        });

        var names = await host.InTenant(TenantA, db => db.Items
            .Where(i => i.CreatedAt > t0)
            .OrderBy(i => i.CreatedAt)
            .Select(i => i.Name)
            .ToListAsync());
        var archived = await host.InTenant(TenantA, db => db.Items.CountAsync(i => i.ArchivedAt <= t0));

        Assert.Equal(["early", "late"], names);
        Assert.Equal(1, archived);
    }
}

internal static class TestDbContextExtensions
{
    public static async Task AddAndSave(this TestDbContext db, Item item)
    {
        db.Items.Add(item);
        await db.SaveChangesAsync();
    }
}
