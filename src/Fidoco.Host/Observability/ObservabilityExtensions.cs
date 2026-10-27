using Azure.Monitor.OpenTelemetry.AspNetCore;
using Fidoco.SharedKernel.Observability;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace Fidoco.Host.Observability;

internal static class ObservabilityExtensions
{
    /// <summary>
    /// Structured logging + OpenTelemetry (traces, metrics, logs). Exports to Application Insights when
    /// APPLICATIONINSIGHTS_CONNECTION_STRING is set, otherwise to OTLP when OTEL_EXPORTER_OTLP_ENDPOINT is set.
    /// </summary>
    public static WebApplicationBuilder AddFidocoObservability(this WebApplicationBuilder builder)
    {
        if (!builder.Environment.IsDevelopment())
        {
            builder.Logging.ClearProviders();
            builder.Logging.AddJsonConsole(o => o.IncludeScopes = true);
        }

        builder.Logging.AddOpenTelemetry(o =>
        {
            o.IncludeFormattedMessage = true;
            o.IncludeScopes = true;
        });

        var otel = builder.Services.AddOpenTelemetry()
            .ConfigureResource(r => r.AddService("fidoco-live"))
            .WithTracing(t => t
                .AddAspNetCoreInstrumentation(o => o.Filter = ctx => !ctx.Request.Path.StartsWithSegments("/health"))
                .AddHttpClientInstrumentation()
                .AddSource(FidocoTelemetry.SourceName))
            .WithMetrics(m => m
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddMeter(FidocoTelemetry.MeterName));

        if (!string.IsNullOrEmpty(builder.Configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"]))
        {
            otel.UseAzureMonitor();
        }
        else if (!string.IsNullOrEmpty(builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]))
        {
            otel.UseOtlpExporter();
        }

        return builder;
    }
}
