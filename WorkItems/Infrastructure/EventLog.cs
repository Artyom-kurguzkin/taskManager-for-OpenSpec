using System.Globalization;
using OpenSpecStore;
using WorkItems.PublicDTOs;

namespace WorkItems.Infrastructure;

/// <summary>
/// events.log in a change folder: one line per event, <c>yyyy-MM-ddTHH:mm:ss | who | kind | text</c>. Lines are only ever appended; a
/// line that does not parse is skipped when reading and left alone when writing.
/// </summary>
internal static class EventLog
{
    internal const string FileName = "events.log";
    private const string Separator = " | ";

    internal static string PathIn(string dir) => Path.Combine(dir, FileName);

    internal static IReadOnlyList<WorkItemEvent> Read(string dir) =>
        Lines(OpenSpecStoreApi.ReadText(PathIn(dir))).Select(Parse).OfType<WorkItemEvent>().ToList();

    internal static string Format(DateTime at, string by, string kind, string text) =>
        string.Join(Separator, at.ToString("s", CultureInfo.InvariantCulture), OneLine(by, true), OneLine(kind, true), OneLine(text, false));

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

    /// <summary>The raw lines of several logs together, oldest first (unreadable ones first, in their order, since they have no time); a merge carries these over.</summary>
    internal static IEnumerable<string> SortedLines(IEnumerable<string> dirs) =>
        dirs.SelectMany(d => Lines(OpenSpecStoreApi.ReadText(PathIn(d)))).OrderBy(l => Parse(l)?.At ?? DateTime.MinValue);

    private static (bool Success, string Message) Write(string dir, Func<string, string> edit)
    {
        try
        {
            if (!Directory.Exists(dir)) return (false, "That work item no longer exists");
            string file = PathIn(dir);
            string text = OpenSpecStoreApi.ReadText(file);
            if (text.Length > 0 && !text.EndsWith('\n')) text += "\n";
            return OpenSpecStoreApi.WriteText(file, edit(text));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return (false, e.Message); }
    }

    private static IEnumerable<string> Lines(string text) =>
        text.Split('\n').Select(l => l.TrimEnd('\r')).Where(l => l.Length > 0);

    private static WorkItemEvent? Parse(string line)
    {
        string[] parts = line.Split(Separator, 4);
        if (parts.Length < 4 || parts[2].Length == 0) return null;
        return DateTime.TryParseExact(parts[0], "s", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime at) ? new WorkItemEvent(at, parts[1], parts[2], parts[3]) : null;
    }

    /// <summary>Newlines become spaces; a name or kind can't hold the separator.</summary>
    private static string OneLine(string value, bool field) =>
        (field ? value.Replace(Separator, " / ") : value).Replace("\r\n", " ").Replace('\n', ' ').Replace('\r', ' ');
}
