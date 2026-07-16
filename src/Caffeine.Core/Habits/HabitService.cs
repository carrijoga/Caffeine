using Caffeine.Core.Common;

namespace Caffeine.Core.Habits;

/// <summary>
/// Daily-habit store with streak calculation. Every mutation saves
/// immediately through the JsonStore, matching the persistence rules of the
/// other modules. "Today" is the clock's local date. Streak rule (spec):
/// consecutive days ending today or yesterday — missing today does not zero
/// the streak until the day is over.
/// </summary>
public sealed class HabitService
{
    private readonly IClock _clock;
    private readonly JsonStore<HabitList> _store;
    private readonly HabitList _list;

    public HabitService(IClock clock, JsonStore<HabitList> store)
    {
        _clock = clock;
        _store = store;
        _list = store.Load();
    }

    /// <summary>The clock's current local date; all day-based logic uses this.</summary>
    public DateOnly Today => DateOnly.FromDateTime(_clock.UtcNow.LocalDateTime);

    /// <summary>Habits in creation order.</summary>
    public IReadOnlyList<Habit> Habits => _list.Items;

    /// <summary>Adds a habit; whitespace-only names are ignored. A blank icon gets the default.</summary>
    public Habit? Add(string name, string icon = "")
    {
        string trimmedName = name.Trim();
        if (trimmedName.Length == 0)
        {
            return null;
        }

        string trimmedIcon = icon.Trim();
        var habit = new Habit
        {
            Name = trimmedName,
            Icon = trimmedIcon.Length == 0 ? Habit.DefaultIcon : trimmedIcon,
            CreatedOn = Today,
        };
        _list.Items.Add(habit);
        _store.Save(_list);
        return habit;
    }

    /// <summary>Renames a habit; whitespace-only names are ignored.</summary>
    public void Rename(Guid id, string newName)
    {
        string trimmed = newName.Trim();
        Habit? habit = _list.Items.FirstOrDefault(h => h.Id == id);
        if (habit is null || trimmed.Length == 0 || habit.Name == trimmed)
        {
            return;
        }

        habit.Name = trimmed;
        _store.Save(_list);
    }

    /// <summary>Deletes the habit and its whole completion history.</summary>
    public void Delete(Guid id)
    {
        if (_list.Items.RemoveAll(h => h.Id == id) > 0)
        {
            _store.Save(_list);
        }
    }

    public bool IsDone(Habit habit, DateOnly date) => habit.CompletedOn.Contains(date);

    public void SetDone(Guid id, DateOnly date, bool done)
    {
        Habit? habit = _list.Items.FirstOrDefault(h => h.Id == id);
        if (habit is null)
        {
            return;
        }

        bool changed = done ? habit.CompletedOn.Add(date) : habit.CompletedOn.Remove(date);
        if (changed)
        {
            _store.Save(_list);
        }
    }

    /// <summary>Consecutive days ending today or yesterday; 0 when neither day is completed.</summary>
    public int CurrentStreak(Habit habit)
    {
        DateOnly day = habit.CompletedOn.Contains(Today) ? Today : Today.AddDays(-1);
        int streak = 0;
        while (habit.CompletedOn.Contains(day))
        {
            streak++;
            day = day.AddDays(-1);
        }

        return streak;
    }

    /// <summary>Longest consecutive run anywhere in the completion log.</summary>
    public int BestStreak(Habit habit)
    {
        int best = 0;
        int run = 0;
        DateOnly previous = default;
        foreach (DateOnly date in habit.CompletedOn.Order())
        {
            run = run > 0 && date == previous.AddDays(1) ? run + 1 : 1;
            best = Math.Max(best, run);
            previous = date;
        }

        return best;
    }

    /// <summary>Completion flags for the last 7 days, oldest first; index 6 is today.</summary>
    public IReadOnlyList<bool> LastSevenDays(Habit habit)
    {
        var days = new bool[7];
        for (int i = 0; i < 7; i++)
        {
            days[i] = habit.CompletedOn.Contains(Today.AddDays(i - 6));
        }

        return days;
    }
}
