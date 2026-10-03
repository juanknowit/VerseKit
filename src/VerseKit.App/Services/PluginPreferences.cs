using System.Text.Json;

namespace VerseKit.App.Services;

/// <summary>
/// Persists the bundled plugins the user has switched off (uninstalled), by
/// <see cref="System.Guid"/>, in <c>~/.config/versekit/plugins.json</c> (key
/// "Disabled", kept for compatibility). Their files ship inside the app, so they
/// are still discovered but listed under Available instead of Installed. Ids of
/// user-installed plugins (from the old enable/disable switch) are ignored.
/// </summary>
public static class PluginPreferences
{
    private static readonly string Dir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config", "versekit");
    private static readonly string FilePath = Path.Combine(Dir, "plugins.json");

    public static HashSet<Guid> LoadDisabled()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var model = JsonSerializer.Deserialize<Model>(File.ReadAllText(FilePath));
                if (model?.Disabled is { } ids)
                    return ids.Where(s => Guid.TryParse(s, out _)).Select(Guid.Parse).ToHashSet();
            }
        }
        catch
        {
            // A corrupt file should never block startup — treat as "none disabled".
        }
        return [];
    }

    public static void SaveDisabled(IEnumerable<Guid> ids)
    {
        try
        {
            Directory.CreateDirectory(Dir);
            var json = JsonSerializer.Serialize(
                new Model { Disabled = ids.Select(g => g.ToString()).ToList() },
                new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(FilePath, json);
        }
        catch
        {
            // Best effort; the in-memory state is already updated.
        }
    }

    private sealed class Model
    {
        public List<string>? Disabled { get; set; }
    }
}
