namespace DependencyViewer.Models;

/// <summary>One component that depends on the selected table.</summary>
public sealed class DependencyItem
{
    public required string Name { get; init; }
    public required string TypeName { get; init; }
    public string Detail { get; init; } = "";
    public string DependencyKind { get; init; } = "";
}
