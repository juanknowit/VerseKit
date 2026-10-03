using FluentAssertions;
using ResourceManager.Models;
using ResourceManager.Services;
using Xunit;

namespace VerseKit.Tests;

public class LineDiffTests
{
    [Fact]
    public void Identical_texts_produce_no_rows()
    {
        LineDiff.Compute("a\nb\nc", "a\nb\nc").Should().BeEmpty();
        LineDiff.Compute("", "").Should().BeEmpty();
    }

    [Fact]
    public void Changed_line_shows_as_removed_then_added_with_line_numbers()
    {
        var rows = LineDiff.Compute("a\nb\nc", "a\nB\nc");

        rows.Select(r => (r.Kind, r.Text)).Should().Equal(
            (DiffKind.Context, "a"),
            (DiffKind.Removed, "b"),
            (DiffKind.Added, "B"),
            (DiffKind.Context, "c"));
        rows[1].OldNumber.Should().Be(2);
        rows[1].NewNumber.Should().BeNull();
        rows[2].NewNumber.Should().Be(2);
        rows[2].OldNumber.Should().BeNull();
    }

    [Fact]
    public void Adding_to_an_empty_resource_lists_every_line_as_added()
    {
        LineDiff.Compute("", "x\ny").Select(r => r.Kind)
            .Should().Equal(DiffKind.Added, DiffKind.Added);
    }

    [Fact]
    public void Windows_line_endings_are_not_a_change()
    {
        LineDiff.Compute("a\r\nb", "a\nb").Should().BeEmpty();
    }

    [Fact]
    public void Long_unchanged_runs_collapse_to_a_gap_keeping_three_lines_of_context()
    {
        var old = string.Join("\n", Enumerable.Range(1, 20).Select(i => $"line {i}"));
        var edited = old.Replace("line 10", "line ten");

        var rows = LineDiff.Compute(old, edited);

        rows.First().Should().BeEquivalentTo(new { Kind = DiffKind.Gap, Text = "⋯ 6 unchanged lines" });
        rows.Last().Should().BeEquivalentTo(new { Kind = DiffKind.Gap, Text = "⋯ 7 unchanged lines" });
        rows.Count(r => r.Kind == DiffKind.Context).Should().Be(6);
    }

    [Fact]
    public void Random_edits_round_trip_and_are_minimal()
    {
        var rng = new Random(1234);
        for (var iteration = 0; iteration < 500; iteration++)
        {
            var a = RandomLines(rng);
            var b = RandomLines(rng);

            var ops = LineDiff.Diff(a, b);

            ops.Where(o => o.Kind != DiffKind.Added).Select(o => a[o.Old])
                .Should().Equal(a, "the old side must rebuild the saved text");
            ops.Where(o => o.Kind != DiffKind.Removed).Select(o => b[o.New])
                .Should().Equal(b, "the new side must rebuild the edited text");
            ops.Count(o => o.Kind != DiffKind.Context)
                .Should().Be(a.Length + b.Length - 2 * Lcs(a, b), "Myers finds a shortest edit script");
        }
    }

    private static string[] RandomLines(Random rng) =>
        Enumerable.Range(0, rng.Next(0, 12)).Select(_ => ((char)('a' + rng.Next(4))).ToString()).ToArray();

    private static int Lcs(string[] a, string[] b)
    {
        var dp = new int[a.Length + 1, b.Length + 1];
        for (var i = 1; i <= a.Length; i++)
            for (var j = 1; j <= b.Length; j++)
                dp[i, j] = a[i - 1] == b[j - 1] ? dp[i - 1, j - 1] + 1 : Math.Max(dp[i - 1, j], dp[i, j - 1]);
        return dp[a.Length, b.Length];
    }
}
