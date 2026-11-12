using System.Text.RegularExpressions;

namespace BoardSync.Merge;

/// <summary>
/// tasks.md merged subtask by subtask. A subtask is identified by its group heading and its text (the number and the tick do not count), so additions from both sides are
/// added, a removal on one side that the other did not touch is removed, and a tick or untick on one side is taken. Numbers are rewritten within each group at the end,
/// the way WorkItems' TasksFile does it (the line formats mirror that class; this assembly does not reference WorkItems, whose TasksFile is internal).
/// </summary>
// ponytail: subtasks have no stable id yet, so a retexted subtask is seen as a removal plus an addition (and a tick made on the old text meets the removal as a clash). The ceiling lifts when tasks.md lines get a hidden id.
internal static partial class TasksMerge
{
    [GeneratedRegex(@"^(?<indent>\s*)- \[(?<box>[^\]]*)\] (?:(?<num>\d+(?:\.\d+)*) )?(?<text>.*)$", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex Checkbox();

    [GeneratedRegex(@"\s*<!--\s*agent\s*(?::\s*(?<by>[^>]*?))?\s*-->\s*$", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex AgentMark();

    [GeneratedRegex(@"^##\s+(?<g>.+)$", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex GroupHeading();

    [GeneratedRegex(@"^\d+\.\s+", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex HeadingNumber();

    private sealed record Parts(string Indent, bool Done, string Num, string Body, string MarkRaw, string MarkBy);

    internal static List<string> Merge(List<string> baseLines, List<string> local, List<string> remote, MergeContext ctx)
    {
        List<Entry> merged = Weave.Merge(Parse(baseLines), Parse(local), Parse(remote), (b, l, r) =>
        {
            Entry any = (l ?? r)!;
            string what = any.Key.StartsWith("T:", StringComparison.Ordinal) ? "subtask: " + Split(any.Text)!.Body : "tasks.md";
            return Weave.Decide(b, l, r, ctx, what, e => e.Text.Trim(), (b2, l2, r2) => Combine(b2, l2, r2, ctx, what));
        });
        return Renumber([.. merged.Select(e => e.Text)]);
    }

    private static List<Entry> Parse(List<string> lines)
    {
        Dictionary<string, int> seen = [];
        List<Entry> entries = [];
        string group = "";
        foreach (string line in lines)
        {
            if (GroupHeading().Match(line) is { Success: true } h)
            {
                group = HeadingNumber().Replace(h.Groups["g"].Value.Trim(), "");
                entries.Add(new Entry(Weave.Unique(seen, "H:" + group), group, line));
            }
            else if (Split(line) is { } p)
                entries.Add(new Entry(Weave.Unique(seen, $"T:{group}|{p.Body}"), (p.Done ? "x" : " ") + "|" + p.MarkBy, line));
            else
                entries.Add(new Entry(Weave.Unique(seen, "L:" + line), line, line));
        }
        return entries;
    }

    private static Parts? Split(string line)
    {
        if (Checkbox().Match(line) is not { Success: true } m) return null;
        string text = m.Groups["text"].Value;
        Match mark = AgentMark().Match(text);
        return new Parts(m.Groups["indent"].Value, m.Groups["box"].Value.Trim().Equals("x", StringComparison.OrdinalIgnoreCase), m.Groups["num"].Value,
            (mark.Success ? text[..mark.Index] : text).TrimEnd(), mark.Success ? mark.Value : "", mark.Success ? mark.Groups["by"].Value.Trim() : "");
    }

    /// <summary>Both sides changed one subtask line (a tick and an agent stamp, say): each part from the side that changed it; the same part changed differently on both goes to the newer side.</summary>
    private static Entry? Combine(Entry? b, Entry l, Entry r, MergeContext ctx, string what)
    {
        if (Split(l.Text) is not { } lp || Split(r.Text) is not { } rp) return null;
        Parts? bp = b is null ? null : Split(b.Text);
        bool done = Pick(lp.Done, rp.Done, bp is null ? null : bp.Done, ctx, out bool clash);
        if (clash) ctx.Report(what, ctx.Newer(lp, rp).Done ? "ticked" : "not ticked", ctx.Newer(rp, lp).Done ? "ticked" : "not ticked");
        string by = Pick(lp.MarkBy, rp.MarkBy, bp?.MarkBy, ctx, out _);
        string mark = by == lp.MarkBy ? lp.MarkRaw : rp.MarkRaw;
        string num = lp.Num.Length > 0 ? lp.Num + " " : "";
        string line = $"{lp.Indent}- [{(done ? "x" : " ")}] {num}{lp.Body}{mark}";
        return new Entry(l.Key, (done ? "x" : " ") + "|" + by, line);
    }

    private static T Pick<T>(T l, T r, object? b, MergeContext ctx, out bool clash)
    {
        clash = false;
        if (Equals(l, r)) return l;
        if (b is not null && Equals(b, l)) return r;
        if (b is not null && Equals(b, r)) return l;
        clash = b is null;   // no base: a difference is a clash; with a base, differing from it on both sides can only be a text part, which just goes to the newer side
        return ctx.Newer(l, r);
    }

    /// <summary>1.1, 1.2, … within each group (the number's prefix), as TasksFile renumbers after a removal.</summary>
    private static List<string> Renumber(List<string> lines)
    {
        Dictionary<string, int> seq = [];
        return [.. lines.Select(line =>
        {
            if (Checkbox().Match(line) is not { Success: true } m || !m.Groups["num"].Success) return line;
            string num = m.Groups["num"].Value;
            string prefix = num.Contains('.') ? num[..num.LastIndexOf('.')] : "";
            int n = seq[prefix] = seq.GetValueOrDefault(prefix) + 1;
            string fresh = prefix.Length > 0 ? $"{prefix}.{n}" : $"{n}";
            return line[..m.Groups["num"].Index] + fresh + line[(m.Groups["num"].Index + num.Length)..];
        })];
    }
}
