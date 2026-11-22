using System.Globalization;
using System.Text.RegularExpressions;
using OpenSpecStore;
using WorkItems.PublicDTOs;

namespace WorkItems.Infrastructure;

/// <summary>
/// events.log in a change folder: one line per event, <c>yyyy-MM-ddTHH:mm:ss | who | kind | text</c>, and a line a merge carried over from
/// another item ends with <c> | from #wiN</c>. Lines are only ever appended; a line that does not parse is skipped when reading and left alone when writing.
/// </summary>
internal static class EventLog
{
    internal const string FileName = "events.log";
    private const string Separator = " | ";

    internal static string PathIn(string dir) => Path.Combine(dir, FileName);

    internal static IReadOnlyList<WorkItemEvent> Read(string dir) =>
        Lines(OpenSpecStoreApi.ReadText(PathIn(dir))).Select(Parse).OfType<WorkItemEvent>().ToList();

    /// <summary>The time of the newest line of the log, nothing else kept (the board's "updated"); null when there is no log or no line parses.</summary>
    internal static DateTime? Newest(string dir) =>
        OpenSpecStoreApi.ReadUnrecorded(PathIn(dir)) is string text ? Lines(text).Select(Parse).OfType<WorkItemEvent>().Select(e => (DateTime?)e.At).Max() : null;

    internal static string Format(DateTime at, string by, string kind, string text) =>
        string.Join(Separator, at.ToString("s", CultureInfo.InvariantCulture), OneLine(by, true), OneLine(kind, true), Escape(text));

    /// <summary>Appends the events (all stamped now, by <paramref name="by"/>) in one write. Never throws: a log that cannot be written is a message, not a failure of what happened.</summary>
    internal static (bool Success, string Message) Append(string dir, string by, IEnumerable<(string Kind, string Text)> events)
    {
        string[] lines = events.Select(e => Format(DateTime.Now, by, e.Kind, e.Text)).ToArray();
        if (lines.Length == 0) return (true, "");
        return Write(dir, text => text + string.Concat(lines.Select(l => l + "\n")));
    }

    /// <summary>Puts raw lines in front of what the log holds (a merged item's own created stays last).</summary>
    internal static (bool Success, string Message) Prepend(string dir, IEnumerable<string> lines)
    {
        string[] add = lines.ToArray();
        return add.Length == 0 ? (true, "") : Write(dir, text => string.Concat(add.Select(l => l + "\n")) + text);
    }

    /// <summary>The raw lines of several logs together, oldest first (unreadable ones first, in their order, since they have no time); a merge carries these over, those of a source that is not the main item marked as from it (see <see cref="FromSource"/>).</summary>
    internal static IEnumerable<string> SortedLines(IEnumerable<(string Dir, int? From)> logs) =>
        logs.SelectMany(l => Lines(OpenSpecStoreApi.ReadText(PathIn(l.Dir))).Select(line => l.From is int id ? FromSource(line, id) : line)).OrderBy(l => Parse(l)?.At ?? DateTime.MinValue);

    private static (bool Success, string Message) Write(string dir, Func<string, string> edit)
    {
        try
        {
            if (!Directory.Exists(dir)) return (false, "That work item no longer exists");
            // read again under the folder's lock and append to what is there now: another writer's event is never overwritten and this one is never refused as stale
            return OpenSpecStoreApi.UpdateText(PathIn(dir), text => edit(text.Length > 0 && !text.EndsWith('\n') ? text + "\n" : text));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return (false, e.Message); }
    }

    private static IEnumerable<string> Lines(string text) =>
        text.Split('\n').Select(l => l.TrimEnd('\r')).Where(l => l.Length > 0);

    private static WorkItemEvent? Parse(string line)
    {
        string[] parts = line.Split(Separator, 4);
        if (parts.Length < 4 || parts[2].Length == 0) return null;
        string text = Unescape(parts[3]);
        int? from = null;
        if (FromMarker.Match(text) is { Success: true } m) { from = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture); text = text[..m.Index]; }
        return DateTime.TryParseExact(parts[0], "s", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime at) ? new WorkItemEvent(at, parts[1], parts[2], text, from) : null;
    }

    private static readonly Regex FromMarker = new(@" \| from #wi(\d+)$");

    /// <summary>The line as a merge carries it into another item: marked as coming from <paramref name="id"/>, unless it already says where it came from (a second merge keeps the first source).</summary>
    internal static string FromSource(string line, int id) => FromMarker.IsMatch(line) ? line : line + Separator + "from #wi" + id;

    /// <summary>A text keeps its line breaks on one line: a backslash is written as two and a break as backslash-n (read back by <see cref="Unescape"/>), so the log stays one event per line.</summary>
    private static string Escape(string text) => text.Replace("\\", "\\\\").Replace("\r\n", "\n").Replace('\r', '\n').Replace("\n", "\\n");

    private static string Unescape(string text) => text.Contains('\\') ? EscapeSequence.Replace(text, m => m.Value == "\\n" ? "\n" : "\\") : text;

    private static readonly Regex EscapeSequence = new(@"\\[\\n]");

    /// <summary>Newlines become spaces; a name or kind can't hold the separator.</summary>
    private static string OneLine(string value, bool field) =>
        (field ? value.Replace(Separator, " / ") : value).Replace("\r\n", " ").Replace('\n', ' ').Replace('\r', ' ');
}
