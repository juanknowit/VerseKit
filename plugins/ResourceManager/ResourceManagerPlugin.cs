using Avalonia.Controls;
using VerseKit.PluginSdk;
using ResourceManager.Views;
using ResourceManager.ViewModels;

namespace ResourceManager;

/// <summary>
/// Plugin that lists and manages Dynamics 365 web resources.
/// Ported concept from https://github.com/MscrmTools/MsCrmTools.WebResourcesManager
/// </summary>
public sealed class ResourceManagerPlugin : IVerseKitPlugin
{
    public Guid PluginId => new("a1b2c3d4-e5f6-7890-abcd-ef1234567890");
    public string Name => "Resource Manager";
    public string Description => "View, create, and update web resources in your Dynamics 365 environment";
    public string Version => "1.0.0";

    private IConnectionProvider? _connectionProvider;

    public Task InitializeAsync(IConnectionProvider connectionProvider, CancellationToken ct)
    {
        _connectionProvider = connectionProvider;
        return Task.CompletedTask;
    }

    public Control CreateView()
    {
        var vm = new WebResourcesViewModel(_connectionProvider!);
        return new WebResourcesView { DataContext = vm };
    }

    public Task CleanupAsync() => Task.CompletedTask;
}
