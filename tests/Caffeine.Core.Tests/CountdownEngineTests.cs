using Caffeine.Core.Timers;
using Xunit;

namespace Caffeine.Core.Tests;

public class CountdownEngineTests
{
    private readonly FakeClock _clock = new();

    [Fact]
    public void StartSetsDurationAndRuns()
    {
        var engine = new CountdownEngine(_clock);

        engine.Start(TimeSpan.FromMinutes(10));

        Assert.True(engine.IsRunning);
        Assert.Equal(TimeSpan.FromMinutes(10), engine.Duration);
        Assert.Equal(TimeSpan.FromMinutes(10), engine.Remaining);
    }

    [Fact]
    public void RemainingCountsDownWithClock()
    {
        var engine = new CountdownEngine(_clock);
        engine.Start(TimeSpan.FromMinutes(10));

        _clock.Advance(TimeSpan.FromMinutes(4));

        Assert.Equal(TimeSpan.FromMinutes(6), engine.Remaining);
    }

    [Fact]
    public void RemainingNeverGoesNegative()
    {
        var engine = new CountdownEngine(_clock);
        engine.Start(TimeSpan.FromMinutes(1));

        _clock.Advance(TimeSpan.FromMinutes(5));

        Assert.Equal(TimeSpan.Zero, engine.Remaining);
    }

    [Fact]
    public void UpdateFiresCompletedExactlyOnce()
    {
        var engine = new CountdownEngine(_clock);
        int completions = 0;
        engine.Completed += () => completions++;
        engine.Start(TimeSpan.FromMinutes(1));

        _clock.Advance(TimeSpan.FromSeconds(30));
        engine.Update();
        Assert.Equal(0, completions);

        _clock.Advance(TimeSpan.FromSeconds(31));
        engine.Update();
        engine.Update(); // second poll must not re-fire

        Assert.Equal(1, completions);
        Assert.False(engine.IsRunning);
        Assert.Equal(TimeSpan.Zero, engine.Remaining);
    }

    [Fact]
    public void PauseFreezesRemaining_ResumeContinues()
    {
        var engine = new CountdownEngine(_clock);
        engine.Start(TimeSpan.FromMinutes(10));
        _clock.Advance(TimeSpan.FromMinutes(3));

        engine.Pause();
        _clock.Advance(TimeSpan.FromHours(1)); // paused time must not count
        Assert.False(engine.IsRunning);
        Assert.Equal(TimeSpan.FromMinutes(7), engine.Remaining);

        engine.Resume();
        _clock.Advance(TimeSpan.FromMinutes(2));
        Assert.True(engine.IsRunning);
        Assert.Equal(TimeSpan.FromMinutes(5), engine.Remaining);
    }

    [Fact]
    public void ResetRestoresDurationAndStops()
    {
        var engine = new CountdownEngine(_clock);
        engine.Start(TimeSpan.FromMinutes(10));
        _clock.Advance(TimeSpan.FromMinutes(3));

        engine.Reset();

        Assert.False(engine.IsRunning);
        Assert.Equal(TimeSpan.FromMinutes(10), engine.Remaining);
    }

    [Fact]
    public void StartWithZeroDurationDoesNotRun()
    {
        var engine = new CountdownEngine(_clock);

        engine.Start(TimeSpan.Zero);

        Assert.False(engine.IsRunning);
        Assert.Equal(TimeSpan.Zero, engine.Remaining);
    }
}
