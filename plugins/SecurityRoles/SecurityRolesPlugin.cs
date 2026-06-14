using Avalonia.Controls;
using SecurityRoles.ViewModels;
using SecurityRoles.Views;
using VerseKit.PluginSdk;

namespace SecurityRoles;

/// <summary>
/// Read-only explorer for Dataverse security roles. Lists every role in the
/// environment and, for the selected role, shows who has it — users and teams.
/// </summary>
public sealed class SecurityRolesPlugin : IVerseKitPlugin
{
    public Guid PluginId => new("c3d4e5f6-a7b8-9012-cdef-345678901234");
    public string Name => "Security Roles";
    public string Description => "Browse security roles and see which users and teams are assigned to each — read only";
    public string Version => "1.0.0";

    private IConnectionProvider? _connectionProvider;

    public Task InitializeAsync(IConnectionProvider connectionProvider, CancellationToken ct)
    {
        _connectionProvider = connectionProvider;
        return Task.CompletedTask;
    }

    public Control CreateView()
    {
        var vm = new SecurityRolesViewModel(_connectionProvider!);
        return new SecurityRolesView { DataContext = vm };
    }

    public Task CleanupAsync() => Task.CompletedTask;
}
