# Life Hub Stage 1 — Project Restructure Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Split Caffeine into `Caffeine.Core` (logic, UI-free) + `Caffeine.App` (WinUI 3) + `Caffeine.Core.Tests` (xUnit), move keep-awake onto its own "Awake" page, and turn Home into the hub dashboard shell with an Awake card — behavior otherwise identical.

**Architecture:** The existing single WinUI project moves to `src/Caffeine.App/`. All non-UI logic moves to a new `src/Caffeine.Core/` class library organized by module (`Common/`, `Awake/`, `Settings/`). `AwakeState` and `UsageTracker` currently depend on `Microsoft.UI.Dispatching.DispatcherQueueTimer`, so a tiny `IAppTimer` abstraction is introduced first (implemented in the App by `DispatcherAppTimer`), letting Core stay free of WinUI/WindowsAppSDK references and making the timed-mode logic unit-testable. A shared `JsonStore<T>` (atomic writes, corrupt-file backup) replaces the duplicated load/save code in `SettingsService`/`UsageStatsService` and becomes the persistence layer for the future Habits/Todos/Timers modules.

**Tech Stack:** .NET 10, WinUI 3 (WindowsAppSDK 1.8.250907003), H.NotifyIcon.WinUI, CommunityToolkit SettingsControls, xUnit.

**Spec:** `docs/superpowers/specs/2026-07-14-life-hub-design.md`

## Global Constraints

- Branch: all work happens on `feature/life-hub-stage1-restructure`.
- The executable must stay `Caffeine.exe`; `RootNamespace` of the app stays `Caffeine`; page namespace stays `Caffeine.Pages`.
- `Caffeine.Core` must NOT reference `Microsoft.WindowsAppSDK` or any WinUI package. It targets `net10.0-windows` (needs `Microsoft.Win32.Registry` and the kernel32 P/Invoke, both in-box on the `-windows` TFM).
- App data directory stays `%LocalAppData%\Caffeine\` (`settings.json`, `usage.json`).
- WindowsAppSDK package version stays exactly `1.8.250907003`; H.NotifyIcon.WinUI `2.4.1`; SettingsControls `8.2.251219`.
- The built exe is locked while the app runs — before any rebuild: `Stop-Process -Name Caffeine -ErrorAction SilentlyContinue` (PowerShell) or `powershell -Command "Stop-Process -Name Caffeine -ErrorAction SilentlyContinue"` (bash).
- `docs/superpowers` is in `.gitignore`; commit plan/spec updates with `git add -f`.
- Commit messages follow the repo convention `feat:|refactor:|test:|docs: <imperative summary>` and end with the `Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>` trailer.
- Build command (until Task 2 changes paths): `dotnet build Caffeine.csproj -p:Platform=x64 -v:m`. After Task 2: `dotnet build src/Caffeine.App/Caffeine.App.csproj -p:Platform=x64 -v:m`. Test command after Task 2: `dotnet test tests/Caffeine.Core.Tests/Caffeine.Core.Tests.csproj`.
- There is no test project until Task 2, so Tasks 1's "test" is: solution builds + app launches + toggle works (see the `verify` skill at `.claude/skills/verify/SKILL.md` for UIA-based driving; never use CopyFromScreen).

---

### Task 1: IAppTimer abstraction (in place, before any file moves)

Free `AwakeState` and `UsageTracker` from `Microsoft.UI.Dispatching` so they can move into Core in Task 2. Pure refactor — behavior identical.

**Files:**
- Create: `src/IAppTimer.cs` (namespace is already `Caffeine.Core.Common` — the file moves, unedited, in Task 2)
- Create: `src/DispatcherAppTimer.cs`
- Modify: `src/AwakeState.cs`
- Modify: `src/UsageTracker.cs`
- Modify: `App.xaml.cs:17` (State property) and `App.xaml.cs:55` (Usage creation)

**Interfaces:**
- Consumes: nothing new.
- Produces: `interface Caffeine.Core.Common.IAppTimer { TimeSpan Interval { get; set; } bool IsRepeating { get; set; } event Action? Tick; void Start(); void Stop(); }`; `AwakeState(Func<IAppTimer> timerFactory)`; `UsageTracker(AwakeState state, IAppTimer flushTimer)`; `sealed class Caffeine.DispatcherAppTimer : IAppTimer` (UI thread only). Task 4's tests and Task 2's moves rely on these exact signatures.

- [ ] **Step 1: Create `src/IAppTimer.cs`**

```csharp
namespace Caffeine.Core.Common;

/// <summary>
/// Minimal timer abstraction so Core services can schedule work without
/// referencing a UI framework. The app supplies a DispatcherQueue-backed
/// implementation; tests supply a fake they fire by hand.
/// </summary>
public interface IAppTimer
{
    TimeSpan Interval { get; set; }

    bool IsRepeating { get; set; }

    event Action? Tick;

    void Start();

    void Stop();
}
```

- [ ] **Step 2: Create `src/DispatcherAppTimer.cs`**

```csharp
using Caffeine.Core.Common;
using Microsoft.UI.Dispatching;

namespace Caffeine;

/// <summary>IAppTimer backed by a DispatcherQueueTimer; must be created on the UI thread.</summary>
public sealed class DispatcherAppTimer : IAppTimer
{
    private readonly DispatcherQueueTimer _timer;

    public DispatcherAppTimer()
    {
        _timer = DispatcherQueue.GetForCurrentThread().CreateTimer();
        _timer.Tick += (_, _) => Tick?.Invoke();
    }

    public TimeSpan Interval
    {
        get => _timer.Interval;
        set => _timer.Interval = value;
    }

    public bool IsRepeating
    {
        get => _timer.IsRepeating;
        set => _timer.IsRepeating = value;
    }

    public event Action? Tick;

    public void Start() => _timer.Start();

