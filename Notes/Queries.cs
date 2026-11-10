using System.Globalization;
using System.Text.RegularExpressions;
using Notes.BusinessRules;
using Notes.PublicDTOs;
using OpenSpecStore;
using WorkItems;
using WorkItems.PublicDTOs;

namespace Notes;

internal static partial class Queries
{
    /// <summary>All notes, parsed once per OpenSpecStore content version. Reading also settles id collisions (see <see cref="Load"/>).</summary>
    internal static IReadOnlyList<Note> GetAll() => Cached().Notes;

    /// <summary>
    /// The notes, assembled once per OpenSpecStore content version. Each change folder's notes/ is read only when its files changed
    /// (name, size, modified time) or the app wrote there itself; the other folders keep their parsed notes and objects.
    /// </summary>
    private static (long Version, IReadOnlyList<Note> Notes, int MaxId) Cached()
    {
        lock (NotesModule.Gate)
        {
            long version = OpenSpecStoreApi.Version;
            string root = OpenSpecStoreApi.RepoRoot ?? "";
            long generation = OpenSpecStoreApi.RepoGeneration;
            if (!NotesModule.Cache.TryGetValue(root, out NotesCache? c) || c.Generation != generation)
                NotesModule.Cache[root] = c = new NotesCache { Generation = generation };
            if (c.Version == version) return (c.Version, c.Notes, c.MaxId);
            (IReadOnlyList<string> touched, long cursor) = OpenSpecStoreApi.ChangesSince(c.Cursor);   // asked before reading: a write that lands meanwhile shows next time
            bool wrote = Load(c, touched);
            if (wrote) version = OpenSpecStoreApi.Version;   // our own renames and rewrites are not a reason to read again
            (c.Version, c.Cursor) = (version, cursor);
            NotesModule.Loads++;
            return (c.Version, c.Notes, c.MaxId);
        }
    }

    internal sealed class Raw(string file, int workItemId, int fileNumber, NoteMeta meta, string body, DateTime created, DateTime modified)
    {
        internal string File = file;
        internal int WorkItemId = workItemId, FileNumber = fileNumber;
        internal NoteMeta Meta = meta;
        internal string Body = body;
        internal DateTime Created = created, Modified = modified;
        internal int Id => FileNumber > 0 ? FileNumber : Meta.Id;
    }

    /// <summary>One change's notes/ folder as last read: what its files looked like, the notes in them and the notes parsed from those.</summary>
    internal sealed class NotesFolder(string fingerprint, List<Raw> raws)
    {
        internal readonly string Fingerprint = fingerprint;
        internal readonly List<Raw> Raws = raws;
        internal List<Note> Notes = [];
    }

    internal sealed class NotesCache
    {
        internal long Generation;
        internal long Version = -1;
        internal long Cursor;
        internal Dictionary<string, NotesFolder> Folders = new(StringComparer.OrdinalIgnoreCase);
        internal IReadOnlyList<Note> Notes = [];
        internal int MaxId;
    }

    /// <summary>A note whose frontmatter did not parse shows its title (from the body) followed by why, so the warning is where the user looks first.</summary>
    private static string WithProblem(string title, string? problem) => problem is null ? title : $"{title} - {problem}";

    private static List<Note> BuildNotes(List<Raw> raws) =>
        [.. raws.Select(r => new Note(r.Id, r.WorkItemId, r.File, WithProblem(NoteFile.Title(r.Meta.Title, r.Body, r.Id), r.Meta.Problem), r.Meta.By ?? "", r.Created, r.Modified, r.Body, r.Meta.Links ?? [], r.Meta.Commits ?? [], r.Meta.Attachments ?? [],
            r.Meta.AgentBy, Time(r.Meta.AgentAt), Time(r.Meta.ReviewedAt), r.Meta.MergedFrom, Time(r.Meta.MergedAt), r.Meta.Kind))];

