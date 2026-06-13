namespace MetadataBrowser.Models;

/// <summary>A Dataverse table (entity) in the browser list.</summary>
public sealed class EntityItem
{
    public required string LogicalName { get; init; }
    public required string DisplayName { get; init; }
    public required string SchemaName { get; init; }
    public bool IsCustom { get; init; }
    public bool IsManaged { get; init; }
    public int? ObjectTypeCode { get; init; }
    public string? PrimaryIdAttribute { get; init; }
    public string? PrimaryNameAttribute { get; init; }

    /// <summary>"Account" when a display name exists, else the logical name.</summary>
    public string Title => string.IsNullOrWhiteSpace(DisplayName) ? LogicalName : DisplayName;

    public string TypeBadge => IsCustom ? "CUSTOM" : "STD";
    public string TypeColor => IsCustom ? "#AF52DE" : "#8E8E93";
}
