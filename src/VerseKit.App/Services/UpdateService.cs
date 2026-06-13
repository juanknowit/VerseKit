using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace VerseKit.App.Services;

public sealed class UpdateService : IDisposable
{
    private const string Owner = "juanknowit";
    private const string Repo = "VerseKit";
    private static readonly string ApiUrl =
        $"https://api.github.com/repos/{Owner}/{Repo}/releases/latest";

    private readonly HttpClient _http;
    private readonly ILogger<UpdateService> _logger;

    public static Version CurrentVersion { get; } =
        Assembly.GetEntryAssembly()?.GetName().Version ?? new Version(0, 1, 0);

    public UpdateService(ILogger<UpdateService> logger)
    {
        _logger = logger;
        _http = new HttpClient();
        _http.DefaultRequestHeaders.UserAgent.ParseAdd(
            $"VerseKit/{CurrentVersion} (macOS; .NET)");
        _http.Timeout = TimeSpan.FromSeconds(10);
    }

    /// <summary>
    /// Checks GitHub for a newer release. Returns null if already up-to-date or on network error.
    /// </summary>
    public async Task<UpdateInfo?> CheckAsync(CancellationToken ct = default)
    {
        try
        {
            var json = await _http.GetStringAsync(ApiUrl, ct);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var tagName = root.GetProperty("tag_name").GetString() ?? "";
            var htmlUrl = root.GetProperty("html_url").GetString() ?? "";
            var body = root.TryGetProperty("body", out var bodyProp) ? bodyProp.GetString() ?? "" : "";

            if (!Version.TryParse(tagName.TrimStart('v'), out var latest))
            {
                _logger.LogWarning("Could not parse release tag '{Tag}'", tagName);
                return null;
            }

            if (latest <= CurrentVersion)
                return null;

            // Find the macOS app zip and its optional .sha256 sidecar.
            // Stricter naming than "any zip": must be a mac/osx zip whose
            // name identifies this app, so we don't grab an unrelated asset.
            string? downloadUrl = null;
            string? downloadName = null;
            var checksumByName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            if (root.TryGetProperty("assets", out var assets))
            {
                foreach (var asset in assets.EnumerateArray())
                {
                    var name = asset.GetProperty("name").GetString() ?? "";
                    var url = asset.GetProperty("browser_download_url").GetString();
                    if (url is null) continue;

                    if (name.EndsWith(".sha256", StringComparison.OrdinalIgnoreCase))
                    {
                        // Map the zip's name → its checksum asset url.
                        checksumByName[name[..^".sha256".Length]] = url;
                    }
                    else if (downloadUrl is null &&
                             name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) &&
                             name.Contains("versekit", StringComparison.OrdinalIgnoreCase) &&
                             (name.Contains("osx", StringComparison.OrdinalIgnoreCase) ||
                              name.Contains("mac", StringComparison.OrdinalIgnoreCase)))
                    {
                        downloadUrl = url;
                        downloadName = name;
                    }
                }
            }

            string? checksumUrl = null;
            if (downloadName is not null)
                checksumByName.TryGetValue(downloadName, out checksumUrl);

            _logger.LogInformation("Update available: {Current} → {Latest} (checksum: {HasSum})",
                CurrentVersion, latest, checksumUrl is not null);
            return new UpdateInfo(latest.ToString(), htmlUrl, downloadUrl, body, checksumUrl);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Update check failed");
            return null;
        }
    }

    /// <summary>
    /// Downloads the release zip to a temp folder. Reports 0..1 progress.
    /// Returns the local file path.
    /// </summary>
    public async Task<string> DownloadAsync(
        UpdateInfo update,
        IProgress<double> progress,
        CancellationToken ct = default)
    {
        var url = update.DownloadUrl
            ?? throw new InvalidOperationException("No download URL in this release.");

        var fileName = Path.GetFileName(new Uri(url).AbsolutePath);
        var destPath = Path.Combine(Path.GetTempPath(), fileName);

        using var response = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();

        var total = response.Content.Headers.ContentLength ?? -1L;
        await using var src = await response.Content.ReadAsStreamAsync(ct);
        await using var dst = File.Create(destPath);

        var buf = new byte[65536];
        long done = 0;
        int read;
        while ((read = await src.ReadAsync(buf, ct)) > 0)
        {
            await dst.WriteAsync(buf.AsMemory(0, read), ct);
            done += read;
            if (total > 0) progress.Report((double)done / total);
        }
        await dst.DisposeAsync();

        await VerifyChecksumAsync(update, destPath, ct);

        _logger.LogInformation("Downloaded update to {Path}", destPath);
        return destPath;
    }

    /// <summary>
    /// If the release published a .sha256 for this asset, enforce it: a
    /// mismatch deletes the download and throws. If no checksum was
    /// published we can't verify, so we proceed but log a warning.
    /// </summary>
    private async Task VerifyChecksumAsync(UpdateInfo update, string filePath, CancellationToken ct)
    {
        if (update.ChecksumUrl is null)
        {
            _logger.LogWarning("Release has no .sha256 — cannot verify download integrity.");
            return;
        }

        var raw = (await _http.GetStringAsync(update.ChecksumUrl, ct)).Trim();
        // Accept "<hex>" or "<hex>  filename" (sha256sum output format).
        var expected = raw.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries)[0]
            .ToLowerInvariant();

        string actual;
        await using (var fs = File.OpenRead(filePath))
            actual = Convert.ToHexString(await SHA256.HashDataAsync(fs, ct)).ToLowerInvariant();

        if (!CryptographicOperations.FixedTimeEquals(
                Encoding.ASCII.GetBytes(actual), Encoding.ASCII.GetBytes(expected)))
        {
            try { File.Delete(filePath); } catch { /* best effort */ }
            _logger.LogError("Checksum mismatch: expected {Expected}, got {Actual}", expected, actual);
            throw new InvalidOperationException(
                "Downloaded update failed its checksum verification and was discarded.");
        }

        _logger.LogInformation("Checksum verified ({Sum}).", actual);
    }

    /// <summary>
    /// Installs a downloaded update zip in place: extracts it with ditto
    /// (preserves executable bits and symlinks, unlike ZipFile), strips the
    /// quarantine flag, and replaces the current .app bundle. Deleting the
    /// running bundle is safe on macOS — the executable stays memory-mapped.
    /// Returns the path of the installed bundle.
    /// </summary>
    public async Task<string> InstallAsync(string zipPath, CancellationToken ct = default)
    {
        var extractDir = Path.Combine(Path.GetTempPath(),
            "versekit-update-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(extractDir);

        await RunToolAsync("/usr/bin/ditto", ["-xk", zipPath, extractDir], ct);

        var newBundle = Directory.GetDirectories(extractDir, "*.app").FirstOrDefault()
            ?? throw new InvalidOperationException("The update zip contains no .app bundle.");

        await RunToolAsync("/usr/bin/xattr", ["-dr", "com.apple.quarantine", newBundle], ct);

        var installPath = CurrentBundlePath()
            ?? Path.Combine("/Applications", Path.GetFileName(newBundle));

        if (Directory.Exists(installPath))
            Directory.Delete(installPath, recursive: true);
        await RunToolAsync("/usr/bin/ditto", [newBundle, installPath], ct);

        try
        {
            Directory.Delete(extractDir, recursive: true);
            File.Delete(zipPath);
        }
        catch { /* temp cleanup is best effort */ }

        _logger.LogInformation("Installed update to {Path}", installPath);
        return installPath;
    }

    /// <summary>The .app bundle the current process runs from, or null in dev.</summary>
    public static string? CurrentBundlePath()
    {
        // BaseDirectory is <bundle>/Contents/MacOS/
        var bundle = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", ".."));
        return bundle.EndsWith(".app", StringComparison.OrdinalIgnoreCase) ? bundle : null;
    }

    /// <summary>Launches the (new) bundle after a short delay and exits this process.</summary>
    public static void Relaunch(string bundlePath)
    {
        // Pass the path as a positional parameter ($1) rather than
        // interpolating it into the script — the shell never re-parses it,
        // so paths with spaces or quotes are safe and can't inject.
        var psi = new ProcessStartInfo("/bin/sh") { UseShellExecute = false };
        psi.ArgumentList.Add("-c");
        psi.ArgumentList.Add("sleep 1; exec /usr/bin/open \"$1\"");
        psi.ArgumentList.Add("sh");        // $0
        psi.ArgumentList.Add(bundlePath);  // $1
        Process.Start(psi);
        Environment.Exit(0);
    }

    private static async Task RunToolAsync(string tool, string[] args, CancellationToken ct)
    {
        var psi = new ProcessStartInfo(tool)
        {
            UseShellExecute = false,
            RedirectStandardError = true,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);

        using var proc = Process.Start(psi)
            ?? throw new InvalidOperationException($"Could not start {tool}");
        var stderr = await proc.StandardError.ReadToEndAsync(ct);
        await proc.WaitForExitAsync(ct);

        // xattr returns 1 when the attribute simply isn't present — not an error.
        if (proc.ExitCode != 0 && !tool.EndsWith("xattr"))
            throw new InvalidOperationException($"{tool} failed ({proc.ExitCode}): {stderr}");
    }

    /// <summary>Opens the GitHub releases page in the system browser.</summary>
    public static void OpenReleasePage(UpdateInfo update) =>
        Process.Start(new ProcessStartInfo(update.ReleasePageUrl) { UseShellExecute = true });

    public void Dispose() => _http.Dispose();
}
