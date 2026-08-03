using Caffeine.Core.Common;
using Caffeine.Core.Todos;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using System.Linq;
using System.Threading.Tasks;
using Windows.System;

namespace Caffeine.Pages;

public sealed partial class TodosPage : Page
{
    private const string NewCategorySentinel = "__new_category__";

    private readonly TodoService _todos;
    private readonly DayChangeWatcher _dayChanges;
    private bool _showCompleted;
    private bool _categoryFilterActive;
    private Guid? _categoryFilterId;

    public TodosPage()
    {
        _todos = ((App)Application.Current).Todos;
        _dayChanges = ((App)Application.Current).DayChanges;
        InitializeComponent();
        PopulateCategoryCombo();
        PopulateCategoryFilterCombo();

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

    private void ManageCategoriesButton_Click(object sender, RoutedEventArgs e) =>
        _ = ShowManageCategoriesDialogAsync();

    private async Task ShowManageCategoriesDialogAsync()
    {
        var list = new StackPanel { Spacing = 8 };

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "Manage categories",
            CloseButtonText = "Done",
        };

        // WinUI only allows one ContentDialog open at a time, so "+ New category" and the
        // per-row delete confirmation (both ContentDialogs) can't be shown while this one is
        // still up. Each nested flow hides this dialog first (resolving whichever ShowAsync()
        // call is currently pending with None), fully awaits the nested dialog to close, and
        // only then calls ShowAsync() on this same instance again itself to reopen — so at most
        // one ShowAsync() for `dialog` is ever in flight. isClosingForNestedDialog distinguishes
        // a Hide() done for this reason from the user genuinely closing via "Done": it's set
        // synchronously right before Hide() (so the resolution this triggers is never treated as
        // a real close by ShowOnceAsync) and cleared once the nested flow reopens `dialog`.
        bool isClosingForNestedDialog = false;

        // Shows `dialog` once and, if that resolution was a real close (not a nested-dialog
        // Hide()), runs the post-session refresh. Called both for the initial show and every
        // reopen, so the refresh always runs on whichever ShowAsync() call is the final one.
        async Task ShowOnceAsync()
        {
            await dialog.ShowAsync();
            if (isClosingForNestedDialog)
            {
                return;
            }

            // Combos may be stale if edits/deletes happened without a full-dialog reopen path;
            // refresh once more after the dialog closes and rebuild the visible list.
            PopulateCategoryCombo();
            RefreshCategoryFilterCombo();
            RebuildList();
        }

        async Task HideForNestedDialogAsync(Func<Task> showNestedDialogAsync)
        {
            isClosingForNestedDialog = true;
            dialog.Hide();
            await showNestedDialogAsync();
            isClosingForNestedDialog = false;
            await ShowOnceAsync();
        }

        void RebuildCategoryList()
        {
            list.Children.Clear();
            foreach (TodoCategory category in _todos.Categories)
            {
                list.Children.Add(BuildCategoryManageRow(category, HideForNestedDialogAsync, RebuildCategoryList));
            }

            if (_todos.Categories.Count == 0)
            {
                list.Children.Add(new TextBlock { Text = "No categories yet." });
            }
        }

        RebuildCategoryList();

        var content = new StackPanel { Spacing = 12 };
        content.Children.Add(list);
        dialog.Content = content;

        var addButton = new Button { Content = "+ New category" };
        addButton.Click += async (_, _) =>
        {
            await HideForNestedDialogAsync(async () =>
            {
                await ShowNewCategoryDialogAsync();
                RebuildCategoryList();
                PopulateCategoryCombo();
                RefreshCategoryFilterCombo();
            });
        };
        content.Children.Add(addButton);

        await ShowOnceAsync();
    }

