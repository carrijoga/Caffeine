# Life Hub Stage 4 — Habits Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add the Habits module — `HabitService` with streak calculation in Core plus tests, a Habits page (check-off, streaks, 7-day dots, rename, confirmed delete), and a Home dashboard card with inline check-off — per the approved life-hub design spec (Stage 4).

**Architecture:** `Habit`/`HabitList`/`HabitService` live in `Caffeine.Core/Habits` with zero UI dependencies; the service takes `IClock` and a `JsonStore<HabitList>` (tests use `FakeClock` + a temp directory) and saves immediately on every mutation. Each habit carries its completion log as a set of dates; streaks are computed from the log on demand. The App layer owns one `HabitService` on `App`, a code-behind `HabitsPage` that rebuilds rows on each change, and a Home card that lists today's habits with checkboxes.

**Tech Stack:** .NET 10, WinUI 3 (Windows App SDK 1.8.250907003, unpackaged), xUnit, System.Text.Json via existing `JsonStore<T>`.

## Global Constraints

- `Caffeine.Core` references no WinUI/WindowsAppSDK packages; everything in it is unit-testable headlessly.
- Habits v1 scope (spec verbatim): "Daily habits. Habit: name, emoji icon, created date. Completion log is a set of dates. Each row: today's check-off, current streak, best streak, and a last-7-days dot strip. Add/rename/delete (delete confirms — it drops history)."
- Streak rule (spec verbatim): "consecutive days ending today or yesterday — missing today does not zero the streak until the day is over."
- Storage: new `habits.json` in `%LocalAppData%\Caffeine\` via `JsonStore<HabitList>`; saves happen immediately on every change.
- Home card (spec): "Habits: today's habits with inline check-off"; card navigates to the Habits page.
- Sidebar order after this stage: **Home, Habits, Todos, Timers, Awake**; footer Settings + Welcome unchanged.
- Home card order after this stage: **Habits, Todos, Timers, Awake** (spec dashboard order).
- Build command (from repo root; kill a running app first):
  `Stop-Process -Name Caffeine -ErrorAction SilentlyContinue; dotnet build src/Caffeine.App/Caffeine.App.csproj -p:Platform=x64 -v:m`
- Test command: `dotnet test tests/Caffeine.Core.Tests/Caffeine.Core.Tests.csproj`
  (Output is Portuguese: "Aprovado!" = passed, "Com falha" = failed.) Suite is 43 before this stage.
- UI tasks verify at runtime per `.claude/skills/verify/SKILL.md` (UIA patterns + PrintWindow screenshots; never CopyFromScreen).
- New XAML pages are auto-included by the SDK — no `.csproj` edits.

---

## File structure

```
src/Caffeine.Core/Habits/Habit.cs               (create)                  Task 1
src/Caffeine.Core/Habits/HabitList.cs           (create)                  Task 1
src/Caffeine.Core/Habits/HabitService.cs        (create)                  Task 1
tests/Caffeine.Core.Tests/HabitServiceTests.cs  (create)                  Task 1
src/Caffeine.App/App.xaml.cs                    (modify: Habits property) Task 2
src/Caffeine.App/MainWindow.xaml(.cs)           (modify: nav item)        Task 2
src/Caffeine.App/Pages/HabitsPage.xaml(.cs)     (create)                  Task 2
.claude/skills/verify/SKILL.md                  (modify: nav list)        Task 2
src/Caffeine.App/Pages/HomePage.xaml(.cs)       (modify: Habits card)     Task 3
README.md                                       (modify: Habits bullet)   Task 3
```

---

### Task 1: Habit, HabitList, HabitService with streak calculation (TDD)

The whole Habits domain: model with a set-of-dates completion log, persistence
document, and the service with add/rename/delete, per-date check-off, current
streak (ending today OR yesterday), best streak, and the last-7-days flags.
"Today" is the clock's local date; tests derive expected dates from the same
conversion, keeping them timezone-safe (pattern proven by TodoServiceTests).

**Files:**
- Create: `src/Caffeine.Core/Habits/Habit.cs`
- Create: `src/Caffeine.Core/Habits/HabitList.cs`
- Create: `src/Caffeine.Core/Habits/HabitService.cs`
- Create: `tests/Caffeine.Core.Tests/HabitServiceTests.cs`

**Interfaces:**
- Consumes: `Caffeine.Core.Common.IClock` (`DateTimeOffset UtcNow { get; }`), `Caffeine.Core.Common.JsonStore<T>` (`JsonStore(string fileName, string? directory = null)`, `T Load()`, `void Save(T document)`), test helper `FakeClock` (settable `UtcNow`, default 2026-01-01 12:00 UTC, `Advance(TimeSpan)`).
- Produces (Tasks 2 and 3 rely on these exact members):
  - `Habit`: `Guid Id`, `string Name`, `string Icon`, `DateOnly CreatedOn`, `HashSet<DateOnly> CompletedOn`, `const string DefaultIcon`.
  - `HabitList`: `List<Habit> Items`.
  - `HabitService(IClock clock, JsonStore<HabitList> store)`:
    `DateOnly Today { get; }`,
    `IReadOnlyList<Habit> Habits { get; }` (creation order),
    `Habit? Add(string name, string icon = "")`,
    `void Rename(Guid id, string newName)`, `void Delete(Guid id)`,
    `bool IsDone(Habit habit, DateOnly date)`,
    `void SetDone(Guid id, DateOnly date, bool done)`,
    `int CurrentStreak(Habit habit)`, `int BestStreak(Habit habit)`,
    `IReadOnlyList<bool> LastSevenDays(Habit habit)` (7 flags, oldest first, index 6 = today).

- [ ] **Step 1: Write the failing tests**

Create `tests/Caffeine.Core.Tests/HabitServiceTests.cs`:

```csharp
using Caffeine.Core.Common;
using Caffeine.Core.Habits;

