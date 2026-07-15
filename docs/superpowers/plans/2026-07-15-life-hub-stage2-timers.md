# Life Hub Stage 2 — Timers Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add the Timers module — Pomodoro, Countdown, and Stopwatch — with clock-driven engines in `Caffeine.Core`, xUnit tests, a Timers page with three tabs, toast notifications, and a Timers card on the Home dashboard.

**Architecture:** Engines live in `Caffeine.Core/Timers` behind a new `IClock` abstraction: they compute remaining/elapsed time from clock timestamps (never by counting ticks), so tests fast-forward a `FakeClock` and call `Update()`. The App layer owns a single `TimersService` holding the three engine singletons plus one repeating `DispatcherAppTimer` that advances the engines and raises toasts — timers keep running while the user navigates pages or hides to the tray. The UI only formats and displays engine state.

**Tech Stack:** .NET 10, WinUI 3 (Windows App SDK 1.8.250907003, unpackaged), `Microsoft.Windows.AppNotifications` (ships in WindowsAppSDK — no new package), xUnit 2.9.2.

## Global Constraints

- `Caffeine.Core` must reference **no** WinUI/WindowsAppSDK packages; engines depend only on `IClock`.
- Pomodoro defaults: **25** min work, **5** min short break, **15** min long break, **4** cycles per long break (spec-mandated).
- Countdown quick presets: **5 / 10 / 25 / 60 minutes** (spec-mandated).
- Persistence: new file `timers.json` in `%LocalAppData%\Caffeine\` via the existing `JsonStore<T>` (`src/Caffeine.Core/Common/JsonStore.cs`); save immediately on every config change.
- One running timer at a time (v1): starting any engine pauses the other two. Switching tabs never stops a running timer; a running engine's tab shows a `●` badge.
- Toast notification on **every** Pomodoro phase transition and on Countdown completion (default toast sound satisfies the spec's "sound at zero").
- Build: `dotnet build src/Caffeine.App/Caffeine.App.csproj -p:Platform=x64 -v:m` (run `Stop-Process -Name Caffeine -ErrorAction SilentlyContinue` first — a running exe locks the output).
- Tests: `dotnet test tests/Caffeine.Core.Tests/Caffeine.Core.Tests.csproj` — output is Portuguese ("Aprovado!" = passed). 8 tests exist before this plan; all must stay green.
- Namespaces: Core code `Caffeine.Core.Timers` / `Caffeine.Core.Common`; App code `Caffeine` / `Caffeine.Pages`; tests `Caffeine.Core.Tests`.

---

### Task 1: IClock abstraction + StopwatchEngine (TDD)

**Files:**
- Create: `src/Caffeine.Core/Common/IClock.cs`
- Create: `src/Caffeine.Core/Common/SystemClock.cs`
- Create: `src/Caffeine.Core/Timers/StopwatchEngine.cs`
- Create: `tests/Caffeine.Core.Tests/FakeClock.cs`
- Test: `tests/Caffeine.Core.Tests/StopwatchEngineTests.cs`

**Interfaces:**
- Consumes: nothing new.
- Produces: `IClock { DateTimeOffset UtcNow { get; } }`, `SystemClock : IClock`, test helper `FakeClock : IClock` with settable `UtcNow` and `Advance(TimeSpan)` — Tasks 2 and 3 build on these. `StopwatchEngine(IClock clock)` with `TimeSpan Elapsed`, `bool IsRunning`, `IReadOnlyList<TimeSpan> Laps`, `void Start()`, `void Pause()`, `void Lap()`, `void Reset()`.

- [ ] **Step 1: Write IClock, SystemClock, and FakeClock (plumbing, no test cycle of their own)**

`src/Caffeine.Core/Common/IClock.cs`:

```csharp
namespace Caffeine.Core.Common;

/// <summary>
/// Time source for Core services. Engines compute elapsed/remaining time from
/// clock readings instead of counting ticks, so tests can fast-forward a fake.
/// </summary>
public interface IClock
{
    DateTimeOffset UtcNow { get; }
}
```

`src/Caffeine.Core/Common/SystemClock.cs`:

```csharp
namespace Caffeine.Core.Common;

/// <summary>Real wall-clock time.</summary>
public sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
```

`tests/Caffeine.Core.Tests/FakeClock.cs`:

```csharp
using Caffeine.Core.Common;

namespace Caffeine.Core.Tests;

/// <summary>Hand-advanced IClock for tests.</summary>
internal sealed class FakeClock : IClock
{
    public DateTimeOffset UtcNow { get; set; } =
        new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    public void Advance(TimeSpan by) => UtcNow += by;
}
```

- [ ] **Step 2: Write the failing StopwatchEngine tests**

`tests/Caffeine.Core.Tests/StopwatchEngineTests.cs`:

```csharp
using Caffeine.Core.Timers;

namespace Caffeine.Core.Tests;

public class StopwatchEngineTests
{
    private readonly FakeClock _clock = new();

    [Fact]
    public void StartsAtZeroAndStopped()
    {
        var sw = new StopwatchEngine(_clock);

        Assert.False(sw.IsRunning);
        Assert.Equal(TimeSpan.Zero, sw.Elapsed);
        Assert.Empty(sw.Laps);
    }

    [Fact]
    public void ElapsedTracksClockWhileRunning()
    {
        var sw = new StopwatchEngine(_clock);
        sw.Start();

        _clock.Advance(TimeSpan.FromSeconds(90));

        Assert.True(sw.IsRunning);
        Assert.Equal(TimeSpan.FromSeconds(90), sw.Elapsed);
    }

