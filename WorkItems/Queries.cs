using System.Text.RegularExpressions;
using OpenSpecStore;
using WorkItems.BusinessRules;
using WorkItems.Infrastructure;
using WorkItems.PublicDTOs;

namespace WorkItems;

internal static partial class Queries
{
    internal static IReadOnlyList<WorkItem> GetAll(bool includeHidden) => All().Where(w => includeHidden || !w.Hidden).ToList();

    /// <summary>Every work item: from the host's cache of the board when it installed one (see WorkItemsApi.UseBoard), else read afresh.</summary>
    private static IReadOnlyList<WorkItem> All() => Board().Items;

    private static BoardRead Board() => WorkItemsModule.Board?.Invoke() ?? ReadBoard();

    /// <summary>The whole board read from disk now, nothing kept: every change folder parsed, numbered, given its commit authors and a status the project has.</summary>
    internal static BoardRead ReadBoard()
    {
        List<FolderRead> folders = [.. ListFolders().Select(f => ReadFolder(f.Dir, f.Archived)).OfType<FolderRead>()];
        IReadOnlyList<int> ids = Number(folders);
        IReadOnlyList<string> statuses = GetStatuses();   // the project's list: an item with a status outside it (a pull brought it) shows in Backlog
        var git = WorkItemsModule.Git?.Invoke();
        return new BoardRead([.. folders.Select((f, i) => Present(f, ids[i], statuses, git))], folders);
    }

    /// <summary>
    /// A folder that is being moved, deleted or written while it is read (a merge or an archive moving it, another process
    /// writing a file in it, an edit in Explorer) is left out of this read instead of failing the whole board: the file watcher
    /// reloads the board once the change on disk is done. ponytail: one folder at a time; no retry, the watcher's reload is the retry.
    /// </summary>
    private static T? Tolerant<T>(Func<T> read) where T : class
    {
        try { return read(); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return null; }
    }

    /// <summary>The change folders in board order: open changes by folder name, then archived ones.</summary>
    internal static IReadOnlyList<(string Dir, bool Archived)> ListFolders()
    {
        if (OpenSpecStoreApi.OpenSpecRoot is not string root) return [];
        string changes = Path.Combine(root, "changes");
        if (!Directory.Exists(changes)) return [];
        string archive = Path.Combine(changes, "archive");

        return
        [
            .. Directory.GetDirectories(changes).Where(d => !Path.GetFileName(d).Equals("archive", StringComparison.OrdinalIgnoreCase)).Order().Select(d => (d, false)),
            .. (Directory.Exists(archive) ? Directory.GetDirectories(archive) : []).Order().Select(d => (d, true)),
        ];
    }

    /// <summary>The folder parsed from disk now; null when it is being moved or written (see <see cref="Tolerant{T}"/>).</summary>
    internal static FolderRead? ReadFolder(string dir, bool archived) => Tolerant(() => Parse(dir, archived));

    /// <summary>The number each folder shows, in the order given: its stored x-rtm.id, else the next free one after the highest stored (provisional until its first edit).</summary>
    internal static IReadOnlyList<int> Number(IReadOnlyList<FolderRead> folders)
    {
        int next = folders.Select(f => f.StoredId ?? 0).DefaultIfEmpty(0).Max();
        return [.. folders.Select(f => f.StoredId ?? ++next)];
    }

    /// <summary>What the folder shows as: its number, the commit authors standing in as contributors until the list is edited in the app (x-rtm.contributors, possibly []), and Backlog for a status the project does not have.</summary>
    internal static WorkItem Present(FolderRead folder, int id, IReadOnlyList<string> statuses, (string Token, Func<string, IReadOnlyList<string>> AuthorsOf)? git)
    {
        WorkItem item = folder.Item.Id == id ? folder.Item : folder.Item with { Id = id };
        if (git is { } g && item.Assigned is null) item = item with { Contributors = [.. g.AuthorsOf(item.Folder)] };
        return statuses.Contains(item.Status) ? item : item with { Status = "Backlog" };
    }