    public void Stop() => _timer.Stop();
}
```

- [ ] **Step 3: Refactor `src/AwakeState.cs` to use the abstraction**

Replace the whole file with:

```csharp
using Caffeine.Core.Common;

namespace Caffeine;

/// <summary>
/// Single source of truth for whether keep-awake is active. The tray icon,
/// tray menu and window toggle all read from and write to this object.
/// Must only be touched from the UI thread (SetThreadExecutionState is
/// per-thread, so all calls have to come from the same thread anyway).
/// </summary>
public sealed class AwakeState
{
    private readonly Func<IAppTimer> _timerFactory;
    private IAppTimer? _timer;

    public AwakeState(Func<IAppTimer> timerFactory)
    {
        _timerFactory = timerFactory;
    }

    public bool IsActive { get; private set; }

    /// <summary>Hold the display on too; when false only the system stays awake.</summary>
    public bool KeepScreenOn { get; set; } = true;

    /// <summary>Auto-off duration for timed mode; null keeps awake indefinitely.</summary>
    public TimeSpan? Interval { get; set; }

    /// <summary>When the current timed session ends; null when inactive or indefinite.</summary>
    public DateTimeOffset? SessionEndsAt { get; private set; }

    public event Action<bool>? Changed;

    public void Set(bool active)
    {
        if (active == IsActive)
        {
            return;
        }

        if (active)
        {
            if (!KeepAwakeService.Enable(KeepScreenOn))
            {
                return; // request rejected; stay inactive
            }

            StartTimerIfTimed();
        }
        else
        {
            KeepAwakeService.Disable();
            StopTimer();
        }

        IsActive = active;
        Changed?.Invoke(active);
    }

    public void Toggle() => Set(!IsActive);

    /// <summary>
    /// Re-issues the power request and restarts the timer after
    /// <see cref="KeepScreenOn"/> or <see cref="Interval"/> changed while active.
    /// </summary>
    public void Reapply()
    {
        if (!IsActive)
        {
            return;
        }

        KeepAwakeService.Enable(KeepScreenOn);
        StopTimer();
        StartTimerIfTimed();
    }

    private void StartTimerIfTimed()
    {
        if (Interval is not { } interval)
        {
            return;
        }

        SessionEndsAt = DateTimeOffset.Now + interval;

        _timer ??= CreateTimer();
        _timer.Interval = interval;
        _timer.Start();
    }

    private void StopTimer()
    {
        SessionEndsAt = null;
        _timer?.Stop();
    }

    private IAppTimer CreateTimer()
    {
        var timer = _timerFactory();
        timer.IsRepeating = false;
        timer.Tick += () => Set(false);
        return timer;
    }
}
```

- [ ] **Step 4: Refactor `src/UsageTracker.cs` to take the timer**

Replace the `using`, field, and constructor portions. The full file becomes:

```csharp
using Caffeine.Core.Common;

namespace Caffeine;

/// <summary>
/// Accumulates how long keep-awake has been active and maintains the daily
/// killstreak. Listens to <see cref="AwakeState.Changed"/> and flushes elapsed
/// time to disk once a minute while active, so a crash loses at most ~1 minute.
/// The flush timer must be a UI-thread timer (created on the UI thread).
/// </summary>
public sealed class UsageTracker
{
    private readonly UsageStats _stats;
    private readonly IAppTimer _flushTimer;
    private DateTimeOffset _sessionStart;
    private DateTimeOffset _lastFlush;
    private bool _active;

    public UsageTracker(AwakeState state, IAppTimer flushTimer)
    {
        _stats = UsageStatsService.Load();

        // A streak only survives overnight; if the last active day is before
        // yesterday the chain is already broken, so show 0 until the next use.
        var today = DateOnly.FromDateTime(DateTime.Now);
        if (_stats.LastActiveDay is { } last && last < today.AddDays(-1))
        {
            _stats.CurrentStreakDays = 0;
        }

        _flushTimer = flushTimer;
        _flushTimer.Interval = TimeSpan.FromMinutes(1);
        _flushTimer.IsRepeating = true;
        _flushTimer.Tick += Flush;

        state.Changed += OnStateChanged;
    }

    /// <summary>Lifetime awake time, including the not-yet-flushed live span.</summary>
    public TimeSpan TotalAwake =>
        TimeSpan.FromSeconds(_stats.TotalAwakeSeconds) + PendingSpan;

    /// <summary>Elapsed time of the running session, or zero when inactive.</summary>
    public TimeSpan CurrentSession =>
        _active ? DateTimeOffset.Now - _sessionStart : TimeSpan.Zero;

    public bool IsSessionActive => _active;

    public int CurrentStreakDays => _stats.CurrentStreakDays;

    public int BestStreakDays => _stats.BestStreakDays;

    private TimeSpan PendingSpan =>
        _active ? DateTimeOffset.Now - _lastFlush : TimeSpan.Zero;

    private void OnStateChanged(bool active)
    {
        if (active)
        {
            _active = true;
            _sessionStart = _lastFlush = DateTimeOffset.Now;
            MarkActiveToday();
            UsageStatsService.Save(_stats);
            _flushTimer.Start();
        }
        else
        {
            _flushTimer.Stop();
            Flush();
            _active = false;
        }
    }

    private void Flush()
    {
        if (!_active)
        {
            return;
        }

        var now = DateTimeOffset.Now;
        _stats.TotalAwakeSeconds += (now - _lastFlush).TotalSeconds;
        _lastFlush = now;
        MarkActiveToday(); // day may have rolled over during a long session
        UsageStatsService.Save(_stats);
    }

    private void MarkActiveToday()
    {
        var today = DateOnly.FromDateTime(DateTime.Now);
        if (_stats.LastActiveDay == today)
        {
            return;
        }

        _stats.CurrentStreakDays = _stats.LastActiveDay == today.AddDays(-1)
            ? _stats.CurrentStreakDays + 1
            : 1;
        _stats.BestStreakDays = Math.Max(_stats.BestStreakDays, _stats.CurrentStreakDays);
        _stats.LastActiveDay = today;
    }
}
```

- [ ] **Step 5: Update `App.xaml.cs` construction sites**

In `App.xaml.cs`, change line 17:

```csharp
    public AwakeState State { get; } = new(() => new DispatcherAppTimer());
