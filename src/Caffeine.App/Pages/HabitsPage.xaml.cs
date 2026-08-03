using Caffeine.Core.Common;
using Caffeine.Core.Habits;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using System.Linq;
using Windows.System;

namespace Caffeine.Pages;

public sealed partial class HabitsPage : Page
{
    private readonly HabitService _habits;
    private readonly DayChangeWatcher _dayChanges;

    public HabitsPage()
    {
        _habits = ((App)Application.Current).Habits;
        _dayChanges = ((App)Application.Current).DayChanges;
        InitializeComponent();
        NewIconPickerHost.Content = EmojiPicker.Build(emoji => NewIconBox.Text = emoji);
        RebuildList();

        _dayChanges.DayChanged += OnDayChanged;
        ActualThemeChanged += OnThemeChanged;
        Unloaded += (_, _) =>
        {
            _dayChanges.DayChanged -= OnDayChanged;
            ActualThemeChanged -= OnThemeChanged;
        };
    }

    private void OnDayChanged() => RebuildList();

    private void OnThemeChanged(FrameworkElement sender, object args) => RebuildList();

    private void NewNameBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter)
        {
            _ = ShowAddDialogAsync();
            e.Handled = true;
        }
    }

    private void Add_Click(object sender, RoutedEventArgs e) => _ = ShowAddDialogAsync();

    /// <summary>Opens the add dialog pre-filled from the inline name/icon boxes, with a day picker defaulting to every day.</summary>
    private async Task ShowAddDialogAsync()
    {
        string initialName = NewNameBox.Text;
        string initialIcon = NewIconBox.Text;

        var nameBox = new TextBox { Text = initialName, PlaceholderText = "Habit name" };
        AutomationProperties.SetName(nameBox, "Habit name");
        var iconBox = new TextBox { Text = initialIcon, Width = 64, MaxLength = 8, PlaceholderText = "⭐" };
        AutomationProperties.SetName(iconBox, "Icon");
        Button iconPicker = EmojiPicker.Build(emoji => iconBox.Text = emoji);
        var iconRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        iconRow.Children.Add(iconBox);
        iconRow.Children.Add(iconPicker);

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "Add habit",
            PrimaryButtonText = "Add",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
        };

        Func<Weekdays>? getRepeat = null;
        StackPanel picker = WeekdayPicker.Build(
            Weekdays.All,
            out getRepeat,
            onChanged: () => dialog.IsPrimaryButtonEnabled = getRepeat!() != Weekdays.None);

        var content = new StackPanel { Spacing = 12 };
        content.Children.Add(nameBox);
        content.Children.Add(iconRow);
        content.Children.Add(picker);
        dialog.Content = content;

        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        Habit? habit = _habits.Add(nameBox.Text, iconBox.Text);
        if (habit is null)
        {
            return; // whitespace-only name — nothing to add
        }

        _habits.SetRepeat(habit.Id, getRepeat());

        NewNameBox.Text = string.Empty;
        NewIconBox.Text = string.Empty;
        RebuildList();
        NewNameBox.Focus(FocusState.Programmatic);
    }

    private void RebuildList()
    {
        HabitRows.Children.Clear();
        List<Habit> today = _habits.Habits.Where(h => _habits.IsScheduled(h, _habits.Today)).ToList();
        foreach (Habit habit in today)
        {
            HabitRows.Children.Add(BuildRow(habit));
        }

        EmptyText.Visibility = today.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        EmptyText.Text = _habits.Habits.Count == 0
            ? "No habits yet — add your first above."
            : "No habits today.";
    }

    private Border BuildRow(Habit habit)
    {
        var check = new CheckBox
        {
            IsChecked = _habits.IsDone(habit, _habits.Today),
            MinWidth = 0,
            VerticalAlignment = VerticalAlignment.Center,
        };
        AutomationProperties.SetName(check, habit.Name);
        check.Checked += (_, _) =>
        {
            _habits.SetDone(habit.Id, _habits.Today, true);
            RebuildList();
        };
        check.Unchecked += (_, _) =>
        {
            _habits.SetDone(habit.Id, _habits.Today, false);
            RebuildList();
        };

        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Spacing = 2 };
        text.Children.Add(new TextBlock
        {
            Text = $"{habit.Icon} {habit.Name}",
            TextWrapping = TextWrapping.Wrap,
        });
        text.Children.Add(new TextBlock
        {
            Text = $"Streak {_habits.CurrentStreak(habit)} · Best {_habits.BestStreak(habit)}",
            Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"],
            Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
        });

        var rename = new Button
        {
            Content = new FontIcon { Glyph = "\uE70F", FontSize = 14 },
            Padding = new Thickness(8),
            VerticalAlignment = VerticalAlignment.Center,
            Flyout = BuildRenameFlyout(habit),
        };
        AutomationProperties.SetName(rename, $"Rename {habit.Name}");

        var delete = new Button
        {
            Content = new FontIcon { Glyph = "\uE74D", FontSize = 14 },
            Padding = new Thickness(8),
            VerticalAlignment = VerticalAlignment.Center,
        };
        AutomationProperties.SetName(delete, $"Delete {habit.Name}");
        delete.Click += async (_, _) => await ConfirmDeleteAsync(habit);

        var grid = new Grid { ColumnSpacing = 12 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var dots = BuildDotStrip(habit);
        Grid.SetColumn(text, 1);
        Grid.SetColumn(dots, 2);
        Grid.SetColumn(rename, 3);
        Grid.SetColumn(delete, 4);
        grid.Children.Add(check);
        grid.Children.Add(text);
        grid.Children.Add(dots);
        grid.Children.Add(rename);
        grid.Children.Add(delete);

        return new Border
        {
            Style = (Style)Resources["HabitRowStyle"],
            Child = grid,
        };
    }

    /// <summary>Seven 8px dots, oldest day first; today is the rightmost.</summary>
    private StackPanel BuildDotStrip(Habit habit)
    {
        var strip = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 4,
            VerticalAlignment = VerticalAlignment.Center,
        };
        foreach (bool done in _habits.LastSevenDays(habit))
        {
            strip.Children.Add(new Ellipse
            {
                Width = 8,
                Height = 8,
                Fill = (Brush)Application.Current.Resources[
                    done ? "AccentFillColorDefaultBrush" : "SubtleFillColorSecondaryBrush"],
            });
        }

        return strip;
    }

    private Flyout BuildRenameFlyout(Habit habit)
    {
        var box = new TextBox { Text = habit.Name, MinWidth = 220 };
        AutomationProperties.SetName(box, "New name");

        var save = new Button
        {
            Content = "Save",
            Style = (Style)Application.Current.Resources["AccentButtonStyle"],
            Margin = new Thickness(0, 8, 0, 0),
        };

        Func<Weekdays>? getRepeat = null;
        StackPanel picker = WeekdayPicker.Build(
            habit.Repeat,
            out getRepeat,
            onChanged: () => save.IsEnabled = getRepeat!() != Weekdays.None);

        var panel = new StackPanel { Spacing = 8 };
        panel.Children.Add(box);
        panel.Children.Add(picker);
        panel.Children.Add(save);

        var flyout = new Flyout { Content = panel };

        void Commit()
        {
            _habits.Rename(habit.Id, box.Text);
            _habits.SetRepeat(habit.Id, getRepeat());
            flyout.Hide();
            RebuildList();
        }

        save.Click += (_, _) => Commit();
        box.KeyDown += (_, e) =>
        {
            if (e.Key == VirtualKey.Enter)
            {
                Commit();
                e.Handled = true;
            }
        };
        return flyout;
    }

    /// <summary>Delete confirms first (spec) — it drops the habit's whole history.</summary>
    private async Task ConfirmDeleteAsync(Habit habit)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = $"Delete \"{habit.Name}\"?",
            Content = "This removes the habit and its whole completion history.",
            PrimaryButtonText = "Delete",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
        };

        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            _habits.Delete(habit.Id);
            RebuildList();
        }
    }
}
