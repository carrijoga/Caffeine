namespace Caffeine.Core.Timers;

/// <summary>Document persisted to timers.json via JsonStore&lt;TimersConfig&gt;.</summary>
public sealed class TimersConfig
{
    public PomodoroConfig Pomodoro { get; set; } = new();
}
