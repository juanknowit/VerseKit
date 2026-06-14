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

    /// <summary>True if the user has at least one privilege granted on this table.</summary>
    public bool HasAnyAccess =>
        IsGranted(Create) || IsGranted(Read) || IsGranted(Write) || IsGranted(Delete)
        || IsGranted(Append) || IsGranted(AppendTo) || IsGranted(Assign) || IsGranted(Share);

    private static bool IsGranted(AccessCell cell) => cell.Applicable && cell.Short != "None";
}
