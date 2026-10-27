namespace Fidoco.SharedKernel.Outbox;

public sealed class OutboxOptions
{
    public const string SectionName = "Outbox";

    /// <summary>Runs the background processors. Tests turn this off and drain the outbox explicitly.</summary>
    public bool Enabled { get; set; } = true;

    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(2);

    public int BatchSize { get; set; } = 20;

    public int MaxAttempts { get; set; } = 10;

    /// <summary>How long a claimed message is locked before another instance may take it over.</summary>
    public TimeSpan LeaseDuration { get; set; } = TimeSpan.FromSeconds(60);

    public TimeSpan MaxBackoff { get; set; } = TimeSpan.FromMinutes(10);
}

public static class OutboxBackoff
{
    /// <summary>Delay before retry number <paramref name="attempts"/>: 2s, 4s, 8s … capped at <paramref name="max"/>.</summary>
    public static TimeSpan Delay(int attempts, TimeSpan max)
    {
        var seconds = Math.Pow(2, Math.Clamp(attempts, 1, 30));
        return TimeSpan.FromSeconds(Math.Min(seconds, max.TotalSeconds));
    }
}
