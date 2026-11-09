using System.Text.RegularExpressions;
using OpenSpecStore;
using WorkItems.BusinessRules;
using WorkItems.Infrastructure;
using WorkItems.PublicDTOs;

namespace WorkItems;

internal static partial class Queries
{
    internal static IReadOnlyList<WorkItem> GetAll(bool includeHidden) => All().Where(w => includeHidden || !w.Hidden).ToList();

    /// <summary>Every work item, parsed once per OpenSpecStore content version (own write, outside edit or repo switch) and git commit.</summary>
    private static IReadOnlyList<WorkItem> All()
    {
        long version = OpenSpecStoreApi.Version;
        string root = OpenSpecStoreApi.RepoRoot ?? "";
        var git = WorkItemsModule.Git?.Invoke();
        string token = git?.Token ?? "";
        if (WorkItemsModule.Cache.TryGetValue(root, out var c) && c.Version == version && c.Git == token) return c.Items;
        IReadOnlyList<WorkItem> items = Load();
        // Commit authors only stand in until someone edits the list in the app (x-rtm.contributors, possibly []).
        if (git is { } g) items = items.Select(i => i.Assigned is null ? i with { Contributors = [.. g.AuthorsOf(i.Folder)] } : i).ToList();
        WorkItemsModule.Cache[root] = (version, token, items);
        WorkItemsModule.Loads++;
        return items;
    }

    private static IReadOnlyList<WorkItem> Load()
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
        List<(string Dir, bool Archived, Rtm Meta)> read = folders.Select(f => (f.Dir, f.Archived, Metadata.Read<Rtm>(Yaml(f.Dir)))).ToList();

        // Changes without a stored number get a provisional one, written on their first edit.
        int next = read.Select(r => r.Meta.Id ?? 0).DefaultIfEmpty(0).Max();
        return read.Select(r => Build(r.Dir, r.Archived, r.Meta, r.Meta.Id ?? ++next)).ToList();
    }

    internal static WorkItem? Get(int id) => All().FirstOrDefault(w => w.Id == id);

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
            Match m = StoredId().Match(File.ReadAllText(file));
            if (m.Success && int.TryParse(m.Groups[1].Value, out int id)) max = Math.Max(max, id);
        }
        return max;
    }

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

    internal static IReadOnlyDictionary<string, IReadOnlyList<string>> GetLanes() =>
        OpenSpecStoreApi.OpenSpecRoot is string root
            ? (Metadata.ReadSection<Dictionary<string, List<string>>>(Path.Combine(root, "config.yaml"), "lanes") ?? []).ToDictionary(k => k.Key, k => (IReadOnlyList<string>)k.Value)
            : new Dictionary<string, IReadOnlyList<string>>();

    internal static string Yaml(string dir) => Path.Combine(dir, ".openspec.yaml");

    /// <summary>Capability paths this change has delta specs for (specs/&lt;path&gt;/spec.md inside the change).</summary>
    internal static IEnumerable<string> DeltaCapabilities(string dir)
    {
        string specs = Path.Combine(dir, "specs");
        return Directory.Exists(specs)
            ? Directory.EnumerateFiles(specs, "spec.md", SearchOption.AllDirectories).Select(f => Path.GetRelativePath(specs, Path.GetDirectoryName(f)!).Replace('\\', '/')).Order()
            : [];
    }

    private static WorkItem Build(string dir, bool archived, Rtm meta, int id)
    {
        string folder = Path.GetFileName(dir);
        string proposal = OpenSpecStoreApi.ReadText(Path.Combine(dir, "proposal.md"));
        string yaml = OpenSpecStoreApi.ReadText(Yaml(dir));
        string? warning = null;
        string status = archived ? "Completed" : meta.Status ?? "Backlog";
        if (!archived && status == "Completed") (status, warning) = ("Blocked", "Marked Completed but not archived");
        if (!WorkItemsApi.Statuses.Contains(status)) status = "Backlog";

        List<string> derived = DeltaCapabilities(dir).ToList();
        List<string> unlinked = [.. (meta.UnlinkedFeatures ?? []).Intersect(derived)];
        // one feature per item: the one picked by hand wins over the change's specs, else its first spec (older files may list several)
        List<FeatureLink> features = [.. (meta.Features ?? []).Except(derived).Select(p => new FeatureLink(p, false)), .. derived.Except(unlinked).Select(p => new FeatureLink(p, true))];
        features = features.Take(1).ToList();

        DateTime created = CreatedLine().Match(yaml) is { Success: true } c && DateTime.TryParse(c.Groups["d"].Value, out DateTime d) ? d : Directory.GetCreationTime(dir);
        DateTime modified = Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories).Select(File.GetLastWriteTime).DefaultIfEmpty(created).Max();

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
            meta.AgentBy, Time(meta.AgentAt), Time(meta.ReviewedAt), Day(meta.Started), Day(meta.Due), Day(meta.Moved), meta.Plan, Revision(dir));
    }

    /// <summary>First 12 hex chars of SHA-256 over path + content of every file in the change folder, sorted by repo-relative path; "" when the folder cannot be read.</summary>
    private static string Revision(string dir)
    {
        try
        {
            string root = OpenSpecStoreApi.RepoRoot ?? dir;
            using var sha = System.Security.Cryptography.IncrementalHash.CreateHash(System.Security.Cryptography.HashAlgorithmName.SHA256);
            foreach (string file in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories).Select(f => (Path: Path.GetRelativePath(root, f).Replace('\\', '/'), Full: f)).OrderBy(f => f.Path, StringComparer.Ordinal).Select(f => f.Full))
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
