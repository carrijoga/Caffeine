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

    /// <summary>True when <paramref name="habit"/> is scheduled to run on <paramref name="date"/>'s day of week.</summary>
    public bool IsScheduled(Habit habit, DateOnly date) => habit.Repeat.HasFlag(ToWeekdayFlag(date.DayOfWeek));

    /// <summary>Updates which days of the week the habit repeats on.</summary>
    public void SetRepeat(Guid id, Weekdays repeat)
    {
        Habit? habit = _list.Items.FirstOrDefault(h => h.Id == id);
        if (habit is null || habit.Repeat == repeat)
        {
            return;
        }

        habit.Repeat = repeat;
        _store.Save(_list);
    }

    private static Weekdays ToWeekdayFlag(DayOfWeek day) => day switch
    {
        DayOfWeek.Sunday => Weekdays.Sunday,
        DayOfWeek.Monday => Weekdays.Monday,
        DayOfWeek.Tuesday => Weekdays.Tuesday,
        DayOfWeek.Wednesday => Weekdays.Wednesday,
        DayOfWeek.Thursday => Weekdays.Thursday,
        DayOfWeek.Friday => Weekdays.Friday,
        DayOfWeek.Saturday => Weekdays.Saturday,
        _ => Weekdays.None,
    };

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

    /// <summary>Consecutive scheduled occurrences ending today or yesterday; 0 when the most recent scheduled occurrence was missed.</summary>
    public int CurrentStreak(Habit habit)
    {
        DateOnly day = IsScheduled(habit, Today) && habit.CompletedOn.Contains(Today)
            ? Today
            : Today.AddDays(-1);

        int streak = 0;
        int safety = 0;
        while (safety < 3650) // safety bound: if nothing is scheduled (Repeat == None), never loop forever
        {
            if (IsScheduled(habit, day))
            {
                if (!habit.CompletedOn.Contains(day))
                {
                    break;
                }

                streak++;
            }

            day = day.AddDays(-1);
            safety++;
        }

        return streak;
    }

    /// <summary>Longest consecutive run of scheduled occurrences anywhere in the completion log.</summary>
    public int BestStreak(Habit habit)
    {
        int best = 0;
        int run = 0;
        DateOnly? previousScheduled = null;
        foreach (DateOnly date in habit.CompletedOn.Order())
        {
            if (!IsScheduled(habit, date))
            {
                continue;
            }

            bool isNextScheduledAfterPrevious = previousScheduled is { } prev && IsImmediatelyNextScheduled(habit, prev, date);
            run = run > 0 && isNextScheduledAfterPrevious ? run + 1 : 1;
            best = Math.Max(best, run);
            previousScheduled = date;
        }

        return best;
    }

    /// <summary>True when <paramref name="candidate"/> is the next scheduled date strictly after <paramref name="previous"/>, with no scheduled date in between.</summary>
    private bool IsImmediatelyNextScheduled(Habit habit, DateOnly previous, DateOnly candidate)
    {
        for (DateOnly day = previous.AddDays(1); day < candidate; day = day.AddDays(1))
        {
            if (IsScheduled(habit, day))
            {
                return false; // a scheduled date was missed between previous and candidate
            }
        }

        return true;
    }

    /// <summary>Completion flags for the last 7 scheduled dates on/before today, oldest first; index 6 is the most recent scheduled date.</summary>
    public IReadOnlyList<bool> LastSevenDays(Habit habit)
    {
        var results = new List<bool>(7);
        DateOnly day = Today;
        int safety = 0;
        while (results.Count < 7 && safety < 3650)
        {
            if (IsScheduled(habit, day))
            {
                results.Add(habit.CompletedOn.Contains(day));
            }

            day = day.AddDays(-1);
            safety++;
        }

        results.Reverse();

        // Pad with false at the front if fewer than 7 scheduled dates exist in the lookback window
        // (e.g. a brand-new once-a-week habit) so callers can always index 0..6 safely.
        while (results.Count < 7)
        {
            results.Insert(0, false);
        }

        return results;
    }
}
