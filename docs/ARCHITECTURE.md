# Architecture Deep-Dive

## Process Model

The application is a single process. Plugins are loaded into isolated `AssemblyLoadContext` instances within that process, not separate processes. This is a deliberate trade-off: process isolation would be safer but adds IPC complexity that is not warranted yet. `AssemblyLoadContext` isolation prevents NuGet dependency version conflicts between plugins.

```
Process: VerseKit.App
│
├── Default AssemblyLoadContext
│     VerseKit.App
│     VerseKit.Core
│     VerseKit.PluginSdk  ← shared, not duplicated
│
├── PluginLoadContext: "ResourceManager"
│     ResourceManager.dll
│     (plugin's own dependencies, isolated)
│
└── PluginLoadContext: "TableBrowser"
      TableBrowser.dll
      (may have different version of a shared library — fine)
```

## Data Flow: Loading a Plugin

```
1. App starts → PluginHost.DiscoverAsync(pluginDirectory)
2. For each .dll found: probe for [Export(typeof(IVerseKitPlugin))]
3. Create PluginLoadContext for each plugin
4. MEF resolves IVerseKitPlugin export
5. Plugin appears in sidebar list (Name, Description, Icon)
6. User clicks plugin → PluginHost.ActivateAsync(pluginId)
7. IVerseKitPlugin.InitializeAsync(connectionProvider, ct) called
8. IVerseKitPlugin.CreateView() returns Avalonia Control
9. Control embedded in new workspace Tab
```

## Data Flow: Making a Dataverse API Call (from a plugin)

```
Plugin
  → await connectionProvider.GetActiveConnectionAsync(ct)
  → returns ServiceClient (already authenticated)
  → plugin calls serviceClient.RetrieveMultiple(query)  [background thread]
  → result returned to plugin
  → plugin updates ViewModel
  → Dispatcher.UIThread.InvokeAsync(() => ...) if UI update needed
```

## Connection State Machine

```
Disconnected
    │ User adds connection profile
    ▼
Connecting (MSAL token acquisition in progress)
    │ Token acquired, ServiceClient.IsReady == true
    ▼
Connected ──────────── Connection lost ──────► Reconnecting
    │                                               │
    │ User switches profile                         │ Retry succeeds
    ▼                                               ▼
Connected (new profile)                         Connected
```

## Settings & Configuration Files

| File | Location | Contents |
|---|---|---|
| `appsettings.json` | App bundle Resources | Default config, Azure AD app IDs for auth |
| `settings.user.json` | `~/.config/versekit/` | User preferences (theme, window size) |
| Connection profiles | macOS Keychain | Encrypted per-profile JSON |
| Plugin directory | `~/.local/share/versekit/plugins/` | Downloaded plugin assemblies |
| Logs | `~/.local/share/versekit/logs/` | Rotating Serilog file sink |

## Threading Model

- **UI thread**: Avalonia Dispatcher thread. Only touch controls here.
- **All CRM calls**: `Task.Run` or `await` continuation on thread pool. `ServiceClient` is thread-safe.
- **Plugin initialization**: Called off UI thread. Plugin must marshal UI updates via `Dispatcher.UIThread.InvokeAsync`.
- **ConnectionManager events**: Fired on the thread pool. Subscribers responsible for marshalling.

## Security Considerations

- Client secrets and certificate paths stored in macOS Keychain only, never in files.
- MSAL token cache stored in memory; persisted to Keychain via `ITokenCache.SetBeforeAccessAsync`.
- No telemetry without explicit user opt-in.
- Plugins are not sandboxed at the OS level — users should only install trusted plugins.