```

and in `OnLaunched`, change the `Usage` line (currently line 55):

```csharp
        Usage = new UsageTracker(State, new DispatcherAppTimer()); // must subscribe before the initial State.Set below
```

- [ ] **Step 6: Build**

```powershell
Stop-Process -Name Caffeine -ErrorAction SilentlyContinue
dotnet build Caffeine.csproj -p:Platform=x64 -v:m
```

Expected: `Build succeeded` with 0 errors.

- [ ] **Step 7: Smoke-run the app**

```powershell
Start-Process "bin\x64\Debug\net10.0-windows10.0.19041.0\Caffeine.exe"
```

Verify via UIA or screenshot (see `.claude/skills/verify/SKILL.md`): window opens, Home page shows the status card, toggling `AwakeToggle` flips the status text both ways. Check no `crash.log` next to the exe. Then stop the process (`Stop-Process -Name Caffeine`).

- [ ] **Step 8: Commit**

```bash
git add src/IAppTimer.cs src/DispatcherAppTimer.cs src/AwakeState.cs src/UsageTracker.cs App.xaml.cs
git commit -m "refactor: Abstract UI timers behind IAppTimer

Prepares AwakeState and UsageTracker to move into a UI-free Core project.

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 2: Split into Caffeine.App / Caffeine.Core / Caffeine.Core.Tests

Physical restructure. All moves use `git mv` to preserve history. Intermediate states don't compile — do the whole task, then build.

**Files:**
- Move: every app file from repo root into `src/Caffeine.App/` (details below)
- Move: `src/*.cs` logic files into `src/Caffeine.Core/{Common,Awake,Settings}/`
- Create: `src/Caffeine.Core/Caffeine.Core.csproj`, `tests/Caffeine.Core.Tests/Caffeine.Core.Tests.csproj`, `tests/Caffeine.Core.Tests/SmokeTests.cs`
- Modify: `Caffeine.slnx`, `README.md`, `.claude/skills/verify/SKILL.md`, namespace lines in all moved Core files, `using` lines in `App.xaml.cs`, `Pages/HomePage.xaml.cs`, `Pages/SettingsPage.xaml.cs`

**Interfaces:**
- Consumes: `IAppTimer`, `AwakeState(Func<IAppTimer>)`, `UsageTracker(AwakeState, IAppTimer)` from Task 1.
- Produces: assemblies `Caffeine.Core` (namespaces `Caffeine.Core.Common`, `Caffeine.Core.Awake`, `Caffeine.Core.Settings` — class names unchanged: `AwakeState`, `KeepAwakeService` (internal), `UsageTracker`, `UsageStats`, `UsageStatsService`, `AppSettings`, `SettingsService`, `StartupService`) and test project `Caffeine.Core.Tests` referencing Core. Tasks 3–4 add tests to it; Task 5 edits pages at their new paths.

- [ ] **Step 1: Create directories and move files with git mv (bash)**

```bash
cd "C:/Users/VIBE/source/repos/carrijoga/caffeine"
mkdir -p src/Caffeine.App src/Caffeine.Core/Common src/Caffeine.Core/Awake src/Caffeine.Core/Settings tests/Caffeine.Core.Tests

# App project files
git mv App.xaml App.xaml.cs MainWindow.xaml MainWindow.xaml.cs app.manifest src/Caffeine.App/
git mv Pages src/Caffeine.App/Pages
git mv Assets src/Caffeine.App/Assets
git mv Caffeine.csproj src/Caffeine.App/Caffeine.App.csproj
git mv src/DispatcherAppTimer.cs src/RelayCommand.cs src/Caffeine.App/

# Core files
git mv src/IAppTimer.cs src/Caffeine.Core/Common/
git mv src/AwakeState.cs src/KeepAwakeService.cs src/UsageTracker.cs src/UsageStats.cs src/UsageStatsService.cs src/Caffeine.Core/Awake/
git mv src/AppSettings.cs src/SettingsService.cs src/StartupService.cs src/Caffeine.Core/Settings/

# stale build output of the old root project (untracked)
rm -rf bin obj
```

- [ ] **Step 2: Create `src/Caffeine.Core/Caffeine.Core.csproj`**

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net10.0-windows</TargetFramework>
    <RootNamespace>Caffeine.Core</RootNamespace>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <Platforms>AnyCPU;x64;ARM64</Platforms>
  </PropertyGroup>

