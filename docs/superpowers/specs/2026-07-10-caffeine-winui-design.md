# Caffeine for Windows — Design

**Date:** 2026-07-10
**Status:** Approved (autonomous session — defaults chosen to match the request)

## Purpose

A lightweight Windows tray app that prevents the screen from sleeping/locking,
toggleable from the system tray, with negligible power usage.

## Requirements

- Keep the display awake (no screen sleep, no idle lock, no screensaver) while enabled.
- Toggle on/off from the system tray ("menu bar" equivalent on Windows).
- Minimal power/CPU usage — the app must not itself keep the CPU busy.
- Built with WinUI 3 (user requirement).

## Keep-awake mechanism

Use `SetThreadExecutionState(ES_CONTINUOUS | ES_DISPLAY_REQUIRED | ES_SYSTEM_REQUIRED)`
instead of the classic "simulate a keypress every 59 seconds" trick:

- One P/Invoke call on enable, one on disable — no timers, no polling, zero idle CPU.
- It is the official Windows API for this (used by video players); it informs the
  power manager directly instead of faking user input.
- No risk of a synthetic keypress interfering with the foreground app.

Component: `KeepAwakeService` — static class wrapping the P/Invoke with
`Enable()` / `Disable()` and an `IsEnabled` property. On process exit,
Windows automatically clears the request, so a crash can never leave the
machine stuck awake.

## App shell

- WinUI 3 / Windows App SDK, **unpackaged** (`WindowsPackageType=None`) and
  **self-contained** (`WindowsAppSDKSelfContained=true`) so the build output is a
  plain runnable .exe with no MSIX or runtime installer needed.
- Tray icon via `H.NotifyIcon.WinUI`:
  - Left-click / double-click: toggle keep-awake.
  - Right-click menu: Active (checkable toggle), Show window, Exit.
  - Icon swaps between "full cup" (active) and "empty cup" (inactive) so state is
    visible at a glance; tooltip shows current state.
- `MainWindow`: small fixed-size window with a `ToggleSwitch` and status text.
  Closing the window hides it to the tray; the app keeps running. Exit only via
  the tray menu.
- Starts **enabled** on launch (matches the original Caffeine behavior and the
  user's stated goal).

## State flow

Single source of truth: an `AwakeState` object owned by `App`, raising a changed
event. Tray icon, tray menu check state, and window toggle all subscribe and
update from it; any of them can flip it. Flipping calls
`KeepAwakeService.Enable()/Disable()`.

## Error handling

- `SetThreadExecutionState` returns 0 on failure — surface as status text
  "failed to enable" (practically never happens).
- No settings persistence, no network, no elevation needed.

## Testing / verification

- UI + P/Invoke tray app: verify by building (`dotnet build`) and launching;
  confirm the process runs and `powercfg /requests` lists a DISPLAY request from
  the exe while enabled (requires elevated shell; otherwise verify via toggle
  behavior and no errors).

## Out of scope (YAGNI)

- Timed activation ("stay awake for 2h"), start-with-Windows, settings
  persistence, packaging/installer. All easy follow-ups.
