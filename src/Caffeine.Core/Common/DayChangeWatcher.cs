namespace Caffeine.Core.Common;

/// <summary>
/// Raises <see cref="DayChanged"/> when the local calendar day rolls over.
/// UI-free and clock-driven: the owner calls <see cref="Poll"/> on a cadence
/// (e.g. a DispatcherTimer) and re-reads "today" when the event fires, so a
/// long-running window doesn't show yesterday's counts, streaks, and dots.
/// Multiple rollovers between polls coalesce into a single event.
/// </summary>
public sealed class DayChangeWatcher
{
    private readonly IClock _clock;
    private DateOnly _lastSeen;

    public DayChangeWatcher(IClock clock)
    {
        _clock = clock;
        _lastSeen = Today();
    }

    /// <summary>Raised once when a <see cref="Poll"/> observes a new local date.</summary>
    public event Action? DayChanged;

    /// <summary>Reads the clock; raises <see cref="DayChanged"/> if the local date advanced.</summary>
    public void Poll()
    {
        DateOnly current = Today();
        if (current != _lastSeen)
        {
            _lastSeen = current;
            DayChanged?.Invoke();
        }
    }

    private DateOnly Today() => DateOnly.FromDateTime(_clock.UtcNow.LocalDateTime);
}
