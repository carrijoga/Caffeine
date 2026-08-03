namespace Caffeine.Core.Todos;

/// <summary>A user-defined label for grouping to-dos; persisted inside todos.json.</summary>
public sealed class TodoCategory
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Name { get; set; } = string.Empty;

    /// <summary>Hex color (e.g. "#6E6E6E") drawn from the fixed in-app palette in CategoryColors.</summary>
    public string ColorHex { get; set; } = "#6E6E6E";
}
