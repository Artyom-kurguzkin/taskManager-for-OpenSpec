using System.Text.RegularExpressions;

namespace WorkItems.BusinessRules;

/// <summary>
/// A change's delta spec against the current main spec. Archiving replaces a MODIFIED requirement with the delta's whole
/// block and refuses to drop a scenario the main spec has since gained (another item's archive added it). Refresh puts
/// such scenarios back into the block, verbatim from the main spec, so an archive never stops on a stale delta. It also
/// repairs everything else the CLI would refuse and that has one obvious fix: names that differ from the main spec only in
/// case or spacing take the main spec's spelling; a MODIFIED requirement the main spec lacks (or any MODIFIED when it has no
/// spec yet) becomes ADDED, and an ADDED one it has becomes MODIFIED; a requirement listed twice is merged; REMOVED / RENAMED
/// entries that cannot apply, repeat, contradict each other or name a requirement the delta also writes are dropped; and
/// requirements outside any delta section go under ADDED.
/// </summary>
internal static partial class DeltaSpec
{
    [GeneratedRegex(@"^##[ \t]+(?<title>.+?)[ \t]*$", RegexOptions.Multiline)]
    private static partial Regex Section();

    [GeneratedRegex(@"^(?<kind>ADDED|MODIFIED|REMOVED|RENAMED)\s+Requirements$", RegexOptions.IgnoreCase)]
    private static partial Regex DeltaTitle();

    [GeneratedRegex(@"^###[ \t]*Requirement:[ \t]*(?<r>.+?)[ \t]*$", RegexOptions.Multiline | RegexOptions.IgnoreCase)]
    private static partial Regex Requirement();

    [GeneratedRegex(@"^#### Scenario:[ \t]*(?<s>.+?)[ \t]*$", RegexOptions.Multiline)]
    private static partial Regex Scenario();

    [GeneratedRegex(@"^###[ \t]*Requirement:[ \t]*(?<r>.+?)[ \t]*$", RegexOptions.IgnoreCase)]
    private static partial Regex RequirementLine();

    [GeneratedRegex(@"^\s*[-*+]\s*`?###\s*Requirement:\s*(?<r>.+?)`?\s*$")]
    private static partial Regex RemovedBullet();

    [GeneratedRegex(@"^\s*[-*+]?\s*FROM:\s*`?###\s*Requirement:\s*(?<r>.+?)`?\s*$")]
    private static partial Regex RenameFrom();

    [GeneratedRegex(@"^\s*[-*+]?\s*TO:\s*`?###\s*Requirement:\s*(?<r>.+?)`?\s*$")]
    private static partial Regex RenameTo();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    [GeneratedRegex(@"[ \t]+#+[ \t]*$")]
    private static partial Regex ClosingHashes();

