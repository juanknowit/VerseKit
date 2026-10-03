using System.Text.Json;
using QueryRunner.Models;

namespace QueryRunner.Services;

/// <summary>
/// Recent and saved queries, one JSON file per connection under
/// <c>~/.config/versekit/queryrunner/</c>. Holds query text only — never
/// results or credentials. A missing or corrupt file just means empty history.
/// </summary>
public static class QueryStore
{
    public const int MaxRecent = 20;

    private static readonly string Dir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config", "versekit", "queryrunner");

    public static (List<StoredQuery> Recent, List<StoredQuery> Saved) Load(string connectionName)
    {
        try
        {
            var path = PathFor(connectionName);
            if (File.Exists(path) && JsonSerializer.Deserialize<Model>(File.ReadAllText(path)) is { } m)
                return (m.Recent ?? [], m.Saved ?? []);
        }
        catch { /* unreadable history should never block the tool */ }
        return ([], []);
    }

    public static void Save(string connectionName, IEnumerable<StoredQuery> recent, IEnumerable<StoredQuery> saved)
    {
        try
        {
            Directory.CreateDirectory(Dir);
            var json = JsonSerializer.Serialize(new Model { Recent = [.. recent], Saved = [.. saved] },
                new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(PathFor(connectionName), json);
        }
        catch { /* history is a convenience; failing to persist is not fatal */ }
    }

    private static string PathFor(string connectionName)
    {
        var safe = string.Concat(connectionName.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
        return Path.Combine(Dir, (safe.Length > 0 ? safe : "default") + ".json");
    }

    private sealed class Model
    {
        public List<StoredQuery>? Recent { get; set; }
        public List<StoredQuery>? Saved { get; set; }
    }
}
