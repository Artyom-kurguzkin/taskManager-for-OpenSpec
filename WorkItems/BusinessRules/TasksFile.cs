using System.Text.RegularExpressions;
using WorkItems.PublicDTOs;

namespace WorkItems.BusinessRules;

/// <summary>
/// tasks.md checkboxes. A box holding only x (any case, any spacing) is done; anything else is open.
/// Edits change single lines so the rest of the file is untouched.
/// </summary>
internal static partial class TasksFile
{
    [GeneratedRegex(@"^(?<indent>\s*)- \[(?<box>[^\]]*)\] (?:(?<num>\d+(?:\.\d+)*) )?(?<text>.*)$")]
    private static partial Regex Checkbox();

    [GeneratedRegex(@"^##\s+(?<g>.+)$")]
    private static partial Regex GroupHeading();

    internal static IReadOnlyList<Subtask> Parse(string text)
    {
        List<Subtask> list = [];
        string group = "";
        int unnumbered = 0;
        foreach (string line in Lines(text))
        {
            if (GroupHeading().Match(line) is { Success: true } g) { group = g.Groups["g"].Value.Trim(); continue; }
            if (Checkbox().Match(line) is not { Success: true } m) continue;
            string num = m.Groups["num"].Success ? m.Groups["num"].Value : $"#{++unnumbered}";
            list.Add(new Subtask(num, m.Groups["text"].Value.Trim(), IsDone(m.Groups["box"].Value), group));
        }
        return list;
    }

    private static bool IsDone(string box) => box.Trim().Equals("x", StringComparison.OrdinalIgnoreCase);

    internal static string SetDone(string text, string number, bool done) =>
        MapCheckboxes(text, (m, line) => Num(m) == number ? $"{m.Groups["indent"].Value}- [{(done ? "x" : " ")}] {line[(m.Groups["num"].Index)..]}" : line);

    /// <summary>Replaces one subtask's text, keeping its indent, box and number.</summary>
    internal static string SetText(string text, string number, string task) =>
        MapCheckboxes(text, (m, line) => Num(m) == number ? line[..m.Groups["text"].Index] + task : line);

    internal static string Append(string text, string task)
    {
        IReadOnlyList<Subtask> subs = Parse(text);
        string nl = text.Contains("\r\n") ? "\r\n" : "\n";
        if (subs.Count == 0)
        {
            // Reuse the last group heading if there is one ("## 2. Hardening" → 2.1), else start group 1.
            string? heading = Lines(text).LastOrDefault(l => GroupHeading().IsMatch(l));
            if (heading is null) return text.TrimEnd() + $"{nl}{nl}## 1. Tasks{nl}{nl}- [ ] 1.1 {task}{nl}";
            string group = Regex.Match(heading, @"^##\s+(\d+)\.").Groups[1] is { Success: true } g ? g.Value : "1";
            return text.TrimEnd() + $"{nl}{nl}- [ ] {group}.1 {task}{nl}";
        }
        string last = subs[^1].Number;
        string next = last.Contains('.') ? $"{last[..last.LastIndexOf('.')]}.{int.Parse(last[(last.LastIndexOf('.') + 1)..]) + 1}" : $"{subs.Count + 1}";
        List<string> lines = Lines(text).ToList();
        int at = lines.FindLastIndex(l => Checkbox().IsMatch(l)) + 1;
        lines.Insert(at, $"- [ ] {next} {task}");
        return string.Join(nl, lines) + nl;
    }

    /// <summary>Removes the given subtasks and renumbers what is left within each group (1.1, 1.2, …).</summary>
    internal static string Remove(string text, IReadOnlyCollection<string> numbers)
    {
        string nl = text.Contains("\r\n") ? "\r\n" : "\n";
        Dictionary<string, int> seq = [];
        List<string> kept = [];
        foreach (string line in Lines(text))
        {
            if (Checkbox().Match(line) is { Success: true } m && m.Groups["num"].Success)
            {
                if (numbers.Contains(Num(m))) continue;
                string prefix = Num(m).Contains('.') ? Num(m)[..Num(m).LastIndexOf('.')] : "";
                int n = seq[prefix] = seq.GetValueOrDefault(prefix) + 1;
                string renumbered = prefix.Length > 0 ? $"{prefix}.{n}" : $"{n}";
                kept.Add($"{m.Groups["indent"].Value}- [{m.Groups["box"].Value}] {renumbered} {m.Groups["text"].Value}");
                continue;
            }
            kept.Add(line);
        }
        return string.Join(nl, kept) + nl;
    }

    /// <summary>A fresh tasks.md holding the given subtasks as group 1.</summary>
    internal static string Create(IEnumerable<Subtask> subtasks) =>
        "# Tasks\n\n## 1. Tasks\n\n" + string.Concat(subtasks.Select((s, i) => $"- [{(s.Done ? "x" : " ")}] 1.{i + 1} {s.Text}\n"));

    /// <summary>A tasks.md with one numbered group per entry (heading text without its number), its subtasks numbered n.1, n.2, …</summary>
    internal static string CreateGrouped(IEnumerable<(string Heading, IReadOnlyList<Subtask> Subtasks)> groups)
    {
        System.Text.StringBuilder sb = new("# Tasks\n");
        int g = 0;
        foreach ((string heading, IReadOnlyList<Subtask> subs) in groups)
        {
            g++;
            sb.Append($"\n## {g}. {heading}\n\n");
            for (int i = 0; i < subs.Count; i++) sb.Append($"- [{(subs[i].Done ? "x" : " ")}] {g}.{i + 1} {subs[i].Text}\n");
        }
        return sb.ToString();
    }

    private static string Num(Match m) => m.Groups["num"].Value;

    private static string MapCheckboxes(string text, Func<Match, string, string> map)
    {
        string nl = text.Contains("\r\n") ? "\r\n" : "\n";
        bool trailing = text.EndsWith(nl);
        string result = string.Join(nl, Lines(text).Select(l => Checkbox().Match(l) is { Success: true } m && m.Groups["num"].Success ? map(m, l) : l));
        return trailing ? result + nl : result;
    }

    private static IEnumerable<string> Lines(string text)
    {
        string body = text.Replace("\r\n", "\n");
        if (body.EndsWith('\n')) body = body[..^1];
        return body.Length == 0 ? [] : body.Split('\n');
    }
}