    /// <summary>
    /// The delta made safe to archive against the main spec (empty main = the capability has no spec yet): names spelled differently
    /// from the main spec's (case, spacing) take its spelling; a MODIFIED requirement the main spec lacks becomes ADDED, an ADDED
    /// one it has becomes MODIFIED; a MODIFIED block naming a renamed requirement's old name takes the new one; a requirement listed
    /// twice is merged into one block; REMOVED / RENAMED entries that cannot apply (source or target missing or taken, listed twice,
    /// FROM without TO or the reverse) are dropped, as is a REMOVED entry for a requirement the delta also writes or renames; requirements
    /// above the first section or in a delta without sections go under ADDED; section headings come out canonical; and every scenario
    /// of the main spec is appended to the MODIFIED requirement that lacks it. Unchanged when nothing is wrong.
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
        public string Raw = "";                                  // REMOVED / RENAMED / other: the section as written (canonical head); empty = gone
        public string Body = "";                                 // REMOVED / RENAMED: the entries
        public string Intro = "";                                // ADDED / MODIFIED: text before the first requirement
        public List<(string Name, string Text)> Blocks = [];     // ADDED / MODIFIED: the section's requirements
    }

    /// <summary>
    /// The structural repairs, on "\n" text (see <see cref="Refresh"/>). RENAMED entries are settled first, since the blocks and the
    /// REMOVED entries depend on which pairs survive. The text comes back untouched when none applies.
    /// </summary>
    private static string Normalize(string text, Dictionary<string, List<(string Name, string Text)>> current)
    {
        List<(int Start, int End, string Kind)> sections = Sections(text).ToList();
        int first = sections.Count > 0 ? sections[0].Start : text.Length;
        List<(int Start, int End, string Name)> loose = Blocks(text, 0, first).ToList();   // requirements outside any section
        if (sections.Count == 0 && loose.Count == 0) return text;
        string? Find(string name) => current.ContainsKey(name) ? name : current.Keys.FirstOrDefault(k => Fold(k) == Fold(name));   // main's exact spelling

        bool dirty = loose.Count > 0;
        List<Part> parts = [];
        foreach ((int start, int end, string kind) in sections)
        {
            int lineEnd = text.IndexOf('\n', start);
            if (lineEnd < 0 || lineEnd > end) lineEnd = end;
            int bodyStart = Math.Min(lineEnd + 1, end);
            string title = Section().Match(text, start).Groups["title"].Value;
            if (kind != "" && Whitespace().Replace(title, " ") != title) dirty = true;   // the CLI would not read this heading
            Part part = new(kind, kind == "" ? text[start..lineEnd] : $"## {kind} Requirements");
            if (kind is "ADDED" or "MODIFIED")
            {
                List<(int Start, int End, string Name)> blocks = Blocks(text, start, end).ToList();
                part.Intro = text[bodyStart..(blocks.Count > 0 ? blocks[0].Start : end)].Trim();
                part.Blocks = blocks.Select(b => (b.Name, text[b.Start..b.End].TrimEnd('\n'))).ToList();
            }
            else
            {
                part.Raw = kind == "" ? text[start..end].TrimEnd('\n') : part.Head + text[lineEnd..end].TrimEnd('\n');
                part.Body = text[bodyStart..end];
            }
            parts.Add(part);
        }
        if (loose.Count > 0)
        {
            Part added = parts.FirstOrDefault(p => p.Kind == "ADDED") ?? new Part("ADDED", "## ADDED Requirements");
            if (!parts.Contains(added)) parts.Insert(0, added);
            added.Blocks.InsertRange(0, loose.Select(b => (b.Name, text[b.Start..b.End].TrimEnd('\n'))));
        }

        List<(string From, string To)> pairs = [];
        HashSet<string> froms = [], tos = [];
        foreach (Part part in parts.Where(p => p.Kind == "RENAMED"))
        {
            (string body, bool changed, int kept) = FixRenames(part.Body, Find, froms, tos, pairs);
            if (changed) { dirty = true; part.Raw = kept == 0 ? "" : part.Head + "\n\n" + body; }
        }

        // the requirement blocks, grouped by their final name in order of first appearance: first text, union of scenarios, the stronger section
        List<string> order = [];
        Dictionary<string, (string Kind, Part Home, string Text, List<string> Extra)> groups = [];
        foreach (Part part in parts.Where(p => p.Kind is "ADDED" or "MODIFIED"))
        {
            foreach ((string orig, string original) in part.Blocks)
            {
                string name = orig, block = original, kind = part.Kind;
                string? exact = Find(name);
                if (exact is not null) { name = exact; kind = "MODIFIED"; }
                else if (tos.Contains(name)) kind = "MODIFIED";
                if (kind == "MODIFIED")
                {
                    int rename = pairs.FindIndex(p => Fold(p.From) == Fold(name));
                    if (rename >= 0) name = pairs[rename].To;   // after a rename, MODIFIED names the new header
                    else if (exact is null && !tos.Contains(name)) kind = "ADDED";
                }
                if (name != orig) { dirty = true; block = "### Requirement: " + name + (block.Contains('\n') ? block[block.IndexOf('\n')..] : ""); }
                if (kind != part.Kind) dirty = true;
                if (!groups.TryGetValue(name, out var g)) { order.Add(name); groups[name] = (kind, part, block, []); continue; }
                dirty = true;
                HashSet<string> have = Scenario().Matches(g.Text).Select(m => m.Groups["s"].Value).ToHashSet();
                have.UnionWith(g.Extra.SelectMany(e => Scenario().Matches(e).Select(m => m.Groups["s"].Value)));
                foreach ((string sName, string sText) in Scenarios(block).GetValueOrDefault(name) ?? [])
                    if (have.Add(sName)) g.Extra.Add(sText);
                groups[name] = (g.Kind == "MODIFIED" || kind == "MODIFIED" ? "MODIFIED" : "ADDED", g.Home, g.Text, g.Extra);
            }
            part.Blocks = [];
        }

        HashSet<string> written = groups.Keys.Concat(pairs.Select(p => p.From)).Select(Fold).ToHashSet();   // a REMOVED entry for these contradicts the rest
        HashSet<string> removed = [];
        foreach (Part part in parts.Where(p => p.Kind == "REMOVED"))
        {
            (string body, bool changed, int kept) = FixRemoved(part.Body, Find, written, removed);
            if (changed) { dirty = true; part.Raw = kept == 0 ? "" : part.Head + "\n\n" + body; }
        }
        if (!dirty) return text;

        foreach (string name in order)
        {
            (string kind, Part home, string block, List<string> extra) = groups[name];
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
        string preamble = text[..(loose.Count > 0 ? loose[0].Start : first)].TrimEnd('\n');
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

    private static string Norm(string name) => ClosingHashes().Replace(name, "").Trim();

    private static string Fold(string name) => Whitespace().Replace(Norm(name), " ").ToLowerInvariant();

    private static string Splice(string line, Group g, string value) => line[..g.Index] + value + line[(g.Index + g.Length)..];

    /// <summary>
    /// A RENAMED section's body without the pairs that cannot apply: no TO for a FROM (or no FROM for a TO), a source missing from the
    /// main spec (unless the target is already there, which the CLI treats as done), a target the main spec has, or a FROM / TO seen before.
    /// A FROM spelled differently from the main spec's takes its spelling. The pairs that stay are added to <paramref name="pairs"/>.
    /// </summary>
    private static (string Body, bool Changed, int Kept) FixRenames(string body, Func<string, string?> find, HashSet<string> froms, HashSet<string> tos, List<(string From, string To)> pairs)
    {
        string[] lines = body.Split('\n');
        List<int> starts = Enumerable.Range(0, lines.Length).Where(i => RenameFrom().IsMatch(lines[i])).ToList();
        int lead = starts.Count > 0 ? starts[0] : lines.Length;
        List<string> kept = lines.Take(lead).Where(l => !RenameTo().IsMatch(l)).ToList();   // a TO before any FROM
        bool changed = kept.Count != lead;
        int keptItems = 0;
        for (int k = 0; k < starts.Count; k++)
        {
            string[] item = lines[starts[k]..(k + 1 < starts.Count ? starts[k + 1] : lines.Length)];
            int toAt = Array.FindIndex(item, l => RenameTo().IsMatch(l));
            if (toAt < 0) { changed = true; continue; }
            int length = item.Length;
            item = item.Where((l, i) => i <= toAt || !RenameTo().IsMatch(l)).ToArray();   // a second TO has no FROM
            changed |= item.Length != length;
            Match from = RenameFrom().Match(item[0]);
            string f = Norm(from.Groups["r"].Value), t = Norm(RenameTo().Match(item[toAt]).Groups["r"].Value);
            string? fe = find(f), te = find(t);
            bool ok = fe is null ? te == t : te is null || (te != t && te == fe);   // te == t: the target exists exactly; te == fe: a case-only rename
            string key = fe ?? f;
            if (!ok || froms.Contains(key) || tos.Contains(t)) { changed = true; continue; }
            froms.Add(key);
            tos.Add(t);
            if (fe is not null)
            {
                pairs.Add((fe, t));
                if (fe != f) { changed = true; item[0] = Splice(item[0], from.Groups["r"], fe); }
            }
            kept.AddRange(item);
            keptItems++;
        }
        return (string.Join("\n", kept).Trim('\n'), changed, keptItems);
    }

    /// <summary>
    /// A REMOVED section's body without the entries that cannot apply: a requirement the main spec lacks, one the delta also writes or
    /// renames (<paramref name="written"/>, folded), or one removed before. A name spelled differently from the main spec's takes its spelling.
    /// </summary>
    private static (string Body, bool Changed, int Kept) FixRemoved(string body, Func<string, string?> find, HashSet<string> written, HashSet<string> removed)
    {
        string[] lines = body.Split('\n');
        List<int> starts = Enumerable.Range(0, lines.Length).Where(i => RequirementLine().IsMatch(lines[i]) || RemovedBullet().IsMatch(lines[i])).ToList();
        if (starts.Count == 0) return (body, false, 0);
        List<string> kept = lines.Take(starts[0]).ToList();
        bool changed = false;
        int keptItems = 0;
        for (int k = 0; k < starts.Count; k++)
        {
            string[] item = lines[starts[k]..(k + 1 < starts.Count ? starts[k + 1] : lines.Length)];
            Match head = RequirementLine().Match(item[0]);
            if (!head.Success) head = RemovedBullet().Match(item[0]);
            string name = Norm(head.Groups["r"].Value);
            string? exact = find(name);
            if (exact is null || written.Contains(Fold(exact)) || !removed.Add(exact)) { changed = true; continue; }
            if (exact != name) { changed = true; item[0] = Splice(item[0], head.Groups["r"], exact); }
            kept.AddRange(item);
            keptItems++;
        }
        return (string.Join("\n", kept).Trim('\n'), changed, keptItems);
    }

    /// <summary>(start, end, kind) of every "## " section; kind is ADDED / MODIFIED / REMOVED / RENAMED (the headings the CLI reads, any case) or empty for any other heading.</summary>
    private static IEnumerable<(int Start, int End, string Kind)> Sections(string text)
    {
        List<Match> heads = Section().Matches(text).ToList();
        for (int i = 0; i < heads.Count; i++)
            yield return (heads[i].Index, i + 1 < heads.Count ? heads[i + 1].Index : text.Length,
                DeltaTitle().Match(heads[i].Groups["title"].Value) is { Success: true } d ? d.Groups["kind"].Value.ToUpperInvariant() : "");
    }

    /// <summary>(start, end, name) of every requirement block between <paramref name="from"/> and <paramref name="to"/>; a block ends where the next requirement or section starts.</summary>
    private static IEnumerable<(int Start, int End, string Name)> Blocks(string text, int from, int to)
    {
        List<Match> reqs = Requirement().Matches(text).Where(m => m.Index >= from && m.Index < to).ToList();
        for (int i = 0; i < reqs.Count; i++)
            yield return (reqs[i].Index, i + 1 < reqs.Count ? reqs[i + 1].Index : to, Norm(reqs[i].Groups["r"].Value));
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
