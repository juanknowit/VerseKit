namespace AccessChecker.Models;

/// <summary>
/// One privilege (e.g. Read on Account) compared between two users: the depth
/// each of them has, and whether those differ.
/// </summary>
public sealed class ComparisonRow
{
    public required string Table { get; init; }
    public required string LogicalName { get; init; }
    public required string Privilege { get; init; }

    /// <summary>The first (selected) user's access cell.</summary>
    public required AccessCell CellA { get; init; }

    /// <summary>The compared user's access cell.</summary>
    public required AccessCell CellB { get; init; }

    public required bool Differs { get; init; }

    public string Title => string.IsNullOrWhiteSpace(Table) ? LogicalName : Table;

    /// <summary>Accent shown on rows that differ, transparent otherwise.</summary>
    public string AccentColor => Differs ? "#FF9500" : "#00000000";
}
