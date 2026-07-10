using System.Runtime.InteropServices;

namespace Caffeine;

/// <summary>
/// Tells the Windows power manager to keep the display (and system) awake via
/// SetThreadExecutionState. One call to enable, one to disable — no timers, no
/// simulated input, no idle CPU cost. Windows clears the request automatically
/// if the process exits, so a crash can never leave the machine stuck awake.
/// </summary>
internal static class KeepAwakeService
{
    [Flags]
    private enum ExecutionState : uint
    {
        SystemRequired = 0x00000001,
        DisplayRequired = 0x00000002,
        Continuous = 0x80000000,
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern ExecutionState SetThreadExecutionState(ExecutionState esFlags);

    public static bool IsEnabled { get; private set; }

    /// <returns>false if the power request was rejected (practically never happens).</returns>
    public static bool Enable()
    {
        var result = SetThreadExecutionState(
            ExecutionState.Continuous | ExecutionState.DisplayRequired | ExecutionState.SystemRequired);
        IsEnabled = result != 0;
        return IsEnabled;
    }

    public static void Disable()
    {
        SetThreadExecutionState(ExecutionState.Continuous);
        IsEnabled = false;
    }
}
