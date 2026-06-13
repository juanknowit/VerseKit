using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.Logging;
using Microsoft.Identity.Client;
using Microsoft.Identity.Client.Extensions.Msal;
using Microsoft.PowerPlatform.Dataverse.Client;
using VerseKit.Core.Models;

namespace VerseKit.Core.Services;

/// <summary>
/// Creates authenticated ServiceClient instances for a given ConnectionProfile.
/// </summary>
public sealed class DataverseClientFactory(ISecretStore secrets, ILogger<DataverseClientFactory> logger)
{
    // ── Persistent MSAL token cache ──────────────────────────────────
    // Without this, MSAL's cache is in-memory only, so every app launch
    // forces a fresh interactive sign-in. The cache blob is stored on
    // disk and encrypted with a key held in the macOS Keychain, so silent
    // token acquisition works across restarts (no browser popup).
    private const string CacheFileName = "msal.cache";
    private const string CacheKeychainService = "com.versekit.msalcache";
    private const string CacheKeychainAccount = "MSALCache";

    private static readonly string CacheDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".local", "share", "versekit", "msal");

    // Built once; the storage location is independent of client/tenant, so
    // the same helper can back every PublicClientApplication we create.
    // Returns null if persistence can't be set up (e.g. unbundled dev run
    // or a Keychain access denial) — connections then fall back to an
    // in-memory cache, working for the session but not across restarts.
    private readonly Lazy<Task<MsalCacheHelper?>> _cacheHelper =
        new(() => CreateCacheHelperAsync(logger));

    private static async Task<MsalCacheHelper?> CreateCacheHelperAsync(ILogger logger)
    {
        try
        {
            Directory.CreateDirectory(CacheDir);
            var props = new StorageCreationPropertiesBuilder(CacheFileName, CacheDir)
                .WithMacKeyChain(CacheKeychainService, CacheKeychainAccount)
                .Build();
            var helper = await MsalCacheHelper.CreateAsync(props);
            helper.VerifyPersistence(); // throws if the keychain round-trip fails
            logger.LogInformation("MSAL token cache persistence ready at {Dir}", CacheDir);
            return helper;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "Persistent MSAL token cache unavailable — using in-memory cache " +
                "(sign-in won't persist across restarts).");
            return null;
        }
    }

    /// <summary>Builds a public client for interactive / device-code flows
    /// with the persistent token cache registered when available.</summary>
    private async Task<IPublicClientApplication> BuildPublicClientAsync(ConnectionProfile profile)
    {
        var builder = PublicClientApplicationBuilder
            .Create(profile.ClientId)
            .WithAuthority(profile.TenantId is not null
                ? $"https://login.microsoftonline.com/{profile.TenantId}"
                : "https://login.microsoftonline.com/common");

        if (profile.AuthMethod == AuthMethod.Interactive)
            builder = builder.WithRedirectUri(profile.RedirectUri ?? "http://localhost");

        var app = builder.Build();

        var helper = await _cacheHelper.Value;
        helper?.RegisterCache(app.UserTokenCache);
        return app;
    }

    // Shown in the browser tab after the OAuth redirect, replacing MSAL's
    // bare default page. Styled to match the app: blue-gradient VK mark,
    // white card, system font, accent #007AFF.
    private const string BrowserSuccessHtml = """
        <!DOCTYPE html>
        <html>
        <head>
        <meta charset="utf-8">
        <title>VerseKit — Signed in</title>
        <style>
            body {
                margin: 0; min-height: 100vh;
                display: flex; align-items: center; justify-content: center;
                background: #F2F2F7;
                font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', sans-serif;
                color: #1C1C1E;
            }
            .card {
                background: #fff; border-radius: 16px; padding: 48px 56px;
                box-shadow: 0 6px 20px 2px rgba(0,0,0,.12);
                text-align: center; max-width: 380px;
            }
            .logo {
                width: 72px; height: 72px; border-radius: 17px;
                background: linear-gradient(180deg, #409CFF 0%, #005BD8 100%);
                display: flex; align-items: center; justify-content: center;
                margin: 0 auto 20px;
                color: #fff; font-size: 20px; font-weight: 700; letter-spacing: .5px;
            }
            .check { color: #34C759; font-size: 15px; font-weight: 600; margin: 0 0 6px; }
            h1 { font-size: 21px; font-weight: 600; margin: 0 0 8px; }
            p { font-size: 14px; color: #6E6E73; margin: 0; line-height: 1.5; }
        </style>
        </head>
        <body>
            <div class="card">
                <div class="logo">VK</div>
                <div class="check">&#10003;&nbsp;Signed in</div>
                <h1>Authentication complete</h1>
                <p>You're connected. You can close this tab and return to VerseKit.</p>
            </div>
            <script>setTimeout(function () { window.close(); }, 4000);</script>
        </body>
        </html>
        """;

    private const string BrowserErrorHtml = """
        <!DOCTYPE html>
        <html>
        <head>
        <meta charset="utf-8">
        <title>VerseKit — Sign-in failed</title>
        <style>
            body {
                margin: 0; min-height: 100vh;
                display: flex; align-items: center; justify-content: center;
                background: #F2F2F7;
                font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', sans-serif;
                color: #1C1C1E;
            }
            .card {
                background: #fff; border-radius: 16px; padding: 48px 56px;
                box-shadow: 0 6px 20px 2px rgba(0,0,0,.12);
                text-align: center; max-width: 420px;
            }
            .logo {
                width: 72px; height: 72px; border-radius: 17px;
                background: linear-gradient(180deg, #409CFF 0%, #005BD8 100%);
                display: flex; align-items: center; justify-content: center;
                margin: 0 auto 20px;
                color: #fff; font-size: 20px; font-weight: 700; letter-spacing: .5px;
            }
            .err { color: #FF3B30; font-size: 15px; font-weight: 600; margin: 0 0 6px; }
            h1 { font-size: 21px; font-weight: 600; margin: 0 0 8px; }
            p { font-size: 14px; color: #6E6E73; margin: 0 0 10px; line-height: 1.5; }
            code { font-size: 12px; color: #8E8E93; word-break: break-all; }
        </style>
        </head>
        <body>
            <div class="card">
                <div class="logo">VK</div>
                <div class="err">&#10007;&nbsp;Sign-in failed</div>
                <h1>Authentication did not complete</h1>
                <p>Close this tab and try connecting again from VerseKit.</p>
                <code>{0} — {1}</code>
            </div>
        </body>
        </html>
        """;

    /// <param name="forceReauth">Skip the silent token cache and force a fresh
    /// interactive / device-code sign-in (account picker). No effect on the
    /// app-only methods, which always acquire a fresh token anyway.</param>
    public async Task<ServiceClient> CreateAsync(ConnectionProfile profile, CancellationToken ct,
        bool forceReauth = false)
    {
        logger.LogInformation("Creating Dataverse connection for profile '{Name}' (reauth: {Reauth})",
            profile.Name, forceReauth);

        var client = profile.AuthMethod switch
        {
            AuthMethod.Interactive => await CreateInteractiveAsync(profile, ct, forceReauth),
            AuthMethod.ClientSecret => await CreateClientSecretAsync(profile, ct),
            AuthMethod.DeviceCode => await CreateDeviceCodeAsync(profile, ct, forceReauth),
            AuthMethod.Certificate => await CreateCertificateAsync(profile, ct),
            _ => throw new ArgumentOutOfRangeException()
        };

        if (!client.IsReady)
        {
            var error = client.LastError ?? "Unknown error";
            logger.LogError("ServiceClient not ready for '{Name}': {Error}", profile.Name, error);
            throw new InvalidOperationException($"Failed to connect to '{profile.Name}': {error}");
        }

        logger.LogInformation("Connected to '{Name}' successfully", profile.Name);
        return client;
    }

    private async Task<ServiceClient> CreateInteractiveAsync(ConnectionProfile profile, CancellationToken ct,
        bool forceReauth = false)
    {
        var app = await BuildPublicClientAsync(profile);
        var scopes = new[] { $"{profile.EnvironmentUrl}/.default" };

        AuthenticationResult? token = null;
        if (!forceReauth)
        {
            var accounts = await app.GetAccountsAsync();
            if (accounts.Any())
            {
                try
                {
                    token = await app.AcquireTokenSilent(scopes, accounts.First()).ExecuteAsync(ct);
                    logger.LogInformation("Silent token acquired from cache for '{Name}'", profile.Name);
                }
                catch (MsalUiRequiredException) { /* cache miss/expired — fall through to interactive */ }
            }
        }

        if (token is null)
        {
            var request = app.AcquireTokenInteractive(scopes)
                .WithUseEmbeddedWebView(false)
                .WithSystemWebViewOptions(new SystemWebViewOptions
                {
                    HtmlMessageSuccess = BrowserSuccessHtml,
                    HtmlMessageError = BrowserErrorHtml
                });
            // Force the account picker so the user can re-confirm / switch.
            if (forceReauth) request = request.WithPrompt(Prompt.SelectAccount);
            token = await request.ExecuteAsync(ct);
        }

        return BuildServiceClient(profile, app, scopes, token.Account);
    }

    private async Task<ServiceClient> CreateClientSecretAsync(ConnectionProfile profile, CancellationToken ct)
    {
        var secret = await secrets.ReadAsync(profile.Name, ct)
            ?? throw new InvalidOperationException($"No client secret found in Keychain for profile '{profile.Name}'");

        var connString = $"AuthType=ClientSecret;" +
                         $"Url={profile.EnvironmentUrl};" +
                         $"ClientId={profile.ClientId};" +
                         $"ClientSecret={secret};" +
                         (profile.TenantId is not null ? $"TenantId={profile.TenantId};" : "");

        return new ServiceClient(connString, logger: null);
    }

    private async Task<ServiceClient> CreateCertificateAsync(ConnectionProfile profile, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(profile.CertificatePath))
            throw new InvalidOperationException("A certificate (.pfx/.p12) file is required for certificate auth.");
        if (!File.Exists(profile.CertificatePath))
            throw new FileNotFoundException($"Certificate file not found: {profile.CertificatePath}");
        // App-only (client-credentials) auth is tenant-scoped — /common is not valid.
        if (string.IsNullOrWhiteSpace(profile.TenantId))
            throw new InvalidOperationException("Tenant ID is required for certificate (app-only) auth.");

        // Password (if the PFX is protected) is stored in the Keychain.
        var password = await secrets.ReadAsync(profile.Name, ct);
        X509Certificate2 cert;
        try
        {
            cert = X509CertificateLoader.LoadPkcs12FromFile(profile.CertificatePath, password);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"Could not load certificate from '{profile.CertificatePath}'. " +
                "Check the file is a valid PKCS#12 (.pfx/.p12) and the password is correct.", ex);
        }

        var app = ConfidentialClientApplicationBuilder
            .Create(profile.ClientId)
            .WithAuthority($"https://login.microsoftonline.com/{profile.TenantId}")
            .WithCertificate(cert)
            .Build();

        var scopes = new[] { $"{profile.EnvironmentUrl}/.default" };

        // Acquire once up front so a bad cert/registration fails here with a
        // clear message rather than later inside ServiceClient.
        _ = await app.AcquireTokenForClient(scopes).ExecuteAsync(ct);

        return new ServiceClient(
            new Uri(profile.EnvironmentUrl),
            // Client-credentials tokens are app-scoped; MSAL caches and
            // refreshes them in-memory, so no per-user cache is needed.
            async (_resourceUrl) =>
            {
                var result = await app.AcquireTokenForClient(scopes).ExecuteAsync();
                return result.AccessToken;
            },
            useUniqueInstance: true,
            logger: null);
    }

    private async Task<ServiceClient> CreateDeviceCodeAsync(ConnectionProfile profile, CancellationToken ct,
        bool forceReauth = false)
    {
        var app = await BuildPublicClientAsync(profile);
        var scopes = new[] { $"{profile.EnvironmentUrl}/.default" };

        AuthenticationResult? token = null;
        if (!forceReauth)
        {
            var accounts = await app.GetAccountsAsync();
            if (accounts.Any())
            {
                try
                {
                    token = await app.AcquireTokenSilent(scopes, accounts.First()).ExecuteAsync(ct);
                    logger.LogInformation("Silent token acquired from cache for '{Name}'", profile.Name);
                }
                catch (MsalUiRequiredException) { /* fall through to device code */ }
            }
        }

        token ??= await app.AcquireTokenWithDeviceCode(scopes, deviceCodeResult =>
        {
            logger.LogInformation("Device code auth: {Message}", deviceCodeResult.Message);
            Console.WriteLine(deviceCodeResult.Message);
            return Task.CompletedTask;
        }).ExecuteAsync(ct);

        return BuildServiceClient(profile, app, scopes, token.Account);
    }

    /// <summary>Wraps a ServiceClient around a token provider that refreshes
    /// silently from the (now persistent) MSAL cache.</summary>
    private static ServiceClient BuildServiceClient(
        ConnectionProfile profile, IPublicClientApplication app,
        string[] scopes, IAccount account) =>
        new(
            new Uri(profile.EnvironmentUrl),
            async (_resourceUrl) =>
            {
                var result = await app.AcquireTokenSilent(scopes, account).ExecuteAsync();
                return result.AccessToken;
            },
            useUniqueInstance: true,
            logger: null);
}
