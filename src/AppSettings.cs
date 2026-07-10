namespace Caffeine;

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
}
