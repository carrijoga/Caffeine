namespace Caffeine.Core.Timers;

/// <summary>User-configurable Pomodoro lengths; spec defaults 25/5/15, 4 cycles.</summary>
public sealed class PomodoroConfig
{
    public int WorkMinutes { get; set; } = 25;

    public int ShortBreakMinutes { get; set; } = 5;

    public int LongBreakMinutes { get; set; } = 15;

    public int CyclesPerLongBreak { get; set; } = 4;
}