    [Fact]
    public void PauseFreezesElapsed_ResumeContinues()
    {
        var sw = new StopwatchEngine(_clock);
        sw.Start();
        _clock.Advance(TimeSpan.FromSeconds(30));

        sw.Pause();
        _clock.Advance(TimeSpan.FromMinutes(10)); // paused time must not count
        Assert.False(sw.IsRunning);
        Assert.Equal(TimeSpan.FromSeconds(30), sw.Elapsed);

        sw.Start();
        _clock.Advance(TimeSpan.FromSeconds(15));
        Assert.Equal(TimeSpan.FromSeconds(45), sw.Elapsed);
    }

    [Fact]
    public void LapRecordsElapsedAtLapTime()
    {
        var sw = new StopwatchEngine(_clock);
        sw.Start();

        _clock.Advance(TimeSpan.FromSeconds(10));
        sw.Lap();
        _clock.Advance(TimeSpan.FromSeconds(20));
        sw.Lap();

        Assert.Equal(
            new[] { TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(30) },
            sw.Laps);
    }

    [Fact]
    public void LapIsIgnoredWhileStopped()
    {
        var sw = new StopwatchEngine(_clock);

        sw.Lap();
        sw.Start();
        _clock.Advance(TimeSpan.FromSeconds(5));
        sw.Pause();
        sw.Lap();

        Assert.Empty(sw.Laps);
    }

    [Fact]
    public void ResetClearsEverything()
    {
        var sw = new StopwatchEngine(_clock);
        sw.Start();
        _clock.Advance(TimeSpan.FromSeconds(10));
        sw.Lap();

        sw.Reset();

        Assert.False(sw.IsRunning);
        Assert.Equal(TimeSpan.Zero, sw.Elapsed);
        Assert.Empty(sw.Laps);
    }
}
```

- [ ] **Step 3: Run tests to verify they fail**

Run: `dotnet test tests/Caffeine.Core.Tests/Caffeine.Core.Tests.csproj`
Expected: build FAILS with "StopwatchEngine" not found (CS0246) — that is the RED state for a new type.

- [ ] **Step 4: Implement StopwatchEngine**

`src/Caffeine.Core/Timers/StopwatchEngine.cs`:

```csharp
using Caffeine.Core.Common;

namespace Caffeine.Core.Timers;

/// <summary>
/// Clock-driven stopwatch: elapsed time is computed from IClock readings, so
/// there is no tick loop to drift and tests can fast-forward a fake clock.
/// </summary>
public sealed class StopwatchEngine
{
    private readonly IClock _clock;
    private readonly List<TimeSpan> _laps = [];
    private DateTimeOffset _startedAt;
    private TimeSpan _accumulated;

    public StopwatchEngine(IClock clock)
    {
        _clock = clock;
    }

    public bool IsRunning { get; private set; }

    public IReadOnlyList<TimeSpan> Laps => _laps;

    public TimeSpan Elapsed =>
        IsRunning ? _accumulated + (_clock.UtcNow - _startedAt) : _accumulated;

    public void Start()
    {
        if (IsRunning)
        {
            return;
        }

        _startedAt = _clock.UtcNow;
        IsRunning = true;
    }

    public void Pause()
    {
        if (!IsRunning)
        {
            return;
        }

        _accumulated += _clock.UtcNow - _startedAt;
        IsRunning = false;
    }

    /// <summary>Records the current total elapsed time; ignored while stopped.</summary>
    public void Lap()
    {
        if (IsRunning)
        {
            _laps.Add(Elapsed);
        }
    }

    public void Reset()
    {
        IsRunning = false;
        _accumulated = TimeSpan.Zero;
        _laps.Clear();
    }
}
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test tests/Caffeine.Core.Tests/Caffeine.Core.Tests.csproj`
Expected: 14/14 passing ("Aprovado: 14").

- [ ] **Step 6: Commit**

```bash
git add src/Caffeine.Core/Common/IClock.cs src/Caffeine.Core/Common/SystemClock.cs src/Caffeine.Core/Timers/StopwatchEngine.cs tests/Caffeine.Core.Tests/FakeClock.cs tests/Caffeine.Core.Tests/StopwatchEngineTests.cs
git commit -m "feat: Add IClock abstraction and StopwatchEngine"
```

---

### Task 2: CountdownEngine (TDD)

**Files:**
- Create: `src/Caffeine.Core/Timers/CountdownEngine.cs`
- Test: `tests/Caffeine.Core.Tests/CountdownEngineTests.cs`

**Interfaces:**
- Consumes: `IClock` and test helper `FakeClock` from Task 1.
- Produces: `CountdownEngine(IClock clock)` with `TimeSpan Duration`, `TimeSpan Remaining`, `bool IsRunning`, `event Action? Completed`, `void Start(TimeSpan duration)`, `void Pause()`, `void Resume()`, `void Reset()`, `void Update()`. Task 4's `TimersService` calls `Update()` from its tick timer and forwards `Completed` to a toast.

- [ ] **Step 1: Write the failing tests**

`tests/Caffeine.Core.Tests/CountdownEngineTests.cs`:

```csharp
using Caffeine.Core.Timers;

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
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Caffeine.Core.Tests/Caffeine.Core.Tests.csproj`
Expected: build FAILS with CS0246 "CountdownEngine" not found.

- [ ] **Step 3: Implement CountdownEngine**

`src/Caffeine.Core/Timers/CountdownEngine.cs`:

```csharp
using Caffeine.Core.Common;

namespace Caffeine.Core.Timers;

/// <summary>
/// Clock-driven countdown. Remaining time is computed from IClock readings;
/// a periodic Update() call (the UI's refresh tick) detects expiry and fires
/// Completed exactly once.
/// </summary>
public sealed class CountdownEngine
{
    private readonly IClock _clock;
    private DateTimeOffset _startedAt;
    private TimeSpan _remainingAtStart;

