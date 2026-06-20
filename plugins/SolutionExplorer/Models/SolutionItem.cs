namespace SolutionExplorer.Models;

/// <summary>A solution in the environment.</summary>
public sealed class SolutionItem
{
    public required Guid SolutionId { get; init; }
    public required string UniqueName { get; init; }
    public required string FriendlyName { get; init; }
    public string Version { get; init; } = "";
    public string Publisher { get; init; } = "";
    public bool IsManaged { get; init; }

    public string Title => string.IsNullOrWhiteSpace(FriendlyName) ? UniqueName : FriendlyName;

    /// <summary>Title-case status used for the badge tooltip and the detail-header pill.</summary>
    public string StatusLabel => IsManaged ? "Managed" : "Unmanaged";
    public string StatusTooltip => IsManaged ? "Managed — read-only" : "Unmanaged — editable";

    // Managed = purple, unmanaged = green. Colour carries the status (with the
    // tooltip and the detail-header pill); the glyph stays the same for both.
    public string BadgeColor => IsManaged ? "#8257E6" : "#34C759";
}
