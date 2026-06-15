using System.Collections.ObjectModel;
using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using VerseKit.App.Services;
using VerseKit.Core.Models;
using VerseKit.Core.Services;

namespace VerseKit.App.ViewModels;

/// <summary>Wraps a saved ConnectionProfile with live IsActive state for sidebar binding.</summary>
public sealed class SavedConnectionItem(ConnectionProfile profile) : ObservableObject
{
    public ConnectionProfile Profile { get; } = profile;
    public string Name => Profile.Name;
    public string EnvironmentUrl => Profile.EnvironmentUrl;
    public string? Folder => Profile.Folder;

    private bool _isActive;
    public bool IsActive
    {
        get => _isActive;
        set => SetProperty(ref _isActive, value);
    }

    /// <summary>True while this row is being dragged (dims it).</summary>
    private bool _isDragging;
    public bool IsDragging
    {
        get => _isDragging;
        set => SetProperty(ref _isDragging, value);
    }
}

/// <summary>A folder node in the connections sidebar holding its connections.</summary>
public sealed class ConnectionFolder(string name) : ObservableObject
{
    public string Name { get; } = name;
    public ObservableCollection<SavedConnectionItem> Connections { get; } = [];

    private bool _isExpanded = true;
    public bool IsExpanded
    {
        get => _isExpanded;
        set => SetProperty(ref _isExpanded, value);
    }

    /// <summary>True while a dragged connection is hovering over this folder.</summary>
    private bool _isDropTarget;
    public bool IsDropTarget
    {
        get => _isDropTarget;
        set => SetProperty(ref _isDropTarget, value);
    }
}

public partial class MainWindowViewModel : ViewModelBase
{
    private readonly PluginHost _pluginHost;
    private readonly ConnectionManager _connectionManager;
    private readonly UpdateService _updateService;
    private readonly PluginCatalogService _catalog;
    private readonly ILogger<MainWindowViewModel> _logger;

    // Connection
    [ObservableProperty] private string _connectionStatus = "Not connected";
    [ObservableProperty] private PluginEntry? _selectedPlugin;
    [ObservableProperty] private bool _isConnectionPanelVisible;
    [ObservableProperty] private bool _isConnected;

    // Workspace
    [ObservableProperty] private Control? _activePluginView;
    [ObservableProperty] private string? _activationError;

