using System.Text.Json;

namespace Caffeine.Core.Settings;

/// <summary>
/// Loads and saves <see cref="AppSettings"/> to %LocalAppData%\Caffeine\settings.json.
/// The app is unpackaged, so there is no ApplicationData store to use instead.
/// Settings are a convenience: any I/O failure falls back to defaults / is ignored.
/// </summary>
public static class SettingsService
{
    private static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Caffeine",
        "settings.json");

    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                return JsonSerializer.Deserialize<AppSettings>(
                    File.ReadAllText(SettingsPath), Options) ?? new AppSettings();
            }
        }
        catch
        {
        }

        return new AppSettings();
    }

    public static void Save(AppSettings settings)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
            File.WriteAllText(SettingsPath, JsonSerializer.Serialize(settings, Options));
        }
        catch
        {
        }
    }
}