    private static FolderRead Parse(string dir, bool archived)
    {
        List<FileSystemInfo> infos = [.. new DirectoryInfo(dir).EnumerateFileSystemInfos("*", SearchOption.AllDirectories)];
        WorkItemsModule.FolderParses++;
        string? problem = null;
        Rtm meta;
        // A .openspec.yaml that does not parse (a hand edit, an agent) must not take the board down: the item shows from what is readable, with the reason as its warning.
        try { meta = Metadata.Read<Rtm>(Yaml(dir), out problem); }   // a value written without quotes that YAML cannot read is mended: the item shows it, with a note
        catch (MetadataException e)
        {
            problem = e.Message + "; fix it and the item's fields will show";
            meta = new Rtm { Id = StoredIdIn(OpenSpecStoreApi.ReadText(Yaml(dir))) };   // its number, when the line can still be found, so links to #wiN hold
        }
        return new FolderRead { Dir = dir, Archived = archived, StoredId = meta.Id, Meta = meta, Item = Build(dir, archived, meta, meta.Id ?? 0, [.. infos.OfType<FileInfo>()], problem) };
    }

    /// <summary>Changes that carry no x-rtm.id of their own, so their number is provisional until their first edit; ResolveDuplicateIds writes it down.</summary>
    internal static IReadOnlyList<WorkItem> WithoutStoredId()
    {
        BoardRead board = Board();
        return [.. board.Folders.Select((f, i) => (f, i)).Where(x => x.f.StoredId is null).Select(x => board.Items[x.i])];
    }

    /// <summary>Changes whenever a change folder appears, goes, is renamed or archived, or a number moves: what the duplicate-number scan needs to know. Names and numbers only, from the cached items.</summary>
    internal static string FolderStamp() =>
        string.Join(';', All().Select(w => (w.Archived ? "archive/" : "") + w.Folder + "=" + w.Id).Order(StringComparer.Ordinal));

    internal static WorkItem? Get(int id) => All().FirstOrDefault(w => w.Id == id);

    /// <summary>
    /// An item's folder, for a step that only needs where to write (the event log right after a command wrote in it): the folder the
    /// board last saw, when it is still there, so the command's own write does not make it read the board again. Else the item is looked up afresh.
    /// </summary>
    internal static string? PathOf(int id)
    {
        if (WorkItemsModule.LastSeen?.Invoke() is { } seen && seen.Items.FirstOrDefault(w => w.Id == id) is { } known && Directory.Exists(known.Path)) return known.Path;
        return Get(id)?.Path;
    }

    internal static int NextId() => All().Select(w => w.Id).DefaultIfEmpty(0).Max() + 1;

    /// <summary>The highest stored x-rtm id of any change (archived included) in one worktree; 0 when none. Reads files directly, so a sibling worktree needs no open repository.</summary>
    internal static int MaxIdUnder(string repoRoot)
    {
        string changes = Path.Combine(repoRoot, "openspec", "changes");
        if (!Directory.Exists(changes)) return 0;
        int max = 0;
        foreach (string file in Directory.EnumerateFiles(changes, ".openspec.yaml", SearchOption.AllDirectories))
        {
            // ponytail: a regex over the x-rtm block instead of a YAML parse; the first indented "id:" after "x-rtm:" is the item's number.
            if (StoredIdIn(File.ReadAllText(file)) is int id) max = Math.Max(max, id);
        }
        return max;
    }

    private static int? StoredIdIn(string yaml) =>
        StoredId().Match(yaml.Replace("\r\n", "\n").Replace('\r', '\n')) is { Success: true } m && int.TryParse(m.Groups[1].Value, out int id) ? id : null;

    [GeneratedRegex(@"^x-rtm:[^\n]*\n(?:[ \t]+[^\n]*\n|[ \t]*\n)*?[ \t]+id:[ \t]*(\d+)", RegexOptions.Multiline)]
    private static partial Regex StoredId();

    /// <summary>Work items whose x-rtm.dependsOn names this one, i.e. what this item is blocking.</summary>
    internal static IReadOnlyList<WorkItem> GetBlocking(int id) => GetAll(includeHidden: true).Where(w => (w.DependsOn ?? []).Contains(id)).ToList();

