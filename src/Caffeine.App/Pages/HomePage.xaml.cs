using Caffeine.Core.Awake;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace Caffeine.Pages;

public sealed partial class HomePage : Page
{
    private readonly AwakeState _state;
    private readonly TimersService _timers;
    private bool _updatingToggle;

    public HomePage()
    {
        var app = (App)Application.Current;
        _state = app.State;
        _timers = app.Timers;
        InitializeComponent();

        _state.Changed += OnStateChanged;
        _timers.Ticked += RefreshTimersCard;
        Unloaded += (_, _) =>
        {
            _state.Changed -= OnStateChanged;
            _timers.Ticked -= RefreshTimersCard;
        };

        OnStateChanged(_state.IsActive);
        RefreshTimersCard();
    }

    private void OnStateChanged(bool active)
    {
        _updatingToggle = true;
        AwakeToggle.IsOn = active;
        _updatingToggle = false;

        BadgeActive.Visibility = active ? Visibility.Visible : Visibility.Collapsed;
        BadgeInactive.Visibility = active ? Visibility.Collapsed : Visibility.Visible;
        AwakeCaption.Text = active
            ? _state.KeepScreenOn ? "Keeping your PC and screen awake" : "Keeping your PC awake"
            : "Your PC can sleep";
    }

    private void AwakeToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (!_updatingToggle)
        {
            _state.Set(AwakeToggle.IsOn);
        }
    }

    private void AwakeToggle_Tapped(object sender, TappedRoutedEventArgs e) =>
        e.Handled = true;

    private void OpenAwake_Click(object sender, RoutedEventArgs e) =>
        ((App)Application.Current).NavigateTo("awake");

    private void RefreshTimersCard()
    {
        TimersCaption.Text =
            _timers.Pomodoro.IsRunning
                ? $"Pomodoro — {TimersDisplay.PhaseName(_timers.Pomodoro.Phase)}, {TimersDisplay.Countdown(_timers.Pomodoro.Remaining)} left"
            : _timers.Countdown.IsRunning
                ? $"Countdown — {TimersDisplay.Countdown(_timers.Countdown.Remaining)} left"
            : _timers.Stopwatch.IsRunning
                ? $"Stopwatch — {TimersDisplay.Whole(_timers.Stopwatch.Elapsed)}"
            : "No timer running";
        StartPomodoroButton.IsEnabled = !_timers.Pomodoro.IsRunning;
    }

    private void StartPomodoro_Click(object sender, RoutedEventArgs e)
    {
        _timers.StartPomodoro();
        RefreshTimersCard();
    }

    private void StartPomodoro_Tapped(object sender, TappedRoutedEventArgs e) =>
        e.Handled = true;

    private void OpenTimers_Click(object sender, RoutedEventArgs e) =>
        ((App)Application.Current).NavigateTo("timers");
}