namespace Caffeine.Core.Tests;

public class HabitServiceTests : IDisposable
{
    private readonly string _dir = Path.Combine(
        Path.GetTempPath(), "caffeine-tests-" + Guid.NewGuid().ToString("N"));

    private readonly FakeClock _clock = new();

    private HabitService CreateService() =>
        new(_clock, new JsonStore<HabitList>("habits.json", _dir));

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

    /// <summary>Marks <paramref name="count"/> consecutive days done, starting at <paramref name="start"/>.</summary>
    private static void MarkDays(HabitService service, Guid id, DateOnly start, int count)
    {
        for (int i = 0; i < count; i++)
        {
            service.SetDone(id, start.AddDays(i), true);
        }
    }

    [Fact]
    public void Add_TrimsNameAndIcon_AndStampsCreation()
    {
        var service = CreateService();

        Habit? habit = service.Add("  drink water  ", " X ");

        Assert.NotNull(habit);
        Assert.Equal("drink water", habit.Name);
        Assert.Equal("X", habit.Icon);
        Assert.Equal(service.Today, habit.CreatedOn);
        Assert.Empty(habit.CompletedOn);
    }

    [Fact]
    public void Add_EmptyName_IsIgnored()
    {
        var service = CreateService();

        Assert.Null(service.Add("   "));
        Assert.Null(service.Add(string.Empty));
        Assert.Empty(service.Habits);
    }

    [Fact]
    public void Add_BlankIcon_GetsDefault()
    {
        var service = CreateService();

        Habit habit = service.Add("stretch")!;

        Assert.Equal(Habit.DefaultIcon, habit.Icon);
    }

    [Fact]
    public void Rename_TrimsNewName_AndIgnoresEmpty()
    {
        var service = CreateService();
        Habit habit = service.Add("jog")!;

        service.Rename(habit.Id, "  morning jog  ");
        Assert.Equal("morning jog", habit.Name);

        service.Rename(habit.Id, "   ");
        Assert.Equal("morning jog", habit.Name);
    }

    [Fact]
    public void Delete_RemovesHabit()
    {
        var service = CreateService();
        Habit keep = service.Add("keep")!;
        Habit drop = service.Add("drop")!;

        service.Delete(drop.Id);

        Habit remaining = Assert.Single(service.Habits);
        Assert.Equal(keep.Id, remaining.Id);
    }

    [Fact]
    public void SetDone_MarksAndUnmarksADate()
    {
        var service = CreateService();
        Habit habit = service.Add("stretch")!;

        service.SetDone(habit.Id, service.Today, true);
        Assert.True(service.IsDone(habit, service.Today));

        service.SetDone(habit.Id, service.Today, false);
        Assert.False(service.IsDone(habit, service.Today));
    }

    [Fact]
    public void CurrentStreak_ZeroWhenNeverDone()
    {
        var service = CreateService();
        Habit habit = service.Add("meditate")!;

        Assert.Equal(0, service.CurrentStreak(habit));
    }

    [Fact]
    public void CurrentStreak_OneWhenDoneTodayOnly()
    {
        var service = CreateService();
        Habit habit = service.Add("meditate")!;

        service.SetDone(habit.Id, service.Today, true);

        Assert.Equal(1, service.CurrentStreak(habit));
    }

    [Fact]
    public void CurrentStreak_CountsConsecutiveRunEndingToday()
    {
        var service = CreateService();
        Habit habit = service.Add("read")!;
        MarkDays(service, habit.Id, service.Today.AddDays(-2), 3);

        Assert.Equal(3, service.CurrentStreak(habit));
    }

