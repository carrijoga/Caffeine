using Caffeine.Core.Common;
using Caffeine.Core.Todos;
using Xunit;

namespace Caffeine.Core.Tests;

public class TodoServiceTests : IDisposable
{
    private readonly string _dir = Path.Combine(
        Path.GetTempPath(), "caffeine-tests-" + Guid.NewGuid().ToString("N"));

    private readonly FakeClock _clock = new();

    private TodoService CreateService() =>
        new(_clock, new JsonStore<TodoList>("todos.json", _dir));

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

    [Fact]
    public void Add_TrimsTitle_AndStampsCreation()
    {
        var service = CreateService();

        TodoItem? item = service.Add("  buy milk  ");

        Assert.NotNull(item);
        Assert.Equal("buy milk", item.Title);
        Assert.Equal(_clock.UtcNow, item.CreatedAt);
        Assert.False(item.IsDone);
        Assert.Null(item.DueDate);
        Assert.Null(item.CompletedAt);
    }

    [Fact]
    public void Add_WhitespaceTitle_IsIgnored()
    {
        var service = CreateService();

        Assert.Null(service.Add("   "));
        Assert.Null(service.Add(string.Empty));
        Assert.Empty(service.Active);
    }

    [Fact]
    public void Active_SortsOverdueThenDueDateThenNewest()
    {
        var service = CreateService();
        DateOnly today = service.Today;

        TodoItem noDueOld = service.Add("no due, old")!;
        _clock.Advance(TimeSpan.FromMinutes(1));
        TodoItem dueTomorrow = service.Add("due tomorrow", today.AddDays(1))!;
        _clock.Advance(TimeSpan.FromMinutes(1));
        TodoItem overdue = service.Add("overdue", today.AddDays(-2))!;
        _clock.Advance(TimeSpan.FromMinutes(1));
        TodoItem dueToday = service.Add("due today", today)!;
        _clock.Advance(TimeSpan.FromMinutes(1));
        TodoItem noDueNew = service.Add("no due, new")!;

        Assert.Equal(
            new[] { overdue.Id, dueToday.Id, dueTomorrow.Id, noDueNew.Id, noDueOld.Id },
            service.Active.Select(i => i.Id).ToArray());
    }

    [Fact]
    public void SetDone_MovesToCompleted_AndStampsCompletion()
    {
        var service = CreateService();
        TodoItem item = service.Add("write tests")!;

        _clock.Advance(TimeSpan.FromHours(1));
        service.SetDone(item.Id, true);

        Assert.Empty(service.Active);
        TodoItem done = Assert.Single(service.Completed);
        Assert.True(done.IsDone);
        Assert.Equal(_clock.UtcNow, done.CompletedAt);
    }

    [Fact]
    public void SetDone_False_RestoresToActive_AndClearsCompletedAt()
    {
        var service = CreateService();
        TodoItem item = service.Add("undo me")!;
        service.SetDone(item.Id, true);

        service.SetDone(item.Id, false);

        TodoItem restored = Assert.Single(service.Active);
        Assert.False(restored.IsDone);
        Assert.Null(restored.CompletedAt);
        Assert.Empty(service.Completed);
    }

    [Fact]
    public void Completed_SortsMostRecentlyCompletedFirst()
    {
        var service = CreateService();
        TodoItem first = service.Add("done first")!;
        TodoItem second = service.Add("done second")!;

        service.SetDone(first.Id, true);
        _clock.Advance(TimeSpan.FromMinutes(5));
        service.SetDone(second.Id, true);

        Assert.Equal(
            new[] { second.Id, first.Id },
            service.Completed.Select(i => i.Id).ToArray());
    }

    [Fact]
    public void Delete_RemovesItem()
    {
        var service = CreateService();
        TodoItem keep = service.Add("keep")!;
        TodoItem drop = service.Add("drop")!;

        service.Delete(drop.Id);

        TodoItem remaining = Assert.Single(service.Active);
        Assert.Equal(keep.Id, remaining.Id);
    }

