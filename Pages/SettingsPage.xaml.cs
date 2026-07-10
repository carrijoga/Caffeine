using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Caffeine.Pages;

public sealed partial class SettingsPage : Page
{
    private readonly App _app;
    private bool _initializing;

    public SettingsPage()
    {
        _app = (App)Application.Current;
        InitializeComponent();

        _initializing = true;
        RememberStateToggle.IsOn = _app.Settings.RememberState;
        RunInTrayToggle.IsOn = _app.Settings.RunInSystemTray;
        _initializing = false;
    }

    private void RememberStateToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (!_initializing)
        {
            _app.SetRememberState(RememberStateToggle.IsOn);
        }
    }

    private void RunInTrayToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (!_initializing)
        {
            _app.SetRunInSystemTray(RunInTrayToggle.IsOn);
        }
    }
}
