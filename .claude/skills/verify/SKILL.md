---
name: verify
description: Build, launch, and drive the Caffeine WinUI 3 app for runtime verification
---

# Verifying Caffeine

WinUI 3 desktop tray app, unpackaged, self-contained.

## Build & launch

```powershell
dotnet build src/Caffeine.App/Caffeine.App.csproj -p:Platform=x64 -v:m   # ~25s
Start-Process "src\Caffeine.App\bin\x64\Debug\net10.0-windows10.0.19041.0\Caffeine.exe"
```

Kill any already-running instance first (`Stop-Process -Name Caffeine`) or the
new build won't be the one you're driving. Crashes land in
`bin\...\crash.log` next to the exe (absence = clean run).

## Screenshots — use PrintWindow, not CopyFromScreen

The machine is in active use; the window is usually occluded and
`SetForegroundWindow` loses. `PrintWindow(hwnd, hdc, 2)` (PW_RENDERFULLCONTENT)
captures the window surface regardless. Get the hwnd from
`(Get-Process Caffeine).MainWindowHandle`. CopyFromScreen captures whatever
app is on top — including the user's personal windows. Don't.

## Driving the UI — UIA patterns, no mouse

UI Automation patterns work without focus and don't steal the pointer:

- Window: root child with `Name = "Caffeine"`.
- Nav items ("Home", "Todos", "Timers", "Awake", "Settings", "Welcome to Caffeine"): `SelectionItemPattern.Select()`.
- Toggles by AutomationId (`AwakeToggle`, `RememberStateToggle`,
  `RunInTrayToggle`, `StartWithWindowsToggle`): `TogglePattern.Toggle()`. Find by **AutomationId**, not
  Name — the SettingsCard shares the toggle's Name and matches first.
- UIA only sees the currently loaded page; navigate before searching.
- Close window: `PostMessage(hwnd, 0x0010, 0, 0)` (WM_CLOSE).

## State to check

- Settings persist to `%LocalAppData%\Caffeine\settings.json`
  (RememberState / RunInSystemTray / LastAwakeActive). Delete it for a
  fresh-defaults run; corrupt it to test the fallback path.
- `RunInSystemTray=true` (default): WM_CLOSE hides to tray, process stays.
  `false`: WM_CLOSE exits the process.
- Awake state itself: `powercfg /requests` shows a DISPLAY request from
  Caffeine.exe, but needs an elevated shell — usually skip and trust the toggle.
- Start with Windows: registry value `Caffeine` under
  `HKCU:\Software\Microsoft\Windows\CurrentVersion\Run` (command ends in
  `--startup`); the toggle reads the registry directly, nothing in
  settings.json. Launch with `-ArgumentList "--startup"` → window stays hidden
  (MainWindowHandle 0) when run-in-tray is on. Leave the value **removed**
  after testing unless the user wants it on.
