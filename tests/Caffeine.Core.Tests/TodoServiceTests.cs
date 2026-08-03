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
    public void AddCategory_TrimsName_AndStores()
    {
        var service = CreateService();

        TodoCategory? category = service.AddCategory("  Work  ", "#FF0000");

        Assert.NotNull(category);
        Assert.Equal("Work", category.Name);
        Assert.Equal("#FF0000", category.ColorHex);
        Assert.Contains(category, service.Categories);
    }

    [Fact]
    public void AddCategory_WhitespaceName_IsIgnored()
    {
        var service = CreateService();

        Assert.Null(service.AddCategory("   ", "#FF0000"));
        Assert.Empty(service.Categories);
    }

    [Fact]
    public void RenameCategory_UpdatesNameAndColor()
    {
        var service = CreateService();
        TodoCategory category = service.AddCategory("Work", "#FF0000")!;

        service.RenameCategory(category.Id, "Personal", "#00FF00");

        TodoCategory updated = Assert.Single(service.Categories);
        Assert.Equal("Personal", updated.Name);
        Assert.Equal("#00FF00", updated.ColorHex);
    }

    [Fact]
    public void RenameCategory_UnknownId_NoOps()
    {
        var service = CreateService();
        service.AddCategory("Work", "#FF0000");

        service.RenameCategory(Guid.NewGuid(), "Nope", "#000000");

        TodoCategory unchanged = Assert.Single(service.Categories);
        Assert.Equal("Work", unchanged.Name);
    }

    [Fact]
    public void CountByCategory_CountsAssignedTodos()
    {
        var service = CreateService();
        TodoCategory work = service.AddCategory("Work", "#FF0000")!;
        TodoItem a = service.Add("task a")!;
        TodoItem b = service.Add("task b")!;
        service.SetCategory(a.Id, work.Id);
        service.SetCategory(b.Id, work.Id);

        Assert.Equal(2, service.CountByCategory(work.Id));
        Assert.Equal(0, service.CountByCategory(Guid.NewGuid()));
    }

    [Fact]
    public void DeleteCategory_RemovesCategory_AndCascadesToAssignedTodos()
    {
        var service = CreateService();
        TodoCategory work = service.AddCategory("Work", "#FF0000")!;
        TodoCategory personal = service.AddCategory("Personal", "#00FF00")!;
        TodoItem workItem = service.Add("work task")!;
        TodoItem personalItem = service.Add("personal task")!;
        TodoItem unassigned = service.Add("no category")!;
        service.SetCategory(workItem.Id, work.Id);
        service.SetCategory(personalItem.Id, personal.Id);

        service.DeleteCategory(work.Id);

        Assert.DoesNotContain(service.Categories, c => c.Id == work.Id);
        Assert.Contains(service.Categories, c => c.Id == personal.Id);
        Assert.DoesNotContain(service.Active, i => i.Id == workItem.Id);
        Assert.Contains(service.Active, i => i.Id == personalItem.Id);
        Assert.Contains(service.Active, i => i.Id == unassigned.Id);
    }

    [Fact]
    public void DeleteCategory_UnknownId_NoOps()
    {
        var service = CreateService();
        service.AddCategory("Work", "#FF0000");

        service.DeleteCategory(Guid.NewGuid());

        Assert.Single(service.Categories);
    }

    [Fact]
    public void SetCategory_AssignsAndClears()
    {
        var service = CreateService();
        TodoCategory work = service.AddCategory("Work", "#FF0000")!;
        TodoItem item = service.Add("task")!;

        service.SetCategory(item.Id, work.Id);
        Assert.Equal(work.Id, service.Active.Single().CategoryId);

        service.SetCategory(item.Id, null);
        Assert.Null(service.Active.Single().CategoryId);
    }

    [Fact]
    public void SetCategory_UnknownTodoId_NoOps()
    {
        var service = CreateService();
        TodoCategory work = service.AddCategory("Work", "#FF0000")!;

        service.SetCategory(Guid.NewGuid(), work.Id);

        Assert.Empty(service.Active);
    }

    [Fact]
    public void Add_WithCategoryId_AssignsCategory()
    {
        var service = CreateService();
        TodoCategory work = service.AddCategory("Work", "#FF0000")!;

        TodoItem item = service.Add("task", categoryId: work.Id)!;

        Assert.Equal(work.Id, item.CategoryId);
    }

    [Fact]
    public void CategoryAndCategoryAssignment_PersistAcrossReload()
    {
        var first = CreateService();
        TodoCategory work = first.AddCategory("Work", "#FF0000")!;
        TodoItem item = first.Add("task", categoryId: work.Id)!;

        var second = CreateService();

        TodoCategory reloadedCategory = Assert.Single(second.Categories);
        Assert.Equal("Work", reloadedCategory.Name);
        Assert.Equal("#FF0000", reloadedCategory.ColorHex);
        TodoItem reloadedItem = Assert.Single(second.Active);
        Assert.Equal(work.Id, reloadedItem.CategoryId);
    }
}
