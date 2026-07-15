using Caffeine.Core.Common;
using Caffeine.Core.Timers;

namespace Caffeine;

/// <summary>
/// App-wide owner of the three timer engines. They live here — not in a page —
/// so timers keep running while the user navigates or the window hides to the
/// tray. One repeating timer advances the engines while any is running, fires
/// toasts on Pomodoro phase changes and Countdown completion, and raises
/// Ticked for whichever page is currently showing timer state.
/// One running timer at a time (v1): starting any engine pauses the other two.
/// </summary>
public sealed class TimersService
{
    private readonly JsonStore<TimersConfig> _store = new("timers.json");
    private readonly IAppTimer _tick;

    public TimersService(Func<IAppTimer> timerFactory)
    {
        Config = _store.Load();
        Config.Pomodoro.Sanitize();

        var clock = new SystemClock();
        Pomodoro = new PomodoroEngine(clock, Config.Pomodoro);
        Countdown = new CountdownEngine(clock);
        Stopwatch = new StopwatchEngine(clock);

        Pomodoro.PhaseChanged += OnPomodoroPhaseChanged;
        Countdown.Completed += () => Toasts.Show("Countdown finished", "Time is up.");

        _tick = timerFactory();
        _tick.Interval = TimeSpan.FromMilliseconds(500);
        _tick.IsRepeating = true;
        _tick.Tick += OnTick;
    }

    public TimersConfig Config { get; }

    public PomodoroEngine Pomodoro { get; }

    public CountdownEngine Countdown { get; }

    public StopwatchEngine Stopwatch { get; }

    /// <summary>Raised twice a second while any engine is running.</summary>
    public event Action? Ticked;

    public bool AnyRunning => Pomodoro.IsRunning || Countdown.IsRunning || Stopwatch.IsRunning;

    public void StartPomodoro()
    {
        Countdown.Pause();
        Stopwatch.Pause();
        Pomodoro.Start();
        RefreshTick();
    }

    public void PausePomodoro()
    {
        Pomodoro.Pause();
        RefreshTick();
    }

    public void SkipPomodoro()
    {
        Countdown.Pause();
        Stopwatch.Pause();
        Pomodoro.Skip();
        RefreshTick();
    }

    public void ResetPomodoro()
    {
        Pomodoro.Reset();
        RefreshTick();
    }

    public void StartCountdown(TimeSpan duration)
    {
        Pomodoro.Pause();
        Stopwatch.Pause();
        Countdown.Start(duration);
        RefreshTick();
    }

    public void PauseCountdown()
    {
        Countdown.Pause();
        RefreshTick();
    }

    public void ResumeCountdown()
    {
        Pomodoro.Pause();
        Stopwatch.Pause();
        Countdown.Resume();
        RefreshTick();
    }

    public void ResetCountdown()
    {
        Countdown.Reset();
        RefreshTick();
    }

    public void StartStopwatch()
    {
        Pomodoro.Pause();
        Countdown.Pause();
        Stopwatch.Start();
        RefreshTick();
    }

    public void PauseStopwatch()
    {
        Stopwatch.Pause();
        RefreshTick();
    }

    public void LapStopwatch() => Stopwatch.Lap();

    public void ResetStopwatch()
    {
        Stopwatch.Reset();
        RefreshTick();
    }

    public void SavePomodoroConfig(
        int workMinutes, int shortBreakMinutes, int longBreakMinutes, int cyclesPerLongBreak)
    {
        Config.Pomodoro.WorkMinutes = workMinutes;
        Config.Pomodoro.ShortBreakMinutes = shortBreakMinutes;
        Config.Pomodoro.LongBreakMinutes = longBreakMinutes;
        Config.Pomodoro.CyclesPerLongBreak = cyclesPerLongBreak;
        _store.Save(Config);
    }

    private void OnTick()
    {
        Pomodoro.Update();
        Countdown.Update();
        RefreshTick();
        Ticked?.Invoke();
    }

    /// <summary>The tick timer runs only while an engine does.</summary>
    private void RefreshTick()
    {
        if (AnyRunning)
        {
            _tick.Start();
        }
        else
        {
            _tick.Stop();
        }
    }

    private static void OnPomodoroPhaseChanged(PomodoroPhase phase) =>
        Toasts.Show("Pomodoro", phase switch
        {
            PomodoroPhase.Work => "Focus time — back to work.",
            PomodoroPhase.ShortBreak => "Short break — step away for a bit.",
            _ => "Long break — you earned it.",
        });
}