    [Fact]
    public void CurrentStreak_RunEndingYesterday_IsPreserved()
    {
        var service = CreateService();
        Habit habit = service.Add("run")!;
        MarkDays(service, habit.Id, service.Today.AddDays(-3), 3);

        Assert.Equal(3, service.CurrentStreak(habit));
    }

    [Fact]
    public void CurrentStreak_ZeroWhenLastDoneTwoDaysAgo()
    {
        var service = CreateService();
        Habit habit = service.Add("run")!;
        MarkDays(service, habit.Id, service.Today.AddDays(-5), 4);

        Assert.Equal(0, service.CurrentStreak(habit));
    }

    [Fact]
    public void CurrentStreak_AcrossDayBoundary()
    {
        var service = CreateService();
        Habit habit = service.Add("meditate")!;
        service.SetDone(habit.Id, service.Today, true);
        Assert.Equal(1, service.CurrentStreak(habit));

        _clock.Advance(TimeSpan.FromDays(1));
        Assert.Equal(1, service.CurrentStreak(habit));

        _clock.Advance(TimeSpan.FromDays(1));
        Assert.Equal(0, service.CurrentStreak(habit));
    }

    [Fact]
    public void BestStreak_ZeroWhenNeverDone()
    {
        var service = CreateService();
        Habit habit = service.Add("write")!;

        Assert.Equal(0, service.BestStreak(habit));
    }

    [Fact]
    public void BestStreak_FindsLongestHistoricalRun()
    {
        var service = CreateService();
        Habit habit = service.Add("write")!;
        MarkDays(service, habit.Id, service.Today.AddDays(-20), 5);
        MarkDays(service, habit.Id, service.Today.AddDays(-1), 2);

        Assert.Equal(5, service.BestStreak(habit));
        Assert.Equal(2, service.CurrentStreak(habit));
    }

    [Fact]
    public void LastSevenDays_FlagsCompletedPositions()
    {
        var service = CreateService();
        Habit habit = service.Add("walk")!;
        service.SetDone(habit.Id, service.Today, true);
        service.SetDone(habit.Id, service.Today.AddDays(-3), true);
        service.SetDone(habit.Id, service.Today.AddDays(-8), true);

        IReadOnlyList<bool> days = service.LastSevenDays(habit);

        Assert.Equal(7, days.Count);
        Assert.True(days[6]);
        Assert.True(days[3]);
        Assert.False(days[0]);
        Assert.False(days[1]);
        Assert.False(days[2]);
        Assert.False(days[4]);
        Assert.False(days[5]);
    }

    [Fact]
    public void Changes_PersistAcrossReload()
    {
        var first = CreateService();
        Habit habit = first.Add("read", "B")!;
        first.SetDone(habit.Id, first.Today, true);
        first.SetDone(habit.Id, first.Today.AddDays(-1), true);

        var second = CreateService();

        Habit reloaded = Assert.Single(second.Habits);
        Assert.Equal(habit.Id, reloaded.Id);
        Assert.Equal("read", reloaded.Name);
        Assert.Equal("B", reloaded.Icon);
        Assert.Equal(2, second.CurrentStreak(reloaded));
    }
}
```

Note: the tests use plain ASCII placeholder icons ("X", "B") rather than
emoji so the assertions never depend on source-file encoding; the default
icon is asserted via `Habit.DefaultIcon`, not a literal.

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Caffeine.Core.Tests/Caffeine.Core.Tests.csproj`
Expected: BUILD FAILURE with CS0246 — `HabitService` / `HabitList` / `Habit` not found.

- [ ] **Step 3: Create the model and document types**

Create `src/Caffeine.Core/Habits/Habit.cs`:

```csharp
namespace Caffeine.Core.Habits;

/// <summary>One daily habit; persisted inside habits.json.</summary>
public sealed class Habit
{
    public const string DefaultIcon = "⭐";

    public Guid Id { get; set; } = Guid.NewGuid();

    public string Name { get; set; } = string.Empty;

    /// <summary>Emoji shown next to the name.</summary>
    public string Icon { get; set; } = DefaultIcon;

    public DateOnly CreatedOn { get; set; }

    /// <summary>The completion log: every day this habit was checked off.</summary>
    public HashSet<DateOnly> CompletedOn { get; set; } = new();
}
```

Create `src/Caffeine.Core/Habits/HabitList.cs`:

```csharp
namespace Caffeine.Core.Habits;

/// <summary>Document persisted to habits.json via JsonStore&lt;HabitList&gt;.</summary>
public sealed class HabitList
{
    public List<Habit> Items { get; set; } = new();
}
```

