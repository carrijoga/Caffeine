# TODO Priority Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a fixed five-level priority to every to-do, order the Active list by it, and move to-do creation from the inline row into a dialog.

**Architecture:** A `TodoPriority` enum lands in `Caffeine.Core` and becomes a property on `TodoItem` with a `Normal` default, so existing `todos.json` files load unchanged. `TodoService` gains a `SetPriority` mutator and re-sorts `Active` as overdue → priority → due date → newest. The app layer gets a `TodoPriorityInfo` presentation lookup (emoji + Portuguese label), a `ContentDialog` that replaces the inline add row, and a per-row emoji button with a flyout for changing priority.

**Tech Stack:** C# / .NET, WinUI 3 (Windows App SDK), xUnit, `System.Text.Json` via the project's `JsonStore<T>`.

Design spec: `docs/superpowers/specs/2026-08-04-todo-priority-design.md`

## Global Constraints

- Target project files: `src/Caffeine.Core/Todos/`, `src/Caffeine.App/Pages/`, `tests/Caffeine.Core.Tests/`.
- Run tests with: `dotnet test tests/Caffeine.Core.Tests/Caffeine.Core.Tests.csproj`
- Build the app with: `dotnet build src/Caffeine.App/Caffeine.App.csproj -p:Platform=x64 -p:RuntimeIdentifier=win-x64`
- Enum member names, type names, and all code identifiers are **English**. Only user-visible priority labels are **Portuguese** (Baixa, Normal, Média, Alta, Urgente).
- `TodoPriority` numeric values are serialized into `todos.json` — never renumber them.
- No new NuGet dependencies.
- Every UI control that shows only an emoji must also set `AutomationProperties.SetName`.
- Do **not** modify categories (`TodoCategory`, `TodoItem.CategoryId`, `TodoService.Categories`). They are out of scope.
- The UI has no MVVM/bindings; rows are built imperatively in code-behind. Follow that existing pattern.
- `docs/superpowers` is in `.gitignore` but its existing files are tracked. If a commit needs to include a doc under it, use `git add -f`.

---

### Task 1: `TodoPriority` enum and `TodoItem.Priority`

**Files:**
- Create: `src/Caffeine.Core/Todos/TodoPriority.cs`
- Modify: `src/Caffeine.Core/Todos/TodoItem.cs`
- Test: `tests/Caffeine.Core.Tests/TodoServiceTests.cs`

**Interfaces:**
- Consumes: nothing (first task).
- Produces: `enum TodoPriority { Low = 0, Normal = 1, Medium = 2, High = 3, Urgent = 4 }` in namespace `Caffeine.Core.Todos`; `TodoItem.Priority` property of type `TodoPriority` defaulting to `TodoPriority.Normal`.

- [ ] **Step 1: Write the failing tests**

Add both tests to `tests/Caffeine.Core.Tests/TodoServiceTests.cs`, before the final closing brace. The second test proves a pre-existing `todos.json` (written before this feature) still loads, defaulting to `Normal`.

```csharp
    [Fact]
    public void Add_DefaultsToNormalPriority()
    {
        var service = CreateService();

        TodoItem item = service.Add("plain item")!;

        Assert.Equal(TodoPriority.Normal, item.Priority);
    }

    [Fact]
    public void Load_LegacyJsonWithoutPriority_DefaultsToNormal()
    {
        string dir = Path.Combine(Path.GetTempPath(), "caffeine-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        string legacyJson = """
            {
              "Items": [
                { "Id": "22222222-2222-2222-2222-222222222222", "Title": "old item", "DueDate": null, "IsDone": false, "CreatedAt": "2026-01-01T00:00:00+00:00", "CompletedAt": null }
              ]
            }
            """;
        File.WriteAllText(Path.Combine(dir, "todos.json"), legacyJson);

        var service = new TodoService(_clock, new JsonStore<TodoList>("todos.json", dir));

        TodoItem loaded = Assert.Single(service.Active);
        Assert.Equal(TodoPriority.Normal, loaded.Priority);

        Directory.Delete(dir, recursive: true);
    }
```

- [ ] **Step 2: Run tests to verify they fail**

