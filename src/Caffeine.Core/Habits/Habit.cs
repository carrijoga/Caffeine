namespace Caffeine.Core.Habits;

/// <summary>One habit; persisted inside habits.json.</summary>
public sealed class Habit
{
    public const string DefaultIcon = "⭐";

    public Guid Id { get; set; } = Guid.NewGuid();

    public string Name { get; set; } = string.Empty;

    /// <summary>Emoji shown next to the name.</summary>
    public string Icon { get; set; } = DefaultIcon;

    public DateOnly CreatedOn { get; set; }

    /// <summary>Days of the week this habit is scheduled on. Defaults to every day.</summary>
    public Weekdays Repeat { get; set; } = Weekdays.All;

    /// <summary>The completion log: every day this habit was checked off.</summary>
    public HashSet<DateOnly> CompletedOn { get; set; } = new();
}
