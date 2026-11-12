using System.Text;
using System.Text.RegularExpressions;

namespace BoardSync.Merge;

/// <summary>
/// A .openspec.yaml (or a note's frontmatter) merged key by key. The text is never parsed into objects and written back: it is cut into entries (a key's line plus the
/// deeper lines under it, the x-rtm children as "x-rtm.key") and each entry keeps its own lines, so what nobody changed keeps its formatting and key order.
/// Scalars: the side that changed against the base wins, both changed: the newer side wins and it is reported. Inline lists ([a, b]): each side's additions and removals.
/// </summary>
internal static partial class YamlMerge
{
    [GeneratedRegex(@"^(?<indent> *)(?<key>[A-Za-z0-9_.\-]+):(?<rest>(?:\s.*)?)$", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex KeyLine();

    internal static List<string> Merge(List<string> baseLines, List<string> local, List<string> remote, MergeContext ctx) =>
        [.. Weave.Merge(Parse(baseLines), Parse(local), Parse(remote), (b, l, r) => Weave.Decide(b, l, r, ctx, Label((l ?? r)!.Key), e => e.Value, Lists))
            .SelectMany(e => e.Text.Split('\n'))];

    private static string Label(string key) => key.StartsWith("x-rtm.", StringComparison.Ordinal) ? key["x-rtm.".Length..] : key;

    private static List<Entry> Parse(List<string> lines)
    {
        List<(string Key, List<string> Lines)> raw = [];
        string? container = null;
        int childIndent = -1;
        foreach (string line in lines)
        {
            string? key = null;
            if (KeyLine().Match(line) is { Success: true } m)
            {
                int indent = m.Groups["indent"].Length;
                string k = m.Groups["key"].Value;
                if (indent == 0)
                {
                    key = k;
                    container = k == "x-rtm" && m.Groups["rest"].Value.Trim().Length == 0 ? k : null;
                    childIndent = -1;
                }
                else if (container is not null)
                {
                    if (childIndent < 0) childIndent = indent;
                    if (indent == childIndent) key = container + "." + k;
                }
            }
            if (key is not null) raw.Add((key, [line]));
            else if (raw.Count > 0) raw[^1].Lines.Add(line);
            else raw.Add(("#header", [line]));
        }

        Dictionary<string, int> seen = [];
        return [.. raw.Select(e =>
        {
            string first = KeyLine().Match(e.Lines[0]) is { Success: true } m ? m.Groups["rest"].Value.Trim() : e.Lines[0];
            string value = string.Join("\n", e.Lines.Skip(1).Select(l => l.TrimEnd()).Prepend(first)).TrimEnd();
            return new Entry(Weave.Unique(seen, e.Key), value, string.Join("\n", e.Lines));
        })];
    }

    /// <summary>Both sides changed an inline list: keep what the base had unless a side removed it, then local additions, then remote additions.</summary>
    private static Entry? Lists(Entry? b, Entry l, Entry r)
    {
        List<string>? li = Items(l), ri = Items(r), bi = b is null ? [] : Items(b);
        if (li is null || ri is null || bi is null) return null;
        List<string> items = [.. bi.Where(x => li.Contains(x) && ri.Contains(x)), .. li.Where(x => !bi.Contains(x)), .. ri.Where(x => !bi.Contains(x))];
        items = [.. items.Distinct()];
        if (items.SequenceEqual(li)) return l;
        if (items.SequenceEqual(ri)) return r;
        string value = "[" + string.Join(", ", items) + "]";
        return new Entry(l.Key, value, l.Text[..(l.Text.IndexOf(':') + 1)] + " " + value);
    }

    private static List<string>? Items(Entry e)
    {
        if (e.Text.Contains('\n') || !e.Value.StartsWith('[') || !e.Value.EndsWith(']')) return null;
        return Split(e.Value[1..^1]);
    }

    /// <summary>The top-level items of a flow list, commas inside quotes, braces and brackets do not split.</summary>
    private static List<string> Split(string inner)
    {
        List<string> items = [];
        StringBuilder sb = new();
        int depth = 0;
        char quote = '\0', prev = '\0';
        bool escaped = false;
        foreach (char c in inner)
        {
            if (quote != '\0')
            {
                if (c == quote && !escaped) quote = '\0';
                escaped = c == '\\' && quote == '"' && !escaped;
            }
            else if ((c == '"' || c == '\'') && (prev == '\0' || "{[:,".Contains(prev))) quote = c;
            else if (c is '{' or '[') depth++;
            else if (c is '}' or ']') depth--;
            else if (c == ',' && depth == 0)
            {
                items.Add(sb.ToString().Trim());
                sb.Clear();
                prev = '\0';
                continue;
            }
            sb.Append(c);
            if (!char.IsWhiteSpace(c)) prev = c;
        }
        if (sb.ToString().Trim() is { Length: > 0 } last) items.Add(last);
        return items;
    }
}
