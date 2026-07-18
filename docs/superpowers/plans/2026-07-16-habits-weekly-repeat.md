# Habits Weekly Repeat Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let each habit declare which days of the week it repeats on (e.g. "Tuesday, Thursday, Saturday"), instead of every habit being implicitly daily.

**Architecture:** Add a `[Flags] Weekdays` enum and a `Repeat` property to `Habit`. `HabitService` gains schedule-aware helpers (`IsScheduled`, `SetRepeat`) and its streak/history calculations (`CurrentStreak`, `BestStreak`, `LastSevenDays`) become "consecutive scheduled occurrence" aware instead of "consecutive calendar day" aware. `HabitsPage` and `HomePage` filter today's habit rows to scheduled-only, and gain day-toggle pickers (built in code-behind, matching the existing `BuildRenameFlyout` pattern) in the add dialog and edit flyout.

**Tech Stack:** C# / .NET, WinUI 3, xUnit, `System.Text.Json`.

## Global Constraints

- Repeat pattern is **specific days of the week only** — no monthly dates, no "every N days" interval (spec: Scope).
- Existing/new habits with no explicit schedule choice default to `Weekdays.All` (every day) — preserves current behavior with no migration step (spec: Data model).
- A habit not scheduled for today is **hidden entirely** from both `HabitsPage` and `HomePage`'s habit card, not shown-disabled (spec: UI, Today's list).
- `Repeat` must never persist as `Weekdays.None` from the UI — both the add dialog and edit flyout disable their save/primary button while zero days are selected (spec: UI).
- `habits.json` must stay human-readable: `Repeat` serializes as comma-joined names (e.g. `"Tuesday, Thursday, Saturday"`), not a raw int (spec: Data model, Readability of the stored value).
- Changing a habit's `Repeat` does not rewrite history — `CompletedOn` entries on now-unscheduled dates are left alone, simply ignored by forward-looking streak logic (spec: Out of scope).

---

### Task 1: `Weekdays` enum and `Habit.Repeat` property

**Files:**
- Create: `src/Caffeine.Core/Habits/Weekdays.cs`
- Modify: `src/Caffeine.Core/Habits/Habit.cs`
- Test: `tests/Caffeine.Core.Tests/HabitServiceTests.cs`

**Interfaces:**
- Produces: `Caffeine.Core.Habits.Weekdays` enum (`None`, `Sunday=1`, `Monday=2`, `Tuesday=4`, `Wednesday=8`, `Thursday=16`, `Friday=32`, `Saturday=64`, `All`); `Habit.Repeat` property of type `Weekdays`, defaulting to `Weekdays.All`.

- [ ] **Step 1: Write the failing test for the default**

Add to `HabitServiceTests.cs` (after `Add_BlankIcon_GetsDefault`):

```csharp
    [Fact]
    public void Add_DefaultsRepeatToEveryDay()
    {
        var service = CreateService();

        Habit habit = service.Add("stretch")!;

        Assert.Equal(Weekdays.All, habit.Repeat);
    }
```

Add `using Caffeine.Core.Habits;` is already present in the file; no new using needed since `Weekdays` will live in the same namespace.

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/Caffeine.Core.Tests --filter Add_DefaultsRepeatToEveryDay`
Expected: FAIL — build error, `Weekdays` does not exist / `Habit` has no `Repeat` member.

- [ ] **Step 3: Create the `Weekdays` enum**

Create `src/Caffeine.Core/Habits/Weekdays.cs`:

```csharp
namespace Caffeine.Core.Habits;

/// <summary>Which days of the week a habit repeats on.</summary>
[Flags]
public enum Weekdays
{
    None = 0,
    Sunday = 1,
    Monday = 2,
    Tuesday = 4,
    Wednesday = 8,
    Thursday = 16,
    Friday = 32,
    Saturday = 64,
    All = Sunday | Monday | Tuesday | Wednesday | Thursday | Friday | Saturday,
}
```

- [ ] **Step 4: Add `Repeat` to `Habit`**

Modify `src/Caffeine.Core/Habits/Habit.cs`:

```csharp
namespace Caffeine.Core.Habits;

/// <summary>One habit; persisted inside habits.json.</summary>
public sealed class Habit
{
    public const string DefaultIcon = "⭐";

    public Guid Id { get; set; } = Guid.NewGuid();

    public string Name { get; set; } = string.Empty;

    /// <summary>Emoji shown next to the name.</summary>
    public string Icon { get; set; } = DefaultIcon;

    public DateOnly CreatedOn { get; set; }

    /// <summary>Days of the week this habit is scheduled on. Defaults to every day.</summary>
    public Weekdays Repeat { get; set; } = Weekdays.All;

