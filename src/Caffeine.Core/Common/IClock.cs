namespace Caffeine.Core.Common;

/// <summary>
/// Time source for Core services. Engines compute elapsed/remaining time from
/// clock readings instead of counting ticks, so tests can fast-forward a fake.
/// </summary>
public interface IClock
{
    DateTimeOffset UtcNow { get; }
}
