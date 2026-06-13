using System.Reflection;
using System.Runtime.Loader;

namespace VerseKit.Core.Services;

/// <summary>
/// Isolated AssemblyLoadContext for a single plugin.
/// Any assembly already loaded in the default context is shared rather than
/// duplicated — this is critical for Avalonia and the Dataverse SDK because
/// the types must be identity-equal across the plugin/host boundary
/// (e.g. CreateView() returns Avalonia.Controls.Control, ServiceClient is
/// passed through IConnectionProvider).
/// Only truly plugin-private assemblies are loaded in isolation.
/// </summary>
internal sealed class PluginLoadContext : AssemblyLoadContext
{
    private readonly AssemblyDependencyResolver _resolver;

    public PluginLoadContext(string pluginAssemblyPath)
        : base(name: Path.GetFileNameWithoutExtension(pluginAssemblyPath), isCollectible: true)
    {
        _resolver = new AssemblyDependencyResolver(pluginAssemblyPath);
    }

    protected override Assembly? Load(AssemblyName assemblyName)
    {
        // 1. Already loaded in the default context → return it directly.
        //    Returning null here is NOT safe: null means "try this context's own fallback
        //    probing," which won't find host assemblies. Return the Assembly object itself
        //    so the CLR gets a concrete, unambiguous reference and type identity is preserved.
        var alreadyLoaded = Default.Assemblies.FirstOrDefault(a =>
            string.Equals(a.GetName().Name, assemblyName.Name, StringComparison.OrdinalIgnoreCase));
        if (alreadyLoaded is not null)
            return alreadyLoaded;

        // 2. Lives in the host's AppBase but not yet loaded → load it into the default
        //    context explicitly.  LoadFromAssemblyPath on Default is idempotent: if the
        //    assembly is already there (loaded via a different code path) it returns the
        //    cached instance, so there is no double-load risk.
        var hostPath = Path.Combine(AppContext.BaseDirectory, assemblyName.Name + ".dll");
        if (File.Exists(hostPath))
        {
            try
            {
                return Default.LoadFromAssemblyPath(hostPath);
            }
            catch
            {
                // Race: another thread loaded it between our Assemblies snapshot and now.
                var raceLoaded = Default.Assemblies.FirstOrDefault(a =>
                    string.Equals(a.GetName().Name, assemblyName.Name, StringComparison.OrdinalIgnoreCase));
                return raceLoaded; // may still be null — CLR will then throw naturally
            }
        }

        // 3. Not a host assembly — load privately from the plugin directory.
        var resolved = _resolver.ResolveAssemblyToPath(assemblyName);
        return resolved is not null ? LoadFromAssemblyPath(resolved) : null;
    }

    protected override IntPtr LoadUnmanagedDll(string unmanagedDllName)
    {
        var resolved = _resolver.ResolveUnmanagedDllToPath(unmanagedDllName);
        return resolved is not null ? LoadUnmanagedDllFromPath(resolved) : IntPtr.Zero;
    }
}