    [Fact]
    public void IsOverdue_And_IsDueToday_ClassifyAgainstToday()
    {
        var service = CreateService();
        DateOnly today = service.Today;
        TodoItem overdue = service.Add("overdue", today.AddDays(-1))!;
        TodoItem dueToday = service.Add("due today", today)!;
        TodoItem future = service.Add("future", today.AddDays(1))!;
        TodoItem noDue = service.Add("no due")!;

        Assert.True(service.IsOverdue(overdue));
        Assert.False(service.IsOverdue(dueToday));
        Assert.True(service.IsDueToday(dueToday));
        Assert.False(service.IsDueToday(overdue));
        Assert.False(service.IsOverdue(future));
        Assert.False(service.IsDueToday(future));
        Assert.False(service.IsOverdue(noDue));
        Assert.False(service.IsDueToday(noDue));
    }

    [Fact]
    public void DoneItem_IsNeverOverdueOrDueToday()
    {
        var service = CreateService();
        DateOnly today = service.Today;
        TodoItem item = service.Add("was overdue", today.AddDays(-3))!;

        service.SetDone(item.Id, true);

        Assert.False(service.IsOverdue(item));
        Assert.False(service.IsDueToday(item));
    }

    [Fact]
    public void Changes_PersistAcrossReload()
    {
        DateOnly due = DateOnly.FromDateTime(_clock.UtcNow.LocalDateTime).AddDays(3);
        var first = CreateService();
        TodoItem kept = first.Add("kept", due)!;
        TodoItem done = first.Add("done")!;
        TodoItem gone = first.Add("gone")!;
        first.SetDone(done.Id, true);
        first.Delete(gone.Id);

        var second = CreateService();

        TodoItem active = Assert.Single(second.Active);
        Assert.Equal(kept.Id, active.Id);
        Assert.Equal("kept", active.Title);
        Assert.Equal(due, active.DueDate);
        TodoItem completed = Assert.Single(second.Completed);
        Assert.Equal(done.Id, completed.Id);
    }

