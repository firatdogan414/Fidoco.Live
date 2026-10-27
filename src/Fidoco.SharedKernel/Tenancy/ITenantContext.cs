namespace Fidoco.SharedKernel.Tenancy;

/// <summary>
/// The tenant the current unit of work runs for. Set from the Host header for HTTP requests and
/// explicitly (<see cref="Use"/>) for webhooks and outbox handlers.
/// </summary>
public interface ITenantContext
{
    /// <summary>The current tenant, or null on the platform host / outside a tenant.</summary>
    Guid? TenantId { get; }

    /// <summary>The current tenant; throws when there is none.</summary>
    Guid RequiredTenantId { get; }

    /// <summary>Switches to <paramref name="tenantId"/> until the returned scope is disposed.</summary>
    IDisposable Use(Guid? tenantId);
}

internal sealed class TenantContext : ITenantContext
{
    public Guid? TenantId { get; private set; }

    public Guid RequiredTenantId =>
        TenantId ?? throw new TenantIsolationException("No tenant is set for the current operation.");

    public IDisposable Use(Guid? tenantId)
    {
        var previous = TenantId;
        TenantId = tenantId;
        return new Restore(this, previous);
    }

    private sealed class Restore(TenantContext context, Guid? previous) : IDisposable
    {
        public void Dispose() => context.TenantId = previous;
    }
}
