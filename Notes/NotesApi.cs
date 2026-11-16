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
    /// <summary>
    /// Settles note ids used twice (two branches or computers each made a note offline, then merged): the note created first keeps its id, every other
    /// one is renamed to the next free id with its frontmatter and its own #nN references rewritten, and a <c>renumbered</c> event is recorded on its work item.
    /// Notes are repaired whenever they are read; this reads them now and returns the renumberings made since the last call (empty: nothing to do, nothing written).
    /// </summary>
    public static IReadOnlyList<(int OldId, int NewId, string Path)> RepairDuplicateIds() => Queries.RepairDuplicateIds();
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

    /// <summary>Deletes the note's file, and the attachment files copied for it unless another note lists them too.</summary>
    public static (bool Success, string Message) Delete(int id) => Conversions.Delete(id);
    /// <summary>Makes the note a decision, a bug or an ordinary note (null) like <see cref="SetKind"/>, and records a <c>converted</c> event (nothing when the kind is already that).</summary>
    public static (bool Success, string Message) ConvertKind(int id, string? kind) => Conversions.ConvertKind(id, kind);
    /// <summary>
    /// Adds a subtask to the note's work item from the note's title (when the title is still the default, "New note" or "Decision": the first line of its text),
    /// and records a <c>converted</c> event; the note stays exactly as it is. Number is the new subtask's. A note with neither, or one already promoted
    /// (see <see cref="PromotedNotes"/>), is refused.
    /// </summary>
    public static (bool Success, string Message, string Number) ConvertToSubtask(int id) => Conversions.ConvertToSubtask(id);
    /// <summary>
    /// Makes a note (a decision for <see cref="NoteKinds.Decision"/>) of the work item's subtask: titled with its text, dated now, by <paramref name="by"/>;
    /// the subtask stays, and a <c>converted</c> event is recorded. Id is the new note's.
    /// </summary>
    public static (bool Success, string Message, int Id) ConvertSubtask(int workItemId, string number, string? kind, string by) => Conversions.ConvertSubtask(workItemId, number, kind, by);
    /// <summary>A subtask line made from a note ends in its <c>#nN</c>: the text without it, and the note's id (null when the line has no such ending).</summary>
    public static (string Text, int? Note) SubtaskReference(string subtaskText) => Conversions.SubtaskReference(subtaskText);
    /// <summary>The notes of the work item that were promoted to a subtask which still exists (a subtask line ending in the note's <c>#nN</c>, or a <c>converted</c> event naming it).</summary>
    public static IReadOnlyList<int> PromotedNotes(int workItemId) => Conversions.PromotedNotes(workItemId);
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

    /// <summary>The absolute folder the repository's attachments go to right now (what <see cref="SetAttachmentsDir"/> last set, else local/task-tracking under the repository).</summary>
    public static string AttachmentsRoot(string repoRoot) => Commands.AttachmentsRoot(repoRoot);

    /// <summary>
    /// Whether a folder may be the attachments folder: Dir is it tidied (forward slashes, no trailing slash; empty gives local/task-tracking), Message says why not.
    /// Refused: outside the repository (rooted, a drive, "..") , the repository itself, inside openspec/, names a folder cannot have.
    /// </summary>
    public static (bool Ok, string Dir, string Message) CheckAttachmentsDir(string? dir) => AttachmentsFolder.Check(dir);

    /// <summary>How many files the repository-relative folder holds, below it too, not counting what a link (junction, symlink) leads to; 0 when it does not exist or is not allowed.</summary>
    public static int CountAttachmentFiles(string repoRoot, string dir) => AttachmentsFolder.Count(repoRoot, dir);

    /// <summary>
    /// Moves every file under <paramref name="fromDir"/> to the same place under <paramref name="toDir"/> (both repository-relative), then points the notes' attachments entries
    /// and the link targets in note bodies and descriptions (open and archived items) to the new place. Never overwrites: a file with the same bytes is dropped, another with the
    /// same name is kept beside it as name-2. Links (junctions, symlinks) are not followed. Refused, changing nothing, when the folders are the same or one holds the other, when a
    /// folder is not allowed (<see cref="CheckAttachmentsDir"/>) or is, or lies below, a link. A missing <paramref name="fromDir"/> is a success with nothing done. Safe to run
    /// again after an interruption. Records no worklog event. Run it with the repository current (the app calls it inside <c>Workspace.In</c>); the caller then sets the new folder
    /// (<see cref="SetAttachmentsDir"/>). Git-ignores <paramref name="toDir"/> like attaching does.
    /// </summary>
    public static AttachmentsMoveResult MoveAttachments(string repoRoot, string fromDir, string toDir) => AttachmentsFolder.Move(repoRoot, fromDir, toDir);

    /// <summary>Whether a folder may be a project's files folder (images and plans go inside it): like <see cref="CheckAttachmentsDir"/>, but empty gives the default, <c>attachments</c>.</summary>
    public static (bool Ok, string Dir, string Message) CheckFilesDir(string? dir) => AttachmentsFolder.CheckFiles(dir);

    /// <summary>The project's files folder as openspec/config.yaml names it (<c>x-rtm.filesDir</c>, tidied), or null when the project has none (the old layout) or the value is not allowed.</summary>
    public static string? ProjectFilesDir(string repoRoot) => AttachmentsFolder.ProjectFilesDir(repoRoot);

    /// <summary>The plans (*.md files) and all the files (the drawings beside them too) below a repository-relative plans folder, not following links; (0, 0) when there is none.</summary>
    public static (int Plans, int Files) CountPlanFiles(string repoRoot, string dir) => AttachmentsFolder.CountPlans(repoRoot, dir);

    /// <summary>
    /// Moves a folder of plans like <see cref="MoveAttachments"/> moves the attachments (same rules: nothing overwritten, a file with the same bytes dropped, another kept as name-2,
    /// links not followed, refused for a folder that is or lies below a link, safe to repeat), and rewrites the references: every work item's plan (and linked documents), open or archived,
    /// and the <c>[[doc]]</c> links and markdown link targets in note bodies and descriptions that name the old folder. Links inside a moved plan are not touched (what it links to beside
    /// it moved with it). Nothing is git-ignored and git is not run: the files are moved as plain files.
    /// </summary>
    public static AttachmentsMoveResult MovePlans(string repoRoot, string fromDir, string toDir) => AttachmentsFolder.Move(repoRoot, fromDir, toDir, plans: true);

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
