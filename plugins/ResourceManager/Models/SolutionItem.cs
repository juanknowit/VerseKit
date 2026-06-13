namespace ResourceManager.Models;

public sealed class SolutionItem
{
    public static readonly SolutionItem All = new()
    {
        Id = Guid.Empty,
        FriendlyName = "All Solutions",
        UniqueName = string.Empty,
        Version = string.Empty
    };

    public required Guid Id { get; init; }
    public required string FriendlyName { get; init; }
    public required string UniqueName { get; init; }
    public required string Version { get; init; }

    /// <summary>The solution publisher's customization prefix (e.g. "kbs"),
    /// prepended to new web resource names. Empty for the "All" pseudo-item.</summary>
    public string PublisherPrefix { get; init; } = string.Empty;

    public bool IsReal => Id != Guid.Empty;

    public string DisplayLabel => Id == Guid.Empty
        ? "All Solutions"
        : $"{FriendlyName}  ({Version})";
}