    public CountdownEngine(IClock clock)
    {
        _clock = clock;
    }

    public TimeSpan Duration { get; private set; }

    public bool IsRunning { get; private set; }

    public event Action? Completed;

    public TimeSpan Remaining
    {
        get
        {
            var remaining = IsRunning
                ? _remainingAtStart - (_clock.UtcNow - _startedAt)
                : _remainingAtStart;
            return remaining < TimeSpan.Zero ? TimeSpan.Zero : remaining;
        }
    }

    public void Start(TimeSpan duration)
    {
        Duration = duration;
        _remainingAtStart = duration;
        _startedAt = _clock.UtcNow;
        IsRunning = duration > TimeSpan.Zero;
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

    public void Resume()
    {
        if (IsRunning || _remainingAtStart <= TimeSpan.Zero)
        {
            return;
        }

        _startedAt = _clock.UtcNow;
        IsRunning = true;
    }

    /// <summary>Stops and restores the last-started duration.</summary>
    public void Reset()
    {
        IsRunning = false;
        _remainingAtStart = Duration;
    }

    /// <summary>Detects expiry; fires Completed once. Call periodically while running.</summary>
    public void Update()
    {
        if (IsRunning && Remaining == TimeSpan.Zero)
        {
            IsRunning = false;
            _remainingAtStart = TimeSpan.Zero;
            Completed?.Invoke();
        }
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/Caffeine.Core.Tests/Caffeine.Core.Tests.csproj`
Expected: 21/21 passing.

- [ ] **Step 5: Commit**

```bash
git add src/Caffeine.Core/Timers/CountdownEngine.cs tests/Caffeine.Core.Tests/CountdownEngineTests.cs
git commit -m "feat: Add CountdownEngine with single-fire completion"
```

---

### Task 3: PomodoroEngine + config persistence types (TDD)

**Files:**
- Create: `src/Caffeine.Core/Timers/PomodoroPhase.cs`
- Create: `src/Caffeine.Core/Timers/PomodoroConfig.cs`
- Create: `src/Caffeine.Core/Timers/TimersConfig.cs`
- Create: `src/Caffeine.Core/Timers/PomodoroEngine.cs`
- Test: `tests/Caffeine.Core.Tests/PomodoroEngineTests.cs`

**Interfaces:**
- Consumes: `IClock` / `FakeClock` from Task 1.
- Produces:
  - `enum PomodoroPhase { Idle, Work, ShortBreak, LongBreak }`
  - `PomodoroConfig { int WorkMinutes = 25; int ShortBreakMinutes = 5; int LongBreakMinutes = 15; int CyclesPerLongBreak = 4; }` (all settable)
  - `TimersConfig { PomodoroConfig Pomodoro { get; set; } = new(); }` — the `timers.json` document type for `JsonStore<TimersConfig>`
  - `PomodoroEngine(IClock clock, PomodoroConfig config)` with `PomodoroConfig Config`, `PomodoroPhase Phase`, `bool IsRunning`, `int CompletedWorkSessions`, `TimeSpan Remaining`, `event Action<PomodoroPhase>? PhaseChanged` (raised with the **new** phase whenever a phase begins, including the first Start), `void Start()`, `void Pause()`, `void Skip()`, `void Reset()`, `void Update()`.

Semantics the tests below pin down: phases auto-advance and auto-start when their time runs out (`Update()`); `Start()` from Idle begins Work, from paused resumes; `Skip()` ends the current phase as if its time ran out (a skipped Work session still counts); `Reset()` returns to Idle silently (no event) and zeroes the session count; config changes apply from the **next** phase — the live phase keeps the duration it started with.

- [ ] **Step 1: Write the failing tests**

`tests/Caffeine.Core.Tests/PomodoroEngineTests.cs`:

```csharp
using Caffeine.Core.Timers;

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
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Caffeine.Core.Tests/Caffeine.Core.Tests.csproj`
Expected: build FAILS with CS0246 "PomodoroEngine" / "PomodoroConfig" not found.

- [ ] **Step 3: Implement the types**

`src/Caffeine.Core/Timers/PomodoroPhase.cs`:

```csharp
namespace Caffeine.Core.Timers;

public enum PomodoroPhase
{
    Idle,
    Work,
    ShortBreak,
    LongBreak,
}
```

`src/Caffeine.Core/Timers/PomodoroConfig.cs`:

```csharp
namespace Caffeine.Core.Timers;

/// <summary>User-configurable Pomodoro lengths; spec defaults 25/5/15, 4 cycles.</summary>
public sealed class PomodoroConfig
{
    public int WorkMinutes { get; set; } = 25;

    public int ShortBreakMinutes { get; set; } = 5;

    public int LongBreakMinutes { get; set; } = 15;

    public int CyclesPerLongBreak { get; set; } = 4;
}
```

`src/Caffeine.Core/Timers/TimersConfig.cs`:

```csharp
namespace Caffeine.Core.Timers;

/// <summary>Document persisted to timers.json via JsonStore&lt;TimersConfig&gt;.</summary>
public sealed class TimersConfig
{
    public PomodoroConfig Pomodoro { get; set; } = new();
}
```

`src/Caffeine.Core/Timers/PomodoroEngine.cs`:

```csharp
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
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/Caffeine.Core.Tests/Caffeine.Core.Tests.csproj`
Expected: 31/31 passing.

- [ ] **Step 5: Commit**

```bash
git add src/Caffeine.Core/Timers/PomodoroPhase.cs src/Caffeine.Core/Timers/PomodoroConfig.cs src/Caffeine.Core/Timers/TimersConfig.cs src/Caffeine.Core/Timers/PomodoroEngine.cs tests/Caffeine.Core.Tests/PomodoroEngineTests.cs
git commit -m "feat: Add Pomodoro state machine and timers config types"
```

---

### Task 4: TimersService, toasts, and App wiring

**Files:**
- Create: `src/Caffeine.App/TimersService.cs`
- Create: `src/Caffeine.App/Toasts.cs`
- Create: `src/Caffeine.App/TimersDisplay.cs`
- Modify: `src/Caffeine.App/App.xaml.cs`

**Interfaces:**
- Consumes: `PomodoroEngine`, `CountdownEngine`, `StopwatchEngine`, `TimersConfig`, `PomodoroPhase` (Tasks 1–3); existing `IAppTimer` (`src/Caffeine.Core/Common/IAppTimer.cs`), `JsonStore<T>`, `SystemClock`, and the app's `DispatcherAppTimer` (`src/Caffeine.App/DispatcherAppTimer.cs`).
- Produces (Tasks 5 and 6 depend on these exact members):
  - `TimersService` (namespace `Caffeine`): `TimersConfig Config`, `PomodoroEngine Pomodoro`, `CountdownEngine Countdown`, `StopwatchEngine Stopwatch`, `event Action? Ticked`, `bool AnyRunning`, `void StartPomodoro()`, `void PausePomodoro()`, `void SkipPomodoro()`, `void ResetPomodoro()`, `void StartCountdown(TimeSpan duration)`, `void PauseCountdown()`, `void ResumeCountdown()`, `void ResetCountdown()`, `void StartStopwatch()`, `void PauseStopwatch()`, `void LapStopwatch()`, `void ResetStopwatch()`, `void SavePomodoroConfig(int workMinutes, int shortBreakMinutes, int longBreakMinutes, int cyclesPerLongBreak)`
  - `App.Timers` property (`public TimersService Timers { get; private set; } = null!;`)
  - `TimersDisplay` (static, namespace `Caffeine`): `string Whole(TimeSpan t)`, `string Countdown(TimeSpan t)`, `string PhaseName(PomodoroPhase phase)`

No unit tests — this layer is WinUI-bound glue; the gate is a clean build (the pages that exercise it land in Tasks 5–6).

- [ ] **Step 1: Create Toasts.cs**

`src/Caffeine.App/Toasts.cs`:

```csharp
using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;

namespace Caffeine;

/// <summary>Best-effort toast notifications; a failing shell must never take the app down.</summary>
internal static class Toasts
{
    public static void Show(string title, string body)
    {
        try
        {
            AppNotificationManager.Default.Show(
                new AppNotificationBuilder()
                    .AddText(title)
                    .AddText(body)
                    .BuildNotification());
        }
        catch
        {
        }
    }
}
```

- [ ] **Step 2: Create TimersDisplay.cs**

`src/Caffeine.App/TimersDisplay.cs`:

```csharp
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
```

- [ ] **Step 3: Create TimersService.cs**

`src/Caffeine.App/TimersService.cs`:

```csharp
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
```

- [ ] **Step 4: Wire into App.xaml.cs**

In `src/Caffeine.App/App.xaml.cs`:

1. Add the property next to the existing `Usage` property:

```csharp
public TimersService Timers { get; private set; } = null!;
```

2. In `OnLaunched`, immediately after the `Usage = new UsageTracker(...)` line and **before** `_window = new MainWindow();` (pages resolve `App.Timers` in their constructors, and `MainWindow`'s constructor already navigates to Home):

```csharp
Timers = new TimersService(() => new DispatcherAppTimer());
try
{
    Microsoft.Windows.AppNotifications.AppNotificationManager.Default.Register();
}
catch
{
    // No toast support (e.g. shell restrictions) — timers still work.
}
```

3. In `ExitApp()`, before `Exit();`:

```csharp
try
{
    Microsoft.Windows.AppNotifications.AppNotificationManager.Default.Unregister();
}
catch
{
}
```

- [ ] **Step 5: Build and run existing tests**

Run: `Stop-Process -Name Caffeine -ErrorAction SilentlyContinue` then
`dotnet build src/Caffeine.App/Caffeine.App.csproj -p:Platform=x64 -v:m`
Expected: 0 errors, 0 warnings.

Run: `dotnet test tests/Caffeine.Core.Tests/Caffeine.Core.Tests.csproj`
Expected: 31/31 still passing.

- [ ] **Step 6: Commit**

```bash
git add src/Caffeine.App/TimersService.cs src/Caffeine.App/Toasts.cs src/Caffeine.App/TimersDisplay.cs src/Caffeine.App/App.xaml.cs
git commit -m "feat: Add app-level TimersService with toasts and tick loop"
```

---

### Task 5: Timers page + sidebar navigation

**Files:**
- Create: `src/Caffeine.App/Pages/TimersPage.xaml`
- Create: `src/Caffeine.App/Pages/TimersPage.xaml.cs`
- Modify: `src/Caffeine.App/MainWindow.xaml` (sidebar item)
- Modify: `src/Caffeine.App/MainWindow.xaml.cs` (nav switch + doc comment)
- Modify: `.claude/skills/verify/SKILL.md` (nav item list)

**Interfaces:**
- Consumes: `TimersService` via `((App)Application.Current).Timers`, `TimersDisplay`, `PomodoroPhase` (Task 4/3). Existing nav pattern: `MainWindow.NavView_SelectionChanged` switch on `item.Tag`.
- Produces: `Caffeine.Pages.TimersPage`, sidebar tag `"timers"` (Tasks 6's Home card navigates to it via `((App)Application.Current).NavigateTo("timers")`).

- [ ] **Step 1: Create TimersPage.xaml**

```xml
<Page
    x:Class="Caffeine.Pages.TimersPage"
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">

    <ScrollViewer>
        <StackPanel Padding="36,24,36,36" Spacing="16" MaxWidth="800" HorizontalAlignment="Stretch">

            <TextBlock Text="Timers" Style="{StaticResource TitleTextBlockStyle}" />

            <SelectorBar x:Name="TabBar" SelectionChanged="TabBar_SelectionChanged">
                <SelectorBarItem x:Name="PomodoroTab" Text="Pomodoro" IsSelected="True" />
                <SelectorBarItem x:Name="CountdownTab" Text="Countdown" />
                <SelectorBarItem x:Name="StopwatchTab" Text="Stopwatch" />
            </SelectorBar>

            <!-- Pomodoro tab -->
            <StackPanel x:Name="PomodoroPanel" Spacing="12">
                <TextBlock
                    x:Name="PomodoroPhaseText"
                    Text="Ready"
                    Style="{StaticResource SubtitleTextBlockStyle}" />
                <TextBlock
                    x:Name="PomodoroTimeText"
                    Text="25:00"
                    FontSize="56"
                    FontWeight="SemiBold" />
                <TextBlock
                    x:Name="PomodoroSessionsText"
                    Text="0 focus sessions completed"
                    Style="{StaticResource CaptionTextBlockStyle}"
                    Foreground="{ThemeResource TextFillColorSecondaryBrush}" />
                <StackPanel Orientation="Horizontal" Spacing="8">
                    <Button
                        x:Name="PomodoroStartPauseButton"
                        Content="Start"
                        MinWidth="96"
                        Style="{StaticResource AccentButtonStyle}"
                        Click="PomodoroStartPause_Click" />
                    <Button x:Name="PomodoroSkipButton" Content="Skip" IsEnabled="False" Click="PomodoroSkip_Click" />
                    <Button Content="Reset" Click="PomodoroReset_Click" />
                </StackPanel>

                <Expander
                    Header="Pomodoro settings"
                    Margin="0,8,0,0"
                    HorizontalAlignment="Stretch"
                    HorizontalContentAlignment="Stretch">
                    <StackPanel Spacing="8">
                        <NumberBox
                            x:Name="WorkMinutesBox"
                            Header="Focus (minutes)"
                            Minimum="1" Maximum="180" SmallChange="5"
                            SpinButtonPlacementMode="Inline"
                            ValueChanged="PomodoroConfig_Changed" />
                        <NumberBox
                            x:Name="ShortBreakMinutesBox"
                            Header="Short break (minutes)"
                            Minimum="1" Maximum="60"
                            SpinButtonPlacementMode="Inline"
                            ValueChanged="PomodoroConfig_Changed" />
                        <NumberBox
                            x:Name="LongBreakMinutesBox"
                            Header="Long break (minutes)"
                            Minimum="1" Maximum="120" SmallChange="5"
                            SpinButtonPlacementMode="Inline"
                            ValueChanged="PomodoroConfig_Changed" />
                        <NumberBox
                            x:Name="CyclesBox"
                            Header="Focus sessions before a long break"
                            Minimum="1" Maximum="12"
                            SpinButtonPlacementMode="Inline"
                            ValueChanged="PomodoroConfig_Changed" />
                    </StackPanel>
                </Expander>
            </StackPanel>

            <!-- Countdown tab -->
            <StackPanel x:Name="CountdownPanel" Spacing="12" Visibility="Collapsed">
                <TextBlock
                    x:Name="CountdownTimeText"
                    Text="0:00"
                    FontSize="56"
                    FontWeight="SemiBold" />
                <StackPanel Orientation="Horizontal" Spacing="8">
                    <NumberBox
                        x:Name="CountdownHoursBox"
                        Header="Hours"
                        Minimum="0" Maximum="23" Value="0"
                        SpinButtonPlacementMode="Compact"
                        Width="104" />
                    <NumberBox
                        x:Name="CountdownMinutesBox"
                        Header="Minutes"
                        Minimum="0" Maximum="59" Value="10"
                        SpinButtonPlacementMode="Compact"
                        Width="104" />
                    <NumberBox
                        x:Name="CountdownSecondsBox"
                        Header="Seconds"
                        Minimum="0" Maximum="59" Value="0"
                        SpinButtonPlacementMode="Compact"
                        Width="104" />
                </StackPanel>
                <StackPanel Orientation="Horizontal" Spacing="8">
                    <Button Content="5 min" Tag="5" Click="CountdownPreset_Click" />
                    <Button Content="10 min" Tag="10" Click="CountdownPreset_Click" />
                    <Button Content="25 min" Tag="25" Click="CountdownPreset_Click" />
                    <Button Content="60 min" Tag="60" Click="CountdownPreset_Click" />
                </StackPanel>
                <StackPanel Orientation="Horizontal" Spacing="8">
                    <Button
                        x:Name="CountdownStartPauseButton"
                        Content="Start"
                        MinWidth="96"
                        Style="{StaticResource AccentButtonStyle}"
                        Click="CountdownStartPause_Click" />
                    <Button Content="Reset" Click="CountdownReset_Click" />
                </StackPanel>
            </StackPanel>

            <!-- Stopwatch tab -->
            <StackPanel x:Name="StopwatchPanel" Spacing="12" Visibility="Collapsed">
                <TextBlock
                    x:Name="StopwatchTimeText"
                    Text="0:00"
                    FontSize="56"
                    FontWeight="SemiBold" />
                <StackPanel Orientation="Horizontal" Spacing="8">
                    <Button
                        x:Name="StopwatchStartPauseButton"
                        Content="Start"
                        MinWidth="96"
                        Style="{StaticResource AccentButtonStyle}"
                        Click="StopwatchStartPause_Click" />
                    <Button x:Name="StopwatchLapButton" Content="Lap" IsEnabled="False" Click="StopwatchLap_Click" />
                    <Button Content="Reset" Click="StopwatchReset_Click" />
                </StackPanel>
                <ItemsControl x:Name="LapsList" />
            </StackPanel>
        </StackPanel>
    </ScrollViewer>
</Page>
```

- [ ] **Step 2: Create TimersPage.xaml.cs**

```csharp
using Caffeine.Core.Timers;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Caffeine.Pages;

public sealed partial class TimersPage : Page
{
    private readonly TimersService _timers;
    private bool _loadingConfig;

    public TimersPage()
    {
        _timers = ((App)Application.Current).Timers;
        InitializeComponent();

        LoadPomodoroConfig();

        _timers.Ticked += Refresh;
        Unloaded += (_, _) => _timers.Ticked -= Refresh;

        Refresh();
        RefreshLaps();
    }

    private void TabBar_SelectionChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
    {
        PomodoroPanel.Visibility = TabBar.SelectedItem == PomodoroTab ? Visibility.Visible : Visibility.Collapsed;
        CountdownPanel.Visibility = TabBar.SelectedItem == CountdownTab ? Visibility.Visible : Visibility.Collapsed;
        StopwatchPanel.Visibility = TabBar.SelectedItem == StopwatchTab ? Visibility.Visible : Visibility.Collapsed;
    }

    // ----- Pomodoro -----

    private void LoadPomodoroConfig()
    {
        _loadingConfig = true;
        WorkMinutesBox.Value = _timers.Config.Pomodoro.WorkMinutes;
        ShortBreakMinutesBox.Value = _timers.Config.Pomodoro.ShortBreakMinutes;
        LongBreakMinutesBox.Value = _timers.Config.Pomodoro.LongBreakMinutes;
        CyclesBox.Value = _timers.Config.Pomodoro.CyclesPerLongBreak;
        _loadingConfig = false;
    }

    private void PomodoroConfig_Changed(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        // NumberBox reports NaN while the user is clearing/typing in a field.
        if (_loadingConfig
            || double.IsNaN(WorkMinutesBox.Value) || double.IsNaN(ShortBreakMinutesBox.Value)
            || double.IsNaN(LongBreakMinutesBox.Value) || double.IsNaN(CyclesBox.Value))
        {
            return;
        }

        _timers.SavePomodoroConfig(
            (int)WorkMinutesBox.Value,
            (int)ShortBreakMinutesBox.Value,
            (int)LongBreakMinutesBox.Value,
            (int)CyclesBox.Value);
        Refresh();
    }

    private void PomodoroStartPause_Click(object sender, RoutedEventArgs e)
    {
        if (_timers.Pomodoro.IsRunning)
        {
            _timers.PausePomodoro();
        }
        else
        {
            _timers.StartPomodoro();
        }

        Refresh();
    }

    private void PomodoroSkip_Click(object sender, RoutedEventArgs e)
    {
        _timers.SkipPomodoro();
        Refresh();
    }

    private void PomodoroReset_Click(object sender, RoutedEventArgs e)
    {
        _timers.ResetPomodoro();
        Refresh();
    }

    // ----- Countdown -----

    private void CountdownPreset_Click(object sender, RoutedEventArgs e)
    {
        int minutes = int.Parse((string)((Button)sender).Tag);
        CountdownHoursBox.Value = minutes / 60;
        CountdownMinutesBox.Value = minutes % 60;
        CountdownSecondsBox.Value = 0;
        _timers.StartCountdown(TimeSpan.FromMinutes(minutes));
        Refresh();
    }

    private void CountdownStartPause_Click(object sender, RoutedEventArgs e)
    {
        var countdown = _timers.Countdown;
        if (countdown.IsRunning)
        {
            _timers.PauseCountdown();
        }
        else if (countdown.Remaining > TimeSpan.Zero && countdown.Remaining < countdown.Duration)
        {
            _timers.ResumeCountdown();
        }
        else
        {
            _timers.StartCountdown(new TimeSpan(
                BoxValue(CountdownHoursBox), BoxValue(CountdownMinutesBox), BoxValue(CountdownSecondsBox)));
        }

        Refresh();
    }

    private static int BoxValue(NumberBox box) => double.IsNaN(box.Value) ? 0 : (int)box.Value;

    private void CountdownReset_Click(object sender, RoutedEventArgs e)
    {
        _timers.ResetCountdown();
        Refresh();
    }

    // ----- Stopwatch -----

    private void StopwatchStartPause_Click(object sender, RoutedEventArgs e)
    {
        if (_timers.Stopwatch.IsRunning)
        {
            _timers.PauseStopwatch();
        }
        else
        {
            _timers.StartStopwatch();
        }

        Refresh();
    }

    private void StopwatchLap_Click(object sender, RoutedEventArgs e)
    {
        _timers.LapStopwatch();
        RefreshLaps();
    }

    private void StopwatchReset_Click(object sender, RoutedEventArgs e)
    {
        _timers.ResetStopwatch();
        RefreshLaps();
        Refresh();
    }

    /// <summary>Laps only change on Lap/Reset, so the list rebuilds only there — not per tick.</summary>
    private void RefreshLaps() =>
        LapsList.ItemsSource = _timers.Stopwatch.Laps
            .Select((lap, i) => $"Lap {i + 1}   {TimersDisplay.Whole(lap)}")
            .Reverse()
            .ToList();

    // ----- Shared refresh (also driven by TimersService.Ticked) -----

    private void Refresh()
    {
        var pomodoro = _timers.Pomodoro;
        PomodoroPhaseText.Text = TimersDisplay.PhaseName(pomodoro.Phase);
        PomodoroTimeText.Text = pomodoro.Phase == PomodoroPhase.Idle
            ? TimersDisplay.Whole(TimeSpan.FromMinutes(_timers.Config.Pomodoro.WorkMinutes))
            : TimersDisplay.Countdown(pomodoro.Remaining);
        PomodoroSessionsText.Text = pomodoro.CompletedWorkSessions == 1
            ? "1 focus session completed"
            : $"{pomodoro.CompletedWorkSessions} focus sessions completed";
        PomodoroStartPauseButton.Content = pomodoro.IsRunning
            ? "Pause"
            : pomodoro.Phase == PomodoroPhase.Idle ? "Start" : "Resume";
        PomodoroSkipButton.IsEnabled = pomodoro.Phase != PomodoroPhase.Idle;

        var countdown = _timers.Countdown;
        CountdownTimeText.Text = TimersDisplay.Countdown(countdown.Remaining);
        CountdownStartPauseButton.Content = countdown.IsRunning
            ? "Pause"
            : countdown.Remaining > TimeSpan.Zero && countdown.Remaining < countdown.Duration
                ? "Resume"
                : "Start";

        var stopwatch = _timers.Stopwatch;
        StopwatchTimeText.Text = TimersDisplay.Whole(stopwatch.Elapsed);
        StopwatchStartPauseButton.Content = stopwatch.IsRunning ? "Pause" : "Start";
        StopwatchLapButton.IsEnabled = stopwatch.IsRunning;

        // Spec: the running timer's tab shows a badge.
        PomodoroTab.Text = pomodoro.IsRunning ? "Pomodoro ●" : "Pomodoro";
        CountdownTab.Text = countdown.IsRunning ? "Countdown ●" : "Countdown";
        StopwatchTab.Text = stopwatch.IsRunning ? "Stopwatch ●" : "Stopwatch";
    }
}
```

- [ ] **Step 3: Add the sidebar item**

In `src/Caffeine.App/MainWindow.xaml`, insert between the Home item and the Awake item (spec sidebar order: Home, [Habits, Todos in later stages,] Timers, Awake):

```xml
                <NavigationViewItem Content="Timers" Tag="timers">
                    <NavigationViewItem.Icon>
                        <FontIcon FontFamily="Segoe UI Emoji" Glyph="&#x23F1;" />
                    </NavigationViewItem.Icon>
                </NavigationViewItem>
```

In `src/Caffeine.App/MainWindow.xaml.cs`, add a case to the switch in `NavView_SelectionChanged` (above the `"awake"` case):

```csharp
                "timers" => typeof(Pages.TimersPage),
```

and update the `NavigateTo` doc comment to include the new tag:

```csharp
    /// <summary>Selects the sidebar item with the given Tag ("home", "timers", "awake", "settings", "welcome").</summary>
```

- [ ] **Step 4: Update the verify skill nav list**

In `.claude/skills/verify/SKILL.md`, change the nav items line to:

```markdown
- Nav items ("Home", "Timers", "Awake", "Settings", "Welcome to Caffeine"): `SelectionItemPattern.Select()`.
```

- [ ] **Step 5: Build, run, and verify the page**

Follow `.claude/skills/verify/SKILL.md`:

1. `Stop-Process -Name Caffeine -ErrorAction SilentlyContinue`
2. `dotnet build src/Caffeine.App/Caffeine.App.csproj -p:Platform=x64 -v:m` — expected 0 errors.
3. `dotnet test tests/Caffeine.Core.Tests/Caffeine.Core.Tests.csproj` — expected 31/31.
4. Launch the exe, use UIA `SelectionItemPattern` to select the "Timers" nav item, capture a `PrintWindow` screenshot, and confirm the three tabs render with the Pomodoro tab active showing "25:00".
5. Confirm no `crash.log` next to the exe.
6. `Stop-Process -Name Caffeine -ErrorAction SilentlyContinue` when done.

- [ ] **Step 6: Commit**

```bash
git add src/Caffeine.App/Pages/TimersPage.xaml src/Caffeine.App/Pages/TimersPage.xaml.cs src/Caffeine.App/MainWindow.xaml src/Caffeine.App/MainWindow.xaml.cs .claude/skills/verify/SKILL.md
git commit -m "feat: Add Timers page with Pomodoro, Countdown and Stopwatch tabs"
```

---

### Task 6: Home dashboard Timers card + README

**Files:**
- Modify: `src/Caffeine.App/Pages/HomePage.xaml`
- Modify: `src/Caffeine.App/Pages/HomePage.xaml.cs`
- Modify: `README.md`

**Interfaces:**
- Consumes: `TimersService` (`((App)Application.Current).Timers`), `TimersDisplay`, sidebar tag `"timers"` from Task 5. Existing Home patterns: `HubCardButtonStyle`, `HubCaptionStyle`, the `Tapped`-handled inner-control trick on the Awake card, and the `Unloaded` unsubscribe.
- Produces: nothing consumed later.

- [ ] **Step 1: Add the Timers card to HomePage.xaml**

Insert **before** the Awake card's comment line (`<!-- Awake module card: ... -->`) — spec dashboard order is Habits, Todos, Timers, Awake, so Timers sits above Awake:

```xml
            <!-- Timers module card: whole card navigates to the Timers page. -->
            <Button
                Style="{StaticResource HubCardButtonStyle}"
                Click="OpenTimers_Click"
                AutomationProperties.Name="Open Timers"
                Margin="0,0,0,4">
                <Grid ColumnSpacing="16">
                    <Grid.ColumnDefinitions>
                        <ColumnDefinition Width="Auto" />
                        <ColumnDefinition Width="*" />
                        <ColumnDefinition Width="Auto" />
                    </Grid.ColumnDefinitions>

                    <Grid Width="48" Height="48" VerticalAlignment="Center">
                        <Border
                            CornerRadius="24"
                            Background="{ThemeResource SubtleFillColorSecondaryBrush}" />
                        <TextBlock
                            Text="&#x23F1;&#xFE0F;"
                            FontSize="22"
                            HorizontalAlignment="Center"
                            VerticalAlignment="Center" />
                    </Grid>

                    <StackPanel Grid.Column="1" VerticalAlignment="Center" Spacing="2">
                        <TextBlock Text="Timers" Style="{StaticResource BodyStrongTextBlockStyle}" />
                        <TextBlock
                            x:Name="TimersCaption"
                            Text="No timer running"
                            Style="{StaticResource HubCaptionStyle}"
                            TextWrapping="Wrap" />
                    </StackPanel>

                    <Button
                        Grid.Column="2"
                        x:Name="StartPomodoroButton"
                        Content="Start Pomodoro"
                        VerticalAlignment="Center"
                        Tapped="StartPomodoro_Tapped"
                        Click="StartPomodoro_Click" />
                </Grid>
            </Button>
```

Also update the trailing placeholder comment at the bottom of the StackPanel to:

```xml
            <!-- Habits and Todos cards land here in later stages. -->
```

- [ ] **Step 2: Wire up HomePage.xaml.cs**

Apply these edits (final file shown in full below to remove ambiguity):

```csharp
using Caffeine.Core.Awake;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace Caffeine.Pages;

public sealed partial class HomePage : Page
{
    private readonly AwakeState _state;
    private readonly TimersService _timers;
    private bool _updatingToggle;

    public HomePage()
    {
        var app = (App)Application.Current;
        _state = app.State;
        _timers = app.Timers;
        InitializeComponent();

        _state.Changed += OnStateChanged;
        _timers.Ticked += RefreshTimersCard;
        Unloaded += (_, _) =>
        {
            _state.Changed -= OnStateChanged;
            _timers.Ticked -= RefreshTimersCard;
        };

        OnStateChanged(_state.IsActive);
        RefreshTimersCard();
    }

    private void OnStateChanged(bool active)
    {
        _updatingToggle = true;
        AwakeToggle.IsOn = active;
        _updatingToggle = false;

        BadgeActive.Visibility = active ? Visibility.Visible : Visibility.Collapsed;
        BadgeInactive.Visibility = active ? Visibility.Collapsed : Visibility.Visible;
        AwakeCaption.Text = active
            ? _state.KeepScreenOn ? "Keeping your PC and screen awake" : "Keeping your PC awake"
            : "Your PC can sleep";
    }

    private void AwakeToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (!_updatingToggle)
        {
            _state.Set(AwakeToggle.IsOn);
        }
    }

    private void AwakeToggle_Tapped(object sender, TappedRoutedEventArgs e) =>
        e.Handled = true;

    private void OpenAwake_Click(object sender, RoutedEventArgs e) =>
        ((App)Application.Current).NavigateTo("awake");

    private void RefreshTimersCard()
    {
        TimersCaption.Text =
            _timers.Pomodoro.IsRunning
                ? $"Pomodoro — {TimersDisplay.PhaseName(_timers.Pomodoro.Phase)}, {TimersDisplay.Countdown(_timers.Pomodoro.Remaining)} left"
            : _timers.Countdown.IsRunning
                ? $"Countdown — {TimersDisplay.Countdown(_timers.Countdown.Remaining)} left"
            : _timers.Stopwatch.IsRunning
                ? $"Stopwatch — {TimersDisplay.Whole(_timers.Stopwatch.Elapsed)}"
            : "No timer running";
        StartPomodoroButton.IsEnabled = !_timers.Pomodoro.IsRunning;
    }

    private void StartPomodoro_Click(object sender, RoutedEventArgs e)
    {
        _timers.StartPomodoro();
        RefreshTimersCard();
    }

    private void StartPomodoro_Tapped(object sender, TappedRoutedEventArgs e) =>
        e.Handled = true;

    private void OpenTimers_Click(object sender, RoutedEventArgs e) =>
        ((App)Application.Current).NavigateTo("timers");
}
```

- [ ] **Step 3: Mention Timers in README.md**

In `README.md`, find the section describing the app's features/pages (the hub description added in Stage 1) and add one line describing the Timers module, e.g. under the existing module list:

```markdown
- **Timers** — Pomodoro (configurable focus/break lengths with toast notifications), countdown with quick presets, and a stopwatch with laps.
```

Match the surrounding list style exactly; the full README rewrite is Stage 5's job — do not restructure anything else.

- [ ] **Step 4: Build, test, and verify**

1. `Stop-Process -Name Caffeine -ErrorAction SilentlyContinue`
2. `dotnet build src/Caffeine.App/Caffeine.App.csproj -p:Platform=x64 -v:m` — 0 errors.
3. `dotnet test tests/Caffeine.Core.Tests/Caffeine.Core.Tests.csproj` — 31/31.
4. Launch the exe and, per `.claude/skills/verify/SKILL.md` (UIA, PrintWindow):
   - Home shows the Timers card above the Awake card with caption "No timer running".
   - Invoke "Start Pomodoro" (UIA InvokePattern on the button named "Start Pomodoro") — caption changes to "Pomodoro — Focus, 25:00 left" (or a second lower) and a toast appears.
   - Invoke the card ("Open Timers") — app navigates to the Timers page, Pomodoro tab shows "Pomodoro ●" badge and Pause available.
   - Check `%LocalAppData%\Caffeine\` — no `timers.json` yet is fine (it's only written on config change).
   - No `crash.log`.
5. `Stop-Process -Name Caffeine -ErrorAction SilentlyContinue` when done.

- [ ] **Step 5: Commit**

```bash
git add src/Caffeine.App/Pages/HomePage.xaml src/Caffeine.App/Pages/HomePage.xaml.cs README.md
git commit -m "feat: Add Timers card to Home dashboard"
```
