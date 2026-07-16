using Caffeine.Core.Common;

namespace Caffeine.Core.Tests;

/// <summary>Hand-advanced IClock for tests.</summary>
internal sealed class FakeClock : IClock
{
    public DateTimeOffset UtcNow { get; set; } =
        new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    public void Advance(TimeSpan by) => UtcNow += by;

    public void SetTo(DateOnly date) =>
        UtcNow = new DateTimeOffset(date.ToDateTime(new TimeOnly(12, 0)), DateTimeOffset.Now.Offset).ToUniversalTime();
}
