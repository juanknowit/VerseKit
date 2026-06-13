using Avalonia.Controls;
using TableBrowser.ViewModels;
using TableBrowser.Views;
using VerseKit.PluginSdk;

namespace TableBrowser;

/// <summary>
/// Read-only explorer for Dataverse table (entity) and column (attribute)
/// metadata. A safe, low-risk second tool that exercises the plugin SDK.
/// </summary>
public sealed class TableBrowserPlugin : IVerseKitPlugin
{
    public Guid PluginId => new("b2c3d4e5-f6a7-8901-bcde-f12345678901");
    public string Name => "Table Browser";
    public string Description => "Browse tables (entities) and their columns (attributes) — read only";
    public string Version => "0.1.0";

    private IConnectionProvider? _connectionProvider;

    public Task InitializeAsync(IConnectionProvider connectionProvider, CancellationToken ct)
    {
        _connectionProvider = connectionProvider;
        return Task.CompletedTask;
    }

    public Control CreateView()
    {
        var vm = new TableBrowserViewModel(_connectionProvider!);
        return new TableBrowserView { DataContext = vm };
    }

    public Task CleanupAsync() => Task.CompletedTask;
}
