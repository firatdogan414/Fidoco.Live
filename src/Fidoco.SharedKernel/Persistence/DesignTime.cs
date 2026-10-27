using Microsoft.EntityFrameworkCore;

namespace Fidoco.SharedKernel.Persistence;

/// <summary>Helpers for IDesignTimeDbContextFactory implementations: migrations are generated for SQL Server.</summary>
public static class DesignTime
{
    public static DbContextOptions<TContext> SqlServerOptions<TContext>()
        where TContext : ModuleDbContext
    {
        var builder = new DbContextOptionsBuilder<TContext>();
        PersistenceServiceCollectionExtensions.ConfigureProvider(
            builder,
            new DatabaseOptions
            {
                Provider = DatabaseProviders.SqlServer,
                ConnectionString = "Server=(design-time);Database=Fidoco;Trusted_Connection=True;TrustServerCertificate=True",
            },
            typeof(TContext));
        return builder.Options;
    }
}