```bash
dotnet test tests/Caffeine.Core.Tests/Caffeine.Core.Tests.csproj --filter "FullyQualifiedName~TodoServiceTests"
```

Expected: compile error — `TodoPriority` does not exist / `TodoItem` has no `Priority`.

- [ ] **Step 3: Create the enum**

Create `src/Caffeine.Core/Todos/TodoPriority.cs`:

```csharp
namespace Caffeine.Core.Todos;

/// <summary>
/// Fixed five-level importance scale for a to-do; persisted inside todos.json.
/// Values are explicit because they are serialized — never renumber them.
/// Ascending order means "most urgent first" is OrderByDescending.
/// </summary>
public enum TodoPriority
{
    Low = 0,
    Normal = 1,
    Medium = 2,
    High = 3,
    Urgent = 4,
}
```

- [ ] **Step 4: Add the property**

In `src/Caffeine.Core/Todos/TodoItem.cs`, add after the `CategoryId` property:

```csharp
    /// <summary>Defaults to Normal, so to-dos saved before this field existed load as Normal.</summary>
    public TodoPriority Priority { get; set; } = TodoPriority.Normal;
```

- [ ] **Step 5: Run tests to verify they pass**

```bash
dotnet test tests/Caffeine.Core.Tests/Caffeine.Core.Tests.csproj --filter "FullyQualifiedName~TodoServiceTests"
```

Expected: PASS, including every pre-existing test.

- [ ] **Step 6: Commit**

```bash
git add src/Caffeine.Core/Todos/TodoPriority.cs src/Caffeine.Core/Todos/TodoItem.cs tests/Caffeine.Core.Tests/TodoServiceTests.cs
git commit -m "feat(todos): add TodoPriority enum and TodoItem.Priority"
```

---

### Task 2: `SetPriority` and priority on `Add`

**Files:**
- Modify: `src/Caffeine.Core/Todos/TodoService.cs`
- Test: `tests/Caffeine.Core.Tests/TodoServiceTests.cs`

**Interfaces:**
- Consumes: `TodoPriority`, `TodoItem.Priority` (Task 1).
- Produces: `TodoService.Add(string title, DateOnly? dueDate = null, TodoPriority priority = TodoPriority.Normal)` returning `TodoItem?`; `TodoService.SetPriority(Guid id, TodoPriority priority)` returning `void`.

- [ ] **Step 1: Write the failing tests**

Add to `tests/Caffeine.Core.Tests/TodoServiceTests.cs`:

```csharp
    [Fact]
    public void Add_HonorsExplicitPriority()
    {
        var service = CreateService();

        TodoItem item = service.Add("urgent item", null, TodoPriority.Urgent)!;

        Assert.Equal(TodoPriority.Urgent, item.Priority);
    }

    [Fact]
    public void SetPriority_ChangesValue_AndPersists()
    {
        var first = CreateService();
        TodoItem item = first.Add("bump me")!;

        first.SetPriority(item.Id, TodoPriority.High);

        var second = CreateService();
        Assert.Equal(TodoPriority.High, Assert.Single(second.Active).Priority);
    }

    [Fact]
    public void SetPriority_UnknownId_DoesNotThrow()
    {
        var service = CreateService();
        service.Add("untouched")!;

        service.SetPriority(Guid.NewGuid(), TodoPriority.Urgent);

        Assert.Equal(TodoPriority.Normal, Assert.Single(service.Active).Priority);
    }
```

- [ ] **Step 2: Run tests to verify they fail**

```bash
dotnet test tests/Caffeine.Core.Tests/Caffeine.Core.Tests.csproj --filter "FullyQualifiedName~TodoServiceTests"
```

Expected: compile error — no `SetPriority`, and `Add` takes at most 2 arguments.

- [ ] **Step 3: Extend `Add`**

In `src/Caffeine.Core/Todos/TodoService.cs`, change the `Add` signature and the object initializer. The parameter is optional so all existing call sites keep compiling.

```csharp
    /// <summary>Adds a to-do; whitespace-only titles are ignored. Returns the new item, or null when ignored.</summary>
    public TodoItem? Add(string title, DateOnly? dueDate = null, TodoPriority priority = TodoPriority.Normal)
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
            Priority = priority,
            CreatedAt = _clock.UtcNow,
        };
        _list.Items.Add(item);
        _store.Save(_list);
        return item;
    }
```

