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

    /// <summary>The user has looked at what an agent wrote here: reviewedAt = now. Text and the agent's stamp stay.</summary>
    internal static (bool Success, string Message) MarkReviewed(int id)
    {
        if (NotesApi.Get(id) is not Note note) return (false, NotFound);
        (string front, string body) = NoteFile.Split(OpenSpecStoreApi.ReadText(note.Path));
        NoteMeta meta = NoteFile.Meta(front);
        meta.Id = id;
        DateTime at = DateTime.Now;   // after what it reviews, even when the agent's clock was ahead
        if (DateTime.TryParse(meta.AgentAt, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime a) && a >= at) at = a.AddSeconds(1);
        meta.ReviewedAt = at.ToString("s", CultureInfo.InvariantCulture);
        return OpenSpecStoreApi.WriteText(note.Path, NoteFile.Compose(meta, body));
    }

    private const string AttachRoot = "local/task-tracking/";

    /// <summary>Copies the file to local/task-tracking/wi&lt;item&gt;/n&lt;note&gt;/ (never overwriting), makes sure local/ is git-ignored, and lists it.</summary>
    internal static (bool Success, string Message) AttachFile(int id, string sourcePath)
    {
        if (!File.Exists(sourcePath)) return (false, NotesApi.Get(id) is null ? NotFound : "That file no longer exists");
        return Store(id, Path.GetFileName(sourcePath), target => File.Copy(sourcePath, target));
    }

    /// <summary>Stores pasted data (a clipboard image) like a picked file: same folder, same naming, same .gitignore handling.</summary>
    internal static (bool Success, string Message) AttachBytes(int id, string fileName, byte[] bytes) =>
        Store(id, Path.GetFileName(fileName ?? ""), target => File.WriteAllBytes(target, bytes));

    /// <summary>Titles the note: rewrites its first "# " heading, or puts one on top when the body has none.</summary>
    internal static (bool Success, string Message) SetTitle(int id, string title)
    {
        title = string.Join(' ', (title ?? "").Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)).Trim();
        if (title.Length == 0) return (false, "A note needs a title");
        if (NotesApi.Get(id) is not Note note) return (false, NotFound);
        List<string> lines = [.. note.Body.Replace("\r\n", "\n").Split('\n')];
        int at = lines.FindIndex(l => l.StartsWith("# "));
        if (at >= 0) lines[at] = "# " + title;
        else { lines.Insert(0, ""); lines.Insert(0, "# " + title); }
        return SaveBody(id, string.Join('\n', lines));
    }

    private static (bool Success, string Message) Store(int id, string name, Action<string> write)
    {
        if (NotesApi.Get(id) is not Note note) return (false, NotFound);
        if (OpenSpecStoreApi.RepoRoot is not string root) return (false, "No repository is open");
        if (name.Length == 0) return (false, "That file has no name");
        string relDir = $"{AttachRoot}wi{note.WorkItemId}/n{id}";
        string dir = Path.Combine(root, relDir.Replace('/', Path.DirectorySeparatorChar));
        string stem = Path.GetFileNameWithoutExtension(name), ext = Path.GetExtension(name);
        try
        {
            Directory.CreateDirectory(dir);
            string target = Path.Combine(dir, name);
            for (int n = 2; File.Exists(target); n++) target = Path.Combine(dir, $"{stem}-{n}{ext}");
            write(target);
            IgnoreLocal(root);
            return Append(note, $"{relDir}/{Path.GetFileName(target)}");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return (false, e.Message); }
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
        meta.Id = id;
        if (meta.Attachments?.Remove(entry) != true) return (false, "That attachment is already gone");
        (bool ok, string message) = OpenSpecStoreApi.WriteText(note.Path, NoteFile.Compose(meta, body));
        if (ok && entry.StartsWith(AttachRoot, StringComparison.Ordinal) && !entry.Contains("..") && OpenSpecStoreApi.RepoRoot is string root)
            try { File.Delete(Path.Combine(root, entry.Replace('/', Path.DirectorySeparatorChar))); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        return (ok, message);
    }

    private static (bool Success, string Message) Append(Note note, string entry)
    {
        (string front, string body) = NoteFile.Split(OpenSpecStoreApi.ReadText(note.Path));
        NoteMeta meta = NoteFile.Meta(front);
        meta.Id = note.Id;
        (meta.Attachments ??= []).Add(entry);
        return OpenSpecStoreApi.WriteText(note.Path, NoteFile.Compose(meta, body));
    }

    /// <summary>Appends "local/" to the repository's .gitignore unless a line already says so.</summary>
    private static void IgnoreLocal(string root)
    {
        string file = Path.Combine(root, ".gitignore");
        string text = File.Exists(file) ? File.ReadAllText(file) : "";
        if (text.Replace("\r", "").Split('\n').Any(l => l.Trim() is "local/" or "/local/" or "local" or "/local")) return;
        string nl = text.Contains("\r\n") ? "\r\n" : "\n";
        string lead = text.Length == 0 || text.EndsWith('\n') ? "" : nl;
        File.AppendAllText(file, $"{lead}# Repo Tasks: attachments and other per-user files{nl}local/{nl}");
    }

    internal static (bool Success, string Message) SetLinkedCommits(int id, IEnumerable<string> hashes)
    {
        if (NotesApi.Get(id) is not Note note) return (false, NotFound);
        (string front, string body) = NoteFile.Split(OpenSpecStoreApi.ReadText(note.Path));
        NoteMeta meta = NoteFile.Meta(front);
        meta.Id = id;
        meta.Commits = hashes.Distinct().ToList();
        return OpenSpecStoreApi.WriteText(note.Path, NoteFile.Compose(meta, body));
    }
}
