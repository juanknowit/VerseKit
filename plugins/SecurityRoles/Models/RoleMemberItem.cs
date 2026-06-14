namespace SecurityRoles.Models;

/// <summary>A user or team assigned to a security role.</summary>
public sealed class RoleMemberItem
{
    public required string Name { get; init; }
    public string Secondary { get; init; } = "";

    /// <summary>"USER" or "TEAM".</summary>
    public required string Kind { get; init; }

    public bool IsDisabled { get; init; }

    public string Title => string.IsNullOrWhiteSpace(Name) ? "(unnamed)" : Name;

    public string KindColor => Kind == "TEAM" ? "#FF9500" : "#007AFF";
}