    private Border BuildCategoryManageRow(
        TodoCategory category, Func<Func<Task>, Task> hideForNestedDialogAsync, Action onChanged)
    {
        var swatch = new Border
        {
            Background = new SolidColorBrush(HexColor.Parse(category.ColorHex)),
            Width = 16,
            Height = 16,
            CornerRadius = new CornerRadius(8),
            VerticalAlignment = VerticalAlignment.Center,
        };

        var name = new TextBlock
        {
            Text = category.Name,
            VerticalAlignment = VerticalAlignment.Center,
        };

        var edit = new Button
        {
            Content = new FontIcon { Glyph = "", FontSize = 14 },
            Padding = new Thickness(8),
            Flyout = BuildEditCategoryFlyout(category, onChanged),
        };
        AutomationProperties.SetName(edit, $"Edit {category.Name}");

        var delete = new Button
        {
            Content = new FontIcon { Glyph = "", FontSize = 14 },
            Padding = new Thickness(8),
        };
        AutomationProperties.SetName(delete, $"Delete {category.Name}");
        delete.Click += async (_, _) =>
        {
            await hideForNestedDialogAsync(async () =>
            {
                await ConfirmDeleteCategoryAsync(category);
                onChanged();
            });
        };

        var grid = new Grid { ColumnSpacing = 8 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(name, 1);
        Grid.SetColumn(edit, 2);
        Grid.SetColumn(delete, 3);
        grid.Children.Add(swatch);
        grid.Children.Add(name);
        grid.Children.Add(edit);
        grid.Children.Add(delete);

        return new Border { Child = grid, Padding = new Thickness(0, 4, 0, 4) };
    }

    private Flyout BuildEditCategoryFlyout(TodoCategory category, Action onChanged)
    {
        var nameBox = new TextBox { Text = category.Name, MinWidth = 200 };
        AutomationProperties.SetName(nameBox, "Category name");

        var save = new Button
        {
            Content = "Save",
            Style = (Style)Application.Current.Resources["AccentButtonStyle"],
            Margin = new Thickness(0, 8, 0, 0),
        };

        Func<string>? getColor = null;
        StackPanel picker = CategoryColorPicker.Build(
            category.ColorHex,
            out getColor,
            onChanged: () => { });

        var panel = new StackPanel { Spacing = 8 };
        panel.Children.Add(nameBox);
        panel.Children.Add(picker);
        panel.Children.Add(save);

        var flyout = new Flyout { Content = panel };

        void Commit()
        {
            _todos.RenameCategory(category.Id, nameBox.Text, getColor!());
            flyout.Hide();
            onChanged();
        }

        save.Click += (_, _) => Commit();
        nameBox.KeyDown += (_, e) =>
        {
            if (e.Key == Windows.System.VirtualKey.Enter)
            {
                Commit();
                e.Handled = true;
            }
        };

        return flyout;
    }

    /// <summary>Confirms before deleting; if to-dos are assigned, the body names the count and both are deleted together.</summary>
    private async Task ConfirmDeleteCategoryAsync(TodoCategory category)
    {
        int count = _todos.CountByCategory(category.Id);
        string body = count == 0
            ? "This removes the category."
            : $"This category has {count} to-do{(count == 1 ? "" : "s")}. " +
              $"Deleting it will also delete all {count} to-do{(count == 1 ? "" : "s")}. This can't be undone.";
        string primaryText = count == 0 ? "Delete" : "Delete category and to-dos";

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = $"Delete \"{category.Name}\"?",
            Content = body,
            PrimaryButtonText = primaryText,
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
        };

        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            _todos.DeleteCategory(category.Id);
        }
    }

    private void PopulateCategoryCombo()
    {
        NewCategoryCombo.Items.Clear();
        NewCategoryCombo.Items.Add(new ComboBoxItem { Content = "Uncategorized", Tag = null });
        foreach (TodoCategory category in _todos.Categories)
        {
            NewCategoryCombo.Items.Add(new ComboBoxItem { Content = category.Name, Tag = category.Id });
        }

        NewCategoryCombo.Items.Add(new ComboBoxItem { Content = "+ New category…", Tag = NewCategorySentinel });
        NewCategoryCombo.SelectedIndex = 0;
    }

    private void PopulateCategoryFilterCombo()
    {
        CategoryFilterCombo.Items.Clear();
        CategoryFilterCombo.Items.Add(new ComboBoxItem { Content = "All", Tag = null });
        CategoryFilterCombo.Items.Add(new ComboBoxItem { Content = "Uncategorized", Tag = Guid.Empty });
        foreach (TodoCategory category in _todos.Categories)
        {
            CategoryFilterCombo.Items.Add(new ComboBoxItem { Content = category.Name, Tag = category.Id });
        }

        CategoryFilterCombo.SelectedIndex = 0;
    }

    private void CategoryFilterCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CategoryFilterCombo.SelectedItem is not ComboBoxItem item)
        {
            return;
        }

        switch (item.Tag)
        {
            case null:
                _categoryFilterActive = false;
                _categoryFilterId = null;
                break;
            case Guid g when g == Guid.Empty:
                _categoryFilterActive = true;
                _categoryFilterId = null;
                break;
            case Guid g:
                _categoryFilterActive = true;
                _categoryFilterId = g;
                break;
        }

        RebuildList();
    }

    private async void NewCategoryCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (NewCategoryCombo.SelectedItem is not ComboBoxItem { Tag: NewCategorySentinel })
        {
            return;
        }

        TodoCategory? created = await ShowNewCategoryDialogAsync();
        PopulateCategoryCombo();
        RefreshCategoryFilterCombo();

        if (created is not null)
        {
            SelectCategoryInCombo(created.Id);
        }
    }

    /// <summary>Rebuilds the category filter combo's items (e.g. after a category is added/removed elsewhere), preserving the currently active filter selection.</summary>
    private void RefreshCategoryFilterCombo()
    {
        bool wasActive = _categoryFilterActive;
        Guid? previousId = _categoryFilterId;

        PopulateCategoryFilterCombo();

        if (!wasActive)
        {
            return; // "All" (index 0) is already selected by PopulateCategoryFilterCombo.
        }

        object? sentinel = previousId is { } id ? id : Guid.Empty;
        foreach (object obj in CategoryFilterCombo.Items)
        {
            if (obj is ComboBoxItem { Tag: Guid tag } item && tag.Equals(sentinel))
            {
                CategoryFilterCombo.SelectedItem = item;
                return;
            }
        }

        // The previously selected category no longer exists (shouldn't happen from this call site,
        // but stay safe) — PopulateCategoryFilterCombo already reset state to "All".
    }

    private void SelectCategoryInCombo(Guid categoryId)
    {
        foreach (object obj in NewCategoryCombo.Items)
        {
            if (obj is ComboBoxItem { Tag: Guid tag } item && tag == categoryId)
            {
                NewCategoryCombo.SelectedItem = item;
                return;
            }
        }
    }

    /// <summary>Shows the add-category dialog. Returns the created category, or null if cancelled/ignored.</summary>
    private async Task<TodoCategory?> ShowNewCategoryDialogAsync()
    {
        var nameBox = new TextBox { PlaceholderText = "Category name" };
        AutomationProperties.SetName(nameBox, "Category name");

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "New category",
            PrimaryButtonText = "Add",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            IsPrimaryButtonEnabled = false,
        };

        nameBox.TextChanged += (_, _) =>
            dialog.IsPrimaryButtonEnabled = nameBox.Text.Trim().Length > 0;

        Func<string>? getColor = null;
        StackPanel picker = CategoryColorPicker.Build(
            Core.Todos.CategoryColors.Palette[^1],
            out getColor);

        var content = new StackPanel { Spacing = 12 };
        content.Children.Add(nameBox);
        content.Children.Add(picker);
        dialog.Content = content;

        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return null;
        }

        return _todos.AddCategory(nameBox.Text, getColor!());
    }

    private void AddTodo()
    {
        DateOnly? due = NewDuePicker.Date is { } picked
            ? DateOnly.FromDateTime(picked.Date)
            : null;
        Guid? categoryId = NewCategoryCombo.SelectedItem is ComboBoxItem { Tag: Guid tag } ? tag : null;

        if (_todos.Add(NewTitleBox.Text, due, categoryId) is null)
        {
            return; // whitespace-only title — nothing to add
        }

        NewTitleBox.Text = string.Empty;
        NewDuePicker.Date = null;
        NewCategoryCombo.SelectedIndex = 0;

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
        if (_categoryFilterActive)
        {
            items = items.Where(i => i.CategoryId == _categoryFilterId).ToList();
        }

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

        if (BuildCategoryBadge(item) is { } badge)
        {
            text.Children.Add(badge);
        }

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

    /// <summary>Small colored badge for the item's category; null when uncategorized or the category no longer exists.</summary>
    private Border? BuildCategoryBadge(TodoItem item)
    {
        if (item.CategoryId is not { } categoryId)
        {
            return null;
        }

        TodoCategory? category = _todos.Categories.FirstOrDefault(c => c.Id == categoryId);
        if (category is null)
        {
            return null;
        }

        return new Border
        {
            Background = new SolidColorBrush(HexColor.Parse(category.ColorHex)),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(6, 2, 6, 2),
            HorizontalAlignment = HorizontalAlignment.Left,
            Child = new TextBlock
            {
                Text = category.Name,
                Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"],
                Foreground = new SolidColorBrush(Microsoft.UI.Colors.White),
            },
        };
    }
}
