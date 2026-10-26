using System.Linq.Expressions;
using System.Text.Json;
using Fidoco.SharedKernel.Domain;
using Fidoco.SharedKernel.Outbox;
using Fidoco.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Fidoco.SharedKernel.Persistence;

/// <summary>Scoped services every module DbContext needs; bundled so derived constructors stay short.</summary>
public sealed class ModuleDbContextDependencies(
    ITenantContext tenant,
    DomainEventRegistry registry,
    OutboxSignal outboxSignal,
    TimeProvider time)
{
    public ITenantContext Tenant { get; } = tenant;

    public DomainEventRegistry Registry { get; } = registry;

    public OutboxSignal OutboxSignal { get; } = outboxSignal;

    public TimeProvider Time { get; } = time;

    /// <summary>For design-time tooling (dotnet ef) only.</summary>
    public static ModuleDbContextDependencies ForDesignTime() =>
        new(new TenantContext(), new DomainEventRegistry([]), new OutboxSignal(), TimeProvider.System);
}

/// <summary>
/// Base class for every module's DbContext:
/// <list type="bullet">
/// <item>own schema (SQL Server) and an outbox table named after the module,</item>
/// <item>a global "Tenant" query filter on every <see cref="ITenantOwned"/> entity (fails closed without tenant),</item>
/// <item>TenantId stamping and cross-tenant write protection on save,</item>
/// <item>domain events written to the outbox in the same transaction,</item>
/// <item>DateTimeOffset stored as Unix milliseconds on SQLite so it can be compared and sorted.</item>
/// </list>
/// </summary>
public abstract class ModuleDbContext(DbContextOptions options, ModuleDbContextDependencies dependencies) : DbContext(options)
{
    public const string TenantFilter = "Tenant";

    /// <summary>Module name, e.g. "Catalog". Gives schema "catalog" and table "CatalogOutbox".</summary>
    public abstract string ModuleName { get; }

    public string Schema => ModuleName.ToLowerInvariant();

    public string OutboxTableName => $"{ModuleName}Outbox";

    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    /// <summary>Evaluated per query by the tenant filter. Guid.Empty matches nothing.</summary>
    protected Guid CurrentTenantId => dependencies.Tenant.TenantId ?? Guid.Empty;

    protected TimeProvider Time => dependencies.Time;

    /// <summary>Configures the module's entities. Table names must be unique across modules (SQLite has no schemas).</summary>
    protected abstract void ConfigureModel(ModelBuilder modelBuilder);