    internal static IReadOnlyList<string> GetWarnings() =>
        All().GroupBy(w => w.Id).Where(g => g.Count() > 1)
            .Select(g => $"#wi{g.Key} is used by {string.Join(" and ", g.Select(w => w.Folder))}")
            .Concat(GetAll(false).Where(w => w.Warning is not null).Select(w => $"#wi{w.Id}: {w.Warning}"))
            .Concat(GetConfigProblems())
            .ToList();

    internal static string GetDescription(int id) =>
        Get(id) is WorkItem w ? Proposal.Body(OpenSpecStoreApi.ReadText(Path.Combine(w.Path, "proposal.md"))) : "";

    internal static IReadOnlyDictionary<string, IReadOnlyList<string>> GetLanes()
    {
        // lanes that do not parse: the board shows none until the file is fixed (the warning says so, see GetConfigProblems)
        return (OpenSpecStoreApi.ReadConfigSection<Dictionary<string, List<string>>>("lanes").Value ?? []).ToDictionary(k => k.Key, k => (IReadOnlyList<string>)k.Value);
    }

    /// <summary>What is wrong with the project's lanes and statuses in config.yaml (each reads as its default until the file is fixed); none when both are fine.</summary>
    internal static IEnumerable<string> GetConfigProblems()
    {
        if (OpenSpecStoreApi.ReadConfigSection<Dictionary<string, List<string>>>("lanes") is { Unavailable: false, Problem: { } lanes }) yield return lanes;
        if (OpenSpecStoreApi.ReadConfigSection<List<string>>("statuses") is { Unavailable: false, Problem: { } statuses }) yield return statuses;
    }

    /// <summary>
    /// The project's statuses in board order: <c>x-rtm.statuses</c> in openspec/config.yaml, else the built-in list. Backlog, In Progress and Completed are always in it
    /// (put back where a hand edit left them out); names are trimmed and a name listed twice counts once.
    /// </summary>
    internal static IReadOnlyList<string> GetStatuses()
    {
        return Normalise(OpenSpecStoreApi.ReadConfigSection<List<string>>("statuses").Value);   // a list that does not parse reads as none: the built-in one until the file is fixed
    }

    internal static IReadOnlyList<string> Normalise(IEnumerable<string?>? saved)
    {
        List<string> list = [.. (saved ?? []).Select(s => (s ?? "").Trim()).Where(s => s.Length > 0).Distinct()];
        if (list.Count == 0) return WorkItemsApi.Statuses;
        if (!list.Contains("Backlog")) list.Insert(0, "Backlog");
        if (!list.Contains("In Progress")) list.Insert(Math.Min(1, list.Count), "In Progress");
        if (!list.Contains("Completed")) list.Add("Completed");
        return list;
    }

    internal static string Yaml(string dir) => Path.Combine(dir, ".openspec.yaml");

    /// <summary>Capability paths this change has delta specs for (specs/&lt;path&gt;/spec.md inside the change).</summary>
    internal static IEnumerable<string> DeltaCapabilities(string dir)
    {
        string specs = Path.Combine(dir, "specs");
        return Directory.Exists(specs)
            ? Directory.EnumerateFiles(specs, "spec.md", SearchOption.AllDirectories).Select(f => Path.GetRelativePath(specs, Path.GetDirectoryName(f)!).Replace('\\', '/')).Order()
            : [];
    }

