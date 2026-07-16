using Caffeine.Core.Awake;
using Caffeine.Core.Common;
using Caffeine.Core.Habits;
using Caffeine.Core.Todos;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace Caffeine.Pages;

public sealed partial class HomePage : Page
{
    private readonly AwakeState _state;
    private readonly TimersService _timers;
    private readonly TodoService _todos;
    private readonly HabitService _habits;
    private readonly DayChangeWatcher _dayChanges;
    private bool _updatingToggle;

    public HomePage()
    {
        var app = (App)Application.Current;
        _state = app.State;
        _timers = app.Timers;
        _todos = app.Todos;
        _habits = app.Habits;
        _dayChanges = app.DayChanges;
        InitializeComponent();

        _state.Changed += OnStateChanged;
        _timers.Ticked += RefreshTimersCard;
        _dayChanges.DayChanged += OnDayChanged;
        ActualThemeChanged += OnThemeChanged;
        Unloaded += (_, _) =>
        {
            _state.Changed -= OnStateChanged;
            _timers.Ticked -= RefreshTimersCard;
            _dayChanges.DayChanged -= OnDayChanged;
            ActualThemeChanged -= OnThemeChanged;
        };

        OnStateChanged(_state.IsActive);
        RefreshTimersCard();
        RefreshTodosCard();
        BuildHabitsCard();
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

    private void RefreshTodosCard()
    {
        IReadOnlyList<TodoItem> active = _todos.Active;
        int overdue = active.Count(_todos.IsOverdue);
        int dueToday = active.Count(_todos.IsDueToday);

        TodosCaption.Text = (dueToday, overdue) switch
        {
            (0, 0) when active.Count == 0 => "All caught up",
            (0, 0) => Plural(active.Count, "open to-do"),
            (_, 0) => $"{Plural(dueToday, "to-do")} due today",
            (0, _) => $"{Plural(overdue, "to-do")} overdue",
            _ => $"{dueToday} due today · {overdue} overdue",
        };
    }

    private static string Plural(int count, string noun) =>
        count == 1 ? $"1 {noun}" : $"{count} {noun}s";

    private void OpenTodos_Click(object sender, RoutedEventArgs e) =>
        ((App)Application.Current).NavigateTo("todos");

    /// <summary>Builds the card's habit checkboxes once; the caption refreshes on every check-off.</summary>
    private void BuildHabitsCard()
    {
        HomeHabitRows.Children.Clear();
        foreach (Habit habit in _habits.Habits)
        {
            var check = new CheckBox
            {
                IsChecked = _habits.IsDone(habit, _habits.Today),
                Content = $"{habit.Icon} {habit.Name}",
                MinWidth = 0,
            };
            AutomationProperties.SetName(check, habit.Name);
            check.Tapped += (_, e) => e.Handled = true;
            check.Checked += (_, _) =>
            {
                _habits.SetDone(habit.Id, _habits.Today, true);
                RefreshHabitsCaption();
            };
            check.Unchecked += (_, _) =>
            {
                _habits.SetDone(habit.Id, _habits.Today, false);
                RefreshHabitsCaption();
            };
            HomeHabitRows.Children.Add(check);
        }

        RefreshHabitsCaption();
    }

    private void RefreshHabitsCaption()
    {
        int total = _habits.Habits.Count;
        HabitsCaption.Text = total == 0
            ? "No habits yet"
            : $"{_habits.Habits.Count(h => _habits.IsDone(h, _habits.Today))} of {total} done today";
    }

    private void OpenHabits_Click(object sender, RoutedEventArgs e) =>
        ((App)Application.Current).NavigateTo("habits");

    private void OnDayChanged()
    {
        RefreshTodosCard();
        BuildHabitsCard(); // rebuilds checkboxes + refreshes the "N of M done today" caption
    }

    private void OnThemeChanged(FrameworkElement sender, object args)
    {
        // Home's cards are XAML with ThemeResource brushes and retint themselves;
        // no code-built brushes here need manual retinting. Kept for symmetry with
        // the module pages and to re-read anything day/theme-derived if added later.
        BuildHabitsCard();
    }
}
