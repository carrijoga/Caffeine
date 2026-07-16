# Life Hub Stage 5: Polish Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ship the final polish stage of the Caffeine life-hub: rewrite the Welcome page to describe the four-module hub, tidy the README, confirm/tune empty states, and clear the deferred-minors backlog (live date-rollover refresh, live theme-switch retint, `SavePomodoroConfig` sanitize, rename-flyout Enter-to-save, and a `LastSevenDays` test-coverage gap).

**Architecture:** Two kinds of change. (1) UI-only edits in `Caffeine.App` — Welcome page copy, empty-state copy, per-page `ActualThemeChanged` re-render, and an Enter key handler — none of which touch Core and none of which are unit-tested (verified by running the app per the `/verify` skill). (2) One new testable Core primitive: a `DayChangeWatcher` that raises an event when the local calendar day rolls over, driven by the existing `IClock` abstraction so it is unit-testable with `FakeClock`; `App` owns one instance and the three module-aware pages (Home, Todos, Habits) subscribe to refresh their day-dependent UI. Plus one pure-logic test-gap fill and one one-line service guard.

**Tech Stack:** .NET 10, WinUI 3 / Windows App SDK 1.8, xUnit, C#. Core library is UI-free (`net10.0-windows`, no WinUI refs).

## Global Constraints