- [ ] **Step 4: Create HabitService**

Create `src/Caffeine.Core/Habits/HabitService.cs`:

```csharp
using Caffeine.Core.Common;

namespace Caffeine.Core.Habits;

/// <summary>
/// Daily-habit store with streak calculation. Every mutation saves
/// immediately through the JsonStore, matching the persistence rules of the
/// other modules. "Today" is the clock's local date. Streak rule (spec):
/// consecutive days ending today or yesterday — missing today does not zero
/// the streak until the day is over.
/// </summary>
public sealed class HabitService
{
    private readonly IClock _clock;
    private readonly JsonStore<HabitList> _store;
    private readonly HabitList _list;

    public HabitService(IClock clock, JsonStore<HabitList> store)
    {
        _clock = clock;
        _store = store;
        _list = store.Load();
    }

    /// <summary>The clock's current local date; all day-based logic uses this.</summary>
    public DateOnly Today => DateOnly.FromDateTime(_clock.UtcNow.LocalDateTime);

    /// <summary>Habits in creation order.</summary>
    public IReadOnlyList<Habit> Habits => _list.Items;

    /// <summary>Adds a habit; whitespace-only names are ignored. A blank icon gets the default.</summary>
    public Habit? Add(string name, string icon = "")
    {
        string trimmedName = name.Trim();
        if (trimmedName.Length == 0)
        {
            return null;
        }

        string trimmedIcon = icon.Trim();
        var habit = new Habit
        {
            Name = trimmedName,
            Icon = trimmedIcon.Length == 0 ? Habit.DefaultIcon : trimmedIcon,
            CreatedOn = Today,
        };
        _list.Items.Add(habit);
        _store.Save(_list);
        return habit;
    }

    /// <summary>Renames a habit; whitespace-only names are ignored.</summary>
    public void Rename(Guid id, string newName)
    {
        string trimmed = newName.Trim();
        Habit? habit = _list.Items.FirstOrDefault(h => h.Id == id);
        if (habit is null || trimmed.Length == 0 || habit.Name == trimmed)
        {
            return;
        }

        habit.Name = trimmed;
        _store.Save(_list);
    }

    /// <summary>Deletes the habit and its whole completion history.</summary>
    public void Delete(Guid id)
    {
        if (_list.Items.RemoveAll(h => h.Id == id) > 0)
        {
            _store.Save(_list);
        }
    }

    public bool IsDone(Habit habit, DateOnly date) => habit.CompletedOn.Contains(date);

    public void SetDone(Guid id, DateOnly date, bool done)
    {
        Habit? habit = _list.Items.FirstOrDefault(h => h.Id == id);
        if (habit is null)
        {
            return;
        }

        bool changed = done ? habit.CompletedOn.Add(date) : habit.CompletedOn.Remove(date);
        if (changed)
        {
            _store.Save(_list);
        }
    }

    /// <summary>Consecutive days ending today or yesterday; 0 when neither day is completed.</summary>
    public int CurrentStreak(Habit habit)
    {
        DateOnly day = habit.CompletedOn.Contains(Today) ? Today : Today.AddDays(-1);
        int streak = 0;
        while (habit.CompletedOn.Contains(day))
        {
            streak++;
            day = day.AddDays(-1);
        }

        return streak;
    }

    /// <summary>Longest consecutive run anywhere in the completion log.</summary>
    public int BestStreak(Habit habit)
    {
        int best = 0;
        int run = 0;
        DateOnly previous = default;
        foreach (DateOnly date in habit.CompletedOn.Order())
        {
            run = run > 0 && date == previous.AddDays(1) ? run + 1 : 1;
            best = Math.Max(best, run);
            previous = date;
        }

        return best;
    }

    /// <summary>Completion flags for the last 7 days, oldest first; index 6 is today.</summary>
    public IReadOnlyList<bool> LastSevenDays(Habit habit)
    {
        var days = new bool[7];
        for (int i = 0; i < 7; i++)
        {
            days[i] = habit.CompletedOn.Contains(Today.AddDays(i - 6));
        }

        return days;
    }
}
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test tests/Caffeine.Core.Tests/Caffeine.Core.Tests.csproj`
Expected: all pass ("Aprovado!"), suite count 59 (43 existing + 16 new — the test file above has exactly 16 `[Fact]` methods).

- [ ] **Step 6: Commit**

```bash
git add src/Caffeine.Core/Habits/ tests/Caffeine.Core.Tests/HabitServiceTests.cs
git commit -m "feat: Add HabitService with streak calculation and persistence"
```

---

### Task 2: App wiring, sidebar nav, and Habits page

