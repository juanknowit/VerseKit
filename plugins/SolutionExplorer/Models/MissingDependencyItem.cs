namespace SolutionExplorer.Models;

/// <summary>
/// A component the solution needs but doesn't contain — an import into an
/// environment without it would fail.
/// </summary>
public sealed class MissingDependencyItem
{
    public required ComponentItem Required { get; init; }
    public required ComponentItem RequiredBy { get; init; }
}
