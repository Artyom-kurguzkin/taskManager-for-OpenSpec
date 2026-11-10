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

    public static (bool Success, string Message, int Id) Create(int workItemId, string by) => Commands.Create(workItemId, by, DateTime.Now);

    /// <summary>Like the two-argument form, but the note's created time (frontmatter <c>created</c>) is <paramref name="created"/>.</summary>
    public static (bool Success, string Message, int Id) Create(int workItemId, string by, DateTime created) => Commands.Create(workItemId, by, created);
    /// <summary>Like the three-argument form, with a kind: <see cref="NoteKinds.Decision"/> for a decision (titled "Decision"), null for an ordinary note.</summary>
    public static (bool Success, string Message, int Id) Create(int workItemId, string by, DateTime created, string? kind) => Commands.Create(workItemId, by, created, kind);
    /// <summary>Makes the note a decision (<see cref="NoteKinds.Decision"/>) or an ordinary note again (null); only the frontmatter <c>kind</c> changes.</summary>
    public static (bool Success, string Message) SetKind(int id, string? kind) => Commands.SetKind(id, kind);
    public static (bool Success, string Message) SaveBody(int id, string body) => Commands.SaveBody(id, body);
    /// <summary>
    /// Turns the note's unticked checkboxes (<c>- [ ] text</c>, outside code blocks) into subtasks of its work item, each once: the line stays in the
    /// note with " (created subtask)" appended. Created lists the new subtask numbers; nothing is written when nothing qualified.
    /// </summary>
    public static (bool Success, string Message, IReadOnlyList<string> Created) PromoteSubtasks(int noteId) => Commands.PromoteSubtasks(noteId);

    public static (bool Success, string Message) AddManualLink(int id, string link) => Commands.AddManualLink(id, link);
    public static (bool Success, string Message) SetLinkedCommits(int id, IEnumerable<string> hashes) => Commands.SetLinkedCommits(id, hashes);

    /// <summary>Copies the file under local/task-tracking/wi&lt;item&gt;/n&lt;note&gt;/, git-ignores local/, and lists it in the note's attachments.</summary>
    public static (bool Success, string Message) AttachFile(int id, string sourcePath) => Commands.AttachFile(id, sourcePath);
    /// <summary>
    /// Sets the repository-relative folder this project's attachments are copied to (the host calls it for every open project; null or empty =
    /// local/task-tracking). Files still go under wi&lt;item&gt;/n&lt;note&gt;/ in it, and the folder is git-ignored.
    /// </summary>
    public static void SetAttachmentsDir(string repoRoot, string? dir) => Commands.SetAttachmentsDir(repoRoot, dir);

    /// <summary>Stores pasted data (e.g. a clipboard image) exactly like <see cref="AttachFile"/>.</summary>
    public static (bool Success, string Message) AttachBytes(int id, string fileName, byte[] bytes) => Commands.AttachBytes(id, fileName, bytes);

    /// <summary>
    /// Saves pasted bytes (an image pasted into a work item's description) as &lt;attachments folder&gt;/wi&lt;item&gt;/&lt;fileName&gt; of that repository, never overwriting
    /// (a -2 suffix), git-ignoring the folder like note attachments. No note is made; Message is the repository-relative path (forward slashes) on success.
    /// </summary>
    public static (bool Success, string Message) AttachItemImage(string repoRoot, int workItemId, string fileName, byte[] bytes) => Commands.AttachItemImage(repoRoot, workItemId, fileName, bytes);

    /// <summary>Rewrites the note's first "# " heading (adds one when the body has none); a blank title is refused.</summary>
    public static (bool Success, string Message) SetTitle(int id, string title) => Commands.SetTitle(id, title);
    public static (bool Success, string Message) AttachLink(int id, string url) => Commands.AttachLink(id, url);
    public static (bool Success, string Message) RemoveAttachment(int id, string entry) => Commands.RemoveAttachment(id, entry);

    /// <summary>Marks an agent's note as reviewed (frontmatter reviewedAt = now).</summary>
    public static (bool Success, string Message) MarkReviewed(int id) => Commands.MarkReviewed(id);

    /// <summary>An agent wrote this note: frontmatter agentBy = <paramref name="agent"/>, agentAt = now.</summary>
    public static (bool Success, string Message) StampAgent(int id, string agent) => Commands.StampAgent(id, agent);
}