    protected sealed override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);

        modelBuilder.Entity<OutboxMessage>(e =>
        {
            e.ToTable(OutboxTableName);
            e.HasKey(m => m.Id);
            e.Property(m => m.EventType).HasMaxLength(300);
            e.Property(m => m.HandlerType).HasMaxLength(300);
            e.Property(m => m.LockedBy).HasMaxLength(200);
            e.HasIndex(m => new { m.ProcessedAt, m.FailedAt, m.NextAttemptAt });
        });

        ConfigureModel(modelBuilder);
        UseClientGeneratedGuidKeys(modelBuilder);
        ApplyTenantFilters(modelBuilder);
    }

    /// <summary>
    /// Ids are always assigned in code (Guid v7). Telling EF so means a new child added to a tracked
    /// parent's collection is inserted, not mistaken for an existing row to update.
    /// </summary>
    private static void UseClientGeneratedGuidKeys(ModelBuilder modelBuilder)
    {
        foreach (var key in modelBuilder.Model.GetEntityTypes().Select(t => t.FindPrimaryKey()).OfType<Microsoft.EntityFrameworkCore.Metadata.IMutableKey>())
        {
            foreach (var property in key.Properties.Where(p => p.ClrType == typeof(Guid)))
            {
                property.ValueGenerated = Microsoft.EntityFrameworkCore.Metadata.ValueGenerated.Never;
            }
        }
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        if (Database.IsSqlite())
        {
            configurationBuilder.Properties<DateTimeOffset>().HaveConversion<DateTimeOffsetToUnixMillisecondsConverter>();
        }
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        var wroteOutbox = PrepareForSave();
        var result = base.SaveChanges(acceptAllChangesOnSuccess);
        if (wroteOutbox)
        {
            dependencies.OutboxSignal.Notify();
        }

        return result;
    }

    public override async Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        var wroteOutbox = PrepareForSave();
        var result = await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
        if (wroteOutbox)
        {
            dependencies.OutboxSignal.Notify();
        }

        return result;
    }

    private bool PrepareForSave()
    {
        ApplyTenantRules();
        return WriteDomainEventsToOutbox();
    }

    private void ApplyTenantRules()
    {
        foreach (var entry in ChangeTracker.Entries<ITenantOwned>())
        {
            if (entry.State is EntityState.Unchanged or EntityState.Detached)
            {
                continue;
            }

            var entityName = entry.Entity.GetType().Name;
            var current = dependencies.Tenant.TenantId
                ?? throw new TenantIsolationException($"Cannot write {entityName} without a tenant context.");

            if (entry.State == EntityState.Added && entry.Entity.TenantId == Guid.Empty)
            {
                entry.Entity.TenantId = current;
            }

            var original = entry.State == EntityState.Added
                ? entry.Entity.TenantId
                : entry.Property(e => e.TenantId).OriginalValue;
            if (entry.Entity.TenantId != current || original != current)
            {
                throw new TenantIsolationException($"Cannot write {entityName} belonging to another tenant.");
            }
        }
    }

    private bool WriteDomainEventsToOutbox()
    {
        var sources = ChangeTracker.Entries<IHasDomainEvents>()
            .Select(e => e.Entity)
            .Where(e => e.DomainEvents.Count > 0)
            .ToList();
        if (sources.Count == 0)
        {
            return false;
        }

        var now = dependencies.Time.GetUtcNow();
        var wrote = false;
        foreach (var source in sources)
        {
            foreach (var domainEvent in source.DomainEvents)
            {
                var eventType = domainEvent.GetType();
                // One row per handler, so a failing handler is retried without re-running the others.
                foreach (var handler in dependencies.Registry.HandlersFor(eventType))
                {
                    OutboxMessages.Add(new OutboxMessage
                    {
                        Id = Guid.CreateVersion7(now),
                        TenantId = dependencies.Tenant.TenantId,
                        EventType = eventType.FullName!,
                        HandlerType = handler.Name,
                        Payload = JsonSerializer.Serialize(domainEvent, eventType, OutboxSerializer.Options),
                        OccurredAt = now,
                        NextAttemptAt = now,
                    });
                    wrote = true;
                }
            }

            source.ClearDomainEvents();
        }

        return wrote;
    }

    private void ApplyTenantFilters(ModelBuilder modelBuilder)
    {
        var tenantOwned = modelBuilder.Model.GetEntityTypes()
            .Where(t => t.BaseType is null && typeof(ITenantOwned).IsAssignableFrom(t.ClrType))
            .ToList();

        foreach (var entityType in tenantOwned)
        {
            var parameter = Expression.Parameter(entityType.ClrType, "e");
            var body = Expression.Equal(
                Expression.Property(parameter, nameof(ITenantOwned.TenantId)),
                Expression.Property(Expression.Constant(this), nameof(CurrentTenantId)));
            modelBuilder.Entity(entityType.ClrType).HasQueryFilter(TenantFilter, Expression.Lambda(body, parameter));
        }
    }
}

/// <summary>SQLite cannot compare DateTimeOffset; store it as UTC Unix milliseconds instead.</summary>
public sealed class DateTimeOffsetToUnixMillisecondsConverter()
    : ValueConverter<DateTimeOffset, long>(
        v => v.ToUnixTimeMilliseconds(),
        v => DateTimeOffset.FromUnixTimeMilliseconds(v));