- [ ] **Step 4: Add `SetPriority`**

Insert directly after `SetDone` in the same file. The unchanged-value guard mirrors `SetDone` and avoids a pointless disk write.

```csharp
    /// <summary>Changes a to-do's priority. No-ops on unknown id or an unchanged value.</summary>
    public void SetPriority(Guid id, TodoPriority priority)
    {
        TodoItem? item = _list.Items.FirstOrDefault(i => i.Id == id);
        if (item is null || item.Priority == priority)
        {
            return;
        }

        item.Priority = priority;
        _store.Save(_list);
    }
```

- [ ] **Step 5: Run tests to verify they pass**

```bash
dotnet test tests/Caffeine.Core.Tests/Caffeine.Core.Tests.csproj --filter "FullyQualifiedName~TodoServiceTests"
```

Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add src/Caffeine.Core/Todos/TodoService.cs tests/Caffeine.Core.Tests/TodoServiceTests.cs
git commit -m "feat(todos): set priority on add and via SetPriority"
```

---

### Task 3: Active-list ordering

**Files:**
- Modify: `src/Caffeine.Core/Todos/TodoService.cs`
- Test: `tests/Caffeine.Core.Tests/TodoServiceTests.cs`

**Interfaces:**
- Consumes: `TodoPriority`, `TodoService.Add(...)` with priority (Tasks 1–2).
- Produces: `TodoService.Active` ordered by overdue → priority (desc) → due date → `CreatedAt` (desc). No signature change.

**Note:** the existing `Active_OrdersOverdueThenDueThenNewest` test must keep passing untouched — all five of its items default to `Normal`, so the new priority tiebreaker is neutral there. If it fails, the ordering was implemented wrong.

- [ ] **Step 1: Write the failing tests**

Add to `tests/Caffeine.Core.Tests/TodoServiceTests.cs`. The second test is the load-bearing one: it pins "overdue outranks priority."

```csharp
    [Fact]
    public void Active_OrdersByPriority_AmongUndatedItems()
    {
        var service = CreateService();

        TodoItem low = service.Add("low", null, TodoPriority.Low)!;
        _clock.Advance(TimeSpan.FromMinutes(1));
        TodoItem urgent = service.Add("urgent", null, TodoPriority.Urgent)!;
        _clock.Advance(TimeSpan.FromMinutes(1));
        TodoItem normal = service.Add("normal", null, TodoPriority.Normal)!;

        Assert.Equal(
            new[] { urgent.Id, normal.Id, low.Id },
            service.Active.Select(i => i.Id).ToArray());
    }

    [Fact]
    public void Active_PutsOverdueAboveHigherPriority()
    {
        var service = CreateService();
        DateOnly today = service.Today;

        TodoItem urgentNotOverdue = service.Add("urgent, on time", today.AddDays(3), TodoPriority.Urgent)!;
        _clock.Advance(TimeSpan.FromMinutes(1));
        TodoItem lowOverdue = service.Add("low, overdue", today.AddDays(-1), TodoPriority.Low)!;

        Assert.Equal(
            new[] { lowOverdue.Id, urgentNotOverdue.Id },
            service.Active.Select(i => i.Id).ToArray());
    }

    [Fact]
    public void Active_EqualPriority_StillOrdersByDueDateThenNewest()
    {
        var service = CreateService();
        DateOnly today = service.Today;

        TodoItem noDueOld = service.Add("no due, old", null, TodoPriority.High)!;
        _clock.Advance(TimeSpan.FromMinutes(1));
        TodoItem dueLater = service.Add("due later", today.AddDays(5), TodoPriority.High)!;
        _clock.Advance(TimeSpan.FromMinutes(1));
        TodoItem dueSooner = service.Add("due sooner", today.AddDays(2), TodoPriority.High)!;
        _clock.Advance(TimeSpan.FromMinutes(1));
        TodoItem noDueNew = service.Add("no due, new", null, TodoPriority.High)!;

        Assert.Equal(
            new[] { dueSooner.Id, dueLater.Id, noDueNew.Id, noDueOld.Id },
            service.Active.Select(i => i.Id).ToArray());
    }

    [Fact]
    public void Completed_OrderingIgnoresPriority()
    {
        var service = CreateService();
        TodoItem lowFirst = service.Add("low", null, TodoPriority.Low)!;
        TodoItem urgentSecond = service.Add("urgent", null, TodoPriority.Urgent)!;

        service.SetDone(urgentSecond.Id, true);
        _clock.Advance(TimeSpan.FromMinutes(1));
        service.SetDone(lowFirst.Id, true);

        // Most recently completed first, regardless of priority.
        Assert.Equal(
            new[] { lowFirst.Id, urgentSecond.Id },
            service.Completed.Select(i => i.Id).ToArray());
    }
