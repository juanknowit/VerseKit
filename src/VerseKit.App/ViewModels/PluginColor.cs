using Avalonia;
using Avalonia.Media;

namespace VerseKit.App.ViewModels;

/// <summary>Assigns each plugin a stable icon colour, picked from a curated
/// palette by a deterministic hash of its id (so the same plugin is the same
/// colour in both the Installed and Available lists, and across launches).</summary>
public static class PluginColor
{
    private static readonly Color[] Palette =
    [
        Color.Parse("#007AFF"), // blue
        Color.Parse("#AF52DE"), // purple
        Color.Parse("#34C759"), // green
        Color.Parse("#FF9500"), // orange
        Color.Parse("#FF2D55"), // pink
        Color.Parse("#5AC8FA"), // teal
        Color.Parse("#5856D6"), // indigo
        Color.Parse("#FF3B30"), // red
    ];

    // Explicit colours for the core plugins (by id), so they read intentionally
    // and never collide. Unknown/third-party plugins fall back to the hash.
    private static readonly Dictionary<string, Color> Overrides = new(System.StringComparer.OrdinalIgnoreCase)
    {
        ["a1b2c3d4-e5f6-7890-abcd-ef1234567890"] = Color.Parse("#007AFF"), // Resource Manager → blue
        ["b2c3d4e5-f6a7-8901-bcde-f12345678901"] = Color.Parse("#FF9500"), // Table Browser → orange
        ["c3d4e5f6-a7b8-9012-cdef-345678901234"] = Color.Parse("#34C759"), // Security Roles → green
        ["d4e5f6a7-b8c9-0123-defa-456789012345"] = Color.Parse("#FF3B30"), // Access Checker → red
        ["e5f6a7b8-c9d0-1234-efab-567890123456"] = Color.Parse("#AF52DE"), // Solution Explorer → purple
    };

    public static IBrush For(string key)
    {
        Color c;
        if (Overrides.TryGetValue(key, out var fixedColor))
        {
            c = fixedColor;
        }
        else
        {
            // FNV-1a — deterministic across processes (unlike string.GetHashCode).
            uint hash = 2166136261;
            foreach (var ch in key)
            {
                hash ^= ch;
                hash *= 16777619;
            }
            c = Palette[hash % (uint)Palette.Length];
        }
        // Subtle vertical sheen so the icon matches the app's pill aesthetic.
        return new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
            GradientStops =
            {
                new GradientStop(Lighten(c, 0.12), 0),
                new GradientStop(c, 1),
            },
        };
    }

    private static Color Lighten(Color c, double amount)
    {
        byte L(byte x) => (byte)System.Math.Round(x + (255 - x) * amount);
        return Color.FromArgb(0xFF, L(c.R), L(c.G), L(c.B));
    }
}
