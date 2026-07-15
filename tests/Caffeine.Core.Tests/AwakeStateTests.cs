using Caffeine.Core.Awake;
using Xunit;

namespace Caffeine.Core.Tests;

public class AwakeStateTests
{
    [Fact]
    public void TimedMode_ArmsTimerAndTurnsOffWhenItFires()
    {
        var timer = new FakeTimer();
        var state = new AwakeState(() => timer) { Interval = TimeSpan.FromMinutes(5) };

        try
        {
            state.Set(true);

            Assert.True(state.IsActive);
            Assert.True(timer.IsRunning);
            Assert.False(timer.IsRepeating);
            Assert.Equal(TimeSpan.FromMinutes(5), timer.Interval);
            Assert.NotNull(state.SessionEndsAt);

            timer.Fire();

            Assert.False(state.IsActive);
            Assert.Null(state.SessionEndsAt);
        }
        finally
        {
            state.Set(false); // always release the real power request
        }
    }

    [Fact]
    public void IndefiniteMode_NeverCreatesTimer()
    {
        bool timerCreated = false;
        var state = new AwakeState(() =>
        {
            timerCreated = true;
            return new FakeTimer();
        });

        try
        {
            state.Set(true);

            Assert.True(state.IsActive);
            Assert.False(timerCreated);
            Assert.Null(state.SessionEndsAt);
        }
        finally
        {
            state.Set(false);
        }
    }

    [Fact]
    public void TurningOff_StopsTimerAndClearsSessionEnd()
    {
        var timer = new FakeTimer();
        var state = new AwakeState(() => timer) { Interval = TimeSpan.FromMinutes(5) };

        try
        {
            state.Set(true);
            state.Set(false);

            Assert.False(state.IsActive);
            Assert.False(timer.IsRunning);
            Assert.Null(state.SessionEndsAt);
        }
        finally
        {
            state.Set(false);
        }
    }
}
