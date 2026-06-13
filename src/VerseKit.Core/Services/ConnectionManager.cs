using System.Reactive.Subjects;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.PowerPlatform.Dataverse.Client;
using VerseKit.Core.Models;
using VerseKit.PluginSdk;

namespace VerseKit.Core.Services;

/// <summary>
/// Manages named connection profiles and the active ServiceClient.
/// Implements IConnectionProvider for plugins.
/// </summary>
public sealed class ConnectionManager : IConnectionProvider, IDisposable
{
    private readonly DataverseClientFactory _factory;
    private readonly ISecretStore _secrets;
    private readonly ILogger<ConnectionManager> _logger;
    private readonly string _profilesDir;
    private readonly string _foldersPath;
    // BehaviorSubject replays the current connection to late subscribers —
    // plugins are typically activated AFTER the user connects, so a plain
    // Subject would mean they never learn about the already-active connection.
    private readonly BehaviorSubject<ServiceClient?> _connectionChanged = new(null);

    private ServiceClient? _activeClient;
    private ConnectionProfile? _activeProfile;

    public ConnectionManager(
        DataverseClientFactory factory,
        ISecretStore secrets,
        ILogger<ConnectionManager> logger)
    {
        _factory = factory;
        _secrets = secrets;
        _logger = logger;
        var baseDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".config", "versekit");
        _profilesDir = Path.Combine(baseDir, "profiles");
        // Kept outside the profiles dir so LoadProfilesAsync's *.json scan
        // never tries to parse it as a connection.
        _foldersPath = Path.Combine(baseDir, "folders.json");
        Directory.CreateDirectory(_profilesDir);
    }

    public string? ActiveConnectionName => _activeProfile?.Name;
    public IObservable<ServiceClient?> ConnectionChanged => _connectionChanged;

    public Task<ServiceClient> GetActiveConnectionAsync(CancellationToken ct)
    {
        if (_activeClient is { IsReady: true })
            return Task.FromResult(_activeClient);
        throw new InvalidOperationException("No active connection. Connect to an environment first.");
    }

    public Task<ServiceClient> RequestConnectionAsync(string reason, CancellationToken ct) =>
        GetActiveConnectionAsync(ct);

    /// <summary>
    /// Connects to an environment. Pass <paramref name="secret"/> for the
    /// methods that need one (ClientSecret value, or Certificate password) so
    /// it is stored in the Keychain before the factory reads it.
    /// </summary>
    public async Task ConnectAsync(ConnectionProfile profile, string? secret = null,
        bool forceReauth = false, CancellationToken ct = default)
    {
        var usesSecret = profile.AuthMethod is AuthMethod.ClientSecret or AuthMethod.Certificate;
        if (usesSecret && !string.IsNullOrWhiteSpace(secret))
            await _secrets.WriteAsync(profile.Name, secret, ct);

        // Connect first, swap after: if the new connection fails or is
        // cancelled (abandoned browser login), the current one survives.
        var newClient = await _factory.CreateAsync(profile, ct, forceReauth);

        _activeClient?.Dispose();
        _activeClient = newClient;
        _activeProfile = profile;
        _connectionChanged.OnNext(_activeClient);
        _logger.LogInformation("Active connection set to '{Name}'", profile.Name);
    }

    public void Disconnect()
    {
        _activeClient?.Dispose();
        _activeClient = null;
        _activeProfile = null;
        _connectionChanged.OnNext(null);
    }

    public async Task SaveProfileAsync(ConnectionProfile profile, string? clientSecret = null, CancellationToken ct = default)
    {
        if (!string.IsNullOrWhiteSpace(clientSecret))
            await _secrets.WriteAsync(profile.Name, clientSecret, ct);

        var json = JsonSerializer.Serialize(profile, new JsonSerializerOptions { WriteIndented = true });
        var path = Path.Combine(_profilesDir, $"{SanitizeName(profile.Name)}.json");
        await File.WriteAllTextAsync(path, json, ct);

        // Register the folder so it appears (and groups) in the sidebar even
        // when assigned via the edit form rather than drag-and-drop.
        if (!string.IsNullOrWhiteSpace(profile.Folder))
            await CreateFolderAsync(profile.Folder, ct);

        _logger.LogInformation("Saved profile '{Name}'", profile.Name);
    }

    public async Task<IReadOnlyList<ConnectionProfile>> LoadProfilesAsync(CancellationToken ct)
    {
        var profiles = new List<ConnectionProfile>();
        foreach (var file in Directory.GetFiles(_profilesDir, "*.json"))
        {
            try
            {
                var json = await File.ReadAllTextAsync(file, ct);
                var profile = JsonSerializer.Deserialize<ConnectionProfile>(json);
                if (profile is not null) profiles.Add(profile);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to load profile from '{File}'", file);
            }
        }
        return profiles;
    }

    public async Task DeleteProfileAsync(string name, CancellationToken ct)
    {
        var path = Path.Combine(_profilesDir, $"{SanitizeName(name)}.json");
        if (File.Exists(path)) File.Delete(path);
        await _secrets.DeleteAsync(name, ct);
    }

    // ── Folders ──────────────────────────────────────────────────────
    // The ordered list of folder names is persisted separately so empty
    // folders and their order survive; each profile records its own folder.

    public async Task<List<string>> LoadFoldersAsync(CancellationToken ct = default)
    {
        if (!File.Exists(_foldersPath)) return [];
        try
        {
            var json = await File.ReadAllTextAsync(_foldersPath, ct);
            return JsonSerializer.Deserialize<List<string>>(json) ?? [];
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load folders list");
            return [];
        }
    }

    private async Task SaveFoldersAsync(IEnumerable<string> folders, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(folders, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(_foldersPath, json, ct);
    }

    public async Task CreateFolderAsync(string name, CancellationToken ct = default)
    {
        var folders = await LoadFoldersAsync(ct);
        if (!folders.Contains(name, StringComparer.OrdinalIgnoreCase))
        {
            folders.Add(name);
            await SaveFoldersAsync(folders, ct);
            _logger.LogInformation("Created folder '{Folder}'", name);
        }
    }

    public async Task RenameFolderAsync(string oldName, string newName, CancellationToken ct = default)
    {
        var folders = await LoadFoldersAsync(ct);
        var idx = folders.FindIndex(f => string.Equals(f, oldName, StringComparison.OrdinalIgnoreCase));
        if (idx >= 0) folders[idx] = newName; else folders.Add(newName);
        await SaveFoldersAsync(folders, ct);

        foreach (var p in await LoadProfilesAsync(ct))
            if (string.Equals(p.Folder, oldName, StringComparison.OrdinalIgnoreCase))
                await SaveProfileAsync(WithFolder(p, newName), ct: ct);
    }

    /// <summary>Removes the folder; its connections move to ungrouped (never deleted).</summary>
    public async Task DeleteFolderAsync(string name, CancellationToken ct = default)
    {
        var folders = await LoadFoldersAsync(ct);
        folders.RemoveAll(f => string.Equals(f, name, StringComparison.OrdinalIgnoreCase));
        await SaveFoldersAsync(folders, ct);

        foreach (var p in await LoadProfilesAsync(ct))
            if (string.Equals(p.Folder, name, StringComparison.OrdinalIgnoreCase))
                await SaveProfileAsync(WithFolder(p, null), ct: ct);

        _logger.LogInformation("Deleted folder '{Folder}' (connections moved to ungrouped)", name);
    }

    /// <summary>Moves a connection into a folder (null/empty = ungrouped).</summary>
    public async Task MoveProfileToFolderAsync(string profileName, string? folder, CancellationToken ct = default)
    {
        var path = Path.Combine(_profilesDir, $"{SanitizeName(profileName)}.json");
        if (!File.Exists(path)) return;

        var profile = JsonSerializer.Deserialize<ConnectionProfile>(await File.ReadAllTextAsync(path, ct));
        if (profile is null) return;

        var target = string.IsNullOrWhiteSpace(folder) ? null : folder;
        await SaveProfileAsync(WithFolder(profile, target), ct: ct);
        if (target is not null) await CreateFolderAsync(target, ct);
    }

    /// <summary>Copy a profile with a different folder (ConnectionProfile is immutable).</summary>
    private static ConnectionProfile WithFolder(ConnectionProfile p, string? folder) => new()
    {
        Name = p.Name,
        EnvironmentUrl = p.EnvironmentUrl,
        AuthMethod = p.AuthMethod,
        ClientId = p.ClientId,
        TenantId = p.TenantId,
        RedirectUri = p.RedirectUri,
        CertificatePath = p.CertificatePath,
        Folder = folder
    };

    private static string SanitizeName(string name) =>
        string.Concat(name.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));

    public void Dispose()
    {
        _activeClient?.Dispose();
        _connectionChanged.Dispose();
    }
}
