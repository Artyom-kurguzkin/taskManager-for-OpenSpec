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
    /// <summary>All notes, parsed once per OpenSpecStore content version.</summary>
    internal static IReadOnlyList<Note> GetAll()
    {
        long version = OpenSpecStoreApi.Version;
        if (NotesModule.Cache is { } c && c.Version == version) return c.Notes;
        IReadOnlyList<Note> notes = Load();
        NotesModule.Cache = (version, notes);
        NotesModule.Loads++;
        return notes;
    }

    private static IReadOnlyList<Note> Load()
    {
        List<Note> notes = [];
        foreach (WorkItem wi in WorkItemsApi.GetAll(includeHidden: true))
        {
            string dir = Path.Combine(wi.Path, "notes");
            if (!Directory.Exists(dir)) continue;
            foreach (string file in Directory.GetFiles(dir, "n*.md").Order())
            {
                (string front, string body) = NoteFile.Split(OpenSpecStoreApi.ReadText(file));
                NoteMeta meta = NoteFile.Meta(front);
                int id = meta.Id > 0 ? meta.Id : int.TryParse(Path.GetFileNameWithoutExtension(file)[1..], out int n) ? n : 0;
                DateTime created = DateTime.TryParse(meta.Created, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime c) ? c : File.GetCreationTime(file);
                notes.Add(new Note(id, wi.Id, file, NoteFile.Title(body, id), meta.By ?? "", created, File.GetLastWriteTime(file), body, meta.Links ?? [], meta.Commits ?? []));
            }
        }
        return notes;
    }

    internal static int NextId() => GetAll().Select(n => n.Id).DefaultIfEmpty(0).Max() + 1;

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
