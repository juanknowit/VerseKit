namespace SecurityRoles.Models;

/// <summary>One table's row in the role's table-permissions matrix.</summary>
public sealed class RolePrivilegeRow
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
}