```

- [ ] **Step 2: Run tests to verify they fail**

```bash
dotnet test tests/Caffeine.Core.Tests/Caffeine.Core.Tests.csproj --filter "FullyQualifiedName~TodoServiceTests"
```

Expected: `Active_OrdersByPriority_AmongUndatedItems` and `Active_PutsOverdueAboveHigherPriority` FAIL on order mismatch (`Completed_OrderingIgnoresPriority` should already pass).

- [ ] **Step 3: Change the ordering**

In `src/Caffeine.Core/Todos/TodoService.cs`, replace the `Active` property:

```csharp
    /// <summary>
    /// Open items: overdue first, then most urgent, then by due date, then newest first.
    /// Overdue outranks priority on purpose — a missed deadline beats a freshly-typed "Urgent".
    /// </summary>
    public IReadOnlyList<TodoItem> Active =>
        _list.Items
            .Where(i => !i.IsDone)
            .OrderByDescending(i => IsOverdue(i))
            .ThenByDescending(i => i.Priority)
            .ThenBy(i => i.DueDate ?? DateOnly.MaxValue)
            .ThenByDescending(i => i.CreatedAt)
            .ToList();
```

- [ ] **Step 4: Drop the stale "no priorities" comment**

In the same file, the class doc comment starts with `Single-list to-do store (v1: no priorities, projects, or subtasks).` Change that first line to:

```csharp
/// Single-list to-do store (v1: no projects or subtasks).
```

- [ ] **Step 5: Run the full test suite**

```bash
dotnet test tests/Caffeine.Core.Tests/Caffeine.Core.Tests.csproj
```

Expected: PASS — including the pre-existing `Active_OrdersOverdueThenDueThenNewest`.

- [ ] **Step 6: Commit**

```bash
git add src/Caffeine.Core/Todos/TodoService.cs tests/Caffeine.Core.Tests/TodoServiceTests.cs
git commit -m "feat(todos): order active list by overdue then priority"
```

---

### Task 4: `TodoPriorityInfo` presentation lookup

**Files:**
- Create: `src/Caffeine.App/Pages/TodoPriorityInfo.cs`

**Interfaces:**
- Consumes: `TodoPriority` (Task 1).
- Produces: `internal static class TodoPriorityInfo` in namespace `Caffeine.Pages`, with:
  - `static IReadOnlyList<TodoPriority> DisplayOrder` — Urgent → Low.
  - `static string Emoji(TodoPriority priority)`
  - `static string Label(TodoPriority priority)`
  - `static string Display(TodoPriority priority)` — `"{emoji} {label}"`, used for combo items and flyout entries.

This is a pure lookup with no test of its own; Tasks 5 and 6 consume it and the manual verification pass in Task 7 confirms the strings render. It lives in the app layer because emoji and Portuguese labels are presentation, not domain.

- [ ] **Step 1: Create the file**

```csharp
using Caffeine.Core.Todos;

namespace Caffeine.Pages;

/// <summary>
/// Emoji + Portuguese label for each priority. Single source of truth so the add
/// dialog, the row badge, and the row flyout cannot drift apart.
/// </summary>
internal static class TodoPriorityInfo
{
    /// <summary>Most urgent first — the order a user expects to scan in a priority menu.</summary>
    public static IReadOnlyList<TodoPriority> DisplayOrder { get; } = new[]
    {
        TodoPriority.Urgent,
        TodoPriority.High,
        TodoPriority.Medium,
        TodoPriority.Normal,
        TodoPriority.Low,
    };

