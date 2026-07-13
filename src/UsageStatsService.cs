using System.Text.Json;

namespace Caffeine;

/// <summary>
/// Loads and saves <see cref="UsageStats"/> to %LocalAppData%\Caffeine\usage.json,
/// mirroring <see cref="SettingsService"/>. Stats are best-effort: any I/O
/// failure falls back to zeros / is ignored.
/// </summary>
public static class UsageStatsService
{
    private static readonly string StatsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Caffeine",
        "usage.json");

    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public static UsageStats Load()
    {
        try
        {
            if (File.Exists(StatsPath))
            {
                return JsonSerializer.Deserialize<UsageStats>(
                    File.ReadAllText(StatsPath), Options) ?? new UsageStats();
            }
        }
        catch
        {
        }

        return new UsageStats();
    }

    public static void Save(UsageStats stats)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(StatsPath)!);
            File.WriteAllText(StatsPath, JsonSerializer.Serialize(stats, Options));
        }
        catch
        {
        }
    }
}
