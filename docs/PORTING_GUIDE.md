# Porting A Windows Dataverse Plugin To Mac

This guide walks through converting an existing C# WinForms Dataverse administration plugin to run as a VerseKit Avalonia plugin.

## Step 1 — Retarget the Project

Change the target framework and remove Windows-only references.

```xml
<!-- Before -->
<TargetFramework>net472</TargetFramework>

<!-- After -->
<TargetFramework>net8.0</TargetFramework>
```

Remove packages:
- Legacy host-specific extensibility packages (replaced by `VerseKit.PluginSdk`)
- `Microsoft.CrmSdk.*` (replaced by `Microsoft.PowerPlatform.Dataverse.Client`)
- `System.Windows.Forms` (replaced by Avalonia)

Add packages:
- `VerseKit.PluginSdk`
- `Microsoft.PowerPlatform.Dataverse.Client`
- `Avalonia`
- `Avalonia.ReactiveUI` (optional, recommended)

## Step 2 — Replace the Plugin Base Class

```csharp
// Before (WinForms host-specific plugin)
public class MyPlugin : PluginBase
{
    public override object GetControl() => new MyPluginControl();
}

public class MyPluginControl : PluginControlBase
{
    // WinForms control
}

// After (VerseKit)
[Export(typeof(IVerseKitPlugin))]
public class MyPlugin : IVerseKitPlugin
{
    public string Name => "My Plugin";
    public string Description => "...";
    public string Version => "1.0.0";
    public Guid PluginId => new("...");

    private IConnectionProvider? _conn;

    public Task InitializeAsync(IConnectionProvider conn, CancellationToken ct)
    {
        _conn = conn;
        return Task.CompletedTask;
    }

    public Control CreateView() => new MyPluginView { DataContext = new MyPluginViewModel(_conn!) };
    public Task CleanupAsync() => Task.CompletedTask;
}
```

## Step 3 — Convert WinForms UI to Avalonia

### Controls mapping

| WinForms | Avalonia |
|---|---|
| `Form` | `Window` |
| `UserControl` | `UserControl` (Avalonia) |
| `Panel`, `GroupBox` | `Panel`, `Border` |
| `DataGridView` | `DataGrid` |
| `TreeView` / `TreeNode` | `TreeView` / `TreeViewItem` |
| `ListView` | `ListBox` or `DataGrid` |
| `ToolStrip` / `MenuStrip` | `Menu`, `ToolBar` |
| `Button` | `Button` |
| `TextBox` | `TextBox` |
| `ComboBox` | `ComboBox` |
| `CheckBox` | `CheckBox` |
| `Label` | `TextBlock` |
| `ProgressBar` | `ProgressBar` |
| `TabControl` / `TabPage` | `TabControl` / `TabItem` |
| `SplitContainer` | `Grid` with `GridSplitter` |
| `ContextMenuStrip` | `ContextMenu` |
| `ToolTip` | `ToolTip` |
| `OpenFileDialog` | `StorageProvider.OpenFilePickerAsync` |
| `SaveFileDialog` | `StorageProvider.SaveFilePickerAsync` |
| `FolderBrowserDialog` | `StorageProvider.OpenFolderPickerAsync` |
| `MessageBox.Show` | Custom Avalonia dialog or `MessageBoxManager` (community) |

### Layout mapping

```csharp
// WinForms: control.Dock = DockStyle.Fill
// Avalonia AXAML:
<Grid>
    <MyControl HorizontalAlignment="Stretch" VerticalAlignment="Stretch" />
</Grid>

// WinForms: FlowLayoutPanel
// Avalonia: WrapPanel or StackPanel

// WinForms: TableLayoutPanel
// Avalonia: Grid with RowDefinitions/ColumnDefinitions
```

## Step 4 — Replace `IOrganizationService` calls

The API surface is nearly identical. `ServiceClient` implements `IOrganizationServiceAsync2`:

```csharp
// Before
IOrganizationService service = this.Service;
var results = service.RetrieveMultiple(query);

// After
var client = await _connectionProvider.GetActiveConnectionAsync(ct);
var results = await client.RetrieveMultipleAsync(query, ct);
```

Key differences:
- All methods now have `Async` variants — prefer them.
- Pass `CancellationToken` to every call.
- `ServiceClient` is already thread-safe; no need to lock.

## Step 5 — Move Business Logic to ViewModels

WinForms plugins often mix logic and UI in the same class. Avalonia works best with MVVM:

```csharp
// Move data fetching / CRM calls into a ViewModel
public class MyPluginViewModel : ReactiveObject
{
    private readonly IConnectionProvider _connectionProvider;
    public ObservableCollection<AccountRow> Accounts { get; } = new();

    public async Task LoadAsync(CancellationToken ct)
    {
        var client = await _connectionProvider.GetActiveConnectionAsync(ct);
        var results = await client.RetrieveMultipleAsync(/* ... */, ct);
        Dispatcher.UIThread.Post(() =>
        {
            Accounts.Clear();
            foreach (var e in results.Entities)
                Accounts.Add(new AccountRow(e));
        });
    }
}
```

## Step 6 — File System Paths

Replace Windows-specific paths:

```csharp
// Before
var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

// After — use XDG-style paths on Mac
var configDir = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
    ".config", "versekit");
```

## Step 7 — Test

1. `dotnet build` — fix any remaining Windows-only API references
2. Copy output to `~/.local/share/versekit/plugins/MyPlugin/`
3. Launch VerseKit and verify the plugin appears in the sidebar
4. Establish a connection and activate the plugin
5. Exercise all major workflows

## Common Gotchas

- **`[STAThread]`** — Not needed on Mac. Remove it.
- **`Application.DoEvents()`** — Remove. Use `async/await` instead.
- **`Invoke` / `BeginInvoke` (WinForms)** — Replace with `Dispatcher.UIThread.InvokeAsync`.
- **`ThreadPool.QueueUserWorkItem`** — Replace with `Task.Run`.
- **Registry access** — Not available on Mac. Use config files or Keychain.
- **COM interop** — Not available on Mac. Find managed alternatives.
- **`System.Drawing` (GDI+)`** — Available on Mac via `System.Drawing.Common`, but prefer Avalonia drawing APIs for UI.
