using System.Text.RegularExpressions;
using OpenSpecStore;
using WorkItems.BusinessRules;
using WorkItems.Infrastructure;
using WorkItems.PublicDTOs;

namespace WorkItems;

internal static partial class Commands
{
    private const string NotFound = "That work item no longer exists";
    private const string Archived = "Completed work items are archived and can't be moved back";

    /// <summary>Loads the item's x-rtm block, applies <paramref name="change"/>, and writes it back (stamping a provisional id).</summary>
    internal static (bool Success, string Message) Update(int id, Action<Rtm> change)
    {
        if (Queries.Get(id) is not WorkItem item) return (false, NotFound);
        string yaml = Queries.Yaml(item.Path);
        Rtm meta = Metadata.Read<Rtm>(yaml);
        meta.Id ??= item.Id;
        change(meta);
        (bool ok, string message) = Metadata.Write(yaml, meta);
        return ok ? SyncSkipSpecs(item.Path) : (ok, message);
    }

    /// <summary>
    /// OpenSpec rejects a change with no delta specs unless .openspec.yaml says <c>skip_specs: true</c>, and rejects
    /// that flag once deltas exist. This is the one CLI key the app maintains: it mirrors whether specs/ has deltas.
    /// </summary>
    internal static (bool Success, string Message) SyncSkipSpecs(string dir)
    {
        string yaml = Queries.Yaml(dir);
        string text = OpenSpecStoreApi.ReadText(yaml);
        bool hasDeltas = Queries.DeltaCapabilities(dir).Any();
        Match line = SkipLine().Match(text);
        string updated = (hasDeltas, line.Success) switch
        {
            (false, false) => SchemaLine().Match(text) is { Success: true } s ? text.Insert(s.Index + s.Length, "skip_specs: true\n") : "skip_specs: true\n" + text,
            (true, true) => text.Remove(line.Index, line.Length),
            _ => text,
        };
        return updated == text ? (true, "") : OpenSpecStoreApi.WriteText(yaml, updated);
    }

    [GeneratedRegex(@"^skip_specs:.*\r?\n?", RegexOptions.Multiline)]
    private static partial Regex SkipLine();

    [GeneratedRegex(@"^schema:.*\r?\n", RegexOptions.Multiline)]
    private static partial Regex SchemaLine();

    internal static (bool Success, string Message) SetStatus(int id, string status)
    {
        if (!WorkItemsApi.Statuses.Contains(status)) return (false, $"Unknown status \"{status}\"");
        if (Queries.Get(id) is not WorkItem item) return (false, NotFound);
        if (item.Archived) return item.Status == status ? (true, "") : (false, Archived);
        if (status != "Blocked" && (item.DependsOn ?? []).Select(Queries.Get).OfType<WorkItem>().FirstOrDefault(d => !WorkItemsApi.IsResolved(d)) is WorkItem dep)
            return (false, $"Waiting on #wi{dep.Id} \"{dep.Title}\" — can't change status until it's resolved");
        if (status != "Completed") return Update(id, m => m.Status = status);

        // Completed = archived. Stamp the number first so the archived copy keeps it.
        (bool ok, string message) = Update(id, _ => { });
        if (!ok) return (ok, message);
        OpenSpecStoreApi.MarkOwnChange(item.Path);
        (bool archived, string error) = WorkItemsModule.Archive(item.Folder);
        OpenSpecStoreApi.MarkOwnChange(item.Path);   // the CLI moved folders: invalidate cached reads
        return (archived, error);
    }

    internal static (bool Success, string Message) SetManualFeatures(int id, IEnumerable<string> featurePaths)
    {
        if (Queries.Get(id) is not WorkItem item) return (false, NotFound);
        HashSet<string> locked = item.Features.Where(f => f.Locked).Select(f => f.Path).ToHashSet();
        return Update(id, m => m.Features = featurePaths.Where(p => !locked.Contains(p)).Distinct().ToList());
    }

    /// <summary>Deletes a custom feature and unlinks it from active work items (they become unassigned). Archived items are left as they are.</summary>
    internal static (bool Success, string Message) DeleteFeature(string path)
    {
        List<WorkItem> linked = Queries.GetAll(includeHidden: true).Where(i => !i.Archived && i.Features.Any(f => f.Path == path)).ToList();
        if (linked.FirstOrDefault(i => i.Features.Any(f => f.Path == path && f.Locked)) is WorkItem specced)
            return (false, $"#wi{specced.Id} has a spec for it in its specs/ folder");
        (bool ok, string message) = Features.FeaturesApi.DeleteFeature(path);   // refuses spec-backed features before any item is touched
        if (!ok) return (false, message);
        foreach (WorkItem it in linked)
            if (SetManualFeatures(it.Id, it.Features.Where(f => !f.Locked && f.Path != path).Select(f => f.Path)) is { Success: false } failed) return failed;
        return (true, "");
    }

