namespace SecurityRoles.Models;

/// <summary>
/// One privilege (e.g. Read on Account) compared between two roles: the depth
/// each role grants, and whether those differ.
/// </summary>
public sealed class RoleComparisonRow
{
    public required string Table { get; init; }
    public required string LogicalName { get; init; }
    public required string Privilege { get; init; }

    /// <summary>The selected role's access cell.</summary>
    public required AccessCell CellA { get; init; }

    /// <summary>The compared role's access cell.</summary>
    public required AccessCell CellB { get; init; }

    public required bool Differs { get; init; }

    public string Title => string.IsNullOrWhiteSpace(Table) ? LogicalName : Table;

    /// <summary>Accent shown on rows that differ, transparent otherwise.</summary>
    public string AccentColor => Differs ? "#FF9500" : "#00000000";
}
