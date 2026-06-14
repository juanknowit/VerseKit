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

    public string Color { get; init; } = "#00000000";
    public string TextColor { get; init; } = "#8E8E93";

    public static readonly AccessCell NotApplicable = new() { Applicable = false };
}