    internal static (bool Success, string Message) SetDescription(int id, string body)
    {
        if (Queries.Get(id) is not WorkItem item) return (false, NotFound);
        string path = Path.Combine(item.Path, "proposal.md");
        return OpenSpecStoreApi.WriteText(path, Proposal.WithBody(OpenSpecStoreApi.ReadText(path), body));
    }

    internal static (bool Success, string Message) SetSubtaskDone(int id, string number, bool done) =>
        EditTasks(id, text => TasksFile.SetDone(text, number, done));

    internal static (bool Success, string Message) AddSubtask(int id, string text) =>
        string.IsNullOrWhiteSpace(text) ? (false, "Type the subtask first") : EditTasks(id, t => TasksFile.Append(t, text.Trim()));

    private static (bool Success, string Message) EditTasks(int id, Func<string, string> edit)
    {
        if (Queries.Get(id) is not WorkItem item) return (false, NotFound);
        string path = Path.Combine(item.Path, "tasks.md");
        return OpenSpecStoreApi.WriteText(path, edit(OpenSpecStoreApi.ReadText(path)));
    }

    internal static (bool Success, string Message, int Id) Create(string title, string status, IEnumerable<string>? features, IEnumerable<string>? contributors) =>
        CreateFolder(title, status, features, contributors, priority: null, proposalBody: "## Why\n\n\n\n## What Changes\n\n- \n", tasks: "# Tasks\n\n## 1. Tasks\n\n", deltas: new Dictionary<string, string>());

    private static (bool Success, string Message, int Id) CreateFolder(
        string title, string status, IEnumerable<string>? features, IEnumerable<string>? contributors, string? priority,
        string proposalBody, string tasks, IReadOnlyDictionary<string, string> deltas, string? folder = null, bool inArchive = false)
    {
        if (OpenSpecStoreApi.OpenSpecRoot is not string root) return (false, "No repository is open", 0);
        if (string.IsNullOrWhiteSpace(title)) return (false, "Give the work item a title", 0);
        StampProvisionalIds();
        int id = Queries.NextId();
        string basePath = inArchive ? Path.Combine("changes", "archive") : "changes";
        string slug = folder ?? Proposal.Slug(title);
        string baseName = inArchive ? $"{DateTime.Today:yyyy-MM-dd}-{slug}" : slug;
        string name = baseName;
        for (int n = 2; Directory.Exists(Path.Combine(root, basePath, name)); n++) name = $"{baseName}-{n}";
        string dir = Path.Combine(root, basePath, name);

        Rtm meta = new()
        {
            Id = id, Title = title.Trim(), Status = status == "Completed" ? "Backlog" : status, Priority = priority,
            Contributors = contributors?.Distinct().ToList(), Features = features?.Distinct().ToList(),
        };
        string yaml = Queries.Yaml(dir);
        foreach ((string path, string text) in new[]
        {
            (yaml, $"schema: spec-driven\ncreated: {DateTime.Today:yyyy-MM-dd}\n"),
            (Path.Combine(dir, "proposal.md"), Proposal.WithBody("", proposalBody)),
            (Path.Combine(dir, "tasks.md"), tasks),
        }.Concat(deltas.Select(d => (Path.Combine(dir, "specs", d.Key.Replace('/', Path.DirectorySeparatorChar), "spec.md"), d.Value))))
        {
            (bool ok, string message) = OpenSpecStoreApi.WriteText(path, text);
            if (!ok) return (false, message, 0);
        }
        (bool written, string error) = Metadata.Write(yaml, meta);
        if (written) (written, error) = SyncSkipSpecs(dir);
        return written ? (true, "", id) : (false, error, 0);
    }

    /// <summary>
    /// Provisional numbers are "highest stored + n", so storing a new number would shift them. Before allocating,
    /// write every provisional number into its change so links like #wi125 never start pointing elsewhere.
    /// </summary>
    private static void StampProvisionalIds()
    {
        foreach (WorkItem w in Queries.GetAll(includeHidden: true))
        {
            string yaml = Queries.Yaml(w.Path);
            Rtm meta = Metadata.Read<Rtm>(yaml);
            if (meta.Id is not null) continue;
            meta.Id = w.Id;
            Metadata.Write(yaml, meta);
        }
    }

    internal static (bool Success, string Message, int NewId) MoveSubtasksToNew(int id, IReadOnlyCollection<string> numbers)
    {
        if (Queries.Get(id) is not WorkItem item) return (false, NotFound, 0);
        if (item.Archived) return (false, Archived, 0);
        List<Subtask> moved = item.Subtasks.Where(s => numbers.Contains(s.Number)).ToList();
        if (moved.Count == 0) return (false, "Select the subtasks to move first", 0);

        (bool ok, string message, int newId) = CreateFolder($"Split from #wi{id}", item.Status, item.Features.Select(f => f.Path), item.Contributors, null,
            $"## Why\n\nSplit out of #wi{id} {item.Title}.\n\n## What Changes\n\n- {string.Join("\n- ", moved.Select(s => s.Text))}\n", TasksFile.Create(moved), deltas: new Dictionary<string, string>());
        if (!ok) return (ok, message, 0);
        (ok, message) = EditTasks(id, text => TasksFile.Remove(text, numbers));
        return (ok, message, newId);
    }

