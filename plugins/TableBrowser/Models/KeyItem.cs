namespace TableBrowser.Models;

/// <summary>An alternate key defined on the selected table.</summary>
public sealed class KeyItem
{
    public required string LogicalName { get; init; }
    public required string DisplayName { get; init; }
    public required string Columns { get; init; }
    /// <summary>Index status: Active, Pending, InProgress or Failed.</summary>
    public required string Status { get; init; }

    public string Title => string.IsNullOrWhiteSpace(DisplayName) ? LogicalName : DisplayName;
    public string StatusColor => Status switch
    {
        "Active" => "#34C759",
        "Failed" => "#FF3B30",
        _ => "#FF9500",
    };
}
