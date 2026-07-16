using Caffeine.Core.Common;

namespace Caffeine.Core.Settings;

/// <summary>
/// Loads and saves <see cref="AppSettings"/> to %LocalAppData%\Caffeine\settings.json.
/// The app is unpackaged, so there is no ApplicationData store to use instead.
/// </summary>
public static class SettingsService
{
    private static readonly JsonStore<AppSettings> Store = new("settings.json");

    public static AppSettings Load() => Store.Load();

    public static void Save(AppSettings settings) => Store.Save(settings);
}
