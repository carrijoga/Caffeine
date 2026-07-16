using Caffeine.Core.Common;

namespace Caffeine.Core.Timers;

/// <summary>
/// Clock-driven stopwatch: elapsed time is computed from IClock readings, so
/// there is no tick loop to drift and tests can fast-forward a fake clock.
/// </summary>
public sealed class StopwatchEngine
{
    private readonly IClock _clock;
    private readonly List<TimeSpan> _laps = [];
    private DateTimeOffset _startedAt;
    private TimeSpan _accumulated;

    public StopwatchEngine(IClock clock)
    {
        _clock = clock;
    }

    public bool IsRunning { get; private set; }

    public IReadOnlyList<TimeSpan> Laps => _laps;

    public TimeSpan Elapsed =>
        IsRunning ? _accumulated + (_clock.UtcNow - _startedAt) : _accumulated;

    public void Start()
    {
        if (IsRunning)
        {
            return;
        }

        _startedAt = _clock.UtcNow;
        IsRunning = true;
    }

    public void Pause()
    {
        if (!IsRunning)
        {
            return;
        }

        _accumulated += _clock.UtcNow - _startedAt;
        IsRunning = false;
    }

    /// <summary>Records the current total elapsed time; ignored while stopped.</summary>
    public void Lap()
    {
        if (IsRunning)
        {
            _laps.Add(Elapsed);
        }
    }

    public void Reset()
    {
        IsRunning = false;
        _accumulated = TimeSpan.Zero;
        _laps.Clear();
    }
}
