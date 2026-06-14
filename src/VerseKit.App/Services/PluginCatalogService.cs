using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;

namespace VerseKit.App.Services;

/// <summary>One installable plugin in the registry manifest.</summary>
public sealed record PluginRegistryEntry(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("description")] string Description,
    [property: JsonPropertyName("version")] string Version,
    [property: JsonPropertyName("author")] string? Author,
    [property: JsonPropertyName("downloadUrl")] string DownloadUrl,
    [property: JsonPropertyName("sha256")] string? Sha256);

/// <summary>
/// Fetches the VerseKit plugin registry (a JSON manifest in the repo) and
/// installs a plugin by downloading its zip, verifying the checksum, and
/// extracting it into the user plugins root. No manual folder handling.
/// </summary>
public sealed class PluginCatalogService : IDisposable
{
    // Raw manifest committed at registry/plugins.json on the default branch.
    private const string RegistryUrl =
        "https://raw.githubusercontent.com/juanknowit/VerseKit/main/registry/plugins.json";

    private readonly HttpClient _http;
    private readonly ILogger<PluginCatalogService> _logger;

    public PluginCatalogService(ILogger<PluginCatalogService> logger)
    {
        _logger = logger;
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("VerseKit (macOS; .NET)");
    }

    /// <summary>Downloads and parses the registry. Returns an empty list on failure.</summary>
    public async Task<IReadOnlyList<PluginRegistryEntry>> FetchAsync(CancellationToken ct = default)
    {
        try
        {
            var json = await _http.GetStringAsync(RegistryUrl, ct);
            var doc = JsonSerializer.Deserialize<RegistryDocument>(json);
            return doc?.Plugins ?? [];
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to fetch plugin registry");
            return [];
        }
    }

    /// <summary>
    /// Downloads <paramref name="entry"/>'s zip, verifies its sha256 (if the
    /// manifest provides one), extracts it, and copies the plugin folder into
    /// <paramref name="userPluginsRoot"/>. Returns the installed folder path.
    /// </summary>
    public async Task<string> InstallAsync(
        PluginRegistryEntry entry, string userPluginsRoot,
        IProgress<double>? progress = null, CancellationToken ct = default)
    {
        var tmpZip = Path.Combine(Path.GetTempPath(), $"versekit-plugin-{Guid.NewGuid():N}.zip");
        var extractDir = Path.Combine(Path.GetTempPath(), $"versekit-plugin-{Guid.NewGuid():N}");

        try
        {
            await DownloadAsync(entry.DownloadUrl, tmpZip, progress, ct);
            VerifyChecksum(entry, tmpZip);

            Directory.CreateDirectory(extractDir);
            ZipFile.ExtractToDirectory(tmpZip, extractDir, overwriteFiles: true);

            // A well-formed package is a single top folder (PluginName/PluginName.dll).
            // If the zip dumped files loose, treat the extract dir itself as the folder.
            var dirs = Directory.GetDirectories(extractDir);
            var source = dirs.Length == 1 ? dirs[0] : extractDir;
            var folderName = dirs.Length == 1 ? Path.GetFileName(source) : SanitizeFolder(entry.Name);

            var dest = Path.Combine(userPluginsRoot, folderName);
            Directory.CreateDirectory(userPluginsRoot);
            if (Directory.Exists(dest)) Directory.Delete(dest, recursive: true);
            CopyDirectory(source, dest);

            _logger.LogInformation("Installed plugin '{Name}' v{Version} to {Dest}",
                entry.Name, entry.Version, dest);
            return dest;
        }
        finally
        {
            try { if (File.Exists(tmpZip)) File.Delete(tmpZip); } catch { /* best effort */ }
            try { if (Directory.Exists(extractDir)) Directory.Delete(extractDir, recursive: true); } catch { /* best effort */ }
        }
    }

    private async Task DownloadAsync(string url, string destPath, IProgress<double>? progress, CancellationToken ct)
    {
        using var response = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();

        var total = response.Content.Headers.ContentLength ?? -1L;
        await using var src = await response.Content.ReadAsStreamAsync(ct);
        await using var dst = File.Create(destPath);

        var buffer = new byte[65536];
        long done = 0;
        int read;
        while ((read = await src.ReadAsync(buffer, ct)) > 0)
        {
            await dst.WriteAsync(buffer.AsMemory(0, read), ct);
            done += read;
            if (total > 0) progress?.Report((double)done / total);
        }
    }

    private void VerifyChecksum(PluginRegistryEntry entry, string zipPath)
    {
        if (string.IsNullOrWhiteSpace(entry.Sha256))
        {
            _logger.LogWarning("Registry entry '{Name}' has no sha256 — installing unverified.", entry.Name);
            return;
        }

        using var fs = File.OpenRead(zipPath);
        var actual = Convert.ToHexString(SHA256.HashData(fs)).ToLowerInvariant();
        var expected = entry.Sha256.Trim().ToLowerInvariant();
        if (!CryptographicOperations.FixedTimeEquals(
                System.Text.Encoding.ASCII.GetBytes(actual),
                System.Text.Encoding.ASCII.GetBytes(expected)))
        {
            throw new InvalidOperationException(
                $"Checksum mismatch for '{entry.Name}'. The download was rejected.");
        }
    }

    private static string SanitizeFolder(string name) =>
        string.Concat(name.Split(Path.GetInvalidFileNameChars())).Replace(" ", "");

    private static void CopyDirectory(string source, string dest)
    {
        Directory.CreateDirectory(dest);
        foreach (var file in Directory.GetFiles(source))
            File.Copy(file, Path.Combine(dest, Path.GetFileName(file)), overwrite: true);
        foreach (var sub in Directory.GetDirectories(source))
            CopyDirectory(sub, Path.Combine(dest, Path.GetFileName(sub)));
    }

    public void Dispose() => _http.Dispose();

    private sealed class RegistryDocument
    {
        [JsonPropertyName("plugins")]
        public List<PluginRegistryEntry>? Plugins { get; set; }
    }
}
