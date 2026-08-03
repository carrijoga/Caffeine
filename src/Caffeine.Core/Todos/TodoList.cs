namespace Caffeine.Core.Todos;

/// <summary>Document persisted to todos.json via JsonStore<TodoList>.</summary>
public sealed class TodoList
{
    public List<TodoItem> Items { get; set; } = new();

    public List<TodoCategory> Categories { get; set; } = new();
}
