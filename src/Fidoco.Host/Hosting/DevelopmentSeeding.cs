using Fidoco.SharedKernel.Hosting;

namespace Fidoco.Host.Hosting;

internal static class DevelopmentSeeding
{
    /// <summary>Runs all module seeders when Seed:DevelopmentData is true in the Development environment.</summary>
    public static async Task SeedDevelopmentDataAsync(this WebApplication app)
    {
        if (!app.Environment.IsDevelopment() || !app.Configuration.GetValue<bool>("Seed:DevelopmentData"))
        {
            return;
        }

        using var scope = app.Services.CreateScope();
        foreach (var seeder in scope.ServiceProvider.GetServices<IDevelopmentSeeder>().OrderBy(s => s.Order))
        {
            await seeder.SeedAsync(scope.ServiceProvider, app.Lifetime.ApplicationStopping);
        }
    }
}
