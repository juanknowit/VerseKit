namespace AccessChecker.Models;

/// <summary>One privilege in the "Why" panel: the effective cell and the roles behind it.</summary>
public sealed class WhyLine
{
    public required string Privilege { get; init; }
    public required AccessCell Cell { get; init; }
    public required string GrantedBy { get; init; }
}
