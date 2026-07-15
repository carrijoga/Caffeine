namespace Caffeine.Core.Timers;

/// <summary>User-configurable Pomodoro lengths; spec defaults 25/5/15, 4 cycles.</summary>
public sealed class PomodoroConfig
{
    public int WorkMinutes { get; set; } = 25;

    public int ShortBreakMinutes { get; set; } = 5;

    public int LongBreakMinutes { get; set; } = 15;

    public int CyclesPerLongBreak { get; set; } = 4;

    /// <summary>
    /// Clamps every value to at least 1. A hand-edited timers.json with zero
    /// cycles would otherwise divide by zero when a work phase completes.
    /// </summary>
    public void Sanitize()
    {
        WorkMinutes = Math.Max(1, WorkMinutes);
        ShortBreakMinutes = Math.Max(1, ShortBreakMinutes);
        LongBreakMinutes = Math.Max(1, LongBreakMinutes);
        CyclesPerLongBreak = Math.Max(1, CyclesPerLongBreak);
    }
}
