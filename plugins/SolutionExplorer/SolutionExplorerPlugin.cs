using Avalonia.Controls;
using SolutionExplorer.ViewModels;
using SolutionExplorer.Views;
using VerseKit.PluginSdk;

namespace SolutionExplorer;

/// <summary>
/// Browses the environment's solutions, shows what each one contains (a
/// component breakdown by type), and exports a solution to disk as a managed
/// or unmanaged .zip.
/// </summary>
public sealed class SolutionExplorerPlugin : IVerseKitPlugin
{
    public Guid PluginId => new("e5f6a7b8-c9d0-1234-efab-567890123456");
    public string Name => "Solution Explorer";
    public string Description => "Browse solutions, see their components, and export managed/unmanaged to disk";
    public string Version => "1.1.0";

    private IConnectionProvider? _connectionProvider;

    public Task InitializeAsync(IConnectionProvider connectionProvider, CancellationToken ct)
    {
        _connectionProvider = connectionProvider;
        return Task.CompletedTask;
    }

    public Control CreateView()
    {
        var vm = new SolutionExplorerViewModel(_connectionProvider!);
        return new SolutionExplorerView { DataContext = vm };
    }

    public Task CleanupAsync() => Task.CompletedTask;
}
