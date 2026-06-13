using Avalonia.Controls;

namespace VerseKit.PluginSdk;

/// <summary>
/// Contract every VerseKit plugin must implement.
/// Export with [Export(typeof(IVerseKitPlugin))] for MEF discovery.
/// </summary>
public interface IVerseKitPlugin
{
    Guid PluginId { get; }
    string Name { get; }
    string Description { get; }
    string Version { get; }

    /// <summary>Called once after the plugin is loaded and a connection is available.</summary>
    Task InitializeAsync(IConnectionProvider connectionProvider, CancellationToken ct);

    /// <summary>Returns the Avalonia Control to embed in the workspace tab.</summary>
    Control CreateView();

    /// <summary>Called before the host closes or unloads the plugin.</summary>
    Task CleanupAsync();
}
