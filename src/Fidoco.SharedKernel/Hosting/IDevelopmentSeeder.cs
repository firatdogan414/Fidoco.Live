namespace Fidoco.SharedKernel.Hosting;

/// <summary>
/// Seeds demo data for local development (Seed:DevelopmentData=true, Development only). Each module seeds
/// its own data; seeders run in ascending <see cref="Order"/> and must be idempotent.
/// </summary>
public interface IDevelopmentSeeder
{
    int Order { get; }

    Task SeedAsync(IServiceProvider services, CancellationToken cancellationToken);
}

/// <summary>Well-known values for the development seed (shared by the module seeders).</summary>
public static class DevelopmentSeed
{
    public const string TenantHost = "afn.localhost";
    public const string PlatformHost = "admin.localhost";
    public const string TenantAdminEmail = "admin@afn.local";
    public const string TenantAdminPassword = "Fidoco-dev-123!";
    public const string PlatformAdminEmail = "admin@fidoco.local";
}
