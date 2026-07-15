namespace Caffeine.Core.Common;

/// <summary>
/// Minimal timer abstraction so Core services can schedule work without
/// referencing a UI framework. The app supplies a DispatcherQueue-backed
/// implementation; tests supply a fake they fire by hand.
/// </summary>
public interface IAppTimer
{
    TimeSpan Interval { get; set; }

    bool IsRepeating { get; set; }

    event Action? Tick;

    void Start();

    void Stop();
}
