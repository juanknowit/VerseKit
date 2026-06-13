namespace ResourceManager.Models;

/// <summary>A solution node in the resource tree, holding the (filtered)
/// web resources that belong to it.</summary>
public sealed class SolutionGroup
{
    public required string Name { get; init; }
    public required IReadOnlyList<WebResourceItem> Items { get; init; }

    public string DisplayName => $"{Name}  ({Items.Count})";
}
