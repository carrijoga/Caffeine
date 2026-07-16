using Caffeine.Core.Timers;
using Xunit;

namespace Caffeine.Core.Tests;

public class PomodoroEngineTests
{
    private readonly FakeClock _clock = new();
    private readonly PomodoroConfig _config = new();
    private readonly List<PomodoroPhase> _phases = [];
    private readonly PomodoroEngine _engine;

    public PomodoroEngineTests()
    {
        _engine = new PomodoroEngine(_clock, _config);
        _engine.PhaseChanged += _phases.Add;
    }

    /// <summary>Runs out the current phase and polls Update once.</summary>
    private void CompletePhase(int minutes)
    {
        _clock.Advance(TimeSpan.FromMinutes(minutes));
        _engine.Update();
    }

    [Fact]
    public void StartsIdle()
    {
        Assert.Equal(PomodoroPhase.Idle, _engine.Phase);
        Assert.False(_engine.IsRunning);
        Assert.Equal(0, _engine.CompletedWorkSessions);
        Assert.Equal(TimeSpan.Zero, _engine.Remaining);
    }

    [Fact]
    public void StartBeginsWorkPhase()
    {
        _engine.Start();

        Assert.Equal(PomodoroPhase.Work, _engine.Phase);
        Assert.True(_engine.IsRunning);
        Assert.Equal(TimeSpan.FromMinutes(25), _engine.Remaining);
        Assert.Equal(new[] { PomodoroPhase.Work }, _phases);
    }

    [Fact]
    public void WorkCompletionMovesToShortBreak()
    {
        _engine.Start();

        CompletePhase(25);

        Assert.Equal(PomodoroPhase.ShortBreak, _engine.Phase);
        Assert.True(_engine.IsRunning); // breaks auto-start
        Assert.Equal(TimeSpan.FromMinutes(5), _engine.Remaining);
        Assert.Equal(1, _engine.CompletedWorkSessions);
        Assert.Equal(new[] { PomodoroPhase.Work, PomodoroPhase.ShortBreak }, _phases);
    }

    [Fact]
    public void BreakCompletionReturnsToWork()
    {
        _engine.Start();
        CompletePhase(25); // -> ShortBreak

        CompletePhase(5);

        Assert.Equal(PomodoroPhase.Work, _engine.Phase);
        Assert.True(_engine.IsRunning);
        Assert.Equal(TimeSpan.FromMinutes(25), _engine.Remaining);
    }

    [Fact]
    public void FourthWorkSessionEarnsLongBreak()
    {
        _engine.Start();
        for (int i = 0; i < 3; i++)
        {
            CompletePhase(25); // work done -> short break
            CompletePhase(5);  // break done -> next work
        }

        CompletePhase(25); // fourth work session done

        Assert.Equal(PomodoroPhase.LongBreak, _engine.Phase);
        Assert.Equal(TimeSpan.FromMinutes(15), _engine.Remaining);
        Assert.Equal(4, _engine.CompletedWorkSessions);
    }

    [Fact]
    public void PauseFreezesRemaining_StartResumes()
    {
        _engine.Start();
        _clock.Advance(TimeSpan.FromMinutes(10));

        _engine.Pause();
        _clock.Advance(TimeSpan.FromHours(2)); // paused time must not count
        Assert.False(_engine.IsRunning);
        Assert.Equal(TimeSpan.FromMinutes(15), _engine.Remaining);

        _engine.Start();
        _clock.Advance(TimeSpan.FromMinutes(5));
        Assert.Equal(TimeSpan.FromMinutes(10), _engine.Remaining);
    }

    [Fact]
    public void SkipEndsPhaseAsIfTimeRanOut()
    {
        _engine.Start();

        _engine.Skip(); // skipped work still counts

        Assert.Equal(PomodoroPhase.ShortBreak, _engine.Phase);
        Assert.Equal(1, _engine.CompletedWorkSessions);

        _engine.Skip();

        Assert.Equal(PomodoroPhase.Work, _engine.Phase);
        Assert.Equal(1, _engine.CompletedWorkSessions);
    }

    [Fact]
    public void SkipWhileIdleDoesNothing()
    {
        _engine.Skip();

        Assert.Equal(PomodoroPhase.Idle, _engine.Phase);
        Assert.Empty(_phases);
    }

    [Fact]
    public void ResetReturnsToIdleSilently()
    {
        _engine.Start();
        CompletePhase(25);
        _phases.Clear();

        _engine.Reset();

        Assert.Equal(PomodoroPhase.Idle, _engine.Phase);
        Assert.False(_engine.IsRunning);
        Assert.Equal(0, _engine.CompletedWorkSessions);
        Assert.Empty(_phases); // Reset fires no PhaseChanged
    }

    [Fact]
    public void CustomConfigDrivesDurationsAndCycleCount()
    {
        var config = new PomodoroConfig
        {
            WorkMinutes = 50,
            ShortBreakMinutes = 10,
            LongBreakMinutes = 30,
            CyclesPerLongBreak = 2,
        };
        var engine = new PomodoroEngine(_clock, config);

        engine.Start();
        Assert.Equal(TimeSpan.FromMinutes(50), engine.Remaining);

        _clock.Advance(TimeSpan.FromMinutes(50));
        engine.Update(); // -> ShortBreak (1 of 2)
        Assert.Equal(TimeSpan.FromMinutes(10), engine.Remaining);

        _clock.Advance(TimeSpan.FromMinutes(10));
        engine.Update(); // -> Work
        _clock.Advance(TimeSpan.FromMinutes(50));
        engine.Update(); // second work done -> LongBreak

        Assert.Equal(PomodoroPhase.LongBreak, engine.Phase);
        Assert.Equal(TimeSpan.FromMinutes(30), engine.Remaining);
    }
}