Own one `HabitService` on `App` (pattern: `TimersService`/`TodoService`
properties there), add the Habits sidebar item between Home and Todos, and
build the Habits page: add row on top (name + optional emoji + Add), then one
card-chrome row per habit with today's check-off, "Streak N · Best M"
caption, a 7-dot last-7-days strip, a rename flyout, and a delete button that
confirms via ContentDialog before dropping history (spec requirement). Rows
are rebuilt in code-behind (pattern: TodosPage.RebuildList — this codebase
uses code-behind, not MVVM bindings).

**Files:**
- Modify: `src/Caffeine.App/App.xaml.cs`
- Modify: `src/Caffeine.App/MainWindow.xaml` (nav items) and `src/Caffeine.App/MainWindow.xaml.cs` (page switch + doc comment)
- Create: `src/Caffeine.App/Pages/HabitsPage.xaml` and `src/Caffeine.App/Pages/HabitsPage.xaml.cs`
- Modify: `.claude/skills/verify/SKILL.md` (nav list line)

**Interfaces:**
- Consumes (from Task 1, verbatim): `HabitService(IClock clock, JsonStore<HabitList> store)`; `.Today` (`DateOnly`), `.Habits` (`IReadOnlyList<Habit>`), `.Add(string, string = "")` → `Habit?`, `.Rename(Guid, string)`, `.Delete(Guid)`, `.IsDone(Habit, DateOnly)`, `.SetDone(Guid, DateOnly, bool)`, `.CurrentStreak(Habit)`, `.BestStreak(Habit)`, `.LastSevenDays(Habit)` → `IReadOnlyList<bool>` (index 6 = today); `Habit.Id/Name/Icon`. Also `Caffeine.Core.Common.SystemClock` and `JsonStore<T>`.
- Produces: `App.Habits` (`public HabitService Habits { get; private set; } = null!;`) — Task 3's Home card reads it; nav tag `"habits"` routed to `Pages.HabitsPage`.

- [ ] **Step 1: Wire HabitService into App**

In `src/Caffeine.App/App.xaml.cs`:

Add the using (the file already has `using Caffeine.Core.Common;` and `using Caffeine.Core.Todos;` from Stage 3):

```csharp
using Caffeine.Core.Habits;
```

Add the property after `public TodoService Todos { get; private set; } = null!;`:

```csharp
    public HabitService Habits { get; private set; } = null!;
```

In `OnLaunched`, right after `Todos = new TodoService(new SystemClock(), new JsonStore<TodoList>("todos.json"));` (must run before `_window = new MainWindow();` — the window immediately constructs HomePage, which will read `app.Habits` in Task 3):

```csharp
        Habits = new HabitService(new SystemClock(), new JsonStore<HabitList>("habits.json"));
```

- [ ] **Step 2: Add the sidebar item and route**

In `src/Caffeine.App/MainWindow.xaml`, insert between the Home item (the `</NavigationViewItem>` closing `HomeItem`) and the Todos item:

```xml
                <NavigationViewItem Content="Habits" Tag="habits">
                    <NavigationViewItem.Icon>
                        <FontIcon Glyph="&#xE787;" />
                    </NavigationViewItem.Icon>
                </NavigationViewItem>
```

