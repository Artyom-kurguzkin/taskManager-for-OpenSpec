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
    /// <summary>All notes: from the host's cache when it installed one (see NotesApi.UseNotes), else read afresh. Reading also settles id collisions (see <see cref="Settle"/>).</summary>
    internal static IReadOnlyList<Note> GetAll() => Cached().Notes;

    /// <summary>The notes and the highest note number seen anywhere: from the host's cache when it installed one, else read afresh.</summary>
    private static NotesRead Cached() => NotesModule.Reader?.Invoke() ?? ReadAll(out _);

    /// <summary>Every change's notes/ folder read from disk now and settled, nothing kept; hands over the renumberings made.</summary>
    internal static NotesRead ReadAll(out IReadOnlyList<(int OldId, int NewId, string Path)> renumbered)
    {
        lock (OpenSpecStoreApi.StepsGate)   // never mid-way through a merge: see StepsGate
        {
            List<NotesFolderRead> folders = [];
            foreach (WorkItem wi in WorkItemsApi.GetAll(includeHidden: true))
                if (Directory.Exists(Path.Combine(wi.Path, "notes"))) folders.Add(ReadFolder(Path.Combine(wi.Path, "notes"), wi.Id));
            return Settle(folders, out renumbered, out _);
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

    /// <summary>Reads the notes (which settles collisions on the way) and hands over the renumberings made since the last call; empty when there were none.</summary>
    internal static IReadOnlyList<(int OldId, int NewId, string Path)> RepairDuplicateIds()
    {
        if (NotesModule.Drain is { } drain) return drain();
        ReadAll(out IReadOnlyList<(int OldId, int NewId, string Path)> made);
        return made;
    }

    /// <summary>A note whose frontmatter did not parse shows its title (from the body) followed by why, so the warning is where the user looks first.</summary>
    private static string WithProblem(string title, string? problem) => problem is null ? title : $"{title} - {problem}";

    private static List<Note> BuildNotes(List<Raw> raws) =>
        [.. raws.Select(r => new Note(r.Id, r.WorkItemId, r.File, WithProblem(NoteFile.Title(r.Meta.Title, r.Body, r.Id), r.Meta.Problem), r.Meta.By ?? "", r.Created, r.Modified, r.Body, r.Meta.Links ?? [], r.Meta.Commits ?? [], r.Meta.Attachments ?? [],
            r.Meta.AgentBy, Time(r.Meta.AgentAt), Time(r.Meta.ReviewedAt), r.Meta.MergedFrom, Time(r.Meta.MergedAt), r.Meta.Kind))];

    /// <summary>One change's notes/ folder read from disk now: every n*.md in it, and the notes parsed from them.</summary>
    internal static NotesFolderRead ReadFolder(string dir, int workItemId)
    {
        NotesModule.FolderReads++;
        List<FileInfo> files = [.. new DirectoryInfo(dir).GetFiles("n*.md").OrderBy(f => f.FullName, StringComparer.CurrentCulture)];
        List<Raw> raws = [];
        foreach (FileInfo file in files)
        {
            (string front, string body) = NoteFile.Split(OpenSpecStoreApi.ReadText(file.FullName));
            NoteMeta meta = NoteFile.Meta(front);
            int.TryParse(Path.GetFileNameWithoutExtension(file.Name)[1..], out int number);
            DateTime created = DateTime.TryParse(meta.Created, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime c) ? c : file.CreationTime;
            raws.Add(new Raw(file.FullName, workItemId, number, meta, body, created, file.LastWriteTime));
        }
        return new NotesFolderRead { Raws = raws, Notes = BuildNotes(raws) };
    }

    /// <summary>A work item's number moved (a provisional one): no file is read again, only the owner of the notes changes.</summary>
    internal static void Rebind(NotesFolderRead folder, int workItemId)
    {
        if (!folder.Raws.Any(r => r.WorkItemId != workItemId)) return;
        foreach (Raw r in folder.Raws) r.WorkItemId = workItemId;
        folder.Notes = BuildNotes(folder.Raws);
    }

    /// <summary>
    /// Brings the folders read (every notes/n*.md of every change, archived ones included) to a consistent set.
    /// Two collisions are settled here, so no page ever sees them:
    /// a frontmatter id that disagrees with the file name is set to the file name's number; and when two files carry one id (an agent
    /// wrote one with a stale number) the one created later, then the one in the later folder, takes the next free id: its file is
    /// renamed and its frontmatter rewritten. #nN links to the old number in other notes are left as they are.
    /// MaxId is the highest number seen anywhere, file names and frontmatter alike, before any of that. <paramref name="wrote"/>: whether it wrote.
    /// </summary>
    internal static NotesRead Settle(IReadOnlyList<NotesFolderRead> folders, out IReadOnlyList<(int OldId, int NewId, string Path)> renumbered, out bool wrote)
    {
        List<(int, int, string)> made = [];
        List<Raw> raws = [.. folders.SelectMany(f => f.Raws)];
        int maxId = raws.Select(r => Math.Max(r.FileNumber, r.Meta.Id)).DefaultIfEmpty(0).Max();
        wrote = false;

        foreach (Raw r in raws.Where(r => r.FileNumber > 0 && r.Meta.Id > 0 && r.Meta.Id != r.FileNumber))   // the file name wins
        {
            r.Meta.Id = r.FileNumber;
            wrote |= OpenSpecStoreApi.WriteText(r.File, NoteFile.Compose(r.Meta, r.Body)).Success;
        }
        foreach (IGrouping<int, Raw> twins in raws.Where(r => r.Id > 0).GroupBy(r => r.Id).Where(g => g.Count() > 1).ToList())
        {
            List<Raw> ordered = [.. twins.OrderBy(r => r.Created).ThenBy(r => r.FileNumber).ThenBy(r => r.File, StringComparer.Ordinal)];
            foreach (Raw r in ordered.Skip(1).Where(r => r.Meta.Problem is null))   // a file whose frontmatter does not parse is never rewritten
            {
                int id = ++maxId, old = r.Id;
                string target = Path.Combine(Path.GetDirectoryName(r.File)!, $"n{id}.md");
                string body = OpenSpecStoreApi.RenameReference(r.Body, "note", old, id);   // what the note says about itself follows it; other notes' links to #n{old} meant the keeper
                r.Meta.Id = id;
                r.Meta.Created ??= r.Created.ToString("s", CultureInfo.InvariantCulture);
                if (!OpenSpecStoreApi.WriteText(target, NoteFile.Compose(r.Meta, body)).Success) continue;
                OpenSpecStoreApi.MarkOwnChange(r.File);
                try { File.Delete(r.File); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
                r.File = target;
                r.FileNumber = id;
                r.Body = body;
                wrote = true;
                made.Add((old, id, target));
                Raw keeper = ordered[0];
                WorkItemsApi.LogEvent(r.WorkItemId, "renumbered", $"{OpenSpecStoreApi.FormatReference("note", old.ToString())} -> {OpenSpecStoreApi.FormatReference("note", id.ToString())}: the id was taken by \"{NoteFile.Title(keeper.Meta.Title, keeper.Body, keeper.Id)}\" ({Path.GetFileName(Path.GetDirectoryName(Path.GetDirectoryName(keeper.File)))}) after a merge");
            }
        }

        if (wrote)   // the files under the notes just changed: build them again from what was written
            foreach (NotesFolderRead f in folders)
            {
                foreach (Raw r in f.Raws) r.Modified = File.GetLastWriteTime(r.File);
                f.Notes = BuildNotes(f.Raws);
            }
        renumbered = made;
        return new NotesRead([.. folders.SelectMany(f => f.Notes)], maxId, wrote);
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

    internal static IReadOnlyList<NoteLink> ResolveLinks(string text)
    {
        List<NoteLink> links = [];
        HashSet<int> noteIds = [.. GetAll().Select(n => n.Id)];
        foreach (OpenSpecStore.PublicDTOs.Reference r in OpenSpecStoreApi.FindReferences(text))
        {
            NoteLink link = r.Kind == "doc" ? new NoteLink("doc", r.Target, DocExists(r.Target))
                : r.Kind == "item" ? new NoteLink("item", r.Target, WorkItemsApi.Get(r.Number) is not null)
                : new NoteLink("note", r.Target, noteIds.Contains(r.Number));
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
