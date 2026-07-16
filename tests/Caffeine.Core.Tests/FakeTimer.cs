using Caffeine.Core.Common;

namespace Caffeine.Core.Tests;

/// <summary>Hand-fired IAppTimer for tests.</summary>
internal sealed class FakeTimer : IAppTimer
{
    public TimeSpan Interval { get; set; }

    public bool IsRepeating { get; set; } = true;

    public bool IsRunning { get; private set; }

    public event Action? Tick;

    public void Start() => IsRunning = true;

    public void Stop() => IsRunning = false;

    public void Fire()
    {
        if (!IsRepeating)
        {
            IsRunning = false;
        }

        Tick?.Invoke();
    }
}
