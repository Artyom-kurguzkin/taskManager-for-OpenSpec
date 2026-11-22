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

    internal static (bool Success, string Message, int Id) Create(int workItemId, string by, DateTime created, string? kind = null)
    {
        if (WorkItemsApi.Get(workItemId) is not WorkItem wi) return (false, "That work item no longer exists", 0);
        if (kind is not null && !NoteKinds.All.Contains(kind)) return (false, $"Unknown note kind '{kind}'; use {string.Join(", ", NoteKinds.All)} or none", 0);
        int id = OpenSpecStoreApi.Allocate("note", roots => roots.Max(r => Queries.MaxNoteIdUnder(r)));
        NoteMeta meta = new() { Id = id, By = by, Created = created.ToString("s", CultureInfo.InvariantCulture), Title = kind == NoteKinds.Decision ? "Decision" : kind == NoteKinds.Bug ? "Bug" : "New note", Kind = kind };
        string file = Path.Combine(wi.Path, "notes", $"n{id}.md");
        (bool ok, string message) = OpenSpecStoreApi.WriteText(file, NoteFile.Compose(meta, ""));
        return ok ? (true, "", id) : (false, message, 0);
    }

    /// <summary>Replaces the body; the frontmatter is kept exactly as it is on disk.</summary>
    internal static (bool Success, string Message) SaveBody(int id, string body)
    {
        if (NotesApi.Get(id) is not Note note) return (false, NotFound);
        (string front, string _) = NoteFile.Split(OpenSpecStoreApi.ReadText(note.Path));
        return OpenSpecStoreApi.WriteText(note.Path, front + body.Replace("\r\n", "\n"));
    }

    private const string Promoted = "(created subtask)";
    private static readonly System.Text.RegularExpressions.Regex Unticked = new(@"^\s*[-*]\s+\[ \]\s+(?<text>\S.*?)\s*$", System.Text.RegularExpressions.RegexOptions.Compiled);

    /// <summary>
    /// Every unticked checkbox line of the note (outside code fences, not yet marked) becomes a subtask of its work item, and the line stays in the
    /// note with " (created subtask)" after it. The note is written once, and only when something was made.
    /// </summary>
    internal static (bool Success, string Message, IReadOnlyList<string> Created) PromoteSubtasks(int id) =>
        NotesApi.Get(id) is Note note ? PromoteSubtasks(note) : (false, NotFound, []);

    /// <summary>
    /// What the editor saves, in one command: the text and the title (each null when unchanged) in one write, then the checkboxes of the text become subtasks.
    /// The note is read once, not again before every step; the later steps work on what was just written.
    /// </summary>
    internal static (bool Success, string Message, IReadOnlyList<string> Created) SaveEdit(int id, string? body, string? title)
    {
        if (NotesApi.Get(id) is not Note note) return (false, NotFound, []);
        if (body is not null || title is not null)
        {
            title = title is null ? null : string.Join(' ', title.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)).Trim();
            if (title is { Length: 0 }) return (false, "A note needs a title", []);
            (string front, string old) = NoteFile.Split(OpenSpecStoreApi.ReadText(note.Path));
            string text = body?.Replace("\r\n", "\n") ?? old;
            string whole = front + text;
            if (title is not null)
            {
                NoteMeta meta = NoteFile.Meta(front);
                if (meta.Problem is not null) return (false, $"This note's {meta.Problem}; fix the file and try again", []);   // never written over
                (meta.Id, meta.Title) = (id, title);
                whole = NoteFile.Compose(meta, text);
            }
            (bool ok, string message) = OpenSpecStoreApi.WriteText(note.Path, whole);
            if (!ok) return (false, message, []);
            note = note with { Body = text };
        }
        return PromoteSubtasks(note);
    }

    private static (bool Success, string Message, IReadOnlyList<string> Created) PromoteSubtasks(Note note)
    {
        int id = note.Id;
        string[] lines = note.Body.Split('\n');
        List<string> created = [];
        string failure = "";
        bool fenced = false, rewritten = false;
        for (int i = 0; i < lines.Length && failure.Length == 0; i++)
        {
            string trimmed = lines[i].TrimStart();
            if (trimmed.StartsWith("```") || trimmed.StartsWith("~~~")) { fenced = !fenced; continue; }
            if (fenced) continue;
            if (Unticked.Match(lines[i].TrimEnd('\r')) is not { Success: true } m) continue;
            if (m.Groups["text"].Value.Contains(Promoted, StringComparison.Ordinal))
            {
                // already promoted, whatever was typed around the marker since: retext its subtask, keep one marker at the end
                string tidy = TidyPromotedLine(note.WorkItemId, lines[i].TrimEnd('\r'), m);
                if (tidy != lines[i].TrimEnd('\r')) { lines[i] = tidy + (lines[i].EndsWith('\r') ? "\r" : ""); rewritten = true; }
                continue;
            }
            HashSet<string> had = [.. WorkItemsApi.Get(note.WorkItemId)?.Subtasks.Select(s => s.Number) ?? []];
            (bool ok, string message) = WorkItemsApi.AddSubtask(note.WorkItemId, m.Groups["text"].Value);
            if (!ok) { failure = message; break; }
            if (WorkItemsApi.Get(note.WorkItemId)?.Subtasks.LastOrDefault(s => !had.Contains(s.Number)) is Subtask added) created.Add(added.Number);
            lines[i] = lines[i].TrimEnd() + " " + Promoted + (lines[i].EndsWith('\r') ? "\r" : "");
        }
        if (created.Count > 0 || rewritten)
        {
            (bool saved, string message) = SaveBody(id, string.Join('\n', lines));
            if (!saved) return (false, message, created);
        }
        return failure.Length == 0 ? (true, "", created) : (false, failure, created);
    }

    /// <summary>
    /// A line that carries the marker was promoted before. The text before the first marker is what the subtask was made from: when exactly one
    /// subtask has that text, it takes the line's text with every marker removed. The line comes back with the new text and one marker at its end.
    /// The marker never reaches a subtask. A line with nothing but markers is left as it is.
    /// </summary>
    private static string TidyPromotedLine(int workItemId, string line, System.Text.RegularExpressions.Match m)
    {
        string text = m.Groups["text"].Value;
        string original = text[..text.IndexOf(Promoted, StringComparison.Ordinal)].Trim();
        string now = System.Text.RegularExpressions.Regex.Replace(text.Replace(Promoted, " "), @"\s+", " ").Trim();
        if (now.Length == 0) return line;
        if (now != original && original.Length > 0 &&
            WorkItemsApi.Get(workItemId)?.Subtasks.Where(s => !s.Number.StartsWith('#') && s.Text.Trim() == original).ToList() is [Subtask only])
            WorkItemsApi.SetSubtaskText(workItemId, only.Number, now);
        return line[..m.Groups["text"].Index] + now + " " + Promoted;
    }

    internal static (bool Success, string Message) AddManualLink(int id, string link)
    {
        if (string.IsNullOrWhiteSpace(link)) return (false, "Type the link first");
        if (NotesApi.Get(id) is not Note note) return (false, NotFound);
        (string front, string body) = NoteFile.Split(OpenSpecStoreApi.ReadText(note.Path));
        NoteMeta meta = NoteFile.Meta(front);
        if (meta.Problem is not null) return (false, $"This note's {meta.Problem}; fix the file and try again");   // never written over
        meta.Id = id;
        (meta.Links ??= []).Add(link.Trim());
        return OpenSpecStoreApi.WriteText(note.Path, NoteFile.Compose(meta, body));
    }

    /// <summary>The user has looked at what an agent wrote here: reviewedAt = now. Text and the agent's stamp stay.</summary>
    internal static (bool Success, string Message) MarkReviewed(int id)
    {
        if (NotesApi.Get(id) is not Note note) return (false, NotFound);
        (string front, string body) = NoteFile.Split(OpenSpecStoreApi.ReadText(note.Path));
        NoteMeta meta = NoteFile.Meta(front);
        if (meta.Problem is not null) return (false, $"This note's {meta.Problem}; fix the file and try again");   // never written over
        meta.Id = id;
        DateTime at = DateTime.Now;   // after what it reviews, even when the agent's clock was ahead
        if (DateTime.TryParse(meta.AgentAt, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime a) && a >= at) at = a.AddSeconds(1);
        meta.ReviewedAt = at.ToString("s", CultureInfo.InvariantCulture);
        return OpenSpecStoreApi.WriteText(note.Path, NoteFile.Compose(meta, body));
    }

    internal const string DefaultAttachmentsDir = OpenSpecStoreApi.DefaultAttachmentsDir;
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, string> AttachDirs = new(StringComparer.OrdinalIgnoreCase);

    private static string DirKey(string root) => Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

    /// <summary>Where this repository's note attachments go (relative, forward slashes). A folder that is not allowed (see <see cref="AttachmentsFolder.Check"/>) is refused: the default is used.</summary>
    internal static void SetAttachmentsDir(string repoRoot, string? dir)
    {
        (bool ok, string clean, string _) = AttachmentsFolder.Check(dir);
        if (!ok || clean == DefaultAttachmentsDir) AttachDirs.TryRemove(DirKey(repoRoot), out _);
        else AttachDirs[DirKey(repoRoot)] = clean;
    }

    private static string AttachmentsDir(string root) => AttachDirs.GetValueOrDefault(DirKey(root), DefaultAttachmentsDir);

    /// <summary>The absolute folder this repository's attachments go to right now.</summary>
    internal static string AttachmentsRoot(string repoRoot) => Path.GetFullPath(Path.Combine(repoRoot, AttachmentsDir(repoRoot).Replace('/', Path.DirectorySeparatorChar)));

    /// <summary>The open repository the note lives in: by its own path, so a switch of the active project meanwhile cannot send its files elsewhere.</summary>
    internal static string? RootOf(Note note) =>
        OpenSpecStoreApi.OpenRoots.FirstOrDefault(r => Path.GetFullPath(note.Path).StartsWith(DirKey(r) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) ?? OpenSpecStoreApi.RepoRoot;

    /// <summary>Copies the file to local/task-tracking/wi&lt;item&gt;/n&lt;note&gt;/ (never overwriting), makes sure local/ is git-ignored, and lists it.</summary>
    internal static (bool Success, string Message) AttachFile(int id, string sourcePath)
    {
        if (!File.Exists(sourcePath)) return (false, NotesApi.Get(id) is null ? NotFound : "That file no longer exists");
        return Store(id, Path.GetFileName(sourcePath), target => File.Copy(sourcePath, target));
    }

    /// <summary>Stores pasted data (a clipboard image) like a picked file: same folder, same naming, same .gitignore handling.</summary>
    internal static (bool Success, string Message) AttachBytes(int id, string fileName, byte[] bytes) =>
        Store(id, Path.GetFileName(fileName ?? ""), target => File.WriteAllBytes(target, bytes));

    /// <summary>Makes the note a decision (<see cref="NoteKinds.Decision"/>) or an ordinary note again (null): the frontmatter <c>kind:</c> only.</summary>
    internal static (bool Success, string Message) SetKind(int id, string? kind)
    {
        if (kind is not null && !NoteKinds.All.Contains(kind)) return (false, $"Unknown note kind '{kind}'; use {string.Join(", ", NoteKinds.All)} or none");
        if (NotesApi.Get(id) is not Note note) return (false, NotFound);
        (string front, string body) = NoteFile.Split(OpenSpecStoreApi.ReadText(note.Path));
        NoteMeta meta = NoteFile.Meta(front);
        if (meta.Problem is not null) return (false, $"This note's {meta.Problem}; fix the file and try again");
        meta.Id = id;
        meta.Kind = kind;
        return OpenSpecStoreApi.WriteText(note.Path, NoteFile.Compose(meta, body));
    }

    /// <summary>Titles the note: writes the frontmatter <c>title:</c> only, the body (and any heading in it) is left alone.</summary>
    internal static (bool Success, string Message) SetTitle(int id, string title)
    {
        title = string.Join(' ', (title ?? "").Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)).Trim();
        if (title.Length == 0) return (false, "A note needs a title");
        if (NotesApi.Get(id) is not Note note) return (false, NotFound);
        (string front, string body) = NoteFile.Split(OpenSpecStoreApi.ReadText(note.Path));
        NoteMeta meta = NoteFile.Meta(front);
        if (meta.Problem is not null) return (false, $"This note's {meta.Problem}; fix the file and try again");   // never written over
        meta.Id = id;
        meta.Title = title;
        return OpenSpecStoreApi.WriteText(note.Path, NoteFile.Compose(meta, body));
    }

    internal static (bool Success, string Message) Store(int id, string name, Action<string> write)
    {
        if (NotesApi.Get(id) is not Note note) return (false, NotFound);
        if (RootOf(note) is not string root) return (false, "No repository is open");
        if (name.Length == 0) return (false, "That file has no name");
        try { return Append(note, WriteUnique(root, $"wi{note.WorkItemId}/n{id}", name, write)); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return (false, e.Message); }
    }

    /// <summary>Writes pasted bytes (an image in a work item's description) under the attachments folder in wi&lt;item&gt;/, no note involved. Message is the repository-relative path on success.</summary>
    internal static (bool Success, string Message) AttachItemImage(string repoRoot, int workItemId, string fileName, byte[] bytes)
    {
        string name = Path.GetFileName(fileName ?? "");
        if (name.Length == 0) return (false, "That file has no name");
        try { return (true, WriteUnique(repoRoot, $"wi{workItemId}", name, target => File.WriteAllBytes(target, bytes))); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return (false, e.Message); }
    }

    /// <summary>The one place the attachments folder rules live: &lt;attachments dir&gt;/&lt;sub&gt;/&lt;name&gt;, never overwriting (-2, -3), the folder git-ignored. Returns the repository-relative path.</summary>
    private static string WriteUnique(string root, string sub, string name, Action<string> write)
    {
        string attachDir = AttachmentsDir(root);
        string relDir = $"{attachDir}/{sub}";
        string dir = Path.Combine(root, relDir.Replace('/', Path.DirectorySeparatorChar));
        string stem = Path.GetFileNameWithoutExtension(name), ext = Path.GetExtension(name);
        Directory.CreateDirectory(dir);
        string target = Path.Combine(dir, name);
        for (int n = 2; File.Exists(target); n++) target = Path.Combine(dir, $"{stem}-{n}{ext}");
        write(target);
        IgnoreFolder(root, attachDir);
        return $"{relDir}/{Path.GetFileName(target)}";
    }

    internal static (bool Success, string Message) AttachLink(int id, string url)
    {
        url = url.Trim();
        if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? uri) || uri.Scheme is not ("http" or "https")) return (false, "Type a full link starting with http:// or https://");
        return NotesApi.Get(id) is Note note ? Append(note, url) : (false, NotFound);
    }

    /// <summary>Forgets the entry; a file copied under local/ is deleted too.</summary>
    internal static (bool Success, string Message) RemoveAttachment(int id, string entry)
    {
        if (NotesApi.Get(id) is not Note note) return (false, NotFound);
        (string front, string body) = NoteFile.Split(OpenSpecStoreApi.ReadText(note.Path));
        NoteMeta meta = NoteFile.Meta(front);
        if (meta.Problem is not null) return (false, $"This note's {meta.Problem}; fix the file and try again");   // never written over
        meta.Id = id;
        if (meta.Attachments?.Remove(entry) != true) return (false, "That attachment is already gone");
        (bool ok, string message) = OpenSpecStoreApi.WriteText(note.Path, NoteFile.Compose(meta, body));
        // a file we copied: <any folder>/wi<item>/n<note>/<name>, so one from before the attachments folder was moved goes too
        if (ok && !entry.Contains("..") && !entry.StartsWith('/') && System.Text.RegularExpressions.Regex.IsMatch(entry, $@"^[^:]+/wi{note.WorkItemId}/n{id}/[^/]+$") && RootOf(note) is string root)
            try { File.Delete(Path.Combine(root, entry.Replace('/', Path.DirectorySeparatorChar))); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        return (ok, message);
    }

    private static (bool Success, string Message) Append(Note note, string entry)
    {
        (string front, string body) = NoteFile.Split(OpenSpecStoreApi.ReadText(note.Path));
        NoteMeta meta = NoteFile.Meta(front);
        if (meta.Problem is not null) return (false, $"This note's {meta.Problem}; fix the file and try again");   // never written over
        meta.Id = note.Id;
        (meta.Attachments ??= []).Add(entry);
        (bool ok, string message) = OpenSpecStoreApi.WriteText(note.Path, NoteFile.Compose(meta, body));
        if (ok) WorkItemsApi.LogEvent(note.WorkItemId, "attachment-added", $"n{note.Id}: {(Uri.TryCreate(entry, UriKind.Absolute, out Uri? uri) && uri.Scheme is "http" or "https" ? entry : Path.GetFileName(entry))}");
        return (ok, message);
    }

    /// <summary>Appends "local/" to the repository's .gitignore unless a line already says so.</summary>
    internal static void IgnoreFolder(string root, string attachDir)
    {
        string ignored = attachDir == DefaultAttachmentsDir ? "local" : attachDir;   // the default keeps ignoring all of local/, as it always did
        string file = Path.Combine(root, ".gitignore");
        string text = File.Exists(file) ? File.ReadAllText(file) : "";
        if (text.Replace("\r", "").Split('\n').Select(l => l.Trim().Trim('/')).Any(l => l.Length > 0 && (ignored == l || ignored.StartsWith(l + "/", StringComparison.Ordinal)))) return;
        string nl = text.Contains("\r\n") ? "\r\n" : "\n";
        string lead = text.Length == 0 || text.EndsWith('\n') ? "" : nl;
        File.AppendAllText(file, $"{lead}# Repo Tasks: attachments and other per-user files{nl}{ignored}/{nl}");
    }

    internal static (bool Success, string Message) SetLinkedCommits(int id, IEnumerable<string> hashes)
    {
        if (NotesApi.Get(id) is not Note note) return (false, NotFound);
        (string front, string body) = NoteFile.Split(OpenSpecStoreApi.ReadText(note.Path));
        NoteMeta meta = NoteFile.Meta(front);
        if (meta.Problem is not null) return (false, $"This note's {meta.Problem}; fix the file and try again");   // never written over
        meta.Id = id;
        meta.Commits = hashes.Distinct().ToList();
        return OpenSpecStoreApi.WriteText(note.Path, NoteFile.Compose(meta, body));
    }

    internal static (bool Success, string Message) StampAgent(int id, string agent)
    {
        if (NotesApi.Get(id) is not Note note) return (false, NotFound);
        (string front, string body) = NoteFile.Split(OpenSpecStoreApi.ReadText(note.Path));
        NoteMeta meta = NoteFile.Meta(front);
        if (meta.Problem is not null) return (false, $"This note's {meta.Problem}; fix the file and try again");   // never written over
        meta.Id = id;
        meta.AgentBy = agent;
        meta.AgentAt = DateTime.Now.ToString("s", CultureInfo.InvariantCulture);
        return OpenSpecStoreApi.WriteText(note.Path, NoteFile.Compose(meta, body));
    }
}
