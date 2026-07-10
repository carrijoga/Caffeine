namespace Caffeine;

/// <summary>
/// Single source of truth for whether keep-awake is active. The tray icon,
/// tray menu and window toggle all read from and write to this object.
/// Must only be touched from the UI thread (SetThreadExecutionState is
/// per-thread, so all calls have to come from the same thread anyway).
/// </summary>
public sealed class AwakeState
{
    public bool IsActive { get; private set; }

    public event Action<bool>? Changed;

    public void Set(bool active)
    {
        if (active == IsActive)
        {
            return;
        }

        if (active)
        {
            if (!KeepAwakeService.Enable())
            {
                return; // request rejected; stay inactive
            }
        }
        else
        {
            KeepAwakeService.Disable();
        }

        IsActive = active;
        Changed?.Invoke(active);
    }

    public void Toggle() => Set(!IsActive);
}
