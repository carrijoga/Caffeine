using Caffeine.Core.Common;
using Xunit;

namespace Caffeine.Core.Tests;

internal sealed class TestDoc
{
    public string Name { get; set; } = string.Empty;

    public int Count { get; set; }
}

public sealed class JsonStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(
        Path.GetTempPath(), "caffeine-tests-" + Guid.NewGuid().ToString("N"));

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
    public void Load_MissingFile_ReturnsFreshDocument()
    {
        var store = new JsonStore<TestDoc>("doc.json", _dir);

        var doc = store.Load();

        Assert.Equal(string.Empty, doc.Name);
        Assert.Equal(0, doc.Count);
    }

    [Fact]
    public void SaveThenLoad_RoundTrips()
    {
        var store = new JsonStore<TestDoc>("doc.json", _dir);

        store.Save(new TestDoc { Name = "espresso", Count = 3 });
        var doc = store.Load();

        Assert.Equal("espresso", doc.Name);
        Assert.Equal(3, doc.Count);
    }

    [Fact]
    public void Save_OverwritesExistingFile()
    {
        var store = new JsonStore<TestDoc>("doc.json", _dir);

        store.Save(new TestDoc { Name = "first" });
        store.Save(new TestDoc { Name = "second" });

        Assert.Equal("second", store.Load().Name);
        Assert.False(File.Exists(Path.Combine(_dir, "doc.json.tmp")));
    }

    [Fact]
    public void Load_CorruptFile_BacksUpAndReturnsFreshDocument()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, "doc.json"), "{ this is not json !!!");
        var store = new JsonStore<TestDoc>("doc.json", _dir);

        var doc = store.Load();

        Assert.Equal(0, doc.Count);
        Assert.True(File.Exists(Path.Combine(_dir, "doc.json.corrupt.bak")));
        Assert.False(File.Exists(Path.Combine(_dir, "doc.json")));
    }
}
