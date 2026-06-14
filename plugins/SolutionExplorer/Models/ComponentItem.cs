namespace SolutionExplorer.Models;

/// <summary>A single component inside a solution.</summary>
public sealed class ComponentItem
{
    public required string Name { get; init; }
    public required string TypeName { get; init; }

    /// <summary>Schema/logical name or other secondary detail.</summary>
    public string Detail { get; init; } = "";

    public bool IsManaged { get; init; }
    public string ManagedBadge => IsManaged ? "Managed" : "Unmanaged";
    public string ManagedColor => IsManaged ? "#5A6470" : "#2E7D5B";
}