    private static WorkItem Build(string dir, bool archived, Rtm meta, int id, IReadOnlyList<FileInfo> files, string? problem)
    {
        string folder = Path.GetFileName(dir);
        string proposal = OpenSpecStoreApi.ReadText(Path.Combine(dir, "proposal.md"));
        string yaml = OpenSpecStoreApi.ReadText(Yaml(dir));
        string? warning = problem;
        string status = archived ? "Completed" : meta.Status ?? "Backlog";
        if (!archived && status == "Completed") (status, warning) = ("Blocked", "Marked Completed but not archived");

        List<string> derived = DeltaCapabilities(dir).ToList();
        List<string> unlinked = [.. (meta.UnlinkedFeatures ?? []).Intersect(derived)];
        // one feature per item: the one picked by hand wins over the change's specs, else its first spec (older files may list several)
        List<FeatureLink> features = [.. (meta.Features ?? []).Except(derived).Select(p => new FeatureLink(p, false)), .. derived.Except(unlinked).Select(p => new FeatureLink(p, true))];
        features = features.Take(1).ToList();

        DateTime created = CreatedLine().Match(yaml) is { Success: true } c && DateTime.TryParse(c.Groups["d"].Value, out DateTime d) ? d : Directory.GetCreationTime(dir);
        DateTime modified = WorklogChange(dir, files) ?? files.Select(f => f.LastWriteTime).DefaultIfEmpty(created).Max();   // the worklog's last change; a checkout or sync rewrites every file, so file times only stand in when there is no worklog

        return new WorkItem(
            id, folder, dir,
            meta.Title ?? Proposal.Title(proposal) ?? Proposal.FolderTitle(folder),
            status, meta.Priority ?? "low",
            meta.Contributors ?? [],
            features,
            TasksFile.Parse(OpenSpecStoreApi.ReadText(Path.Combine(dir, "tasks.md"))),
            meta.Lanes ?? [],
            archived, created, modified, warning, meta.Hidden == true, meta.Contributors,
            meta.Label, meta.Commits ?? [], (meta.Tests ?? []).Select(t => new TestLink(t.Suite, t.Test, t.Note)).ToList(), meta.DependsOn ?? [], meta.MergedFrom ?? [], unlinked,
            meta.AgentBy, Time(meta.AgentAt), Time(meta.ReviewedAt), Day(meta.Started), Day(meta.Due), Day(meta.Moved), meta.Plan, Revision(dir, files), meta.Docs ?? []);
    }

    /// <summary>
    /// First 12 hex chars of SHA-256 over path + content of every file in the change folder, sorted by repo-relative path; "" when the folder cannot be read.
    /// It hashes contents, not the folder's look (sizes and times): an agent compares it across processes and copies of the repository, which times do not survive,
    /// and it is computed only when the folder is parsed, so unchanged folders never pay for it.
    /// </summary>
    private static string Revision(string dir, IReadOnlyList<FileInfo> files)
    {
        try
        {
            string root = OpenSpecStoreApi.RepoRoot ?? dir;
            using var sha = System.Security.Cryptography.IncrementalHash.CreateHash(System.Security.Cryptography.HashAlgorithmName.SHA256);
            foreach (string file in files.Select(f => (Path: Path.GetRelativePath(root, f.FullName).Replace('\\', '/'), Full: f.FullName)).OrderBy(f => f.Path, StringComparer.Ordinal).Select(f => f.Full))
                sha.AppendData(System.Text.Encoding.UTF8.GetBytes(Path.GetRelativePath(root, file).Replace('\\', '/') + "\n" + File.ReadAllText(file)));
            return Convert.ToHexStringLower(sha.GetHashAndReset())[..12];
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return ""; }
    }

    /// <summary>When the worklog last changed: the newest line of events.log or note's <c>created</c> time; null when the change has neither.</summary>
    private static DateTime? WorklogChange(string dir, IReadOnlyList<FileInfo> files)
    {
        DateTime? last = EventLog.Read(dir).Select(e => (DateTime?)e.At).Max();
        foreach (FileInfo f in files.Where(f => f.Extension == ".md" && f.Directory?.Name == "notes"))
        {
            try
            {
                string head = string.Join(Environment.NewLine, File.ReadLines(f.FullName).Take(10));
                if (NoteCreated().Match(head) is { Success: true } m && DateTime.TryParse(m.Groups["d"].Value, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out DateTime at) && at > (last ?? DateTime.MinValue)) last = at;
            }
            catch (IOException) { }
        }
        return last;
    }

    [GeneratedRegex(@"^created:\s*(?<d>\S+)", RegexOptions.Multiline)]
    private static partial Regex NoteCreated();

    private static DateOnly? Day(string? text) => DateOnly.TryParseExact(text, "yyyy-MM-dd", out DateOnly d) ? d : null;

    private static DateTime? Time(string? text) => DateTime.TryParse(text, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out DateTime d) ? d : null;

    [GeneratedRegex(@"^created:\s*(?<d>\S+)", RegexOptions.Multiline)]
    private static partial Regex CreatedLine();
}
