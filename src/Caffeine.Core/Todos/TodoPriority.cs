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