    public static string Emoji(TodoPriority priority) => priority switch
    {
        TodoPriority.Urgent => "🔴",
        TodoPriority.High => "🟠",
        TodoPriority.Medium => "🟡",
        TodoPriority.Normal => "🔵",
        TodoPriority.Low => "🟢",
        _ => "🔵",
    };

    public static string Label(TodoPriority priority) => priority switch
    {
        TodoPriority.Urgent => "Urgente",
        TodoPriority.High => "Alta",
        TodoPriority.Medium => "Média",
        TodoPriority.Normal => "Normal",
        TodoPriority.Low => "Baixa",
        _ => "Normal",
    };

    /// <summary>"🔴 Urgente" — for combo box items and flyout entries.</summary>
    public static string Display(TodoPriority priority) =>
        $"{Emoji(priority)} {Label(priority)}";
}
```

- [ ] **Step 2: Verify it compiles**

```bash
dotnet build src/Caffeine.App/Caffeine.App.csproj -p:Platform=x64 -p:RuntimeIdentifier=win-x64
```

Expected: build succeeds.

- [ ] **Step 3: Commit**

```bash
git add src/Caffeine.App/Pages/TodoPriorityInfo.cs
git commit -m "feat(todos): add priority emoji and label lookup"
```

---

### Task 5: Replace the inline add row with a dialog

**Files:**
- Modify: `src/Caffeine.App/Pages/TodosPage.xaml`
- Modify: `src/Caffeine.App/Pages/TodosPage.xaml.cs`

**Interfaces:**
- Consumes: `TodoPriorityInfo` (Task 4), `TodoService.Add(title, due, priority)` (Task 2).
- Produces: an "Add to-do" `Button` named `AddButton` wired to `Add_Click`; a private `async Task ShowAddDialogAsync()` on `TodosPage`.

- [ ] **Step 1: Replace the add row in XAML**

In `src/Caffeine.App/Pages/TodosPage.xaml`, replace the whole `<Grid Grid.Row="2" …>` block (the one holding `NewTitleBox`, `NewDuePicker`, and `AddButton`) with a single button. `HorizontalAlignment="Left"` keeps it from stretching across the 800px column.

```xml
        <Button
            Grid.Row="2"
            x:Name="AddButton"
            Content="Add to-do"
            Style="{StaticResource AccentButtonStyle}"
            HorizontalAlignment="Left"
            Margin="0,0,0,16"
            Click="Add_Click" />
```

- [ ] **Step 2: Rewrite the add handlers in code-behind**

In `src/Caffeine.App/Pages/TodosPage.xaml.cs`, delete `NewTitleBox_KeyDown` and the whole `AddTodo()` method, and replace `Add_Click` with the dialog flow below. `NewTitleBox` and `NewDuePicker` no longer exist, so any reference to them must go.

```csharp
    private void Add_Click(object sender, RoutedEventArgs e) => _ = ShowAddDialogAsync();

    /// <summary>Opens the add dialog: title, optional due date, and priority (default Normal).</summary>
    private async Task ShowAddDialogAsync()
    {
        var titleBox = new TextBox { PlaceholderText = "Add a to-do…" };
        AutomationProperties.SetName(titleBox, "To-do title");

        var duePicker = new CalendarDatePicker { PlaceholderText = "Due date" };
        AutomationProperties.SetName(duePicker, "Due date");

        var priorityBox = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
        AutomationProperties.SetName(priorityBox, "Prioridade");
        foreach (TodoPriority level in TodoPriorityInfo.DisplayOrder)
        {
            priorityBox.Items.Add(new ComboBoxItem
            {
                Content = TodoPriorityInfo.Display(level),
                Tag = level,
            });
            if (level == TodoPriority.Normal)
            {
                priorityBox.SelectedIndex = priorityBox.Items.Count - 1;
            }
        }

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "Add to-do",
            PrimaryButtonText = "Add",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            IsPrimaryButtonEnabled = false, // empty title — nothing to add yet
        };

        // Gate the primary button so the dialog can't silently discard a blank title.
        titleBox.TextChanged += (_, _) =>
            dialog.IsPrimaryButtonEnabled = titleBox.Text.Trim().Length > 0;

        var content = new StackPanel { Spacing = 12 };
        content.Children.Add(titleBox);
        content.Children.Add(duePicker);
        content.Children.Add(priorityBox);
        dialog.Content = content;

        // Focus on Opened, not before ShowAsync — the content isn't in the visual tree yet.
        // This is the one construct here with no precedent elsewhere in the app; if focus
        // doesn't land in the title box at runtime (Task 7, check 2), try setting
        // titleBox.Loaded instead.
        dialog.Opened += (_, _) => titleBox.Focus(FocusState.Programmatic);

        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        DateOnly? due = duePicker.Date is { } picked
            ? DateOnly.FromDateTime(picked.Date)
            : null;
        var priority = (TodoPriority)((ComboBoxItem)priorityBox.SelectedItem).Tag;

        if (_todos.Add(titleBox.Text, due, priority) is null)
        {
            return; // whitespace-only title — nothing to add
        }

        if (_showCompleted)
        {
            ViewSelector.SelectedItem = ActiveTab; // rebuilds via SelectionChanged
        }
        else
        {
            RebuildList();
        }
    }
