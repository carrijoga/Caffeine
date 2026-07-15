using Caffeine.Core.Common;

namespace Caffeine.Core.Timers;

/// <summary>
/// Pomodoro state machine: Idle → Work → (ShortBreak | LongBreak) → Work → …
/// Clock-driven like the other engines: a periodic Update() call advances the
/// machine when the current phase runs out, auto-starting the next phase and
/// raising PhaseChanged so the app can toast. Config changes apply from the
/// next phase; the live phase keeps the duration it started with.
/// </summary>
public sealed class PomodoroEngine
{
    private readonly IClock _clock;
    private DateTimeOffset _startedAt;
    private TimeSpan _remainingAtStart;

    public PomodoroEngine(IClock clock, PomodoroConfig config)
    {
        _clock = clock;
        Config = config;
    }

    public PomodoroConfig Config { get; }

    public PomodoroPhase Phase { get; private set; } = PomodoroPhase.Idle;

    public bool IsRunning { get; private set; }

    /// <summary>Work sessions completed since the last Reset.</summary>
    public int CompletedWorkSessions { get; private set; }

    /// <summary>Raised with the new phase whenever a phase begins (including the first Start).</summary>
    public event Action<PomodoroPhase>? PhaseChanged;

    public TimeSpan Remaining
    {
        get
        {
            if (Phase == PomodoroPhase.Idle)
            {
                return TimeSpan.Zero;
            }

            var remaining = IsRunning
                ? _remainingAtStart - (_clock.UtcNow - _startedAt)
                : _remainingAtStart;
            return remaining < TimeSpan.Zero ? TimeSpan.Zero : remaining;
        }
    }

    /// <summary>From Idle, begins a work session; when paused, resumes.</summary>
    public void Start()
    {
        if (Phase == PomodoroPhase.Idle)
        {
            BeginPhase(PomodoroPhase.Work);
        }
        else if (!IsRunning)
        {
            _startedAt = _clock.UtcNow;
            IsRunning = true;
        }
    }

    public void Pause()
    {
        if (!IsRunning)
        {
            return;
        }

        _remainingAtStart = Remaining;
        IsRunning = false;
    }

    /// <summary>Ends the current phase immediately, as if its time ran out.</summary>
    public void Skip()
    {
        if (Phase != PomodoroPhase.Idle)
        {
            AdvancePhase();
        }
    }

    public void Reset()
    {
        Phase = PomodoroPhase.Idle;
        IsRunning = false;
        CompletedWorkSessions = 0;
        _remainingAtStart = TimeSpan.Zero;
    }

    /// <summary>Advances to the next phase once the current one runs out. Call periodically while running.</summary>
    public void Update()
    {
        if (IsRunning && Phase != PomodoroPhase.Idle && Remaining == TimeSpan.Zero)
        {
            AdvancePhase();
        }
    }

    private void AdvancePhase()
    {
        if (Phase == PomodoroPhase.Work)
        {
            CompletedWorkSessions++;
            BeginPhase(CompletedWorkSessions % Config.CyclesPerLongBreak == 0
                ? PomodoroPhase.LongBreak
                : PomodoroPhase.ShortBreak);
        }
        else
        {
            BeginPhase(PomodoroPhase.Work);
        }
    }

    private void BeginPhase(PomodoroPhase phase)
    {
        Phase = phase;
        _remainingAtStart = TimeSpan.FromMinutes(phase switch
        {
            PomodoroPhase.Work => Config.WorkMinutes,
            PomodoroPhase.ShortBreak => Config.ShortBreakMinutes,
            _ => Config.LongBreakMinutes,
        });
        _startedAt = _clock.UtcNow;
        IsRunning = true;
        PhaseChanged?.Invoke(phase);
    }
}
