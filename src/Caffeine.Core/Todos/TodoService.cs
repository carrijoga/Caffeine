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
