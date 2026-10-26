using System.Text.Json;
using System.Text.Json.Serialization;
using Fidoco.Host.Observability;
using Fidoco.Host.Realtime;
using Fidoco.Modules.Catalog;
using Fidoco.Modules.Identity;
using Fidoco.Modules.Notifications;
using Fidoco.Modules.Payments;
using Fidoco.Modules.Playback;
using Fidoco.Modules.Tenants;
using Fidoco.SharedKernel.Persistence;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;

var builder = WebApplication.CreateBuilder(args);
var configuration = builder.Configuration;

builder.AddFidocoObservability();

builder.Services.AddProblemDetails();
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
});
builder.Services.AddHealthChecks();
builder.Services.AddMemoryCache();
// In-memory now; switch to Redis (AddStackExchangeRedisCache) without code changes in the modules.
builder.Services.AddDistributedMemoryCache();
builder.Services.AddSingleton(TimeProvider.System);

var signalR = builder.Services.AddSignalR();
if (!string.IsNullOrEmpty(configuration["Azure:SignalR:ConnectionString"]))
{
    signalR.AddAzureSignalR(configuration["Azure:SignalR:ConnectionString"]);
}

builder.Services
    .AddFidocoSharedKernel(configuration)
    .AddTenantsModule(configuration)
    .AddIdentityModule(configuration)
    .AddNotificationsModule(configuration)
    .AddCatalogModule(configuration)
    .AddPaymentsModule(configuration)
    .AddPlaybackModule(configuration);

var app = builder.Build();

await app.Services.InitializeModuleDatabasesAsync();

app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseDefaultFiles();
app.UseStaticFiles();

var api = app.MapGroup("/api/v1");
api.MapTenantsEndpoints()
    .MapIdentityEndpoints()
    .MapNotificationsEndpoints()
    .MapCatalogEndpoints()
    .MapPaymentsEndpoints()
    .MapPlaybackEndpoints();

app.MapHub<LiveHub>("/hubs/live");
app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks("/health");

// Unknown API routes are 404s; everything else is the SPA.
app.Map("/api/{**rest}", () => Results.NotFound());
app.MapFallbackToFile("index.html");

await app.RunAsync();

/// <summary>Entry point marker used by integration tests (WebApplicationFactory).</summary>
public partial class Program;
