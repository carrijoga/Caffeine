namespace Caffeine.Core.Settings;

/// <summary>
/// User-configurable settings, persisted as JSON by <see cref="SettingsService"/>.
/// </summary>
public sealed class AppSettings
{
    /// <summary>Restore the keep-awake toggle position on the next launch.</summary>
    public bool RememberState { get; set; }

    /// <summary>Keep a tray icon and hide to tray on window close instead of exiting.</summary>
    public bool RunInSystemTray { get; set; } = true;

    /// <summary>Last known toggle position, used when <see cref="RememberState"/> is on.</summary>
    public bool LastAwakeActive { get; set; } = true;

    /// <summary>Also keep the display on while awake; off lets the screen sleep normally.</summary>
    public bool KeepScreenOn { get; set; } = true;

    /// <summary>Turn Awake off automatically after <see cref="IntervalHours"/>:<see cref="IntervalMinutes"/>.</summary>
    public bool TimedMode { get; set; }

    public int IntervalHours { get; set; } = 1;

    public int IntervalMinutes { get; set; }
}