</Project>
```

- [ ] **Step 3: Update namespaces in the moved Core files**

Class bodies stay untouched; only the `namespace` line (and one added `using`) changes per file:

| File | Change |
|---|---|
| `src/Caffeine.Core/Awake/AwakeState.cs` | `namespace Caffeine;` → `namespace Caffeine.Core.Awake;` (keeps `using Caffeine.Core.Common;`) |
| `src/Caffeine.Core/Awake/KeepAwakeService.cs` | `namespace Caffeine;` → `namespace Caffeine.Core.Awake;` |
| `src/Caffeine.Core/Awake/UsageTracker.cs` | `namespace Caffeine;` → `namespace Caffeine.Core.Awake;` (keeps `using Caffeine.Core.Common;`) |
| `src/Caffeine.Core/Awake/UsageStats.cs` | `namespace Caffeine;` → `namespace Caffeine.Core.Awake;` |
| `src/Caffeine.Core/Awake/UsageStatsService.cs` | `namespace Caffeine;` → `namespace Caffeine.Core.Awake;` |
| `src/Caffeine.Core/Settings/AppSettings.cs` | `namespace Caffeine;` → `namespace Caffeine.Core.Settings;` |
| `src/Caffeine.Core/Settings/SettingsService.cs` | `namespace Caffeine;` → `namespace Caffeine.Core.Settings;` |
| `src/Caffeine.Core/Settings/StartupService.cs` | `namespace Caffeine;` → `namespace Caffeine.Core.Settings;` |

`src/Caffeine.Core/Common/IAppTimer.cs` already has the right namespace — no edit.

- [ ] **Step 4: Rewrite `src/Caffeine.App/Caffeine.App.csproj`**

Full content (same as the old `Caffeine.csproj` plus the `ProjectReference`):

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <OutputType>WinExe</OutputType>
    <TargetFramework>net10.0-windows10.0.19041.0</TargetFramework>
    <TargetPlatformMinVersion>10.0.17763.0</TargetPlatformMinVersion>
    <RootNamespace>Caffeine</RootNamespace>
    <AssemblyName>Caffeine</AssemblyName>
    <ApplicationManifest>app.manifest</ApplicationManifest>
    <Platforms>x64;ARM64</Platforms>
    <RuntimeIdentifiers>win-x64;win-arm64</RuntimeIdentifiers>
    <UseWinUI>true</UseWinUI>
    <EnableMsixTooling>true</EnableMsixTooling>
    <WindowsPackageType>None</WindowsPackageType>
    <WindowsAppSDKSelfContained>true</WindowsAppSDKSelfContained>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.WindowsAppSDK" Version="1.8.250907003" />
    <PackageReference Include="H.NotifyIcon.WinUI" Version="2.4.1" />
    <PackageReference Include="CommunityToolkit.WinUI.Controls.SettingsControls" Version="8.2.251219" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\Caffeine.Core\Caffeine.Core.csproj" />
  </ItemGroup>

  <ItemGroup>
    <Content Include="Assets\**\*.ico">
      <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
    </Content>
  </ItemGroup>

</Project>
```

- [ ] **Step 5: Add usings in the App source files**

- `src/Caffeine.App/App.xaml.cs` — add after `using System.Drawing;` (line 1):

```csharp
using Caffeine.Core.Awake;
using Caffeine.Core.Settings;
```

- `src/Caffeine.App/Pages/HomePage.xaml.cs` — add as first line:

```csharp
using Caffeine.Core.Awake;
```

- `src/Caffeine.App/Pages/SettingsPage.xaml.cs` — add as first line:

```csharp
using Caffeine.Core.Settings;
```

`MainWindow.xaml.cs`, `WelcomePage.xaml.cs`, `RelayCommand.cs` reference no Core types — no edits.

- [ ] **Step 6: Create the test project**

`tests/Caffeine.Core.Tests/Caffeine.Core.Tests.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net10.0-windows</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <IsPackable>false</IsPackable>
    <Platforms>AnyCPU;x64;ARM64</Platforms>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.12.0" />
    <PackageReference Include="xunit" Version="2.9.2" />
    <PackageReference Include="xunit.runner.visualstudio" Version="2.8.2" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\src\Caffeine.Core\Caffeine.Core.csproj" />
  </ItemGroup>

</Project>
```

`tests/Caffeine.Core.Tests/SmokeTests.cs`:

```csharp
using Caffeine.Core.Common;
using Xunit;

namespace Caffeine.Core.Tests;

public class SmokeTests
{
    [Fact]
    public void CoreAssemblyLoads() =>
        Assert.NotNull(typeof(IAppTimer).Assembly);
}
```

- [ ] **Step 7: Rewrite `Caffeine.slnx`**

```xml
<Solution>
  <Configurations>
    <Platform Name="ARM64" />
    <Platform Name="x64" />
  </Configurations>
  <Project Path="src/Caffeine.App/Caffeine.App.csproj">
    <Platform Solution="*|ARM64" Project="ARM64" />
    <Platform Solution="*|x64" Project="x64" />
  </Project>
  <Project Path="src/Caffeine.Core/Caffeine.Core.csproj" />
  <Project Path="tests/Caffeine.Core.Tests/Caffeine.Core.Tests.csproj" />
</Solution>
```

- [ ] **Step 8: Update `README.md` Building and Project layout sections**

Replace the two code blocks in "Building" with:

```powershell
dotnet build src/Caffeine.App/Caffeine.App.csproj -p:Platform=x64 -p:RuntimeIdentifier=win-x64
# run: src\Caffeine.App\bin\x64\Debug\net10.0-windows10.0.19041.0\win-x64\Caffeine.exe
```

```powershell
dotnet publish src/Caffeine.App/Caffeine.App.csproj -c Release -p:Platform=x64 -r win-x64 --self-contained
```

Add below them:

```powershell
dotnet test tests/Caffeine.Core.Tests/Caffeine.Core.Tests.csproj
```

Replace the "Project layout" list with:

```markdown
- [src/Caffeine.Core](src/Caffeine.Core) — all app logic, UI-free (keep-awake, settings, usage stats)
- [src/Caffeine.App](src/Caffeine.App) — the WinUI 3 shell: windows, pages, tray icon
- [tests/Caffeine.Core.Tests](tests/Caffeine.Core.Tests) — xUnit tests for Core
- [docs/superpowers/specs](docs/superpowers/specs) — design docs
```

- [ ] **Step 9: Update `.claude/skills/verify/SKILL.md` build/launch paths**

Replace the "Build & launch" code block with:

```powershell
dotnet build src/Caffeine.App/Caffeine.App.csproj -p:Platform=x64 -v:m   # ~25s
Start-Process "src\Caffeine.App\bin\x64\Debug\net10.0-windows10.0.19041.0\Caffeine.exe"
```

(The crash.log note stays true — it lands next to the exe.)

- [ ] **Step 10: Build everything and run tests**

```powershell
Stop-Process -Name Caffeine -ErrorAction SilentlyContinue
dotnet build src/Caffeine.App/Caffeine.App.csproj -p:Platform=x64 -v:m
dotnet test tests/Caffeine.Core.Tests/Caffeine.Core.Tests.csproj
```

