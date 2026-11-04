using System.Text.RegularExpressions;

namespace WorkItems.BusinessRules;

/// <summary>
/// A change's delta spec against the current main spec. Archiving replaces a MODIFIED requirement with the delta's whole
/// block and refuses to drop a scenario the main spec has since gained (another item's archive added it). Refresh puts
/// such scenarios back into the block, verbatim from the main spec, so an archive never stops on a stale delta. It also
/// repairs what else the CLI would refuse: an ADDED requirement the main spec already has (another change added it first),
/// a requirement listed twice (merged deltas), and REMOVED / RENAMED entries for requirements the main spec lacks.
/// </summary>
internal static partial class DeltaSpec
{
    [GeneratedRegex(@"^## (?<kind>\w+) Requirements[ \t]*$", RegexOptions.Multiline)]
    private static partial Regex Section();

    [GeneratedRegex(@"^### Requirement:[ \t]*(?<r>.+?)[ \t]*$", RegexOptions.Multiline)]
    private static partial Regex Requirement();

    [GeneratedRegex(@"^#### Scenario:[ \t]*(?<s>.+?)[ \t]*$", RegexOptions.Multiline)]
    private static partial Regex Scenario();

    [GeneratedRegex(@"^### Requirement:[ \t]*(?<r>.+?)[ \t]*$")]
    private static partial Regex RequirementLine();

    [GeneratedRegex(@"^\s*[-*+]\s*`?###\s*Requirement:\s*(?<r>.+?)`?\s*$")]
    private static partial Regex RemovedBullet();

    [GeneratedRegex(@"^\s*[-*+]?\s*FROM:\s*`?###\s*Requirement:\s*(?<r>.+?)`?\s*$")]
    private static partial Regex RenameFrom();

