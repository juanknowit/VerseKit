using Avalonia.Controls;
using VerseKit.PluginSdk;
using WebResourcesManager.Views;
using WebResourcesManager.ViewModels;

namespace WebResourcesManager;

/// <summary>
/// Plugin that lists and manages Dynamics 365 web resources.
/// Ported concept from https://github.com/MscrmTools/MsCrmTools.WebResourcesManager
/// </summary>
public sealed class WebResourcesManagerPlugin : IVerseKitPlugin
{
    public Guid PluginId => new("a1b2c3d4-e5f6-7890-abcd-ef1234567890");
    public string Name => "Web Resources Manager";
    public string Description => "View, create, and update web resources in your Dynamics 365 environment";
    public string Version => "0.1.0";

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
