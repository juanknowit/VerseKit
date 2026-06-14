namespace DependencyViewer.Models;

/// <summary>A table (entity) in the left-hand list.</summary>
public sealed class EntityListItem
{
    public required Guid MetadataId { get; init; }
    public required string DisplayName { get; init; }
    public required string LogicalName { get; init; }

    public string Title => string.IsNullOrWhiteSpace(DisplayName) ? LogicalName : DisplayName;
}
