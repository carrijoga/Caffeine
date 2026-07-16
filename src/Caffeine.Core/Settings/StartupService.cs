using Microsoft.Win32;

namespace Caffeine.Core.Settings;

/// <summary>
/// Manages the per-user "start with Windows" Run entry. The registry value is
/// the single source of truth — nothing is duplicated in settings.json, so the
/// toggle can never drift from what Windows will actually do at sign-in.
/// </summary>
public static class StartupService
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "Caffeine";

    private static string Command => $"\"{Environment.ProcessPath}\" --startup";

    public static bool IsEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath);
            return key?.GetValue(ValueName) is string;
        }
        catch
        {
            return false;
        }
    }

    public static void SetEnabled(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath);
            if (enabled)
            {
                key.SetValue(ValueName, Command);
            }
            else
            {
                key.DeleteValue(ValueName, throwOnMissingValue: false);
            }
        }
        catch
        {
        }
    }

    /// <summary>Rewrite the registered command if the exe moved since registration.</summary>
    public static void RefreshPath()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
            if (key?.GetValue(ValueName) is string current && current != Command)
            {
                key.SetValue(ValueName, Command);
            }
        }
        catch
        {
        }
    }
}