Expected: build succeeds; test run reports `Passed! - 1 test`.

- [ ] **Step 11: Smoke-run**

```powershell
Start-Process "src\Caffeine.App\bin\x64\Debug\net10.0-windows10.0.19041.0\Caffeine.exe"
```

Verify: window opens, Home/Settings/Welcome navigation works, toggle flips state, tray icon appears, no crash.log. Stop the process afterward.

- [ ] **Step 12: Commit**

```bash
git add -A
git commit -m "refactor: Split into Caffeine.Core, Caffeine.App and test projects

Core holds all UI-free logic under module namespaces (Common, Awake,
Settings); the WinUI shell moves to src/Caffeine.App; xUnit test project
added. Behavior unchanged.

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 3: JsonStore<T> persistence helper (TDD)

The shared persistence layer for current settings/stats and the future Habits/Todos/Timers stores: atomic writes, corrupt-file backup.

**Files:**
- Create: `src/Caffeine.Core/Common/JsonStore.cs`
- Create: `tests/Caffeine.Core.Tests/JsonStoreTests.cs`
- Modify: `src/Caffeine.Core/Settings/SettingsService.cs` (full rewrite, below)
- Modify: `src/Caffeine.Core/Awake/UsageStatsService.cs` (full rewrite, below)

**Interfaces:**
- Consumes: nothing from other tasks.
- Produces: `sealed class Caffeine.Core.Common.JsonStore<T> where T : class, new()` with `JsonStore(string fileName, string? directory = null)` (directory defaults to `%LocalAppData%\Caffeine`), `T Load()`, `void Save(T document)`. Stages 2–4 build their stores on this. `SettingsService`/`UsageStatsService` keep their existing public static `Load()`/`Save(...)` signatures.

- [ ] **Step 1: Write the failing tests — `tests/Caffeine.Core.Tests/JsonStoreTests.cs`**

```csharp
using Caffeine.Core.Common;
using Xunit;

namespace Caffeine.Core.Tests;

internal sealed class TestDoc
{
    public string Name { get; set; } = string.Empty;

    public int Count { get; set; }
}

public sealed class JsonStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(
        Path.GetTempPath(), "caffeine-tests-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch
        {
        }
    }

    [Fact]
    public void Load_MissingFile_ReturnsFreshDocument()
    {
        var store = new JsonStore<TestDoc>("doc.json", _dir);

        var doc = store.Load();

        Assert.Equal(string.Empty, doc.Name);
        Assert.Equal(0, doc.Count);
    }

    [Fact]
    public void SaveThenLoad_RoundTrips()
    {
        var store = new JsonStore<TestDoc>("doc.json", _dir);

        store.Save(new TestDoc { Name = "espresso", Count = 3 });
        var doc = store.Load();

        Assert.Equal("espresso", doc.Name);
        Assert.Equal(3, doc.Count);
    }

    [Fact]
    public void Save_OverwritesExistingFile()
    {
        var store = new JsonStore<TestDoc>("doc.json", _dir);

        store.Save(new TestDoc { Name = "first" });
        store.Save(new TestDoc { Name = "second" });

        Assert.Equal("second", store.Load().Name);
        Assert.False(File.Exists(Path.Combine(_dir, "doc.json.tmp")));
    }

    [Fact]
    public void Load_CorruptFile_BacksUpAndReturnsFreshDocument()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, "doc.json"), "{ this is not json !!!");
        var store = new JsonStore<TestDoc>("doc.json", _dir);

        var doc = store.Load();

        Assert.Equal(0, doc.Count);
        Assert.True(File.Exists(Path.Combine(_dir, "doc.json.corrupt.bak")));
        Assert.False(File.Exists(Path.Combine(_dir, "doc.json")));
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Caffeine.Core.Tests/Caffeine.Core.Tests.csproj`
Expected: build FAILS with `CS0246: The type or namespace name 'JsonStore<>' could not be found` (a compile failure is this cycle's red).

- [ ] **Step 3: Implement `src/Caffeine.Core/Common/JsonStore.cs`**

```csharp
using System.Text.Json;

namespace Caffeine.Core.Common;

