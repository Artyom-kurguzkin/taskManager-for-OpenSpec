using System.Text.RegularExpressions;
using BoardSync.PublicDTOs;

namespace BoardSync.Merge;

/// <summary>Picks the rule by the board-relative path and handles what every rule shares: equal sides, one side changed, line endings.</summary>
internal static partial class FileMerge
{
    [GeneratedRegex(@"(^|/)notes/n\d+\.md$", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex NotePath();

    internal static MergeResult Run(string path, string? baseText, string local, string remote, bool remoteIsNewer)
    {
        string nb = MergeText.Normalize(baseText ?? ""), nl = MergeText.Normalize(local), nr = MergeText.Normalize(remote);
        string newline = (local.Length > 0 ? local : remote).Contains("\r\n") ? "\r\n" : "\n";
        string InNewline(string text) => newline == "\r\n" ? MergeText.Normalize(text).Replace("\n", "\r\n") : MergeText.Normalize(text);

        if (nl == nr) return new MergeResult(local, []);
        if (nb == nl) return new MergeResult(InNewline(remote), []);
        if (nb == nr) return new MergeResult(local, []);

        MergeContext ctx = new(remoteIsNewer);
        // The newer side wins whole, one conflict reported: what a merge falls back to when it cannot finish in reasonable time or space.
        MergeResult Whole()
        {
            string kept = ctx.Newer(local, remote), dropped = ctx.Newer(remote, local);
            return new MergeResult(InNewline(kept), [new MergeConflict(path, kept, dropped)]);
        }
        if ((long)nb.Length + nl.Length + nr.Length > MaxChars) return Whole();
        try { return Rules(path, nb, nl, nr, newline, ctx, local, remote, InNewline, Whole); }
        catch (RegexMatchTimeoutException) { return Whole(); }
    }

    // ponytail: past this many characters in the three texts a merge is not tried; board files are a few kilobytes.
    private const int MaxChars = 16_000_000;

    private static MergeResult Rules(string path, string nb, string nl, string nr, string newline, MergeContext ctx, string local, string remote, Func<string, string> InNewline, Func<MergeResult> whole)
    {
        List<string> b = MergeText.Lines(nb), l = MergeText.Lines(nl), r = MergeText.Lines(nr);
        string name = path[(path.LastIndexOf('/') + 1)..];
        List<string> merged;
        if (name == "events.log") merged = EventLogMerge.Merge(l, r);
        else if (name.EndsWith(".openspec.yaml", StringComparison.Ordinal)) merged = YamlMerge.Merge(b, l, r, ctx);
        else if (name == "tasks.md") merged = TasksMerge.Merge(b, l, r, ctx);
        else if (NotePath().IsMatch(path)) merged = NoteMerge.Merge(b, l, r, ctx);
        else if (name.EndsWith(".md", StringComparison.Ordinal)) merged = Diff3.Merge(b, l, r, ctx);
        else
        {
            return whole();
        }
        return new MergeResult(MergeText.Join(merged, newline, (nl.Length > 0 ? nl : nr).EndsWith('\n')), ctx.Conflicts);
    }
}
