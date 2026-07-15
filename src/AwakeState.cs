using Caffeine.Core.Common;

namespace Caffeine;

/// <summary>
/// Single source of truth for whether keep-awake is active. The tray icon,
/// tray menu and window toggle all read from and write to this object.
/// Must only be touched from the UI thread (SetThreadExecutionState is
/// per-thread, so all calls have to come from the same thread anyway).
/// </summary>
public sealed class AwakeState
{
    private readonly Func<IAppTimer> _timerFactory;
    private IAppTimer? _timer;

    public AwakeState(Func<IAppTimer> timerFactory)
    {
        _timerFactory = timerFactory;
    }

    public bool IsActive { get; private set; }

    /// <summary>Hold the display on too; when false only the system stays awake.</summary>
    public bool KeepScreenOn { get; set; } = true;

    /// <summary>Auto-off duration for timed mode; null keeps awake indefinitely.</summary>
    public TimeSpan? Interval { get; set; }

    /// <summary>When the current timed session ends; null when inactive or indefinite.</summary>
    public DateTimeOffset? SessionEndsAt { get; private set; }

    public event Action<bool>? Changed;

    public void Set(bool active)
    {
        if (active == IsActive)
        {
            return;
        }

        if (active)
        {
            if (!KeepAwakeService.Enable(KeepScreenOn))
            {
                return; // request rejected; stay inactive
            }

            StartTimerIfTimed();
        }
        else
        {
            KeepAwakeService.Disable();
            StopTimer();
        }

        IsActive = active;
        Changed?.Invoke(active);
    }

    public void Toggle() => Set(!IsActive);

    /// <summary>
    /// Re-issues the power request and restarts the timer after
    /// <see cref="KeepScreenOn"/> or <see cref="Interval"/> changed while active.
    /// </summary>
    public void Reapply()
    {
        if (!IsActive)
        {
            return;
        }

        KeepAwakeService.Enable(KeepScreenOn);
        StopTimer();
        StartTimerIfTimed();
    }

    private void StartTimerIfTimed()
    {
        if (Interval is not { } interval)
        {
            return;
        }

        SessionEndsAt = DateTimeOffset.Now + interval;

        _timer ??= CreateTimer();
        _timer.Interval = interval;
        _timer.Start();
    }

    private void StopTimer()
    {
        SessionEndsAt = null;
        _timer?.Stop();
    }

    private IAppTimer CreateTimer()
    {
        var timer = _timerFactory();
        timer.IsRepeating = false;
        timer.Tick += () => Set(false);
        return timer;
    }
}
