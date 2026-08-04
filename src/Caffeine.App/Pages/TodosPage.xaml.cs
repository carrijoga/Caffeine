using Caffeine.Core.Common;
using Caffeine.Core.Todos;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

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

    private void Add_Click(object sender, RoutedEventArgs e) => _ = ShowAddDialogAsync();

    /// <summary>Opens the add dialog: title, optional due date, and priority (default Normal).</summary>
    private async Task ShowAddDialogAsync()
    {
        var titleBox = new TextBox { PlaceholderText = "Add a to-do…" };
        AutomationProperties.SetName(titleBox, "To-do title");

        var duePicker = new CalendarDatePicker { PlaceholderText = "Due date" };
        AutomationProperties.SetName(duePicker, "Due date");

        var priorityBox = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
        AutomationProperties.SetName(priorityBox, "Prioridade");
        foreach (TodoPriority level in TodoPriorityInfo.DisplayOrder)
        {
            priorityBox.Items.Add(new ComboBoxItem
            {
                Content = TodoPriorityInfo.Display(level),
                Tag = level,
            });
            if (level == TodoPriority.Normal)
            {
                priorityBox.SelectedIndex = priorityBox.Items.Count - 1;
            }
        }

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "Add to-do",
            PrimaryButtonText = "Add",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            IsPrimaryButtonEnabled = false, // empty title — nothing to add yet
        };

        // Gate the primary button so the dialog can't silently discard a blank title.
        titleBox.TextChanged += (_, _) =>
            dialog.IsPrimaryButtonEnabled = titleBox.Text.Trim().Length > 0;

        var content = new StackPanel { Spacing = 12 };
        content.Children.Add(titleBox);
        content.Children.Add(duePicker);
        content.Children.Add(priorityBox);
        dialog.Content = content;

        // Focus on Opened, not before ShowAsync — the content isn't in the visual tree yet.
        // This is the one construct here with no precedent elsewhere in the app; if focus
        // doesn't land in the title box at runtime (Task 7, check 2), try setting
        // titleBox.Loaded instead.
        dialog.Opened += (_, _) => titleBox.Focus(FocusState.Programmatic);

        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        DateOnly? due = duePicker.Date is { } picked
            ? DateOnly.FromDateTime(picked.Date)
            : null;
        var priority = (TodoPriority)((ComboBoxItem)priorityBox.SelectedItem).Tag;

        if (_todos.Add(titleBox.Text, due, priority) is null)
        {
            return; // whitespace-only title — nothing to add
        }

        if (_showCompleted)
        {
            ViewSelector.SelectedItem = ActiveTab; // rebuilds via SelectionChanged
        }
        else
        {
            RebuildList();
        }
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