    /// <summary>True when the app itself wrote, deleted or moved this folder or something in it (the store keeps that record).</summary>
    private static bool Written(string dir, IReadOnlyList<string> touched) =>
        touched.Any(p => p.StartsWith(dir, StringComparison.OrdinalIgnoreCase) && (p.Length == dir.Length || p[dir.Length] is '\\' or '/'));

    private static NotesFolder ReadFolder(string fingerprint, int workItemId, List<FileInfo> files)
    {
        NotesModule.FolderReads++;
        List<Raw> raws = [];
        foreach (FileInfo file in files)
        {
            (string front, string body) = NoteFile.Split(OpenSpecStoreApi.ReadText(file.FullName));
            NoteMeta meta = NoteFile.Meta(front);
            int.TryParse(Path.GetFileNameWithoutExtension(file.Name)[1..], out int number);
            DateTime created = DateTime.TryParse(meta.Created, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime c) ? c : file.CreationTime;
            raws.Add(new Raw(file.FullName, workItemId, number, meta, body, created, file.LastWriteTime));
        }
        return new NotesFolder(fingerprint, raws) { Notes = BuildNotes(raws) };
    }

    /// <summary>
    /// Brings the cache up to date with every notes/n*.md of every change, archived ones included, reading only the folders that changed.
    /// Two collisions are settled here, so no page ever sees them:
    /// a frontmatter id that disagrees with the file name is set to the file name's number; and when two files carry one id (an agent
    /// wrote one with a stale number) the one created later, then the one in the later folder, takes the next free id: its file is
    /// renamed and its frontmatter rewritten. #nN links to the old number in other notes are left as they are.
    /// MaxId is the highest number seen anywhere, file names and frontmatter alike, before any of that. Returns whether it wrote.
    /// </summary>
    private static bool Load(NotesCache cache, IReadOnlyList<string> touched)
    {
        Dictionary<string, NotesFolder> now = new(StringComparer.OrdinalIgnoreCase);
        List<NotesFolder> folders = [];
        foreach (WorkItem wi in WorkItemsApi.GetAll(includeHidden: true))
        {
            string dir = Path.Combine(wi.Path, "notes");
            DirectoryInfo info = new(dir);
            if (!info.Exists) continue;
            List<FileInfo> files = [.. info.GetFiles("n*.md").OrderBy(f => f.FullName, StringComparer.CurrentCulture)];
            string fingerprint = string.Join('\n', files.Select(f => $"{f.Name}|{f.Length}|{f.LastWriteTimeUtc.Ticks}"));   // taken before reading, so a write during the read shows as a change next time
            NotesFolder folder;
            if (cache.Folders.TryGetValue(dir, out NotesFolder? old) && old.Fingerprint == fingerprint && !Written(dir, touched))
            {
                folder = old;
                if (old.Raws.Any(r => r.WorkItemId != wi.Id))   // a provisional number moved: no file to read again, only the owner of the notes changes
                {
                    foreach (Raw r in old.Raws) r.WorkItemId = wi.Id;
                    old.Notes = BuildNotes(old.Raws);
                }
            }
            else folder = ReadFolder(fingerprint, wi.Id, files);
            now[dir] = folder;
            folders.Add(folder);
        }
        cache.Folders = now;
        List<Raw> raws = [.. folders.SelectMany(f => f.Raws)];
        int maxId = raws.Select(r => Math.Max(r.FileNumber, r.Meta.Id)).DefaultIfEmpty(0).Max();
        bool wrote = false;

        foreach (Raw r in raws.Where(r => r.FileNumber > 0 && r.Meta.Id > 0 && r.Meta.Id != r.FileNumber))   // the file name wins
        {
            r.Meta.Id = r.FileNumber;
            wrote |= OpenSpecStoreApi.WriteText(r.File, NoteFile.Compose(r.Meta, r.Body)).Success;
        }
        foreach (IGrouping<int, Raw> twins in raws.Where(r => r.Id > 0).GroupBy(r => r.Id).Where(g => g.Count() > 1).ToList())
            foreach (Raw r in twins.OrderBy(r => r.Created).ThenBy(r => r.FileNumber).ThenBy(r => r.File, StringComparer.Ordinal).Skip(1).Where(r => r.Meta.Problem is null))   // a file whose frontmatter does not parse is never rewritten
            {
                int id = ++maxId;
                string target = Path.Combine(Path.GetDirectoryName(r.File)!, $"n{id}.md");
                r.Meta.Id = id;
                r.Meta.Created ??= r.Created.ToString("s", CultureInfo.InvariantCulture);
                if (!OpenSpecStoreApi.WriteText(target, NoteFile.Compose(r.Meta, r.Body)).Success) continue;
                OpenSpecStoreApi.MarkOwnChange(r.File);
                try { File.Delete(r.File); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
                r.File = target;
                r.FileNumber = id;
                wrote = true;
            }

        if (wrote)   // the files under the cached notes just changed: build them again from what was written
            foreach (NotesFolder f in folders)
            {
                foreach (Raw r in f.Raws) r.Modified = File.GetLastWriteTime(r.File);
                f.Notes = BuildNotes(f.Raws);
            }
        cache.Notes = [.. folders.SelectMany(f => f.Notes)];
        cache.MaxId = maxId;
        return wrote;
    }

    private static DateTime? Time(string? text) => DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime d) ? d : null;