    [Fact]
    public void Load_LegacyJsonWithoutCategoryFields_DefaultsToUncategorized()
    {
        string dir = Path.Combine(Path.GetTempPath(), "caffeine-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        string legacyJson = """
            {
              "Items": [
                { "Id": "11111111-1111-1111-1111-111111111111", "Title": "old item", "DueDate": null, "IsDone": false, "CreatedAt": "2026-01-01T00:00:00+00:00", "CompletedAt": null }
              ]
            }
            """;
        File.WriteAllText(Path.Combine(dir, "todos.json"), legacyJson);

        var service = new TodoService(_clock, new JsonStore<TodoList>("todos.json", dir));

        TodoItem loaded = Assert.Single(service.Active);
        Assert.Null(loaded.CategoryId);
        Assert.Empty(service.Categories);

        Directory.Delete(dir, recursive: true);
    }

    [Fact]
    public void Add_DefaultsToNormalPriority()
    {
        var service = CreateService();

        TodoItem item = service.Add("plain item")!;

        Assert.Equal(TodoPriority.Normal, item.Priority);
    }

    [Fact]
    public void Load_LegacyJsonWithoutPriority_DefaultsToNormal()
    {
        string dir = Path.Combine(Path.GetTempPath(), "caffeine-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        string legacyJson = """
            {
              "Items": [
                { "Id": "22222222-2222-2222-2222-222222222222", "Title": "old item", "DueDate": null, "IsDone": false, "CreatedAt": "2026-01-01T00:00:00+00:00", "CompletedAt": null }
              ]
            }
            """;
        File.WriteAllText(Path.Combine(dir, "todos.json"), legacyJson);

        var service = new TodoService(_clock, new JsonStore<TodoList>("todos.json", dir));

        TodoItem loaded = Assert.Single(service.Active);
        Assert.Equal(TodoPriority.Normal, loaded.Priority);

        Directory.Delete(dir, recursive: true);
    }

    [Fact]
    public void Add_HonorsExplicitPriority()
    {
        var service = CreateService();

        TodoItem item = service.Add("urgent item", null, TodoPriority.Urgent)!;

        Assert.Equal(TodoPriority.Urgent, item.Priority);
    }

    [Fact]
    public void SetPriority_ChangesValue_AndPersists()
    {
        var first = CreateService();
        TodoItem item = first.Add("bump me")!;

        first.SetPriority(item.Id, TodoPriority.High);

        var second = CreateService();
        Assert.Equal(TodoPriority.High, Assert.Single(second.Active).Priority);
    }

    [Fact]
    public void SetPriority_UnknownId_DoesNotThrow()
    {
        var service = CreateService();
        _ = service.Add("untouched")!;

        service.SetPriority(Guid.NewGuid(), TodoPriority.Urgent);

        Assert.Equal(TodoPriority.Normal, Assert.Single(service.Active).Priority);
    }

    [Fact]
    public void Active_OrdersByPriority_AmongUndatedItems()
    {
        var service = CreateService();

        TodoItem low = service.Add("low", null, TodoPriority.Low)!;
        _clock.Advance(TimeSpan.FromMinutes(1));
        TodoItem urgent = service.Add("urgent", null, TodoPriority.Urgent)!;
        _clock.Advance(TimeSpan.FromMinutes(1));
        TodoItem normal = service.Add("normal", null, TodoPriority.Normal)!;

        Assert.Equal(
            new[] { urgent.Id, normal.Id, low.Id },
            service.Active.Select(i => i.Id).ToArray());
    }

    [Fact]
    public void Active_PutsOverdueAboveHigherPriority()
    {
        var service = CreateService();
        DateOnly today = service.Today;

        TodoItem highNotOverdue = service.Add("high, on time", today.AddDays(3), TodoPriority.High)!;
        _clock.Advance(TimeSpan.FromMinutes(1));
        TodoItem lowOverdue = service.Add("low, overdue", today.AddDays(-1), TodoPriority.Low)!;
        _clock.Advance(TimeSpan.FromMinutes(1));
        // Undated, so never overdue (IsOverdue requires a non-null DueDate). Under the OLD
        // ordering (DueDate ?? DateOnly.MaxValue ascending) this item sorts dead last no
        // matter its priority. Under the NEW ordering it out-ranks highNotOverdue on priority
        // once both are tied on "not overdue" — which is exactly what should distinguish the
        // two comparators.
        TodoItem undatedUrgent = service.Add("urgent, undated", null, TodoPriority.Urgent)!;

        Assert.Equal(
            new[] { lowOverdue.Id, undatedUrgent.Id, highNotOverdue.Id },
            service.Active.Select(i => i.Id).ToArray());
    }

    [Fact]
    public void Active_EqualPriority_StillOrdersByDueDateThenNewest()
    {
        var service = CreateService();
        DateOnly today = service.Today;

        TodoItem noDueOld = service.Add("no due, old", null, TodoPriority.High)!;
        _clock.Advance(TimeSpan.FromMinutes(1));
        TodoItem dueLater = service.Add("due later", today.AddDays(5), TodoPriority.High)!;
        _clock.Advance(TimeSpan.FromMinutes(1));
        TodoItem dueSooner = service.Add("due sooner", today.AddDays(2), TodoPriority.High)!;
        _clock.Advance(TimeSpan.FromMinutes(1));
        TodoItem noDueNew = service.Add("no due, new", null, TodoPriority.High)!;

        Assert.Equal(
            new[] { dueSooner.Id, dueLater.Id, noDueNew.Id, noDueOld.Id },
            service.Active.Select(i => i.Id).ToArray());
    }

    [Fact]
    public void Completed_OrderingIgnoresPriority()
    {
        var service = CreateService();
        TodoItem lowFirst = service.Add("low", null, TodoPriority.Low)!;
        TodoItem urgentSecond = service.Add("urgent", null, TodoPriority.Urgent)!;

        service.SetDone(urgentSecond.Id, true);
        _clock.Advance(TimeSpan.FromMinutes(1));
        service.SetDone(lowFirst.Id, true);

        // Most recently completed first, regardless of priority.
        Assert.Equal(
            new[] { lowFirst.Id, urgentSecond.Id },
            service.Completed.Select(i => i.Id).ToArray());
    }
}
