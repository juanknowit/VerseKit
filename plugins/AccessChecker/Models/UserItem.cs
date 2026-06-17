namespace AccessChecker.Models;

/// <summary>A system user shown in the picker list.</summary>
public sealed class UserItem
{
    public required Guid UserId { get; init; }
    public required string Name { get; init; }
    public string Secondary { get; init; } = "";
    public string BusinessUnit { get; init; } = "";
    public bool IsDisabled { get; init; }

    public string Title => string.IsNullOrWhiteSpace(Name) ? "(no name)" : Name;
    public string Badge => IsDisabled ? "OFF" : "USER";
    public string BadgeColor => IsDisabled ? "#C0392B" : "#5A6470";

    public string Initials
    {
        get
        {
            var parts = Title.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var letters = parts.Select(p => char.ToUpperInvariant(p[0])).Take(2).ToArray();
            return letters.Length > 0 ? new string(letters) : "?";
        }
    }

    // Drives the compare AutoCompleteBox's type-ahead filter and selected text.
    public override string ToString() => Title;
}
