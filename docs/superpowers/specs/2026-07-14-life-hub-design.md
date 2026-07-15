# Caffeine Life Hub — Design

**Date:** 2026-07-14
**Status:** Approved

## Goal

Transform Caffeine from a single-purpose keep-awake utility into a personal
daily-life hub with four modules: **Awake** (the existing feature), **Habits**,
**Todos**, and **Timers** (Pomodoro / Countdown / Stopwatch).

## Decisions

- Keep-awake stays as a full module with its own page; tray behavior unchanged.
- Staged delivery: restructure the shell first, then one module per stage.
- Storage: one JSON file per module in `%LocalAppData%\Caffeine\` (existing
  pattern), via a shared `JsonStore<T>` helper with atomic writes.
- Home becomes a daily dashboard with one card per module.
- Feature depth: lean MVP (defined per module below).
- Architecture: multi-project split — `Caffeine.Core` (logic, no UI) +
  `Caffeine.App` (WinUI 3) + `Caffeine.Core.Tests` (xUnit).

## Solution structure

```
caffeine/
├─ Caffeine.slnx
├─ src/
│  ├─ Caffeine.Core/          # class library, net10.0-windows, no WinUI deps
│  │  ├─ Common/              # JsonStore<T>, IClock
│  │  ├─ Awake/               # KeepAwakeService, AwakeState, UsageTracker, UsageStats
│  │  ├─ Habits/              # Habit, HabitService, streak calculation
│  │  ├─ Todos/               # TodoItem, TodoService
│  │  ├─ Timers/              # PomodoroEngine, CountdownEngine (IClock-driven)
│  │  └─ Settings/            # AppSettings, SettingsService, StartupService
│  └─ Caffeine.App/           # WinUI 3: App.xaml, MainWindow, Pages/, tray icon,
│                             #   RelayCommand and other view-layer helpers
└─ tests/
   └─ Caffeine.Core.Tests/    # xUnit
```

- `Caffeine.Core` targets `net10.0-windows` (needs the
  `SetThreadExecutionState` P/Invoke and registry access) but references no
  WinUI/WindowsAppSDK packages. Everything in it is unit-testable headlessly.
- `Caffeine.App` holds XAML, code-behind, tray icon, and navigation only.
- Timer engines live in Core behind an `IClock` abstraction so tests can
  fast-forward time; the UI uses a `DispatcherTimer` purely to refresh the
  display.

## Shell, navigation, and Home

`NavigationView` sidebar:

- **Home** — daily dashboard, one card per module:
  - Habits: today's habits with inline check-off
  - Todos: today's + overdue items
  - Timers: quick-start Pomodoro button
  - Awake: status + toggle
  - Each card navigates to its module page.
- **Habits**, **Todos**, **Timers** — module pages.
- **Awake** — receives the current Home dashboard content unchanged (usage
  stats, killstreak, timed mode, screen-on toggle).
- Footer: **Settings**, **Welcome** (as today).

Modules not yet built do not appear in the sidebar — no placeholder pages.

## Module designs (lean MVP)

### Timers

Three tabs (`SelectorBar`/`Pivot`): Pomodoro, Countdown, Stopwatch.

- **Pomodoro**: configurable work / short-break / long-break lengths and
  cycles-per-long-break (defaults 25/5/15, 4 cycles). State machine in Core:
  `Idle → Work → ShortBreak | LongBreak → …` with start/pause/skip/reset.
  Toast notification on every phase transition. Config persists.
- **Countdown**: hours/minutes/seconds input plus quick presets
  (5/10/25/60 min). Toast + sound at zero.
- **Stopwatch**: start/pause/reset, lap list.
- One running timer at a time in v1. Switching tabs does not stop a running
  timer; the running tab shows a badge.

### Todos

Single list. Item: title, optional due date, done flag, created/completed
timestamps. Add via textbox at top, complete via checkbox, delete with button.
Two views: **Active** (sorted overdue → due date → newest) and **Completed**.
No priorities, projects, or subtasks in v1.

### Habits

Daily habits. Habit: name, emoji icon, created date. Completion log is a set
of dates. Each row: today's check-off, current streak, best streak, and a
last-7-days dot strip. Add/rename/delete (delete confirms — it drops history).

**Streak rule:** consecutive days ending today or yesterday — missing today
does not zero the streak until the day is over.

## Persistence & error handling

- Generic `JsonStore<T>` in `Core/Common`: typed load/save to
  `%LocalAppData%\Caffeine\` with atomic writes (temp file + replace).
- Files: existing `settings.json` and usage stats unchanged; new
  `habits.json`, `todos.json`, `timers.json` (Pomodoro config/presets).
- Saves happen immediately on every change.
- Corrupt file on load → rename to `*.corrupt.bak`, start that module empty,
  never crash.

## Staged delivery

Each stage builds, runs, and is usable when it ships:

1. **Restructure** — split into Core/App/Tests, move existing code, new
   sidebar with Awake as its own page, Home becomes a dashboard shell with
   just the Awake card. Pure reorganization; behavior identical.
2. **Timers** — engines in Core + tests, Timers page, toasts, Home card.
3. **Todos** — TodoService + tests, Todos page, Home card.
4. **Habits** — HabitService + streak logic + tests, Habits page, Home card.
5. **Polish** — Welcome page describes the hub, README rewrite, empty states.

## Testing

xUnit against Core:

- Streak edge cases: day boundaries, gaps, done-yesterday-not-today.
- Pomodoro state machine: transitions, pause/resume, cycle counting.
- Todo sorting and state changes.
- `JsonStore` round-trip and corrupt-file recovery.

UI verified by running the app (`/verify` skill).
