# Caffeine

A tiny Windows tray app that keeps your screen awake — no sleep, no idle lock,
no screensaver — toggleable from the system tray. Built with WinUI 3.

## How it works

Instead of the classic trick of simulating a keypress every 59 seconds, Caffeine
calls the official Windows power API,
[`SetThreadExecutionState`](https://learn.microsoft.com/windows/win32/api/winbase/nf-winbase-setthreadexecutionstate),
with `ES_CONTINUOUS | ES_DISPLAY_REQUIRED | ES_SYSTEM_REQUIRED` — the same
mechanism video players use. That means:

- **Zero power overhead** — one API call on toggle, no timers, no polling,
  no synthetic input. The app sits fully idle.
- **No side effects** — nothing interferes with the foreground app.
- **Fail-safe** — Windows clears the request automatically if the app exits,
  so your machine can never get stuck awake.

## Usage

- The app starts **active** and lives in the system tray (coffee cup icon).
- **Left-click** the tray icon to toggle keep-awake on/off. The icon dims when
  inactive; the tooltip shows the current state.
- **Right-click** for a menu: toggle, open the status window, or exit.
- Closing the window just hides it to the tray — exit via the tray menu.

The window opens on a **Home** dashboard with cards for each module:

- **Awake** — the keep-awake toggle described above.
- **Todos** — a single to-do list with due dates and Active/Completed views.
- **Timers** — Pomodoro (configurable focus/break lengths with toast notifications), countdown with quick presets, and a stopwatch with laps.

## Building

Requires the .NET 10 SDK on Windows 10 1809+.

```powershell
dotnet build src/Caffeine.App/Caffeine.App.csproj -p:Platform=x64 -p:RuntimeIdentifier=win-x64
# run: src\Caffeine.App\bin\x64\Debug\net10.0-windows10.0.19041.0\win-x64\Caffeine.exe
```

For an optimized standalone build (no .NET install needed on the target machine):

```powershell
dotnet publish src/Caffeine.App/Caffeine.App.csproj -c Release -p:Platform=x64 -r win-x64 --self-contained
```

```powershell
dotnet test tests/Caffeine.Core.Tests/Caffeine.Core.Tests.csproj
```

> Note: the exe is locked while the app is running — exit it from the tray
> before rebuilding.

## Project layout

- [src/Caffeine.Core](src/Caffeine.Core) — all app logic, UI-free (keep-awake, settings, usage stats)
- [src/Caffeine.App](src/Caffeine.App) — the WinUI 3 shell: windows, pages, tray icon
- [tests/Caffeine.Core.Tests](tests/Caffeine.Core.Tests) — xUnit tests for Core
- [docs/superpowers/specs](docs/superpowers/specs) — design docs
