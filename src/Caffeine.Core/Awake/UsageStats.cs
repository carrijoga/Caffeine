namespace Caffeine.Core.Awake;

/// <summary>
/// Lifetime usage statistics, persisted as JSON by <see cref="UsageStatsService"/>.
/// </summary>
public sealed class UsageStats
{
    /// <summary>Total time keep-awake has been active, across all sessions.</summary>
    public double TotalAwakeSeconds { get; set; }

    /// <summary>Consecutive days (ending on <see cref="LastActiveDay"/>) with any awake time.</summary>
    public int CurrentStreakDays { get; set; }

    /// <summary>Longest streak ever reached.</summary>
    public int BestStreakDays { get; set; }

    /// <summary>Most recent day keep-awake was active, local time.</summary>
    public DateOnly? LastActiveDay { get; set; }
}
