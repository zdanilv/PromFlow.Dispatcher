namespace Configurator.Infrastructure.Persistence.Archive;

public sealed class ArchiveBackoffPolicy
{
    private readonly TimeSpan _baseDelay;
    private readonly TimeSpan _maxDelay;

    public ArchiveBackoffPolicy()
        : this(TimeSpan.FromMilliseconds(100), TimeSpan.FromSeconds(5))
    {
    }

    public ArchiveBackoffPolicy(TimeSpan baseDelay, TimeSpan maxDelay)
    {
        if (baseDelay <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(baseDelay), baseDelay, "Base delay must be positive.");
        }

        if (maxDelay < baseDelay)
        {
            throw new ArgumentOutOfRangeException(nameof(maxDelay), maxDelay, "Max delay must not be less than base delay.");
        }

        _baseDelay = baseDelay;
        _maxDelay = maxDelay;
    }

    public TimeSpan GetDelay(int consecutiveFailureCount)
    {
        if (consecutiveFailureCount <= 0)
        {
            return TimeSpan.Zero;
        }

        var multiplier = 1L << Math.Min(consecutiveFailureCount - 1, 30);
        var delayTicks = _baseDelay.Ticks * multiplier;
        if (delayTicks < 0 || delayTicks > _maxDelay.Ticks)
        {
            return _maxDelay;
        }

        return TimeSpan.FromTicks(delayTicks);
    }
}
