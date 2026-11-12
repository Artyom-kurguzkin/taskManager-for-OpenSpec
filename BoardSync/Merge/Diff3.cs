namespace BoardSync.Merge;

/// <summary>Line-based three-way merge (diff3 over LCS matches). Changes in separate places are both applied; a region both sides changed differently keeps the newer side's lines and is reported.</summary>
internal static class Diff3
{
    // ponytail: the LCS table is quadratic in the changed middle of a file; past this many cells the middle counts as fully changed (a real clash becomes more likely, nothing is lost). Board files are small. Adjacent edits (line 2 here, line 3 there) are one region and clash, as in git.
    private const long MaxCells = 16_000_000;

    internal static List<string> Merge(List<string> baseLines, List<string> local, List<string> remote, MergeContext ctx)
    {
        // No base: the lines both sides share stand in for it, so a line only one side has is that side's addition and a stretch both rewrote is a clash.
        List<string> b = baseLines.Count > 0 ? baseLines : Common(local, remote);
        int[] ml = Match(b, local), mr = Match(b, remote);
        List<string> result = [];
        int pb = 0, pl = 0, pr = 0;
        for (int i = 0; i <= b.Count; i++)
        {
            bool anchor = i == b.Count || (ml[i] >= 0 && mr[i] >= 0);
            if (!anchor) continue;
            int el = i == b.Count ? local.Count : ml[i], er = i == b.Count ? remote.Count : mr[i];
            Chunk(b[pb..i], local[pl..el], remote[pr..er], result, ctx);
            if (i < b.Count) result.Add(local[el]);
            pb = i + 1;
            pl = el + 1;
            pr = er + 1;
        }
        return result;
    }

    private static void Chunk(List<string> b, List<string> l, List<string> r, List<string> result, MergeContext ctx)
    {
        if (l.SequenceEqual(r) || r.SequenceEqual(b)) { result.AddRange(l); return; }
        if (l.SequenceEqual(b)) { result.AddRange(r); return; }
        List<string> kept = ctx.Newer(l, r), dropped = ctx.Newer(r, l);
        ctx.Report($"text near line {result.Count + 1}", string.Join("\n", kept), string.Join("\n", dropped));
        result.AddRange(kept);
    }

    private static List<string> Common(List<string> a, List<string> b)
    {
        int[] m = Match(a, b);
        return [.. a.Where((_, i) => m[i] >= 0)];
    }

    /// <summary>For each line of a, the index of the line of x it is matched to in a longest common subsequence, or -1.</summary>
    private static int[] Match(List<string> a, List<string> x)
    {
        int[] res = Enumerable.Repeat(-1, a.Count).ToArray();
        int p = 0;
        while (p < a.Count && p < x.Count && a[p] == x[p]) { res[p] = p; p++; }
        int s = 0;
        while (s < a.Count - p && s < x.Count - p && a[a.Count - 1 - s] == x[x.Count - 1 - s]) { res[a.Count - 1 - s] = x.Count - 1 - s; s++; }
        int n = a.Count - p - s, m = x.Count - p - s;
        if (n <= 0 || m <= 0 || (long)n * m > MaxCells) return res;

        int w = m + 1;
        int[] dp = new int[(n + 1) * w];   // dp[i, j]: LCS length of a[p+i..] and x[p+j..]
        for (int i = n - 1; i >= 0; i--)
            for (int j = m - 1; j >= 0; j--)
                dp[i * w + j] = a[p + i] == x[p + j] ? dp[(i + 1) * w + j + 1] + 1 : Math.Max(dp[(i + 1) * w + j], dp[i * w + j + 1]);
        for (int i = 0, j = 0; i < n && j < m;)
        {
            if (a[p + i] == x[p + j]) { res[p + i] = p + j; i++; j++; }
            else if (dp[(i + 1) * w + j] >= dp[i * w + j + 1]) i++;
            else j++;
        }
        return res;
    }
}
