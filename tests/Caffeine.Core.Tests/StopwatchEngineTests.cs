using Caffeine.Core.Timers;
using Xunit;

namespace Caffeine.Core.Tests;

public class StopwatchEngineTests
{
    private readonly FakeClock _clock = new();

    [Fact]
    public void StartsAtZeroAndStopped()
    {
        var sw = new StopwatchEngine(_clock);

        Assert.False(sw.IsRunning);
        Assert.Equal(TimeSpan.Zero, sw.Elapsed);
        Assert.Empty(sw.Laps);
    }

    [Fact]
    public void ElapsedTracksClockWhileRunning()
    {
        var sw = new StopwatchEngine(_clock);
        sw.Start();

        _clock.Advance(TimeSpan.FromSeconds(90));

        Assert.True(sw.IsRunning);
        Assert.Equal(TimeSpan.FromSeconds(90), sw.Elapsed);
    }

    [Fact]
    public void PauseFreezesElapsed_ResumeContinues()
    {
        var sw = new StopwatchEngine(_clock);
        sw.Start();
        _clock.Advance(TimeSpan.FromSeconds(30));

        sw.Pause();
        _clock.Advance(TimeSpan.FromMinutes(10)); // paused time must not count
        Assert.False(sw.IsRunning);
        Assert.Equal(TimeSpan.FromSeconds(30), sw.Elapsed);

        sw.Start();
        _clock.Advance(TimeSpan.FromSeconds(15));
        Assert.Equal(TimeSpan.FromSeconds(45), sw.Elapsed);
    }

    [Fact]
    public void LapRecordsElapsedAtLapTime()
    {
        var sw = new StopwatchEngine(_clock);
        sw.Start();

        _clock.Advance(TimeSpan.FromSeconds(10));
        sw.Lap();
        _clock.Advance(TimeSpan.FromSeconds(20));
        sw.Lap();

        Assert.Equal(
            new[] { TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(30) },
            sw.Laps);
    }

    [Fact]
    public void LapIsIgnoredWhileStopped()
    {
        var sw = new StopwatchEngine(_clock);

        sw.Lap();
        sw.Start();
        _clock.Advance(TimeSpan.FromSeconds(5));
        sw.Pause();
        sw.Lap();

        Assert.Empty(sw.Laps);
    }

    [Fact]
    public void ResetClearsEverything()
    {
        var sw = new StopwatchEngine(_clock);
        sw.Start();
        _clock.Advance(TimeSpan.FromSeconds(10));
        sw.Lap();

        sw.Reset();

        Assert.False(sw.IsRunning);
        Assert.Equal(TimeSpan.Zero, sw.Elapsed);
        Assert.Empty(sw.Laps);
    }
}
