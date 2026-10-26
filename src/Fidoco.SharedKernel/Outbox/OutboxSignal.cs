namespace Fidoco.SharedKernel.Outbox;

/// <summary>
/// Wakes the outbox processors right after a commit that wrote outbox rows, so side effects run within
/// milliseconds instead of waiting for the next poll. Polling remains the fallback (other instances).
/// </summary>
public sealed class OutboxSignal
{
    private TaskCompletionSource _current = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>A task that completes on the next <see cref="Notify"/>. Read it before processing.</summary>
    public Task NextSignal => Volatile.Read(ref _current).Task;

    public void Notify()
    {
        var previous = Interlocked.Exchange(ref _current, new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously));
        previous.TrySetResult();
    }
}
