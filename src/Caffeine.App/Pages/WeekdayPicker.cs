using Caffeine.Core.Habits;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;

namespace Caffeine.Pages;

/// <summary>Builds a row of 7 day-of-week toggle buttons shared by the add dialog and edit flyout.</summary>
internal static class WeekdayPicker
{
    private static readonly (Weekdays Flag, string Short, string Full)[] Days =
    {
        (Weekdays.Sunday, "S", "Sunday"),
        (Weekdays.Monday, "M", "Monday"),
        (Weekdays.Tuesday, "T", "Tuesday"),
        (Weekdays.Wednesday, "W", "Wednesday"),
        (Weekdays.Thursday, "T", "Thursday"),
        (Weekdays.Friday, "F", "Friday"),
        (Weekdays.Saturday, "S", "Saturday"),
    };

    /// <summary>
    /// Builds the toggle row pre-selected per <paramref name="initial"/>. <paramref name="getValue"/>
    /// reads the combined selection at any point (e.g. when the caller's save button is clicked).
    /// <paramref name="onChanged"/> fires whenever any toggle flips, so callers can enable/disable
    /// their save button based on whether at least one day is selected.
    /// </summary>
    public static StackPanel Build(Weekdays initial, out Func<Weekdays> getValue, Action? onChanged = null)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
        var toggles = new List<(Weekdays Flag, ToggleButton Button)>();

        foreach (var (flag, shortLabel, fullLabel) in Days)
        {
            var toggle = new ToggleButton
            {
                Content = shortLabel,
                IsChecked = initial.HasFlag(flag),
                MinWidth = 36,
                Padding = new Thickness(0, 6, 0, 6),
            };
            AutomationProperties.SetName(toggle, fullLabel);
            toggle.Checked += (_, _) => onChanged?.Invoke();
            toggle.Unchecked += (_, _) => onChanged?.Invoke();
            toggles.Add((flag, toggle));
            panel.Children.Add(toggle);
        }

        getValue = () =>
        {
            Weekdays result = Weekdays.None;
            foreach (var (flag, button) in toggles)
            {
                if (button.IsChecked == true)
                {
                    result |= flag;
                }
            }

            return result;
        };

        return panel;
    }
}
