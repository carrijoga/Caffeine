using Caffeine.Core.Timers;

namespace Caffeine;

/// <summary>Shared time/phase formatting for the Timers page and the Home card.</summary>
internal static class TimersDisplay
{
    /// <summary>h:mm:ss at an hour or more, m:ss below (whole seconds, truncated).</summary>
    public static string Whole(TimeSpan t) =>
        t.TotalHours >= 1
            ? $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}"
            : $"{t.Minutes}:{t.Seconds:00}";

    /// <summary>Ceiling to whole seconds so a fresh 10-minute countdown reads 10:00, not 9:59.</summary>
    public static string Countdown(TimeSpan t) =>
        Whole(TimeSpan.FromSeconds(Math.Ceiling(t.TotalSeconds)));

    public static string PhaseName(PomodoroPhase phase) => phase switch
    {
        PomodoroPhase.Work => "Focus",
        PomodoroPhase.ShortBreak => "Short break",
        PomodoroPhase.LongBreak => "Long break",
        _ => "Ready",
    };
}
