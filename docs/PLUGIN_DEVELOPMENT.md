# Plugin Development Guide

## Minimal Plugin

Create a .NET 8 class library project and reference the `VerseKit.PluginSdk` NuGet package.

```xml
<!-- MyPlugin.csproj -->
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="VerseKit.PluginSdk" Version="1.0.0" />
    <PackageReference Include="Avalonia" Version="11.*" />
  </ItemGroup>
</Project>
```

```csharp
using System.Composition;
using Avalonia.Controls;
using Microsoft.PowerPlatform.Dataverse.Client;
using VerseKit.PluginSdk;

[Export(typeof(IVerseKitPlugin))]
public class MyPlugin : IVerseKitPlugin
{
    public string Name => "My Plugin";
    public string Description => "Does something useful";
    public string Version => "1.0.0";
    public Guid PluginId => new("xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx");

    private IConnectionProvider? _connectionProvider;

    public async Task InitializeAsync(IConnectionProvider connectionProvider, CancellationToken ct)
    {
        _connectionProvider = connectionProvider;
        // Optionally pre-fetch data here
    }

    public Control CreateView() => new MyPluginView { DataContext = new MyPluginViewModel(_connectionProvider!) };

    public Task CleanupAsync() => Task.CompletedTask;
}
```

## Calling the Dataverse API

```csharp
// In a ViewModel or service class — never on the UI thread
var client = await _connectionProvider.GetActiveConnectionAsync(ct);

var query = new QueryExpression("account")
{
    ColumnSet = new ColumnSet("name", "emailaddress1"),
    TopCount = 50
};

var results = await client.RetrieveMultipleAsync(query, ct);
```

## Reacting to Connection Changes

```csharp
// In InitializeAsync or constructor
connectionProvider.ConnectionChanged
    .Subscribe(async newClient =>
    {
        // Connection was switched — reload your data
        await LoadDataAsync(newClient, CancellationToken.None);
    });
```

## Requesting a New Connection

If your plugin needs the user to pick a connection (e.g. to compare two environments):

```csharp
var secondClient = await _connectionProvider.RequestConnectionAsync(
    "Select the target environment to compare against", ct);
```

## Plugin Directory Layout

```
~/.local/share/versekit/plugins/
└── MyPlugin/
    ├── MyPlugin.dll
    ├── MyPlugin.deps.json
    └── (plugin's private dependencies)
```

The host scans each subdirectory, creates an `AssemblyLoadContext`, and probes for `IVerseKitPlugin` exports.

## Avalonia UI Tips

- Use AXAML for layouts: `UserControl` with `DataContext` bound to a ViewModel.
- Use `ReactiveUI` (`ReactiveObject`) for ViewModels.
- Marshal updates to UI thread: `Dispatcher.UIThread.InvokeAsync(() => MyCollection.Add(item))`.
- Avalonia `DataGrid` is the equivalent of WinForms `DataGridView`.
- Use `Avalonia.Svg` for scalable icons.

## Common Dataverse Operations

```csharp
// Retrieve a single record
var account = await client.RetrieveAsync("account", accountId, new ColumnSet("name"), ct);

// Create a record
var entity = new Entity("account") { ["name"] = "Contoso" };
var newId = await client.CreateAsync(entity, ct);

// Update
var update = new Entity("account", accountId) { ["name"] = "Contoso Ltd" };
await client.UpdateAsync(update, ct);

// Delete
await client.DeleteAsync("account", accountId, ct);

// Execute a message
var req = new WhoAmIRequest();
var resp = (WhoAmIResponse)await client.ExecuteAsync(req, ct);
```

## Packaging for Distribution

```bash
dotnet publish MyPlugin -r osx-arm64 --no-self-contained -o dist/MyPlugin/
# Copy dist/MyPlugin/ into ~/.local/share/versekit/plugins/MyPlugin/
```
