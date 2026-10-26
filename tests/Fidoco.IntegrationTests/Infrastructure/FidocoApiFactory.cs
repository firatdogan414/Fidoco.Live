using Fidoco.Modules.Notifications.Email;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Time.Testing;

namespace Fidoco.IntegrationTests.Infrastructure;

/// <summary>
/// The real app on an in-memory SQLite database with fake external services. Background outbox
/// processing is off; tests call <see cref="TestApp.DrainOutboxAsync"/> to run side effects deterministically.
/// </summary>
public class FidocoApiFactory : WebApplicationFactory<Program>
{
    public const string PlatformHost = "platform.test";

    private readonly SqliteConnection? _keepAlive;
    private readonly Dictionary<string, string?> _settings;

    public FidocoApiFactory()
        : this(provider: "Sqlite", connectionString: null)
    {
    }

    protected FidocoApiFactory(string provider, string? connectionString, IDictionary<string, string?>? overrides = null)
    {
        if (connectionString is null)
        {
            connectionString = $"Data Source=file:fidoco-{Guid.NewGuid():N}?mode=memory&cache=shared";
            _keepAlive = new SqliteConnection(connectionString);
            _keepAlive.Open();
        }

        _settings = new Dictionary<string, string?>
        {
            ["Database:Provider"] = provider,
            ["Database:ConnectionString"] = connectionString,
            ["Database:MigrateOnStartup"] = "true",
            ["Outbox:Enabled"] = "false",
            ["Platform:Hosts:0"] = PlatformHost,
            ["Seed:DevelopmentData"] = "false",
            ["Email:Provider"] = "File",
            ["PublicUrls:Template"] = "https://{host}",
            ["RateLimiting:LoginPerMinute"] = "1000",
            ["RateLimiting:CheckoutPerMinute"] = "1000",
            ["RateLimiting:PlaybackTokenPerMinute"] = "1000",
            ["RateLimiting:HeartbeatPerMinute"] = "1000",
        };
        foreach (var (key, value) in overrides ?? new Dictionary<string, string?>())
        {
            _settings[key] = value;
        }
    }

    /// <summary>Starts at the real current time (Stripe signatures and cookies use wall-clock time).</summary>
    public FakeTimeProvider Time { get; } = new(DateTimeOffset.UtcNow);

    internal CapturingEmailSender Emails { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        foreach (var (key, value) in _settings)
        {
            builder.UseSetting(key, value);
        }

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(Time);
            services.RemoveAll<IEmailSender>();
            services.AddSingleton<IEmailSender>(Emails);
            ConfigureFakes(services);
        });
    }

    /// <summary>Hook for replacing external services (payments, video, email) with fakes.</summary>
    protected virtual void ConfigureFakes(IServiceCollection services)
    {
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            _keepAlive?.Dispose();
        }
    }
}
