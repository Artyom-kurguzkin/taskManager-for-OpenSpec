using Notes.PublicDTOs;

namespace Notes;

/// <summary>
/// The only public type in this assembly. Notes live next to their work item in the change folder
/// (so they move with it when archived); ids are unique across the whole repository.
/// </summary>
public static class NotesApi
{
    public static IReadOnlyList<Note> GetAll() => Queries.GetAll();
    public static IReadOnlyList<Note> GetForWorkItem(int workItemId) => Queries.GetAll().Where(n => n.WorkItemId == workItemId).ToList();
    public static Note? Get(int id) => Queries.GetAll().FirstOrDefault(n => n.Id == id);

    /// <summary>Finds [[doc]], #wiN and #nN references in the text, in order, each once.</summary>
    public static IReadOnlyList<NoteLink> ResolveLinks(string text) => Queries.ResolveLinks(text);

    public static (bool Success, string Message, int Id) Create(int workItemId, string by) => Commands.Create(workItemId, by);
    public static (bool Success, string Message) SaveBody(int id, string body) => Commands.SaveBody(id, body);
    public static (bool Success, string Message) AddManualLink(int id, string link) => Commands.AddManualLink(id, link);
    public static (bool Success, string Message) SetLinkedCommits(int id, IEnumerable<string> hashes) => Commands.SetLinkedCommits(id, hashes);

    /// <summary>Copies the file under local/task-tracking/wi&lt;item&gt;/n&lt;note&gt;/, git-ignores local/, and lists it in the note's attachments.</summary>
    public static (bool Success, string Message) AttachFile(int id, string sourcePath) => Commands.AttachFile(id, sourcePath);
    public static (bool Success, string Message) AttachLink(int id, string url) => Commands.AttachLink(id, url);
    public static (bool Success, string Message) RemoveAttachment(int id, string entry) => Commands.RemoveAttachment(id, entry);

    /// <summary>Marks an agent's note as reviewed (frontmatter reviewedAt = now).</summary>
    public static (bool Success, string Message) MarkReviewed(int id) => Commands.MarkReviewed(id);
}