    /// <summary>The completion log: every day this habit was checked off.</summary>
    public HashSet<DateOnly> CompletedOn { get; set; } = new();
}
```

(Only the class summary comment and the new `Repeat` property change; other members are unchanged.)

- [ ] **Step 5: Run test to verify it passes**

Run: `dotnet test tests/Caffeine.Core.Tests --filter Add_DefaultsRepeatToEveryDay`
Expected: PASS

- [ ] **Step 6: Commit**

```bash
git add src/Caffeine.Core/Habits/Weekdays.cs src/Caffeine.Core/Habits/Habit.cs tests/Caffeine.Core.Tests/HabitServiceTests.cs
git commit -m "feat(habits): add Weekdays enum and Habit.Repeat property"
```

---

### Task 2: Human-readable JSON serialization for `Repeat`

**Files:**
- Modify: `src/Caffeine.Core/Common/JsonStore.cs:15`
- Test: `tests/Caffeine.Core.Tests/HabitServiceTests.cs`

**Interfaces:**
- Consumes: `Habit.Repeat` (`Weekdays`) from Task 1.
- Produces: `JsonStore<T>`'s serializer now writes/reads `[Flags]` enum properties as comma-joined names instead of raw ints. No signature changes — this is an internal options change.

- [ ] **Step 1: Write the failing test**

`SetRepeat` doesn't exist until Task 3, so this task's test can't exercise a non-default `Repeat` value yet — it only proves the enum converter is wired up, using `Add`'s existing `Weekdays.All` default. The non-default round-trip (`Repeat_PersistsAsReadableNames_ViaSetRepeat`) is covered once `SetRepeat` lands in Task 3.

Add to `HabitServiceTests.cs`:

```csharp
    [Fact]
    public void Habit_Repeat_SerializesAsReadableNames()
    {
        var service = CreateService();
        service.Add("stretch");

        string json = File.ReadAllText(Path.Combine(_dir, "habits.json"));

        Assert.Contains("\"Repeat\": \"All\"", json);
    }
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/Caffeine.Core.Tests --filter Habit_Repeat_SerializesAsReadableNames`
Expected: FAIL — JSON contains `"Repeat": 127` (or similar raw int), not `"Repeat": "All"`.

- [ ] **Step 3: Add the enum converter to `JsonStore`**

Modify `src/Caffeine.Core/Common/JsonStore.cs:1` and `:15`:

```csharp
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Caffeine.Core.Common;
```

```csharp
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/Caffeine.Core.Tests --filter Habit_Repeat_SerializesAsReadableNames`
Expected: PASS

- [ ] **Step 5: Run the full test suite to check for regressions**

Run: `dotnet test tests/Caffeine.Core.Tests`
Expected: PASS (all existing tests still green — no other stored type currently uses enums, so this is additive-only).

- [ ] **Step 6: Commit**

```bash
git add src/Caffeine.Core/Common/JsonStore.cs tests/Caffeine.Core.Tests/HabitServiceTests.cs
git commit -m "feat(habits): serialize Repeat as readable names in habits.json"
```

---

### Task 3: `HabitService.IsScheduled` and `SetRepeat`

**Files:**
- Modify: `src/Caffeine.Core/Habits/HabitService.cs`
- Test: `tests/Caffeine.Core.Tests/HabitServiceTests.cs`

**Interfaces:**
- Consumes: `Habit.Repeat` (Task 1), readable JSON round-trip (Task 2).
- Produces: `HabitService.IsScheduled(Habit habit, DateOnly date) : bool`; `HabitService.SetRepeat(Guid id, Weekdays repeat) : void`.

- [ ] **Step 1: Write the failing tests**

Add to `HabitServiceTests.cs`:

```csharp
    [Fact]
    public void IsScheduled_ReturnsTrueOnlyForFlaggedDays()
    {
        var service = CreateService();
        Habit habit = service.Add("trash")!;
        service.SetRepeat(habit.Id, Weekdays.Tuesday | Weekdays.Thursday | Weekdays.Saturday);

        var tuesday = new DateOnly(2026, 7, 21);   // a known Tuesday
        var wednesday = new DateOnly(2026, 7, 22); // a known Wednesday

        Assert.True(service.IsScheduled(habit, tuesday));
        Assert.False(service.IsScheduled(habit, wednesday));
    }

    [Fact]
    public void SetRepeat_UpdatesAndPersists_AndIgnoresUnknownId()
    {
        var service = CreateService();
        Habit habit = service.Add("trash")!;

        service.SetRepeat(habit.Id, Weekdays.Monday | Weekdays.Friday);
        Assert.Equal(Weekdays.Monday | Weekdays.Friday, habit.Repeat);

        service.SetRepeat(Guid.NewGuid(), Weekdays.All); // unknown id — no throw, no effect
        Assert.Equal(Weekdays.Monday | Weekdays.Friday, habit.Repeat);

        var reloaded = CreateService();
        Assert.Equal(Weekdays.Monday | Weekdays.Friday, reloaded.Habits.Single().Repeat);
    }

    [Fact]
    public void Repeat_PersistsAsReadableNames_ViaSetRepeat()
    {
        var first = CreateService();
        Habit habit = first.Add("trash")!;
        first.SetRepeat(habit.Id, Weekdays.Tuesday | Weekdays.Thursday | Weekdays.Saturday);

        string json = File.ReadAllText(Path.Combine(_dir, "habits.json"));
        Assert.Contains("Tuesday, Thursday, Saturday", json);

        var second = CreateService();
        Assert.Equal(Weekdays.Tuesday | Weekdays.Thursday | Weekdays.Saturday, second.Habits.Single().Repeat);
    }
```

`Assert.Single(...)` and LINQ `.Single()` require `using System.Linq;` — check the test file's top; if absent, add it.

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Caffeine.Core.Tests --filter "IsScheduled_ReturnsTrueOnlyForFlaggedDays|SetRepeat_UpdatesAndPersists_AndIgnoresUnknownId|Repeat_PersistsAsReadableNames_ViaSetRepeat"`
Expected: FAIL — `HabitService` has no `IsScheduled` or `SetRepeat` members.

- [ ] **Step 3: Implement `IsScheduled` and `SetRepeat`**

Modify `src/Caffeine.Core/Habits/HabitService.cs`. Add after `Delete` (around line 73):

