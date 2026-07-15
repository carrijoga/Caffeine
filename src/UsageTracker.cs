using Caffeine.Core.Common;

namespace Caffeine;

/// <summary>
/// Accumulates how long keep-awake has been active and maintains the daily
/// killstreak. Listens to <see cref="AwakeState.Changed"/> and flushes elapsed
/// time to disk once a minute while active, so a crash loses at most ~1 minute.
/// The flush timer must be a UI-thread timer (created on the UI thread).
/// </summary>
public sealed class UsageTracker
{
    private readonly UsageStats _stats;
    private readonly IAppTimer _flushTimer;
    private DateTimeOffset _sessionStart;
    private DateTimeOffset _lastFlush;
    private bool _active;

    public UsageTracker(AwakeState state, IAppTimer flushTimer)
    {
        _stats = UsageStatsService.Load();

        // A streak only survives overnight; if the last active day is before
        // yesterday the chain is already broken, so show 0 until the next use.
        var today = DateOnly.FromDateTime(DateTime.Now);
        if (_stats.LastActiveDay is { } last && last < today.AddDays(-1))
        {
            _stats.CurrentStreakDays = 0;
        }

        _flushTimer = flushTimer;
        _flushTimer.Interval = TimeSpan.FromMinutes(1);
        _flushTimer.IsRepeating = true;
        _flushTimer.Tick += Flush;

        state.Changed += OnStateChanged;
    }

    /// <summary>Lifetime awake time, including the not-yet-flushed live span.</summary>
    public TimeSpan TotalAwake =>
        TimeSpan.FromSeconds(_stats.TotalAwakeSeconds) + PendingSpan;

    /// <summary>Elapsed time of the running session, or zero when inactive.</summary>
    public TimeSpan CurrentSession =>
        _active ? DateTimeOffset.Now - _sessionStart : TimeSpan.Zero;

    public bool IsSessionActive => _active;

    public int CurrentStreakDays => _stats.CurrentStreakDays;

    public int BestStreakDays => _stats.BestStreakDays;

    private TimeSpan PendingSpan =>
        _active ? DateTimeOffset.Now - _lastFlush : TimeSpan.Zero;

    private void OnStateChanged(bool active)
    {
        if (active)
        {
            _active = true;
            _sessionStart = _lastFlush = DateTimeOffset.Now;
            MarkActiveToday();
            UsageStatsService.Save(_stats);
            _flushTimer.Start();
        }
        else
        {
            _flushTimer.Stop();
            Flush();
            _active = false;
        }
    }

    private void Flush()
    {
        if (!_active)
        {
            return;
        }

        var now = DateTimeOffset.Now;
        _stats.TotalAwakeSeconds += (now - _lastFlush).TotalSeconds;
        _lastFlush = now;
        MarkActiveToday(); // day may have rolled over during a long session
        UsageStatsService.Save(_stats);
    }

    private void MarkActiveToday()
    {
        var today = DateOnly.FromDateTime(DateTime.Now);
        if (_stats.LastActiveDay == today)
        {
            return;
        }

        _stats.CurrentStreakDays = _stats.LastActiveDay == today.AddDays(-1)
            ? _stats.CurrentStreakDays + 1
            : 1;
        _stats.BestStreakDays = Math.Max(_stats.BestStreakDays, _stats.CurrentStreakDays);
        _stats.LastActiveDay = today;
    }
}
