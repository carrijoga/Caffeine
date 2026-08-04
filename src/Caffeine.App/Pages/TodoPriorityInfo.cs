using Caffeine.Core.Todos;

namespace Caffeine.Pages;

/// <summary>
/// Emoji + Portuguese label for each priority. Single source of truth so the add
/// dialog, the row badge, and the row flyout cannot drift apart.
/// </summary>
internal static class TodoPriorityInfo
{
    /// <summary>Most urgent first — the order a user expects to scan in a priority menu.</summary>
    public static IReadOnlyList<TodoPriority> DisplayOrder { get; } = new[]
    {
        TodoPriority.Urgent,
        TodoPriority.High,
        TodoPriority.Medium,
        TodoPriority.Normal,
        TodoPriority.Low,
    };

    public static string Emoji(TodoPriority priority) => priority switch
    {
        TodoPriority.Urgent => "🔴",
        TodoPriority.High => "🟠",
        TodoPriority.Medium => "🟡",
        TodoPriority.Normal => "🔵",
        TodoPriority.Low => "🟢",
        _ => "🔵",
    };

    public static string Label(TodoPriority priority) => priority switch
    {
        TodoPriority.Urgent => "Urgente",
        TodoPriority.High => "Alta",
        TodoPriority.Medium => "Média",
        TodoPriority.Normal => "Normal",
        TodoPriority.Low => "Baixa",
        _ => "Normal",
    };

    /// <summary>"🔴 Urgente" — for combo box items and flyout entries.</summary>
    public static string Display(TodoPriority priority) =>
        $"{Emoji(priority)} {Label(priority)}";
}