```csharp
    /// <summary>True when <paramref name="habit"/> is scheduled to run on <paramref name="date"/>'s day of week.</summary>
    public bool IsScheduled(Habit habit, DateOnly date) => habit.Repeat.HasFlag(ToWeekdayFlag(date.DayOfWeek));

    /// <summary>Updates which days of the week the habit repeats on.</summary>
    public void SetRepeat(Guid id, Weekdays repeat)
    {
        Habit? habit = _list.Items.FirstOrDefault(h => h.Id == id);
        if (habit is null || habit.Repeat == repeat)
        {
            return;
        }

        habit.Repeat = repeat;
        _store.Save(_list);
    }

    private static Weekdays ToWeekdayFlag(DayOfWeek day) => day switch
    {
        DayOfWeek.Sunday => Weekdays.Sunday,
        DayOfWeek.Monday => Weekdays.Monday,
        DayOfWeek.Tuesday => Weekdays.Tuesday,
        DayOfWeek.Wednesday => Weekdays.Wednesday,
        DayOfWeek.Thursday => Weekdays.Thursday,
        DayOfWeek.Friday => Weekdays.Friday,
        DayOfWeek.Saturday => Weekdays.Saturday,
        _ => Weekdays.None,
    };
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/Caffeine.Core.Tests --filter "IsScheduled_ReturnsTrueOnlyForFlaggedDays|SetRepeat_UpdatesAndPersists_AndIgnoresUnknownId|Repeat_PersistsAsReadableNames_ViaSetRepeat"`
Expected: PASS

- [ ] **Step 5: Remove the now-redundant narrower test from Task 2**

The `Habit_Repeat_SerializesAsReadableNames` test from Task 2 is superseded by `Repeat_PersistsAsReadableNames_ViaSetRepeat`. Leave both — they're cheap and cover slightly different paths (default value vs. explicit `SetRepeat`). No action needed.

- [ ] **Step 6: Run the full test suite**

Run: `dotnet test tests/Caffeine.Core.Tests`
Expected: PASS

- [ ] **Step 7: Commit**

```bash
git add src/Caffeine.Core/Habits/HabitService.cs tests/Caffeine.Core.Tests/HabitServiceTests.cs
git commit -m "feat(habits): add IsScheduled and SetRepeat to HabitService"
```

---

### Task 4: Schedule-aware `CurrentStreak`, `BestStreak`, `LastSevenDays`

**Files:**
- Modify: `src/Caffeine.Core/Habits/HabitService.cs`
- Test: `tests/Caffeine.Core.Tests/HabitServiceTests.cs`

**Interfaces:**
- Consumes: `IsScheduled` from Task 3.
- Produces: `CurrentStreak`, `BestStreak`, `LastSevenDays` keep their existing signatures (`(Habit) : int` / `(Habit) : int` / `(Habit) : IReadOnlyList<bool>`) but now count/skip based on `IsScheduled` instead of every calendar day. For a habit with `Repeat == Weekdays.All`, behavior is byte-for-byte identical to before (all existing tests must keep passing unmodified).

- [ ] **Step 1: Write the failing tests for non-daily schedules**

Add to `HabitServiceTests.cs`. These use fixed dates anchored on a known Tuesday so day-of-week arithmetic is unambiguous — `2026-07-21` is a Tuesday, `2026-07-23` Thursday, `2026-07-25` Saturday.

```csharp
    [Fact]
    public void CurrentStreak_NonDaily_SkipsUnscheduledDays_WithoutBreakingStreak()
    {
        var service = CreateService();
        Habit habit = service.Add("trash")!;
        service.SetRepeat(habit.Id, Weekdays.Tuesday | Weekdays.Thursday | Weekdays.Saturday);

        var tue = new DateOnly(2026, 7, 21);
        var thu = new DateOnly(2026, 7, 23);
        var sat = new DateOnly(2026, 7, 25);

        service.SetDone(habit.Id, tue, true);
        service.SetDone(habit.Id, thu, true);
        service.SetDone(habit.Id, sat, true);

        // "Today" is Saturday: walk service's clock there via FakeClock.
        _clock.SetTo(sat);

        Assert.Equal(3, service.CurrentStreak(habit));
    }

    [Fact]
    public void CurrentStreak_NonDaily_MissedScheduledDay_ZeroesStreak()
    {
        var service = CreateService();
        Habit habit = service.Add("trash")!;
        service.SetRepeat(habit.Id, Weekdays.Tuesday | Weekdays.Thursday | Weekdays.Saturday);

        var tue = new DateOnly(2026, 7, 21);
        var sat = new DateOnly(2026, 7, 25);
        // Thursday (7/23) deliberately left undone.

        service.SetDone(habit.Id, tue, true);
        _clock.SetTo(sat);

        Assert.Equal(0, service.CurrentStreak(habit)); // Thursday was scheduled and missed, breaking the run
    }

    [Fact]
    public void BestStreak_NonDaily_CountsOnlyScheduledOccurrences()
    {
        var service = CreateService();
        Habit habit = service.Add("trash")!;
        service.SetRepeat(habit.Id, Weekdays.Tuesday | Weekdays.Thursday | Weekdays.Saturday);

        var tue = new DateOnly(2026, 7, 21);
        var thu = new DateOnly(2026, 7, 23);
        var sat = new DateOnly(2026, 7, 25);

        service.SetDone(habit.Id, tue, true);
        service.SetDone(habit.Id, thu, true);
        service.SetDone(habit.Id, sat, true);

        Assert.Equal(3, service.BestStreak(habit));
    }

    [Fact]
    public void LastSevenDays_NonDaily_ReturnsLastSevenScheduledDates()
    {
        var service = CreateService();
        Habit habit = service.Add("trash")!;
        service.SetRepeat(habit.Id, Weekdays.Tuesday | Weekdays.Thursday | Weekdays.Saturday);

        var sat = new DateOnly(2026, 7, 25);
        _clock.SetTo(sat);
        service.SetDone(habit.Id, sat, true); // only today (the 7th scheduled slot back) is done

        IReadOnlyList<bool> days = service.LastSevenDays(habit);

        Assert.Equal(7, days.Count);
        Assert.True(days[6]);  // today (Sat 7/25) — done
        Assert.False(days[5]); // Thu 7/23 — not done
        Assert.False(days[0]); // the oldest of the 7 scheduled dates back — not done
    }

    [Fact]
    public void CurrentStreak_Daily_UnchangedFromBeforeThisFeature()
    {
        // Regression guard: Weekdays.All must reproduce the exact prior calendar-day behavior.
        var service = CreateService();
        Habit habit = service.Add("read")!;
        MarkDays(service, habit.Id, service.Today.AddDays(-2), 3);

        Assert.Equal(3, service.CurrentStreak(habit));
    }
```

