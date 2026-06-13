using Microsoft.PowerPlatform.Dataverse.Client;

namespace VerseKit.PluginSdk;

/// <summary>
/// Gives plugins access to Dataverse connections without depending on Core internals.
/// </summary>
public interface IConnectionProvider
{
    string? ActiveConnectionName { get; }

    /// <summary>Returns the current active ServiceClient, or throws if none is connected.</summary>
    Task<ServiceClient> GetActiveConnectionAsync(CancellationToken ct);

    /// <summary>Prompts the user to pick or create a connection (e.g. for multi-env plugins).</summary>
    Task<ServiceClient> RequestConnectionAsync(string reason, CancellationToken ct);

    /// <summary>Fires whenever the active connection changes (switch profile, reconnect, disconnect).</summary>
    IObservable<ServiceClient?> ConnectionChanged { get; }
}
