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
        ModeCombo.SelectedIndex = _app.Settings.TimedMode ? 1 : 0;
        HoursBox.Value = _app.Settings.IntervalHours;
        MinutesBox.Value = _app.Settings.IntervalMinutes;
        IntervalCard.Visibility = _app.Settings.TimedMode ? Visibility.Visible : Visibility.Collapsed;
        KeepScreenOnToggle.IsOn = _app.Settings.KeepScreenOn;
        RememberStateToggle.IsOn = _app.Settings.RememberState;
        RunInTrayToggle.IsOn = _app.Settings.RunInSystemTray;
        StartWithWindowsToggle.IsOn = StartupService.IsEnabled();
        _initializing = false;
    }

    private void ModeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        bool timed = ModeCombo.SelectedIndex == 1;
        IntervalCard.Visibility = timed ? Visibility.Visible : Visibility.Collapsed;

        if (!_initializing)
        {
            ApplyAwakeMode();
        }
    }

    private void Interval_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (!_initializing)
        {
            ApplyAwakeMode();
        }
    }

    private void ApplyAwakeMode()
    {
        // NumberBox reports NaN while the text field is empty; keep the last saved value.
        int hours = double.IsNaN(HoursBox.Value) ? _app.Settings.IntervalHours : (int)HoursBox.Value;
        int minutes = double.IsNaN(MinutesBox.Value) ? _app.Settings.IntervalMinutes : (int)MinutesBox.Value;
        _app.SetAwakeMode(ModeCombo.SelectedIndex == 1, hours, minutes);
    }

    private void KeepScreenOnToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (!_initializing)
        {
            _app.SetKeepScreenOn(KeepScreenOnToggle.IsOn);
        }
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

    private void StartWithWindowsToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (!_initializing)
        {
            StartupService.SetEnabled(StartWithWindowsToggle.IsOn);
        }
    }
}
