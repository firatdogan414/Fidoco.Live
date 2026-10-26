using Fidoco.SharedKernel.Domain;
using Fidoco.SharedKernel.Outbox;
using Fidoco.SharedKernel.Persistence;
using Fidoco.SharedKernel.Tenancy;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace Fidoco.UnitTests.SharedKernel;

internal sealed record ItemCreated(Guid ItemId, string Name) : IDomainEvent;

internal sealed class Item : AggregateRoot, ITenantOwned
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid TenantId { get; set; }

    public string Name { get; set; } = "";

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? ArchivedAt { get; set; }

    public static Item Create(string name, DateTimeOffset createdAt)
    {
        var item = new Item { Name = name, CreatedAt = createdAt };
        item.Raise(new ItemCreated(item.Id, name));
        return item;
    }
}

internal sealed class TestDbContext(DbContextOptions<TestDbContext> options, ModuleDbContextDependencies dependencies)
    : ModuleDbContext(options, dependencies)
{
    public override string ModuleName => "Test";

    public DbSet<Item> Items => Set<Item>();

    protected override void ConfigureModel(ModelBuilder modelBuilder) =>
        modelBuilder.Entity<Item>(e => e.ToTable("TestItems"));
}

/// <summary>Records calls; can be told to fail a number of times first.</summary>
internal sealed class RecordingHandler(HandlerLog log) : IDomainEventHandler<ItemCreated>
{
    public Task HandleAsync(ItemCreated domainEvent, CancellationToken cancellationToken)
    {
        log.Calls.Add(("recording", domainEvent.Name));
        return Task.CompletedTask;
    }
}

internal sealed class FlakyHandler(HandlerLog log, ITenantContext tenant) : IDomainEventHandler<ItemCreated>
{
    public Task HandleAsync(ItemCreated domainEvent, CancellationToken cancellationToken)
    {
        log.Calls.Add(("flaky", domainEvent.Name));
        log.TenantsSeen.Add(tenant.TenantId);
        if (log.FailuresLeft > 0)
        {
            log.FailuresLeft--;
            throw new InvalidOperationException("Simulated failure");
        }

        return Task.CompletedTask;
    }
}

internal sealed class HandlerLog
{
    public List<(string Handler, string Name)> Calls { get; } = [];

    public List<Guid?> TenantsSeen { get; } = [];

    public int FailuresLeft { get; set; }
}

/// <summary>A DI container with one test module on an in-memory SQLite database.</summary>
internal sealed class TestModuleHost : IAsyncDisposable
{
    private readonly SqliteConnection _keepAlive;

    public TestModuleHost(int maxAttempts = 10)
    {
        var connectionString = $"Data Source=file:{Guid.NewGuid():N}?mode=memory&cache=shared";
        _keepAlive = new SqliteConnection(connectionString);
        _keepAlive.Open();

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Database:Provider"] = "Sqlite",
                ["Database:ConnectionString"] = connectionString,
                ["Outbox:MaxAttempts"] = maxAttempts.ToString(System.Globalization.CultureInfo.InvariantCulture),
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<TimeProvider>(Time);
        services.AddSingleton(Log);
        services.AddModuleDbContext<TestDbContext>(configuration);
        services.AddDomainEventHandler<ItemCreated, RecordingHandler>();
        services.AddDomainEventHandler<ItemCreated, FlakyHandler>();
        Services = services.BuildServiceProvider();
        Services.InitializeModuleDatabasesAsync().GetAwaiter().GetResult();
    }

    public FakeTimeProvider Time { get; } = new(new DateTimeOffset(2026, 11, 7, 18, 0, 0, TimeSpan.Zero));

    public HandlerLog Log { get; } = new();

    public ServiceProvider Services { get; }

    public OutboxDispatcher<TestDbContext> Dispatcher => Services.GetRequiredService<OutboxDispatcher<TestDbContext>>();

    /// <summary>Runs <paramref name="action"/> in a new scope with the given tenant.</summary>
    public async Task<T> InTenant<T>(Guid? tenantId, Func<TestDbContext, Task<T>> action)
    {
        using var scope = Services.CreateScope();
        using var tenant = scope.ServiceProvider.GetRequiredService<ITenantContext>().Use(tenantId);
        return await action(scope.ServiceProvider.GetRequiredService<TestDbContext>());
    }

    public Task InTenant(Guid? tenantId, Func<TestDbContext, Task> action) =>
        InTenant<bool>(tenantId, async db =>
        {
            await action(db);
            return true;
        });

    public async ValueTask DisposeAsync()
    {
        await Services.DisposeAsync();
        await _keepAlive.DisposeAsync();
    }
}
