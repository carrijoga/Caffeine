# TODO Priority — Design

## Problem

`TodoItem` today has no notion of importance. Every open to-do carries the
same weight, so the only ordering signal is the due date — and items with
no due date fall back to creation order, which is arbitrary. The user wants
a five-level priority (🟢 Baixa, 🔵 Normal, 🟡 Média, 🟠 Alta, 🔴 Urgente)
attached to every to-do, visible on the row, and reflected in the ordering
of the Active list.

Alongside this, to-do creation moves out of the inline row and into a
dialog, so the new priority field has somewhere to live at creation time
without crowding the page header.

## Scope

Priority is a fixed five-level scale — not user-defined, unlike categories.
Every to-do always has exactly one priority; there is no "unset" state.
Creation moves to a modal dialog that collects title, due date, and
priority. Priority is editable after creation from the to-do's row.

Out of scope: user-defined priority levels, per-priority filtering, colors
beyond the five emoji, and any change to the Completed list's ordering.

**Categories are deliberately untouched.** The `TodoCategory` model,
`TodoItem.CategoryId`, and `TodoService.Categories` landed in commit
`1adfba4` as data-layer only — there is no UI, no method to create or
assign a category, and `TodoCategory.cs`'s doc comment references a
`CategoryColors` palette that does not exist in the repository. Completing
that work is a separate task. The new add dialog is the natural place a
category field would eventually go, but this design does not add one.

## Data model

New file `src/Caffeine.Core/Todos/TodoPriority.cs`:

```csharp
namespace Caffeine.Core.Todos;

/// <summary>Fixed five-level importance scale for a to-do; persisted inside todos.json.</summary>
public enum TodoPriority
{
    Low = 0,
    Normal = 1,
    Medium = 2,
    High = 3,
    Urgent = 4,
}
```

Numeric values are explicit because they are serialized into `todos.json`;
renumbering them later would silently reinterpret existing files. The
ascending order is intentional — "most urgent first" is
`OrderByDescending(i => i.Priority)`, with no lookup table.

`TodoItem` (`src/Caffeine.Core/Todos/TodoItem.cs`) gains one property:

```csharp
public TodoPriority Priority { get; set; } = TodoPriority.Normal;
```

**Backward compatibility:** existing `todos.json` files predate `Priority`.
`System.Text.Json` leaves absent properties at their C# member default, so
every pre-existing to-do loads as `Normal`. This is the same no-migration
approach used for `CategoryId` and the Habits `Weekdays.Repeat` field.
Note that `Normal = 1` is deliberately *not* the zero value: the default
comes from the property initializer, not from `default(TodoPriority)`, so
the enum's numeric order can stay Low→Urgent. A test pins this behavior.

**Corruption behavior — measured, not assumed.** `JsonStore` registers a
`JsonStringEnumConverter`, so priority persists as a string name
(`"Priority": "Urgent"`), not a number. Verified empirically against the
real `JsonStore`:

| `"Priority"` in the file | Result |
| ------------------------ | ------ |
| absent | loads as `Normal` (the backward-compat case above) |
| `"Bogus"` (unknown name) | **throws → whole file renamed to `todos.json.corrupt.bak`, list replaced with an empty document** |
| `null` | **throws → same whole-file loss** |
| `99` (out-of-range number) | loads and survives as `99`; sorts above `Urgent` |

This means the unreachable `_ =>` fallback arms in `TodoPriorityInfo` do
**not** protect against the corruption shape that can actually occur. They
only cover the numeric case, which this app never writes. The arms are kept
anyway (harmless, and they do cover a hand-edited number), but the real
exposure is `JsonStore`'s catch-all `Load()`, which discards the entire
document on any deserialization failure.

This is a **pre-existing `JsonStore` weakness, not introduced by this
feature** — `Weekdays` has been persisted in `habits.json` under the same
converter since before this change. Hardening it (a tolerant converter, or
a `Load()` that salvages readable items) is tracked as separate work
because the fix belongs in `JsonStore`, shared by every module.

**Forward compatibility:** an older build reading a newer file ignores the
unknown `Priority` member without error, but its next save omits the field —
silently resetting every to-do to `Normal`. Acceptable for a local
single-user desktop app; worth a release note if a downgrade is ever likely.

## Service logic (`TodoService`)

The class doc comment currently reads "v1: no priorities, projects, or
subtasks" — update it to drop the priorities clause.

**Ordering.** `Active` changes from:

```csharp
.OrderBy(i => i.DueDate ?? DateOnly.MaxValue)
.ThenByDescending(i => i.CreatedAt)
```

to:

```csharp
.OrderByDescending(i => IsOverdue(i))   // anything past due stays on top
.ThenByDescending(i => i.Priority)      // Urgent → Low
.ThenBy(i => i.DueDate ?? DateOnly.MaxValue)
.ThenByDescending(i => i.CreatedAt)
```

Overdue outranks priority by design: a real missed deadline is more
actionable than an `Urgent` item created moments ago. Priority then breaks
ties among everything not overdue — which is where the current ordering is
weakest, since undated items previously fell back to creation order alone.

`Completed` is unchanged. Priority is irrelevant once an item is done, and
"most recently completed first" remains the useful order.

**Mutation.** One new method, shaped like the existing `SetDone`:

```csharp
/// <summary>Changes a to-do's priority. No-ops on unknown id or an unchanged value.</summary>
public void SetPriority(Guid id, TodoPriority priority)
```

The unchanged-value no-op avoids a disk write when the user reselects the
current priority, matching `SetDone`'s `item.IsDone == done` guard.

