namespace SecurityRoles.Models;

/// <summary>A Dataverse security role in the browser list.</summary>
public sealed class RoleItem
{
    public required Guid RoleId { get; init; }
    public required string Name { get; init; }
    public string BusinessUnit { get; init; } = "";
    public bool IsManaged { get; init; }
    public DateTime? ModifiedOn { get; init; }

    public string Title => string.IsNullOrWhiteSpace(Name) ? "(unnamed role)" : Name;

    public string TypeBadge => IsManaged ? "MANAGED" : "CUSTOM";
    public string TypeColor => IsManaged ? "#8E8E93" : "#AF52DE";
}
