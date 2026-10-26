using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Fidoco.SharedKernel.Observability;

/// <summary>Shared ActivitySource and Meter for custom traces and metrics.</summary>
public static class FidocoTelemetry
{
    public const string SourceName = "Fidoco";
    public const string MeterName = "Fidoco";

    public static readonly ActivitySource ActivitySource = new(SourceName);
    public static readonly Meter Meter = new(MeterName);

    public static readonly Counter<long> PurchasesCompleted = Meter.CreateCounter<long>("fidoco.purchases_completed");
    public static readonly Counter<long> PlaybackSessionsStarted = Meter.CreateCounter<long>("fidoco.playback_sessions_started");
    public static readonly Counter<long> Heartbeats = Meter.CreateCounter<long>("fidoco.heartbeats");
    public static readonly Counter<long> SessionsReplaced = Meter.CreateCounter<long>("fidoco.sessions_replaced");
    public static readonly Counter<long> OutboxFailed = Meter.CreateCounter<long>("fidoco.outbox_failed");
}
