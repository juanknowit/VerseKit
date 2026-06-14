namespace QueryRunner.Models;

/// <summary>One result row. Cells are positional, aligned to the result columns;
/// the grid binds each column to <c>Cells[i]</c>.</summary>
public sealed class ResultRow
{
    public required string[] Cells { get; init; }
}
