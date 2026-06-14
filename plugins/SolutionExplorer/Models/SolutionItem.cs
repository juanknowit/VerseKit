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
    public string Badge => IsManaged ? "MANAGED" : "UNMANAGED";
    public string BadgeColor => IsManaged ? "#5A6470" : "#2E7D5B";

    public string Initials
    {
        get
        {
            var parts = Title.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var letters = parts.Select(p => char.ToUpperInvariant(p[0])).Take(2).ToArray();
            return letters.Length > 0 ? new string(letters) : "?";
        }
    }
}
