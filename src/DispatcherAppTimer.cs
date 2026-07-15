using Caffeine.Core.Common;
using Microsoft.UI.Dispatching;

namespace Caffeine;

/// <summary>IAppTimer backed by a DispatcherQueueTimer; must be created on the UI thread.</summary>
public sealed class DispatcherAppTimer : IAppTimer
{
    private readonly DispatcherQueueTimer _timer;

    public DispatcherAppTimer()
    {
        _timer = DispatcherQueue.GetForCurrentThread().CreateTimer();
        _timer.Tick += (_, _) => Tick?.Invoke();
    }

    public TimeSpan Interval
    {
        get => _timer.Interval;
        set => _timer.Interval = value;
    }

    public bool IsRepeating
    {
        get => _timer.IsRepeating;
        set => _timer.IsRepeating = value;
    }

    public event Action? Tick;

    public void Start() => _timer.Start();

    public void Stop() => _timer.Stop();
}
