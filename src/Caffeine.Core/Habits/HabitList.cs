namespace Caffeine.Core.Habits;

/// <summary>Document persisted to habits.json via JsonStore&lt;HabitList&gt;.</summary>
public sealed class HabitList
{
    public List<Habit> Items { get; set; } = new();
}
