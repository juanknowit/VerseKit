using System.Text.Json;

namespace VerseKit.Core.Services;

/// <summary>
/// Development-only secret store backed by a plain JSON file.
/// NOT secure — use only on dev machines where Keychain is unavailable
/// (e.g. unsigned builds, CI, unit tests).
/// </summary>
public sealed class FileSecretStore : ISecretStore
{
    private readonly string _filePath;
    private readonly SemaphoreSlim _lock = new(1, 1);

    public FileSecretStore()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".config", "versekit");
        Directory.CreateDirectory(dir);
        _filePath = Path.Combine(dir, "dev-secrets.json");
    }

    public async Task WriteAsync(string account, string secret, CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            var store = await LoadAsync(ct);
            store[account] = secret;
            await SaveAsync(store, ct);
        }
        finally { _lock.Release(); }
    }

    public async Task<string?> ReadAsync(string account, CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            var store = await LoadAsync(ct);
            return store.TryGetValue(account, out var v) ? v : null;
        }
        finally { _lock.Release(); }
    }

    public async Task DeleteAsync(string account, CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            var store = await LoadAsync(ct);
            if (store.Remove(account))
                await SaveAsync(store, ct);
        }
        finally { _lock.Release(); }
    }

    private async Task<Dictionary<string, string>> LoadAsync(CancellationToken ct)
    {
        if (!File.Exists(_filePath)) return new();
        var json = await File.ReadAllTextAsync(_filePath, ct);
        return JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? new();
    }

    private Task SaveAsync(Dictionary<string, string> store, CancellationToken ct) =>
        File.WriteAllTextAsync(_filePath,
            JsonSerializer.Serialize(store, new JsonSerializerOptions { WriteIndented = true }), ct);
}
