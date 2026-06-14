# VerseKit

A macOS toolkit for Microsoft Dataverse administrators.

VerseKit is a native desktop plugin host for tools that administer Microsoft Dynamics 365 and Power Platform Dataverse environments. It is built with .NET and Avalonia for macOS, with isolated plugin loading, cancellable Dataverse operations, and profile secrets stored in the macOS Keychain.

VerseKit is not affiliated with, endorsed by, or sponsored by Microsoft.

> **Status:** Early but functional. The shell, connection management, Table Browser, Resource Manager, and Security Roles plugins are present. Release builds are ad-hoc signed but not yet notarized (see [Installing a Release](#installing-a-release)).

## Technology Stack

| Layer | Choice |
|---|---|
| Runtime | .NET 10 |
| UI | Avalonia UI |
| Dataverse connectivity | `Microsoft.PowerPlatform.Dataverse.Client` |
| Authentication | `Microsoft.Identity.Client` |
| Plugin loading | Isolated `AssemblyLoadContext` per plugin |
| Dependency injection | `Microsoft.Extensions.DependencyInjection` |
| Logging | `Microsoft.Extensions.Logging` + Serilog |
| Secret storage | macOS Keychain via Security framework P/Invoke |

## Project Structure

```text
VerseKit/
├── VerseKit.slnx
├── src/
│   ├── VerseKit.App/          # Avalonia shell application
│   ├── VerseKit.Core/         # connection, plugin host, services
│   └── VerseKit.PluginSdk/    # plugin contract
├── plugins/
│   ├── ResourceManager/
│   ├── SecurityRoles/
│   └── TableBrowser/
├── docs/
└── scripts/
```

## Installing a Release

Download the zip for your Mac from the [latest release](https://github.com/juanknowit/VerseKit/releases/latest):

- **Apple Silicon (M1/M2/M3/M4):** `…-osx-arm64.zip`
- **Intel Mac:** `…-osx-x64.zip`

Unzip and move `VerseKit.app` to `/Applications`. Because the app is ad-hoc
signed but **not notarized**, macOS adds a download-quarantine flag and reports
it as "damaged" on first launch. Clear the flag once, then open normally:

```bash
xattr -cr "/Applications/VerseKit.app"
```

Notarization (which removes this step entirely) requires an Apple Developer ID
and is not yet set up.

## Development

### Prerequisites

- macOS 13 Ventura or later
- .NET 10 SDK
- Xcode Command Line Tools
- Access to a Microsoft Dataverse environment
- An Entra ID app registration for Dataverse authentication

### Build And Run

```bash
dotnet restore VerseKit.slnx
dotnet build VerseKit.slnx
dotnet run --project src/VerseKit.App
```

### Tests

```bash
dotnet test
```

### Bundle

```bash
make bundle
```

## Connection Storage

Client secrets and certificate passwords are stored in the macOS Keychain. Non-secret profile metadata is stored as JSON under `~/.config/versekit/profiles/`.

VerseKit does not auto-connect on launch. Selecting a profile each session is intentional so administrators do not unknowingly operate against the wrong environment.

## Plugin Model

Plugins implement `IVerseKitPlugin` from `VerseKit.PluginSdk`. Each plugin is loaded into its own `AssemblyLoadContext` and receives Dataverse access through `IConnectionProvider`.

Plugins run in-process with full trust. Install only plugins you trust.

## Documentation

- [Architecture](docs/ARCHITECTURE.md)
- [Connection management](docs/CONNECTION_MANAGEMENT.md)
- [Design system](docs/DESIGN.md)
- [Plugin development](docs/PLUGIN_DEVELOPMENT.md)
- [Porting guide](docs/PORTING_GUIDE.md)

## License

MIT. See [LICENSE](LICENSE).
