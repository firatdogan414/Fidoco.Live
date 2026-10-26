using Microsoft.AspNetCore.SignalR;

namespace Fidoco.Host.Realtime;

/// <summary>
/// Placeholder hub for future realtime features (live fight card, polls, chat).
/// Scale-out: set Azure:SignalR:ConnectionString and Azure SignalR Service is used automatically.
/// </summary>
internal sealed class LiveHub : Hub;
