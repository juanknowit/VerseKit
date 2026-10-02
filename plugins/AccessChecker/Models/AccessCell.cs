namespace AccessChecker.Models;

/// <summary>
/// One cell in the effective-access matrix: the depth a user has for a single
/// privilege (e.g. Read) on a single table, after aggregating all their roles.
/// </summary>
public sealed class AccessCell
{
    /// <summary>Whether the table supports this privilege at all. If false, render nothing.</summary>
    public bool Applicable { get; init; }

    /// <summary>Compact label shown in the cell, e.g. "User", "BU", "P:C", "Org", "None".</summary>
    public string Short { get; init; } = "";

    /// <summary>Full label shown as a tooltip, e.g. "Parent: Child Business Units".</summary>
    public string Full { get; init; } = "";

    /// <summary>Tooltip: the depth, plus the roles that grant it ("why").</summary>
    public string Tooltip { get; init; } = "";

    /// <summary>The roles granting this privilege, deepest first (empty if none).</summary>
    public IReadOnlyList<AccessGrant> Grants { get; init; } = [];

    public string Color { get; init; } = "#00000000";
    public string TextColor { get; init; } = "#8E8E93";

    public static readonly AccessCell NotApplicable = new() { Applicable = false };

    /// <summary>Full name of a privilege depth, e.g. "Business Unit".</summary>
    public static string DepthLabel(Microsoft.Crm.Sdk.Messages.PrivilegeDepth depth) => depth switch
    {
        Microsoft.Crm.Sdk.Messages.PrivilegeDepth.Basic => "User",
        Microsoft.Crm.Sdk.Messages.PrivilegeDepth.Local => "Business Unit",
        Microsoft.Crm.Sdk.Messages.PrivilegeDepth.Deep => "Parent: Child Business Units",
        Microsoft.Crm.Sdk.Messages.PrivilegeDepth.Global => "Organization",
        _ => depth.ToString(),
    };
}