This test plan calls `_clock.SetTo(DateOnly)`, which doesn't exist on `FakeClock` yet (it currently only has a settable `UtcNow` property and `Advance(TimeSpan)` — see Step 2).

- [ ] **Step 2: Add `SetTo` to `FakeClock`**

`FakeClock` (`tests/Caffeine.Core.Tests/FakeClock.cs`) is:

```csharp
internal sealed class FakeClock : IClock
{
    public DateTimeOffset UtcNow { get; set; } =
        new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    public void Advance(TimeSpan by) => UtcNow += by;
}
```

Add a `SetTo` method, following the same local-noon normalization `DayChangeWatcherTests` uses to stay timezone-robust:

```csharp
    public void SetTo(DateOnly date) =>
        UtcNow = new DateTimeOffset(date.ToDateTime(new TimeOnly(12, 0)), DateTimeOffset.Now.Offset).ToUniversalTime();
```

Full resulting file:

```csharp
using Caffeine.Core.Common;

namespace Caffeine.Core.Tests;

/// <summary>Hand-advanced IClock for tests.</summary>
internal sealed class FakeClock : IClock
{
    public DateTimeOffset UtcNow { get; set; } =
        new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    public void Advance(TimeSpan by) => UtcNow += by;

    public void SetTo(DateOnly date) =>
        UtcNow = new DateTimeOffset(date.ToDateTime(new TimeOnly(12, 0)), DateTimeOffset.Now.Offset).ToUniversalTime();
}
```

- [ ] **Step 3: Run tests to verify they fail**

Run: `dotnet test tests/Caffeine.Core.Tests --filter "CurrentStreak_NonDaily_SkipsUnscheduledDays_WithoutBreakingStreak|CurrentStreak_NonDaily_MissedScheduledDay_ZeroesStreak|BestStreak_NonDaily_CountsOnlyScheduledOccurrences|LastSevenDays_NonDaily_ReturnsLastSevenScheduledDates"`
Expected: FAIL — current implementation treats every calendar day as scheduled, so non-daily habits compute wrong streak/history values.

- [ ] **Step 4: Rewrite `CurrentStreak`, `BestStreak`, `LastSevenDays`**

Modify `src/Caffeine.Core/Habits/HabitService.cs`, replacing the three methods (previously at lines 92-132):

```csharp
    /// <summary>Consecutive scheduled occurrences ending today or yesterday; 0 when the most recent scheduled occurrence was missed.</summary>
    public int CurrentStreak(Habit habit)
    {
        DateOnly day = IsScheduled(habit, Today) && habit.CompletedOn.Contains(Today)
            ? Today
            : Today.AddDays(-1);

        int streak = 0;
        int safety = 0;
        while (safety < 3650) // safety bound: if nothing is scheduled (Repeat == None), never loop forever
        {
            if (IsScheduled(habit, day))
            {
                if (!habit.CompletedOn.Contains(day))
                {
                    break;
                }

                streak++;
            }

            day = day.AddDays(-1);
            safety++;
        }

        return streak;
    }

    /// <summary>Longest consecutive run of scheduled occurrences anywhere in the completion log.</summary>
    public int BestStreak(Habit habit)
    {
        int best = 0;
        int run = 0;
        DateOnly? previousScheduled = null;
        foreach (DateOnly date in habit.CompletedOn.Order())
        {
            if (!IsScheduled(habit, date))
            {
                continue;
            }

            bool isNextScheduledAfterPrevious = previousScheduled is { } prev && IsImmediatelyNextScheduled(habit, prev, date);
            run = run > 0 && isNextScheduledAfterPrevious ? run + 1 : 1;
            best = Math.Max(best, run);
            previousScheduled = date;
        }

        return best;
    }

    /// <summary>True when <paramref name="candidate"/> is the next scheduled date strictly after <paramref name="previous"/>, with no scheduled date in between.</summary>
    private bool IsImmediatelyNextScheduled(Habit habit, DateOnly previous, DateOnly candidate)
    {
        for (DateOnly day = previous.AddDays(1); day < candidate; day = day.AddDays(1))
        {
            if (IsScheduled(habit, day))
            {
                return false; // a scheduled date was missed between previous and candidate
            }
        }

        return true;
    }

    /// <summary>Completion flags for the last 7 scheduled dates on/before today, oldest first; index 6 is the most recent scheduled date.</summary>
    public IReadOnlyList<bool> LastSevenDays(Habit habit)
    {
        var results = new List<bool>(7);
        DateOnly day = Today;
        int safety = 0;
        while (results.Count < 7 && safety < 3650)
        {
            if (IsScheduled(habit, day))
            {
                results.Add(habit.CompletedOn.Contains(day));
            }

            day = day.AddDays(-1);
            safety++;
        }

        results.Reverse();

        // Pad with false at the front if fewer than 7 scheduled dates exist in the lookback window
        // (e.g. a brand-new once-a-week habit) so callers can always index 0..6 safely.
        while (results.Count < 7)
        {
            results.Insert(0, false);
        }

        return results;
    }
```

