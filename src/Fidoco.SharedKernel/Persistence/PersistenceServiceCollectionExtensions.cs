using Fidoco.SharedKernel.Domain;
using Fidoco.SharedKernel.Http;
using Fidoco.SharedKernel.Outbox;
using Fidoco.SharedKernel.Tenancy;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Fidoco.SharedKernel.Persistence;

/// <summary>Registered once per module DbContext; used to initialize databases at startup.</summary>
public sealed record ModuleDatabaseRegistration(Type ContextType);

public static class PersistenceServiceCollectionExtensions
{
    /// <summary>Shared kernel services: tenant context, domain event registry, outbox signal, options.</summary>
    public static IServiceCollection AddFidocoSharedKernel(this IServiceCollection services, IConfiguration configuration)
    {
        services.TryAddScoped<ITenantContext, TenantContext>();
        services.TryAddScoped<ModuleDbContextDependencies>();
        services.TryAddSingleton<DomainEventRegistry>();
        services.TryAddSingleton<OutboxSignal>();
        if (!services.Any(d => d.ServiceType == typeof(OutboxHealthCheck)))
        {
            services.AddSingleton<OutboxHealthCheck>();
            services.AddHealthChecks().AddCheck<OutboxHealthCheck>("outbox", tags: ["outbox"]);
        }

        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<PublicUrls>();
        services.AddOptions<PublicUrlOptions>().Bind(configuration.GetSection(PublicUrlOptions.SectionName));
        services.AddOptions<DatabaseOptions>().Bind(configuration.GetSection(DatabaseOptions.SectionName));
        services.AddOptions<OutboxOptions>().Bind(configuration.GetSection(OutboxOptions.SectionName));
        return services;
    }

    /// <summary>Registers a module DbContext with its provider, outbox processor and health check.</summary>
    public static IServiceCollection AddModuleDbContext<TContext>(this IServiceCollection services, IConfiguration configuration)
        where TContext : ModuleDbContext
    {
        services.AddFidocoSharedKernel(configuration);

        services.AddDbContext<TContext>((sp, options) =>
        {
            var database = sp.GetRequiredService<IOptions<DatabaseOptions>>().Value;
            ConfigureProvider(options, database, typeof(TContext));
        });

        services.AddSingleton(new ModuleDatabaseRegistration(typeof(TContext)));
        services.AddSingleton<OutboxDispatcher<TContext>>();
        services.AddSingleton<IOutboxDispatcher>(sp => sp.GetRequiredService<OutboxDispatcher<TContext>>());
        services.AddHostedService<OutboxProcessor<TContext>>();
        services.AddScoped<IDataRetentionTask, OutboxRetention<TContext>>();
        services.AddHealthChecks().AddDbContextCheck<TContext>($"db-{typeof(TContext).Name}", tags: ["db"]);
        return services;
    }

    public static void ConfigureProvider(DbContextOptionsBuilder options, DatabaseOptions database, Type contextType)
    {
        var schema = SchemaFor(contextType);
        var migrationsAssembly = contextType.Assembly.GetName().Name;
        switch (database.Provider)
        {
            case DatabaseProviders.SqlServer:
                options.UseSqlServer(database.ConnectionString, sql =>
                {
                    sql.MigrationsAssembly(migrationsAssembly);
                    sql.MigrationsHistoryTable("__EFMigrationsHistory", schema);
                    sql.EnableRetryOnFailure();
                });
                break;
            case DatabaseProviders.Sqlite:
                options.UseSqlite(database.ConnectionString, sqlite => sqlite.MigrationsAssembly(migrationsAssembly));
                break;
            default:
                throw new InvalidOperationException($"Unknown database provider '{database.Provider}'.");
        }
    }

    private static string SchemaFor(Type contextType) =>
        contextType.Name.Replace("DbContext", string.Empty, StringComparison.Ordinal).ToLowerInvariant();

    /// <summary>
    /// Creates the SQLite schema from the model (dev/test) or applies migrations (SQL Server, when enabled).
    /// </summary>
    public static async Task InitializeModuleDatabasesAsync(this IServiceProvider services, CancellationToken cancellationToken = default)
    {
        using var scope = services.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<IOptions<DatabaseOptions>>().Value;
        foreach (var registration in scope.ServiceProvider.GetServices<ModuleDatabaseRegistration>())
        {
            var db = (ModuleDbContext)scope.ServiceProvider.GetRequiredService(registration.ContextType);
            if (db.Database.IsSqlite())
            {
                await EnsureSqliteTablesAsync(db, cancellationToken);
            }
            else if (database.MigrateOnStartup)
            {
                await db.Database.MigrateAsync(cancellationToken);
            }
        }
    }

    private static async Task EnsureSqliteTablesAsync(ModuleDbContext db, CancellationToken cancellationToken)
    {
        var connectionString = db.Database.GetConnectionString();
        var dataSource = new SqliteConnectionStringBuilder(connectionString).DataSource;
        var directory = Path.GetDirectoryName(dataSource);
        if (!string.IsNullOrEmpty(directory) && !dataSource.StartsWith("file:", StringComparison.Ordinal) && dataSource != ":memory:")
        {
            Directory.CreateDirectory(directory);
        }

        // Several module contexts share one SQLite file, so check for this module's own outbox table
        // instead of "any table exists" (which is what EnsureCreated does).
        await db.Database.OpenConnectionAsync(cancellationToken);
        try
        {
            var connection = db.Database.GetDbConnection();
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = $name";
            var parameter = command.CreateParameter();
            parameter.ParameterName = "$name";
            parameter.Value = db.OutboxTableName;
            command.Parameters.Add(parameter);
            var exists = Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken), System.Globalization.CultureInfo.InvariantCulture) > 0;
            if (!exists)
            {
                await db.GetService<IRelationalDatabaseCreator>().CreateTablesAsync(cancellationToken);
            }
        }
        finally
        {
            await db.Database.CloseConnectionAsync();
        }
    }
}
