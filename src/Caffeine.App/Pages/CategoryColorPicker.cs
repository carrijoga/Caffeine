using Caffeine.Core.Todos;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace Caffeine.Pages;

/// <summary>Builds a row of color swatch toggle buttons shared by the category add/edit dialogs.</summary>
internal static class CategoryColorPicker
{
    /// <summary>
    /// Builds the swatch row with <paramref name="initialHex"/> pre-selected (falls back to the
    /// first palette entry if unrecognized). <paramref name="getValue"/> reads the selected hex at
    /// any point. <paramref name="onChanged"/> fires whenever the selection changes.
    /// </summary>
    public static StackPanel Build(string initialHex, out Func<string> getValue, Action? onChanged = null)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
        var toggles = new List<(string Hex, ToggleButton Button)>();
        string selected = CategoryColors.Palette.Contains(initialHex)
            ? initialHex
            : CategoryColors.Palette[^1];

        foreach (string hex in CategoryColors.Palette)
        {
            Color color = HexColor.Parse(hex);
            var toggle = new ToggleButton
            {
                Background = new SolidColorBrush(color),
                IsChecked = hex == selected,
                Width = 28,
                Height = 28,
                Padding = new Thickness(0),
                CornerRadius = new CornerRadius(14),
            };
            AutomationProperties.SetName(toggle, hex);
            toggles.Add((hex, toggle));
            panel.Children.Add(toggle);
        }

        foreach (var (hex, toggle) in toggles)
        {
            toggle.Checked += (_, _) =>
            {
                foreach (var (otherHex, otherToggle) in toggles)
                {
                    if (otherHex != hex)
                    {
                        otherToggle.IsChecked = false;
                    }
                }

                onChanged?.Invoke();
            };

            toggle.Unchecked += (_, _) =>
            {
                // Swatches behave like a single-select group: re-check the toggle that was
                // just unchecked so exactly one swatch remains selected at all times (e.g.
                // when the user clicks the already-selected swatch).
                if (toggles.All(t => t.Button.IsChecked != true))
                {
                    toggle.IsChecked = true;
                }
            };
        }

        getValue = () => toggles.First(t => t.Button.IsChecked == true).Hex;

        return panel;
    }
}
