namespace ResourceManager.Models;

public enum DiffKind { Context, Added, Removed, Gap }

/// <summary>One row of the "Review Changes" diff (unified, line-based).</summary>
public sealed class DiffLine
{
    public required DiffKind Kind { get; init; }
    /// <summary>1-based line number in the saved version (null for added lines / gaps).</summary>
    public int? OldNumber { get; init; }
    /// <summary>1-based line number in the edited version (null for removed lines / gaps).</summary>
    public int? NewNumber { get; init; }
    public required string Text { get; init; }

    public string Marker => Kind switch { DiffKind.Added => "+", DiffKind.Removed => "−", _ => "" };
    public string Background => Kind switch
    {
        DiffKind.Added => "#E7F6EC",
        DiffKind.Removed => "#FDECEC",
        DiffKind.Gap => "#F5F5F7",
        _ => "#00FFFFFF",
    };
    public string Foreground => Kind switch
    {
        DiffKind.Added => "#1E7B3A",
        DiffKind.Removed => "#B4231B",
        DiffKind.Gap => "#8E8E93",
        _ => "#3C3C43",
    };
}
