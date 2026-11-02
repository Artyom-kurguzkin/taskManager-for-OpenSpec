using System.Text.RegularExpressions;

namespace WorkItems.BusinessRules;

/// <summary>
/// A change's delta spec against the current main spec. Archiving replaces a MODIFIED requirement with the delta's whole
/// block and refuses to drop a scenario the main spec has since gained (another item's archive added it). Refresh puts
/// such scenarios back into the block, verbatim from the main spec, so an archive never stops on a stale delta.
/// </summary>
internal static partial class DeltaSpec
{
    [GeneratedRegex(@"^## (?<kind>\w+) Requirements[ \t]*$", RegexOptions.Multiline)]
    private static partial Regex Section();

    [GeneratedRegex(@"^### Requirement:[ \t]*(?<r>.+?)[ \t]*$", RegexOptions.Multiline)]
    private static partial Regex Requirement();

    [GeneratedRegex(@"^#### Scenario:[ \t]*(?<s>.+?)[ \t]*$", RegexOptions.Multiline)]
    private static partial Regex Scenario();

    /// <summary>The delta with every scenario of the main spec appended to the MODIFIED requirement that lacks it; unchanged when nothing is missing.</summary>
    internal static string Refresh(string delta, string main)
    {
        string nl = delta.Contains("\r\n") ? "\r\n" : "\n";
        string text = delta.Replace("\r\n", "\n");
        Dictionary<string, List<(string Name, string Text)>> current = Scenarios(main.Replace("\r\n", "\n"));
        List<(int At, string Text)> inserts = [];
        foreach ((int start, int end, string kind) in Sections(text))
        {
            if (kind != "MODIFIED") continue;
            foreach ((int bStart, int bEnd, string name) in Blocks(text, start, end))
            {
                if (!current.TryGetValue(name, out List<(string Name, string Text)>? scenarios)) continue;   // gone from the main spec: nothing to copy
                HashSet<string> have = Scenario().Matches(text[bStart..bEnd]).Select(m => m.Groups["s"].Value).ToHashSet();
                string missing = string.Concat(scenarios.Where(s => !have.Contains(s.Name)).Select(s => "\n\n" + s.Text));
                int cut = bEnd;   // the block's own text, before its trailing blank lines
                while (cut > bStart && text[cut - 1] == '\n') cut--;
                if (missing.Length > 0) inserts.Add((cut, missing + (cut == text.Length ? "\n" : "")));
            }
        }
        foreach ((int at, string add) in inserts.OrderByDescending(i => i.At)) text = text[..at] + add + text[at..];
        return nl == "\n" ? text : text.Replace("\n", nl);
    }

    /// <summary>(start, end, kind) of every "## X Requirements" section; a delta without such headings is one unnamed section.</summary>
    private static IEnumerable<(int Start, int End, string Kind)> Sections(string text)
    {
        List<Match> heads = Section().Matches(text).ToList();
        for (int i = 0; i < heads.Count; i++)
            yield return (heads[i].Index, i + 1 < heads.Count ? heads[i + 1].Index : text.Length, heads[i].Groups["kind"].Value);
    }

    /// <summary>(start, end, name) of every requirement block between <paramref name="from"/> and <paramref name="to"/>; a block ends where the next requirement or section starts.</summary>
    private static IEnumerable<(int Start, int End, string Name)> Blocks(string text, int from, int to)
    {
        List<Match> reqs = Requirement().Matches(text).Where(m => m.Index >= from && m.Index < to).ToList();
        for (int i = 0; i < reqs.Count; i++)
            yield return (reqs[i].Index, i + 1 < reqs.Count ? reqs[i + 1].Index : to, reqs[i].Groups["r"].Value);
    }

    /// <summary>Requirement name → its scenarios (name and full text, heading included) in the main spec.</summary>
    private static Dictionary<string, List<(string Name, string Text)>> Scenarios(string main)
    {
        Dictionary<string, List<(string, string)>> result = [];
        foreach ((int start, int end, string name) in Blocks(main, 0, main.Length))
        {
            List<Match> scenes = Scenario().Matches(main).Where(m => m.Index >= start && m.Index < end).ToList();
            result[name] = scenes.Select((m, i) => (m.Groups["s"].Value, main[m.Index..(i + 1 < scenes.Count ? scenes[i + 1].Index : end)].TrimEnd())).ToList();
        }
        return result;
    }
}
