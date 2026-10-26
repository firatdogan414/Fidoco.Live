namespace Fidoco.SharedKernel.Tenancy;

/// <summary>
/// Marks tenant data. <see cref="Persistence.ModuleDbContext"/> adds a global query filter on
/// <see cref="TenantId"/>, stamps it on insert and refuses writes for another tenant.
/// </summary>
public interface ITenantOwned
{
    Guid TenantId { get; set; }
}

public sealed class TenantIsolationException(string message) : InvalidOperationException(message);
