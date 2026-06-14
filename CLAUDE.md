# CLAUDE.md — VerseKit

AI development instructions for this project. Read this file at the start of every session.

---

## Project Summary

A macOS-native desktop application for Microsoft Dataverse administrators. It is a **plugin host** for tools that administer Microsoft Dynamics 365 / Power Platform environments.

This project targets macOS using .NET and Avalonia UI.

---

## Non-Negotiable Constraints

1. **No WinForms, no WPF, no Windows APIs.** UI must be Avalonia UI only.
2. **No deprecated WCF-based Dataverse SDK packages.** Use `Microsoft.PowerPlatform.Dataverse.Client` exclusively for Dataverse connectivity.
3. **Authentication must work without a browser popup when a `ServiceClient` connection string / client credentials are provided.** Interactive MSAL flows are opt-in.
4. **Connection *secrets* (client secrets) must be stored in the macOS Keychain.** Non-secret profile metadata (name, URL, tenant, auth method) is JSON under `~/.config/versekit/profiles/`. Never put a secret in those JSON files.
5. **Every plugin runs in its own `AssemblyLoadContext`.** Never load plugin assemblies into the default context.
6. **All async CRM operations must be cancellable** (pass `CancellationToken` everywhere).

---

## Technology Stack

| Concern | Package / Library |
|---|---|
| UI | `Avalonia`, `Avalonia.Desktop`, `Avalonia.Themes.Fluent` |
| CRM SDK | `Microsoft.PowerPlatform.Dataverse.Client` |
| Auth | `Microsoft.Identity.Client` (MSAL) |
| Plugin discovery | `System.Composition` (MEF Lite) |
| DI | `Microsoft.Extensions.DependencyInjection` |
| Logging | `Microsoft.Extensions.Logging`, `Serilog.Extensions.Logging`, `Serilog.Sinks.File` |
| Config | `Microsoft.Extensions.Configuration.Json` |
| Keychain | `Security` framework P/Invoke (see `KeychainService.cs`) |
| Testing | `xunit`, `Moq`, `FluentAssertions` |

---

## Key Source Files (once created)

| File | Role |
|---|---|
| `src/VerseKit.PluginSdk/IVerseKitPlugin.cs` | Plugin contract — all plugins implement this |
| `src/VerseKit.PluginSdk/IConnectionProvider.cs` | How plugins request a `ServiceClient` |
| `src/VerseKit.Core/Services/PluginHost.cs` | MEF-based plugin discovery and lifecycle |
| `src/VerseKit.Core/Services/ConnectionManager.cs` | Manages named connection profiles |
| `src/VerseKit.Core/Services/DataverseClientFactory.cs` | Creates authenticated `ServiceClient` instances |
| `src/VerseKit.Core/Services/KeychainService.cs` | macOS Keychain read/write via P/Invoke |
| `src/VerseKit.App/Views/MainWindow.axaml` | Root Avalonia window |
| `src/VerseKit.App/ViewModels/MainWindowViewModel.cs` | Shell view model |
| `src/VerseKit.App/Theming/ThemeManager.cs` | Applies + persists accent colour & background style |

---

## Design System

**Before changing any UI, read [docs/DESIGN.md](docs/DESIGN.md).** It is the source
of truth for the visual language: design tokens, button/input styles, layout and
surface patterns, and the Avalonia/macOS constraints already solved (shadow
clipping, acrylic limits, focus rings, `IsHitTestVisible`, MVVM `CanExecute`). All
theming lives in `src/VerseKit.App/App.axaml` and is inherited by plugins —
extend the shared styles, don't redefine per-view. Keep new UI consistent with the
DESIGN.md checklist.

## Coding Conventions

- **MVVM pattern** for all Avalonia views. ViewModels in `ViewModels/`, Views in `Views/`.
- **No code-behind logic** — only `InitializeComponent()` and data binding in `.axaml.cs` files.
- **ReactiveUI** for ViewModel bindings (`ReactiveObject`, `ObservableAsPropertyHelper`).
- **Result<T>** pattern for fallible operations — never throw across plugin/host boundary.
- **No `async void`** except Avalonia event handlers. Use `async Task` everywhere else.
- Prefix interfaces with `I`. No Hungarian notation.
- One class per file. File name matches class name.

---

## Plugin SDK Contract (target interface)