- [ ] **Step 5: Run the new tests to verify they pass**

Run: `dotnet test tests/Caffeine.Core.Tests --filter "CurrentStreak_NonDaily_SkipsUnscheduledDays_WithoutBreakingStreak|CurrentStreak_NonDaily_MissedScheduledDay_ZeroesStreak|BestStreak_NonDaily_CountsOnlyScheduledOccurrences|LastSevenDays_NonDaily_ReturnsLastSevenScheduledDates|CurrentStreak_Daily_UnchangedFromBeforeThisFeature"`
Expected: PASS

- [ ] **Step 6: Run the full test suite, including all pre-existing streak tests**

Run: `dotnet test tests/Caffeine.Core.Tests`
Expected: PASS — every pre-existing `CurrentStreak_*`, `BestStreak_*`, and `LastSevenDays_*` test (which all use daily habits, `Repeat == Weekdays.All`) must still pass unmodified, proving the rewrite is behavior-preserving for daily habits.

- [ ] **Step 7: Commit**

```bash
git add src/Caffeine.Core/Habits/HabitService.cs tests/Caffeine.Core.Tests/HabitServiceTests.cs tests/Caffeine.Core.Tests/FakeClock.cs
git commit -m "feat(habits): make streak and history calculations schedule-aware"
```

---

### Task 5: Pre-existing-record migration test (deserialize JSON without `Repeat`)

**Files:**
- Test: `tests/Caffeine.Core.Tests/HabitServiceTests.cs`

**Interfaces:**
- Consumes: `Habit.Repeat` default (Task 1), `JsonStore` (Task 2).
- Produces: nothing new — this is a pure regression test proving old `habits.json` files load safely.

- [ ] **Step 1: Write the test**

Add to `HabitServiceTests.cs`:

```csharp
    [Fact]
    public void Load_PreExistingRecordWithoutRepeat_DefaultsToEveryDay()
    {
        Directory.CreateDirectory(_dir);
        string legacyJson = """
        {
          "Items": [
            {
              "Id": "11111111-1111-1111-1111-111111111111",
              "Name": "old habit",
              "Icon": "⭐",
              "CreatedOn": "2026-01-01",
              "CompletedOn": ["2026-01-01"]
            }
          ]
        }
        """;
        File.WriteAllText(Path.Combine(_dir, "habits.json"), legacyJson);

        var service = CreateService();

        Habit habit = Assert.Single(service.Habits);
        Assert.Equal("old habit", habit.Name);
        Assert.Equal(Weekdays.All, habit.Repeat);
    }
```

- [ ] **Step 2: Run test to verify it fails or passes**

Run: `dotnet test tests/Caffeine.Core.Tests --filter Load_PreExistingRecordWithoutRepeat_DefaultsToEveryDay`
Expected: PASS already, since `System.Text.Json` leaves properties missing from the payload at the C# member default (`Weekdays.All`), and no production code change is needed. This step exists to lock in that guarantee as a regression test, not to drive new implementation.

If it unexpectedly FAILS, the likely cause is `JsonSerializerOptions` needing `PropertyNameCaseInsensitive = true` or the `Weekdays` initializer not being applied on deserialization (constructor vs. object-initializer semantics) — investigate `Habit`'s property declaration from Task 1 before changing anything else.

- [ ] **Step 3: Commit**

```bash
git add tests/Caffeine.Core.Tests/HabitServiceTests.cs
git commit -m "test(habits): lock in default-to-every-day for pre-existing records"
```

---

### Task 6: `HabitsPage` — filter today's list to scheduled habits

**Files:**
- Modify: `src/Caffeine.App/Pages/HabitsPage.xaml.cs:62-71`

**Interfaces:**
- Consumes: `HabitService.IsScheduled` (Task 3).
- Produces: `RebuildList` now only renders rows for habits scheduled today; no signature change.

This task has no automated test (UI code-behind in this codebase is verified via the `verify` skill, not unit tests — see spec Testing section). Verification happens in Task 9.

- [ ] **Step 1: Modify `RebuildList`**

In `src/Caffeine.App/Pages/HabitsPage.xaml.cs`, replace:

```csharp
    private void RebuildList()
    {
        HabitRows.Children.Clear();
        foreach (Habit habit in _habits.Habits)
        {
            HabitRows.Children.Add(BuildRow(habit));
        }

        EmptyText.Visibility = _habits.Habits.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }
```

with:

```csharp
    private void RebuildList()
    {
        HabitRows.Children.Clear();
        List<Habit> today = _habits.Habits.Where(h => _habits.IsScheduled(h, _habits.Today)).ToList();
        foreach (Habit habit in today)
        {
            HabitRows.Children.Add(BuildRow(habit));
        }

        EmptyText.Visibility = today.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }
```

This requires `using System.Linq;` — check the top of the file; add it if absent.

Note: `EmptyText`'s current copy ("No habits yet — add your first above.") would now also show when habits exist but none are scheduled today. That's a pre-existing string this task doesn't need to change per spec (out of scope for wording), but flag it for Task 9's manual verification pass — if it reads oddly with a populated-but-none-today habit list, that's worth a one-line copy tweak at that point, not now.

