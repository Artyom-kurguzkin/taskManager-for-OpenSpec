using System.Globalization;

namespace BoardSync.Merge;

/// <summary>events.log is append-only: the union of both sides' lines, each once, ordered by the leading time. A line one side removed stays. Never a conflict.</summary>
internal static class EventLogMerge
{
    internal static List<string> Merge(List<string> local, List<string> remote)
    {
        HashSet<string> seen = [];
        List<(string Line, DateTime At)> all = [];
        DateTime carry = DateTime.MinValue;
        foreach (string line in local.Concat(remote).Where(l => l.Trim().Length > 0))
        {
            if (!seen.Add(line)) continue;
            string time = line.Split('|')[0].Trim();
            if (DateTime.TryParse(time, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTime at)) carry = at;   // a line without a time sorts with the one before it
            all.Add((line, carry));
        }
        return [.. all.OrderBy(x => x.At).Select(x => x.Line)];   // OrderBy is stable
    }
}