    /// <summary>One more than the highest number any note file carries anywhere in the repository, by file name or frontmatter, archived changes included.</summary>
    internal static int NextId() { var c = Cached(); return Math.Max(c.MaxId, c.Notes.Select(n => n.Id).DefaultIfEmpty(0).Max()) + 1; }

    /// <summary>The highest note number in one worktree, by <c>notes/n&lt;N&gt;.md</c> file name or frontmatter id (archived changes included); 0 when none.</summary>
    internal static int MaxNoteIdUnder(string repoRoot)
    {
        string changes = Path.Combine(repoRoot, "openspec", "changes");
        if (!Directory.Exists(changes)) return 0;
        int max = 0;
        foreach (string file in Directory.EnumerateFiles(changes, "n*.md", SearchOption.AllDirectories))
        {
            if (!string.Equals(Path.GetFileName(Path.GetDirectoryName(file)), "notes", StringComparison.OrdinalIgnoreCase)) continue;
            if (int.TryParse(Path.GetFileNameWithoutExtension(file)[1..], out int number)) max = Math.Max(max, number);
            max = Math.Max(max, NoteFile.Meta(NoteFile.Split(File.ReadAllText(file)).Frontmatter).Id);
        }
        return max;
    }

    [GeneratedRegex(@"\[\[(?<doc>[^\]]+)\]\]|#wi(?<wi>\d+)\b|#n(?<n>\d+)\b")]
    private static partial Regex Reference();

    internal static IReadOnlyList<NoteLink> ResolveLinks(string text)
    {
        List<NoteLink> links = [];
        HashSet<int> noteIds = [.. GetAll().Select(n => n.Id)];
        foreach (Match m in Reference().Matches(text))
        {
            NoteLink link = m.Groups["doc"].Success ? new NoteLink("doc", m.Groups["doc"].Value.Trim(), DocExists(m.Groups["doc"].Value.Trim()))
                : m.Groups["wi"].Success ? new NoteLink("item", m.Groups["wi"].Value, WorkItemsApi.Get(int.Parse(m.Groups["wi"].Value)) is not null)
                : new NoteLink("note", m.Groups["n"].Value, noteIds.Contains(int.Parse(m.Groups["n"].Value)));
            if (!links.Contains(link)) links.Add(link);
        }
        return links;
    }

    /// <summary>[[runbook/uptime]] matches runbook/uptime(.md) in the repo root or under docs/.</summary>
    private static bool DocExists(string path)
    {
        if (OpenSpecStoreApi.RepoRoot is not string root) return false;
        string rel = path.Replace('/', Path.DirectorySeparatorChar);
        return new[] { rel, rel + ".md", Path.Combine("docs", rel), Path.Combine("docs", rel + ".md") }.Any(p => File.Exists(Path.Combine(root, p)));
    }
}
