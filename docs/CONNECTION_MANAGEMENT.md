# Connection Management

## Overview

A **connection profile** stores everything needed to authenticate and connect to a Dynamics 365 environment. Profiles are stored in the macOS Keychain, never in plain-text files.

## Profile Fields

| Field | Description |
|---|---|
| `Name` | Display name, e.g. "Contoso Prod" |
| `EnvironmentUrl` | `https://orgname.crm.dynamics.com` |
| `AuthMethod` | `Interactive`, `ClientCredentials`, or `DeviceCode` |
| `ClientId` | Azure AD app registration client ID |
| `TenantId` | Azure AD tenant ID (optional; speeds up auth) |
| `ClientSecret` | (ClientCredentials only) stored in Keychain |
| `CertificateThumbprint` | (ClientCredentials cert) stored in Keychain |
| `RedirectUri` | Default: `http://localhost` |

## Azure AD App Registration Requirements

All auth methods require an Azure AD App Registration:

1. Go to [portal.azure.com](https://portal.azure.com) → Azure Active Directory → App registrations → New registration
2. Name: e.g. `VerseKit`
3. Redirect URI: `http://localhost` (Public client / native)
4. Under **API permissions**: Add `Dynamics CRM → user_impersonation` (delegated) for interactive auth, or `Dynamics CRM → user_impersonation` (application) for client credentials
5. Grant admin consent

## Auth Methods

### Interactive (Recommended for users)

Opens the system browser for OAuth2 PKCE flow. The user logs in with their Microsoft 365 credentials. Tokens are cached and refreshed silently until the refresh token expires (~90 days).

```
ConnectionString:
AuthType=OAuth;Url=https://org.crm.dynamics.com;AppId=<clientId>;
RedirectUri=http://localhost;LoginPrompt=Auto;TokenCacheStorePath=keychain
```

### Client Credentials (App secret)

No user interaction. Suitable for CI, automation, or shared service accounts.

```
ConnectionString:
AuthType=ClientSecret;Url=https://org.crm.dynamics.com;
ClientId=<clientId>;ClientSecret=<secret>;TenantId=<tenantId>
```

The `ClientSecret` value is retrieved from the Keychain at runtime and never written to disk.

### Device Code

Prints a code and URL to the app UI. User opens the URL on any device and enters the code. Useful when the machine has no browser.

## macOS Keychain Integration

Profile secrets are stored as **Generic Password** items:

| Keychain attribute | Value |
|---|---|
| Service (`kSecAttrService`) | `com.versekit.connections` |
| Account (`kSecAttrAccount`) | `{profile-name}` |
| Label (`kSecAttrLabel`) | `VerseKit - {profile-name}` |
| Secret (`kSecValueData`) | JSON-serialized encrypted profile |

### P/Invoke Sketch

```csharp
[DllImport("/System/Library/Frameworks/Security.framework/Security")]
static extern int SecKeychainAddGenericPassword(
    IntPtr keychain, uint serviceNameLength, string serviceName,
    uint accountNameLength, string accountName,
    uint passwordLength, byte[] passwordData, out IntPtr itemRef);

[DllImport("/System/Library/Frameworks/Security.framework/Security")]
static extern int SecKeychainFindGenericPassword(
    IntPtr keychain, uint serviceNameLength, string serviceName,
    uint accountNameLength, string accountName,
    out uint passwordLength, out IntPtr passwordData, out IntPtr itemRef);
```

Use the `KeychainAccess` NuGet package (`Xamarin.Security.Keychain` or `KeychainAccess`) as a higher-level wrapper to avoid raw P/Invoke.

## MSAL Token Cache Persistence

MSAL acquires tokens and caches them in memory by default. To persist the cache across app restarts (so users don't re-authenticate every launch):

```csharp
publicClientApp.UserTokenCache.SetBeforeAccessAsync(async args =>
{
    var cached = await keychainService.ReadAsync("msal_token_cache", profileName, ct);
    args.TokenCache.DeserializeMsalV3(cached);
});

publicClientApp.UserTokenCache.SetAfterAccessAsync(async args =>
{
    if (args.HasStateChanged)
    {
        var data = args.TokenCache.SerializeMsalV3();
        await keychainService.WriteAsync("msal_token_cache", profileName, data, ct);
    }
});
```

## Testing a Connection

The UI provides a "Test Connection" button that calls:

```csharp
var client = await DataverseClientFactory.CreateAsync(profile, ct);
if (!client.IsReady)
    throw new InvalidOperationException(client.LastError?.Message);

var whoAmI = (WhoAmIResponse)await client.ExecuteAsync(new WhoAmIRequest(), ct);
// Display: "Connected as {whoAmI.UserId} to org {whoAmI.OrganizationId}"
```
