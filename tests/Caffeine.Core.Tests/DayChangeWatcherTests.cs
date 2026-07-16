using Caffeine.Core.Common;
using Xunit;

namespace Caffeine.Core.Tests;

public class DayChangeWatcherTests
{
    [Fact]
    public void Poll_DoesNotFire_WhenStillSameDay()
    {
        var clock = new FakeClock(); // starts 2026-01-01 12:00 local
        var watcher = new DayChangeWatcher(clock);
        int fired = 0;
        watcher.DayChanged += () => fired++;

        clock.Advance(TimeSpan.FromHours(2)); // same calendar day
        watcher.Poll();

        Assert.Equal(0, fired);
    }

    [Fact]
    public void Poll_Fires_WhenDayRollsOver()
    {
        var clock = new FakeClock();
        var watcher = new DayChangeWatcher(clock);
        int fired = 0;
        watcher.DayChanged += () => fired++;

        clock.Advance(TimeSpan.FromDays(1)); // crosses local midnight
        watcher.Poll();

        Assert.Equal(1, fired);
    }

    [Fact]
    public void Poll_FiresOnce_ForMultipleDaysCrossed()
    {
        var clock = new FakeClock();
        var watcher = new DayChangeWatcher(clock);
        int fired = 0;
        watcher.DayChanged += () => fired++;

        clock.Advance(TimeSpan.FromDays(3)); // three rollovers, one poll
        watcher.Poll();

        Assert.Equal(1, fired); // coalesced
    }

    [Fact]
    public void Poll_FiresAgain_OnNextDay()
    {
        var clock = new FakeClock();
        var watcher = new DayChangeWatcher(clock);
        int fired = 0;
        watcher.DayChanged += () => fired++;

        clock.Advance(TimeSpan.FromDays(1));
        watcher.Poll(); // fires (1)
        clock.Advance(TimeSpan.FromDays(1));
        watcher.Poll(); // fires (2)

        Assert.Equal(2, fired);
    }

    [Fact]
    public void Poll_IsIdempotent_WithinTheSameDay_AfterARollover()
    {
        var clock = new FakeClock();
        var watcher = new DayChangeWatcher(clock);
        int fired = 0;
        watcher.DayChanged += () => fired++;

        clock.Advance(TimeSpan.FromDays(1));
        watcher.Poll(); // fires (1)
        watcher.Poll(); // same day now — no additional fire
        watcher.Poll();

        Assert.Equal(1, fired);
    }
}
