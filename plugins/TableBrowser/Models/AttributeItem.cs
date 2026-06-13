namespace TableBrowser.Models;

/// <summary>A column (attribute) of a Dataverse table.</summary>
public sealed class AttributeItem
{
    public required string LogicalName { get; init; }
    public required string DisplayName { get; init; }
    public required string AttributeType { get; init; }
    public required string RequiredLevel { get; init; }
    public bool IsCustom { get; init; }
    public bool IsPrimaryId { get; init; }
    public bool IsPrimaryName { get; init; }

    public string Title => string.IsNullOrWhiteSpace(DisplayName) ? LogicalName : DisplayName;

    /// <summary>A short marker shown next to special columns.</summary>
    public string Marker =>
        IsPrimaryId ? "PK" : IsPrimaryName ? "Name" : IsCustom ? "Custom" : "";

    public bool HasMarker => Marker.Length > 0;
    public string MarkerColor =>
        IsPrimaryId ? "#007AFF" : IsPrimaryName ? "#34C759" : "#AF52DE";
}
