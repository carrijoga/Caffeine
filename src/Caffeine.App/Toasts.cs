using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;

namespace Caffeine;

/// <summary>Best-effort toast notifications; a failing shell must never take the app down.</summary>
internal static class Toasts
{
    public static void Show(string title, string body)
    {
        try
        {
            AppNotificationManager.Default.Show(
                new AppNotificationBuilder()
                    .AddText(title)
                    .AddText(body)
                    .BuildNotification());
        }
        catch
        {
        }
    }
}