- [ ] **Step 2: Build the app to verify it compiles**

Run: `dotnet build src/Caffeine.App`
Expected: Build succeeds.

- [ ] **Step 3: Commit**

```bash
git add src/Caffeine.App/Pages/HabitsPage.xaml.cs
git commit -m "feat(habits): hide habits not scheduled for today in HabitsPage"
```

---

### Task 7: `HomePage` — filter habit card to scheduled habits

**Files:**
- Modify: `src/Caffeine.App/Pages/HomePage.xaml.cs:124-159`

**Interfaces:**
- Consumes: `HabitService.IsScheduled` (Task 3).
- Produces: `BuildHabitsCard` and `RefreshHabitsCaption` only count/render today-scheduled habits; no signature change.

- [ ] **Step 1: Modify `BuildHabitsCard`**

In `src/Caffeine.App/Pages/HomePage.xaml.cs`, replace the `foreach` target:

```csharp
    private void BuildHabitsCard()
    {
        HomeHabitRows.Children.Clear();
        foreach (Habit habit in _habits.Habits.Where(h => _habits.IsScheduled(h, _habits.Today)))
        {
            var check = new CheckBox
            {
                IsChecked = _habits.IsDone(habit, _habits.Today),
                Content = $"{habit.Icon} {habit.Name}",
                MinWidth = 0,
            };
            AutomationProperties.SetName(check, habit.Name);
            check.Tapped += (_, e) => e.Handled = true;
            check.Checked += (_, _) =>
            {
                _habits.SetDone(habit.Id, _habits.Today, true);
                RefreshHabitsCaption();
            };
            check.Unchecked += (_, _) =>
            {
                _habits.SetDone(habit.Id, _habits.Today, false);
                RefreshHabitsCaption();
            };
            HomeHabitRows.Children.Add(check);
        }

        RefreshHabitsCaption();
    }
```

- [ ] **Step 2: Modify `RefreshHabitsCaption`**

Replace:

```csharp
    private void RefreshHabitsCaption()
    {
        int total = _habits.Habits.Count;
        HabitsCaption.Text = total == 0
            ? "No habits yet"
            : $"{_habits.Habits.Count(h => _habits.IsDone(h, _habits.Today))} of {total} done today";
    }
```

with:

```csharp
    private void RefreshHabitsCaption()
    {
        List<Habit> today = _habits.Habits.Where(h => _habits.IsScheduled(h, _habits.Today)).ToList();
        HabitsCaption.Text = today.Count == 0
            ? "No habits today"
            : $"{today.Count(h => _habits.IsDone(h, _habits.Today))} of {today.Count} done today";
    }
```

The caption text changes from "No habits yet" to "No habits today" for the zero-scheduled-today case, since habits may exist but simply not be due today — "No habits yet" would now be misleading (spec intent: today's list reflects only what's scheduled today).

`using System.Linq;` is already present in this file (used by `_todos.Active.Count(...)` at line 104); no new using needed.

- [ ] **Step 3: Build the app to verify it compiles**

Run: `dotnet build src/Caffeine.App`
Expected: Build succeeds.

- [ ] **Step 4: Commit**

```bash
git add src/Caffeine.App/Pages/HomePage.xaml.cs
git commit -m "feat(habits): filter Home's habit card to today's scheduled habits"
```

---

### Task 8: Day-toggle picker control (shared code-behind helper)

**Files:**
- Create: `src/Caffeine.App/Pages/WeekdayPicker.cs`

**Interfaces:**
- Produces: `static class WeekdayPicker` with `static StackPanel Build(Weekdays initial, out Func<Weekdays> getValue)` — builds 7 horizontally-laid-out `ToggleButton`s labeled S/M/T/W/T/F/S (with full day names in `AutomationProperties.Name` for accessibility), pre-toggled per `initial`, and returns a delegate the caller invokes to read the current combined `Weekdays` value at save time.

