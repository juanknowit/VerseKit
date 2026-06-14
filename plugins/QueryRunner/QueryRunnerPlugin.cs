using Avalonia.Controls;
using QueryRunner.ViewModels;
using QueryRunner.Views;
using VerseKit.PluginSdk;

namespace QueryRunner;

/// <summary>
/// Runs FetchXML or OData (Web API) queries and shows the results in a grid,
/// with CSV / Excel export. Read-only.
/// </summary>
public sealed class QueryRunnerPlugin : IVerseKitPlugin
{
    // GUID chosen so the icon-colour hash lands on an unused palette slot (indigo).
    public Guid PluginId => new("f6a7b8c9-d0e1-2345-fabc-678901234568");
    public string Name => "Query Runner";
    public string Description => "Run FetchXML or OData queries, view results in a grid, and export to CSV/Excel — read only";
    public string Version => "1.1.0";

    private IConnectionProvider? _connectionProvider;

    public Task InitializeAsync(IConnectionProvider connectionProvider, CancellationToken ct)
    {
        _connectionProvider = connectionProvider;
        return Task.CompletedTask;
    }

    public Control CreateView()
    {
        var vm = new QueryRunnerViewModel(_connectionProvider!);
        return new QueryRunnerView { DataContext = vm };
    }

    public Task CleanupAsync() => Task.CompletedTask;
}
