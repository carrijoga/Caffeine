# Life Hub Stage 3 — Todos Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add the Todos module — `TodoService` in Core with tests, a Todos page with Active/Completed views, and a Home dashboard card — per the approved life-hub design spec (Stage 3), opening with the Pomodoro config sanitize clamp carried from the Stage 2 final review.

**Architecture:** `TodoItem`/`TodoList`/`TodoService` live in `Caffeine.Core/Todos` with zero UI dependencies; the service takes `IClock` (so tests fast-forward time) and a `JsonStore<TodoList>` (so tests point at a temp directory) and saves immediately on every mutation. The App layer owns one `TodoService` instance on `App` (same pattern as `TimersService`), a code-behind `TodosPage` that rebuilds its row list on each change, and a Home card that summarizes due-today/overdue counts.

**Tech Stack:** .NET 10, WinUI 3 (Windows App SDK 1.8.250907003, unpackaged), xUnit, System.Text.Json via existing `JsonStore<T>`.

## Global Constraints

- `Caffeine.Core` references no WinUI/WindowsAppSDK packages; everything in it is unit-testable headlessly.
- Storage: new `todos.json` in `%LocalAppData%\Caffeine\` via `JsonStore<TodoList>`; saves happen immediately on every change; corrupt file recovery is JsonStore's job (already built).
- Todos v1 scope (spec verbatim): "Single list. Item: title, optional due date, done flag, created/completed timestamps. Add via textbox at top, complete via checkbox, delete with button. Two views: **Active** (sorted overdue → due date → newest) and **Completed**. No priorities, projects, or subtasks in v1."
- Home card shows today's + overdue items summary; card navigates to the Todos page.
- Sidebar order after this stage: **Home, Todos, Timers, Awake** (spec lists module pages as Habits, Todos, Timers — Habits arrives in Stage 4 above Todos), footer Settings + Welcome unchanged.
- Home card order after this stage: **Todos, Timers, Awake** (spec dashboard order: Habits, Todos, Timers, Awake).
- Build command (from repo root; kill a running app first):
  `Stop-Process -Name Caffeine -ErrorAction SilentlyContinue; dotnet build src/Caffeine.App/Caffeine.App.csproj -p:Platform=x64 -v:m`
- Test command: `dotnet test tests/Caffeine.Core.Tests/Caffeine.Core.Tests.csproj`
  (Output is Portuguese: "Aprovado!" = passed, "Com falha" = failed.)
- UI tasks verify at runtime per `.claude/skills/verify/SKILL.md` (UIA patterns + PrintWindow screenshots; never CopyFromScreen).
- New XAML pages are auto-included by the SDK — no `.csproj` edits needed.

---

## File structure

```
src/Caffeine.Core/Timers/PomodoroConfig.cs      (modify: Sanitize)      Task 1
src/Caffeine.App/TimersService.cs               (modify: call Sanitize) Task 1
tests/Caffeine.Core.Tests/PomodoroConfigTests.cs (create)               Task 1
src/Caffeine.Core/Todos/TodoItem.cs             (create)                Task 2
src/Caffeine.Core/Todos/TodoList.cs             (create)                Task 2
src/Caffeine.Core/Todos/TodoService.cs          (create)                Task 2
tests/Caffeine.Core.Tests/TodoServiceTests.cs   (create)                Task 2
src/Caffeine.App/App.xaml.cs                    (modify: Todos property) Task 3
src/Caffeine.App/MainWindow.xaml(.cs)           (modify: nav item)      Task 3
src/Caffeine.App/Pages/TodosPage.xaml(.cs)      (create)                Task 3
src/Caffeine.App/Pages/HomePage.xaml(.cs)       (modify: Todos card)    Task 4
.claude/skills/verify/SKILL.md                  (modify: nav list)      Task 3
README.md                                       (modify: Todos bullet)  Task 4
```

---

### Task 1: PomodoroConfig sanitize clamp (carried Stage 2 review item)

A hand-edited `timers.json` with `CyclesPerLongBreak: 0` currently throws
`DivideByZeroException` inside `PomodoroEngine` when a work phase completes
(`CompletedWorkSessions % Config.CyclesPerLongBreak`). Zero/negative minute
values create zero-length phases. Clamp all four values to at least 1 when
the app loads the config.

**Files:**
- Modify: `src/Caffeine.Core/Timers/PomodoroConfig.cs`
- Modify: `src/Caffeine.App/TimersService.cs` (constructor, after `Config = _store.Load();`)
- Create: `tests/Caffeine.Core.Tests/PomodoroConfigTests.cs`

**Interfaces:**
- Consumes: existing `PomodoroConfig` properties (`WorkMinutes`, `ShortBreakMinutes`, `LongBreakMinutes`, `CyclesPerLongBreak`, all `int`, defaults 25/5/15/4).
- Produces: `public void Sanitize()` on `PomodoroConfig` — clamps each of the four properties to `Math.Max(1, value)`. `TimersService` calls it once at load.

- [ ] **Step 1: Write the failing tests**

Create `tests/Caffeine.Core.Tests/PomodoroConfigTests.cs`:

```csharp
using Caffeine.Core.Timers;

