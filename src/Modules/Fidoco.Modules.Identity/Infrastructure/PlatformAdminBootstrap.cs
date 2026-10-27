using Fidoco.Modules.Identity.Domain;
using Fidoco.Modules.Identity.Integration;
using Fidoco.SharedKernel.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Fidoco.Modules.Identity.Infrastructure;

/// <summary>
/// Creates the first platform admin from Platform:BootstrapAdminEmail when there is none and e-mails an
/// invitation to choose a password (sent to the first host in Platform:Hosts).
/// </summary>
internal sealed class PlatformAdminBootstrap(IServiceScopeFactory scopes, IConfiguration configuration, ILogger<PlatformAdminBootstrap> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var email = configuration["Platform:BootstrapAdminEmail"];
        var host = configuration.GetSection("Platform:Hosts").Get<string[]>()?.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(email) || host is null)
        {
            return;
        }

        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        if (await db.PlatformAdmins.AnyAsync(cancellationToken))
        {
            return;
        }

        var admin = new PlatformAdmin { Id = Guid.CreateVersion7(), Email = Fidoco.SharedKernel.Http.Emails.Normalize(email), CreatedAt = DateTimeOffset.UtcNow };
        db.PlatformAdmins.Add(admin);
        await db.SaveChangesAsync(cancellationToken);
        await scope.ServiceProvider.GetRequiredService<AdminAccounts>()
            .SendPasswordLinkAsync(AdminKind.Platform, admin.Id, null, admin.Email, AdminTokenPurpose.Invite, host, cancellationToken);
        logger.LogInformation("Created the first platform admin and sent an invitation");
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

/// <summary>Development: a platform admin with the shared development password.</summary>
internal sealed class PlatformAdminDevelopmentSeeder : IDevelopmentSeeder
{
    public int Order => 11;

    public async Task SeedAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        var db = services.GetRequiredService<IdentityDbContext>();
        if (await db.PlatformAdmins.AnyAsync(a => a.Email == DevelopmentSeed.PlatformAdminEmail, cancellationToken))
        {
            return;
        }

        var admin = new PlatformAdmin { Id = Guid.CreateVersion7(), Email = DevelopmentSeed.PlatformAdminEmail, CreatedAt = DateTimeOffset.UtcNow };
        admin.PasswordHash = services.GetRequiredService<IPasswordHasher<PlatformAdmin>>().HashPassword(admin, DevelopmentSeed.TenantAdminPassword);
        db.PlatformAdmins.Add(admin);
        await db.SaveChangesAsync(cancellationToken);
    }
}
