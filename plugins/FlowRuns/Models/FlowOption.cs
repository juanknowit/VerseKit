namespace FlowRuns.Models;

/// <summary>A cloud flow in the filter dropdown. <see cref="Id"/> is null for "All flows".</summary>
public sealed class FlowOption
{
    public Guid? Id { get; init; }
    public required string Name { get; init; }
}
