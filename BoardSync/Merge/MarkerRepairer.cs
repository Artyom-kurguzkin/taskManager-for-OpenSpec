using BoardSync.PublicDTOs;
using BoardSync.Sync;
using YamlDotNet.RepresentationModel;

namespace BoardSync.Merge;

/// <summary>
/// Rebuilds a board file that a git merge left conflict markers in. With the three versions git knows (the merge base, ours, theirs) the board's own merge runs over them and the
/// newer side wins what both changed; without them the hunks in the file are the sides (the text outside the markers is the base) and ours wins. Never throws: null = leave the file
/// as it is (no rule for this kind of file, the result still has markers or is not valid yaml).
/// </summary>
internal static class MarkerRepairer
{
    internal static MarkerRepair? Run(string path, string markedText, string? baseText, string? ours, string? theirs, bool theirsIsNewer)
    {
        if (!FileMerge.HasFieldRule(path)) return null;
        bool fromGit = ours is not null && theirs is not null;
        string b, o, t;
        if (fromGit) (b, o, t) = (baseText ?? "", ours!, theirs!);
        else if (Hunks(markedText) is { } h) (b, o, t, theirsIsNewer) = (h.Base, h.Ours, h.Theirs, false);
        else return null;

        MergeResult merged = FileMerge.Run(path, b, o, t, theirsIsNewer);
        if (GitGuard.HasMarkers(merged.Text.Split('\n'))) return null;
        if (path.EndsWith(".openspec.yaml", StringComparison.Ordinal) && !IsYaml(merged.Text)) return null;
        return new MarkerRepair(merged.Text, merged.Conflicts, fromGit);
    }

    private static bool IsYaml(string text)
    {
        try { new YamlStream().Load(new StringReader(text)); return true; }
        catch (YamlDotNet.Core.YamlException) { return false; }
    }

    /// <summary>
    /// The file as its two sides, outside fenced code blocks (the same markers the git guard looks for): every hunk's first half goes to ours, its second half to theirs, a diff3 hunk's middle part
    /// to the base; the lines outside any hunk go to all three. null when a hunk is not closed.
    /// </summary>
    private static (string Base, string Ours, string Theirs)? Hunks(string text)
    {
        List<string> b = [], o = [], t = [];
        int stage = 0;          // 0 outside, 1 ours, 2 base (diff3), 3 theirs
        string? fence = null;
        foreach (string raw in text.Replace("\r\n", "\n").Split('\n'))
        {
            string trimmed = raw.TrimStart(' ');
            if (raw.Length - trimmed.Length <= 3 && (trimmed.StartsWith("```", StringComparison.Ordinal) || trimmed.StartsWith("~~~", StringComparison.Ordinal)))
            {
                string mark = trimmed[..3];
                if (fence is null) fence = mark; else if (fence == mark) fence = null;
            }
            else if (fence is null)
            {
                if (stage == 0 && raw.StartsWith("<<<<<<< ", StringComparison.Ordinal)) { stage = 1; continue; }
                if (stage == 1 && raw.StartsWith("|||||||", StringComparison.Ordinal)) { stage = 2; continue; }
                if (stage is 1 or 2 && raw.StartsWith("=======", StringComparison.Ordinal) && raw.TrimEnd().Length == 7) { stage = 3; continue; }
                if (stage == 3 && raw.StartsWith(">>>>>>> ", StringComparison.Ordinal)) { stage = 0; continue; }
            }
            if (stage == 0) { b.Add(raw); o.Add(raw); t.Add(raw); }
            else if (stage == 1) o.Add(raw);
            else if (stage == 2) b.Add(raw);
            else t.Add(raw);
        }
        if (stage != 0) return null;
        return (string.Join('\n', b), string.Join('\n', o), string.Join('\n', t));
    }
}