namespace Caffeine.Core.Tests;

public class PomodoroConfigTests
{
    [Fact]
    public void Sanitize_ClampsZeroAndNegativeValuesToOne()
    {
        var config = new PomodoroConfig
        {
            WorkMinutes = 0,
            ShortBreakMinutes = -5,
            LongBreakMinutes = 0,
            CyclesPerLongBreak = 0,
        };

        config.Sanitize();

        Assert.Equal(1, config.WorkMinutes);
        Assert.Equal(1, config.ShortBreakMinutes);
        Assert.Equal(1, config.LongBreakMinutes);
        Assert.Equal(1, config.CyclesPerLongBreak);
    }

    [Fact]
    public void Sanitize_LeavesValidValuesUntouched()
    {
        var config = new PomodoroConfig
        {
            WorkMinutes = 50,
            ShortBreakMinutes = 10,
            LongBreakMinutes = 30,
            CyclesPerLongBreak = 2,
        };

        config.Sanitize();

        Assert.Equal(50, config.WorkMinutes);
        Assert.Equal(10, config.ShortBreakMinutes);
        Assert.Equal(30, config.LongBreakMinutes);
        Assert.Equal(2, config.CyclesPerLongBreak);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Caffeine.Core.Tests/Caffeine.Core.Tests.csproj`
Expected: BUILD FAILURE with CS1061 — `PomodoroConfig` does not contain a definition for `Sanitize`.

- [ ] **Step 3: Implement Sanitize**

In `src/Caffeine.Core/Timers/PomodoroConfig.cs`, add inside the class (after `CyclesPerLongBreak`):

```csharp
    /// <summary>
    /// Clamps every value to at least 1. A hand-edited timers.json with zero
    /// cycles would otherwise divide by zero when a work phase completes.
    /// </summary>
    public void Sanitize()
    {
        WorkMinutes = Math.Max(1, WorkMinutes);
        ShortBreakMinutes = Math.Max(1, ShortBreakMinutes);
        LongBreakMinutes = Math.Max(1, LongBreakMinutes);
        CyclesPerLongBreak = Math.Max(1, CyclesPerLongBreak);
    }
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/Caffeine.Core.Tests/Caffeine.Core.Tests.csproj`
Expected: all pass ("Aprovado!"), suite count 33 (31 existing + 2 new).

- [ ] **Step 5: Call Sanitize at load in TimersService**

In `src/Caffeine.App/TimersService.cs`, the constructor currently begins:

```csharp
    public TimersService(Func<IAppTimer> timerFactory)
    {
        Config = _store.Load();
```

Change to:

```csharp
    public TimersService(Func<IAppTimer> timerFactory)
    {
        Config = _store.Load();
        Config.Pomodoro.Sanitize();
```

- [ ] **Step 6: Build the app to verify it compiles**

Run: `Stop-Process -Name Caffeine -ErrorAction SilentlyContinue; dotnet build src/Caffeine.App/Caffeine.App.csproj -p:Platform=x64 -v:m`
Expected: Build succeeded, 0 errors.

- [ ] **Step 7: Commit**

```bash
git add tests/Caffeine.Core.Tests/PomodoroConfigTests.cs src/Caffeine.Core/Timers/PomodoroConfig.cs src/Caffeine.App/TimersService.cs
git commit -m "fix: Clamp hand-edited Pomodoro config values on load"
```

---

### Task 2: TodoItem, TodoList, TodoService (TDD)

The whole Todos domain: item model, persistence document, and the service
with sorted views and immediate saves. "Today" is the clock's local date so
due/overdue flips at local midnight; tests derive expected dates from the
same conversion, keeping them timezone-safe.

**Files:**
- Create: `src/Caffeine.Core/Todos/TodoItem.cs`
- Create: `src/Caffeine.Core/Todos/TodoList.cs`
- Create: `src/Caffeine.Core/Todos/TodoService.cs`
- Create: `tests/Caffeine.Core.Tests/TodoServiceTests.cs`

**Interfaces:**
- Consumes: `Caffeine.Core.Common.IClock` (`DateTimeOffset UtcNow { get; }`), `Caffeine.Core.Common.JsonStore<T>` (`JsonStore(string fileName, string? directory = null)`, `T Load()`, `void Save(T document)`), test helper `FakeClock` (settable `UtcNow`, default 2026-01-01 12:00 UTC, `Advance(TimeSpan)`).
- Produces (Tasks 3 and 4 rely on these exact members):
  - `TodoItem`: `Guid Id`, `string Title`, `DateOnly? DueDate`, `bool IsDone`, `DateTimeOffset CreatedAt`, `DateTimeOffset? CompletedAt`.
  - `TodoList`: `List<TodoItem> Items`.
  - `TodoService(IClock clock, JsonStore<TodoList> store)`:
    `DateOnly Today { get; }`,
    `IReadOnlyList<TodoItem> Active { get; }`,
    `IReadOnlyList<TodoItem> Completed { get; }`,
    `bool IsOverdue(TodoItem item)`, `bool IsDueToday(TodoItem item)`,
    `TodoItem? Add(string title, DateOnly? dueDate = null)`,
    `void SetDone(Guid id, bool done)`, `void Delete(Guid id)`.

- [ ] **Step 1: Write the failing tests**

Create `tests/Caffeine.Core.Tests/TodoServiceTests.cs`:

```csharp
using Caffeine.Core.Common;
using Caffeine.Core.Todos;

namespace Caffeine.Core.Tests;

public class TodoServiceTests : IDisposable
{
    private readonly string _dir = Path.Combine(
        Path.GetTempPath(), "caffeine-tests-" + Guid.NewGuid().ToString("N"));

    private readonly FakeClock _clock = new();

    private TodoService CreateService() =>
        new(_clock, new JsonStore<TodoList>("todos.json", _dir));

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
    public void Add_TrimsTitle_AndStampsCreation()
    {
        var service = CreateService();

        TodoItem? item = service.Add("  buy milk  ");

        Assert.NotNull(item);
        Assert.Equal("buy milk", item.Title);
        Assert.Equal(_clock.UtcNow, item.CreatedAt);
        Assert.False(item.IsDone);
        Assert.Null(item.DueDate);
        Assert.Null(item.CompletedAt);
    }

    [Fact]
    public void Add_WhitespaceTitle_IsIgnored()
    {
        var service = CreateService();

        Assert.Null(service.Add("   "));
        Assert.Null(service.Add(string.Empty));
        Assert.Empty(service.Active);
    }

    [Fact]
    public void Active_SortsOverdueThenDueDateThenNewest()
    {
        var service = CreateService();
        DateOnly today = service.Today;

        TodoItem noDueOld = service.Add("no due, old")!;
        _clock.Advance(TimeSpan.FromMinutes(1));
        TodoItem dueTomorrow = service.Add("due tomorrow", today.AddDays(1))!;
        _clock.Advance(TimeSpan.FromMinutes(1));
        TodoItem overdue = service.Add("overdue", today.AddDays(-2))!;
        _clock.Advance(TimeSpan.FromMinutes(1));
        TodoItem dueToday = service.Add("due today", today)!;
        _clock.Advance(TimeSpan.FromMinutes(1));
        TodoItem noDueNew = service.Add("no due, new")!;

        Assert.Equal(
            new[] { overdue.Id, dueToday.Id, dueTomorrow.Id, noDueNew.Id, noDueOld.Id },
            service.Active.Select(i => i.Id).ToArray());
    }

    [Fact]
    public void SetDone_MovesToCompleted_AndStampsCompletion()
    {
        var service = CreateService();
        TodoItem item = service.Add("write tests")!;

        _clock.Advance(TimeSpan.FromHours(1));
        service.SetDone(item.Id, true);

        Assert.Empty(service.Active);
        TodoItem done = Assert.Single(service.Completed);
        Assert.True(done.IsDone);
        Assert.Equal(_clock.UtcNow, done.CompletedAt);
    }

    [Fact]
    public void SetDone_False_RestoresToActive_AndClearsCompletedAt()
    {
        var service = CreateService();
        TodoItem item = service.Add("undo me")!;
        service.SetDone(item.Id, true);

        service.SetDone(item.Id, false);

        TodoItem restored = Assert.Single(service.Active);
        Assert.False(restored.IsDone);
        Assert.Null(restored.CompletedAt);
        Assert.Empty(service.Completed);
    }

    [Fact]
    public void Completed_SortsMostRecentlyCompletedFirst()
    {
        var service = CreateService();
        TodoItem first = service.Add("done first")!;
        TodoItem second = service.Add("done second")!;

        service.SetDone(first.Id, true);
        _clock.Advance(TimeSpan.FromMinutes(5));
        service.SetDone(second.Id, true);

        Assert.Equal(
            new[] { second.Id, first.Id },
            service.Completed.Select(i => i.Id).ToArray());
    }

    [Fact]
    public void Delete_RemovesItem()
    {
        var service = CreateService();
        TodoItem keep = service.Add("keep")!;
        TodoItem drop = service.Add("drop")!;

        service.Delete(drop.Id);

        TodoItem remaining = Assert.Single(service.Active);
        Assert.Equal(keep.Id, remaining.Id);
    }

    [Fact]
    public void IsOverdue_And_IsDueToday_ClassifyAgainstToday()
    {
        var service = CreateService();
        DateOnly today = service.Today;
        TodoItem overdue = service.Add("overdue", today.AddDays(-1))!;
        TodoItem dueToday = service.Add("due today", today)!;
        TodoItem future = service.Add("future", today.AddDays(1))!;
        TodoItem noDue = service.Add("no due")!;

        Assert.True(service.IsOverdue(overdue));
        Assert.False(service.IsOverdue(dueToday));
        Assert.True(service.IsDueToday(dueToday));
        Assert.False(service.IsDueToday(overdue));
        Assert.False(service.IsOverdue(future));
        Assert.False(service.IsDueToday(future));
        Assert.False(service.IsOverdue(noDue));
        Assert.False(service.IsDueToday(noDue));
    }

    [Fact]
    public void DoneItem_IsNeverOverdueOrDueToday()
    {
        var service = CreateService();
        DateOnly today = service.Today;
        TodoItem item = service.Add("was overdue", today.AddDays(-3))!;

        service.SetDone(item.Id, true);

        Assert.False(service.IsOverdue(item));
        Assert.False(service.IsDueToday(item));
    }

    [Fact]
    public void Changes_PersistAcrossReload()
    {
        DateOnly due = DateOnly.FromDateTime(_clock.UtcNow.LocalDateTime).AddDays(3);
        var first = CreateService();
        TodoItem kept = first.Add("kept", due)!;
        TodoItem done = first.Add("done")!;
        TodoItem gone = first.Add("gone")!;
        first.SetDone(done.Id, true);
        first.Delete(gone.Id);

        var second = CreateService();

        TodoItem active = Assert.Single(second.Active);
        Assert.Equal(kept.Id, active.Id);
        Assert.Equal("kept", active.Title);
        Assert.Equal(due, active.DueDate);
        TodoItem completed = Assert.Single(second.Completed);
        Assert.Equal(done.Id, completed.Id);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Caffeine.Core.Tests/Caffeine.Core.Tests.csproj`
Expected: BUILD FAILURE with CS0246 — `TodoService` / `TodoList` / `TodoItem` not found.

- [ ] **Step 3: Create the model and document types**

Create `src/Caffeine.Core/Todos/TodoItem.cs`:

```csharp
namespace Caffeine.Core.Todos;

/// <summary>One to-do entry; persisted inside todos.json.</summary>
public sealed class TodoItem
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Title { get; set; } = string.Empty;

    public DateOnly? DueDate { get; set; }

    public bool IsDone { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? CompletedAt { get; set; }
}
```

Create `src/Caffeine.Core/Todos/TodoList.cs`:

```csharp
namespace Caffeine.Core.Todos;

/// <summary>Document persisted to todos.json via JsonStore&lt;TodoList&gt;.</summary>
public sealed class TodoList
{
    public List<TodoItem> Items { get; set; } = new();
}
```

- [ ] **Step 4: Create TodoService**

Create `src/Caffeine.Core/Todos/TodoService.cs`:

```csharp
using Caffeine.Core.Common;

namespace Caffeine.Core.Todos;

/// <summary>
/// Single-list to-do store (v1: no priorities, projects, or subtasks).
/// Every mutation saves immediately through the JsonStore, matching the
/// persistence rules of the other modules. "Today" is the clock's local
/// date, so due/overdue classification flips at local midnight.
/// </summary>
public sealed class TodoService
{
    private readonly IClock _clock;
    private readonly JsonStore<TodoList> _store;
    private readonly TodoList _list;

    public TodoService(IClock clock, JsonStore<TodoList> store)
    {
        _clock = clock;
        _store = store;
        _list = store.Load();
    }

    /// <summary>The clock's current local date; due/overdue comparisons use this.</summary>
    public DateOnly Today => DateOnly.FromDateTime(_clock.UtcNow.LocalDateTime);

    /// <summary>Open items: overdue first, then by due date, then newest first for items with no due date.</summary>
    public IReadOnlyList<TodoItem> Active =>
        _list.Items
            .Where(i => !i.IsDone)
            .OrderBy(i => i.DueDate ?? DateOnly.MaxValue)
            .ThenByDescending(i => i.CreatedAt)
            .ToList();

    /// <summary>Done items, most recently completed first.</summary>
    public IReadOnlyList<TodoItem> Completed =>
        _list.Items
            .Where(i => i.IsDone)
            .OrderByDescending(i => i.CompletedAt)
            .ToList();

    public bool IsOverdue(TodoItem item) =>
        !item.IsDone && item.DueDate is { } due && due < Today;

    public bool IsDueToday(TodoItem item) =>
        !item.IsDone && item.DueDate == Today;

    /// <summary>Adds a to-do; whitespace-only titles are ignored. Returns the new item, or null when ignored.</summary>
    public TodoItem? Add(string title, DateOnly? dueDate = null)
    {
        string trimmed = title.Trim();
        if (trimmed.Length == 0)
        {
            return null;
        }

        var item = new TodoItem
        {
            Title = trimmed,
            DueDate = dueDate,
            CreatedAt = _clock.UtcNow,
        };
        _list.Items.Add(item);
        _store.Save(_list);
        return item;
    }

    public void SetDone(Guid id, bool done)
    {
        TodoItem? item = _list.Items.FirstOrDefault(i => i.Id == id);
        if (item is null || item.IsDone == done)
        {
            return;
        }

        item.IsDone = done;
        item.CompletedAt = done ? _clock.UtcNow : null;
        _store.Save(_list);
    }

    public void Delete(Guid id)
    {
        if (_list.Items.RemoveAll(i => i.Id == id) > 0)
        {
            _store.Save(_list);
        }
    }
}
```

Sorting note: `OrderBy(i => i.DueDate ?? DateOnly.MaxValue)` puts dated items
in ascending due order — overdue naturally first — and pushes undated items
to the end, where `ThenByDescending(CreatedAt)` orders them newest first.
That is exactly the spec's "overdue → due date → newest".

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test tests/Caffeine.Core.Tests/Caffeine.Core.Tests.csproj`
Expected: all pass ("Aprovado!"), suite count 44 (33 after Task 1 + 11 new).

- [ ] **Step 6: Commit**

```bash
git add src/Caffeine.Core/Todos/ tests/Caffeine.Core.Tests/TodoServiceTests.cs
git commit -m "feat: Add TodoService with sorted views and immediate persistence"
```

---

### Task 3: App wiring, sidebar nav, and Todos page

Own one `TodoService` on `App` (pattern: `TimersService`), add the Todos
sidebar item between Home and Timers, and build the Todos page: add row on
top (textbox + optional due date + Add), Active/Completed `SelectorBar`,
rows rebuilt in code-behind (pattern: the stopwatch laps list — this
codebase uses code-behind, not MVVM bindings).

**Files:**
- Modify: `src/Caffeine.App/App.xaml.cs`
- Modify: `src/Caffeine.App/MainWindow.xaml` (nav items) and `src/Caffeine.App/MainWindow.xaml.cs` (page switch + doc comment)
- Create: `src/Caffeine.App/Pages/TodosPage.xaml` and `src/Caffeine.App/Pages/TodosPage.xaml.cs`
- Modify: `.claude/skills/verify/SKILL.md` (nav list line)

**Interfaces:**
- Consumes (from Task 2, verbatim): `TodoService(IClock clock, JsonStore<TodoList> store)`; `TodoService.Today` (`DateOnly`), `.Active` / `.Completed` (`IReadOnlyList<TodoItem>`), `.IsOverdue(TodoItem)`, `.IsDueToday(TodoItem)`, `.Add(string, DateOnly?)` → `TodoItem?`, `.SetDone(Guid, bool)`, `.Delete(Guid)`; `TodoItem.Id/Title/DueDate/IsDone/CompletedAt`. Also `Caffeine.Core.Common.SystemClock` and `JsonStore<T>`.
- Produces: `App.Todos` (`public TodoService Todos { get; private set; } = null!;`) — Task 4's Home card reads it; nav tag `"todos"` routed to `Pages.TodosPage`.

- [ ] **Step 1: Wire TodoService into App**

In `src/Caffeine.App/App.xaml.cs`:

Add usings (the file already has `using Caffeine.Core.Awake;` and `using Caffeine.Core.Settings;`):

```csharp
using Caffeine.Core.Common;
using Caffeine.Core.Todos;
```

Add the property after `public TimersService Timers { get; private set; } = null!;`:

```csharp
    public TodoService Todos { get; private set; } = null!;
```

In `OnLaunched`, right after `Timers = new TimersService(() => new DispatcherAppTimer());` (must run before `_window = new MainWindow();` — the window immediately constructs HomePage, which will read `app.Todos` in Task 4):

```csharp
        Todos = new TodoService(new SystemClock(), new JsonStore<TodoList>("todos.json"));
```

- [ ] **Step 2: Add the sidebar item and route**

In `src/Caffeine.App/MainWindow.xaml`, insert between the Home item (`</NavigationViewItem>` closing `HomeItem`) and the Timers item:

```xml
                <NavigationViewItem Content="Todos" Tag="todos">
                    <NavigationViewItem.Icon>
                        <FontIcon Glyph="&#xE9D5;" />
                    </NavigationViewItem.Icon>
                </NavigationViewItem>
```

(`E9D5` is the Fluent "CheckList" glyph, standard icon font like Home's `E80F`.)

In `src/Caffeine.App/MainWindow.xaml.cs`, add to the switch in `NavView_SelectionChanged` (before the `"timers"` case):

```csharp
                "todos" => typeof(Pages.TodosPage),
```

And update the `NavigateTo` doc comment to:

```csharp
    /// <summary>Selects the sidebar item with the given Tag ("home", "todos", "timers", "awake", "settings", "welcome").</summary>
```

- [ ] **Step 3: Create the Todos page XAML**

Create `src/Caffeine.App/Pages/TodosPage.xaml`:

```xml
<Page
    x:Class="Caffeine.Pages.TodosPage"
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">

    <Page.Resources>
        <!-- Row chrome matching the hub cards on Home. -->
        <Style x:Key="TodoRowStyle" TargetType="Border">
            <Setter Property="Background" Value="{ThemeResource CardBackgroundFillColorDefaultBrush}" />
            <Setter Property="BorderBrush" Value="{ThemeResource CardStrokeColorDefaultBrush}" />
            <Setter Property="BorderThickness" Value="1" />
            <Setter Property="CornerRadius" Value="{StaticResource ControlCornerRadius}" />
            <Setter Property="Padding" Value="16,8" />
        </Style>
    </Page.Resources>

    <Grid Padding="36,24,36,36" MaxWidth="800">
        <Grid.RowDefinitions>
            <RowDefinition Height="Auto" />
            <RowDefinition Height="Auto" />
            <RowDefinition Height="Auto" />
            <RowDefinition Height="Auto" />
            <RowDefinition Height="*" />
        </Grid.RowDefinitions>

        <TextBlock
            Text="Todos"
            Style="{StaticResource TitleTextBlockStyle}"
            Margin="0,0,0,4" />
        <TextBlock
            Grid.Row="1"
            Text="One list. Get it out of your head."
            Style="{StaticResource BodyTextBlockStyle}"
            Foreground="{ThemeResource TextFillColorSecondaryBrush}"
            TextWrapping="Wrap"
            Margin="0,0,0,20" />

        <Grid Grid.Row="2" ColumnSpacing="8" Margin="0,0,0,16">
            <Grid.ColumnDefinitions>
                <ColumnDefinition Width="*" />
                <ColumnDefinition Width="Auto" />
                <ColumnDefinition Width="Auto" />
            </Grid.ColumnDefinitions>
            <TextBox
                x:Name="NewTitleBox"
                PlaceholderText="Add a to-do…"
                KeyDown="NewTitleBox_KeyDown" />
            <CalendarDatePicker
                x:Name="NewDuePicker"
                Grid.Column="1"
                PlaceholderText="Due date"
                AutomationProperties.Name="Due date" />
            <Button
                Grid.Column="2"
                x:Name="AddButton"
                Content="Add"
                Style="{StaticResource AccentButtonStyle}"
                Click="Add_Click" />
        </Grid>

        <SelectorBar
            x:Name="ViewSelector"
            Grid.Row="3"
            Margin="0,0,0,12"
            SelectionChanged="ViewSelector_SelectionChanged">
            <SelectorBarItem x:Name="ActiveTab" Text="Active" />
            <SelectorBarItem x:Name="CompletedTab" Text="Completed" />
        </SelectorBar>

        <ScrollViewer Grid.Row="4">
            <StackPanel>
                <TextBlock
                    x:Name="EmptyText"
                    Style="{StaticResource BodyTextBlockStyle}"
                    Foreground="{ThemeResource TextFillColorSecondaryBrush}"
                    Margin="0,8,0,0"
                    Visibility="Collapsed" />
                <StackPanel x:Name="TodoRows" Spacing="4" />
            </StackPanel>
        </ScrollViewer>
    </Grid>
</Page>
```

- [ ] **Step 4: Create the Todos page code-behind**

Create `src/Caffeine.App/Pages/TodosPage.xaml.cs`:

```csharp
using Caffeine.Core.Todos;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.System;

namespace Caffeine.Pages;

public sealed partial class TodosPage : Page
{
    private readonly TodoService _todos;
    private bool _showCompleted;

    public TodosPage()
    {
        _todos = ((App)Application.Current).Todos;
        InitializeComponent();

        // Selecting the tab fires ViewSelector_SelectionChanged → RebuildList.
        ViewSelector.SelectedItem = ActiveTab;
    }

    private void ViewSelector_SelectionChanged(
        SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
    {
        _showCompleted = sender.SelectedItem == CompletedTab;
        RebuildList();
    }

    private void NewTitleBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter)
        {
            AddTodo();
            e.Handled = true;
        }
    }

    private void Add_Click(object sender, RoutedEventArgs e) => AddTodo();

    private void AddTodo()
    {
        DateOnly? due = NewDuePicker.Date is { } picked
            ? DateOnly.FromDateTime(picked.Date)
            : null;

        if (_todos.Add(NewTitleBox.Text, due) is null)
        {
            return; // whitespace-only title — nothing to add
        }

        NewTitleBox.Text = string.Empty;
        NewDuePicker.Date = null;

        if (_showCompleted)
        {
            ViewSelector.SelectedItem = ActiveTab; // rebuilds via SelectionChanged
        }
        else
        {
            RebuildList();
        }

        NewTitleBox.Focus(FocusState.Programmatic);
    }

    private void RebuildList()
    {
        IReadOnlyList<TodoItem> items = _showCompleted ? _todos.Completed : _todos.Active;

        TodoRows.Children.Clear();
        foreach (TodoItem item in items)
        {
            TodoRows.Children.Add(BuildRow(item));
        }

        EmptyText.Text = _showCompleted ? "Nothing completed yet." : "All caught up!";
        EmptyText.Visibility = items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private Border BuildRow(TodoItem item)
    {
        var check = new CheckBox
        {
            IsChecked = item.IsDone,
            MinWidth = 0,
            VerticalAlignment = VerticalAlignment.Center,
        };
        AutomationProperties.SetName(check, item.Title);
        check.Checked += (_, _) =>
        {
            _todos.SetDone(item.Id, true);
            RebuildList();
        };
        check.Unchecked += (_, _) =>
        {
            _todos.SetDone(item.Id, false);
            RebuildList();
        };

        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Spacing = 2 };
        var title = new TextBlock { Text = item.Title, TextWrapping = TextWrapping.Wrap };
        if (item.IsDone)
        {
            title.TextDecorations = Windows.UI.Text.TextDecorations.Strikethrough;
            title.Opacity = 0.6;
        }
        text.Children.Add(title);

        if (BuildDetail(item) is { } detail)
        {
            text.Children.Add(detail);
        }

        var delete = new Button
        {
            Content = new FontIcon { Glyph = "\uE74D", FontSize = 14 },
            Padding = new Thickness(8),
            VerticalAlignment = VerticalAlignment.Center,
        };
        AutomationProperties.SetName(delete, $"Delete {item.Title}");
        delete.Click += (_, _) =>
        {
            _todos.Delete(item.Id);
            RebuildList();
        };

        var grid = new Grid { ColumnSpacing = 12 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(text, 1);
        Grid.SetColumn(delete, 2);
        grid.Children.Add(check);
        grid.Children.Add(text);
        grid.Children.Add(delete);

        return new Border
        {
            Style = (Style)Resources["TodoRowStyle"],
            Child = grid,
        };
    }

    /// <summary>Caption under the title: due info (Active) or completion date (Completed); null when there is nothing to say.</summary>
    private TextBlock? BuildDetail(TodoItem item)
    {
        string detailText;
        bool critical = false;

        if (item.IsDone)
        {
            if (item.CompletedAt is not { } completed)
            {
                return null;
            }

            detailText = $"Completed {completed.LocalDateTime:MMM d}";
        }
        else if (item.DueDate is { } due)
        {
            if (_todos.IsOverdue(item))
            {
                detailText = $"Overdue — was due {due:MMM d}";
                critical = true;
            }
            else
            {
                detailText = due == _todos.Today ? "Due today" : $"Due {due:MMM d}";
            }
        }
        else
        {
            return null;
        }

        var detail = new TextBlock
        {
            Text = detailText,
            Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"],
        };
        detail.Foreground = critical
            ? (Brush)Application.Current.Resources["SystemFillColorCriticalBrush"]
            : (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"];
        return detail;
    }
}
```

- [ ] **Step 5: Update the verify skill's nav list**

In `.claude/skills/verify/SKILL.md`, change the line:

```
- Nav items ("Home", "Timers", "Awake", "Settings", "Welcome to Caffeine"): `SelectionItemPattern.Select()`.
```

to:

```
- Nav items ("Home", "Todos", "Timers", "Awake", "Settings", "Welcome to Caffeine"): `SelectionItemPattern.Select()`.
```

- [ ] **Step 6: Build**

Run: `Stop-Process -Name Caffeine -ErrorAction SilentlyContinue; dotnet build src/Caffeine.App/Caffeine.App.csproj -p:Platform=x64 -v:m`
Expected: Build succeeded, 0 errors.

- [ ] **Step 7: Verify at runtime (UIA per .claude/skills/verify/SKILL.md)**

Launch the built exe, then via UIA:
1. Select the "Todos" nav item — page shows title "Todos", add row, Active/Completed tabs, "All caught up!" empty text.
2. Type "buy milk" into the add textbox, invoke "Add" — a row appears with an unchecked checkbox named "buy milk" and a "Delete buy milk" button; textbox is cleared.
3. Toggle the "buy milk" checkbox — row leaves the Active list ("All caught up!" returns).
4. Select "Completed" tab — "buy milk" row appears (checked); toggle it off — it disappears from Completed.
5. Back on "Active", delete the row via "Delete buy milk" — list empty again.
6. Take a PrintWindow screenshot of the page with at least one active row for the report.
7. Close the app (kill process) and confirm `%LocalAppData%\Caffeine\todos.json` exists and contains the expected items from mid-test (any state is fine — it must be valid JSON with an `Items` array).

- [ ] **Step 8: Commit**

```bash
git add src/Caffeine.App/App.xaml.cs src/Caffeine.App/MainWindow.xaml src/Caffeine.App/MainWindow.xaml.cs src/Caffeine.App/Pages/TodosPage.xaml src/Caffeine.App/Pages/TodosPage.xaml.cs .claude/skills/verify/SKILL.md
git commit -m "feat: Add Todos page with Active/Completed views and sidebar nav"
```

---

### Task 4: Home dashboard Todos card + README bullet

Add the Todos card to the Home dashboard above the Timers card (spec order:
Habits, Todos, Timers, Awake). Whole card navigates to the Todos page
(pattern: existing Timers/Awake cards, `HubCardButtonStyle`); the caption
summarizes due-today/overdue counts. No inner control on this card — a
glanceable count is the v1 dashboard value. The caption is computed once at
page construction; HomePage is recreated on every navigation and todos only
change on the Todos page, so no live refresh is needed.

**Files:**
- Modify: `src/Caffeine.App/Pages/HomePage.xaml`
- Modify: `src/Caffeine.App/Pages/HomePage.xaml.cs`
- Modify: `README.md` (module list bullet)

**Interfaces:**
- Consumes: `App.Todos` (from Task 3); `TodoService.Active`, `.IsOverdue(TodoItem)`, `.IsDueToday(TodoItem)`; `App.NavigateTo("todos")`.
- Produces: nothing consumed by later tasks.

- [ ] **Step 1: Add the Todos card to HomePage.xaml**

In `src/Caffeine.App/Pages/HomePage.xaml`, insert directly above the
`<!-- Timers module card: ... -->` comment:

```xml
            <!-- Todos module card: whole card navigates to the Todos page. -->
            <Button
                Style="{StaticResource HubCardButtonStyle}"
                Click="OpenTodos_Click"
                AutomationProperties.Name="Open Todos"
                Margin="0,0,0,4">
                <Grid ColumnSpacing="16">
                    <Grid.ColumnDefinitions>
                        <ColumnDefinition Width="Auto" />
                        <ColumnDefinition Width="*" />
                    </Grid.ColumnDefinitions>

                    <Grid Width="48" Height="48" VerticalAlignment="Center">
                        <Border
                            CornerRadius="24"
                            Background="{ThemeResource SubtleFillColorSecondaryBrush}" />
                        <TextBlock
                            Text="&#x2705;"
                            FontSize="22"
                            HorizontalAlignment="Center"
                            VerticalAlignment="Center" />
                    </Grid>

                    <StackPanel Grid.Column="1" VerticalAlignment="Center" Spacing="2">
                        <TextBlock Text="Todos" Style="{StaticResource BodyStrongTextBlockStyle}" />
                        <TextBlock
                            x:Name="TodosCaption"
                            Text="All caught up"
                            Style="{StaticResource HubCaptionStyle}"
                            TextWrapping="Wrap" />
                    </StackPanel>
                </Grid>
            </Button>
```

- [ ] **Step 2: Wire the card in HomePage.xaml.cs**

In `src/Caffeine.App/Pages/HomePage.xaml.cs`:

Add the using:

```csharp
using Caffeine.Core.Todos;
```

Add the field after `private readonly TimersService _timers;`:

```csharp
    private readonly TodoService _todos;
```

In the constructor, after `_timers = app.Timers;`:

```csharp
        _todos = app.Todos;
```

At the end of the constructor, after `RefreshTimersCard();`:

```csharp
        RefreshTodosCard();
```

Add the methods after `OpenTimers_Click`:

```csharp
    private void RefreshTodosCard()
    {
        IReadOnlyList<TodoItem> active = _todos.Active;
        int overdue = active.Count(_todos.IsOverdue);
        int dueToday = active.Count(_todos.IsDueToday);

        TodosCaption.Text = (dueToday, overdue) switch
        {
            (0, 0) when active.Count == 0 => "All caught up",
            (0, 0) => Plural(active.Count, "open to-do"),
            (_, 0) => $"{Plural(dueToday, "to-do")} due today",
            (0, _) => $"{Plural(overdue, "to-do")} overdue",
            _ => $"{dueToday} due today · {overdue} overdue",
        };
    }

    private static string Plural(int count, string noun) =>
        count == 1 ? $"1 {noun}" : $"{count} {noun}s";

    private void OpenTodos_Click(object sender, RoutedEventArgs e) =>
        ((App)Application.Current).NavigateTo("todos");
```

- [ ] **Step 3: Add the README bullet**

In `README.md`, the module list added in Stage 2 has a Timers bullet. Add a
Todos bullet directly above the Timers bullet, matching the existing bullet
style:

```markdown
- **Todos** — a single to-do list with due dates and Active/Completed views.
```

- [ ] **Step 4: Build**

Run: `Stop-Process -Name Caffeine -ErrorAction SilentlyContinue; dotnet build src/Caffeine.App/Caffeine.App.csproj -p:Platform=x64 -v:m`
Expected: Build succeeded, 0 errors.

- [ ] **Step 5: Verify at runtime (UIA per .claude/skills/verify/SKILL.md)**

1. Launch; Home shows three cards in order: Todos, Timers, Awake.
2. With no todos: Todos caption reads "All caught up".
3. Navigate to Todos, add "task one" with no due date, add "task two" due today (pick today in the date picker); navigate back to Home — caption reads "1 to-do due today" (the undated item doesn't count toward due today).
4. Invoke the "Open Todos" card — app navigates to the Todos page with the sidebar selection on Todos.
5. Clean up: delete both test items (or delete `%LocalAppData%\Caffeine\todos.json` after killing the app).
6. PrintWindow screenshot of Home with the Todos card for the report.

- [ ] **Step 6: Commit**

```bash
git add src/Caffeine.App/Pages/HomePage.xaml src/Caffeine.App/Pages/HomePage.xaml.cs README.md
git commit -m "feat: Add Todos card to Home dashboard"
```
