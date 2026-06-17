using Avalonia.Controls;
using FlowRuns.ViewModels;
using FlowRuns.Views;
using VerseKit.PluginSdk;

namespace FlowRuns;

/// <summary>
/// Lists cloud flow run history (success / failure) from the Dataverse
/// <c>flowrun</c> table and exports it to CSV or Excel — read only.
/// </summary>
public sealed class FlowRunsPlugin : IVerseKitPlugin
{
    // GUID chosen so the icon-colour hash lands on the one unused palette slot (teal).
    public Guid PluginId => new("b8c9d0e1-f2a3-4567-bcde-000000000000");
    public string Name => "Flow Runs";
    public string Description => "View and export cloud flow run history — succeeded and failed — read only";
    public string Version => "1.0.0";

    private IConnectionProvider? _connectionProvider;

    public Task InitializeAsync(IConnectionProvider connectionProvider, CancellationToken ct)
    {
        _connectionProvider = connectionProvider;
        return Task.CompletedTask;
    }

    public Control CreateView()
    {
        var vm = new FlowRunsViewModel(_connectionProvider!);
        return new FlowRunsView { DataContext = vm };
    }

    public Task CleanupAsync() => Task.CompletedTask;
}
