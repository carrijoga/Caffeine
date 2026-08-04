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

    public Guid? CategoryId { get; set; }

    /// <summary>Defaults to Normal, so to-dos saved before this field existed load as Normal.</summary>
    public TodoPriority Priority { get; set; } = TodoPriority.Normal;
}
