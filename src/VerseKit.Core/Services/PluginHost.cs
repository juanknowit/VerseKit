using System.Reflection;
using Microsoft.Extensions.Logging;
using VerseKit.Core.Models;
using VerseKit.PluginSdk;

namespace VerseKit.Core.Services;

/// <summary>
/// Discovers and manages plugin lifecycles.
/// Each plugin assembly is loaded into its own PluginLoadContext.
/// </summary>
public sealed class PluginHost(ILogger<PluginHost> logger) : IDisposable
{
    private readonly List<(PluginLoadContext Context, PluginEntry Entry)> _loaded = [];

    public IReadOnlyList<PluginEntry> LoadedPlugins =>
        _loaded.Select(x => x.Entry).ToList();

    /// <summary>Scans <paramref name="pluginDirectory"/> for plugin assemblies and loads them,
    /// tagging each with <paramref name="origin"/>.</summary>
    public async Task DiscoverAsync(string pluginDirectory, PluginOrigin origin, CancellationToken ct)
    {
        // Touch IVerseKitPlugin here to guarantee VerseKit.PluginSdk is materialised in
        // AssemblyLoadContext.Default BEFORE any PluginLoadContext is created.
        // PluginLoadContext.Load returns null for default-context assemblies, but that
        // null-fallback only works if the assembly is already present in Default.Assemblies.
        // Without this line, the first plugin load races against lazy JIT resolution.
        _ = typeof(IVerseKitPlugin).Assembly;

        if (!Directory.Exists(pluginDirectory))
        {
            logger.LogWarning("Plugin directory does not exist: {Dir}", pluginDirectory);
            return;
        }

        foreach (var subdir in Directory.GetDirectories(pluginDirectory))
        {
            ct.ThrowIfCancellationRequested();

            // Convention: the plugin DLL matches the folder name (e.g. ResourceManager/ResourceManager.dll).
            // Loading every DLL in the folder would pull in hundreds of framework assemblies unnecessarily.
            var folderName = Path.GetFileName(subdir);
            var candidate = Path.Combine(subdir, $"{folderName}.dll");
            if (!File.Exists(candidate))
            {
                logger.LogDebug("No matching DLL found in '{Dir}' — skipping", subdir);
                continue;
            }

            try
            {
                await LoadPluginAssemblyAsync(candidate, origin, ct);
            }
            catch (System.Reflection.ReflectionTypeLoadException rtle)
            {
                logger.LogWarning("Failed to load plugin from '{Dll}' — type load errors:", candidate);
                foreach (var le in rtle.LoaderExceptions.Where(e => e is not null))
                    logger.LogWarning("  LoaderException: {Msg}", le!.Message);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to load plugin from '{Dll}'", candidate);
            }
        }

        logger.LogInformation("Plugin discovery complete. {Count} plugin(s) loaded.", _loaded.Count);
    }

    private Task LoadPluginAssemblyAsync(string assemblyPath, PluginOrigin origin, CancellationToken ct)
    {
        var context = new PluginLoadContext(assemblyPath);
        // Load the plugin's own assembly from a byte stream rather than by path.
        // When a plugin is updated in place, a path-load can hand back the stale
        // image still memory-mapped by a not-yet-collected previous context, so
        // the reload would report the OLD version. Reading the bytes guarantees
        // the freshly written file is what actually gets loaded.
        Assembly assembly;
        using (var stream = File.OpenRead(assemblyPath))
            assembly = context.LoadFromStream(stream);

        var pluginTypes = assembly.GetTypes()
            .Where(t => !t.IsAbstract && t.IsClass && typeof(IVerseKitPlugin).IsAssignableFrom(t));

        foreach (var type in pluginTypes)
        {
            if (Activator.CreateInstance(type) is not IVerseKitPlugin plugin)
                continue;

            var entry = new PluginEntry { Plugin = plugin, AssemblyPath = assemblyPath, Origin = origin };
            _loaded.Add((context, entry));
            logger.LogInformation("Loaded plugin '{Name}' v{Version} from '{Path}'",
                plugin.Name, plugin.Version, assemblyPath);
        }

        return Task.CompletedTask;
    }

    public async Task ActivateAsync(Guid pluginId, IConnectionProvider connectionProvider, CancellationToken ct)
    {
        var entry = _loaded.Select(x => x.Entry).FirstOrDefault(e => e.Plugin.PluginId == pluginId)
            ?? throw new KeyNotFoundException($"Plugin {pluginId} not found");

        if (entry.IsActivated) return;

        await entry.Plugin.InitializeAsync(connectionProvider, ct);
        entry.IsActivated = true;
        logger.LogInformation("Activated plugin '{Name}'", entry.Plugin.Name);
    }

    public async Task DeactivateAsync(Guid pluginId, CancellationToken ct)
    {
        var entry = _loaded.Select(x => x.Entry).FirstOrDefault(e => e.Plugin.PluginId == pluginId);
        if (entry is null || !entry.IsActivated) return;

        await entry.Plugin.CleanupAsync();
        entry.IsActivated = false;
    }

    /// <summary>
    /// Cleans up and unloads every loaded plugin and clears the list, so callers
    /// can rescan from disk (e.g. after install/remove). The collectible load
    /// contexts release their assemblies, freeing the files for deletion/replace.
    /// </summary>
    public async Task ResetAsync()
    {
        foreach (var (context, entry) in _loaded)
        {
            try { await entry.Plugin.CleanupAsync(); } catch { /* best effort */ }
            context.Unload();
        }
        _loaded.Clear();
        // Encourage the unloaded contexts to be collected before files are touched.
        GC.Collect();
        GC.WaitForPendingFinalizers();
    }

    public void Dispose()
    {
        foreach (var (context, entry) in _loaded)
        {
            try { entry.Plugin.CleanupAsync().GetAwaiter().GetResult(); } catch { /* best effort */ }
            context.Unload();
        }
        _loaded.Clear();
    }
}