    [GeneratedRegex(@"^\s*[-*+]?\s*TO:\s*`?###\s*Requirement:\s*(?<r>.+?)`?\s*$")]
    private static partial Regex RenameTo();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    /// <summary>
    /// The delta made safe to archive against the main spec: an ADDED requirement the main spec already has becomes MODIFIED, a
    /// requirement listed twice is merged into one block, a REMOVED / RENAMED entry for a requirement the main spec lacks is dropped,
    /// and every scenario of the main spec is appended to the MODIFIED requirement that lacks it. Unchanged when nothing is wrong.
    /// </summary>
    internal static string Refresh(string delta, string main)
    {
        string nl = delta.Contains("\r\n") ? "\r\n" : "\n";
        string text = delta.Replace("\r\n", "\n");
        Dictionary<string, List<(string Name, string Text)>> current = Scenarios(main.Replace("\r\n", "\n"));
        text = Normalize(text, current);
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

    private sealed class Part(string kind, string head)
    {
        public string Kind { get; } = kind;
        public string Head { get; } = head;
        public string Raw = "";                                  // REMOVED / RENAMED / other: the section as written (head included); empty = gone
        public string Intro = "";                                // ADDED / MODIFIED: text before the first requirement
        public List<(string Name, string Text)> Blocks = [];     // ADDED / MODIFIED: the section's requirements
    }

    /// <summary>
    /// The structural repairs, on "\n" text: ADDED-but-in-main becomes MODIFIED, a requirement named twice becomes one block
    /// (first text, union of scenarios, the stronger section), dead REMOVED / RENAMED entries go. The text comes back untouched when none applies.
    /// </summary>
    private static string Normalize(string text, Dictionary<string, List<(string Name, string Text)>> current)
    {
        List<(int Start, int End, string Kind)> sections = Sections(text).ToList();
        if (sections.Count == 0) return text;
        HashSet<string> folded = current.Keys.Select(Fold).ToHashSet();
        bool Has(string name) => current.ContainsKey(name) || folded.Contains(Fold(name));   // a near miss is the CLI's typo error: left for the user

        bool dirty = false;
        List<Part> parts = [];
        HashSet<string> seen = [];
        foreach ((int start, int end, string kind) in sections)
        {
            int lineEnd = text.IndexOf('\n', start);
            if (lineEnd < 0 || lineEnd > end) lineEnd = end;
            int bodyStart = Math.Min(lineEnd + 1, end);
            Part part = new(kind, text[start..lineEnd]);
            if (kind is "ADDED" or "MODIFIED")
            {
                List<(int Start, int End, string Name)> blocks = Blocks(text, start, end).ToList();
                part.Intro = text[bodyStart..(blocks.Count > 0 ? blocks[0].Start : end)].Trim();
                foreach ((int bStart, int bEnd, string name) in blocks)
                {
                    part.Blocks.Add((name, text[bStart..bEnd].TrimEnd('\n')));
                    if (!seen.Add(name) || (kind == "ADDED" && current.ContainsKey(name))) dirty = true;
                }
            }
            else
            {
                part.Raw = text[start..end].TrimEnd('\n');
                if (kind is "REMOVED" or "RENAMED")
                {
                    (string body, int dropped, int kept) = DropDead(text[bodyStart..end], kind == "RENAMED", Has);
                    if (dropped > 0) { dirty = true; part.Raw = kept == 0 ? "" : part.Head + "\n\n" + body; }
                }
            }
            parts.Add(part);
        }
        if (!dirty) return text;

        // group the requirements by name, in order of first appearance
        List<string> order = [];
        Dictionary<string, (string Kind, Part Home, string Text, List<string> Extra)> groups = [];
        foreach (Part part in parts.Where(p => p.Kind is "ADDED" or "MODIFIED"))
        {
            foreach ((string name, string block) in part.Blocks)
            {
                if (!groups.TryGetValue(name, out var g)) { order.Add(name); groups[name] = (part.Kind, part, block, []); continue; }
                HashSet<string> have = Scenario().Matches(g.Text).Select(m => m.Groups["s"].Value).ToHashSet();
                have.UnionWith(g.Extra.SelectMany(e => Scenario().Matches(e).Select(m => m.Groups["s"].Value)));
                foreach ((string sName, string sText) in Scenarios(block).GetValueOrDefault(name) ?? [])
                    if (have.Add(sName)) g.Extra.Add(sText);
                groups[name] = (g.Kind == "MODIFIED" || part.Kind == "MODIFIED" ? "MODIFIED" : "ADDED", g.Home, g.Text, g.Extra);
            }
            part.Blocks = [];
        }
        foreach (string name in order)
        {
            (string kind, Part home, string block, List<string> extra) = groups[name];
            if (kind == "ADDED" && current.ContainsKey(name)) kind = "MODIFIED";
            Part? into = home.Kind == kind ? home : parts.FirstOrDefault(p => p.Kind == kind);
            if (into is null)
            {
                into = new Part(kind, $"## {kind} Requirements");
                int at = parts.FindIndex(p => p.Kind == "ADDED");
                parts.Insert(at < 0 ? parts.Count : at, into);
            }
            into.Blocks.Add((name, string.Join("\n\n", new[] { block }.Concat(extra))));
        }

        List<string> pieces = [];
        string preamble = text[..sections[0].Start].TrimEnd('\n');
        if (preamble.Length > 0) pieces.Add(preamble);
        foreach (Part part in parts)
        {
            if (part.Kind is "ADDED" or "MODIFIED")
            {
                if (part.Blocks.Count == 0 && part.Intro.Length == 0) continue;   // emptied by the repair
                pieces.Add(part.Head);
                if (part.Intro.Length > 0) pieces.Add(part.Intro);
                pieces.AddRange(part.Blocks.Select(b => b.Text));
            }
            else if (part.Raw.Length > 0) pieces.Add(part.Raw);
        }
        return string.Join("\n\n", pieces) + "\n";
    }

    private static string Fold(string name) => Whitespace().Replace(name.Trim(), " ").ToLowerInvariant();

    /// <summary>The body of a REMOVED / RENAMED section without the entries for requirements the main spec does not have (a RENAMED pair goes when neither of its names exists).</summary>
    private static (string Body, int Dropped, int Kept) DropDead(string body, bool renamed, Func<string, bool> has)
    {
        string[] lines = body.Split('\n');
        List<int> starts = [];
        for (int i = 0; i < lines.Length; i++)
            if (renamed ? RenameFrom().IsMatch(lines[i]) : RequirementLine().IsMatch(lines[i]) || RemovedBullet().IsMatch(lines[i])) starts.Add(i);
        if (starts.Count == 0) return (body, 0, 0);
        List<string> kept = lines.Take(starts[0]).ToList();
        int dropped = 0, keptItems = 0;
        for (int k = 0; k < starts.Count; k++)
        {
            string[] item = lines[starts[k]..(k + 1 < starts.Count ? starts[k + 1] : lines.Length)];
            Match head = renamed ? RenameFrom().Match(item[0]) : RequirementLine().Match(item[0]);
            if (!head.Success) head = RemovedBullet().Match(item[0]);
            string? to = renamed ? item.Select(l => RenameTo().Match(l)).FirstOrDefault(m => m.Success)?.Groups["r"].Value : null;
            if (!has(head.Groups["r"].Value) && (to is null || !has(to))) { dropped++; continue; }
            kept.AddRange(item);
            keptItems++;
        }
        return (string.Join("\n", kept).Trim('\n'), dropped, keptItems);
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
