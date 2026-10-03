using Microsoft.Crm.Sdk.Messages;

namespace AccessChecker.Models;

/// <summary>One role that grants a privilege, and how the user holds that role.</summary>
/// <param name="Role">Role name.</param>
/// <param name="Source">"Direct", or the name of the team that holds the role.</param>
/// <param name="Depth">Depth this role grants the privilege at.</param>
public sealed record AccessGrant(string Role, string Source, PrivilegeDepth Depth)
{
    public string SourceLabel => Source == "Direct" ? "direct" : $"via team {Source}";
}
