using System.Text.Json;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Fidoco.Host.Hosting;

/// <summary>JSON health report: overall status plus each check (databases, outbox, Stripe, Mux).</summary>
internal static class HealthResponse
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public static Task WriteAsync(HttpContext context, HealthReport report)
    {
        context.Response.ContentType = "application/json; charset=utf-8";
        var body = new
        {
            status = report.Status.ToString(),
            durationMs = (int)report.TotalDuration.TotalMilliseconds,
            checks = report.Entries.ToDictionary(
                e => e.Key,
                e => new { status = e.Value.Status.ToString(), description = e.Value.Description, data = e.Value.Data }),
        };
        return context.Response.WriteAsync(JsonSerializer.Serialize(body, Options));
    }
}
