using Avalonia.Controls;
using AccessChecker.ViewModels;
using AccessChecker.Views;
using VerseKit.PluginSdk;

namespace AccessChecker;

/// <summary>
/// Shows a user's *effective* table privileges — the access they actually have,
/// aggregated across all their security roles and team memberships — so an admin
/// can answer "what can this user do?" without manually combining roles.
/// </summary>
public sealed class AccessCheckerPlugin : IVerseKitPlugin
{
    public Guid PluginId => new("d4e5f6a7-b8c9-0123-defa-456789012345");
    public string Name => "Access Checker";
    public string Description => "See a user's effective table privileges across all their roles and teams — read only";
    public string Version => "1.1.0";

    private IConnectionProvider? _connectionProvider;

    public Task InitializeAsync(IConnectionProvider connectionProvider, CancellationToken ct)
    {
        _connectionProvider = connectionProvider;
        return Task.CompletedTask;
    }

    public Control CreateView()
    {
        var vm = new AccessCheckerViewModel(_connectionProvider!);
        return new AccessCheckerView { DataContext = vm };
    }

    public Task CleanupAsync() => Task.CompletedTask;
}
