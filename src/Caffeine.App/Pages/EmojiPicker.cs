using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;

namespace Caffeine.Pages;

/// <summary>Builds a button that opens a curated emoji grid, shared by the inline add row and the add dialog.</summary>
internal static class EmojiPicker
{
    private static readonly string[] Emoji =
    {
        "⭐", "🎯", "✅", "🔥", "💪", "🏃", "🚴", "🏋️",
        "🧘", "🚶", "🏊", "⚽", "🚭", "🍎", "🥗", "💧",
        "😴", "🛌", "🧹", "🧺", "🧴", "🪥", "☀️", "🌙",
        "📖", "✍️", "🎨", "🎵", "🎸", "💻", "🧠", "💰",
        "📵", "🚫", "🙏", "❤️", "🌱", "🐶", "🎉", "🍺",
    };

    /// <summary>
    /// Builds a small button that opens a flyout of emoji; picking one invokes <paramref name="onPicked"/>
    /// and closes the flyout.
    /// </summary>
    public static Button Build(Action<string> onPicked)
    {
        var button = new Button
        {
            Content = new FontIcon { Glyph = "", FontSize = 14 },
            Padding = new Thickness(8),
            VerticalAlignment = VerticalAlignment.Center,
        };
        AutomationProperties.SetName(button, "Choose emoji");

        var grid = new GridView
        {
            SelectionMode = ListViewSelectionMode.None,
            IsItemClickEnabled = true,
            MaxWidth = 280,
            ItemsSource = Emoji,
            ItemTemplate = BuildItemTemplate(),
        };
        grid.ItemContainerStyle = new Style(typeof(GridViewItem))
        {
            Setters =
            {
                new Setter(FrameworkElement.WidthProperty, 40.0),
                new Setter(FrameworkElement.HeightProperty, 40.0),
            },
        };

        var flyout = new Flyout { Content = grid };
        grid.ItemClick += (_, e) =>
        {
            onPicked((string)e.ClickedItem);
            flyout.Hide();
        };

        button.Flyout = flyout;
        return button;
    }

    private static DataTemplate BuildItemTemplate()
    {
        const string xaml = """
            <DataTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation">
                <TextBlock Text="{Binding}" FontSize="20" HorizontalAlignment="Center" VerticalAlignment="Center" />
            </DataTemplate>
            """;
        return (DataTemplate)XamlReader.Load(xaml);
    }
}
