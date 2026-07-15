using Caffeine.Core.Timers;
using Xunit;

namespace Caffeine.Core.Tests;

public class PomodoroConfigTests
{
    [Fact]
    public void Sanitize_ClampsZeroAndNegativeValuesToOne()
    {
        var config = new PomodoroConfig
        {
            WorkMinutes = 0,
            ShortBreakMinutes = -5,
            LongBreakMinutes = 0,
            CyclesPerLongBreak = 0,
        };

        config.Sanitize();

        Assert.Equal(1, config.WorkMinutes);
        Assert.Equal(1, config.ShortBreakMinutes);
        Assert.Equal(1, config.LongBreakMinutes);
        Assert.Equal(1, config.CyclesPerLongBreak);
    }

    [Fact]
    public void Sanitize_LeavesValidValuesUntouched()
    {
        var config = new PomodoroConfig
        {
            WorkMinutes = 50,
            ShortBreakMinutes = 10,
            LongBreakMinutes = 30,
            CyclesPerLongBreak = 2,
        };

        config.Sanitize();

        Assert.Equal(50, config.WorkMinutes);
        Assert.Equal(10, config.ShortBreakMinutes);
        Assert.Equal(30, config.LongBreakMinutes);
        Assert.Equal(2, config.CyclesPerLongBreak);
    }
}
