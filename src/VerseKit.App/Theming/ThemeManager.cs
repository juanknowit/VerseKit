using System;
using System.IO;
using System.Text.Json;
using Avalonia;
using Avalonia.Media;

namespace VerseKit.App.Theming;

/// <summary>
/// Applies the chosen accent <see cref="AccentPreset"/> to the live application
/// resource dictionary (so the UI recolours instantly) and persists the choice
/// to <c>~/.config/versekit/settings.json</c>.
/// </summary>
public static class ThemeManager
{
    private static readonly string SettingsDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".config", "versekit");

    private static readonly string SettingsPath = Path.Combine(SettingsDir, "settings.json");

    public static AccentPreset Current { get; private set; } = AccentPreset.ById(AccentPreset.DefaultId);

    public static BackgroundOption CurrentBackground { get; private set; } =
        BackgroundOption.ById(BackgroundOption.DefaultId);

    /// <summary>Reads the saved accent id; falls back to the default (Blue).</summary>
    public static AccentPreset LoadSavedPreset() =>
        AccentPreset.ById(LoadSettings()?.AccentId);

    /// <summary>Reads the saved background style; falls back to the default (Glass).</summary>
    public static BackgroundOption LoadSavedBackground() =>
        BackgroundOption.ById(LoadSettings()?.BackgroundId);

    private static AppSettings? LoadSettings()
    {
        try
        {
            if (File.Exists(SettingsPath))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(SettingsPath));
        }
        catch
        {
            // Corrupt / unreadable settings should never block launch.
        }
        return null;
    }

    /// <summary>Applies a preset to <see cref="Application.Current"/>'s resources.</summary>
    public static void Apply(AccentPreset preset)
    {
        var app = Application.Current;
        if (app is null) return;

        var r = app.Resources;

        SolidColorBrush Brush(Color c) => new(c);

        r["AccentColor"] = preset.Accent;
        r["AccentBrush"] = Brush(preset.Accent);
        r["AccentHoverBrush"] = Brush(preset.AccentHover);
        r["AccentPressedBrush"] = Brush(preset.AccentPressed);
        r["TintBrush"] = Brush(preset.Tint);
        r["TintHoverBrush"] = Brush(preset.TintHover);
        r["TintPressedBrush"] = Brush(preset.TintPressed);
        r["AccentTextBrush"] = Brush(preset.AccentText);
        r["SelectionBrush"] = Brush(preset.Selection);

        // Fluent theme accent — drives CheckBox marks, ProgressBar fill, etc.
        r["SystemAccentColor"] = preset.Accent;
        r["SystemAccentColorLight1"] = preset.FluentLight1;
        r["SystemAccentColorLight2"] = preset.FluentLight2;
        r["SystemAccentColorLight3"] = preset.FluentLight3;
        r["SystemAccentColorDark1"] = preset.FluentDark1;
        r["SystemAccentColorDark2"] = preset.FluentDark2;
        r["SystemAccentColorDark3"] = preset.FluentDark3;

        Current = preset;

        // The Theme background derives its gradient from the accent, so the
        // window surface must be recomputed whenever the accent changes.
        ApplyWindowSurface();
    }

    /// <summary>Applies the chosen background style to the window surface.</summary>
    public static void ApplyBackground(BackgroundOption option)
    {
        CurrentBackground = option;
        ApplyWindowSurface();
    }

    /// <summary>
    /// Sets the <c>WindowBackgroundBrush</c> (painted behind the floating cards)
    /// and the <c>TitleBarForegroundBrush</c> (chrome text/icons) from the current
    /// accent + background style. AcrylicBlur stays enabled on the window always;
    /// an opaque brush simply covers the blur, so only the brush has to change.
    /// </summary>
    private static void ApplyWindowSurface()
    {
        var app = Application.Current;
        if (app is null) return;

        var r = app.Resources;
        switch (CurrentBackground.Style)
        {
            case BackgroundStyle.Theme:
                r["WindowBackgroundBrush"] = Current.BackgroundGradient;
                r["TitleBarForegroundBrush"] = new SolidColorBrush(Colors.White);
                break;
            case BackgroundStyle.White:
                // Near-white (not pure) so the white content cards still read as
                // distinct surfaces rather than dissolving into the background.
                r["WindowBackgroundBrush"] = new SolidColorBrush(Color.Parse("#F5F5F7"));
                r["TitleBarForegroundBrush"] = new SolidColorBrush(Color.Parse("#3C3C43"));
                break;
            default: // Acrylic — transparent surface lets the blur show through.
                r["WindowBackgroundBrush"] = new SolidColorBrush(Colors.Transparent);
                r["TitleBarForegroundBrush"] = new SolidColorBrush(Colors.White);
                break;
        }
    }

    /// <summary>Applies <paramref name="preset"/> and writes it to disk.</summary>
    public static void Save(AccentPreset preset)
    {
        Apply(preset);
        Persist();
    }

    /// <summary>Applies <paramref name="option"/> and writes it to disk.</summary>
    public static void SaveBackground(BackgroundOption option)
    {
        ApplyBackground(option);
        Persist();
    }

    private static void Persist()
    {
        try
        {
            Directory.CreateDirectory(SettingsDir);
            var json = JsonSerializer.Serialize(
                new AppSettings { AccentId = Current.Id, BackgroundId = CurrentBackground.Id },
                new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(SettingsPath, json);
        }
        catch
        {
            // Persistence is best-effort; the live theme is already applied.
        }
    }

    private sealed class AppSettings
    {
        public string? AccentId { get; set; }
        public string? BackgroundId { get; set; }
    }
}
