namespace BoardSync.Merge;

/// <summary>A note file: the frontmatter between the leading --- lines merged by field, the body as text.</summary>
internal static class NoteMerge
{
    internal static List<string> Merge(List<string> baseLines, List<string> local, List<string> remote, MergeContext ctx)
    {
        (List<string>? bf, List<string> bb) = Split(baseLines);
        (List<string>? lf, List<string> lb) = Split(local);
        (List<string>? rf, List<string> rb) = Split(remote);
        if (lf is null && rf is null) return Diff3.Merge(baseLines, local, remote, ctx);

        List<string> front = YamlMerge.Merge(bf ?? [], lf ?? [], rf ?? [], ctx);
        List<string> body = Diff3.Merge(bb, lb, rb, ctx);
        return front.Count == 0 ? body : ["---", .. front, "---", .. body];
    }

    private static (List<string>? Front, List<string> Body) Split(List<string> lines)
    {
        if (lines.Count == 0 || lines[0] != "---") return (null, lines);
        int end = lines.FindIndex(1, l => l == "---");
        return end < 0 ? (null, lines) : (lines[1..end], lines[(end + 1)..]);
    }
}