This is pure UI-construction code, matching the existing style of `BuildRenameFlyout`/`BuildDotStrip` in `HabitsPage.xaml.cs` (private methods building controls in C#). It's extracted to its own file because it's shared by both the add dialog (Task 9) and the edit flyout (Task 10) — two independent call sites is the DRY threshold per the plan's file-structure guidance.

- [ ] **Step 1: Create the file**

Create `src/Caffeine.App/Pages/WeekdayPicker.cs`:

```csharp
using Caffeine.Core.Habits;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace Caffeine.Pages;

/// <summary>Builds a row of 7 day-of-week toggle buttons shared by the add dialog and edit flyout.</summary>
internal static class WeekdayPicker
{
    private static readonly (Weekdays Flag, string Short, string Full)[] Days =
    {
        (Weekdays.Sunday, "S", "Sunday"),
        (Weekdays.Monday, "M", "Monday"),
        (Weekdays.Tuesday, "T", "Tuesday"),
        (Weekdays.Wednesday, "W", "Wednesday"),
        (Weekdays.Thursday, "T", "Thursday"),
        (Weekdays.Friday, "F", "Friday"),
        (Weekdays.Saturday, "S", "Saturday"),
    };

    /// <summary>
    /// Builds the toggle row pre-selected per <paramref name="initial"/>. <paramref name="getValue"/>
    /// reads the combined selection at any point (e.g. when the caller's save button is clicked).
    /// <paramref name="onChanged"/> fires whenever any toggle flips, so callers can enable/disable
    /// their save button based on whether at least one day is selected.
    /// </summary>
    public static StackPanel Build(Weekdays initial, out Func<Weekdays> getValue, Action? onChanged = null)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
        var toggles = new List<(Weekdays Flag, ToggleButton Button)>();

        foreach (var (flag, shortLabel, fullLabel) in Days)
        {
            var toggle = new ToggleButton
            {
                Content = shortLabel,
                IsChecked = initial.HasFlag(flag),
                MinWidth = 36,
                Padding = new Thickness(0, 6, 0, 6),
            };
            AutomationProperties.SetName(toggle, fullLabel);
            toggle.Checked += (_, _) => onChanged?.Invoke();
            toggle.Unchecked += (_, _) => onChanged?.Invoke();
            toggles.Add((flag, toggle));
            panel.Children.Add(toggle);
        }

        getValue = () =>
        {
            Weekdays result = Weekdays.None;
            foreach (var (flag, button) in toggles)
            {
                if (button.IsChecked == true)
                {
                    result |= flag;
                }
            }

            return result;
        };

        return panel;
    }
}
```

- [ ] **Step 2: Build to verify it compiles**

Run: `dotnet build src/Caffeine.App`
Expected: Build succeeds (nothing calls `WeekdayPicker` yet, but it must compile standalone).

- [ ] **Step 3: Commit**

```bash
git add src/Caffeine.App/Pages/WeekdayPicker.cs
git commit -m "feat(habits): add shared WeekdayPicker toggle-row control"
```

---

### Task 9: Add-habit dialog with day picker

**Files:**
- Modify: `src/Caffeine.App/Pages/HabitsPage.xaml:54-58` (Add button)
- Modify: `src/Caffeine.App/Pages/HabitsPage.xaml.cs:38-60` (`NewNameBox_KeyDown`, `Add_Click`, `AddHabit`)

**Interfaces:**
- Consumes: `WeekdayPicker.Build` (Task 8), `HabitService.Add` + `SetRepeat` (existing + Task 3).
- Produces: `Add_Click`/Enter-key now opens a `ContentDialog` instead of adding immediately from the inline row.

- [ ] **Step 1: Update `HabitsPage.xaml` — Add button opens the dialog instead of direct add**

The inline row keeps its `NewNameBox` and `NewIconBox` (they're still the fields the dialog reads from — no XAML layout change needed, only the button's role changes conceptually). No XAML edit is strictly required since `Click="Add_Click"` still applies; only the code-behind changes. Skip this step — XAML is unchanged.

- [ ] **Step 2: Replace `AddHabit` with a dialog-opening flow**

In `src/Caffeine.App/Pages/HabitsPage.xaml.cs`, replace:

```csharp
    private void NewNameBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter)
        {
            AddHabit();
            e.Handled = true;
        }
    }

    private void Add_Click(object sender, RoutedEventArgs e) => AddHabit();

    private void AddHabit()
    {
        if (_habits.Add(NewNameBox.Text, NewIconBox.Text) is null)
        {
            return; // whitespace-only name — nothing to add
        }

        NewNameBox.Text = string.Empty;
        NewIconBox.Text = string.Empty;
        RebuildList();
        NewNameBox.Focus(FocusState.Programmatic);
    }
```

with:

```csharp
    private void NewNameBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter)
        {
            _ = ShowAddDialogAsync();
            e.Handled = true;
        }
    }

    private void Add_Click(object sender, RoutedEventArgs e) => _ = ShowAddDialogAsync();

    /// <summary>Opens the add dialog pre-filled from the inline name/icon boxes, with a day picker defaulting to every day.</summary>
    private async Task ShowAddDialogAsync()
    {
        string initialName = NewNameBox.Text;
        string initialIcon = NewIconBox.Text;

        var nameBox = new TextBox { Text = initialName, PlaceholderText = "Habit name" };
        AutomationProperties.SetName(nameBox, "Habit name");
        var iconBox = new TextBox { Text = initialIcon, Width = 64, MaxLength = 8, PlaceholderText = "⭐" };
        AutomationProperties.SetName(iconBox, "Icon");

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "Add habit",
            PrimaryButtonText = "Add",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
        };

        StackPanel picker = WeekdayPicker.Build(
            Weekdays.All,
            out Func<Weekdays> getRepeat,
            onChanged: () => dialog.IsPrimaryButtonEnabled = getRepeat() != Weekdays.None);

        var content = new StackPanel { Spacing = 12 };
        content.Children.Add(nameBox);
        content.Children.Add(iconBox);
        content.Children.Add(picker);
        dialog.Content = content;

        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        Habit? habit = _habits.Add(nameBox.Text, iconBox.Text);
        if (habit is null)
        {
            return; // whitespace-only name — nothing to add
        }

        _habits.SetRepeat(habit.Id, getRepeat());

        NewNameBox.Text = string.Empty;
        NewIconBox.Text = string.Empty;
        RebuildList();
        NewNameBox.Focus(FocusState.Programmatic);
    }
```

Add `using Caffeine.Core.Habits;` — already present at the top of the file (line 2). Add `using System.Threading.Tasks;` if `Task` is not already resolvable — check; `ConfirmDeleteAsync` already returns `Task` in this file (line 207 in the original), so `Task` is already in scope via implicit usings or an existing `using`. No new using needed.

- [ ] **Step 3: Build to verify it compiles**

Run: `dotnet build src/Caffeine.App`
Expected: Build succeeds.

- [ ] **Step 4: Commit**

```bash
git add src/Caffeine.App/Pages/HabitsPage.xaml.cs
git commit -m "feat(habits): add day picker to the add-habit dialog"
```

---

### Task 10: Edit flyout gains the day picker

**Files:**
- Modify: `src/Caffeine.App/Pages/HabitsPage.xaml.cs:171-204` (`BuildRenameFlyout`)

**Interfaces:**
- Consumes: `WeekdayPicker.Build` (Task 8), `HabitService.Rename` + `SetRepeat` (existing + Task 3).
- Produces: `BuildRenameFlyout` now also lets the user change `Repeat`; signature unchanged (`Flyout BuildRenameFlyout(Habit habit)`).

- [ ] **Step 1: Replace `BuildRenameFlyout`**

In `src/Caffeine.App/Pages/HabitsPage.xaml.cs`, replace:

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

with (declare `save` before calling `WeekdayPicker.Build` so `onChanged` can close over it directly, exactly matching Task 9's pattern):

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

        StackPanel picker = WeekdayPicker.Build(
            habit.Repeat,
            out Func<Weekdays> getRepeat,
            onChanged: () => save.IsEnabled = getRepeat() != Weekdays.None);

        var panel = new StackPanel { Spacing = 8 };
        panel.Children.Add(box);
        panel.Children.Add(picker);
        panel.Children.Add(save);

        var flyout = new Flyout { Content = panel };

        void Commit()
        {
            _habits.Rename(habit.Id, box.Text);
            _habits.SetRepeat(habit.Id, getRepeat());
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

Use this final version (`save` declared before `WeekdayPicker.Build` is called, `onChanged` closes over it directly) — it's valid C# since `save` is a fully-constructed local by the time the lambda can execute, and it matches Task 9's structure exactly. Discard the "re-wire after the fact" draft above.

- [ ] **Step 2: Build to verify it compiles**

Run: `dotnet build src/Caffeine.App`
Expected: Build succeeds.

- [ ] **Step 3: Commit**

```bash
git add src/Caffeine.App/Pages/HabitsPage.xaml.cs
git commit -m "feat(habits): add day picker to the edit flyout"
```

---

### Task 11: Manual verification pass

**Files:** none (no code changes — verification only).

- [ ] **Step 1: Run the full automated test suite**

Run: `dotnet test`
Expected: All tests pass (Core tests from Tasks 1-5, no App-level tests exist per this codebase's pattern).

- [ ] **Step 2: Launch and drive the app**

Use the `verify` skill to build, launch, and drive the Caffeine WinUI 3 app. Cover:

1. Navigate to Habits page. Click "Add" with an empty name — dialog should not create a habit (existing whitespace-guard behavior, now inside the dialog).
2. Add a habit named "Remove trash" with icon 🗑️, deselect all days except Tuesday/Thursday/Saturday in the dialog's picker. Verify the "Add" button is disabled when all 7 toggles are off, and re-enables when at least one is on.
3. Confirm the habit appears in the list only if today (per system clock) is Tuesday, Thursday, or Saturday; otherwise confirm it's absent from both `HabitsPage` and the Home page's habit card, but still exists (reopen edit flyout by temporarily checking via a daily test habit, or trust Task 3-5's automated coverage for the hidden state — the important runtime check is that a *visible* habit's checkbox/streak still works).
4. Add a second, daily habit (leave all 7 days selected) — confirm it behaves exactly as before (visible every day, checkbox toggles, streak/dot strip updates).
5. Open the daily habit's edit flyout (pencil icon), verify all 7 day toggles show pre-selected, deselect all but one day, click Save — confirm the habit now only appears on that one day of the week after a rebuild (trigger `OnDayChanged` naturally isn't practical in one sitting; instead confirm the flyout closes, the row updates streak text without error, and rely on Task 3-5's automated tests for the actual per-day filtering logic).
6. Verify the Home page's "N of M done today" caption only counts today-scheduled habits, and reads "No habits today" (not "No habits yet") when habits exist but none are scheduled for today (can be tested by making all habits Tuesday-only and running the app on a non-Tuesday, or by trusting the automated `RefreshHabitsCaption` logic if the current day can't be controlled).
7. Confirm `habits.json` (under `%LocalAppData%\Caffeine\`) shows `Repeat` as readable day names, not integers.

- [ ] **Step 3: Report results**

Summarize what was checked and any deviations found. If a deviation is found, stop and fix it in a follow-up task before considering the plan complete — do not mark this step done on unverified claims.

---

## Self-Review Notes

- **Spec coverage:** Data model (Tasks 1-2), migration/backward-compat (Tasks 1, 5), `IsScheduled`/`SetRepeat` (Task 3), schedule-aware streak/history (Task 4), add dialog (Task 9), edit flyout (Task 10), today-filtering on both `HabitsPage` and `HomePage` (Tasks 6-7), zero-days-disables-save guard (Tasks 9-10), readable JSON (Task 2), out-of-scope items (monthly/interval recurrence, retroactive streak rewriting) — none implemented, correctly excluded. All spec sections have a corresponding task.
- **Placeholder scan:** no TBD/TODO markers; every task's code steps show complete, final code. (An earlier draft of Tasks 4 and 10 showed "first attempt, then simplified" reasoning inline — cleaned up so each step now shows only the single correct implementation.)
- **Type consistency:** `Weekdays` (Task 1) → `Habit.Repeat` (Task 1) → `IsScheduled`/`SetRepeat` (Task 3) → `CurrentStreak`/`BestStreak`/`LastSevenDays` (Task 4) → `WeekdayPicker.Build(Weekdays, out Func<Weekdays>, Action?)` (Task 8) → both call sites (Tasks 9-10) all use the same `Weekdays` type and the same `HabitService` method names/signatures throughout. `FakeClock.SetTo(DateOnly)` (Task 4 Step 2) is fully specified against the real current file contents, not left conditional.
