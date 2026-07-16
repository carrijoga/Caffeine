using Caffeine.Core.Timers;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Caffeine.Pages;

public sealed partial class TimersPage : Page
{
    private readonly TimersService _timers;
    private bool _loadingConfig;

    public TimersPage()
    {
        _timers = ((App)Application.Current).Timers;
        InitializeComponent();

        LoadPomodoroConfig();

        _timers.Ticked += Refresh;
        Unloaded += (_, _) => _timers.Ticked -= Refresh;

        Refresh();
        RefreshLaps();
    }

    private void TabBar_SelectionChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
    {
        PomodoroPanel.Visibility = TabBar.SelectedItem == PomodoroTab ? Visibility.Visible : Visibility.Collapsed;
        CountdownPanel.Visibility = TabBar.SelectedItem == CountdownTab ? Visibility.Visible : Visibility.Collapsed;
        StopwatchPanel.Visibility = TabBar.SelectedItem == StopwatchTab ? Visibility.Visible : Visibility.Collapsed;
    }

    // ----- Pomodoro -----

    private void LoadPomodoroConfig()
    {
        _loadingConfig = true;
        WorkMinutesBox.Value = _timers.Config.Pomodoro.WorkMinutes;
        ShortBreakMinutesBox.Value = _timers.Config.Pomodoro.ShortBreakMinutes;
        LongBreakMinutesBox.Value = _timers.Config.Pomodoro.LongBreakMinutes;
        CyclesBox.Value = _timers.Config.Pomodoro.CyclesPerLongBreak;
        _loadingConfig = false;
    }

    private void PomodoroConfig_Changed(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        // NumberBox reports NaN while the user is clearing/typing in a field.
        if (_loadingConfig
            || double.IsNaN(WorkMinutesBox.Value) || double.IsNaN(ShortBreakMinutesBox.Value)
            || double.IsNaN(LongBreakMinutesBox.Value) || double.IsNaN(CyclesBox.Value))
        {
            return;
        }

        _timers.SavePomodoroConfig(
            (int)WorkMinutesBox.Value,
            (int)ShortBreakMinutesBox.Value,
            (int)LongBreakMinutesBox.Value,
            (int)CyclesBox.Value);
        Refresh();
    }

    private void PomodoroStartPause_Click(object sender, RoutedEventArgs e)
    {
        if (_timers.Pomodoro.IsRunning)
        {
            _timers.PausePomodoro();
        }
        else
        {
            _timers.StartPomodoro();
        }

        Refresh();
    }

    private void PomodoroSkip_Click(object sender, RoutedEventArgs e)
    {
        _timers.SkipPomodoro();
        Refresh();
    }

    private void PomodoroReset_Click(object sender, RoutedEventArgs e)
    {
        _timers.ResetPomodoro();
        Refresh();
    }

    // ----- Countdown -----

    private void CountdownPreset_Click(object sender, RoutedEventArgs e)
    {
        int minutes = int.Parse((string)((Button)sender).Tag);
        CountdownHoursBox.Value = minutes / 60;
        CountdownMinutesBox.Value = minutes % 60;
        CountdownSecondsBox.Value = 0;
        _timers.StartCountdown(TimeSpan.FromMinutes(minutes));
        Refresh();
    }

    private void CountdownStartPause_Click(object sender, RoutedEventArgs e)
    {
        var countdown = _timers.Countdown;
        if (countdown.IsRunning)
        {
            _timers.PauseCountdown();
        }
        else if (countdown.Remaining > TimeSpan.Zero && countdown.Remaining < countdown.Duration)
        {
            _timers.ResumeCountdown();
        }
        else
        {
            _timers.StartCountdown(new TimeSpan(
                BoxValue(CountdownHoursBox), BoxValue(CountdownMinutesBox), BoxValue(CountdownSecondsBox)));
        }

        Refresh();
    }

    private static int BoxValue(NumberBox box) => double.IsNaN(box.Value) ? 0 : (int)box.Value;

    private void CountdownReset_Click(object sender, RoutedEventArgs e)
    {
        _timers.ResetCountdown();
        Refresh();
    }

    // ----- Stopwatch -----

    private void StopwatchStartPause_Click(object sender, RoutedEventArgs e)
    {
        if (_timers.Stopwatch.IsRunning)
        {
            _timers.PauseStopwatch();
        }
        else
        {
            _timers.StartStopwatch();
        }

        Refresh();
    }

    private void StopwatchLap_Click(object sender, RoutedEventArgs e)
    {
        _timers.LapStopwatch();
        RefreshLaps();
    }

    private void StopwatchReset_Click(object sender, RoutedEventArgs e)
    {
        _timers.ResetStopwatch();
        RefreshLaps();
        Refresh();
    }

    /// <summary>Laps only change on Lap/Reset, so the list rebuilds only there — not per tick.</summary>
    private void RefreshLaps() =>
        LapsList.ItemsSource = _timers.Stopwatch.Laps
            .Select((lap, i) => $"Lap {i + 1}   {TimersDisplay.Whole(lap)}")
            .Reverse()
            .ToList();

    // ----- Shared refresh (also driven by TimersService.Ticked) -----

    private void Refresh()
    {
        var pomodoro = _timers.Pomodoro;
        PomodoroPhaseText.Text = TimersDisplay.PhaseName(pomodoro.Phase);
        PomodoroTimeText.Text = pomodoro.Phase == PomodoroPhase.Idle
            ? TimersDisplay.Whole(TimeSpan.FromMinutes(_timers.Config.Pomodoro.WorkMinutes))
            : TimersDisplay.Countdown(pomodoro.Remaining);
        PomodoroSessionsText.Text = pomodoro.CompletedWorkSessions == 1
            ? "1 focus session completed"
            : $"{pomodoro.CompletedWorkSessions} focus sessions completed";
        PomodoroStartPauseButton.Content = pomodoro.IsRunning
            ? "Pause"
            : pomodoro.Phase == PomodoroPhase.Idle ? "Start" : "Resume";
        PomodoroSkipButton.IsEnabled = pomodoro.Phase != PomodoroPhase.Idle;

        var countdown = _timers.Countdown;
        CountdownTimeText.Text = TimersDisplay.Countdown(countdown.Remaining);
        CountdownStartPauseButton.Content = countdown.IsRunning
            ? "Pause"
            : countdown.Remaining > TimeSpan.Zero && countdown.Remaining < countdown.Duration
                ? "Resume"
                : "Start";

        var stopwatch = _timers.Stopwatch;
        StopwatchTimeText.Text = TimersDisplay.Whole(stopwatch.Elapsed);
        StopwatchStartPauseButton.Content = stopwatch.IsRunning ? "Pause" : "Start";
        StopwatchLapButton.IsEnabled = stopwatch.IsRunning;

        // Spec: the running timer's tab shows a badge.
        PomodoroTab.Text = pomodoro.IsRunning ? "Pomodoro ●" : "Pomodoro";
        CountdownTab.Text = countdown.IsRunning ? "Countdown ●" : "Countdown";
        StopwatchTab.Text = stopwatch.IsRunning ? "Stopwatch ●" : "Stopwatch";
    }
}
