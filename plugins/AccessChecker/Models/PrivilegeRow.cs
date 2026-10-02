namespace AccessChecker.Models;

/// <summary>One table's row in the user's effective table-permissions matrix.</summary>
public sealed class PrivilegeRow
{
    public required string Table { get; init; }
    public required string LogicalName { get; init; }

    /// <summary>"User/Team", "BU", or "Org" — the table's ownership type.</summary>
    public required string Owner { get; init; }

    public required AccessCell Create { get; init; }
    public required AccessCell Read { get; init; }
    public required AccessCell Write { get; init; }
    public required AccessCell Delete { get; init; }
    public required AccessCell Append { get; init; }
    public required AccessCell AppendTo { get; init; }
    public required AccessCell Assign { get; init; }
    public required AccessCell Share { get; init; }

    public string Title => string.IsNullOrWhiteSpace(Table) ? LogicalName : Table;

    /// <summary>The "Why" panel lines: each privilege the table supports and
    /// the roles that grant it.</summary>
    public IReadOnlyList<WhyLine> Why =>
    [
        .. new (string Label, AccessCell Cell)[]
        {
            ("Create", Create), ("Read", Read), ("Write", Write), ("Delete", Delete),
            ("Append", Append), ("Append To", AppendTo), ("Assign", Assign), ("Share", Share),
        }
        .Where(p => p.Cell.Applicable)
        .Select(p => new WhyLine
        {
            Privilege = p.Label,
            Cell = p.Cell,
            GrantedBy = p.Cell.Grants.Count == 0
                ? "Not granted by any of the user's roles"
                : string.Join("  ·  ", p.Cell.Grants.Select(g => $"{g.Role} ({g.SourceLabel}) — {AccessCell.DepthLabel(g.Depth)}")),
        })
    ];

    /// <summary>True if the user has at least one privilege granted on this table.</summary>
    public bool HasAnyAccess =>
        IsGranted(Create) || IsGranted(Read) || IsGranted(Write) || IsGranted(Delete)
        || IsGranted(Append) || IsGranted(AppendTo) || IsGranted(Assign) || IsGranted(Share);

    private static bool IsGranted(AccessCell cell) => cell.Applicable && cell.Short != "None";
}
