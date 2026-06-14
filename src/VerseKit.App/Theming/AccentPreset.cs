using System.Collections.Generic;
using Avalonia;
using Avalonia.Media;

namespace VerseKit.App.Theming;

/// <summary>
/// A single accent colour preset. Every other accent-related brush (hover,
/// pressed, the pale "tint" used for selected rows, the text-selection wash and
/// the Fluent <c>SystemAccentColor</c> shades that drive checkboxes / progress
/// bars) is derived from <see cref="Accent"/> so a preset only has to declare
/// its base colour plus a hand-tuned readable text colour.
/// </summary>
public sealed record AccentPreset(string Id, string Name, Color Accent, Color AccentText)
{
    /// <summary>The eight macOS-style presets shown in Settings.</summary>
    public static readonly IReadOnlyList<AccentPreset> All = new[]
    {
        new AccentPreset("blue",     "Blue",     Color.Parse("#007AFF"), Color.Parse("#0066D6")),
        new AccentPreset("purple",   "Purple",   Color.Parse("#AF52DE"), Color.Parse("#9636C4")),
        new AccentPreset("pink",     "Pink",     Color.Parse("#FF2D55"), Color.Parse("#D70036")),
        new AccentPreset("red",      "Red",      Color.Parse("#FF3B30"), Color.Parse("#D70015")),
        new AccentPreset("orange",   "Orange",   Color.Parse("#FF9500"), Color.Parse("#C2670A")),
        new AccentPreset("yellow",   "Yellow",   Color.Parse("#FFCC00"), Color.Parse("#9A7400")),
        new AccentPreset("green",    "Green",    Color.Parse("#34C759"), Color.Parse("#1E8E41")),
        new AccentPreset("graphite", "Graphite", Color.Parse("#8E8E93"), Color.Parse("#5A5A60")),
    };

    public const string DefaultId = "blue";

    public static AccentPreset ById(string? id)
    {
        foreach (var p in All)
            if (string.Equals(p.Id, id, System.StringComparison.OrdinalIgnoreCase))
                return p;
        return All[0];
    }

    /// <summary>Solid brush of the base accent — used for the Settings swatch.</summary>
    public IBrush SwatchBrush => new SolidColorBrush(Accent);

    /// <summary>
    /// Diagonal "aurora" gradient derived from the accent, used as the window
    /// background in the Theme background style. The natural feel comes from a
    /// slight hue shift between stops (analogous colours) rather than a plain
    /// light→dark ramp of one hue: a lighter, cyan-ward top-left flows through
    /// the base accent into a deeper, indigo-ward bottom-right.
    /// </summary>
    public IBrush BackgroundGradient
    {
        get
        {
            var (h, s, l) = ToHsl(Accent);
            var top    = FromHsl(h - 13, s, System.Math.Min(0.78, l + 0.22));
            var mid    = FromHsl(h,      s, l);
            var bottom = FromHsl(h + 13, s, System.Math.Max(0.30, l - 0.10));
            return new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                EndPoint   = new RelativePoint(1, 1, RelativeUnit.Relative),
                GradientStops =
                {
                    new GradientStop(top, 0),
                    new GradientStop(mid, 0.55),
                    new GradientStop(bottom, 1),
                }
            };
        }
    }

    // ── Derived shades ───────────────────────────────────────────────────
    public Color AccentHover => Darken(Accent, 0.06);
    public Color AccentPressed => Darken(Accent, 0.13);
    public Color Tint => MixWithWhite(Accent, 0.12);
    public Color TintHover => MixWithWhite(Accent, 0.18);
    public Color TintPressed => MixWithWhite(Accent, 0.26);
    public Color Selection => MixWithWhite(Accent, 0.35);

    public Color FluentLight1 => Lighten(Accent, 0.10);
    public Color FluentLight2 => Lighten(Accent, 0.20);
    public Color FluentLight3 => Lighten(Accent, 0.30);
    public Color FluentDark1 => Darken(Accent, 0.10);
    public Color FluentDark2 => Darken(Accent, 0.20);
    public Color FluentDark3 => Darken(Accent, 0.30);

    /// <summary><paramref name="t"/> is the fraction of accent kept (0.12 = a
    /// pale 12% wash over white).</summary>
    private static Color MixWithWhite(Color c, double t) => Mix(c, Colors.White, t);

    private static Color Lighten(Color c, double amount) => Mix(c, Colors.White, 1 - amount);

    private static Color Darken(Color c, double amount) => Mix(c, Colors.Black, 1 - amount);

    /// <summary>Linear blend: <paramref name="t"/>=1 keeps <paramref name="a"/>.</summary>
    private static Color Mix(Color a, Color b, double t)
    {
        byte L(byte x, byte y) => (byte)System.Math.Round(x * t + y * (1 - t));
        return Color.FromArgb(0xFF, L(a.R, b.R), L(a.G, b.G), L(a.B, b.B));
    }

    // ── HSL conversion ───────────────────────────────────────────────────
    // Used by BackgroundGradient to shift hue and lightness independently.
    // H is in degrees [0,360); S and L in [0,1].
    private static (double H, double S, double L) ToHsl(Color c)
    {
        double r = c.R / 255.0, g = c.G / 255.0, b = c.B / 255.0;
        double max = System.Math.Max(r, System.Math.Max(g, b));
        double min = System.Math.Min(r, System.Math.Min(g, b));
        double l = (max + min) / 2, h = 0, s = 0, d = max - min;
        if (d > 0)
        {
            s = l > 0.5 ? d / (2 - max - min) : d / (max + min);
            if (max == r) h = (g - b) / d + (g < b ? 6 : 0);
            else if (max == g) h = (b - r) / d + 2;
            else h = (r - g) / d + 4;
            h *= 60;
        }
        return (h, s, l);
    }

    private static Color FromHsl(double h, double s, double l)
    {
        h = ((h % 360) + 360) % 360 / 360.0;
        s = System.Math.Clamp(s, 0, 1);
        l = System.Math.Clamp(l, 0, 1);
        double r, g, b;
        if (s == 0)
        {
            r = g = b = l;
        }
        else
        {
            double q = l < 0.5 ? l * (1 + s) : l + s - l * s;
            double p = 2 * l - q;
            r = HueToRgb(p, q, h + 1.0 / 3);
            g = HueToRgb(p, q, h);
            b = HueToRgb(p, q, h - 1.0 / 3);
        }
        byte B(double x) => (byte)System.Math.Round(x * 255);
        return Color.FromArgb(0xFF, B(r), B(g), B(b));
    }

    private static double HueToRgb(double p, double q, double t)
    {
        if (t < 0) t += 1;
        if (t > 1) t -= 1;
        if (t < 1.0 / 6) return p + (q - p) * 6 * t;
        if (t < 1.0 / 2) return q;
        if (t < 2.0 / 3) return p + (q - p) * (2.0 / 3 - t) * 6;
        return p;
    }
}
