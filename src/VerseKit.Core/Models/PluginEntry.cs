using VerseKit.PluginSdk;

namespace VerseKit.Core.Models;

/// <summary>Where a plugin was loaded from.</summary>
public enum PluginOrigin
{
    /// <summary>Shipped inside the .app bundle (read-only, can't be removed).</summary>
    Bundled,

    /// <summary>Installed by the user under ~/.local/share/versekit/plugins (removable).</summary>
    User,
}

public sealed class PluginEntry
{
    public required IVerseKitPlugin Plugin { get; init; }
    public required string AssemblyPath { get; init; }
    public PluginOrigin Origin { get; init; } = PluginOrigin.User;

    /// <summary>The folder containing the plugin assembly (and its dependencies).</summary>
    public string PluginDirectory => Path.GetDirectoryName(AssemblyPath)!;

    public bool IsActivated { get; set; }
}
