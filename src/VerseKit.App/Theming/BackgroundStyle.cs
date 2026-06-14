using System.Collections.Generic;

namespace VerseKit.App.Theming;

/// <summary>How the window surface behind the floating cards is painted.</summary>
public enum BackgroundStyle
{
    /// <summary>Transparent window + AcrylicBlur — the blurred desktop shows through (default).</summary>
    Acrylic,

    /// <summary>An opaque diagonal gradient derived from the chosen accent.</summary>
    Theme,

    /// <summary>A flat, near-white surface.</summary>
    White,
}

/// <summary>A selectable background style shown in the Settings picker.</summary>
public sealed record BackgroundOption(string Id, string Name, BackgroundStyle Style)
{
    public static readonly IReadOnlyList<BackgroundOption> All = new[]
    {
        new BackgroundOption("acrylic", "Glass", BackgroundStyle.Acrylic),
        new BackgroundOption("theme",   "Theme", BackgroundStyle.Theme),
        new BackgroundOption("white",   "White", BackgroundStyle.White),
    };

    public const string DefaultId = "acrylic";

    public static BackgroundOption ById(string? id)
    {
        foreach (var o in All)
            if (string.Equals(o.Id, id, System.StringComparison.OrdinalIgnoreCase))
                return o;
        return All[0];
    }
}
