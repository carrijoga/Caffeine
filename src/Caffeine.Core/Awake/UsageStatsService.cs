using Caffeine.Core.Common;

namespace Caffeine.Core.Awake;

/// <summary>
/// Loads and saves <see cref="UsageStats"/> to %LocalAppData%\Caffeine\usage.json,
/// mirroring <see cref="Caffeine.Core.Settings.SettingsService"/>.
/// </summary>
public static class UsageStatsService
{
    private static readonly JsonStore<UsageStats> Store = new("usage.json");

    public static UsageStats Load() => Store.Load();

    public static void Save(UsageStats stats) => Store.Save(stats);
}
