namespace Caffeine.Core.Todos;

/// <summary>Fixed palette of category colors, chosen for contrast in both light and dark theme.</summary>
public static class CategoryColors
{
    public static readonly IReadOnlyList<string> Palette = new[]
    {
        "#E81123", // red
        "#EA8D00", // orange
        "#EAA300", // amber
        "#107C10", // green
        "#00B7C3", // teal
        "#0078D4", // blue
        "#5C2D91", // purple
        "#C239B3", // magenta
        "#8E8CD8", // periwinkle
        "#6E6E6E", // gray (default)
    };
}
