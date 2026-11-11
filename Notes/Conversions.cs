using System.Text.RegularExpressions;
using Notes.PublicDTOs;
using OpenSpecStore;
using WorkItems;
using WorkItems.PublicDTOs;

namespace Notes;

/// <summary>
/// Entries of the worklog turned into one another: note, decision and bug into one another, a note to a subtask, a subtask to a note. Nothing is destroyed:
/// a note promoted to a subtask stays as it is, a subtask made a note stays too. Each conversion records one <c>converted</c> event on the work item
/// (<c>note #n12 -> subtask 2.3</c>).
/// </summary>
internal static partial class Conversions
{
    private const string NotFound = "That note no longer exists";
    private const int MaxSubtaskText = 200;

    [GeneratedRegex(@"^(#+|[-*+]|>|\[[ xX]\])\s*")]
    private static partial Regex MarkdownLead();

    /// <summary>Deletes the note's file, and the files copied for it under the attachments folder unless another note lists them too.</summary>
    internal static (bool Success, string Message) Delete(int id)
    {
        if (NotesApi.Get(id) is not Note note) return (false, NotFound);
        string? root = Commands.RootOf(note);
        HashSet<string> shared = [.. NotesApi.GetAll().Where(n => n.Id != id).SelectMany(n => n.Attachments)];
        (bool ok, string message) = OpenSpecStoreApi.DeleteFile(note.Path);
        if (!ok || root is null) return (ok, message);
        Regex own = new($@"^[^:]+/wi{note.WorkItemId}/n{id}/[^/]+$");   // a copy made for this note: <any attachments folder>/wi<item>/n<note>/<name>
        foreach (string entry in note.Attachments.Where(a => !a.Contains("..") && !a.StartsWith('/') && own.IsMatch(a) && !shared.Contains(a)))
        {
            string file = Path.Combine(root, entry.Replace('/', Path.DirectorySeparatorChar));
            try
            {
                File.Delete(file);
                if (Path.GetDirectoryName(file) is string dir && Directory.Exists(dir) && !Directory.EnumerateFileSystemEntries(dir).Any()) Directory.Delete(dir);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        }
        return (true, "");
    }

    internal static (bool Success, string Message) ConvertKind(int id, string? kind)
    {
        if (NotesApi.Get(id) is not Note note) return (false, NotFound);
        if (note.Kind == kind) return (true, "");
        (bool ok, string message) = Commands.SetKind(id, kind);
        if (ok) WorkItemsApi.LogEvent(note.WorkItemId, "converted", $"{Noun(note.Kind)} #n{id} -> {Noun(kind)}");
        return (ok, message);
    }

    private static string Noun(string? kind) => kind switch { NoteKinds.Decision => "decision", NoteKinds.Bug => "bug", _ => "note" };

    [GeneratedRegex(@"^(?:note|decision|bug) #n(?<id>\d+) -> subtask (?<number>\S+)$")]
    private static partial Regex PromotedEvent();

    /// <summary>The notes of the work item that were promoted to a subtask which still exists: the last <c>converted</c> event of each says which.</summary>
    internal static IReadOnlyList<int> PromotedNotes(int workItemId)
    {
        if (WorkItemsApi.Get(workItemId) is not WorkItem item) return [];
        HashSet<string> numbers = [.. item.Subtasks.Select(s => s.Number)];
        Dictionary<int, string> last = [];
        foreach (WorkItemEvent e in WorkItemsApi.GetEvents(workItemId))
            if (e.Kind == "converted" && PromotedEvent().Match(e.Text) is { Success: true } m) last[int.Parse(m.Groups["id"].Value)] = m.Groups["number"].Value;
        return [.. last.Where(p => numbers.Contains(p.Value)).Select(p => p.Key).Order()];
    }

    /// <summary>The subtask's text: the note's title, or when that is still the default the first line of its text without its markdown lead.</summary>
    private static string? SubtaskText(Note note)
    {
        string title = note.Title.Trim();
        if (title.Length > 0 && !title.Equals("New note", StringComparison.OrdinalIgnoreCase) && !title.Equals("Decision", StringComparison.OrdinalIgnoreCase) && !title.Equals("Bug", StringComparison.OrdinalIgnoreCase)) return title;
        foreach (string raw in note.Body.Split('\n'))
        {
            string line = raw.Trim();
            for (string next = MarkdownLead().Replace(line, "").Trim(); next != line; next = MarkdownLead().Replace(line, "").Trim()) line = next;
            if (line.Length > 0) return line.Length > MaxSubtaskText ? line[..MaxSubtaskText].TrimEnd() : line;
        }
        return null;
    }

    internal static (bool Success, string Message, string Number) ConvertToSubtask(int id)
    {
        if (NotesApi.Get(id) is not Note note) return (false, NotFound, "");
        if (PromotedNotes(note.WorkItemId).Contains(id)) return (false, "That note is already a subtask", "");
        if (SubtaskText(note) is not string text) return (false, "Write something in the note first: its title or first line becomes the subtask", "");
        HashSet<string> had = [.. WorkItemsApi.Get(note.WorkItemId)?.Subtasks.Select(s => s.Number) ?? []];
        (bool ok, string message) = WorkItemsApi.AddSubtask(note.WorkItemId, text);
        if (!ok) return (false, message, "");
        string number = WorkItemsApi.Get(note.WorkItemId)?.Subtasks.LastOrDefault(s => !had.Contains(s.Number))?.Number ?? "";
        WorkItemsApi.LogEvent(note.WorkItemId, "converted", $"{Noun(note.Kind)} #n{id} -> subtask {number}");
        return (true, "", number);
    }

    internal static (bool Success, string Message, int Id) ConvertSubtask(int workItemId, string number, string? kind, string by)
    {
        if (WorkItemsApi.Get(workItemId) is not WorkItem item) return (false, "That work item no longer exists", 0);
        if (item.Subtasks.FirstOrDefault(s => s.Number == number && !s.Number.StartsWith('#')) is not Subtask subtask) return (false, "That subtask no longer exists", 0);
        (bool ok, string message, int id) = Commands.Create(workItemId, by, DateTime.Now, kind);
        if (!ok) return (false, message, 0);
        (ok, message) = Commands.SetTitle(id, subtask.Text);
        if (!ok) { Delete(id); return (false, message, 0); }   // no half a conversion: the new note goes
        WorkItemsApi.LogEvent(workItemId, "converted", $"subtask {number} -> {Noun(kind)} #n{id}");
        return (true, "", id);
    }
}
