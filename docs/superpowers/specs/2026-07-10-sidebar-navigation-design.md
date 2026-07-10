# Caffeine — PowerToys-style Sidebar Navigation — Design

**Date:** 2026-07-10
**Status:** Approved (autonomous session — defaults chosen to match the request)

## Purpose

Give Caffeine a left sidebar shell like Microsoft PowerToys, with three
destinations:

- **Home** — the keep-awake toggle (an animated character will be added here
  later; the layout reserves space for it).
- **Settings** — app configuration, PowerToys-style settings cards:
  - **Remember state** ("persistent state"): when on, the keep-awake toggle
    position survives app restarts.
  - **Run in system tray**: when on (default), closing the window hides the app
    to the tray and the tray icon exists; when off, there is no tray icon and
    closing the window exits the app.
- **Welcome** — placed at the bottom of the sidebar (like PowerToys'
  "Welcome to PowerToys"), with an explanation of the app and a mini tutorial.

## Approach

Use the built-in WinUI 3 **`NavigationView`** control — the same control
PowerToys itself uses — in left (expanded) pane mode, hosting a `Frame` that
navigates between three `Page`s. Alternatives considered:

- *Hand-rolled sidebar (ListView + ContentControl)*: more work, worse
  accessibility/keyboard support, no benefit.
- *Pivot/TabView*: doesn't match the requested PowerToys look.

`NavigationView` wins on fidelity and cost.

## Shell (MainWindow)

- Keep the existing custom title bar (icon + "Caffeine" in the chrome) and Mica
  backdrop.
- Below the title bar: `NavigationView` with
  - `PaneDisplayMode="Left"`, `IsBackButtonVisible=Collapsed`,
    `IsSettingsVisible=false` (we supply our own Settings item so it sits in the
    main menu like PowerToys' "General", not pinned by the control).
  - Menu items: **Home** (`&#xE80F;`), **Settings** (`&#xE713;`).
  - Footer item: **Welcome** (`&#xE82F;` info/book glyph).
- Navigation: `SelectionChanged` → `Frame.Navigate(pageType)`. Home is selected
  at startup. Page instances are navigated by type; state lives outside the
  pages so recreation is harmless.
- Window default size grows to 960×640 to fit the pane.

## Pages

`Pages/HomePage`, `Pages/SettingsPage`, `Pages/WelcomePage` — each a
`Page` that reaches the shared services via `((App)Application.Current)`.

- **HomePage**: title + description, the existing "Keep screen awake"
  `SettingsCard` with the `ToggleSwitch`, subscribed to `AwakeState.Changed`
  (unsubscribed on unload). A placeholder region below the card is reserved for
  the future animated character (empty for now, no dead UI shipped).
- **SettingsPage**: two `SettingsCard`s with toggles bound to `AppSettings`:
  "Remember state" and "Run in system tray". Changes apply immediately and are
  saved to disk on every change.
- **WelcomePage**: what Caffeine does, the "How it works" explanation (moved
  from the old single window), and a 3-step mini tutorial (toggle on Home, use
  the tray icon, adjust Settings). Uses plain text blocks + SettingsCards for
  visual consistency.

## Settings persistence

The app is unpackaged, so no `ApplicationData` store — persist to
`%LocalAppData%\Caffeine\settings.json`.

- `AppSettings` (record of plain properties): `RememberState` (default false),
  `RunInSystemTray` (default true), `LastAwakeActive` (default true).
- `SettingsService`: static `Load()` (missing/corrupt file → defaults) and
  `Save(AppSettings)` (create directory, write JSON). Failures are swallowed —
  settings are a convenience, never a crash.

## Behavior wiring (App)

- **Startup**: load settings. Initial awake state = `LastAwakeActive` if
  `RememberState`, else `true` (current behavior).
- **State change**: update `LastAwakeActive` and save when `RememberState` is
  on.
- **Run in system tray on** (default): tray icon created, window close hides to
  tray (current behavior).
- **Run in system tray off**: tray icon disposed/not created; window close
  exits the app (disabling keep-awake first). Toggling the setting at runtime
  creates/disposes the tray icon immediately.

## Error handling

- Settings I/O failures fall back to defaults silently (log nothing — no
  logging infra beyond crash.log, and this isn't a crash).
- Everything else unchanged from the existing design.

## Testing / verification

- `dotnet build`, launch, click through all three pages, flip both settings,
  confirm `settings.json` appears and survives restart, confirm close-to-tray
  vs close-to-exit follows the setting.

## Out of scope (YAGNI)

- The animated character itself (space reserved only).
- Start-with-Windows, localization, timed activation.
