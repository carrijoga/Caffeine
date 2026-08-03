using Windows.UI;

namespace Caffeine.Pages;

/// <summary>Shared hex-string to <see cref="Color"/> parsing used by category color UI.</summary>
internal static class HexColor
{
    /// <summary>
    /// Parses a "#RRGGBB" (or "RRGGBB") hex string into an opaque <see cref="Color"/>.
    /// Malformed input falls back to opaque black.
    /// </summary>
    internal static Color Parse(string hex)
    {
        hex = hex.TrimStart('#');
        return hex.Length == 6
            ? Color.FromArgb(0xFF, byte.Parse(hex[..2], System.Globalization.NumberStyles.HexNumber), byte.Parse(hex[2..4], System.Globalization.NumberStyles.HexNumber), byte.Parse(hex[4..6], System.Globalization.NumberStyles.HexNumber))
            : Color.FromArgb(0xFF, 0, 0, 0);
    }
}
