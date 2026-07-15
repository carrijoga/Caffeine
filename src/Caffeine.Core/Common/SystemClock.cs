namespace Caffeine.Core.Common;

/// <summary>Real wall-clock time.</summary>
public sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