(`E787` is the Fluent "Calendar" glyph — standard icon font like Home's `E80F` — fitting for daily habits.)

In `src/Caffeine.App/MainWindow.xaml.cs`, add to the switch in `NavView_SelectionChanged` (before the `"todos"` case):

```csharp
                "habits" => typeof(Pages.HabitsPage),
```

And update the `NavigateTo` doc comment to:

```csharp
    /// <summary>Selects the sidebar item with the given Tag ("home", "habits", "todos", "timers", "awake", "settings", "welcome").</summary>
```

- [ ] **Step 3: Create the Habits page XAML**

Create `src/Caffeine.App/Pages/HabitsPage.xaml`:

```xml
<Page
    x:Class="Caffeine.Pages.HabitsPage"
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">

    <Page.Resources>
        <!-- Row chrome matching the hub cards on Home (same values as TodosPage's TodoRowStyle). -->
        <Style x:Key="HabitRowStyle" TargetType="Border">
            <Setter Property="Background" Value="{ThemeResource CardBackgroundFillColorDefaultBrush}" />
            <Setter Property="BorderBrush" Value="{ThemeResource CardStrokeColorDefaultBrush}" />
            <Setter Property="BorderThickness" Value="1" />
            <Setter Property="CornerRadius" Value="{StaticResource ControlCornerRadius}" />
            <Setter Property="Padding" Value="16,10" />
        </Style>
    </Page.Resources>

    <Grid Padding="36,24,36,36" MaxWidth="800">
        <Grid.RowDefinitions>
            <RowDefinition Height="Auto" />
            <RowDefinition Height="Auto" />
            <RowDefinition Height="Auto" />
            <RowDefinition Height="*" />
        </Grid.RowDefinitions>

        <TextBlock
            Text="Habits"
            Style="{StaticResource TitleTextBlockStyle}"
            Margin="0,0,0,4" />
        <TextBlock
            Grid.Row="1"
            Text="Small things, done daily."
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
                x:Name="NewNameBox"
                PlaceholderText="Add a habit…"
                KeyDown="NewNameBox_KeyDown" />
            <TextBox
                x:Name="NewIconBox"
                Grid.Column="1"
                Width="64"
                MaxLength="8"
                PlaceholderText="&#x2B50;"
                AutomationProperties.Name="Icon" />
            <Button
                Grid.Column="2"
                Content="Add"
                Style="{StaticResource AccentButtonStyle}"
                Click="Add_Click" />
        </Grid>

        <ScrollViewer Grid.Row="3">
            <StackPanel>
                <TextBlock
                    x:Name="EmptyText"
                    Text="No habits yet — add your first above."
                    Style="{StaticResource BodyTextBlockStyle}"
                    Foreground="{ThemeResource TextFillColorSecondaryBrush}"
                    Margin="0,8,0,0"
                    Visibility="Collapsed" />
                <StackPanel x:Name="HabitRows" Spacing="4" />
            </StackPanel>
        </ScrollViewer>
    </Grid>
</Page>
```

- [ ] **Step 4: Create the Habits page code-behind**

Create `src/Caffeine.App/Pages/HabitsPage.xaml.cs`:

```csharp
using Caffeine.Core.Habits;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.System;

namespace Caffeine.Pages;

public sealed partial class HabitsPage : Page
{
    private readonly HabitService _habits;

    public HabitsPage()
    {
        _habits = ((App)Application.Current).Habits;
        InitializeComponent();
        RebuildList();
    }

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

    private void RebuildList()
    {
        HabitRows.Children.Clear();
        foreach (Habit habit in _habits.Habits)
        {
            HabitRows.Children.Add(BuildRow(habit));
        }

        EmptyText.Visibility = _habits.Habits.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private Border BuildRow(Habit habit)
    {
        var check = new CheckBox
        {
            IsChecked = _habits.IsDone(habit, _habits.Today),
            MinWidth = 0,
            VerticalAlignment = VerticalAlignment.Center,
        };
        AutomationProperties.SetName(check, habit.Name);
        check.Checked += (_, _) =>
        {
            _habits.SetDone(habit.Id, _habits.Today, true);
            RebuildList();
        };
        check.Unchecked += (_, _) =>
        {
            _habits.SetDone(habit.Id, _habits.Today, false);
            RebuildList();
        };

        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Spacing = 2 };
        text.Children.Add(new TextBlock
        {
            Text = $"{habit.Icon} {habit.Name}",
            TextWrapping = TextWrapping.Wrap,
        });
        text.Children.Add(new TextBlock
        {
            Text = $"Streak {_habits.CurrentStreak(habit)} · Best {_habits.BestStreak(habit)}",
            Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"],
            Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
        });

        var rename = new Button
        {
            Content = new FontIcon { Glyph = "\uE70F", FontSize = 14 },
            Padding = new Thickness(8),
            VerticalAlignment = VerticalAlignment.Center,
            Flyout = BuildRenameFlyout(habit),
        };
        AutomationProperties.SetName(rename, $"Rename {habit.Name}");

        var delete = new Button
        {
            Content = new FontIcon { Glyph = "\uE74D", FontSize = 14 },
            Padding = new Thickness(8),
            VerticalAlignment = VerticalAlignment.Center,
        };
        AutomationProperties.SetName(delete, $"Delete {habit.Name}");
        delete.Click += async (_, _) => await ConfirmDeleteAsync(habit);

        var grid = new Grid { ColumnSpacing = 12 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var dots = BuildDotStrip(habit);
        Grid.SetColumn(text, 1);
        Grid.SetColumn(dots, 2);
        Grid.SetColumn(rename, 3);
        Grid.SetColumn(delete, 4);
        grid.Children.Add(check);
        grid.Children.Add(text);
        grid.Children.Add(dots);
        grid.Children.Add(rename);
        grid.Children.Add(delete);

        return new Border
        {
            Style = (Style)Resources["HabitRowStyle"],
            Child = grid,
        };
    }

    /// <summary>Seven 8px dots, oldest day first; today is the rightmost.</summary>
    private StackPanel BuildDotStrip(Habit habit)
    {
        var strip = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 4,
            VerticalAlignment = VerticalAlignment.Center,
        };
        foreach (bool done in _habits.LastSevenDays(habit))
        {
            strip.Children.Add(new Ellipse
            {
                Width = 8,
                Height = 8,
                Fill = (Brush)Application.Current.Resources[
                    done ? "AccentFillColorDefaultBrush" : "SubtleFillColorSecondaryBrush"],
            });
        }

        return strip;
    }

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
        save.Click += (_, _) =>
        {
            _habits.Rename(habit.Id, box.Text);
            flyout.Hide();
            RebuildList();
        };
        return flyout;
    }

    /// <summary>Delete confirms first (spec) — it drops the habit's whole history.</summary>
    private async Task ConfirmDeleteAsync(Habit habit)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = $"Delete \"{habit.Name}\"?",
            Content = "This removes the habit and its whole completion history.",
            PrimaryButtonText = "Delete",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
        };

        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            _habits.Delete(habit.Id);
            RebuildList();
        }
    }
}
```

- [ ] **Step 5: Update the verify skill's nav list**

In `.claude/skills/verify/SKILL.md`, change the line:

```
- Nav items ("Home", "Todos", "Timers", "Awake", "Settings", "Welcome to Caffeine"): `SelectionItemPattern.Select()`.
```

to:

```
- Nav items ("Home", "Habits", "Todos", "Timers", "Awake", "Settings", "Welcome to Caffeine"): `SelectionItemPattern.Select()`.
```

- [ ] **Step 6: Build**

Run: `Stop-Process -Name Caffeine -ErrorAction SilentlyContinue; dotnet build src/Caffeine.App/Caffeine.App.csproj -p:Platform=x64 -v:m`
Expected: Build succeeded, 0 errors.

- [ ] **Step 7: Verify at runtime (UIA per .claude/skills/verify/SKILL.md)**

Launch the built exe, then via UIA:
1. Select the "Habits" nav item — page shows title "Habits", add row, and the empty text.
2. Type "Drink water" into the name textbox (leave the icon box empty), invoke "Add" — a row appears containing an unchecked checkbox named "Drink water", the caption "Streak 0 · Best 0", and "Rename Drink water" / "Delete Drink water" buttons; the name box is cleared.
3. Toggle the "Drink water" checkbox on — caption becomes "Streak 1 · Best 1".
4. Toggle it off — caption returns to "Streak 0 · Best 0"; toggle it back on for the screenshot.
5. Invoke "Rename Drink water", set the flyout's "New name" textbox to "Morning water" (ValuePattern), invoke "Save" — the row text updates to include "Morning water".
6. Invoke "Delete Morning water" — a dialog appears; invoke "Cancel" — the row is still there. Invoke "Delete Morning water" again, then invoke "Delete" in the dialog — the row disappears and the empty text returns.
7. Take a PrintWindow screenshot earlier in the flow (step 4-5, with one row visible including the dot strip) for the report.
8. Kill the app; confirm `%LocalAppData%\Caffeine\habits.json` exists and is valid JSON with an `Items` array; delete `habits.json` to leave a clean state.

- [ ] **Step 8: Commit**

```bash
git add src/Caffeine.App/App.xaml.cs src/Caffeine.App/MainWindow.xaml src/Caffeine.App/MainWindow.xaml.cs src/Caffeine.App/Pages/HabitsPage.xaml src/Caffeine.App/Pages/HabitsPage.xaml.cs .claude/skills/verify/SKILL.md
git commit -m "feat: Add Habits page with streaks, rename and confirmed delete"
```

---

### Task 3: Home dashboard Habits card + README bullet

Add the Habits card to the Home dashboard above the Todos card (spec order:
Habits, Todos, Timers, Awake). Whole card navigates to the Habits page
(pattern: existing cards, `HubCardButtonStyle`); the card body lists every
habit with an inline check-off checkbox (spec: "today's habits with inline
check-off") — checkboxes handle `Tapped` so clicking them doesn't trigger
card navigation (pattern: the Awake card's ToggleSwitch and the Timers
card's button). The caption reads "N of M done today" and refreshes after
each check-off; rows are built once at page construction (HomePage is
recreated on every navigation).

**Files:**
- Modify: `src/Caffeine.App/Pages/HomePage.xaml`
- Modify: `src/Caffeine.App/Pages/HomePage.xaml.cs`
- Modify: `README.md` (module list bullet)

**Interfaces:**
- Consumes: `App.Habits` (from Task 2); `HabitService.Habits`, `.Today`, `.IsDone(Habit, DateOnly)`, `.SetDone(Guid, DateOnly, bool)`; `Habit.Id/Name/Icon`; `App.NavigateTo("habits")`.
- Produces: nothing consumed by later tasks.

- [ ] **Step 1: Add the Habits card to HomePage.xaml**

In `src/Caffeine.App/Pages/HomePage.xaml`, insert directly above the
`<!-- Todos module card: ... -->` comment:

```xml
            <!-- Habits module card: whole card navigates to the Habits page;
                 the checkboxes check off today without navigating. -->
            <Button
                Style="{StaticResource HubCardButtonStyle}"
                Click="OpenHabits_Click"
                AutomationProperties.Name="Open Habits"
                Margin="0,0,0,4">
                <StackPanel Spacing="10">
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
                                Text="&#x1F501;"
                                FontSize="22"
                                HorizontalAlignment="Center"
                                VerticalAlignment="Center" />
                        </Grid>

                        <StackPanel Grid.Column="1" VerticalAlignment="Center" Spacing="2">
                            <TextBlock Text="Habits" Style="{StaticResource BodyStrongTextBlockStyle}" />
                            <TextBlock
                                x:Name="HabitsCaption"
                                Text="No habits yet"
                                Style="{StaticResource HubCaptionStyle}"
                                TextWrapping="Wrap" />
                        </StackPanel>
                    </Grid>

                    <StackPanel x:Name="HomeHabitRows" Spacing="2" />
                </StackPanel>
            </Button>
```

Also update the trailing comment at the bottom of the card list from
`<!-- Stage 4 adds the Habits card above the Todos card. -->` to
`<!-- All four module cards are in place: Habits, Todos, Timers, Awake. -->`.

- [ ] **Step 2: Wire the card in HomePage.xaml.cs**

In `src/Caffeine.App/Pages/HomePage.xaml.cs`:

Add the using:

```csharp
using Caffeine.Core.Habits;
```

Add the field after `private readonly TodoService _todos;`:

```csharp
    private readonly HabitService _habits;
```

In the constructor, after `_todos = app.Todos;`:

```csharp
        _habits = app.Habits;
```

At the end of the constructor, after `RefreshTodosCard();`:

```csharp
        BuildHabitsCard();
```

Add the methods after `OpenTodos_Click`:

```csharp
    /// <summary>Builds the card's habit checkboxes once; the caption refreshes on every check-off.</summary>
    private void BuildHabitsCard()
    {
        HomeHabitRows.Children.Clear();
        foreach (Habit habit in _habits.Habits)
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

    private void RefreshHabitsCaption()
    {
        int total = _habits.Habits.Count;
        HabitsCaption.Text = total == 0
            ? "No habits yet"
            : $"{_habits.Habits.Count(h => _habits.IsDone(h, _habits.Today))} of {total} done today";
    }

    private void OpenHabits_Click(object sender, RoutedEventArgs e) =>
        ((App)Application.Current).NavigateTo("habits");
```

And add the using needed by `AutomationProperties`:

```csharp
using Microsoft.UI.Xaml.Automation;
```

- [ ] **Step 3: Add the README bullet**

In `README.md`, the module list has Todos and Timers bullets from earlier
stages. Add a Habits bullet directly above the Todos bullet, matching the
existing bullet style:

```markdown
- **Habits** — daily habits with current/best streaks and a 7-day history.
```

- [ ] **Step 4: Build**

Run: `Stop-Process -Name Caffeine -ErrorAction SilentlyContinue; dotnet build src/Caffeine.App/Caffeine.App.csproj -p:Platform=x64 -v:m`
Expected: Build succeeded, 0 errors.

- [ ] **Step 5: Verify at runtime (UIA per .claude/skills/verify/SKILL.md)**

1. Launch; Home shows four cards in order: Habits, Todos, Timers, Awake.
2. With no habits: Habits caption reads "No habits yet" and the card has no checkbox rows.
3. Navigate to Habits, add "Read" (default icon); navigate back to Home — caption reads "0 of 1 done today" and a checkbox named "Read" is on the card.
4. Toggle the "Read" checkbox on the Home card — caption becomes "1 of 1 done today" and the app stays on Home (no navigation happened).
5. Invoke the "Open Habits" card — app navigates to the Habits page; the "Read" row's checkbox is checked and its caption shows "Streak 1 · Best 1".
6. Take a PrintWindow screenshot of Home with the Habits card populated for the report.
7. Clean up: kill the app, delete `%LocalAppData%\Caffeine\habits.json`.

- [ ] **Step 6: Commit**

```bash
git add src/Caffeine.App/Pages/HomePage.xaml src/Caffeine.App/Pages/HomePage.xaml.cs README.md
git commit -m "feat: Add Habits card to Home dashboard"
```
