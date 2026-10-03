namespace TableBrowser.Models;

/// <summary>One relationship of the selected table, seen from that table.</summary>
public sealed class RelationshipItem
{
    public required string SchemaName { get; init; }
    /// <summary>"1:N" (other table looks this one up), "N:1" (this table looks
    /// the other up) or "N:N".</summary>
    public required string Kind { get; init; }
    public required string RelatedTable { get; init; }
    /// <summary>The lookup column (1:N / N:1) or intersect table (N:N).</summary>
    public required string Via { get; init; }
    public bool IsCustom { get; init; }

    public string KindColor => Kind switch
    {
        "1:N" => "#007AFF",
        "N:1" => "#34C759",
        _ => "#AF52DE",
    };
}
