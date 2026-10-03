namespace QueryRunner.Models;

/// <summary>A query kept in history: a recent run (no name) or a saved one.</summary>
public sealed record StoredQuery(string Name, int Mode, string Text, DateTime When)
{
    public bool IsOData => Mode == 1;
    public string ModeLabel => IsOData ? "ODATA" : "FETCH";
    public string ModeColor => IsOData ? "#0A84FF" : "#AF52DE";

    /// <summary>Saved queries show their name; recents a one-line preview of the query.</summary>
    public string Title => Name.Length > 0 ? Name : Preview;

    /// <summary>The query collapsed to one line, for the list.</summary>
    public string Preview
    {
        get
        {
            var oneLine = string.Join(' ', Text.Split((char[])['\n', '\r', '\t'],
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
            return oneLine.Length > 120 ? oneLine[..120] + "…" : oneLine;
        }
    }

    public string WhenLabel =>
        (DateTime.Now - When) switch
        {
            { TotalMinutes: < 1 } => "just now",
            { TotalHours: < 1 } d => $"{(int)d.TotalMinutes} min ago",
            { TotalDays: < 1 } d => $"{(int)d.TotalHours} h ago",
            _ => When.ToString("d MMM"),
        };
}
