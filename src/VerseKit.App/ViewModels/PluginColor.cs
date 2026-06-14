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

    public static IBrush For(string key)
    {
        // FNV-1a — deterministic across processes (unlike string.GetHashCode).
        uint hash = 2166136261;
        foreach (var ch in key)
        {
            hash ^= ch;
            hash *= 16777619;
        }

        var c = Palette[hash % (uint)Palette.Length];
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
