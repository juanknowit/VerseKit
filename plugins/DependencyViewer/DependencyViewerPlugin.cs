using Avalonia.Controls;
using DependencyViewer.ViewModels;
using DependencyViewer.Views;
using VerseKit.PluginSdk;

namespace DependencyViewer;

/// <summary>
/// Shows what depends on a table before you delete it — running
/// RetrieveDependenciesForDelete and resolving each dependent component's name.
/// </summary>
public sealed class DependencyViewerPlugin : IVerseKitPlugin
{
    // GUID chosen so the icon-colour hash lands on an unused palette slot (pink).
    public Guid PluginId => new("a7b8c9d0-e1f2-3456-abcd-789012345671");
    public string Name => "Dependency Viewer";
    public string Description => "See what depends on a table before deleting it — read only";
    public string Version => "1.0.0";

    private IConnectionProvider? _connectionProvider;

    public Task InitializeAsync(IConnectionProvider connectionProvider, CancellationToken ct)
    {
        _connectionProvider = connectionProvider;
        return Task.CompletedTask;
    }

    public Control CreateView()
    {
        var vm = new DependencyViewerViewModel(_connectionProvider!);
        return new DependencyViewerView { DataContext = vm };
    }

    public Task CleanupAsync() => Task.CompletedTask;
}
