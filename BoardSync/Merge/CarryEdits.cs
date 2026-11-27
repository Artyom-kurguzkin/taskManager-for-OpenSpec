using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace BoardSync.Merge;

/// <summary>A file under a change folder that was merged into another item: its text when the merge was made (null: it had none) and now (null: it is gone, which carries nothing).</summary>
internal sealed record CarryEdit(string? Base, string? Mine);

/// <summary>What carrying edits changes in the merged item: the new text of every file (paths relative to the merged item), and whether a note was written for what could not be applied.</summary>
internal sealed record CarryOutcome(IReadOnlyDictionary<string, string> Files, bool WroteNote);

/// <summary>
/// The edits made to a change folder on a computer that had not yet seen it merged into another item, carried into that item (OpenSpec never deletes; nothing is dropped).
/// Events are added marked "| from #wi&lt;id&gt;"; notes are copied with mergedFrom and mergedAt (renumbered when the number is taken) or merged with the item's copy;
/// subtasks added or ticked go to the item's group; list fields of .openspec.yaml are unioned. A changed title, status, priority, label or date, and a changed proposal,
/// delta spec or other file, cannot be applied (the merged item has its own): they are written as one note in the merged item.
/// Pure: it reads and writes nothing; the caller hands in the texts and takes the new ones.
/// </summary>
internal static partial class CarryEdits
{
    [GeneratedRegex(@"^(?:.*/)?notes/n(?<n>\d+)\.md$", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex NotePath();

    [GeneratedRegex(@"^(?<indent>\s*)- \[(?<box>[^\]]*)\] (?:(?<num>\d+(?:\.\d+)*) )?(?<text>.*)$", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex Checkbox();

    [GeneratedRegex(@"^##\s+(?<g>.+)$", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex Heading();

    [GeneratedRegex(@"^\d+\.\s+", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex HeadingNumber();

    [GeneratedRegex(@"^(?<n>\d+)\.", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex HeadingNumberOnly();

    [GeneratedRegex(@"^ {2}(?<key>[A-Za-z0-9_.\-]+):(?<value>.*)$", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex RtmChild();

    [GeneratedRegex(@" \| from #wi\d+$", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex FromMarker();

    /// <summary>x-rtm keys that are never carried, not even as a note: the merge decides them, or they say who touched the file.</summary>
    private static readonly HashSet<string> Skipped = new(StringComparer.Ordinal) { "id", "mergedFrom", "mergedFolders", "agentBy", "agentAt", "reviewedAt" };

    /// <param name="fromId">The id of the item that was merged away.</param>
    /// <param name="fromTitle">Its title as the merge saw it (the merged item's group heading says it).</param>
    /// <param name="edits">Its files that changed since the merge, by path relative to its folder.</param>
    /// <param name="z">The text of a file of the merged item (path relative to it), null when it has none.</param>
    /// <param name="noteIdTaken">Whether another note anywhere already has this number.</param>
    /// <param name="newNoteId">A note number nobody has, a new one on every call.</param>
    internal static CarryOutcome Carry(int fromId, string fromTitle, IReadOnlyDictionary<string, CarryEdit> edits, Func<string, string?> z,
        Func<int, bool> noteIdTaken, Func<int> newNoteId, DateTime now)
    {
        Dictionary<string, string> files = new(StringComparer.Ordinal);
        string day = now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        List<string> notApplied = [];
        string? Current(string path) => files.TryGetValue(path, out string? text) ? text : z(path);

        foreach ((string rest, CarryEdit edit) in edits.OrderBy(e => e.Key, StringComparer.Ordinal))
        {
            if (edit.Mine is null || edit.Mine == edit.Base) continue;       // gone here carries nothing: the merged item keeps what it has
            if (rest == "events.log") files[rest] = Events(fromId, edit, Current(rest));
            else if (rest == "tasks.md") files[rest] = Tasks(fromId, fromTitle, day, edit, Current(rest));
            else if (rest == ".openspec.yaml") files[rest] = Yaml(edit, Current(rest), notApplied);
            else if (NotePath().Match(rest) is { Success: true } m && rest.StartsWith("notes/", StringComparison.Ordinal))
            {
                (string path, string text) = Note(fromId, day, int.Parse(m.Groups["n"].Value, CultureInfo.InvariantCulture), rest, edit, Current, noteIdTaken, newNoteId, notApplied);
                files[path] = text;
            }
            else notApplied.Add($"## {rest}\n\n{Fence(edit.Mine)}");
        }

        if (notApplied.Count > 0)
        {
            int id = newNoteId();
            string title = $"#wi{fromId} was edited on another computer after the merge";
            files[$"notes/n{id}.md"] = $"---\nid: {id}\nby: Sync\ncreated: {now.ToString("s", CultureInfo.InvariantCulture)}\nmergedFrom: {fromId}\nmergedAt: {day}\n---\n# {title}\n\n"
                + $"{fromTitle} (#wi{fromId}) was merged into this item. Its folder was edited on another computer before that computer had seen the merge. What could not be applied to this item is here.\n\n"
                + string.Join("\n\n", notApplied) + "\n";
        }
        return new CarryOutcome(files, notApplied.Count > 0);
    }

    private static string Fence(string text) => "```\n" + text.TrimEnd('\r', '\n') + "\n```";

    // ---- events.log ----------------------------------------------------------------------------------------------------------

    private static List<string> LinesOf(string? text) => [.. (text ?? "").Split('\n').Select(l => l.TrimEnd('\r')).Where(l => l.Trim().Length > 0)];

    private static string NewlineOf(string? text) => text is not null && text.Contains("\r\n") ? "\r\n" : "\n";

    private static string Events(int fromId, CarryEdit edit, string? zLog)
    {
        HashSet<string> had = [.. LinesOf(edit.Base)];
        List<string> zLines = LinesOf(zLog);
        HashSet<string> inZ = [.. zLines.Select(l => FromMarker().Replace(l, ""))];       // a line is not added twice, marked or not
        List<string> added = [.. LinesOf(edit.Mine).Where(l => !had.Contains(l) && !inZ.Contains(FromMarker().Replace(l, "")))
            .Select(l => FromMarker().IsMatch(l) ? l : l + " | from #wi" + fromId.ToString(CultureInfo.InvariantCulture))];
        if (added.Count == 0) return zLog ?? "";
        string newline = NewlineOf(zLog);
        return string.Concat(EventLogMerge.Merge(zLines, added).Select(l => l + newline));
    }

    // ---- notes ---------------------------------------------------------------------------------------------------------------

    private static (string Path, string Text) Note(int fromId, string day, int number, string rest, CarryEdit edit, Func<string, string?> current,
        Func<int, bool> noteIdTaken, Func<int> newNoteId, List<string> notApplied)
    {
        string? zCopy = current(rest);
        if (edit.Base is not null && zCopy is not null)
        {
            // Edited here since the merge, and the merged item has this note (the merge copied it): three-way, as two computers editing one note.
            BoardSync.PublicDTOs.MergeResult merged = FileMerge.Run(rest, edit.Base, edit.Mine!, zCopy, remoteIsNewer: false);
            foreach (BoardSync.PublicDTOs.MergeConflict c in merged.Conflicts)
                notApplied.Add($"## {rest}\n\nBoth edited the same place; the merged item's text was kept. The text written on the other computer:\n\n{Fence(c.Dropped)}");
            return (rest, merged.Text);
        }

        // A note written here, or one the merged item lacks: copied in as a merge copies a note, stamped with where it came from, and renumbered when its number is taken.
        string path = rest, text = StampMerged(edit.Mine!, fromId, day);
        if (current(rest) is not null || noteIdTaken(number))
        {
            int id = newNoteId();
            path = $"notes/n{id}.md";
            text = Regex.Replace(text, @"(?m)^id:\s*\d+\s*$", $"id: {id}", RegexOptions.None, TimeSpan.FromSeconds(1));
        }
        return (path, text);
    }

    /// <summary>mergedFrom and mergedAt after the keys the note has; a note that already says where it came from keeps that; a note with no frontmatter gets one.</summary>
    private static string StampMerged(string text, int from, string day)
    {
        string nl = NewlineOf(text);
        string stamp = $"mergedFrom: {from}{nl}mergedAt: {day}{nl}";
        Match fm = Regex.Match(text, @"\A---[ \t]*\r?\n(?<keys>(?:.*\r?\n)*?)---[ \t]*(?:\r?\n|\z)", RegexOptions.None, TimeSpan.FromSeconds(1));
        if (!fm.Success) return $"---{nl}{stamp}---{nl}{text}";
        if (Regex.IsMatch(fm.Groups["keys"].Value, @"^mergedFrom:", RegexOptions.Multiline, TimeSpan.FromSeconds(1))) return text;
        return text.Insert(fm.Groups["keys"].Index + fm.Groups["keys"].Length, stamp);
    }

    // ---- tasks.md ------------------------------------------------------------------------------------------------------------

    private sealed record Sub(string Group, string Text, bool Done);

    private static List<Sub> TasksOf(string? text)
    {
        List<Sub> tasks = [];
        string group = "";
        foreach (string line in LinesOf(text))
        {
            if (Heading().Match(line) is { Success: true } h) group = HeadingNumber().Replace(h.Groups["g"].Value.Trim(), "");
            else if (Checkbox().Match(line) is { Success: true } c) tasks.Add(new Sub(group, c.Groups["text"].Value.Trim(), c.Groups["box"].Value.Trim().Equals("x", StringComparison.OrdinalIgnoreCase)));
        }
        return tasks;
    }

    private static string Tasks(int fromId, string fromTitle, string day, CarryEdit edit, string? zText)
    {
        List<Sub> before = TasksOf(edit.Base), mine = TasksOf(edit.Mine);
        List<string> lines = [.. (zText ?? "# Tasks\n").Split('\n').Select(l => l.TrimEnd('\r'))];
        if (lines.Count > 0 && lines[^1].Length == 0) lines.RemoveAt(lines.Count - 1);
        string newline = NewlineOf(zText);

        foreach (Sub t in mine)
        {
            Sub? was = before.FirstOrDefault(b => b.Group == t.Group && b.Text == t.Text);
            if (was is not null && was.Done == t.Done) continue;       // nothing changed on this one
            int heading = GroupOf(lines, fromId, fromTitle, t.Group);
            if (was is null)
            {
                if (heading < 0) heading = NewGroup(lines, fromId, fromTitle, day);
                if (!InGroup(lines, heading).Any(i => Checkbox().Match(lines[i]) is { Success: true } c && c.Groups["text"].Value.Trim() == t.Text))
                    AddSubtask(lines, heading, t);
            }
            else if (heading >= 0)
            {
                foreach (int i in InGroup(lines, heading))
                    if (Checkbox().Match(lines[i]) is { Success: true } c && c.Groups["text"].Value.Trim() == t.Text)
                        lines[i] = lines[i][..c.Groups["box"].Index] + (t.Done ? "x" : " ") + lines[i][(c.Groups["box"].Index + c.Groups["box"].Length)..];
            }
        }
        return string.Concat(lines.Select(l => l + newline));
    }

    /// <summary>The line index of the heading of the merged item's group for the source's group: "#wi&lt;id&gt; ..." (a source that was merged in), or the title's (the item the merge kept the name of); -1 when there is none.</summary>
    private static int GroupOf(List<string> lines, int fromId, string fromTitle, string group)
    {
        List<(int Line, string Name)> groups = [];
        for (int i = 0; i < lines.Count; i++)
            if (Heading().Match(lines[i]) is { Success: true } h) groups.Add((i, HeadingNumber().Replace(h.Groups["g"].Value.Trim(), "")));
        List<(int Line, string Name)> mine = [.. groups.Where(g => g.Name.StartsWith($"#wi{fromId} ", StringComparison.Ordinal))];
        if (mine.Count == 0) mine = [.. groups.Where(g => fromTitle.Length > 0 && (g.Name == fromTitle || g.Name.StartsWith(fromTitle + " · ", StringComparison.Ordinal)))];
        if (mine.Count == 0) return -1;
        if (mine.Count > 1 && group.Length > 0 && mine.FirstOrDefault(g => g.Name.Contains(group, StringComparison.Ordinal)) is { Name: not null } named && named.Name.Length > 0) return named.Line;
        return mine[0].Line;
    }

    private static int NewGroup(List<string> lines, int fromId, string fromTitle, string day)
    {
        int count = lines.Count(l => Heading().IsMatch(l));
        if (lines.Count > 0 && lines[^1].Trim().Length > 0) lines.Add("");
        lines.Add($"## {count + 1}. #wi{fromId} {fromTitle} · merged {day}");
        lines.Add("");
        return lines.Count - 2;
    }

    private static IEnumerable<int> InGroup(List<string> lines, int heading)
    {
        for (int i = heading + 1; i < lines.Count && !Heading().IsMatch(lines[i]); i++) yield return i;
    }

    private static void AddSubtask(List<string> lines, int heading, Sub t)
    {
        int last = heading;
        int max = 0;
        string prefix = HeadingNumberOnly().Match(HeadingText(lines[heading])) is { Success: true } hn ? hn.Groups["n"].Value : "1";
        foreach (int i in InGroup(lines, heading))
        {
            if (Checkbox().Match(lines[i]) is not { Success: true } c) continue;
            last = i;
            string num = c.Groups["num"].Value;
            if (num.Length == 0) continue;
            prefix = num.Contains('.') ? num[..num.LastIndexOf('.')] : prefix;
            if (int.TryParse(num.Contains('.') ? num[(num.LastIndexOf('.') + 1)..] : num, CultureInfo.InvariantCulture, out int n)) max = Math.Max(max, n);
        }
        string line = $"- [{(t.Done ? "x" : " ")}] {prefix}.{max + 1} {t.Text}";
        if (last == heading)
        {
            // an empty group: after the heading and its blank line
            int at = heading + 1;
            if (at < lines.Count && lines[at].Trim().Length == 0) at++; else lines.Insert(at++, "");
            lines.Insert(at, line);
        }
        else lines.Insert(last + 1, line);
    }

    private static string HeadingText(string line) => Heading().Match(line).Groups["g"].Value.Trim();

    // ---- .openspec.yaml ------------------------------------------------------------------------------------------------------

    private static Dictionary<string, string> RtmOf(List<string> lines)
    {
        Dictionary<string, string> keys = new(StringComparer.Ordinal);
        bool inside = false;
        foreach (string line in lines)
        {
            if (line.StartsWith("x-rtm:", StringComparison.Ordinal)) { inside = true; continue; }
            if (inside && line.Length > 0 && line[0] != ' ') inside = false;
            if (inside && RtmChild().Match(line) is { Success: true } m) keys[m.Groups["key"].Value] = m.Groups["value"].Value.Trim();
        }
        return keys;
    }

    private static string Yaml(CarryEdit edit, string? zText, List<string> notApplied)
    {
        string newline = NewlineOf(zText);
        List<string> lines = LinesOf(zText);
        Dictionary<string, string> before = RtmOf(LinesOf(edit.Base)), mine = RtmOf(LinesOf(edit.Mine)), inZ = RtmOf(lines);
        List<string> changes = [];

        foreach ((string key, string value) in mine)
        {
            if (Skipped.Contains(key) || before.TryGetValue(key, out string? was) && was == value) continue;
            if (value.StartsWith('[') && value.EndsWith(']') && (!before.TryGetValue(key, out string? b) || b.StartsWith('[')))
            {
                // a list: what this side added is added to the merged item's list
                List<string> had = SplitFlow(before.GetValueOrDefault(key) ?? "[]"), now = SplitFlow(value);
                List<string> add = [.. now.Where(i => !had.Contains(i))];
                if (add.Count == 0) continue;
                List<string> target = inZ.TryGetValue(key, out string? zv) && zv.StartsWith('[') ? SplitFlow(zv) : [];
                foreach (string item in add) if (!target.Contains(item)) target.Add(item);
                SetRtm(lines, key, $"[{string.Join(", ", target)}]");
                inZ[key] = $"[{string.Join(", ", target)}]";
            }
            else if (!inZ.TryGetValue(key, out string? current) || current != value)
                changes.Add($"- {key}: {(before.TryGetValue(key, out string? old) && old.Length > 0 ? old : "(none)")} -> {value}");
        }
        if (changes.Count > 0) notApplied.Add("## .openspec.yaml\n\nChanged on the other computer; this item keeps its own values:\n\n" + string.Join("\n", changes));
        return string.Concat(lines.Select(l => l + newline));
    }

    /// <summary>Replaces the value of an x-rtm child, or adds the key at the end of the x-rtm block.</summary>
    private static void SetRtm(List<string> lines, string key, string value)
    {
        int rtm = lines.FindIndex(l => l.StartsWith("x-rtm:", StringComparison.Ordinal));
        if (rtm < 0) { lines.Add("x-rtm:"); rtm = lines.Count - 1; }
        int end = rtm + 1;
        while (end < lines.Count && (lines[end].Length == 0 || lines[end][0] == ' '))
        {
            if (RtmChild().Match(lines[end]) is { Success: true } m && m.Groups["key"].Value == key) { lines[end] = $"  {key}: {value}"; return; }
            end++;
        }
        while (end > rtm + 1 && lines[end - 1].Trim().Length == 0) end--;
        lines.Insert(end, $"  {key}: {value}");
    }

    /// <summary>The items of an inline list "[a, b, {c: d, e: f}, "g, h"]", split at the commas that are not inside braces, brackets or quotes, each trimmed.</summary>
    internal static List<string> SplitFlow(string list)
    {
        string inner = list.Trim();
        if (inner.StartsWith('[') && inner.EndsWith(']')) inner = inner[1..^1];
        List<string> items = [];
        StringBuilder current = new();
        int depth = 0;
        char quote = '\0';
        for (int i = 0; i < inner.Length; i++)
        {
            char c = inner[i];
            if (quote != '\0')
            {
                current.Append(c);
                if (c == '\\' && quote == '"' && i + 1 < inner.Length) current.Append(inner[++i]);
                else if (c == quote) quote = '\0';
                continue;
            }
            if (c is '"' or '\'') quote = c;
            else if (c is '{' or '[') depth++;
            else if (c is '}' or ']') depth--;
            else if (c == ',' && depth == 0) { items.Add(current.ToString().Trim()); current.Clear(); continue; }
            current.Append(c);
        }
        if (current.ToString().Trim().Length > 0) items.Add(current.ToString().Trim());
        return items;
    }
}
