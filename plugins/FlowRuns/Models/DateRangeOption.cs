namespace FlowRuns.Models;

/// <summary>A time window for the run query. <see cref="Days"/> counts back from now.</summary>
public sealed class DateRangeOption
{
    public required string Label { get; init; }
    public required int Days { get; init; }
}