**Creation.** `Add` grows an optional trailing parameter:

```csharp
public TodoItem? Add(string title, DateOnly? dueDate = null, TodoPriority priority = TodoPriority.Normal)
```

Optional with a `Normal` default keeps every existing call site — including
the current tests — compiling unchanged, the same technique the categories
design used for `Guid? categoryId = null`.

## Presentation mapping (`TodoPriorityInfo`)

Emoji and Portuguese label are presentation concerns, not domain data, so
they live in the app layer next to `EmojiPicker.cs` rather than in
`Caffeine.Core`. A single static lookup is the only place these strings
appear, so the add dialog, the row flyout, and the row badge cannot drift
apart:

| Priority | Emoji | Label   |
| -------- | ----- | ------- |
| `Low`    | 🟢    | Baixa   |
| `Normal` | 🔵    | Normal  |
| `Medium` | 🟡    | Média   |
| `High`   | 🟠    | Alta    |
| `Urgent` | 🔴    | Urgente |

The helper exposes the levels in display order (Urgent → Low, matching how
a user scans a priority menu) plus per-level emoji and label accessors.

## UI — Todos page (`TodosPage.xaml` / `.xaml.cs`)

**Creation moves to a dialog.** The inline add row in `TodosPage.xaml` —
`NewTitleBox`, `NewDuePicker`, and the `AddButton` grid — is removed and
replaced by a single "Add to-do" button. All creation flows through the
dialog; there is no second quick-add path.

The dialog follows `HabitsPage`'s add-habit dialog exactly: a
`ContentDialog` with `XamlRoot = XamlRoot`, `PrimaryButtonText = "Add"`,
`CloseButtonText = "Cancel"`, and `DefaultButton = ContentDialogButton.Primary`.
Its content is a `StackPanel` (spacing 12) holding:

- a title `TextBox` (placeholder "Add a to-do…"),
- a `CalendarDatePicker` for the optional due date,
- a priority `ComboBox` listing all five levels as "🔴 Urgente" style
  entries, pre-selected to Normal.

The primary button is disabled while the title is whitespace-only, so the
dialog cannot silently discard input — the same enable/disable gating
`HabitsPage` applies via its weekday picker. On primary, the handler calls
`_todos.Add(title, due, priority)` and rebuilds the list; on cancel it does
nothing. The title box takes focus when the dialog opens and
`DefaultButton = Primary` makes Enter confirm, keeping creation to
click → type → Enter.

This costs the page its former two-keystroke capture (type + Enter). That
is an accepted trade-off of routing all creation through the dialog, and
the focus/Enter behavior above is the mitigation.

If the user is on the Completed tab when adding, the page switches to
Active first — reusing the existing `ViewSelector.SelectedItem = ActiveTab`
branch in `AddTodo`, so the new item is actually visible.

**Row display (`BuildRow`).** Each row gains a priority control between the
checkbox and the text block. It is a compact `Button` whose content is the
priority emoji, with a `Flyout` listing all five levels (emoji + label) —
the same button-plus-flyout shape as `EmojiPicker.Build`. Picking a level
calls `_todos.SetPriority(item.Id, level)`, hides the flyout, and calls
`RebuildList()`. Rebuilding is correct here rather than merely repainting
the row, because a priority change can reorder the Active list, and seeing
the item move is the confirmation that the change took effect.

The row `Grid` grows from three columns to four: checkbox (`Auto`),
priority (`Auto`), text (`*`), delete (`Auto`).

**Accessibility.** An emoji alone conveys nothing to a screen reader, so
the row button gets `AutomationProperties.SetName` of "Prioridade: {label}"
(reflecting the current value), each flyout entry is named by its label,
and the dialog's `ComboBox` is named "Prioridade" — consistent with the
existing `AutomationProperties` calls on the checkbox, delete button, and
due-date picker.

## Testing

Extend `tests/Caffeine.Core.Tests/TodoServiceTests.cs`:

- `Add` defaults a new to-do to `Normal` when no priority is passed, and
  honors an explicit priority when one is.
- `SetPriority` changes the value and persists it across a service reload.
- `SetPriority` no-ops on an unknown id (does not throw).
- Ordering: among non-overdue items with no due date, `Urgent` sorts above
  `Normal` above `Low`.
- Ordering: an **overdue** `Low` item sorts above a non-overdue `Urgent`
  item. This is the load-bearing case for the overdue-outranks-priority
  rule and the easiest to regress in a later refactor.
- Ordering: among items of equal priority, the earlier due date still wins,
  and undated items still fall back to newest-first.
- `Completed` ordering is unaffected by priority.
- A legacy JSON fixture with no `Priority` field loads as `Normal`,
  mirroring the existing `Load_LegacyJsonWithoutCategoryFields_...` test.

No new automated UI test file, consistent with the rest of this codebase.
The implementation plan's UI tasks include a manual verification pass (via
the `verify` skill) covering: opening the add dialog from the button,
the primary button staying disabled on an empty title, creating a to-do at
a non-default priority, the emoji rendering on the row, changing priority
from the row flyout and watching the item reorder, and confirming the
inline add row is gone.

## Out of scope

- User-defined or reorderable priority levels (the five are fixed).
- Filtering or grouping the list by priority.
- Priority affecting the Completed list's ordering.
- Any change to categories, including wiring up the existing model-only
  `CategoryId` / `Categories` fields or the missing `CategoryColors` palette.
- Bulk priority edits across multiple to-dos.
- Any server-side or cross-device sync (persistence remains the existing
  local `todos.json` file, unchanged mechanism).
