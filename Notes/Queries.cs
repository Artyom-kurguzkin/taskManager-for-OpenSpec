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

    private static (long Version, IReadOnlyList<Note> Notes, int MaxId) Cached()
    {
        long version = OpenSpecStoreApi.Version;
        string root = OpenSpecStoreApi.RepoRoot ?? "";
        if (NotesModule.Cache.TryGetValue(root, out var c) && c.Version == version) return c;
        (IReadOnlyList<Note> notes, int maxId, bool wrote) = Load();
        if (wrote) version = OpenSpecStoreApi.Version;   // our own renames and rewrites are not a reason to read again
        NotesModule.Cache[root] = (version, notes, maxId);
        NotesModule.Loads++;
        return NotesModule.Cache[root];
    }

    private sealed class Raw(string file, int workItemId, int fileNumber, NoteMeta meta, string body, DateTime created)
    {
        internal string File = file;
        internal int WorkItemId = workItemId, FileNumber = fileNumber;
        internal NoteMeta Meta = meta;
        internal string Body = body;
        internal DateTime Created = created;
        internal int Id => FileNumber > 0 ? FileNumber : Meta.Id;
    }

    /// <summary>
    /// Reads every notes/n*.md of every change, archived ones included. Two collisions are settled here, so no page ever sees them:
    /// a frontmatter id that disagrees with the file name is set to the file name's number; and when two files carry one id (an agent
    /// wrote one with a stale number) the one created later, then the one in the later folder, takes the next free id: its file is
    /// renamed and its frontmatter rewritten. #nN links to the old number in other notes are left as they are.
    /// MaxId is the highest number seen anywhere, file names and frontmatter alike, before any of that.
    /// </summary>
    private static (IReadOnlyList<Note> Notes, int MaxId, bool Wrote) Load()
    {
        List<Raw> raws = [];
        foreach (WorkItem wi in WorkItemsApi.GetAll(includeHidden: true))
        {
            string dir = Path.Combine(wi.Path, "notes");
            if (!Directory.Exists(dir)) continue;
            foreach (string file in Directory.GetFiles(dir, "n*.md").Order())
            {
                (string front, string body) = NoteFile.Split(OpenSpecStoreApi.ReadText(file));
                NoteMeta meta = NoteFile.Meta(front);
                int.TryParse(Path.GetFileNameWithoutExtension(file)[1..], out int number);
                DateTime created = DateTime.TryParse(meta.Created, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime c) ? c : File.GetCreationTime(file);
                raws.Add(new Raw(file, wi.Id, number, meta, body, created));
            }
        }
        int maxId = raws.Select(r => Math.Max(r.FileNumber, r.Meta.Id)).DefaultIfEmpty(0).Max();
        bool wrote = false;

        foreach (Raw r in raws.Where(r => r.FileNumber > 0 && r.Meta.Id > 0 && r.Meta.Id != r.FileNumber))   // the file name wins
        {
            r.Meta.Id = r.FileNumber;
            wrote |= OpenSpecStoreApi.WriteText(r.File, NoteFile.Compose(r.Meta, r.Body)).Success;
        }
        foreach (IGrouping<int, Raw> twins in raws.Where(r => r.Id > 0).GroupBy(r => r.Id).Where(g => g.Count() > 1).ToList())
            foreach (Raw r in twins.OrderBy(r => r.Created).ThenBy(r => r.FileNumber).ThenBy(r => r.File, StringComparer.Ordinal).Skip(1))
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

        List<Note> notes = [];
        foreach (Raw r in raws)
            notes.Add(new Note(r.Id, r.WorkItemId, r.File, NoteFile.Title(r.Meta.Title, r.Body, r.Id), r.Meta.By ?? "", r.Created, File.GetLastWriteTime(r.File), r.Body, r.Meta.Links ?? [], r.Meta.Commits ?? [], r.Meta.Attachments ?? [],
                r.Meta.AgentBy, Time(r.Meta.AgentAt), Time(r.Meta.ReviewedAt), r.Meta.MergedFrom, Time(r.Meta.MergedAt)));
        return (notes, maxId, wrote);
    }

    private static DateTime? Time(string? text) => DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime d) ? d : null;

    /// <summary>One more than the highest number any note file carries anywhere in the repository, by file name or frontmatter, archived changes included.</summary>
    internal static int NextId() { var c = Cached(); return Math.Max(c.MaxId, c.Notes.Select(n => n.Id).DefaultIfEmpty(0).Max()) + 1; }

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
