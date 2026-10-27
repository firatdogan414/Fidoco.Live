using System.Collections.Concurrent;
using Fidoco.Modules.Notifications.Email;

namespace Fidoco.IntegrationTests.Infrastructure;

/// <summary>Collects emails instead of sending them.</summary>
internal sealed class CapturingEmailSender : IEmailSender
{
    public ConcurrentQueue<OutgoingEmail> Sent { get; } = new();

    public Task SendAsync(OutgoingEmail email, CancellationToken cancellationToken)
    {
        Sent.Enqueue(email);
        return Task.CompletedTask;
    }

    public IReadOnlyList<OutgoingEmail> To(string address) =>
        Sent.Where(e => string.Equals(e.To, address, StringComparison.OrdinalIgnoreCase)).ToList();
}