    /// <summary>Like MoveSubtasksToNew, but the new item becomes a dependency: the source is blocked until it is archived.</summary>
    internal static (bool Success, string Message, int NewId) MoveSubtasksToDependency(int id, IReadOnlyCollection<string> numbers)
    {
        (bool ok, string message, int newId) = MoveSubtasksToNew(id, numbers);
        if (!ok) return (ok, message, 0);
        (ok, message) = Update(id, m => { m.DependsOn = [.. m.DependsOn ?? [], newId]; m.Status = "Blocked"; });
        return ok ? (true, "", newId) : (false, message, 0);
    }

    /// <summary>Makes an existing item a dependency of this one; this one is blocked only while that item is unresolved.</summary>
    internal static (bool Success, string Message) AddDependency(int id, int dependencyId)
    {
        if (Queries.Get(id) is not WorkItem item) return (false, NotFound);
        if (item.Archived) return (false, Archived);
        if (dependencyId == id) return (false, "A work item can't depend on itself");
        if (Queries.Get(dependencyId) is not WorkItem dep) return (false, $"No work item #wi{dependencyId}");
        if ((item.DependsOn ?? []).Contains(dependencyId)) return (false, $"Already waiting on #wi{dep.Id}");
        if ((dep.DependsOn ?? []).Contains(id)) return (false, $"#wi{dep.Id} already waits on #wi{id}, so they can't wait on each other");
        bool blocks = !WorkItemsApi.IsResolved(dep);
        return Update(id, m => { m.DependsOn = [.. m.DependsOn ?? [], dependencyId]; if (blocks) m.Status = "Blocked"; });
    }

    internal static (bool Success, string Message) SetDependencies(int id, IEnumerable<int> ids) =>
        Update(id, m => m.DependsOn = ids.Distinct().ToList());

    internal static (bool Success, string Message, int Id) Merge(IReadOnlyList<int> ids)
    {
        List<WorkItem> items = ids.Select(Queries.Get).OfType<WorkItem>().ToList();
        if (items.Count < 2) return (false, "Select at least two work items to merge", 0);
        bool allArchived = items.All(i => i.Archived);

        // Delta specs: refuse when two items change the same requirement; otherwise concatenate per capability.
        // Archived items have no deltas (already merged into specs/), so they contribute nothing here.
        Dictionary<string, string> deltas = [];
        Dictionary<(string Cap, string Req), int> owner = [];
        foreach (WorkItem it in items)
        foreach (string cap in Queries.DeltaCapabilities(it.Path))
        {
            string text = OpenSpecStoreApi.ReadText(Path.Combine(it.Path, "specs", cap.Replace('/', Path.DirectorySeparatorChar), "spec.md"));
            foreach (string req in Proposal.RequirementNames(text))
            {
                if (owner.TryGetValue((cap, req), out int other) && other != it.Id)
                    return (false, $"#wi{other} and #wi{it.Id} both change requirement \"{req}\" in {cap}", 0);
                owner[(cap, req)] = it.Id;
            }
            // ponytail: plain concatenation can repeat "## ADDED Requirements" headers; OpenSpec accepts it, tidy by hand if wanted.
            deltas[cap] = deltas.TryGetValue(cap, out string? prev) ? prev.TrimEnd() + "\n\n" + text : text;
        }

        string status = items.FirstOrDefault(i => !i.Archived)?.Status ?? items[0].Status;
        string body = string.Join("\n\n", items.Select(it => $"<!-- merged from #wi{it.Id} {it.Title} -->\n{Queries.GetDescription(it.Id).Trim()}")) + "\n";
        string tasks = TasksFile.Create(items.SelectMany(it => it.Subtasks));
        (bool ok, string message, int id) = CreateFolder(
            string.Join(" + ", items.Select(i => i.Title)), status,
            items.SelectMany(i => i.Features.Where(f => !f.Locked).Select(f => f.Path)),
            items.SelectMany(i => i.Contributors),
            items.Any(i => i.Priority == "high") ? "high" : null,
            body, tasks, deltas, folder: "merge-" + string.Join("-", items.Select(i => $"wi{i.Id}")), inArchive: allArchived);
        if (!ok) return (ok, message, 0);
        foreach (WorkItem it in items) OpenSpecStoreApi.DeleteFolder(it.Path);
        return (true, "", id);
    }

    internal static (bool Success, string Message) EditLanes(string column, Action<List<string>> edit)
    {
        if (OpenSpecStoreApi.OpenSpecRoot is not string root) return (false, "No repository is open");
        string yaml = Path.Combine(root, "config.yaml");
        Dictionary<string, List<string>> all = Metadata.ReadSection<Dictionary<string, List<string>>>(yaml, "lanes") ?? [];
        List<string> lanes = all.TryGetValue(column, out List<string>? l) ? l : all[column] = [];
        edit(lanes);
        if (lanes.Count == 0) all.Remove(column);
        return Metadata.WriteSection(yaml, "lanes", all.Count == 0 ? null : all);
    }
}
