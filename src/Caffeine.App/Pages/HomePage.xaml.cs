using Caffeine.Core.Awake;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Caffeine.Pages;

public sealed partial class HomePage : Page
{
    private readonly AwakeState _state;
    private readonly UsageTracker _usage;
    private readonly DispatcherTimer _refreshTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private bool _updatingToggle;

    public HomePage()
    {
        var app = (App)Application.Current;
        _state = app.State;
        _usage = app.Usage;
        InitializeComponent();

        _refreshTimer.Tick += (_, _) => RefreshStats();
        _state.Changed += OnStateChanged;
        Loaded += (_, _) => _refreshTimer.Start();
        Unloaded += (_, _) =>
        {
            _refreshTimer.Stop();
            _state.Changed -= OnStateChanged;
        };

        OnStateChanged(_state.IsActive);
    }

    private void OnStateChanged(bool active)
    {
        _updatingToggle = true;
        AwakeToggle.IsOn = active;
        _updatingToggle = false;

        BadgeActive.Visibility = active ? Visibility.Visible : Visibility.Collapsed;
        BadgeInactive.Visibility = active ? Visibility.Collapsed : Visibility.Visible;
        UpdateStatusText();

        RefreshStats();
    }

    private void UpdateStatusText()
    {
        if (!_state.IsActive)
        {
            StatusTitle.Text = "Your PC can sleep";
            StatusCaption.Text = "Normal power and lock settings apply";
            return;
        }

        StatusTitle.Text = _state.KeepScreenOn
            ? "Keeping your PC and screen awake"
            : "Keeping your PC awake";

        string baseCaption = _state.KeepScreenOn
            ? "Your display won't turn off, lock, or start the screensaver"
            : "Your PC won't sleep, but the display can turn off";

        if (_state.SessionEndsAt is { } endsAt)
        {
            var remaining = endsAt - DateTimeOffset.Now;
            if (remaining < TimeSpan.Zero)
            {
                remaining = TimeSpan.Zero;
            }

            string left = remaining.TotalHours >= 1
                ? $"{(int)remaining.TotalHours} h {remaining.Minutes} min"
                : remaining.TotalMinutes >= 1
                    ? $"{remaining.Minutes} min {remaining.Seconds} s"
                    : $"{remaining.Seconds} s";
            baseCaption = $"Turns off in {left} — {char.ToLower(baseCaption[0])}{baseCaption[1..]}";
        }

        StatusCaption.Text = baseCaption;
    }

    private void AwakeToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (!_updatingToggle)
        {
            _state.Set(AwakeToggle.IsOn);
        }
    }

    private void RefreshStats()
    {
        UpdateStatusText();

        var (years, months, days, hours, minutes) = Breakdown(_usage.TotalAwake);
        YearsText.Text = years.ToString();
        MonthsText.Text = months.ToString();
        DaysText.Text = days.ToString();
        HoursText.Text = hours.ToString();
        MinutesText.Text = minutes.ToString();

        if (_usage.IsSessionActive)
        {
            var session = _usage.CurrentSession;
            SessionText.Text =
                $"{(int)session.TotalHours:00}:{session.Minutes:00}:{session.Seconds:00}";
            SessionCaption.Text = "Awake right now";
        }
        else
        {
            SessionText.Text = "—";
            SessionCaption.Text = "Toggle on to start brewing";
        }

        StreakText.Text = _usage.CurrentStreakDays.ToString();
        StreakUnitText.Text = _usage.CurrentStreakDays == 1 ? "day" : "days";
        BestStreakText.Text = _usage.BestStreakDays == 1
            ? "Best: 1 day"
            : $"Best: {_usage.BestStreakDays} days";
    }

    /// <summary>Splits a duration for display: 1 year = 365 days, 1 month = 30 days.</summary>
    private static (int Years, int Months, int Days, int Hours, int Minutes) Breakdown(TimeSpan t)
    {
        int totalDays = (int)t.TotalDays;
        int years = totalDays / 365;
        int months = totalDays % 365 / 30;
        int days = totalDays % 365 % 30;
        return (years, months, days, t.Hours, t.Minutes);
    }
}
