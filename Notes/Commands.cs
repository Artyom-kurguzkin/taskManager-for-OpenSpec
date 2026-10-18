using System.Globalization;
using Notes.BusinessRules;
using Notes.PublicDTOs;
using OpenSpecStore;
using WorkItems;
using WorkItems.PublicDTOs;

namespace Notes;

internal static class Commands
{
    private const string NotFound = "That note no longer exists";

    internal static (bool Success, string Message, int Id) Create(int workItemId, string by)
    {
        if (WorkItemsApi.Get(workItemId) is not WorkItem wi) return (false, "That work item no longer exists", 0);
        int id = Queries.NextId();
        NoteMeta meta = new() { Id = id, By = by, Created = DateTime.Now.ToString("s", CultureInfo.InvariantCulture) };
        string file = Path.Combine(wi.Path, "notes", $"n{id}.md");
        (bool ok, string message) = OpenSpecStoreApi.WriteText(file, NoteFile.Compose(meta, $"# New note\n\n"));
        return ok ? (true, "", id) : (false, message, 0);
    }

    /// <summary>Replaces the body; the frontmatter is kept exactly as it is on disk.</summary>
    internal static (bool Success, string Message) SaveBody(int id, string body)
    {
        if (NotesApi.Get(id) is not Note note) return (false, NotFound);
        (string front, string _) = NoteFile.Split(OpenSpecStoreApi.ReadText(note.Path));
        return OpenSpecStoreApi.WriteText(note.Path, front + body.Replace("\r\n", "\n"));
    }

    internal static (bool Success, string Message) AddManualLink(int id, string link)
    {
        if (string.IsNullOrWhiteSpace(link)) return (false, "Type the link first");
        if (NotesApi.Get(id) is not Note note) return (false, NotFound);
        (string front, string body) = NoteFile.Split(OpenSpecStoreApi.ReadText(note.Path));
        NoteMeta meta = NoteFile.Meta(front);
        meta.Id = id;
        (meta.Links ??= []).Add(link.Trim());
        return OpenSpecStoreApi.WriteText(note.Path, NoteFile.Compose(meta, body));
    }
}