```

- [ ] **Step 3: Remove the now-unused `KeyRoutedEventArgs` using if the compiler flags it**

`NewTitleBox_KeyDown` was the only user of `Microsoft.UI.Xaml.Input` and `Windows.System`. Build and, if warnings/errors point at unused usings, remove these two lines from the top of `TodosPage.xaml.cs`:

```csharp
using Microsoft.UI.Xaml.Input;
using Windows.System;
```

- [ ] **Step 4: Build**

```bash
dotnet build src/Caffeine.App/Caffeine.App.csproj -p:Platform=x64 -p:RuntimeIdentifier=win-x64
```

Expected: build succeeds with no reference to `NewTitleBox` or `NewDuePicker` remaining.

- [ ] **Step 5: Commit**

```bash
git add src/Caffeine.App/Pages/TodosPage.xaml src/Caffeine.App/Pages/TodosPage.xaml.cs
git commit -m "feat(todos): move to-do creation into a dialog with priority"
```

---

### Task 6: Priority control on each row

**Files:**
- Modify: `src/Caffeine.App/Pages/TodosPage.xaml.cs`

**Interfaces:**
- Consumes: `TodoPriorityInfo` (Task 4), `TodoService.SetPriority` (Task 2).
- Produces: a private `Button BuildPriorityButton(TodoItem item)` on `TodosPage`; `BuildRow` grid grows to four columns.

- [ ] **Step 1: Add the priority button builder**

In `src/Caffeine.App/Pages/TodosPage.xaml.cs`, add this method after `BuildRow`. It mirrors `EmojiPicker.Build`'s button-plus-flyout shape, but each entry here is a labeled button rather than a grid cell, so the label is readable and screen-reader friendly.

```csharp
    /// <summary>Emoji button opening a flyout of the five priority levels; picking one re-sorts the list.</summary>
    private Button BuildPriorityButton(TodoItem item)
    {
        var button = new Button
        {
            Content = new TextBlock { Text = TodoPriorityInfo.Emoji(item.Priority), FontSize = 14 },
            Padding = new Thickness(8),
            VerticalAlignment = VerticalAlignment.Center,
        };
        AutomationProperties.SetName(button, $"Prioridade: {TodoPriorityInfo.Label(item.Priority)}");

        var options = new StackPanel { Spacing = 2 };
        var flyout = new Flyout { Content = options };

        foreach (TodoPriority level in TodoPriorityInfo.DisplayOrder)
        {
            TodoPriority captured = level;
            var option = new Button
            {
                Content = TodoPriorityInfo.Display(captured),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Left,
                Background = null,
                BorderThickness = new Thickness(0),
            };
            AutomationProperties.SetName(option, TodoPriorityInfo.Label(captured));
            option.Click += (_, _) =>
            {
                flyout.Hide();
                _todos.SetPriority(item.Id, captured);
                RebuildList(); // priority can reorder the list — rebuild, don't just repaint
            };
            options.Children.Add(option);
        }

        button.Flyout = flyout;
        return button;
    }