- `Caffeine.Core` references no WinUI/WindowsAppSDK packages — everything in it is unit-testable headlessly (spec §Solution structure). `DayChangeWatcher` goes in Core and must stay UI-free.
- "Today" is the clock's local date: `DateOnly.FromDateTime(clock.UtcNow.LocalDateTime)` (matches `HabitService.Today` and `TodoService.Today`). Day logic flips at local midnight.
- Saves happen immediately on every change; corrupt file on load → never crash (spec §Persistence). No change to this — just don't regress it.
- Streak rule (spec §Habits): consecutive days ending today or yesterday — missing today does not zero the streak until the day is over. `LastSevenDays` returns oldest-first, index 6 = today.
- One JSON file per module in `%LocalAppData%\Caffeine\` via `JsonStore<T>` (spec §Persistence). No new files.
- UI style is code-behind with `x:Name` manipulation — no MVVM/bindings (established pattern across all pages). Rows are rebuilt via `RebuildList`/`BuildRow`.
- Build: `Stop-Process -Name Caffeine -ErrorAction SilentlyContinue; dotnet build src/Caffeine.App/Caffeine.App.csproj -p:Platform=x64 -v:m`
- Test: `dotnet test tests/Caffeine.Core.Tests/Caffeine.Core.Tests.csproj` (Portuguese output: "Aprovado!" = passed, "Com falha: 0" = 0 failed). Baseline entering Stage 5: **59** tests.
- `docs/superpowers/` is gitignored — plan/ledger commits need `git add -f`.
- FontIcon `Glyph` values in code-behind must use `"\uXXXX"` escape form, never literal private-use-area characters (recurring trap in this project).

---

## File map

**Task 1 — `LastSevenDays` test gap (Core test only):**
- Test: `tests/Caffeine.Core.Tests/HabitServiceTests.cs` (add one fact)

**Task 2 — `DayChangeWatcher` (Core, TDD):**
- Create: `src/Caffeine.Core/Common/DayChangeWatcher.cs`
- Test: `tests/Caffeine.Core.Tests/DayChangeWatcherTests.cs`

**Task 3 — `SavePomodoroConfig` sanitize (App):**
- Modify: `src/Caffeine.App/TimersService.cs:129-137`

**Task 4 — Live refresh wiring: date-rollover + theme retint + rename Enter-to-save (App):**
- Modify: `src/Caffeine.App/App.xaml.cs` (own a `DayChangeWatcher`, pump it, expose it)
- Modify: `src/Caffeine.App/Pages/HomePage.xaml.cs` (subscribe to day-change + theme-change; rebuild day-dependent cards)
- Modify: `src/Caffeine.App/Pages/TodosPage.xaml.cs` (subscribe to day-change + theme-change; `RebuildList`)
- Modify: `src/Caffeine.App/Pages/HabitsPage.xaml.cs` (subscribe to day-change + theme-change; `RebuildList`; rename-flyout Enter-to-save)

**Task 5 — Welcome page rewrite + empty-state polish + README (App + docs):**
- Modify: `src/Caffeine.App/Pages/WelcomePage.xaml`
- Modify: `src/Caffeine.App/Pages/TodosPage.xaml` (empty-state copy, if tuned)
- Modify: `src/Caffeine.App/Pages/HabitsPage.xaml` (empty-state copy, if tuned)
- Modify: `README.md`

---

## Task 1: Fill the `LastSevenDays` coverage gap

The existing `HabitServiceTests` covers `LastSevenDays` shape and that the middle days reflect the log, but never asserts index 0 (the oldest day, `Today - 6`) being `true`. This is a pure-logic test with no production change — it locks in that the 7-day window's far edge is inclusive.

**Files:**
- Test: `tests/Caffeine.Core.Tests/HabitServiceTests.cs`

**Interfaces:**
- Consumes: `HabitService(IClock, JsonStore<HabitList>)`, `HabitService.Add(string, string=""), HabitService.SetDone(Guid, DateOnly, bool)`, `HabitService.Today` (`DateOnly`), `HabitService.LastSevenDays(Habit) → IReadOnlyList<bool>` (7 items, oldest first, index 6 = today). All already exist.
- Produces: nothing (test-only).

- [ ] **Step 1: Read the existing test file's conventions**

Open `tests/Caffeine.Core.Tests/HabitServiceTests.cs`. Note how sibling tests build a service: they construct a `FakeClock`, a `JsonStore<HabitList>` over a temp path, add a habit, and mark days done relative to `service.Today`. Match that exact construction (helper method or inline — whichever the file already uses) so this test reads like its neighbors. Do not invent a new fixture pattern.

- [ ] **Step 2: Add the failing test**

Add this fact to `HabitServiceTests.cs`, adapting the service-construction lines to match the file's existing helper (if the file has a `CreateService()`/`NewService()` helper, use it; otherwise inline the same construction the other facts use):

```csharp
[Fact]
public void LastSevenDays_MarksOldestDay_WhenDoneSevenDaysAgo()
{
    // Construct the service the same way the other facts in this file do.
    var clock = new FakeClock();
    var service = new HabitService(clock, NewTempStore());
    Habit habit = service.Add("Read")!;

    // Today - 6 is index 0 (the oldest slot in the 7-day window).
    service.SetDone(habit.Id, service.Today.AddDays(-6), true);

    IReadOnlyList<bool> days = service.LastSevenDays(habit);

    Assert.True(days[0]);   // oldest day is inclusive
    Assert.False(days[5]);  // an untouched interior day
    Assert.False(days[6]);  // today, not marked
}
```

> **Note to implementer:** `NewTempStore()` is a stand-in for whatever this test file already uses to get a `JsonStore<HabitList>` over a throwaway file. If the file constructs the store inline (e.g. `new JsonStore<HabitList>(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json"))`), do that here too. Do not add a new helper if the file doesn't have one.

- [ ] **Step 3: Run the test to verify it passes immediately (no production change needed)**

Run: `dotnet test tests/Caffeine.Core.Tests/Caffeine.Core.Tests.csproj --filter "FullyQualifiedName~LastSevenDays_MarksOldestDay"`
Expected: PASS. This is a characterization test — the production code (`days[i] = habit.CompletedOn.Contains(Today.AddDays(i - 6))`) is already correct; the test documents and guards the far edge that was previously unasserted.

> **Why no RED step here:** this task closes a coverage gap, not a behavior gap. The implementation already satisfies it. If the test *fails*, that is a real regression signal — stop and report it rather than editing production code to match, because the spec's window semantics (index 0 = Today-6, inclusive) are the source of truth.

- [ ] **Step 4: Run the whole suite**

Run: `dotnet test tests/Caffeine.Core.Tests/Caffeine.Core.Tests.csproj`
Expected: "Aprovado!" with 60 passed, 0 failed (was 59).

- [ ] **Step 5: Commit**

```bash
git add tests/Caffeine.Core.Tests/HabitServiceTests.cs
git commit -m "test: assert LastSevenDays includes the oldest day in the window"
```

---

## Task 2: `DayChangeWatcher` in Core (TDD)

A small, clock-driven watcher that tells the UI "the local calendar day just rolled over." Pages compute "today" (due-today counts, streaks, the 7-day dot strip) once when built and never again — so a window left open past midnight shows stale data. This primitive lives in Core (UI-free, `IClock`-driven) so it is unit-testable; `App` (Task 4) drives it from a `DispatcherTimer` and pages subscribe to its event.

Design: the watcher records the local date at construction. Each call to `Poll()` reads the clock; if the local date differs from the last-seen date, it updates the stored date and raises `DayChanged`. Polling (not self-timing) keeps Core free of any timer dependency — the App decides cadence.

**Files:**
- Create: `src/Caffeine.Core/Common/DayChangeWatcher.cs`
- Test: `tests/Caffeine.Core.Tests/DayChangeWatcherTests.cs`

**Interfaces:**
- Consumes: `IClock` (`DateTimeOffset UtcNow { get; }`) from `Caffeine.Core.Common`.
- Produces:
  - `DayChangeWatcher(IClock clock)` — constructor; captures the current local date.
  - `event Action? DayChanged` — raised by `Poll()` when the local date has advanced since the last raise (or since construction).
  - `void Poll()` — reads the clock; raises `DayChanged` once if the local date changed. Multiple day rollovers between polls raise the event once (coalesced) — the UI only needs "something changed, re-read today."

- [ ] **Step 1: Write the failing tests**

Create `tests/Caffeine.Core.Tests/DayChangeWatcherTests.cs`:

```csharp
using Caffeine.Core.Common;
using Xunit;

namespace Caffeine.Core.Tests;

public class DayChangeWatcherTests
{
    [Fact]
    public void Poll_DoesNotFire_WhenStillSameDay()
    {
        var clock = new FakeClock(); // starts 2026-01-01 12:00 local
        var watcher = new DayChangeWatcher(clock);
        int fired = 0;
        watcher.DayChanged += () => fired++;

        clock.Advance(TimeSpan.FromHours(2)); // same calendar day
        watcher.Poll();

        Assert.Equal(0, fired);
    }

    [Fact]
    public void Poll_Fires_WhenDayRollsOver()
    {
        var clock = new FakeClock();
        var watcher = new DayChangeWatcher(clock);
        int fired = 0;
        watcher.DayChanged += () => fired++;

        clock.Advance(TimeSpan.FromDays(1)); // crosses local midnight
        watcher.Poll();

        Assert.Equal(1, fired);
    }

    [Fact]
    public void Poll_FiresOnce_ForMultipleDaysCrossed()
    {
        var clock = new FakeClock();
        var watcher = new DayChangeWatcher(clock);
        int fired = 0;
        watcher.DayChanged += () => fired++;

        clock.Advance(TimeSpan.FromDays(3)); // three rollovers, one poll
        watcher.Poll();

        Assert.Equal(1, fired); // coalesced
    }

    [Fact]
    public void Poll_FiresAgain_OnNextDay()
    {
        var clock = new FakeClock();
        var watcher = new DayChangeWatcher(clock);
        int fired = 0;
        watcher.DayChanged += () => fired++;

        clock.Advance(TimeSpan.FromDays(1));
        watcher.Poll(); // fires (1)
        clock.Advance(TimeSpan.FromDays(1));
        watcher.Poll(); // fires (2)

        Assert.Equal(2, fired);
    }

    [Fact]
    public void Poll_IsIdempotent_WithinTheSameDay_AfterARollover()
    {
        var clock = new FakeClock();
        var watcher = new DayChangeWatcher(clock);
        int fired = 0;
        watcher.DayChanged += () => fired++;

        clock.Advance(TimeSpan.FromDays(1));
        watcher.Poll(); // fires (1)
        watcher.Poll(); // same day now — no additional fire
        watcher.Poll();

        Assert.Equal(1, fired);
    }
}
```

- [ ] **Step 2: Run to verify the tests fail to compile (RED)**

Run: `dotnet test tests/Caffeine.Core.Tests/Caffeine.Core.Tests.csproj --filter "FullyQualifiedName~DayChangeWatcherTests"`
Expected: build FAILS with CS0246 — `DayChangeWatcher` type not found.

- [ ] **Step 3: Write the implementation**

Create `src/Caffeine.Core/Common/DayChangeWatcher.cs`:

```csharp
namespace Caffeine.Core.Common;

/// <summary>
/// Raises <see cref="DayChanged"/> when the local calendar day rolls over.
/// UI-free and clock-driven: the owner calls <see cref="Poll"/> on a cadence
/// (e.g. a DispatcherTimer) and re-reads "today" when the event fires, so a
/// long-running window doesn't show yesterday's counts, streaks, and dots.
/// Multiple rollovers between polls coalesce into a single event.
/// </summary>
public sealed class DayChangeWatcher
{
    private readonly IClock _clock;
    private DateOnly _lastSeen;

    public DayChangeWatcher(IClock clock)
    {
        _clock = clock;
        _lastSeen = Today();
    }

    /// <summary>Raised once when a <see cref="Poll"/> observes a new local date.</summary>
    public event Action? DayChanged;

    /// <summary>Reads the clock; raises <see cref="DayChanged"/> if the local date advanced.</summary>
    public void Poll()
    {
        DateOnly current = Today();
        if (current != _lastSeen)
        {
            _lastSeen = current;
            DayChanged?.Invoke();
        }
    }

    private DateOnly Today() => DateOnly.FromDateTime(_clock.UtcNow.LocalDateTime);
}
```

- [ ] **Step 4: Run the tests to verify GREEN**

Run: `dotnet test tests/Caffeine.Core.Tests/Caffeine.Core.Tests.csproj --filter "FullyQualifiedName~DayChangeWatcherTests"`
Expected: PASS, 5 passed.

- [ ] **Step 5: Run the whole suite**

Run: `dotnet test tests/Caffeine.Core.Tests/Caffeine.Core.Tests.csproj`
Expected: "Aprovado!" with 65 passed, 0 failed (60 from Task 1 + 5).

- [ ] **Step 6: Commit**

```bash
git add src/Caffeine.Core/Common/DayChangeWatcher.cs tests/Caffeine.Core.Tests/DayChangeWatcherTests.cs
git commit -m "feat: add DayChangeWatcher for local midnight rollover detection"
```

---

## Task 3: `SavePomodoroConfig` sanitizes before persisting

`TimersService.SavePomodoroConfig` writes the raw values it is handed straight to `Config.Pomodoro` and saves. The Timers page NumberBoxes have `Minimum="1"` today, so bad values can't get in via the UI — but the ledger flagged this as a latent gap: the config type already owns a `Sanitize()` that clamps every value to ≥ 1 (guarding against a hand-edited `timers.json` that would otherwise divide by zero on a work-phase completion). The ctor already calls `Sanitize()` on load; the save path should too, for symmetry and defense in depth. One line.

**Files:**
- Modify: `src/Caffeine.App/TimersService.cs:129-137`

**Interfaces:**
- Consumes: `PomodoroConfig.Sanitize()` (already exists — clamps `WorkMinutes`, `ShortBreakMinutes`, `LongBreakMinutes`, `CyclesPerLongBreak` each to `Math.Max(1, value)`).
- Produces: no signature change to `SavePomodoroConfig`.

> **No unit test:** `TimersService` lives in `Caffeine.App` (references WinUI), so it is not in the headless test project — consistent with the rest of the App layer. `PomodoroConfig.Sanitize()` itself is already unit-tested in `PomodoroConfigTests`. This change is verified by the final build plus the Task 4 runtime pass.

- [ ] **Step 1: Add the `Sanitize()` call**

In `src/Caffeine.App/TimersService.cs`, in `SavePomodoroConfig`, after the four assignments and before `_store.Save(Config)`, add the sanitize call:

```csharp
public void SavePomodoroConfig(
    int workMinutes, int shortBreakMinutes, int longBreakMinutes, int cyclesPerLongBreak)
{
    Config.Pomodoro.WorkMinutes = workMinutes;
    Config.Pomodoro.ShortBreakMinutes = shortBreakMinutes;
    Config.Pomodoro.LongBreakMinutes = longBreakMinutes;
    Config.Pomodoro.CyclesPerLongBreak = cyclesPerLongBreak;
    Config.Pomodoro.Sanitize(); // clamp to >=1 before persisting (defense in depth; UI already guards)
    _store.Save(Config);
}
```

- [ ] **Step 2: Build**

Run: `Stop-Process -Name Caffeine -ErrorAction SilentlyContinue; dotnet build src/Caffeine.App/Caffeine.App.csproj -p:Platform=x64 -v:m`
Expected: Build succeeded, 0 errors.

- [ ] **Step 3: Commit**

```bash
git add src/Caffeine.App/TimersService.cs
git commit -m "fix: sanitize Pomodoro config on save, not just on load"
```

---

## Task 4: Live refresh wiring — date rollover, theme retint, rename Enter-to-save

Wire the `DayChangeWatcher` from Task 2 into the App and have the three day-dependent pages (Home, Todos, Habits) re-render when the day rolls over. In the same pass, make those pages re-render on `ActualThemeChanged` so brushes pulled from resources in code-behind retint on a live system-theme flip (XAML `ThemeResource` retints automatically, but code-behind `(Brush)Resources[...]` captured at row-build time does not). Also add Enter-to-save to the Habits rename flyout (the add row already has Enter-to-add; the rename flyout doesn't).

This is an integration task: it touches App wiring and three pages, all following the existing subscribe-on-load / unsubscribe-on-unload pattern that the pages already use for `_timers.Ticked` and `_state.Changed`.

**Files:**
- Modify: `src/Caffeine.App/App.xaml.cs`
- Modify: `src/Caffeine.App/Pages/HomePage.xaml.cs`
- Modify: `src/Caffeine.App/Pages/TodosPage.xaml.cs`
- Modify: `src/Caffeine.App/Pages/HabitsPage.xaml.cs`

**Interfaces:**
- Consumes: `DayChangeWatcher(IClock)`, `DayChangeWatcher.DayChanged` (`event Action?`), `DayChangeWatcher.Poll()` from Task 2. `SystemClock` (`Caffeine.Core.Common`, already used in `App.OnLaunched`). `IAppTimer`/`DispatcherAppTimer` (already used: `Interval`, `IsRepeating`, `Tick` event, per `App.State = new(() => new DispatcherAppTimer())` and `TimersService`).
- Produces: `App.DayChanges` (`public DayChangeWatcher DayChanges { get; private set; }`) — pages subscribe to `((App)Application.Current).DayChanges.DayChanged`.

> **No unit test:** all four files are in `Caffeine.App` (WinUI). The rollover *logic* is unit-tested in Task 2; this task is the UI wiring, verified by the runtime `/verify` pass in Step 8.

- [ ] **Step 1: Own and pump a `DayChangeWatcher` in `App`**

In `src/Caffeine.App/App.xaml.cs`:

Add the property near the other service properties (after `public HabitService Habits { get; private set; } = null!;`):

```csharp
public DayChangeWatcher DayChanges { get; private set; } = null!;
```

Add the `using` if not present:

```csharp
using Caffeine.Core.Common;
```

(It is already imported — `JsonStore<TodoList>` and `SystemClock` come from there. Confirm; do not duplicate.)

In `OnLaunched`, after the `Habits = new HabitService(...)` line, construct the watcher on a shared clock and start a slow repeating timer that polls it. Add:

```csharp
DayChanges = new DayChangeWatcher(new SystemClock());
var dayTick = new DispatcherAppTimer
{
    Interval = TimeSpan.FromMinutes(1),
    IsRepeating = true,
};
dayTick.Tick += () => DayChanges.Poll();
dayTick.Start();
_dayTick = dayTick; // keep a reference so it isn't collected
```

Add the backing field near the top of the class (with the other private fields like `_window`, `_trayIcon`):

```csharp
private IAppTimer? _dayTick;
```

> **Cadence rationale:** one poll per minute is plenty — the worst-case staleness after midnight is under a minute, invisible in practice, and the poll is a single `DateOnly` comparison. Do not poll every 500ms; this is not a countdown.

> **Verify `DispatcherAppTimer` surface before writing:** open `src/Caffeine.App` for the `IAppTimer`/`DispatcherAppTimer` definition and confirm the member names used above (`Interval`, `IsRepeating`, `Tick`, `Start()`). The `TimersService` ctor uses `_tick.Interval = ...; _tick.IsRepeating = true; _tick.Tick += OnTick;` — match that exact surface. If `DispatcherAppTimer` has no public `Start()` (e.g. it auto-starts, or starts via a different member), use whatever the codebase's own timers use and adjust this step accordingly; the intent is "a 1-minute repeating tick that calls `DayChanges.Poll()`."

- [ ] **Step 2: Home subscribes to day-change and theme-change**

In `src/Caffeine.App/Pages/HomePage.xaml.cs`, in the constructor where the page already wires `_state.Changed`, `_timers.Ticked`, and the `Unloaded` cleanup, add subscriptions to the app's day watcher and the page's own theme-changed event.

Add a field:

```csharp
private readonly DayChangeWatcher _dayChanges;
```

In the ctor, alongside `_habits = app.Habits;`:

```csharp
_dayChanges = app.DayChanges;
```

(Add `using Caffeine.Core.Common;` at the top if not present.)

Then, in the same block that subscribes the other events (before the existing `Unloaded += ...` — or fold into it), add:

```csharp
_dayChanges.DayChanged += OnDayChanged;
ActualThemeChanged += OnThemeChanged;
```

Extend the existing `Unloaded` handler to unsubscribe both:

```csharp
Unloaded += (_, _) =>
{
    _state.Changed -= OnStateChanged;
    _timers.Ticked -= RefreshTimersCard;
    _dayChanges.DayChanged -= OnDayChanged;
    ActualThemeChanged -= OnThemeChanged;
};
```

Add the two handlers. On a day change, the day-dependent cards are Todos (due-today/overdue counts) and Habits (done-today count + the inline checkboxes' checked state). On a theme change, rebuild the Habits card so its checkbox content is fresh (Home's other cards use XAML `ThemeResource` and retint themselves; only rebuild what's built in code):

```csharp
private void OnDayChanged()
{
    RefreshTodosCard();
    BuildHabitsCard(); // rebuilds checkboxes + refreshes the "N of M done today" caption
}

private void OnThemeChanged(FrameworkElement sender, object args)
{
    // Home's cards are XAML with ThemeResource brushes and retint themselves;
    // no code-built brushes here need manual retinting. Kept for symmetry with
    // the module pages and to re-read anything day/theme-derived if added later.
    BuildHabitsCard();
}
```

> **Signature note:** `ActualThemeChanged` is `TypedEventHandler<FrameworkElement, object>`, so the handler is `(FrameworkElement sender, object args)`. Match exactly.

- [ ] **Step 3: Todos subscribes to day-change and theme-change**

In `src/Caffeine.App/Pages/TodosPage.xaml.cs`:

Add a field and grab the watcher in the ctor:

```csharp
private readonly DayChangeWatcher _dayChanges;
```

In the ctor, after `_todos = ((App)Application.Current).Todos;`:

```csharp
_dayChanges = ((App)Application.Current).DayChanges;
```

(Add `using Caffeine.Core.Common;` if not present.)

At the end of the ctor (after `ViewSelector.SelectedItem = ActiveTab;`), subscribe and arrange cleanup. TodosPage doesn't currently have an `Unloaded` handler — add one:

```csharp
_dayChanges.DayChanged += OnDayChanged;
ActualThemeChanged += OnThemeChanged;
Unloaded += (_, _) =>
{
    _dayChanges.DayChanged -= OnDayChanged;
    ActualThemeChanged -= OnThemeChanged;
};
```

Add the handlers. Both just rebuild the list — `RebuildList` re-reads `_todos.Today` (fixing overdue/due-today captions at rollover) and rebuilds every row's brushes from resources (fixing the critical-red "Overdue" caption and secondary text on a theme flip):

```csharp
private void OnDayChanged() => RebuildList();

private void OnThemeChanged(FrameworkElement sender, object args) => RebuildList();
```

> **Add the `FrameworkElement` reference:** `OnThemeChanged`'s signature needs `Microsoft.UI.Xaml.FrameworkElement`, already in scope via the existing `using Microsoft.UI.Xaml;`. Confirm the file has it (it does — `RoutedEventArgs`/`Visibility` come from there).

- [ ] **Step 4: Habits subscribes to day-change and theme-change, and gets rename Enter-to-save**

In `src/Caffeine.App/Pages/HabitsPage.xaml.cs`:

Add a field and grab the watcher:

```csharp
private readonly DayChangeWatcher _dayChanges;
```

In the ctor, after `_habits = ((App)Application.Current).Habits;`:

```csharp
_dayChanges = ((App)Application.Current).DayChanges;
```

(Add `using Caffeine.Core.Common;` if not present.)

At the end of the ctor (after `RebuildList();`), subscribe with cleanup. HabitsPage has no `Unloaded` handler today — add one:

```csharp
_dayChanges.DayChanged += OnDayChanged;
ActualThemeChanged += OnThemeChanged;
Unloaded += (_, _) =>
{
    _dayChanges.DayChanged -= OnDayChanged;
    ActualThemeChanged -= OnThemeChanged;
};
```

Add the handlers — both rebuild the list, which re-reads `_habits.Today` (fixing streak/dot-strip staleness at rollover) and re-pulls the accent/subtle dot brushes and caption brush from resources (fixing theme flip):

```csharp
private void OnDayChanged() => RebuildList();

private void OnThemeChanged(FrameworkElement sender, object args) => RebuildList();
```

Now add Enter-to-save to the rename flyout. In `BuildRenameFlyout`, the local `box` (the rename `TextBox`) currently has no key handler. Add one that invokes the same save path as the Save button, so Enter commits the rename. Change the flyout construction so the save logic is reachable from both the button click and the Enter key:

```csharp
private Flyout BuildRenameFlyout(Habit habit)
{
    var box = new TextBox { Text = habit.Name, MinWidth = 220 };
    AutomationProperties.SetName(box, "New name");
    var save = new Button
    {
        Content = "Save",
        Style = (Style)Application.Current.Resources["AccentButtonStyle"],
        Margin = new Thickness(0, 8, 0, 0),
    };
    var panel = new StackPanel();
    panel.Children.Add(box);
    panel.Children.Add(save);

    var flyout = new Flyout { Content = panel };

    void Commit()
    {
        _habits.Rename(habit.Id, box.Text);
        flyout.Hide();
        RebuildList();
    }

    save.Click += (_, _) => Commit();
    box.KeyDown += (_, e) =>
    {
        if (e.Key == VirtualKey.Enter)
        {
            Commit();
            e.Handled = true;
        }
    };
    return flyout;
}
```

> `VirtualKey` comes from `Windows.System`, already imported in this file (the add row's `NewNameBox_KeyDown` uses `VirtualKey.Enter`). `KeyRoutedEventArgs` comes from `Microsoft.UI.Xaml.Input`, already imported. Confirm both `using`s are present; do not duplicate.

- [ ] **Step 5: Build**

Run: `Stop-Process -Name Caffeine -ErrorAction SilentlyContinue; dotnet build src/Caffeine.App/Caffeine.App.csproj -p:Platform=x64 -v:m`
Expected: Build succeeded, 0 errors. If you see CS-errors about `DispatcherAppTimer.Start()` or event signatures, reconcile against the actual `IAppTimer`/`DispatcherAppTimer` definition in `src/Caffeine.App` (see Step 1's verify note) — do not guess repeatedly; read the type.

- [ ] **Step 6: Run the Core suite (guard against accidental Core breakage)**

Run: `dotnet test tests/Caffeine.Core.Tests/Caffeine.Core.Tests.csproj`
Expected: "Aprovado!" 65 passed, 0 failed (unchanged — this task adds no Core tests).

- [ ] **Step 7: Commit**

```bash
git add src/Caffeine.App/App.xaml.cs src/Caffeine.App/Pages/HomePage.xaml.cs src/Caffeine.App/Pages/TodosPage.xaml.cs src/Caffeine.App/Pages/HabitsPage.xaml.cs
git commit -m "feat: refresh day-dependent UI on midnight rollover and theme change; rename flyout commits on Enter"
```

- [ ] **Step 8: Runtime verify (`/verify` skill)**

Launch the app and verify via UIA per `.claude/skills/verify/SKILL.md`. Because true midnight rollover can't be forced from the UI, verify what is observable:

1. **Theme retint:** open Habits (add a habit if empty so a row with dots renders, and add a to-do with a past due date on Todos so the critical-red "Overdue" caption renders). Flip the system theme (Settings → Personalization → Colors, or the app's theme if it exposes one). Confirm the Habits dot strip, the "Streak · Best" caption, and the Todos "Overdue" caption re-render in the new theme's colors rather than staying the old theme's brushes. (Before this task, code-built brushes would not retint until you renavigated.)
2. **Rename Enter-to-save:** on Habits, open a habit's rename flyout, type a new name, press **Enter**. Confirm the flyout closes and the row shows the new name (previously Enter did nothing; only the Save button worked).
3. **Rename Save button still works:** repeat with the Save button to confirm no regression.
4. **No console/crash regressions:** confirm add/complete/delete on both pages and the Home card checkboxes still work and don't navigate.

Record the verification result in the task report. If the theme flip is impractical to trigger in the environment, note that explicitly and confirm at minimum that the pages still build their rows correctly and Enter-to-save works.

---

## Task 5: Welcome page rewrite, empty-state polish, README

The Welcome page still describes only the keep-awake feature ("Caffeine keeps your PC and display awake…", three getting-started cards all about the toggle/tray). The spec's headline Stage 5 item is: **Welcome page describes the hub.** Rewrite it to introduce all four modules while keeping the existing visual language (title + intro + `SettingsCard`/`SettingsExpander` list). Then confirm the module-page empty states read well, and give the README a final pass.

**Files:**
- Modify: `src/Caffeine.App/Pages/WelcomePage.xaml`
- Modify: `src/Caffeine.App/Pages/TodosPage.xaml` (only if empty-state copy is tuned)
- Modify: `src/Caffeine.App/Pages/HabitsPage.xaml` (only if empty-state copy is tuned)
- Modify: `README.md`

**Interfaces:**
- Consumes: nothing new. `SettingsCard`/`SettingsExpander` from `CommunityToolkit.WinUI.Controls` (namespace `toolkit`, already declared in `WelcomePage.xaml`).
- Produces: nothing consumed by other tasks (this is the last task).

> **No unit test:** XAML/docs only. Verified by the final build and a Welcome-page glance in the runtime pass.

- [ ] **Step 1: Rewrite the Welcome page**

Replace the body of `src/Caffeine.App/Pages/WelcomePage.xaml` (keep the `<Page>` element, its `x:Class`, and the three `xmlns` declarations including `xmlns:toolkit` exactly as they are). Replace the `<ScrollViewer>…</ScrollViewer>` content with hub-oriented copy: one intro paragraph about the hub, then one `SettingsCard` per module, then keep the "How it works" expander (still accurate and useful) with copy trimmed to the keep-awake mechanism.

Use these exact FontIcon glyphs (Segoe Fluent Icons), written as XAML character references — Home `&#xE80F;`, Habits (repeat/refresh) `&#xE72C;`, Todos (checklist) `&#xE9D5;`, Timers (clock) `&#xE916;`, Awake (coffee cup — reuse the app's `&#xE706;` used on the current step 1), How-it-works `&#xE946;`. (These are the same glyphs the sidebar/cards already use where they overlap; `E9D5` is the Todos sidebar glyph, `E80F` is a home glyph.)

```xml
    <ScrollViewer>
        <StackPanel Padding="36,24,36,36" Spacing="4" MaxWidth="800" HorizontalAlignment="Stretch">

            <TextBlock
                Text="Welcome to Caffeine"
                Style="{StaticResource TitleTextBlockStyle}"
                Margin="0,0,0,4" />
            <TextBlock
                Text="Caffeine is a small hub for your day. It keeps your PC awake when you need it to, and adds a few light-touch tools — habits, a to-do list, and focus timers — that live in the same tray app and open on a single Home dashboard."
                Style="{StaticResource BodyTextBlockStyle}"
                Foreground="{ThemeResource TextFillColorSecondaryBrush}"
                TextWrapping="Wrap"
                Margin="0,0,0,20" />

            <TextBlock
                Text="What's inside"
                Style="{StaticResource BodyStrongTextBlockStyle}"
                Margin="1,0,0,8" />

            <toolkit:SettingsCard
                Header="Home"
                Description="Your day at a glance — one card per module. Check off a habit or a to-do, start a Pomodoro, or toggle keep-awake without leaving the dashboard.">
                <toolkit:SettingsCard.HeaderIcon>
                    <FontIcon Glyph="&#xE80F;" />
                </toolkit:SettingsCard.HeaderIcon>
            </toolkit:SettingsCard>

            <toolkit:SettingsCard
                Header="Habits"
                Description="Small things done daily. Each habit tracks its current streak, best streak, and a 7-day history. Missing today doesn't break the streak until the day is over.">
                <toolkit:SettingsCard.HeaderIcon>
                    <FontIcon Glyph="&#xE72C;" />
                </toolkit:SettingsCard.HeaderIcon>
            </toolkit:SettingsCard>

            <toolkit:SettingsCard
                Header="Todos"
                Description="One list to get things out of your head. Add a to-do with an optional due date; overdue and due-today items sort to the top, with a separate Completed view.">
                <toolkit:SettingsCard.HeaderIcon>
                    <FontIcon Glyph="&#xE9D5;" />
                </toolkit:SettingsCard.HeaderIcon>
            </toolkit:SettingsCard>

            <toolkit:SettingsCard
                Header="Timers"
                Description="Pomodoro with configurable focus and break lengths, a countdown with quick presets, and a stopwatch with laps. One runs at a time and keeps going while you work in other tabs.">
                <toolkit:SettingsCard.HeaderIcon>
                    <FontIcon Glyph="&#xE916;" />
                </toolkit:SettingsCard.HeaderIcon>
            </toolkit:SettingsCard>

            <toolkit:SettingsCard
                Header="Awake"
                Description="The original: keep your PC and display awake so it won't lock or sleep while you're away — during a download, a long read, or a presentation. Toggle it here, on the Home card, or from the tray icon.">
                <toolkit:SettingsCard.HeaderIcon>
                    <FontIcon Glyph="&#xE706;" />
                </toolkit:SettingsCard.HeaderIcon>
            </toolkit:SettingsCard>

            <toolkit:SettingsExpander
                Header="How keep-awake works"
                Description="No simulated keypresses, no timers — zero power overhead"
                Margin="0,16,0,0">
                <toolkit:SettingsExpander.HeaderIcon>
                    <FontIcon Glyph="&#xE946;" />
                </toolkit:SettingsExpander.HeaderIcon>
                <toolkit:SettingsExpander.Items>
                    <toolkit:SettingsCard ContentAlignment="Left">
                        <TextBlock
                            Text="Caffeine asks the Windows power manager to keep the display and system awake using the same API video players use (SetThreadExecutionState). It's a single call when you toggle — the app sits completely idle in between, and Windows automatically clears the request if Caffeine exits. Closing the window keeps it running in the tray; left-click the cup to toggle, right-click for options."
                            Style="{StaticResource BodyTextBlockStyle}"
                            Foreground="{ThemeResource TextFillColorSecondaryBrush}"
                            TextWrapping="Wrap" />
                    </toolkit:SettingsCard>
                </toolkit:SettingsExpander.Items>
            </toolkit:SettingsExpander>
        </StackPanel>
    </ScrollViewer>
```

- [ ] **Step 2: Confirm/tune module empty states**

Both module pages already have empty states:
- `TodosPage.xaml.cs` `RebuildList` sets `EmptyText.Text` to `"All caught up!"` (Active) / `"Nothing completed yet."` (Completed) and toggles visibility on `items.Count == 0`.
- `HabitsPage.xaml` has `EmptyText` = `"No habits yet — add your first above."`, toggled in `RebuildList`.

Read both. Confirm they render (they're wired). **No code change is required unless a string reads poorly** — the spec asks that empty states exist, and they do. If you change any copy, keep it to the string only. Do **not** add new empty-state machinery. If no change is warranted, skip to Step 3 and don't stage these two files.

> **Timers page:** the Stopwatch lap list is the only "list" on Timers; it is inherently empty until the first lap and needs no empty-state message (the running time display carries the state). No change.

- [ ] **Step 3: Final README pass**

Open `README.md`. The module list (Awake/Habits/Todos/Timers) was already added in earlier stages and is current. Give it one editorial pass for consistency with the Welcome copy — confirm the four module bullets match what the app actually does and read cleanly. Make only wording fixes if something is stale or inconsistent; do not restructure the file. If the README is already accurate, make no change and don't stage it.

> **Guard against churn:** the goal is a *correct, consistent* README, not a rewrite. A no-op here is a valid outcome. If you edit, keep the diff minimal.

- [ ] **Step 4: Build**

Run: `Stop-Process -Name Caffeine -ErrorAction SilentlyContinue; dotnet build src/Caffeine.App/Caffeine.App.csproj -p:Platform=x64 -v:m`
Expected: Build succeeded, 0 errors. (A malformed XAML character reference or a stray literal PUA glyph will fail the XAML compile — if so, fix to `&#xXXXX;` form.)

- [ ] **Step 5: Commit**

```bash
git add src/Caffeine.App/Pages/WelcomePage.xaml README.md
# stage the two module pages ONLY if you tuned their empty-state copy in Step 2:
# git add src/Caffeine.App/Pages/TodosPage.xaml src/Caffeine.App/Pages/HabitsPage.xaml
git commit -m "docs: rewrite Welcome page for the four-module hub; README pass"
```

- [ ] **Step 6: Runtime verify (`/verify` skill)**

Launch the app, open the **Welcome** page from the footer, and confirm via UIA/screenshot that it now shows the five module cards (Home, Habits, Todos, Timers, Awake) plus the "How keep-awake works" expander, all icons render (no missing-glyph boxes), and copy reads cleanly in both light and dark theme. Confirm the empty states show on Todos (Completed tab with nothing completed) and Habits (with no habits). Record the result in the task report.

---

## Self-Review

**1. Spec coverage (spec §Staged delivery item 5 — "Welcome page describes the hub, README rewrite, empty states"):**
- Welcome page describes the hub → Task 5, Step 1 (five module cards). ✅
- README rewrite → Task 5, Step 3 (editorial pass; already substantially rewritten in prior stages, so a correctness pass is the right scope). ✅
- Empty states → Task 5, Step 2 (confirm/tune; both pages already have them). ✅
- Deferred-minors sweep (user-requested "everything deferred" scope): date-rollover → Tasks 2+4; theme retint → Task 4; `SavePomodoroConfig` sanitize → Task 3; rename Enter-to-save → Task 4; `LastSevenDays` test gap → Task 1. ✅ All ledger carry-overs from Stages 2–4 addressed.

**2. Placeholder scan:** No "TBD"/"handle edge cases"/"similar to Task N" — every code step shows the code. The one deliberately conditional step (Task 5 Step 2/3, "only if copy reads poorly") is scoped with an explicit "no-op is valid" instruction, not a vague placeholder. ✅

**3. Type consistency:**
- `DayChangeWatcher(IClock)`, `DayChanged` (`event Action?`), `Poll()` — defined in Task 2, consumed identically in Task 4. ✅
- `App.DayChanges` (`DayChangeWatcher`) — produced in Task 4 Step 1, consumed in Steps 2–4 via `((App)Application.Current).DayChanges`. ✅
- `PomodoroConfig.Sanitize()` — existing, called in Task 3. ✅
- `ActualThemeChanged` handler signature `(FrameworkElement, object)` — consistent across all three page edits in Task 4. ✅
- `VirtualKey.Enter` / `KeyDown` — matches the existing add-row handlers in both pages. ✅

**Note carried to execution:** Task 4 Step 1 depends on the exact public surface of `DispatcherAppTimer`/`IAppTimer`. The plan instructs the implementer to read that type and match `TimersService`'s usage (`Interval`, `IsRepeating`, `Tick`) rather than assume `Start()`. This is the one place the plan defers to the codebase — flagged explicitly so it isn't a silent guess.
