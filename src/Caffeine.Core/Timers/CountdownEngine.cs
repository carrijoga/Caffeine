using Caffeine.Core.Common;

namespace Caffeine.Core.Timers;

/// <summary>
/// Clock-driven countdown. Remaining time is computed from IClock readings;
/// a periodic Update() call (the UI's refresh tick) detects expiry and fires
/// Completed exactly once.
/// </summary>
public sealed class CountdownEngine
{
    private readonly IClock _clock;
    private DateTimeOffset _startedAt;
    private TimeSpan _remainingAtStart;

    public CountdownEngine(IClock clock)
    {
        _clock = clock;
    }

    public TimeSpan Duration { get; private set; }

    public bool IsRunning { get; private set; }

    public event Action? Completed;

    public TimeSpan Remaining
    {
        get
        {
            var remaining = IsRunning
                ? _remainingAtStart - (_clock.UtcNow - _startedAt)
                : _remainingAtStart;
            return remaining < TimeSpan.Zero ? TimeSpan.Zero : remaining;
        }
    }

    public void Start(TimeSpan duration)
    {
        Duration = duration;
        _remainingAtStart = duration;
        _startedAt = _clock.UtcNow;
        IsRunning = duration > TimeSpan.Zero;
    }

    public void Pause()
    {
        if (!IsRunning)
        {
            return;
        }

        _remainingAtStart = Remaining;
        IsRunning = false;
    }

    public void Resume()
    {
        if (IsRunning || _remainingAtStart <= TimeSpan.Zero)
        {
            return;
        }

        _startedAt = _clock.UtcNow;
        IsRunning = true;
    }

    /// <summary>Stops and restores the last-started duration.</summary>
    public void Reset()
    {
        IsRunning = false;
        _remainingAtStart = Duration;
    }

    /// <summary>Detects expiry; fires Completed once. Call periodically while running.</summary>
    public void Update()
    {
        if (IsRunning && Remaining == TimeSpan.Zero)
        {
            IsRunning = false;
            _remainingAtStart = TimeSpan.Zero;
            Completed?.Invoke();
        }
    }
}