```csharp
// VerseKit.PluginSdk
public interface IVerseKitPlugin
{
    string Name { get; }
    string Description { get; }
    string Version { get; }
    Guid PluginId { get; }

    // Called by host after plugin is loaded and connection is ready
    Task InitializeAsync(IConnectionProvider connectionProvider, CancellationToken ct);

    // Returns an Avalonia Control to embed in the workspace tab
    Control CreateView();

    // Called when the host is about to close or switch connections
    Task CleanupAsync();
}

public interface IConnectionProvider
{
    Task<ServiceClient> GetActiveConnectionAsync(CancellationToken ct);
    Task<ServiceClient> RequestConnectionAsync(string reason, CancellationToken ct);
    string ActiveConnectionName { get; }
    IObservable<ServiceClient> ConnectionChanged { get; }
}
```

---

## Connection Flow

```
User clicks "New Connection"
        │
        ▼
ConnectionManager shows dialog:
  - Environment URL
  - Auth method: [Interactive | Client Credentials | Device Code]
        │
        ▼
DataverseClientFactory.CreateAsync(profile)
  → MSAL acquires token (browser popup for Interactive)
  → new ServiceClient(connectionString) or ServiceClient(tokenProvider)
        │
        ▼
ServiceClient.IsReady == true
  → Profile saved to Keychain (encrypted)
  → ConnectionManager.ActiveConnection set
  → All open plugins notified via IConnectionProvider.ConnectionChanged
```

---

## macOS Keychain Storage

Only the client **secret** is stored as a Keychain item; the rest of the profile is JSON on disk (see Constraint 4). The Keychain item is:
- **Service**: `com.versekit.connections`
- **Account**: `{profile-name}`
- **Secret**: JSON-serialized `ConnectionProfile` (URL, auth method, client ID, encrypted secret/cert path)

Use `Security.SecKeychainAddGenericPassword` / `SecKeychainFindGenericPassword` via P/Invoke. See `docs/CONNECTION_MANAGEMENT.md`.

---

## Authentication: What Works on Mac

| Method | Use case | Notes |
|---|---|---|
| Interactive (MSAL + system browser) | Human users, dev machines | Opens default browser for OAuth2 PKCE flow |
| Client Credentials (app secret) | Service accounts, CI | App registration in Azure AD required |
| Client Credentials (certificate) | Production service accounts | Preferred over secrets |
| Device code | Headless / no browser | Prints code, user authenticates on another device |

**Connection string format** (passed to `ServiceClient`):
```
AuthType=OAuth;Url=https://org.crm.dynamics.com;AppId=<guid>;RedirectUri=http://localhost;LoginPrompt=Auto
```

---

## Porting a Windows Plugin

See `docs/PORTING_GUIDE.md`. Key steps:
1. Replace `UserControl` (WinForms) with Avalonia `UserControl`
2. Replace Windows-specific plugin base classes with `IVerseKitPlugin`
3. Replace `Service` (`IOrganizationService`) with `ServiceClient` (same interface, drop-in)
4. Replace Windows-specific UI code (MessageBox, FolderBrowserDialog) with Avalonia equivalents
5. Use MEF `[Export(typeof(IVerseKitPlugin))]` for plugin discovery

---

## Development Workflow

```bash
# Build everything
dotnet build VerseKit.slnx

# Run the app
dotnet run --project src/VerseKit.App

# Run tests
dotnet test

# Add a NuGet package to a project
dotnet add src/VerseKit.Core package <PackageName>

# Publish as macOS app bundle
dotnet publish src/VerseKit.App -r osx-arm64 --self-contained -o ./publish/osx-arm64
```

---

## What NOT to Do

- Do not reference `System.Windows.*` namespaces anywhere.
- Do not use `Thread.Sleep` — use `Task.Delay`.
- Do not store secrets in `appsettings.json` or any plain-text file.
- Do not call `ServiceClient` methods on the UI thread — always `await` on a background context.
- Do not access `Application.Current` from a non-UI thread in Avalonia — use `Dispatcher.UIThread.InvokeAsync`.
- Do not catch `Exception` at the plugin boundary and swallow it — surface via `Result<T>.Failure` or log and rethrow.
- **Do not auto-connect on launch.** Even though MSAL tokens now persist (silent reconnect is available), connecting must stay an explicit user action — selecting a profile each session is a deliberate safety step so nobody unknowingly edits the wrong (e.g. production) environment.

---

## Useful References

- [Avalonia UI Docs](https://docs.avaloniaui.net/)
- [Dataverse SDK (ServiceClient)](https://learn.microsoft.com/en-us/power-apps/developer/data-platform/org-service/overview)
- [MSAL.NET on Mac](https://learn.microsoft.com/en-us/entra/msal/dotnet/getting-started/choosing-msal-dotnet)
- [WebResourcesManager source](https://github.com/MscrmTools/MsCrmTools.WebResourcesManager)
- [Power Platform .NET SDK changelog](https://learn.microsoft.com/en-us/power-apps/developer/data-platform/org-service/release-notes)
