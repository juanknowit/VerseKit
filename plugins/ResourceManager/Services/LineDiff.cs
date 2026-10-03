using ResourceManager.Models;

namespace ResourceManager.Services;

/// <summary>
/// Line-based diff (Myers' O(ND) algorithm) producing unified-diff rows, with
/// unchanged runs collapsed to <see cref="ContextLines"/> lines either side of
/// each change.
/// </summary>
public static class LineDiff
{
    public const int ContextLines = 3;

    // Myers' trace grows with the square of the edit distance; past this the
    // files are effectively rewritten, so show a whole-file replace instead.
    private const int MaxEditDistance = 2000;

    /// <summary>Diff rows, or an empty list when the texts are identical.</summary>
    public static List<DiffLine> Compute(string oldText, string newText)
    {
        var a = Split(oldText);
        var b = Split(newText);
        return Collapse(Diff(a, b), a, b);
    }

    private static string[] Split(string text) =>
        text.Length == 0 ? [] : text.Replace("\r\n", "\n").Split('\n');

    /// <summary>Every line as an edit op (old/new indices, -1 when absent). Internal for tests.</summary>
    internal static List<(DiffKind Kind, int Old, int New)> Diff(string[] a, string[] b)
    {
        // Trim the common prefix and suffix — typical edits are small.
        var pre = 0;
        while (pre < a.Length && pre < b.Length && a[pre] == b[pre]) pre++;
        var suf = 0;
        while (suf < a.Length - pre && suf < b.Length - pre
               && a[a.Length - 1 - suf] == b[b.Length - 1 - suf]) suf++;

        var ops = new List<(DiffKind, int, int)>();
        for (var i = 0; i < pre; i++) ops.Add((DiffKind.Context, i, i));
        Middle(a, pre, a.Length - suf, b, pre, b.Length - suf, ops);
        for (var i = 0; i < suf; i++) ops.Add((DiffKind.Context, a.Length - suf + i, b.Length - suf + i));
        return ops;
    }

    private static void Middle(string[] a, int a0, int a1, string[] b, int b0, int b1,
                               List<(DiffKind, int, int)> ops)
    {
        int n = a1 - a0, m = b1 - b0, max = n + m;
        if (max == 0) return;

        var v = new int[2 * max + 2];
        var off = max + 1;
        // trace[d] = v[k] for k in [-d, d] after step d.
        var trace = new List<int[]>();

        for (var d = 0; d <= max; d++)
        {
            if (d > MaxEditDistance)
            {
                for (var i = a0; i < a1; i++) ops.Add((DiffKind.Removed, i, -1));
                for (var j = b0; j < b1; j++) ops.Add((DiffKind.Added, -1, j));
                return;
            }

            for (var k = -d; k <= d; k += 2)
            {
                var x = k == -d || (k != d && v[off + k - 1] < v[off + k + 1])
                    ? v[off + k + 1]
                    : v[off + k - 1] + 1;
                var y = x - k;
                while (x < n && y < m && a[a0 + x] == b[b0 + y]) { x++; y++; }
                v[off + k] = x;

                if (x >= n && y >= m)
                {
                    trace.Add(v[(off - d)..(off + d + 1)]);
                    Backtrack(trace, d, n, m, a0, b0, ops);
                    return;
                }
            }
            trace.Add(v[(off - d)..(off + d + 1)]);
        }
    }

    private static void Backtrack(List<int[]> trace, int dEnd, int n, int m, int a0, int b0,
                                  List<(DiffKind, int, int)> ops)
    {
        static int At(int[] row, int d, int k) => row[k + d];

        var rev = new List<(DiffKind, int, int)>();
        int x = n, y = m;
        for (var d = dEnd; d > 0; d--)
        {
            var prev = trace[d - 1];
            var k = x - y;
            var down = k == -d || (k != d && At(prev, d - 1, k - 1) < At(prev, d - 1, k + 1));
            var prevK = down ? k + 1 : k - 1;
            var prevX = At(prev, d - 1, prevK);
            var prevY = prevX - prevK;

            while (x > prevX && y > prevY) { x--; y--; rev.Add((DiffKind.Context, a0 + x, b0 + y)); }
            if (down) { y--; rev.Add((DiffKind.Added, -1, b0 + y)); }
            else { x--; rev.Add((DiffKind.Removed, a0 + x, -1)); }
        }
        while (x > 0 && y > 0) { x--; y--; rev.Add((DiffKind.Context, a0 + x, b0 + y)); }

        rev.Reverse();
        ops.AddRange(rev);
    }

    private static List<DiffLine> Collapse(List<(DiffKind Kind, int Old, int New)> ops, string[] a, string[] b)
    {
        var rows = new List<DiffLine>();
        if (ops.All(o => o.Kind == DiffKind.Context)) return rows;

        // Keep context lines within ContextLines of any change.
        var keep = new bool[ops.Count];
        for (var i = 0; i < ops.Count; i++)
        {
            if (ops[i].Kind == DiffKind.Context) continue;
            for (var j = Math.Max(0, i - ContextLines); j <= Math.Min(ops.Count - 1, i + ContextLines); j++)
                keep[j] = true;
        }

        var skipped = 0;
        void FlushGap()
        {
            if (skipped == 0) return;
            rows.Add(new DiffLine { Kind = DiffKind.Gap, Text = $"⋯ {skipped} unchanged line{(skipped == 1 ? "" : "s")}" });
            skipped = 0;
        }

        for (var i = 0; i < ops.Count; i++)
        {
            var (kind, o, n) = ops[i];
            if (kind == DiffKind.Context && !keep[i]) { skipped++; continue; }
            FlushGap();
            rows.Add(new DiffLine
            {
                Kind = kind,
                OldNumber = o >= 0 ? o + 1 : null,
                NewNumber = n >= 0 ? n + 1 : null,
                Text = kind == DiffKind.Added ? b[n] : a[o],
            });
        }
        FlushGap();
        return rows;
    }
}
