namespace Caffeine.Core.Habits;

/// <summary>Which days of the week a habit repeats on.</summary>
[Flags]
public enum Weekdays
{
    None = 0,
    Sunday = 1,
    Monday = 2,
    Tuesday = 4,
    Wednesday = 8,
    Thursday = 16,
    Friday = 32,
    Saturday = 64,
    All = Sunday | Monday | Tuesday | Wednesday | Thursday | Friday | Saturday,
}
