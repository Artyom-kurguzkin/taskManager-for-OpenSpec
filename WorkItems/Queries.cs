using System.Text.RegularExpressions;
using OpenSpecStore;
using WorkItems.BusinessRules;
using WorkItems.Infrastructure;
using WorkItems.PublicDTOs;

namespace WorkItems;

internal static partial class Queries
{
    internal static IReadOnlyList<WorkItem> GetAll(bool includeHidden) => All().Where(w => includeHidden || !w.Hidden).ToList();

    /// <summary>
    /// Every work item. The list is assembled once per OpenSpecStore content version (own write, outside edit or repo switch) and git commit,
    /// and each change folder is parsed only when its files changed (see <see cref="FolderEntry"/>): the others keep their objects.
    /// </summary>
    private static IReadOnlyList<WorkItem> All()
    {
        lock (WorkItemsModule.Gate)
        {
            long version = OpenSpecStoreApi.Version;
            string root = OpenSpecStoreApi.RepoRoot ?? "";
            long generation = OpenSpecStoreApi.RepoGeneration;   // a repository closed and opened again starts empty: the store forgot what the app had read of it
            var git = WorkItemsModule.Git?.Invoke();
            string token = git?.Token ?? "";
            int epoch = WorkItemsModule.GitEpoch;
            if (!WorkItemsModule.Cache.TryGetValue(root, out RepoCache? c) || c.Generation != generation)
                WorkItemsModule.Cache[root] = c = new RepoCache { Generation = generation };
            if (c.Version == version && c.Git == token && c.GitEpoch == epoch) return c.Items;

            (IReadOnlyList<string> touched, long cursor) = OpenSpecStoreApi.ChangesSince(c.Cursor);   // asked before reading: a write that lands meanwhile shows next time
            c.Ordered = ReadFolders(c.Ordered, touched);

            // Changes without a stored number get a provisional one, written on their first edit.
            int next = c.Ordered.Select(e => e.Meta.Id ?? 0).DefaultIfEmpty(0).Max();
            List<WorkItem> items = [];
            foreach (FolderEntry e in c.Ordered)
            {
                int id = e.Meta.Id ?? ++next;
                if (e.Item.Id != id) e.Item = e.Item with { Id = id };
                // Commit authors only stand in until someone edits the list in the app (x-rtm.contributors, possibly []).
                items.Add(e.WithAuthors(git, token, epoch));
            }
            (c.Version, c.Git, c.GitEpoch, c.Cursor, c.Items) = (version, token, epoch, cursor, items);
            WorkItemsModule.Loads++;
            return items;
        }
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

    /// <summary>The change folders in board order, each from <paramref name="old"/> when nothing in it changed and else read again; folders that are gone drop out.</summary>
    private static List<FolderEntry> ReadFolders(List<FolderEntry> old, IReadOnlyList<string> touched)
    {
        if (OpenSpecStoreApi.OpenSpecRoot is not string root) return [];
        string changes = Path.Combine(root, "changes");
        if (!Directory.Exists(changes)) return [];
        string archive = Path.Combine(changes, "archive");

        List<(string Dir, bool Archived)> folders =
        [
            .. Directory.GetDirectories(changes).Where(d => !Path.GetFileName(d).Equals("archive", StringComparison.OrdinalIgnoreCase)).Order().Select(d => (d, false)),
            .. (Directory.Exists(archive) ? Directory.GetDirectories(archive) : []).Order().Select(d => (d, true)),
        ];
        Dictionary<string, FolderEntry> before = old.ToDictionary(e => e.Dir, StringComparer.OrdinalIgnoreCase);
        List<FolderEntry> now = [];
        foreach ((string dir, bool archived) in folders)
            if (Tolerant(() => Fresh(dir, archived, before.GetValueOrDefault(dir), touched)) is FolderEntry entry) now.Add(entry);
        return now;
    }

    /// <summary>
    /// The folder's entry: <paramref name="old"/> when its files look the same and the app did not write in it, else a new one parsed from disk.
    /// The look of a folder is the name, size and modified time of every file in it, from one directory pass that reads no file.
    /// </summary>
    private static FolderEntry Fresh(string dir, bool archived, FolderEntry? old, IReadOnlyList<string> touched)
    {
        List<FileSystemInfo> infos = [.. new DirectoryInfo(dir).EnumerateFileSystemInfos("*", SearchOption.AllDirectories)];
        string fingerprint = Fingerprint(dir, infos);   // taken before reading, so a write during the read shows as a change next time
        if (old is not null && old.Archived == archived && old.Fingerprint == fingerprint && !Written(dir, touched)) return old;
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
        return new FolderEntry(dir, archived, fingerprint, meta, Build(dir, archived, meta, meta.Id ?? 0, [.. infos.OfType<FileInfo>()], problem));
    }

    private static string Fingerprint(string dir, List<FileSystemInfo> infos)
    {
        System.Text.StringBuilder sb = new();
        foreach (FileSystemInfo info in infos)
        {
            sb.Append(info.FullName, dir.Length, info.FullName.Length - dir.Length);
            if (info is FileInfo f) sb.Append('|').Append(f.Length).Append('|').Append(f.LastWriteTimeUtc.Ticks);
            sb.Append('\n');
        }
        return sb.ToString();
    }

    /// <summary>True when the app itself wrote, deleted or moved this folder or something in it (the store keeps that record).</summary>
    internal static bool Written(string dir, IReadOnlyList<string> touched) =>
        touched.Any(p => p.StartsWith(dir, StringComparison.OrdinalIgnoreCase) && (p.Length == dir.Length || p[dir.Length] is '\\' or '/'));

    /// <summary>Changes that carry no x-rtm.id of their own, so their number is provisional until their first edit; ResolveDuplicateIds writes it down.</summary>
    internal static IReadOnlyList<WorkItem> WithoutStoredId()
    {
        lock (WorkItemsModule.Gate)
        {
            All();
            return WorkItemsModule.Cache.TryGetValue(OpenSpecStoreApi.RepoRoot ?? "", out RepoCache? c) ? [.. c.Ordered.Where(e => e.Meta.Id is null).Select(e => e.Item)] : [];
        }
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
        lock (WorkItemsModule.Gate)
            if (WorkItemsModule.Cache.TryGetValue(OpenSpecStoreApi.RepoRoot ?? "", out RepoCache? c) && c.Items.FirstOrDefault(w => w.Id == id) is { } known && Directory.Exists(known.Path)) return known.Path;
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
            .ToList();

    internal static string GetDescription(int id) =>
        Get(id) is WorkItem w ? Proposal.Body(OpenSpecStoreApi.ReadText(Path.Combine(w.Path, "proposal.md"))) : "";

    internal static IReadOnlyDictionary<string, IReadOnlyList<string>> GetLanes()
    {
        if (OpenSpecStoreApi.OpenSpecRoot is not string root) return new Dictionary<string, IReadOnlyList<string>>();
        try { return (Metadata.ReadSection<Dictionary<string, List<string>>>(Path.Combine(root, "config.yaml"), "lanes") ?? []).ToDictionary(k => k.Key, k => (IReadOnlyList<string>)k.Value); }
        catch (MetadataException) { return new Dictionary<string, IReadOnlyList<string>>(); }   // lanes that do not parse: the board shows none until the file is fixed
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
        if (!WorkItemsApi.Statuses.Contains(status)) status = "Backlog";

        List<string> derived = DeltaCapabilities(dir).ToList();
        List<string> unlinked = [.. (meta.UnlinkedFeatures ?? []).Intersect(derived)];
        // one feature per item: the one picked by hand wins over the change's specs, else its first spec (older files may list several)
        List<FeatureLink> features = [.. (meta.Features ?? []).Except(derived).Select(p => new FeatureLink(p, false)), .. derived.Except(unlinked).Select(p => new FeatureLink(p, true))];
        features = features.Take(1).ToList();

        DateTime created = CreatedLine().Match(yaml) is { Success: true } c && DateTime.TryParse(c.Groups["d"].Value, out DateTime d) ? d : Directory.GetCreationTime(dir);
        DateTime modified = files.Select(f => f.LastWriteTime).DefaultIfEmpty(created).Max();

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

    private static DateOnly? Day(string? text) => DateOnly.TryParseExact(text, "yyyy-MM-dd", out DateOnly d) ? d : null;

    private static DateTime? Time(string? text) => DateTime.TryParse(text, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out DateTime d) ? d : null;

    [GeneratedRegex(@"^created:\s*(?<d>\S+)", RegexOptions.Multiline)]
    private static partial Regex CreatedLine();
}
