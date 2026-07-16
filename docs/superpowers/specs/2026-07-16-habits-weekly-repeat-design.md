# Habits Weekly Repeat — Design

## Problem

Habits today are implicitly daily: `Habit` has no scheduling field, and
`HabitsPage` shows every habit every day. Some habits don't belong on every
day (e.g. "Remove trash 🗑️" only happens Tuesday, Thursday, Saturday). The
user wants to pick which days of the week each habit applies to.

## Scope

Specific days of the week only (no monthly dates, no "every N days"
interval — out of scope for this pass). Schedule is set at creation and
editable later from the same rename flyout. No migration step is needed
beyond a safe default for pre-existing records.

## Data model

Add a `[Flags] Weekdays` enum to `Caffeine.Core.Habits`:

```csharp
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

`Habit` gets one new property:

```csharp
public Weekdays Repeat { get; set; } = Weekdays.All;
```

**Backward compatibility:** existing `habits.json` records predate this
field. `System.Text.Json` leaves properties absent from the JSON payload at
their C# member default, and the member default here is the `Weekdays.All`
initializer — so every pre-existing habit deserializes as "every day,"
preserving current behavior with no explicit migration code.

**Readability of the stored value:** `JsonStore<T>`'s `JsonSerializerOptions`
(`src/Caffeine.Core/Common/JsonStore.cs:15`) has no enum converter today, so
a `[Flags]` enum would serialize as a raw integer (e.g. `84`). Add
`Converters = { new JsonStringEnumConverter() }` to those options so
`habits.json` stays human-readable (e.g. `"Tuesday, Thursday, Saturday"`).
This is a one-line, additive change to shared options and affects no other
document type currently stored (none of them hold enums yet).

## Service logic (`HabitService`)

New method:

```csharp
public bool IsScheduled(Habit habit, DateOnly date) =>
    habit.Repeat.HasFlag(ToWeekdayFlag(date.DayOfWeek));
```

(`ToWeekdayFlag` is a private `DayOfWeek → Weekdays` mapping helper.)

New method to update the schedule, mirroring the shape of `Rename`:

```csharp
public void SetRepeat(Guid id, Weekdays repeat)
```

Ignores unknown ids; no-ops (does not save) if the value is unchanged;
otherwise updates and saves — same pattern as `Rename`.

**Streak calculation becomes schedule-aware.** Today, `CurrentStreak`,
`BestStreak`, and `LastSevenDays` all treat "consecutive" as "consecutive
calendar days." They change to "consecutive **scheduled occurrences**":

- `CurrentStreak`: walk backward from today (or yesterday, per the existing
  "grace day" rule) one calendar day at a time as before, but skip
  (continue, don't break or count) any date where `!IsScheduled(habit,
  date)`. The streak only increments or terminates on scheduled dates.
- `BestStreak`: iterate the sorted completion log as today, but two
  completions are "consecutive" if there is no *scheduled* date strictly
  between them that's missing from the log — not simply "exactly one day
  apart."
- `LastSevenDays`: renamed in behavior (signature can stay, or be renamed
  to `LastSevenScheduledDays` for clarity — implementer's call) to walk
  backward from today collecting the **last 7 dates on which the habit was
  scheduled**, flagging each as done/not-done, instead of the last 7
  calendar days. For an every-day habit this is identical to today's
  output, so no visual change for existing habits.

A habit with `Repeat == Weekdays.None` is possible via the API (all toggles
off) but the UI prevents saving that state (see below) — `IsScheduled`
always returns `false` for it, which degrades gracefully (habit never
appears, streak is always 0) rather than crashing.

## UI

**Add flow.** The inline add row's "Add" button opens a `ContentDialog`
containing: name box, icon box, and seven day-toggle buttons (S M T W T F
S) all pre-selected (= daily, matching today's implicit default). The
dialog's primary button is disabled while zero days are selected, so a
schedule-less habit can't be created. Confirming calls `HabitService.Add`
followed by `SetRepeat` (or `Add` gains an optional `Weekdays repeat =
Weekdays.All` parameter — implementer's call, either keeps `Add`'s existing
callers working unchanged).

**Edit flow.** The existing rename flyout (pencil icon) gains the same
seven day-toggle buttons beneath the name box, pre-populated from
`habit.Repeat`. "Save" calls both `Rename` and `SetRepeat`. Same
zero-days-disables-save guard as the add dialog.

**Today's list.** `HabitsPage.RebuildList` filters to
`_habits.Habits.Where(h => _habits.IsScheduled(h, _habits.Today))` before
building rows — a habit not scheduled for today simply doesn't appear.
Same filter applies to the Home dashboard's habit card
(`HomePage.BuildHabitsCard`), so the "N of M done today" caption and its
checkbox rows only ever count today's scheduled habits.

**Row display.** No layout change to `BuildRow` — the streak caption text
("Streak N · Best M") and the 7-dot strip keep their current visual form;
only the underlying calculation changes (scheduled-occurrence-based instead
of calendar-day-based), which is invisible for daily habits and produces
correct, denser dots for partial-week habits.

## Testing

Extend `HabitServiceTests.cs`:

- `IsScheduled` returns true/false correctly per day of week.
- A habit created via `Add` (no explicit repeat) defaults to `Weekdays.All`.
- `SetRepeat` updates the schedule and persists across reload; no-ops on
  unknown id or unchanged value (matching `Rename`'s test shape).
- Deserializing a JSON fixture that lacks the `Repeat` property (simulating
  a pre-existing `habits.json`) yields `Weekdays.All` on load.
- `CurrentStreak`/`BestStreak`/`LastSevenDays` (or its renamed equivalent)
  against a non-daily `Repeat` (e.g. Tue/Thu/Sat), using `FakeClock` to
  cross scheduled and unscheduled days, verifying unscheduled days neither
  break nor pad the streak.
- JSON round-trip: `Repeat` set to a partial-week value persists and
  reloads correctly, serialized as readable names (not a raw int).

No new UI test file — this codebase verifies pages at runtime via the
`verify` skill (UIA automation), not with an automated UI test suite; the
implementation plan's UI tasks will include a manual/UIA verification pass
covering the add dialog, edit flyout, and today-filtered list.

## Out of scope

- Monthly/specific-date recurrence.
- "Every N days" rolling interval.
- Retroactively re-deriving streaks when a habit's schedule changes (e.g.
  changing Repeat doesn't rewrite history — `CompletedOn` entries on
  now-unscheduled dates are simply ignored by the new streak/scheduled
  logic going forward, not deleted).
- Notifications/reminders tied to scheduled days.