```

- [ ] **Step 2: Add the column and place the button in `BuildRow`**

In `BuildRow`, replace the grid-assembly block (from `var grid = new Grid { ColumnSpacing = 12 };` through `grid.Children.Add(delete);`) with the four-column version:

```csharp
        Button priority = BuildPriorityButton(item);

        var grid = new Grid { ColumnSpacing = 12 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(priority, 1);
        Grid.SetColumn(text, 2);
        Grid.SetColumn(delete, 3);
        grid.Children.Add(check);
        grid.Children.Add(priority);
        grid.Children.Add(text);
        grid.Children.Add(delete);
```

- [ ] **Step 3: Build**

```bash
dotnet build src/Caffeine.App/Caffeine.App.csproj -p:Platform=x64 -p:RuntimeIdentifier=win-x64
```

Expected: build succeeds.

- [ ] **Step 4: Commit**

```bash
git add src/Caffeine.App/Pages/TodosPage.xaml.cs
git commit -m "feat(todos): change priority from the to-do row"
```

---

### Task 7: Full suite + manual verification

**Files:**
- Modify: none expected (fix-only if a defect surfaces).

**Interfaces:**
- Consumes: everything from Tasks 1–6.
- Produces: nothing new — this is the gate before the work is called done.

- [ ] **Step 1: Run the full test suite**

```bash
dotnet test tests/Caffeine.Core.Tests/Caffeine.Core.Tests.csproj
```

Expected: PASS, zero failures. Report the actual counts; do not claim success without the output.

- [ ] **Step 2: Launch the app**

Use the `verify` skill to build and drive the app. If it is unavailable, build and run manually:

```bash
dotnet build src/Caffeine.App/Caffeine.App.csproj -p:Platform=x64 -p:RuntimeIdentifier=win-x64
```

- [ ] **Step 3: Walk the checklist on the Todos page**

Confirm each, and report any that fail rather than glossing over them:

1. The inline title box and due-date picker are **gone**; only an "Add to-do" button remains.
2. Clicking "Add to-do" opens the dialog with the title focused, priority pre-set to "🔵 Normal".
3. "Add" is disabled while the title is empty or whitespace, and enables as soon as real text is typed.
4. Typing a title and pressing Enter creates the to-do (Enter triggers the default primary button).
5. Cancel discards without creating anything.
6. Creating an item at "🔴 Urgente" shows 🔴 on its row and places it above Normal items that have no due date.
7. Clicking a row's emoji opens the five-level flyout; picking a different level updates the emoji and re-sorts the list.
8. An **overdue** item stays above a non-overdue 🔴 Urgente item.
9. Adding while the Completed tab is active switches to Active so the new item is visible.
10. Existing to-dos created before this change still load and show 🔵 Normal.

- [ ] **Step 4: Commit any fixes**

Only if Step 3 surfaced a defect:

```bash
git add -A
git commit -m "fix(todos): <what was actually wrong>"
```

---

## Self-Review

**Spec coverage:**
- Data model (`TodoPriority`, `TodoItem.Priority`, backward compat) → Task 1.
- `SetPriority`, `Add` with priority → Task 2.
- Active ordering, `Completed` unchanged, stale comment removed → Task 3.
- `TodoPriorityInfo` emoji/label table → Task 4.
- Inline row removed, `ContentDialog` creation, disabled-primary gating, focus + Enter, Completed→Active switch → Task 5.
- Row emoji button + flyout, four-column grid, `AutomationProperties` → Task 6.
- All eight spec test cases → Tasks 1–3; manual verification pass → Task 7.
- Categories untouched: no task modifies them.

**Type consistency:** `TodoPriority` members (`Low`/`Normal`/`Medium`/`High`/`Urgent`) are identical across Tasks 1–6. `TodoPriorityInfo.Emoji` / `Label` / `Display` / `DisplayOrder` are defined in Task 4 and used with those exact names in Tasks 5–6. `Add(title, due, priority)` is defined in Task 2 and called with three arguments in Task 5. `SetPriority(Guid, TodoPriority)` is defined in Task 2 and called in Task 6.

**Known risk:** Task 5 deletes `NewTitleBox`/`NewDuePicker` from XAML while `TodosPage.xaml.cs` still references them mid-edit — the file will not compile until both Step 1 and Step 2 are complete. Do them together before building.
