using Caffeine.Core.Common;
using Caffeine.Core.Todos;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.System;

namespace Caffeine.Pages;

public sealed partial class TodosPage : Page
{
    private readonly TodoService _todos;
    private readonly DayChangeWatcher _dayChanges;
    private bool _showCompleted;

    public TodosPage()
    {
        _todos = ((App)Application.Current).Todos;
        _dayChanges = ((App)Application.Current).DayChanges;
        InitializeComponent();

        // Selecting the tab fires ViewSelector_SelectionChanged → RebuildList.
        ViewSelector.SelectedItem = ActiveTab;

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

    private void ViewSelector_SelectionChanged(
        SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
    {
        _showCompleted = sender.SelectedItem == CompletedTab;
        RebuildList();
    }

    private void NewTitleBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter)
        {
            AddTodo();
            e.Handled = true;
        }
    }

    private void Add_Click(object sender, RoutedEventArgs e) => AddTodo();

    private void AddTodo()
    {
        DateOnly? due = NewDuePicker.Date is { } picked
            ? DateOnly.FromDateTime(picked.Date)
            : null;

        if (_todos.Add(NewTitleBox.Text, due) is null)
        {
            return; // whitespace-only title — nothing to add
        }

        NewTitleBox.Text = string.Empty;
        NewDuePicker.Date = null;

        if (_showCompleted)
        {
            ViewSelector.SelectedItem = ActiveTab; // rebuilds via SelectionChanged
        }
        else
        {
            RebuildList();
        }

        NewTitleBox.Focus(FocusState.Programmatic);
    }

    private void RebuildList()
    {
        IReadOnlyList<TodoItem> items = _showCompleted ? _todos.Completed : _todos.Active;

        TodoRows.Children.Clear();
        foreach (TodoItem item in items)
        {
            TodoRows.Children.Add(BuildRow(item));
        }

        EmptyText.Text = _showCompleted ? "Nothing completed yet." : "All caught up!";
        EmptyText.Visibility = items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private Border BuildRow(TodoItem item)
    {
        var check = new CheckBox
        {
            IsChecked = item.IsDone,
            MinWidth = 0,
            VerticalAlignment = VerticalAlignment.Center,
        };
        AutomationProperties.SetName(check, item.Title);
        check.Checked += (_, _) =>
        {
            _todos.SetDone(item.Id, true);
            RebuildList();
        };
        check.Unchecked += (_, _) =>
        {
            _todos.SetDone(item.Id, false);
            RebuildList();
        };

        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Spacing = 2 };
        var title = new TextBlock { Text = item.Title, TextWrapping = TextWrapping.Wrap };
        if (item.IsDone)
        {
            title.TextDecorations = Windows.UI.Text.TextDecorations.Strikethrough;
            title.Opacity = 0.6;
        }
        text.Children.Add(title);

        if (BuildDetail(item) is { } detail)
        {
            text.Children.Add(detail);
        }

        var delete = new Button
        {
            Content = new FontIcon { Glyph = "\uE74D", FontSize = 14 },
            Padding = new Thickness(8),
            VerticalAlignment = VerticalAlignment.Center,
        };
        AutomationProperties.SetName(delete, $"Delete {item.Title}");
        delete.Click += (_, _) =>
        {
            _todos.Delete(item.Id);
            RebuildList();
        };

        var grid = new Grid { ColumnSpacing = 12 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(text, 1);
        Grid.SetColumn(delete, 2);
        grid.Children.Add(check);
        grid.Children.Add(text);
        grid.Children.Add(delete);

        return new Border
        {
            Style = (Style)Resources["TodoRowStyle"],
            Child = grid,
        };
    }

    /// <summary>Caption under the title: due info (Active) or completion date (Completed); null when there is nothing to say.</summary>
    private TextBlock? BuildDetail(TodoItem item)
    {
        string detailText;
        bool critical = false;

        if (item.IsDone)
        {
            if (item.CompletedAt is not { } completed)
            {
                return null;
            }

            detailText = $"Completed {completed.LocalDateTime:MMM d}";
        }
        else if (item.DueDate is { } due)
        {
            if (_todos.IsOverdue(item))
            {
                detailText = $"Overdue — was due {due:MMM d}";
                critical = true;
            }
            else
            {
                detailText = due == _todos.Today ? "Due today" : $"Due {due:MMM d}";
            }
        }
        else
        {
            return null;
        }

        var detail = new TextBlock
        {
            Text = detailText,
            Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"],
        };
        detail.Foreground = critical
            ? (Brush)Application.Current.Resources["SystemFillColorCriticalBrush"]
            : (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"];
        return detail;
    }
}
