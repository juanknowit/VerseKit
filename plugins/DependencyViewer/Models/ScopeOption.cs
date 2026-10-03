namespace DependencyViewer.Models;

/// <summary>What to check dependencies for: the whole table or one of its columns.</summary>
public sealed class ScopeOption
{
    public required int ComponentType { get; init; }   // 1 = Entity, 2 = Attribute
    public required Guid MetadataId { get; init; }
    public required string Title { get; init; }
    public string LogicalName { get; init; } = "";

    public bool IsTable => ComponentType == 1;
}