/// <summary>
/// Typed JSON persistence for one document under %LocalAppData%\Caffeine\.
/// Writes are atomic (temp file, then move/replace) so a crash mid-save can
/// never corrupt the store. A corrupt file on load is renamed to
/// *.corrupt.bak and replaced with a fresh document — the app never crashes
/// over bad data. All I/O is best-effort, mirroring the old services.
/// </summary>
public sealed class JsonStore<T>
    where T : class, new()
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    private readonly string _path;

    public JsonStore(string fileName, string? directory = null)
    {
        directory ??= Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Caffeine");
        _path = Path.Combine(directory, fileName);
    }

    public T Load()
    {
        try
        {
            if (!File.Exists(_path))
            {
                return new T();
            }

            return JsonSerializer.Deserialize<T>(File.ReadAllText(_path), Options) ?? new T();
        }
        catch
        {
            BackupCorruptFile();
            return new T();
        }
    }

    public void Save(T document)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);

            string tmp = _path + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(document, Options));
            File.Move(tmp, _path, overwrite: true);
        }
        catch
        {
            // Persistence is best-effort; never take the app down over disk I/O.
        }
    }

    private void BackupCorruptFile()
    {
        try
        {
            File.Move(_path, _path + ".corrupt.bak", overwrite: true);
        }
        catch
        {
        }
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/Caffeine.Core.Tests/Caffeine.Core.Tests.csproj`
Expected: `Passed! - 5 tests` (4 new + smoke test), 0 failed.

- [ ] **Step 5: Rewrite `src/Caffeine.Core/Settings/SettingsService.cs` on top of JsonStore**

```csharp
using Caffeine.Core.Common;

namespace Caffeine.Core.Settings;

/// <summary>
/// Loads and saves <see cref="AppSettings"/> to %LocalAppData%\Caffeine\settings.json.
/// The app is unpackaged, so there is no ApplicationData store to use instead.
/// </summary>
public static class SettingsService
{
    private static readonly JsonStore<AppSettings> Store = new("settings.json");

    public static AppSettings Load() => Store.Load();

    public static void Save(AppSettings settings) => Store.Save(settings);
}
```

- [ ] **Step 6: Rewrite `src/Caffeine.Core/Awake/UsageStatsService.cs` on top of JsonStore**

```csharp
using Caffeine.Core.Common;

namespace Caffeine.Core.Awake;

/// <summary>
/// Loads and saves <see cref="UsageStats"/> to %LocalAppData%\Caffeine\usage.json,
/// mirroring <see cref="Caffeine.Core.Settings.SettingsService"/>.
/// </summary>
public static class UsageStatsService
{
    private static readonly JsonStore<UsageStats> Store = new("usage.json");

    public static UsageStats Load() => Store.Load();

    public static void Save(UsageStats stats) => Store.Save(stats);
}
```

- [ ] **Step 7: Full build + tests**

```powershell
Stop-Process -Name Caffeine -ErrorAction SilentlyContinue
dotnet build src/Caffeine.App/Caffeine.App.csproj -p:Platform=x64 -v:m
dotnet test tests/Caffeine.Core.Tests/Caffeine.Core.Tests.csproj
```

Expected: build succeeds, 5 tests pass.

- [ ] **Step 8: Commit**

```bash
git add src/Caffeine.Core/Common/JsonStore.cs tests/Caffeine.Core.Tests/JsonStoreTests.cs src/Caffeine.Core/Settings/SettingsService.cs src/Caffeine.Core/Awake/UsageStatsService.cs
git commit -m "feat: Add JsonStore<T> with atomic writes and corrupt-file recovery

Settings and usage stats now persist through the shared store; future
hub modules (habits, todos, timers) will use it too.

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 4: AwakeState unit tests with a fake timer

Locks in the Task 1 abstraction: timed mode arms a one-shot timer whose tick deactivates keep-awake.

**Files:**
- Create: `tests/Caffeine.Core.Tests/FakeTimer.cs`
- Create: `tests/Caffeine.Core.Tests/AwakeStateTests.cs`

**Interfaces:**
- Consumes: `IAppTimer`, `AwakeState(Func<IAppTimer>)` from Task 1.
- Produces: `internal sealed class FakeTimer : IAppTimer` with extra members `bool IsRunning { get; }` and `void Fire()` — reusable by later stages' timer tests.

Note: these tests call the real `SetThreadExecutionState` P/Invoke (harmless on the test thread), so each test must end deactivated — the `finally { state.Set(false); }` blocks are mandatory.

- [ ] **Step 1: Write `tests/Caffeine.Core.Tests/FakeTimer.cs`**

```csharp
using Caffeine.Core.Common;

namespace Caffeine.Core.Tests;

/// <summary>Hand-fired IAppTimer for tests.</summary>
internal sealed class FakeTimer : IAppTimer
{
    public TimeSpan Interval { get; set; }

    public bool IsRepeating { get; set; } = true;

    public bool IsRunning { get; private set; }

    public event Action? Tick;

    public void Start() => IsRunning = true;

    public void Stop() => IsRunning = false;

    public void Fire()
    {
        if (!IsRepeating)
        {
            IsRunning = false;
        }

        Tick?.Invoke();
    }
}
```

- [ ] **Step 2: Write the failing tests — `tests/Caffeine.Core.Tests/AwakeStateTests.cs`**

```csharp
using Caffeine.Core.Awake;
using Xunit;

namespace Caffeine.Core.Tests;

public class AwakeStateTests
{
    [Fact]
    public void TimedMode_ArmsTimerAndTurnsOffWhenItFires()
    {
        var timer = new FakeTimer();
        var state = new AwakeState(() => timer) { Interval = TimeSpan.FromMinutes(5) };

        try
        {
            state.Set(true);

            Assert.True(state.IsActive);
            Assert.True(timer.IsRunning);
            Assert.False(timer.IsRepeating);
            Assert.Equal(TimeSpan.FromMinutes(5), timer.Interval);
            Assert.NotNull(state.SessionEndsAt);

            timer.Fire();

            Assert.False(state.IsActive);
            Assert.Null(state.SessionEndsAt);
        }
        finally
        {
            state.Set(false); // always release the real power request
        }
    }

    [Fact]
    public void IndefiniteMode_NeverCreatesTimer()
    {
        bool timerCreated = false;
        var state = new AwakeState(() =>
        {
            timerCreated = true;
            return new FakeTimer();
        });

        try
        {
            state.Set(true);

            Assert.True(state.IsActive);
            Assert.False(timerCreated);
            Assert.Null(state.SessionEndsAt);
        }
        finally
        {
            state.Set(false);
        }
    }

    [Fact]
    public void TurningOff_StopsTimerAndClearsSessionEnd()
    {
        var timer = new FakeTimer();
        var state = new AwakeState(() => timer) { Interval = TimeSpan.FromMinutes(5) };

        try
        {
            state.Set(true);
            state.Set(false);

            Assert.False(state.IsActive);
            Assert.False(timer.IsRunning);
            Assert.Null(state.SessionEndsAt);
        }
        finally
        {
            state.Set(false);
        }
    }
}
```

- [ ] **Step 3: Run the new tests**

Run: `dotnet test tests/Caffeine.Core.Tests/Caffeine.Core.Tests.csproj`
Expected: all 8 tests PASS immediately — the production code already exists (Task 1). If any fail, the Task 1 refactor has a bug: fix `AwakeState`, not the tests. (There is no red phase here; these are characterization tests for already-written code.)

- [ ] **Step 4: Commit**

```bash
git add tests/Caffeine.Core.Tests/FakeTimer.cs tests/Caffeine.Core.Tests/AwakeStateTests.cs
git commit -m "test: Cover AwakeState timed mode via FakeTimer

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 5: Hub navigation — Awake page, new Home dashboard

`HomePage` (the Awake dashboard) becomes `AwakePage`; a new `HomePage` is the hub dashboard shell with the Awake card; the sidebar gains an Awake item and Settings moves to the footer (per spec).

**Files:**
- Move: `src/Caffeine.App/Pages/HomePage.xaml` → `src/Caffeine.App/Pages/AwakePage.xaml` (+ `.xaml.cs`)
- Create: `src/Caffeine.App/Pages/HomePage.xaml`, `src/Caffeine.App/Pages/HomePage.xaml.cs` (new dashboard)
- Modify: `src/Caffeine.App/MainWindow.xaml`, `src/Caffeine.App/MainWindow.xaml.cs`, `src/Caffeine.App/App.xaml.cs`, `.claude/skills/verify/SKILL.md`

**Interfaces:**
- Consumes: `AwakeState` (`IsActive`, `KeepScreenOn`, `Set(bool)`, `Changed`) from Core.
- Produces: `MainWindow.NavigateTo(string tag)` (selects the sidebar item whose `Tag` matches: `"home"`, `"awake"`, `"settings"`, `"welcome"`); `App.NavigateTo(string tag)` forwarding to the window. Later stages' Home cards navigate with these.

- [ ] **Step 1: Rename the old dashboard to AwakePage**

```bash
cd "C:/Users/VIBE/source/repos/carrijoga/caffeine"
git mv src/Caffeine.App/Pages/HomePage.xaml src/Caffeine.App/Pages/AwakePage.xaml
git mv src/Caffeine.App/Pages/HomePage.xaml.cs src/Caffeine.App/Pages/AwakePage.xaml.cs
```

Then three text edits:
- `AwakePage.xaml` line 2: `x:Class="Caffeine.Pages.HomePage"` → `x:Class="Caffeine.Pages.AwakePage"`
- `AwakePage.xaml` line 26: `Text="Caffeine"` → `Text="Awake"` (page heading; everything else unchanged)
- `AwakePage.xaml.cs`: `public sealed partial class HomePage : Page` → `public sealed partial class AwakePage : Page`, and the constructor `public HomePage()` → `public AwakePage()`

- [ ] **Step 2: Create the new `src/Caffeine.App/Pages/HomePage.xaml`**

```xml
<Page
    x:Class="Caffeine.Pages.HomePage"
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">

    <Page.Resources>
        <!-- Card chrome matching the SettingsCards used across the app. -->
        <Style x:Key="HubCardStyle" TargetType="Border">
            <Setter Property="Background" Value="{ThemeResource CardBackgroundFillColorDefaultBrush}" />
            <Setter Property="BorderBrush" Value="{ThemeResource CardStrokeColorDefaultBrush}" />
            <Setter Property="BorderThickness" Value="1" />
            <Setter Property="CornerRadius" Value="{StaticResource ControlCornerRadius}" />
        </Style>
        <Style x:Key="HubCaptionStyle" TargetType="TextBlock" BasedOn="{StaticResource CaptionTextBlockStyle}">
            <Setter Property="Foreground" Value="{ThemeResource TextFillColorSecondaryBrush}" />
        </Style>
    </Page.Resources>

    <ScrollViewer>
        <StackPanel Padding="36,24,36,36" Spacing="4" MaxWidth="800" HorizontalAlignment="Stretch">

            <TextBlock
                Text="Home"
                Style="{StaticResource TitleTextBlockStyle}"
                Margin="0,0,0,4" />
            <TextBlock
                Text="Your day at a glance."
                Style="{StaticResource BodyTextBlockStyle}"
                Foreground="{ThemeResource TextFillColorSecondaryBrush}"
                TextWrapping="Wrap"
                Margin="0,0,0,20" />

            <!-- Awake module card -->
            <Border Style="{StaticResource HubCardStyle}" Padding="20,18">
                <Grid ColumnSpacing="16">
                    <Grid.ColumnDefinitions>
                        <ColumnDefinition Width="Auto" />
                        <ColumnDefinition Width="*" />
                        <ColumnDefinition Width="Auto" />
                        <ColumnDefinition Width="Auto" />
                    </Grid.ColumnDefinitions>

                    <Grid Width="48" Height="48" VerticalAlignment="Center">
                        <Border
                            x:Name="BadgeActive"
                            CornerRadius="24"
                            Background="{ThemeResource AccentFillColorDefaultBrush}" />
                        <Border
                            x:Name="BadgeInactive"
                            CornerRadius="24"
                            Background="{ThemeResource SubtleFillColorSecondaryBrush}"
                            Visibility="Collapsed" />
                        <TextBlock
                            Text="&#x2615;"
                            FontSize="22"
                            HorizontalAlignment="Center"
                            VerticalAlignment="Center" />
                    </Grid>

                    <StackPanel Grid.Column="1" VerticalAlignment="Center" Spacing="2">
                        <TextBlock Text="Awake" Style="{StaticResource BodyStrongTextBlockStyle}" />
                        <TextBlock
                            x:Name="AwakeCaption"
                            Text="Your PC can sleep"
                            Style="{StaticResource HubCaptionStyle}"
                            TextWrapping="Wrap" />
                    </StackPanel>

                    <ToggleSwitch
                        Grid.Column="2"
                        x:Name="AwakeToggle"
                        OnContent="On"
                        OffContent="Off"
                        MinWidth="0"
                        Margin="0,-2,0,-2"
                        VerticalAlignment="Center"
                        Toggled="AwakeToggle_Toggled" />

                    <Button
                        Grid.Column="3"
                        x:Name="OpenAwakeButton"
                        VerticalAlignment="Center"
                        Click="OpenAwake_Click"
                        AutomationProperties.Name="Open Awake">
                        <FontIcon Glyph="&#xE76C;" FontSize="12" />
                    </Button>
                </Grid>
            </Border>

            <!-- Habits, Todos and Timers cards land here in later stages. -->
        </StackPanel>
    </ScrollViewer>
</Page>
```

- [ ] **Step 3: Create the new `src/Caffeine.App/Pages/HomePage.xaml.cs`**

```csharp
using Caffeine.Core.Awake;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Caffeine.Pages;

public sealed partial class HomePage : Page
{
    private readonly AwakeState _state;
    private bool _updatingToggle;

    public HomePage()
    {
        _state = ((App)Application.Current).State;
        InitializeComponent();

        _state.Changed += OnStateChanged;
        Unloaded += (_, _) => _state.Changed -= OnStateChanged;

        OnStateChanged(_state.IsActive);
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

    private void OpenAwake_Click(object sender, RoutedEventArgs e) =>
        ((App)Application.Current).NavigateTo("awake");
}
```

- [ ] **Step 4: Update `src/Caffeine.App/MainWindow.xaml` navigation items**

Replace the `<NavigationView.MenuItems>` and `<NavigationView.FooterMenuItems>` blocks with:

```xml
            <NavigationView.MenuItems>
                <NavigationViewItem x:Name="HomeItem" Content="Home" Tag="home">
                    <NavigationViewItem.Icon>
                        <FontIcon Glyph="&#xE80F;" />
                    </NavigationViewItem.Icon>
                </NavigationViewItem>
                <NavigationViewItem Content="Awake" Tag="awake">
                    <NavigationViewItem.Icon>
                        <FontIcon FontFamily="Segoe UI Emoji" Glyph="&#x2615;" />
                    </NavigationViewItem.Icon>
                </NavigationViewItem>
            </NavigationView.MenuItems>

            <NavigationView.FooterMenuItems>
                <NavigationViewItem Content="Settings" Tag="settings">
                    <NavigationViewItem.Icon>
                        <FontIcon Glyph="&#xE713;" />
                    </NavigationViewItem.Icon>
                </NavigationViewItem>
                <NavigationViewItem Content="Welcome to Caffeine" Tag="welcome">
                    <NavigationViewItem.Icon>
                        <FontIcon Glyph="&#xE82F;" />
                    </NavigationViewItem.Icon>
                </NavigationViewItem>
            </NavigationView.FooterMenuItems>
```

- [ ] **Step 5: Update `src/Caffeine.App/MainWindow.xaml.cs`**

Replace the `NavView_SelectionChanged` method and add `NavigateTo` below it:

```csharp
    private void NavView_SelectionChanged(
        NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItem is NavigationViewItem item)
        {
            Type page = (string)item.Tag switch
            {
                "awake" => typeof(Pages.AwakePage),
                "settings" => typeof(Pages.SettingsPage),
                "welcome" => typeof(Pages.WelcomePage),
                _ => typeof(Pages.HomePage),
            };

            if (ContentFrame.CurrentSourcePageType != page)
            {
                ContentFrame.Navigate(page);
            }
        }
    }

    /// <summary>Selects the sidebar item with the given Tag ("home", "awake", "settings", "welcome").</summary>
    public void NavigateTo(string tag)
    {
        foreach (object entry in NavView.MenuItems.Concat(NavView.FooterMenuItems))
        {
            if (entry is NavigationViewItem item && (string)item.Tag == tag)
            {
                NavView.SelectedItem = item;
                return;
            }
        }
    }
```

Add `using System.Linq;` only if the compiler asks for it (`ImplicitUsings` already covers it).

- [ ] **Step 6: Add `NavigateTo` to `src/Caffeine.App/App.xaml.cs`**

Insert after the `ExitApp` method, inside the class:

```csharp
    /// <summary>Navigate the main window to a sidebar section by tag.</summary>
    public void NavigateTo(string tag) => _window?.NavigateTo(tag);
```

- [ ] **Step 7: Update `.claude/skills/verify/SKILL.md` nav item list**

Change the "Nav items" bullet to:

```markdown
- Nav items ("Home", "Awake", "Settings", "Welcome to Caffeine"): `SelectionItemPattern.Select()`.
```

- [ ] **Step 8: Build and test**

```powershell
Stop-Process -Name Caffeine -ErrorAction SilentlyContinue
dotnet build src/Caffeine.App/Caffeine.App.csproj -p:Platform=x64 -v:m
dotnet test tests/Caffeine.Core.Tests/Caffeine.Core.Tests.csproj
```

Expected: build succeeds, 8 tests pass.

- [ ] **Step 9: Runtime verification (use the `verify` skill)**

Launch the app and confirm via UIA/screenshot:
1. Sidebar shows Home, Awake (main) and Settings, Welcome to Caffeine (footer).
2. Home shows the "Your day at a glance." dashboard with the Awake card; the card's toggle flips the caption between "Your PC can sleep" and "Keeping your PC and screen awake".
3. The card's "Open Awake" button navigates to the Awake page (usage stats, killstreak, session counter — the old dashboard, now titled "Awake").
4. Toggling on the Awake page is reflected back on Home (state is shared).
5. Settings and Welcome pages still load.
6. No `crash.log`. Stop the process afterward.

- [ ] **Step 10: Commit**

```bash
git add -A
git commit -m "feat: Add hub navigation - Awake page and Home dashboard shell

The old Home dashboard becomes the Awake module page; Home is now the
hub dashboard with an Awake status card. Settings moves to the sidebar
footer.

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

## Done criteria (Stage 1)

- Solution = 3 projects; `Caffeine.Core` has no WinUI/WindowsAppSDK references.
- `dotnet test` green (8 tests); app builds and runs from `src/Caffeine.App`.
- Sidebar: Home + Awake, footer Settings + Welcome; Home shows the Awake card; Awake page = old dashboard.
- Settings, usage stats, tray behavior, startup toggle all work exactly as before.
- README build instructions and the `verify` skill match the new layout.
