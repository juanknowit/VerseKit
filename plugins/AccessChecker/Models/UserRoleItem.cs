namespace AccessChecker.Models;

/// <summary>A role the user has, with where it comes from (directly or via a team).</summary>
public sealed class UserRoleItem
{
    public required string Name { get; init; }
    public required string Source { get; init; }   // "Direct" or the team name
    public string BusinessUnit { get; init; } = "";

    public bool IsViaTeam => !string.Equals(Source, "Direct", StringComparison.OrdinalIgnoreCase);
    public string SourceBadge => IsViaTeam ? "TEAM" : "DIRECT";
    public string SourceColor => IsViaTeam ? "#7A5AA8" : "#2E7D5B";
}