    // Settings & updates
    [ObservableProperty] private bool _isSettingsPanelVisible;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DownloadUpdateCommand))]
    private bool _isUpdateAvailable;

    [ObservableProperty] private string _updateStatus = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CheckUpdatesCommand))]
    private bool _isCheckingUpdate;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DownloadUpdateCommand))]
    [NotifyCanExecuteChangedFor(nameof(CheckUpdatesCommand))]
    private bool _isDownloadingUpdate;

    [ObservableProperty] private double _downloadProgress;

    private UpdateInfo? _pendingUpdate;

    public ConnectionViewModel ConnectionForm { get; }

    /// <summary>Enabled plugins shown in the sidebar TOOLS list.</summary>
    public ObservableCollection<PluginEntry> Plugins { get; } = [];

    /// <summary>All discovered plugins (enabled + disabled) for the manager sheet.</summary>
    public ObservableCollection<PluginItemViewModel> PluginItems { get; } = [];

    [ObservableProperty] private bool _isPluginsPanelVisible;
    [ObservableProperty] private string? _pluginStatus;

    /// <summary>Set by the view to pick a folder to install (TopLevel.StorageProvider).</summary>
    public Func<Task<string?>>? PickFolderAsync { get; set; }

    private string _userPluginRoot = "";
    private string _bundledPluginRoot = "";
    private HashSet<Guid> _disabledPlugins = [];

    /// <summary>Plugins available to install (not installed, or with an update).</summary>
    public ObservableCollection<PluginCatalogItemViewModel> CatalogItems { get; } = [];

    [ObservableProperty] private bool _isCatalogLoading;
    [ObservableProperty] private string? _catalogStatus;

    private IReadOnlyList<Services.PluginRegistryEntry> _registry = [];

    /// <summary>Flat list of all connections, kept for IsActive bookkeeping.</summary>
    public ObservableCollection<SavedConnectionItem> SavedProfiles { get; } = [];

    /// <summary>The sidebar tree: ConnectionFolder nodes followed by ungrouped
    /// SavedConnectionItem leaves. Shares item instances with SavedProfiles.</summary>
    public ObservableCollection<object> SidebarNodes { get; } = [];

    /// <summary>Set by the view to prompt for a folder name (new / rename).</summary>
    public Func<string, string, string?, Task<string?>>? PromptTextAsync { get; set; }

    /// <summary>Set by the view to confirm an action. Args: title, message, confirm-button label.</summary>
    public Func<string, string, string, Task<bool>>? ConfirmAsync { get; set; }

    public string AppVersion => $"v{UpdateService.CurrentVersion.ToString(3)}";

    public MainWindowViewModel(
        PluginHost pluginHost,
        ConnectionManager connectionManager,
        ConnectionViewModel connectionForm,
        UpdateService updateService,
        PluginCatalogService catalog,
        ILogger<MainWindowViewModel> logger)
    {
        _pluginHost = pluginHost;
        _connectionManager = connectionManager;
        _updateService = updateService;
        _catalog = catalog;
        _logger = logger;
        ConnectionForm = connectionForm;

        connectionForm.ProfileSaved += () => Dispatcher.UIThread.Post(() =>
        {
            IsConnectionPanelVisible = false;
            _ = RefreshSavedProfilesAsync(CancellationToken.None);
        });

        connectionForm.ProfileDeleted += () => Dispatcher.UIThread.Post(() =>
        {
            IsConnectionPanelVisible = false;
            _ = RefreshSavedProfilesAsync(CancellationToken.None);
        });

        _connectionManager.ConnectionChanged.Subscribe(client =>
        {
            Dispatcher.UIThread.Post(() =>
            {
                var ready = client is { IsReady: true };
                var name = _connectionManager.ActiveConnectionName;

                IsConnected = ready;
                ConnectionStatus = ready ? $"Connected: {name}" : "Not connected";

                foreach (var item in SavedProfiles)
                    item.IsActive = ready && item.Name == name;

                if (!ready)
                {
                    // Disconnected — tools are unusable; close the open one.
                    SelectedPlugin = null;
                    return;
                }

                IsConnectionPanelVisible = false;

                // Environment switched while a tool is open: rebuild the
                // tool view from scratch so no data crosses environments.
                if (SelectedPlugin is not null)
                    ActivePluginView = SelectedPlugin.Plugin.CreateView();
            });
        });
    }

    // Design-time constructor
    public MainWindowViewModel()
    {
        _pluginHost = null!;
        _connectionManager = null!;
        _updateService = null!;
        _catalog = null!;
        _logger = null!;
        ConnectionForm = null!;
        ConnectionStatus = "Connected: Demo Org";
        IsConnected = true;
        IsUpdateAvailable = true;
        UpdateStatus = "Version 0.2.0 is available.";
        SavedProfiles.Add(new SavedConnectionItem(
            new ConnectionProfile { Name = "Contoso Production", EnvironmentUrl = "https://contoso.crm.dynamics.com", ClientId = "x", AuthMethod = AuthMethod.Interactive })
            { IsActive = true });
        SavedProfiles.Add(new SavedConnectionItem(
            new ConnectionProfile { Name = "Dev Sandbox", EnvironmentUrl = "https://dev.crm.dynamics.com", ClientId = "x", AuthMethod = AuthMethod.Interactive }));
    }

    // ── Connection ──────────────────────────────────────────────────

    [RelayCommand]
    private void ToggleConnectionPanel() => IsConnectionPanelVisible = !IsConnectionPanelVisible;

    [RelayCommand]
    private void OpenNewConnection()
    {
        ConnectionForm.BeginNew();
        IsConnectionPanelVisible = true;
    }

    [RelayCommand]
    private void EditProfile(SavedConnectionItem item)
    {
        if (item is null) return;
        ConnectionForm.BeginEdit(item.Profile);
        IsConnectionPanelVisible = true;
    }

    [RelayCommand]
    private void Disconnect()
    {
        _connectionManager.Disconnect();
        ConnectionStatus = "Not connected";
        ActivePluginView = null;
        foreach (var item in SavedProfiles)
            item.IsActive = false;
    }

    // In-flight profile connection — cancellable from the status chip,
    // and capped at 2 minutes in case a browser login is abandoned.
    private CancellationTokenSource? _connectCts;
    [ObservableProperty] private bool _isConnectingProfile;

    [RelayCommand]
    private async Task ConnectToProfileAsync(SavedConnectionItem item)
    {
        if (item is null || item.IsActive) return; // already connected — no re-auth

        _connectCts?.Cancel(); // supersede any earlier attempt
        var cts = _connectCts = new CancellationTokenSource(TimeSpan.FromMinutes(2));

        IsConnectingProfile = true;
        ConnectionStatus = $"Connecting to {item.Name}…";
        try
        {
            await _connectionManager.ConnectAsync(item.Profile, ct: cts.Token);
            // Connecting may have auto-resolved and persisted the tenant id;
            // reload so the in-memory profile (and edit form) reflect it.
            await RefreshSavedProfilesAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            ConnectionStatus = "Connection cancelled (or timed out after 2 minutes).";
        }
        catch (Exception ex)
        {
            ConnectionStatus = $"Connection failed: {ex.Message}";
            _logger.LogError(ex, "Failed to reconnect to profile '{Name}'", item.Name);
        }
        finally
        {
            if (_connectCts == cts)
            {
                IsConnectingProfile = false;
                _connectCts = null;
            }
            cts.Dispose();
        }
    }

    [RelayCommand]
    private void CancelConnect() => _connectCts?.Cancel();


    // ── Plugin activation ────────────────────────────────────────────

    partial void OnSelectedPluginChanged(PluginEntry? value)
    {
        ActivePluginView = null;
        ActivationError = null;
        if (value is not null)
            _ = ActivateAndShowAsync(value);
    }

    private async Task ActivateAndShowAsync(PluginEntry entry)
    {
        if (!IsConnected)
        {
            ActivationError = "Connect to an environment before opening a tool.";
            SelectedPlugin = null;
            return;
        }

        try
        {
            await _pluginHost.ActivateAsync(entry.Plugin.PluginId, _connectionManager, CancellationToken.None);
            ActivePluginView = entry.Plugin.CreateView();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to activate plugin '{Name}'", entry.Plugin.Name);
            ActivationError = $"Could not load plugin: {ex.Message}";
        }
    }

    public async Task LoadPluginsAsync(CancellationToken ct)
    {
        // Load saved connection profiles
        await RefreshSavedProfilesAsync(ct);

        _userPluginRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".local", "share", "versekit", "plugins");
        // Plugins bundled inside the .app bundle (Contents/MacOS/../Resources/plugins)
        _bundledPluginRoot = Path.GetFullPath(
            Path.Combine(AppContext.BaseDirectory, "..", "Resources", "plugins"));

        _disabledPlugins = PluginPreferences.LoadDisabled();

        await DiscoverAllAsync(ct);
        RebuildPluginLists();
    }

    /// <summary>Resets the host and re-discovers both roots (user first so a user
    /// copy shadows a bundled plugin of the same id).</summary>
    private async Task DiscoverAllAsync(CancellationToken ct)
    {
        await _pluginHost.ResetAsync();

        Directory.CreateDirectory(_userPluginRoot);
        await _pluginHost.DiscoverAsync(_userPluginRoot, PluginOrigin.User, ct);

        if (Directory.Exists(_bundledPluginRoot))
            await _pluginHost.DiscoverAsync(_bundledPluginRoot, PluginOrigin.Bundled, ct);
    }

    /// <summary>Rebuilds the manager list (all plugins) and the sidebar tool list
    /// (enabled only) from the host's currently-loaded plugins.</summary>
    private void RebuildPluginLists()
    {
        // The previously-open tool view belongs to a now-unloaded context.
        SelectedPlugin = null;
        ActivePluginView = null;
        Plugins.Clear();
        PluginItems.Clear();

        var seen = new HashSet<Guid>();
        foreach (var entry in _pluginHost.LoadedPlugins)
        {
            if (!seen.Add(entry.Plugin.PluginId)) continue;

            var enabled = !_disabledPlugins.Contains(entry.Plugin.PluginId);
            PluginItems.Add(new PluginItemViewModel(entry, enabled) { EnabledChanged = OnPluginEnabledToggled });
            if (enabled) Plugins.Add(entry);
        }

        // Keep the "Available" list in sync with what's now installed.
        RebuildCatalog();
        ApplyBetaFlags();
    }

    /// <summary>Flags installed plugins that the registry marks as beta, so the
    /// BETA badge also shows in the Installed list (not just Available).</summary>
    private void ApplyBetaFlags()
    {
        var betaIds = _registry.Where(e => e.Beta)
            .Select(e => e.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var item in PluginItems)
            item.IsBeta = betaIds.Contains(item.Entry.Plugin.PluginId.ToString());
    }

    private async Task RefreshSavedProfilesAsync(CancellationToken ct)
    {
        var profiles = await _connectionManager.LoadProfilesAsync(ct);
        var folders = await _connectionManager.LoadFoldersAsync(ct);
        var activeName = _connectionManager.ActiveConnectionName;

        SavedProfiles.Clear();
        foreach (var p in profiles.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase))
            SavedProfiles.Add(new SavedConnectionItem(p) { IsActive = p.Name == activeName });

        RebuildSidebar(folders);
    }

    /// <summary>Builds the sidebar tree from SavedProfiles + the folder list:
    /// folder nodes (in saved order) first, then ungrouped connections.</summary>
    private void RebuildSidebar(IReadOnlyList<string> folders)
    {
        SidebarNodes.Clear();

        // Folder nodes — include empty folders so they persist visibly.
        foreach (var name in folders)
        {
            var node = new ConnectionFolder(name);
            foreach (var item in SavedProfiles.Where(i =>
                         string.Equals(i.Folder, name, StringComparison.OrdinalIgnoreCase)))
                node.Connections.Add(item);
            SidebarNodes.Add(node);
        }

        // Ungrouped connections (no folder, or folder no longer in the list).
        var known = new HashSet<string>(folders, StringComparer.OrdinalIgnoreCase);
        foreach (var item in SavedProfiles.Where(i =>
                     string.IsNullOrWhiteSpace(i.Folder) || !known.Contains(i.Folder!)))
            SidebarNodes.Add(item);
    }

    // ── Folder commands ──────────────────────────────────────────────

    [RelayCommand]
    private async Task NewFolderAsync()
    {
        if (PromptTextAsync is null) return;
        var name = await PromptTextAsync("New folder", "Folder name", null);
        if (string.IsNullOrWhiteSpace(name)) return;
        await _connectionManager.CreateFolderAsync(name.Trim(), CancellationToken.None);
        await RefreshSavedProfilesAsync(CancellationToken.None);
    }

    [RelayCommand]
    private async Task RenameFolderAsync(ConnectionFolder folder)
    {
        if (folder is null || PromptTextAsync is null) return;
        var name = await PromptTextAsync("Rename folder", "Folder name", folder.Name);
        if (string.IsNullOrWhiteSpace(name) || name.Trim() == folder.Name) return;
        await _connectionManager.RenameFolderAsync(folder.Name, name.Trim(), CancellationToken.None);
        await RefreshSavedProfilesAsync(CancellationToken.None);
    }

    [RelayCommand]
    private async Task DeleteFolderAsync(ConnectionFolder folder)
    {
        if (folder is null) return;
        var ok = ConfirmAsync is null || await ConfirmAsync(
            "Delete folder?",
            $"'{folder.Name}' will be removed. Its {folder.Connections.Count} connection(s) " +
            "move to the top level — they are not deleted.",
            "Delete");
        if (!ok) return;
        await _connectionManager.DeleteFolderAsync(folder.Name, CancellationToken.None);
        await RefreshSavedProfilesAsync(CancellationToken.None);
    }

    /// <summary>Moves a connection into a folder (null = ungrouped) and refreshes.</summary>
    public async Task MoveConnectionAsync(SavedConnectionItem item, string? folder)
    {
        if (item is null) return;
        await _connectionManager.MoveProfileToFolderAsync(item.Name, folder, CancellationToken.None);
        await RefreshSavedProfilesAsync(CancellationToken.None);
    }

    // ── Plugins manager ──────────────────────────────────────────────

    [RelayCommand]
    private void OpenPluginsManager()
    {
        PluginStatus = null;
        IsPluginsPanelVisible = true;
        _ = LoadCatalogAsync(CancellationToken.None);
    }

    [RelayCommand]
    private void TogglePluginsPanel() => IsPluginsPanelVisible = !IsPluginsPanelVisible;

    [RelayCommand]
    private void OpenPluginsFolder()
    {
        try
        {
            Directory.CreateDirectory(_userPluginRoot);
            Process.Start(new ProcessStartInfo(_userPluginRoot) { UseShellExecute = true });
        }
        catch (Exception ex) { _logger.LogWarning(ex, "Failed to open plugins folder"); }
    }

    [RelayCommand]
    private async Task RescanPluginsAsync()
    {
        await DiscoverAllAsync(CancellationToken.None);
        RebuildPluginLists();
        PluginStatus = $"{PluginItems.Count} plugin(s) found.";
    }

    [RelayCommand]
    private async Task InstallPluginAsync()
    {
        if (PickFolderAsync is null) return;
        var folder = await PickFolderAsync();
        if (string.IsNullOrWhiteSpace(folder)) return;

        // Convention: a plugin folder "Name" contains "Name.dll".
        var name = Path.GetFileName(folder.TrimEnd('/', '\\'));
        if (string.IsNullOrWhiteSpace(name) || !File.Exists(Path.Combine(folder, $"{name}.dll")))
        {
            PluginStatus = $"That folder doesn't look like a plugin — expected {name}/{name}.dll.";
            return;
        }

        var proceed = ConfirmAsync is null || await ConfirmAsync(
            "Install plugin?",
            $"'{name}' will run in-process with full access to your environments. " +
            "Only install plugins you trust.",
            "Install");
        if (!proceed) return;

        try
        {
            // Unload everything first so existing files aren't locked on overwrite.
            await _pluginHost.ResetAsync();
            var dest = Path.Combine(_userPluginRoot, name);
            if (Directory.Exists(dest)) Directory.Delete(dest, recursive: true);
            CopyDirectory(folder, dest);

            await DiscoverAllAsync(CancellationToken.None);
            RebuildPluginLists();
            PluginStatus = $"Installed '{name}'.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to install plugin from '{Folder}'", folder);
            PluginStatus = $"Install failed: {ex.Message}";
            await DiscoverAllAsync(CancellationToken.None);
            RebuildPluginLists();
        }
    }

    [RelayCommand]
    private async Task RemovePluginAsync(PluginItemViewModel? item)
    {
        if (item is null || !item.IsRemovable) return;

        var ok = ConfirmAsync is null || await ConfirmAsync(
            "Remove plugin?",
            $"'{item.Name}' will be deleted from the plugins folder.",
            "Remove");
        if (!ok) return;

        var dir = item.Entry.PluginDirectory;
        try
        {
            await _pluginHost.ResetAsync();
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);

            _disabledPlugins.Remove(item.Entry.Plugin.PluginId);
            PluginPreferences.SaveDisabled(_disabledPlugins);

            await DiscoverAllAsync(CancellationToken.None);
            RebuildPluginLists();
            PluginStatus = $"Removed '{item.Name}'.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to remove plugin '{Name}'", item.Name);
            PluginStatus = $"Remove failed: {ex.Message}";
            await DiscoverAllAsync(CancellationToken.None);
            RebuildPluginLists();
        }
    }

    /// <summary>Row toggle handler: persist the disabled set and add/remove the
    /// plugin from the sidebar live (the assembly stays loaded until restart).</summary>
    private void OnPluginEnabledToggled(PluginItemViewModel item)
    {
        var id = item.Entry.Plugin.PluginId;
        if (item.IsEnabled) _disabledPlugins.Remove(id);
        else _disabledPlugins.Add(id);
        PluginPreferences.SaveDisabled(_disabledPlugins);

        var inSidebar = Plugins.FirstOrDefault(p => p.Plugin.PluginId == id);
        if (item.IsEnabled && inSidebar is null)
        {
            Plugins.Add(item.Entry);
        }
        else if (!item.IsEnabled && inSidebar is not null)
        {
            if (ReferenceEquals(SelectedPlugin, inSidebar))
            {
                SelectedPlugin = null;
                ActivePluginView = null;
            }
            Plugins.Remove(inSidebar);
        }
    }

    private static void CopyDirectory(string source, string dest)
    {
        Directory.CreateDirectory(dest);
        foreach (var file in Directory.GetFiles(source))
            File.Copy(file, Path.Combine(dest, Path.GetFileName(file)), overwrite: true);
        foreach (var sub in Directory.GetDirectories(source))
            CopyDirectory(sub, Path.Combine(dest, Path.GetFileName(sub)));
    }

    // ── Plugin catalog (registry) ────────────────────────────────────

    private async Task LoadCatalogAsync(CancellationToken ct)
    {
        if (IsCatalogLoading) return;
        IsCatalogLoading = true;
        CatalogStatus = null;
        try
        {
            _registry = await _catalog.FetchAsync(ct);
            RebuildCatalog();
            ApplyBetaFlags();
            if (_registry.Count == 0)
                CatalogStatus = "No plugins available (couldn't reach the registry).";
        }
        finally
        {
            IsCatalogLoading = false;
        }
    }

    /// <summary>Rebuilds the "Available" list from the cached registry, showing only
    /// plugins that aren't installed — or that have a newer version available.</summary>
    private void RebuildCatalog()
    {
        CatalogItems.Clear();
        foreach (var entry in _registry)
        {
            var installed = PluginItems.FirstOrDefault(p =>
                p.Entry.Plugin.PluginId.ToString().Equals(entry.Id, StringComparison.OrdinalIgnoreCase));
            var updateAvailable = installed is not null
                && Version.TryParse(entry.Version, out var avail)
                && Version.TryParse(installed.Entry.Plugin.Version, out var cur)
                && avail > cur;

            // Already installed and current → hide from "Available".
            if (installed is not null && !updateAvailable) continue;

            CatalogItems.Add(new PluginCatalogItemViewModel(entry)
            {
                IsInstalled = installed is not null,
                IsUpdateAvailable = updateAvailable,
            });
        }

        if (_registry.Count > 0)
            CatalogStatus = CatalogItems.Count == 0 ? "All available plugins are installed." : null;
    }

    [RelayCommand]
    private async Task InstallFromCatalogAsync(PluginCatalogItemViewModel? item)
    {
        if (item is null || !item.CanInstall) return;

        item.IsBusy = true;
        CatalogStatus = $"Installing {item.Name}…";
        var name = item.Name;
        try
        {
            await _pluginHost.ResetAsync();
            await _catalog.InstallAsync(item.Entry, _userPluginRoot, progress: null, ct: CancellationToken.None);
            await DiscoverAllAsync(CancellationToken.None);
            RebuildPluginLists();          // also rebuilds the catalog (drops the now-installed row)
            CatalogStatus = $"Installed {name}.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to install '{Name}' from catalog", name);
            await DiscoverAllAsync(CancellationToken.None);
            RebuildPluginLists();
            CatalogStatus = $"Install failed: {ex.Message}";
        }
        finally
        {
            item.IsBusy = false;
        }
    }

    // ── Settings & updates ───────────────────────────────────────────

    [RelayCommand]
    private void ToggleSettingsPanel() => IsSettingsPanelVisible = !IsSettingsPanelVisible;

    [RelayCommand]
    private void OpenGitHub() =>
        Process.Start(new ProcessStartInfo("https://github.com/juanknowit/VerseKit")
            { UseShellExecute = true });

    // ── Theme (accent colour) ────────────────────────────────────────────

    /// <summary>The accent presets shown as swatches in Settings.</summary>
    public ObservableCollection<AccentSwatchItem> AccentPresets { get; } =
        new(Theming.AccentPreset.All.Select(p =>
            new AccentSwatchItem(p, p.Id == Theming.ThemeManager.Current.Id)));

    [RelayCommand]
    private void SelectAccent(string? id)
    {
        if (string.IsNullOrEmpty(id)) return;

        Theming.ThemeManager.Save(Theming.AccentPreset.ById(id));

        foreach (var swatch in AccentPresets)
            swatch.IsSelected = swatch.Id == Theming.ThemeManager.Current.Id;
    }

    /// <summary>The background-style options shown as a segmented control in Settings.</summary>
    public ObservableCollection<BackgroundOptionItem> BackgroundOptions { get; } =
        new(Theming.BackgroundOption.All.Select(o =>
            new BackgroundOptionItem(o, o.Id == Theming.ThemeManager.CurrentBackground.Id)));

    [RelayCommand]
    private void SelectBackground(string? id)
    {
        if (string.IsNullOrEmpty(id)) return;

        Theming.ThemeManager.SaveBackground(Theming.BackgroundOption.ById(id));

        foreach (var option in BackgroundOptions)
            option.IsSelected = option.Id == Theming.ThemeManager.CurrentBackground.Id;
    }

    /// <summary>Silent check on startup — sets the badge on the settings cog.</summary>
    public async Task CheckForUpdatesAsync(CancellationToken ct)
    {
        var update = await _updateService.CheckAsync(ct);
        if (update is null) return;

        _pendingUpdate = update;
        UpdateStatus = $"Version {update.Version} is available.";
        IsUpdateAvailable = true;
    }

    /// <summary>User-initiated check from the settings panel, with feedback.</summary>
    [RelayCommand(CanExecute = nameof(CanCheckUpdates))]
    private async Task CheckUpdatesAsync()
    {
        IsCheckingUpdate = true;
        UpdateStatus = "Checking for updates…";
        try
        {
            var update = await _updateService.CheckAsync(CancellationToken.None);
            if (update is null)
            {
                IsUpdateAvailable = false;
                UpdateStatus = $"You're up to date ({AppVersion}).";
            }
            else
            {
                _pendingUpdate = update;
                IsUpdateAvailable = true;
                UpdateStatus = $"Version {update.Version} is available.";
            }
        }
        finally
        {
            IsCheckingUpdate = false;
        }
    }

    private bool CanCheckUpdates() => !IsCheckingUpdate && !IsDownloadingUpdate;

    [RelayCommand(CanExecute = nameof(CanDownloadUpdate))]
    private async Task DownloadUpdateAsync()
    {
        if (_pendingUpdate is null) return;

        if (_pendingUpdate.DownloadUrl is null)
        {
            // Release has no mac asset — open the releases page instead.
            UpdateService.OpenReleasePage(_pendingUpdate);
            return;
        }

        IsDownloadingUpdate = true;
        UpdateStatus = "Downloading…";
        try
        {
            var progress = new Progress<double>(p =>
                Dispatcher.UIThread.Post(() =>
                {
                    DownloadProgress = p;
                    UpdateStatus = $"Downloading… {p:P0}";
                }));

            var zip = await _updateService.DownloadAsync(_pendingUpdate, progress, CancellationToken.None);

            // Post status changes so they queue AFTER any pending progress
            // posts — assigning directly here can be overwritten by a
            // straggling "Downloading… 100 %" report.
            Dispatcher.UIThread.Post(() => UpdateStatus = "Installing update…");

            var installed = await _updateService.InstallAsync(zip, CancellationToken.None);

            Dispatcher.UIThread.Post(() =>
                UpdateStatus = $"Updated to v{_pendingUpdate.Version} — restarting…");
            await Task.Delay(800);

            UpdateService.Relaunch(installed);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Update failed");
            Dispatcher.UIThread.Post(() => UpdateStatus = $"Update failed: {ex.Message}");
        }
        finally
        {
            IsDownloadingUpdate = false;
        }
    }

    private bool CanDownloadUpdate() => IsUpdateAvailable && !IsDownloadingUpdate;
}
