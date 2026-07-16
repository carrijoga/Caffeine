using Caffeine.Core.Common;
using Caffeine.Core.Habits;
using Xunit;

namespace Caffeine.Core.Tests;

public class HabitServiceTests : IDisposable
{
    private readonly string _dir = Path.Combine(
        Path.GetTempPath(), "caffeine-tests-" + Guid.NewGuid().ToString("N"));

    private readonly FakeClock _clock = new();

    private HabitService CreateService() =>
        new(_clock, new JsonStore<HabitList>("habits.json", _dir));

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch
        {
        }
    }

    /// <summary>Marks <paramref name="count"/> consecutive days done, starting at <paramref name="start"/>.</summary>
    private static void MarkDays(HabitService service, Guid id, DateOnly start, int count)
    {
        for (int i = 0; i < count; i++)
        {
            service.SetDone(id, start.AddDays(i), true);
        }
    }

    [Fact]
    public void Add_TrimsNameAndIcon_AndStampsCreation()
    {
        var service = CreateService();

        Habit? habit = service.Add("  drink water  ", " X ");

        Assert.NotNull(habit);
        Assert.Equal("drink water", habit.Name);
        Assert.Equal("X", habit.Icon);
        Assert.Equal(service.Today, habit.CreatedOn);
        Assert.Empty(habit.CompletedOn);
    }

    [Fact]
    public void Add_EmptyName_IsIgnored()
    {
        var service = CreateService();

        Assert.Null(service.Add("   "));
        Assert.Null(service.Add(string.Empty));
        Assert.Empty(service.Habits);
    }

    [Fact]
    public void Add_BlankIcon_GetsDefault()
    {
        var service = CreateService();

        Habit habit = service.Add("stretch")!;

        Assert.Equal(Habit.DefaultIcon, habit.Icon);
    }

    [Fact]
    public void Add_DefaultsRepeatToEveryDay()
    {
        var service = CreateService();

        Habit habit = service.Add("stretch")!;

        Assert.Equal(Weekdays.All, habit.Repeat);
    }

    [Fact]
    public void Rename_TrimsNewName_AndIgnoresEmpty()
    {
        var service = CreateService();
        Habit habit = service.Add("jog")!;

        service.Rename(habit.Id, "  morning jog  ");
        Assert.Equal("morning jog", habit.Name);

        service.Rename(habit.Id, "   ");
        Assert.Equal("morning jog", habit.Name);
    }

    [Fact]
    public void Delete_RemovesHabit()
    {
        var service = CreateService();
        Habit keep = service.Add("keep")!;
        Habit drop = service.Add("drop")!;

        service.Delete(drop.Id);

        Habit remaining = Assert.Single(service.Habits);
        Assert.Equal(keep.Id, remaining.Id);
    }

    [Fact]
    public void SetDone_MarksAndUnmarksADate()
    {
        var service = CreateService();
        Habit habit = service.Add("stretch")!;

        service.SetDone(habit.Id, service.Today, true);
        Assert.True(service.IsDone(habit, service.Today));

        service.SetDone(habit.Id, service.Today, false);
        Assert.False(service.IsDone(habit, service.Today));
    }

    [Fact]
    public void CurrentStreak_ZeroWhenNeverDone()
    {
        var service = CreateService();
        Habit habit = service.Add("meditate")!;

        Assert.Equal(0, service.CurrentStreak(habit));
    }

    [Fact]
    public void CurrentStreak_OneWhenDoneTodayOnly()
    {
        var service = CreateService();
        Habit habit = service.Add("meditate")!;

        service.SetDone(habit.Id, service.Today, true);

        Assert.Equal(1, service.CurrentStreak(habit));
    }

    [Fact]
    public void CurrentStreak_CountsConsecutiveRunEndingToday()
    {
        var service = CreateService();
        Habit habit = service.Add("read")!;
        MarkDays(service, habit.Id, service.Today.AddDays(-2), 3);

        Assert.Equal(3, service.CurrentStreak(habit));
    }

    [Fact]
    public void CurrentStreak_RunEndingYesterday_IsPreserved()
    {
        var service = CreateService();
        Habit habit = service.Add("run")!;
        MarkDays(service, habit.Id, service.Today.AddDays(-3), 3);

        Assert.Equal(3, service.CurrentStreak(habit));
    }

    [Fact]
    public void CurrentStreak_ZeroWhenLastDoneTwoDaysAgo()
    {
        var service = CreateService();
        Habit habit = service.Add("run")!;
        MarkDays(service, habit.Id, service.Today.AddDays(-5), 4);

        Assert.Equal(0, service.CurrentStreak(habit));
    }

    [Fact]
    public void CurrentStreak_AcrossDayBoundary()
    {
        var service = CreateService();
        Habit habit = service.Add("meditate")!;
        service.SetDone(habit.Id, service.Today, true);
        Assert.Equal(1, service.CurrentStreak(habit));

        _clock.Advance(TimeSpan.FromDays(1));
        Assert.Equal(1, service.CurrentStreak(habit));

        _clock.Advance(TimeSpan.FromDays(1));
        Assert.Equal(0, service.CurrentStreak(habit));
    }

    [Fact]
    public void BestStreak_ZeroWhenNeverDone()
    {
        var service = CreateService();
        Habit habit = service.Add("write")!;

        Assert.Equal(0, service.BestStreak(habit));
    }

    [Fact]
    public void BestStreak_FindsLongestHistoricalRun()
    {
        var service = CreateService();
        Habit habit = service.Add("write")!;
        MarkDays(service, habit.Id, service.Today.AddDays(-20), 5);
        MarkDays(service, habit.Id, service.Today.AddDays(-1), 2);

        Assert.Equal(5, service.BestStreak(habit));
        Assert.Equal(2, service.CurrentStreak(habit));
    }

    [Fact]
    public void LastSevenDays_FlagsCompletedPositions()
    {
        var service = CreateService();
        Habit habit = service.Add("walk")!;
        service.SetDone(habit.Id, service.Today, true);
        service.SetDone(habit.Id, service.Today.AddDays(-3), true);
        service.SetDone(habit.Id, service.Today.AddDays(-8), true);

        IReadOnlyList<bool> days = service.LastSevenDays(habit);

        Assert.Equal(7, days.Count);
        Assert.True(days[6]);
        Assert.True(days[3]);
        Assert.False(days[0]);
        Assert.False(days[1]);
        Assert.False(days[2]);
        Assert.False(days[4]);
        Assert.False(days[5]);
    }

    [Fact]
    public void LastSevenDays_MarksOldestDay_WhenDoneSevenDaysAgo()
    {
        var service = CreateService();
        Habit habit = service.Add("Read")!;

        // Today - 6 is index 0 (the oldest slot in the 7-day window).
        service.SetDone(habit.Id, service.Today.AddDays(-6), true);

        IReadOnlyList<bool> days = service.LastSevenDays(habit);

        Assert.True(days[0]);   // oldest day is inclusive
        Assert.False(days[5]);  // an untouched interior day
        Assert.False(days[6]);  // today, not marked
    }

    [Fact]
    public void Changes_PersistAcrossReload()
    {
        var first = CreateService();
        Habit habit = first.Add("read", "B")!;
        first.SetDone(habit.Id, first.Today, true);
        first.SetDone(habit.Id, first.Today.AddDays(-1), true);

        var second = CreateService();

        Habit reloaded = Assert.Single(second.Habits);
        Assert.Equal(habit.Id, reloaded.Id);
        Assert.Equal("read", reloaded.Name);
        Assert.Equal("B", reloaded.Icon);
        Assert.Equal(2, second.CurrentStreak(reloaded));
    }
}
