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
        Rtm meta;
        try { meta = Metadata.Read<Rtm>(yaml); }
        catch (MetadataException e) { return (false, item.Warning ?? e.Message); }   // a yaml that does not parse is never written over: it is the user's to fix
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

    private static string Today() => DateOnly.FromDateTime(DateTime.Today).ToString("yyyy-MM-dd");

    /// <summary>Who an event is recorded as: the configured actor, trimmed, else "someone".</summary>
    private static string Actor()
    {
        try { return WorkItemsModule.Actor?.Invoke()?.Trim() is { Length: > 0 } name ? name : "someone"; }
        catch (Exception) { return "someone"; }   // a host callback must not fail a command
    }

    /// <summary>Records events in the item's change folder after a command succeeded. A log that can't be written never changes the command's result.</summary>
    private static void Log(int id, params (string Kind, string Text)[] events)
    {
        if (events.Length > 0 && Queries.PathOf(id) is string path) EventLog.Append(path, Actor(), events);
    }

    /// <summary>One event per entry that is in <paramref name="after"/> but not <paramref name="before"/> (linked), and per entry the other way round (unlinked).</summary>
    private static (string Kind, string Text)[] Diff(string noun, IEnumerable<string> before, IEnumerable<string> after, string addedKind = "linked", string removedKind = "unlinked") =>
        [.. before.Except(after).Select(t => ($"{noun}-{removedKind}", t)), .. after.Except(before).Select(t => ($"{noun}-{addedKind}", t))];

    internal static (bool Success, string Message) LogEvent(int id, string kind, string text)
    {
        if (string.IsNullOrWhiteSpace(kind)) return (false, "An event needs a kind");
        if (Queries.Get(id) is not WorkItem item) return (false, NotFound);
        return EventLog.Append(item.Path, Actor(), [(kind.Trim(), text ?? "")]);
    }

    internal static (bool Success, string Message) SetLinkedCommits(int id, IEnumerable<string> hashes)
    {
        List<string> now = hashes.Distinct().ToList(), before = [];
        (bool ok, string message) = Update(id, m => { before = m.Commits ?? []; m.Commits = now; });
        if (ok) Log(id, Diff("commit", before, now));
        return (ok, message);
    }

    internal static (bool Success, string Message) SetLinkedTests(int id, IEnumerable<TestLink> tests)
    {
        List<RtmTest> now = tests.Distinct().Select(t => new RtmTest { Suite = t.Suite, Test = t.Test, Note = t.Note }).ToList();
        List<string> before = [];
        (bool ok, string message) = Update(id, m => { before = (m.Tests ?? []).Select(TestName).ToList(); m.Tests = now; });
        if (ok) Log(id, Diff("test", before, now.Select(TestName).Distinct().ToList()));
        return (ok, message);
    }

    private static string TestName(RtmTest t) => $"{t.Suite} / {t.Test}";

    internal static (bool Success, string Message) SetLinkedDocs(int id, IEnumerable<string> paths)
    {
        List<string> now = paths.Select(p => (p ?? "").Replace('\\', '/').Trim()).Where(p => p.Length > 0).Distinct().ToList();
        if (now.FirstOrDefault(p => Path.IsPathRooted(p) || p.StartsWith('/') || (p.Length > 1 && p[1] == ':') || p.Split('/').Contains("..")) is string bad)
            return (false, $"A document is a path inside the repository, like docs/guide.md (not \"{bad}\")");
        List<string> before = [];
        (bool ok, string message) = Update(id, m => { before = m.Docs ?? []; m.Docs = now; });
        if (ok) Log(id, Diff("doc", before, now));
        return (ok, message);
    }

    internal static (bool Success, string Message) SetStatus(int id, string status)
    {
        if (!WorkItemsApi.Statuses.Contains(status)) return (false, $"Unknown status \"{status}\"");
        if (Queries.Get(id) is not WorkItem item) return (false, NotFound);
        if (item.Archived) return item.Status == status ? (true, "") : (false, Archived);
        if (status != "Blocked" && (item.DependsOn ?? []).Select(Queries.Get).OfType<WorkItem>().FirstOrDefault(d => !WorkItemsApi.IsResolved(d)) is WorkItem dep)
            return (false, $"Waiting on #wi{dep.Id} \"{dep.Title}\" — can't change status until it's resolved");
        if (status != "Completed")   // the first move to In Progress stamps the start day; a day already there is kept
        {
            (bool changed, string reason) = Update(id, m => { m.Status = status; if (item.Status != status) m.Moved = Today(); if (status == "In Progress") m.Started ??= DateOnly.FromDateTime(DateTime.Today).ToString("yyyy-MM-dd"); });
            if (changed && item.Status != status) Log(id, ("status", $"{item.Status} -> {status}"));
            return (changed, reason);
        }

        // Completed = archived. Stamp the number first so the archived copy keeps it.
        (bool ok, string message) = Update(id, m => { if (item.Status != status) m.Moved = Today(); });
        if (!ok) return (ok, message);
        if (RefreshDeltas(item.Path) is { Success: false } stale) return stale;   // before the CLI sees them
        string folder = item.Folder;
        if (Directory.Exists(Path.Combine(OpenSpecStoreApi.OpenSpecRoot!, "changes", "archive", $"{DateTime.Today:yyyy-MM-dd}-{folder}")))
        {   // the CLI names the archive <today>-<folder> and refuses to overwrite: take a free name first
            string free = FreeFolderName(folder);
            if (OpenSpecStoreApi.MoveFolder(item.Path, Path.Combine(OpenSpecStoreApi.OpenSpecRoot!, "changes", free)) is { Success: false } renamed) return renamed;
            folder = free;
        }
        OpenSpecStoreApi.MarkOwnChange(item.Path);
        // The CLI may refuse (validation, a spec it can't merge). The app never shows that: it archives with fewer checks, then without merging specs.
        string[][] rungs = [[], ["--no-validate"], ["--no-validate", "--skip-specs"]];
        string first = "";
        bool archived = false;
        for (int i = 0; i < rungs.Length && !archived; i++)
        {
            (archived, string error, bool couldRun) = WorkItemsModule.Archive(folder, rungs[i]);
            if (i == 0 || !couldRun) first = error;
            if (!archived && !couldRun) break;
            if (archived && i == 2) NoteSpecsNotMerged(folder, first);
        }
        OpenSpecStoreApi.MarkOwnChange(item.Path);   // the CLI moved folders: invalidate cached reads
        if (archived) Log(id, ("status", $"{item.Status} -> {status}"));   // into the archived folder, where the item lives now
        return archived ? (true, "") : (false, first);
    }

    /// <summary>The CLI archived the change only with --skip-specs: records in a note of the archived folder why its specs weren't merged.</summary>
    private static void NoteSpecsNotMerged(string folder, string reason)
    {
        string archive = Path.Combine(OpenSpecStoreApi.OpenSpecRoot!, "changes", "archive");
        string? dir = Directory.Exists(archive)
            ? Directory.EnumerateDirectories(archive).Where(d => Path.GetFileName(d) == folder || DatePrefix().Replace(Path.GetFileName(d), "") == folder).OrderBy(d => d, StringComparer.Ordinal).LastOrDefault()
            : null;
        if (dir is null) return;
        int noteId = NextNoteId();
        string created = DateTime.Now.ToString("s", System.Globalization.CultureInfo.InvariantCulture);
        string why = AnsiCode().Replace(reason, "").Trim();
        string note = $"---\nid: {noteId}\nby: App\ncreated: {created}\n---\n# Archived without merging its specs\n\n"
            + "The OpenSpec CLI refused to merge this item's specs into `openspec/specs`, so the item was archived with its specs left in its own `specs/` folder. The CLI said:\n\n"
            + $"```\n{why}\n```\n";
        OpenSpecStoreApi.WriteText(Path.Combine(dir, "notes", $"n{noteId}.md"), note);
        OpenSpecStoreApi.MarkOwnChange(dir);
    }

    [GeneratedRegex(@"\x1B\[[0-9;]*[A-Za-z]")]
    private static partial Regex AnsiCode();

    /// <summary>Puts back into each MODIFIED requirement of the change's delta specs the scenarios the main spec has gained since (see DeltaSpec).</summary>
    private static (bool Success, string Message) RefreshDeltas(string changeDir)
    {
        foreach (string cap in Queries.DeltaCapabilities(changeDir))
        {
            string rel = Path.Combine("specs", cap.Replace('/', Path.DirectorySeparatorChar), "spec.md");
            string main = Path.Combine(OpenSpecStoreApi.OpenSpecRoot!, rel);
            string path = Path.Combine(changeDir, rel);   // a main spec that doesn't exist yet is an empty one
            string delta = OpenSpecStoreApi.ReadText(path), refreshed = DeltaSpec.Refresh(delta, File.Exists(main) ? OpenSpecStoreApi.ReadText(main) : "");
            if (refreshed != delta && OpenSpecStoreApi.WriteText(path, refreshed) is { Success: false } failed) return failed;
        }
        return (true, "");
    }

    /// <summary>
    /// Moves an item to one feature (null = none): it becomes the item's only feature. Features from the change's own specs/
    /// folder are unlinked (x-rtm.unlinkedFeatures) rather than removed: the spec stays, and moving back links it again.
    /// </summary>
    internal static (bool Success, string Message) MoveFeature(int id, string? to)
    {
        if (Queries.Get(id) is not WorkItem item) return (false, NotFound);
        List<string> specced = Queries.DeltaCapabilities(item.Path).ToList();   // all of them: only the first shows as the item's feature
        return Update(id, m =>
        {
            m.UnlinkedFeatures = specced.Where(p => p != to).Distinct().ToList();
            m.Features = to is null || specced.Contains(to) ? [] : [to];
        });
    }

    /// <summary>Open (not archived) work items whose change has a delta spec folder for the feature.</summary>
    internal static IReadOnlyList<int> FeatureSpecOwners(string path) =>
        Queries.GetAll(includeHidden: true).Where(i => !i.Archived && Queries.DeltaCapabilities(i.Path).Contains(path)).Select(i => i.Id).ToList();

    /// <summary>
    /// Deletes a feature and unlinks it from active work items (they become unassigned). Open changes' delta spec folders for it
    /// are deleted too (a change left with no deltas gets skip_specs); archived items are left as they are.
    /// </summary>
    internal static (bool Success, string Message) DeleteFeature(string path)
    {
        List<WorkItem> linked = Queries.GetAll(includeHidden: true).Where(i => !i.Archived && (Metadata.TryRead<Rtm>(Queries.Yaml(i.Path), out _).Features ?? []).Contains(path)).ToList();
        List<WorkItem> specced = Queries.GetAll(includeHidden: true).Where(i => !i.Archived && Queries.DeltaCapabilities(i.Path).Contains(path)).ToList();
        string main = Path.Combine(OpenSpecStoreApi.OpenSpecRoot!, "specs", path.Replace('/', Path.DirectorySeparatorChar));
        if (Directory.Exists(main) && Directory.EnumerateFiles(main, "spec.md", SearchOption.AllDirectories).Count() > 1)
            return (false, $"{Features.FeaturesApi.GetAll().FirstOrDefault(f => f.Path == path)?.Name ?? path}'s folder holds other features; delete those first");   // before anything is deleted
        foreach (WorkItem it in specced)
            if (DeleteDelta(it.Path, path) is { Success: false } gone) return gone;
        (bool ok, string message) = Features.FeaturesApi.DeleteFeature(path);
        if (!ok) return (false, message);
        foreach (WorkItem it in linked)
            if (Update(it.Id, m => m.Features = (m.Features ?? []).Where(p => p != path).ToList()) is { Success: false } failed) return failed;
        return (true, "");
    }

    /// <summary>Deletes the change's specs/&lt;path&gt;/ folder, then any parent folders left empty (specs/ included), and re-syncs skip_specs.</summary>
    private static (bool Success, string Message) DeleteDelta(string changeDir, string path)
    {
        string specs = Path.Combine(changeDir, "specs"), dir = Path.Combine(specs, path.Replace('/', Path.DirectorySeparatorChar));
        (bool ok, string message) = OpenSpecStoreApi.DeleteFolder(dir);
        if (!ok) return (ok, message);
        for (string? up = Path.GetDirectoryName(dir); up is not null && up.StartsWith(specs) && Directory.Exists(up) && !Directory.EnumerateFileSystemEntries(up).Any(); up = Path.GetDirectoryName(up))
            if (OpenSpecStoreApi.DeleteFolder(up) is { Success: false } failed) return failed;
        return SyncSkipSpecs(changeDir);
    }

    /// <summary>Renames a feature and carries every work item along: delta spec folders for it move, x-rtm links are renamed.</summary>
    internal static (bool Success, string Message, string? NewPath) RenameFeature(string path, string name)
    {
        List<WorkItem> items = Queries.GetAll(includeHidden: true).ToList();
        (bool ok, string message, string? newPath) = Features.FeaturesApi.RenameFeature(path, name);
        if (!ok || newPath == path) return (ok, message, newPath);
        string Rel(string p) => p.Replace('/', Path.DirectorySeparatorChar);
        List<string> Swap(List<string> list) => list.Select(p => p == path ? newPath! : p).ToList();
        foreach (WorkItem it in items)
        {
            string delta = Path.Combine(it.Path, "specs", Rel(path));
            if (Directory.Exists(delta) && OpenSpecStoreApi.MoveFolder(delta, Path.Combine(it.Path, "specs", Rel(newPath!))) is { Success: false } moveFailed) return (false, moveFailed.Message, null);
            if (Metadata.TryRead<Rtm>(Queries.Yaml(it.Path), out _) is { } meta && ((meta.Features ?? []).Contains(path) || (meta.UnlinkedFeatures ?? []).Contains(path)))
                if (Update(it.Id, m => { if (m.Features is not null) m.Features = Swap(m.Features); if (m.UnlinkedFeatures is not null) m.UnlinkedFeatures = Swap(m.UnlinkedFeatures); }) is { Success: false } failed) return (false, failed.Message, null);
        }
        return (true, "", newPath);
    }

    internal static (bool Success, string Message) SetDescription(int id, string body)
    {
        if (Queries.Get(id) is not WorkItem item) return (false, NotFound);
        string path = Path.Combine(item.Path, "proposal.md");
        return OpenSpecStoreApi.WriteText(path, Proposal.WithBody(OpenSpecStoreApi.ReadText(path), body));
    }

    internal static (bool Success, string Message) SetSubtaskDone(int id, string number, bool done)
    {
        Subtask? before = Queries.Get(id)?.Subtasks.FirstOrDefault(s => s.Number == number && !s.Number.StartsWith('#'));
        (bool ok, string message) = EditTasks(id, text => TasksFile.SetDone(text, number, done));
        if (ok && before is not null && before.Done != done) Log(id, (done ? "subtask-done" : "subtask-reopened", $"{number} {before.Text}"));
        return (ok, message);
    }

    internal static (bool Success, string Message) SetSubtaskText(int id, string number, string text) =>
        string.IsNullOrWhiteSpace(text) ? (false, "A subtask needs some text") : EditTasks(id, t => TasksFile.SetText(t, number, text.Trim()));

    internal static (bool Success, string Message) MarkSubtaskReviewed(int id, string number) =>
        EditTasks(id, text => TasksFile.ClearAgent(text, number));

    /// <summary>The user has looked at what an agent did to this item: ReviewedAt = now. The agent's own stamp stays.</summary>
    internal static (bool Success, string Message) MarkReviewed(int id) =>
        Update(id, m => m.ReviewedAt = ReviewStamp(m.AgentAt));

    /// <summary>Now, or one second after the agent's own time when its clock was ahead (UTC, another zone): a review must come after what it reviews.</summary>
    internal static string ReviewStamp(string? agentAt)
    {
        DateTime at = DateTime.Now;
        if (DateTime.TryParse(agentAt, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out DateTime a) && a >= at) at = a.AddSeconds(1);
        return at.ToString("s", System.Globalization.CultureInfo.InvariantCulture);
    }

    internal static (bool Success, string Message) AddSubtask(int id, string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return (false, "Type the subtask first");
        HashSet<string> had = [.. Queries.Get(id)?.Subtasks.Select(s => s.Number) ?? []];
        (bool ok, string message) = EditTasks(id, t => TasksFile.Append(t, text.Trim()));
        if (ok && Queries.Get(id)?.Subtasks.LastOrDefault(s => !had.Contains(s.Number)) is Subtask added) Log(id, ("subtask-added", $"{added.Number} {added.Text}"));
        return (ok, message);
    }

    private static (bool Success, string Message) EditTasks(int id, Func<string, string> edit)
    {
        if (Queries.Get(id) is not WorkItem item) return (false, NotFound);
        string path = Path.Combine(item.Path, "tasks.md");
        return OpenSpecStoreApi.WriteText(path, edit(OpenSpecStoreApi.ReadText(path)));
    }

    internal static (bool Success, string Message, int Id) Create(string title, string status, IEnumerable<string>? features, IEnumerable<string>? contributors) =>
        CreateFolder(title, status, features, contributors, priority: null, proposalBody: "## Why\n\n\n\n## What Changes\n\n- \n", tasks: "# Tasks\n\n## 1. Tasks\n\n", deltas: new Dictionary<string, string>(), more: m => m.Moved = Today());

    /// <summary>
    /// A folder name no change has ever had: not under changes/ and not under changes/archive/ either (there with its
    /// yyyy-MM-dd- prefix stripped, any date). Numbers duplicates: name, name-2, name-3, ...
    /// </summary>
    private static string FreeFolderName(string baseName)
    {
        string changes = Path.Combine(OpenSpecStoreApi.OpenSpecRoot!, "changes"), archive = Path.Combine(changes, "archive");
        HashSet<string> used = new(StringComparer.OrdinalIgnoreCase);
        if (Directory.Exists(changes)) used.UnionWith(Directory.EnumerateDirectories(changes).Select(d => Path.GetFileName(d)));
        if (Directory.Exists(archive)) used.UnionWith(Directory.EnumerateDirectories(archive).Select(d => DatePrefix().Replace(Path.GetFileName(d), "")));
        string name = baseName;
        for (int n = 2; used.Contains(name); n++) name = $"{baseName}-{n}";
        return name;
    }

    [GeneratedRegex(@"^\d{4}-\d{2}-\d{2}-")]
    private static partial Regex DatePrefix();

    private static (bool Success, string Message, int Id) CreateFolder(
        string title, string status, IEnumerable<string>? features, IEnumerable<string>? contributors, string? priority,
        string proposalBody, string tasks, IReadOnlyDictionary<string, string> deltas, string? folder = null, bool inArchive = false, Action<Rtm>? more = null)
    {
        if (OpenSpecStoreApi.OpenSpecRoot is not string root) return (false, "No repository is open", 0);
        if (string.IsNullOrWhiteSpace(title)) return (false, "Give the work item a title", 0);
        StampProvisionalIds();
        int id = OpenSpecStoreApi.Allocate("work-item", roots => roots.Max(r => Queries.MaxIdUnder(r)));
        string basePath = inArchive ? Path.Combine("changes", "archive") : "changes";
        string slug = FreeFolderName(folder ?? Proposal.Slug(title));
        string name = inArchive ? $"{DateTime.Today:yyyy-MM-dd}-{slug}" : slug;
        string dir = Path.Combine(root, basePath, name);

        Rtm meta = new()
        {
            Id = id, Title = title.Trim(), Status = status == "Completed" ? "Backlog" : status, Priority = priority,
            Contributors = contributors?.Distinct().ToList(), Features = features?.Distinct().ToList(),
        };
        more?.Invoke(meta);
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
        if (written) EventLog.Append(dir, Actor(), [("created", meta.Title!)]);
        return written ? (true, "", id) : (false, error, 0);
    }

    /// <summary>
    /// Provisional numbers are "highest stored + n", so storing a new number would shift them. Before allocating,
    /// write every provisional number into its change so links like #wi125 never start pointing elsewhere.
    /// </summary>
    /// <summary>
    /// Two changes carrying one number (created in two places, a copied folder, a merge): every one but the keeper takes the
    /// next free number. The keeper is an archived change first (its number is history), then the earliest created, then the
    /// first folder name, so every copy of the repository resolves the same way. References other items hold to the number
    /// stay as they are: they most likely meant the keeper. Returns what was renumbered.
    /// </summary>
    internal static IReadOnlyList<(int OldId, int NewId, string Folder)> ResolveDuplicateIds()
    {
        WorkItemsModule.DuplicateScans++;
        if (OpenSpecStoreApi.OpenSpecRoot is null) return [];
        StampProvisionalIds();
        List<(int, int, string)> renumbered = [];
        foreach (IGrouping<int, WorkItem> twins in Queries.GetAll(includeHidden: true).GroupBy(w => w.Id).Where(g => g.Count() > 1).ToList())
        {
            foreach (WorkItem w in twins.OrderBy(w => w.Archived ? 0 : 1).ThenBy(w => w.Created).ThenBy(w => w.Folder, StringComparer.Ordinal).Skip(1))
            {
                int newId = OpenSpecStoreApi.Allocate("work-item", roots => roots.Max(r => Queries.MaxIdUnder(r)));
                string yaml = Queries.Yaml(w.Path);
                Rtm meta = Metadata.TryRead<Rtm>(yaml, out string? problem);
                if (problem is not null) continue;   // does not parse: left as it is until fixed, and the warning stays
                meta.Id = newId;
                if (Metadata.Write(yaml, meta).Success) renumbered.Add((w.Id, newId, w.Folder));
            }
        }
        return renumbered;
    }

    private static void StampProvisionalIds()
    {
        foreach (WorkItem w in Queries.WithoutStoredId())   // only those: reading every item's file for a number it already has was most of this scan's cost
        {
            string yaml = Queries.Yaml(w.Path);
            Rtm meta = Metadata.TryRead<Rtm>(yaml, out string? problem);
            if (problem is not null || meta.Id is not null) continue;
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

        // one subtask becomes the item it describes, so its text is the title; several get a title naming where they came from
        string title = moved is [Subtask only] ? only.Text : $"Split from #wi{id}";
        (bool ok, string message, int newId) = CreateFolder(title, item.Status, item.Features.Select(f => f.Path), item.Contributors, null,
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
        string was = Queries.Get(id)?.Status ?? "";
        (ok, message) = Update(id, m => { m.DependsOn = [.. m.DependsOn ?? [], newId]; m.Status = "Blocked"; });
        if (ok) Log(id, DependencyAdded(newId, was, blocked: true));
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
        (bool ok, string message) = Update(id, m => { m.DependsOn = [.. m.DependsOn ?? [], dependencyId]; if (blocks) m.Status = "Blocked"; });
        if (ok) Log(id, DependencyAdded(dependencyId, item.Status, blocks));
        return (ok, message);
    }

    /// <summary>The dependency, then the move to Blocked it caused (when the item was not Blocked already).</summary>
    private static (string Kind, string Text)[] DependencyAdded(int dependencyId, string was, bool blocked) =>
        blocked && was != "Blocked" ? [("dependency-added", $"#wi{dependencyId}"), ("status", $"{was} -> Blocked")] : [("dependency-added", $"#wi{dependencyId}")];

    internal static (bool Success, string Message) SetDependencies(int id, IEnumerable<int> ids)
    {
        List<int> now = ids.Distinct().ToList(), before = [];
        (bool ok, string message) = Update(id, m => { before = m.DependsOn ?? []; m.DependsOn = now; });
        if (ok) Log(id, Diff("dependency", before.Select(d => $"#wi{d}"), now.Select(d => $"#wi{d}"), "added", "removed"));
        return (ok, message);
    }

    internal static (bool Success, string Message, int Id) Merge(IReadOnlyList<int> ids) => Merge(ids, ids.Count > 0 ? ids[0] : 0);

    /// <summary>Merges the items into one that keeps the title and description of <paramref name="into"/>; the other descriptions become notes on it.</summary>
    internal static (bool Success, string Message, int Id) Merge(IReadOnlyList<int> ids, int into)
    {
        List<WorkItem> items = ids.Select(Queries.Get).OfType<WorkItem>().ToList();
        if (items.Count < 2) return (false, "Select at least two work items to merge", 0);
        if (items.FirstOrDefault(i => i.Id == into) is not WorkItem main) return (false, $"#wi{into} is not one of the work items to merge", 0);
        bool allArchived = items.All(i => i.Archived);

        // Delta specs: refuse when two active items change the same requirement; otherwise concatenate per capability.
        // An archived item keeps its specs/ folder, but they are already merged into openspec/specs: they can't conflict, and
        // putting them in an active change would apply them twice when it is archived. So they join the merged change only when
        // it is archived too; otherwise they are kept under from-wi<id>/specs/ (see CopyExtras).
        Dictionary<string, string> deltas = [];
        Dictionary<(string Cap, string Req), int> owner = [];
        foreach (WorkItem it in items.Where(i => !i.Archived || allArchived))
        foreach (string cap in Queries.DeltaCapabilities(it.Path))
        {
            string text = OpenSpecStoreApi.ReadText(Path.Combine(it.Path, "specs", cap.Replace('/', Path.DirectorySeparatorChar), "spec.md"));
            foreach (string req in it.Archived ? [] : Proposal.RequirementNames(text))
            {
                if (owner.TryGetValue((cap, req), out int other) && other != it.Id)
                    return (false, $"#wi{other} and #wi{it.Id} both change requirement \"{req}\" in {cap}", 0);
                owner[(cap, req)] = it.Id;
            }
            // ponytail: plain concatenation can repeat "## ADDED Requirements" headers; OpenSpec accepts it, tidy by hand if wanted.
            deltas[cap] = deltas.TryGetValue(cap, out string? prev) ? prev.TrimEnd() + "\n\n" + text : text;
        }

        string status = items.FirstOrDefault(i => !i.Archived)?.Status ?? items[0].Status;
        string body = Queries.GetDescription(main.Id).Trim() + "\n";
        // Task groups stay groups. The main item's keep their headings ("Title · Group" when it had several); each other source's
        // are headed "#wiN Title · merged yyyy-MM-dd" ("#wiN Title · Group · merged yyyy-MM-dd" when it had several), so the
        // Subtasks tile can tell what was merged in, and when. A group that already says where it came from is left as it is.
        string mergedOn = DateTime.Now.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        string tasks = TasksFile.CreateGrouped(items.SelectMany(it =>
            it.Subtasks.GroupBy(s => s.Group).ToList() is { Count: > 0 } groups
                ? groups.Select(g => (Heading: GroupHeading(it, g.Key, groups.Count > 1, it.Id == main.Id, mergedOn), Subtasks: (IReadOnlyList<Subtask>)g.ToList()))
                : []));
        HashSet<int> sources = [.. items.Select(i => i.Id)];
        (bool ok, string message, int id) = CreateFolder(
            main.Title, status,
            items.SelectMany(i => i.Features.Where(f => !f.Locked).Select(f => f.Path)),
            items.SelectMany(i => i.Contributors),
            items.Any(i => i.Priority == "high") ? "high" : null,
            body, tasks, deltas, folder: "merge-" + string.Join("-", items.Select(i => $"wi{i.Id}")), inArchive: allArchived,
            more: m => KeepLinks(m, items, sources));
        if (!ok) return (ok, message, 0);

        // Nothing but the folders is dropped: notes and any other files move over, and items that waited on a source now wait on this one.
        string merged = Queries.Get(id)!.Path;
        EventLog.Prepend(merged, EventLog.SortedLines(items.Select(i => i.Path)));   // every source's events, oldest first, then the merged item's own created
        foreach (WorkItem it in items) CopyExtras(it.Path, merged, it.Id, keepSpecs: it.Archived && !allArchived, stampNotes: it.Id == main.Id ? null : mergedOn);
        // Note numbers are repository-wide and WorkItems can't reference Notes (Notes references WorkItems), so the files are written here in the notes format.
        int noteId = NextNoteId();
        foreach (WorkItem it in items.Where(i => i.Id != main.Id))
        {
            string created = DateTime.Now.ToString("s", System.Globalization.CultureInfo.InvariantCulture);
            string note = $"---\nid: {noteId}\nby: Merge\ncreated: {created}\nmergedFrom: {it.Id}\nmergedAt: {mergedOn}\n---\n# Merged from #wi{it.Id} {it.Title}\n\n{Queries.GetDescription(it.Id).Trim()}\n";
            OpenSpecStoreApi.WriteText(Path.Combine(merged, "notes", $"n{noteId++}.md"), note);
        }
        foreach (WorkItem w in Queries.GetAll(includeHidden: true).Where(w => !sources.Contains(w.Id) && (w.DependsOn ?? []).Any(sources.Contains)).ToList())
            Update(w.Id, m => m.DependsOn = [.. m.DependsOn!.Select(d => sources.Contains(d) ? id : d).Distinct()]);
        foreach (WorkItem it in items) OpenSpecStoreApi.DeleteFolder(it.Path);
        return (true, "", id);
    }

    /// <summary>The highest note number over every notes/n*.md in the repository (archived changes included), plus one.</summary>
    private static int NextNoteId()
    {
        if (OpenSpecStoreApi.OpenSpecRoot is not string root || !Directory.Exists(Path.Combine(root, "changes"))) return 1;
        int max = 0;
        foreach (string file in Directory.EnumerateFiles(Path.Combine(root, "changes"), "n*.md", SearchOption.AllDirectories))
        {
            if (Path.GetFileName(Path.GetDirectoryName(file)) != "notes") continue;
            if (int.TryParse(Path.GetFileNameWithoutExtension(file)[1..], out int n)) max = Math.Max(max, n);
            if (Regex.Match(OpenSpecStoreApi.ReadText(file).Replace("\r\n", "\n"), @"\A---\n(?:.*\n)*?id:\s*(\d+)") is { Success: true } m && int.TryParse(m.Groups[1].Value, out int fm)) max = Math.Max(max, fm);
        }
        return max + 1;
    }

    private static string GroupName(string group) => Regex.Replace(group, @"^\d+\.\s*", "").Trim();

    /// <summary>The heading (without its number) of one of an item's task groups in the merged tasks.md; see Merge.</summary>
    private static string GroupHeading(WorkItem it, string group, bool several, bool isMain, string mergedOn)
    {
        string name = GroupName(group);
        if (Regex.IsMatch(name, @"^#wi\d+ .+ · merged \d{4}-\d{2}-\d{2}$")) return name;   // merged in by an earlier merge: it keeps saying so
        string title = isMain ? it.Title : $"#wi{it.Id} {it.Title}";
        string heading = several && name.Length > 0 ? $"{title} · {name}" : title;
        return isMain ? heading : $"{heading} · merged {mergedOn}";
    }

    /// <summary>
    /// Adds <c>mergedFrom</c> and <c>mergedAt</c> to a note's frontmatter, after the keys it has (they are never reordered or
    /// rewritten); a note that already says where it was merged in from keeps that. A note with no frontmatter gets one.
    /// </summary>
    internal static string StampMerged(string text, int from, string day)
    {
        string nl = text.Contains("\r\n") ? "\r\n" : "\n";
        string stamp = $"mergedFrom: {from}{nl}mergedAt: {day}{nl}";
        Match fm = Regex.Match(text, @"\A---[ \t]*\r?\n(?<keys>(?:.*\r?\n)*?)---[ \t]*(?:\r?\n|\z)");
        if (!fm.Success) return $"---{nl}{stamp}---{nl}{text}";
        if (Regex.IsMatch(fm.Groups["keys"].Value, @"^mergedFrom:", RegexOptions.Multiline)) return text;
        return text.Insert(fm.Groups["keys"].Index + fm.Groups["keys"].Length, stamp);
    }
    /// <summary>What the sources carried in x-rtm besides the fields CreateFolder sets: label, links, tests, lanes, dependencies, old numbers.</summary>
    private static void KeepLinks(Rtm m, IReadOnlyList<WorkItem> items, HashSet<int> sources)
    {
        m.Label = items.Select(i => i.Label).FirstOrDefault(l => !string.IsNullOrEmpty(l));   // one label: the first set
        if (items.SelectMany(i => i.Commits ?? []).Distinct().ToList() is { Count: > 0 } commits) m.Commits = commits;
        if (items.SelectMany(i => i.Docs ?? []).Distinct().ToList() is { Count: > 0 } docs) m.Docs = docs;
        List<RtmTest> tests = [.. items.SelectMany(i => i.Tests ?? []).GroupBy(t => (t.Suite, t.Test))
            .Select(g => g.FirstOrDefault(t => t.Note is not null) ?? g.First()).Select(t => new RtmTest { Suite = t.Suite, Test = t.Test, Note = t.Note })];
        if (tests.Count > 0) m.Tests = tests;
        Dictionary<string, string> lanes = [];
        foreach ((string column, string lane) in items.SelectMany(i => i.Lanes)) lanes.TryAdd(column, lane);   // first source wins per column
        if (lanes.Count > 0) m.Lanes = lanes;
        if (items.SelectMany(i => i.DependsOn ?? []).Where(d => !sources.Contains(d)).Distinct().ToList() is { Count: > 0 } deps) m.DependsOn = deps;
        m.MergedFrom = [.. items.SelectMany(i => (i.MergedFrom ?? []).Prepend(i.Id)).Distinct()];
    }

    /// <summary>Copies a source's notes into the merged change as they are, and its other files under from-wi&lt;id&gt;/.</summary>
    private static void CopyExtras(string from, string to, int id, bool keepSpecs, string? stampNotes = null)
    {
        string[] own = [".openspec.yaml", "proposal.md", "tasks.md", EventLog.FileName];
        foreach (string file in Directory.EnumerateFiles(from, "*", SearchOption.AllDirectories))
        {
            string rel = Path.GetRelativePath(from, file);
            string top = rel.Split(Path.DirectorySeparatorChar)[0];
            if ((top == "specs" && !keepSpecs) || own.Contains(rel)) continue;   // specs are merged per capability (an archived source's only when the result is not archived), the rest is rebuilt
            string target = top == "notes" ? Path.Combine(to, rel) : Path.Combine(to, $"from-wi{id}", rel);
            if (File.Exists(target)) continue;
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            if (top == "notes" && stampNotes is not null && rel.EndsWith(".md")) OpenSpecStoreApi.WriteText(target, StampMerged(OpenSpecStoreApi.ReadText(file), id, stampNotes));   // a note a merge moves in says from where and when
            else File.Copy(file, target);
        }
    }

    /// <summary>A subgroup already in the column (case and surrounding spaces ignored) is a quiet success: nothing is added twice.</summary>
    internal static (bool Success, string Message) AddLane(string column, string lane)
    {
        lane = lane.Trim();
        if (lane.Length == 0) return (false, "Type a name first");
        return EditLanes(column, l => { if (!l.Any(x => string.Equals(x.Trim(), lane, StringComparison.OrdinalIgnoreCase))) l.Add(lane); });
    }

    /// <summary>Places the item in the column's subgroup, creating the subgroup when the column does not have it yet; an existing one keeps its spelling.</summary>
    internal static (bool Success, string Message) SetLane(int id, string column, string lane)
    {
        lane = lane.Trim();
        if (lane.Length == 0) return (false, "Type a name first");
        string name = lane;
        (bool ok, string message) = EditLanes(column, l =>
        {
            if (l.FirstOrDefault(x => string.Equals(x.Trim(), lane, StringComparison.OrdinalIgnoreCase)) is string found) name = found; else l.Add(lane);
        });
        return ok ? Update(id, m => (m.Lanes ??= [])[column] = name) : (ok, message);
    }

    internal static (bool Success, string Message) EditLanes(string column, Action<List<string>> edit)
    {
        if (OpenSpecStoreApi.OpenSpecRoot is not string root) return (false, "No repository is open");
        string yaml = Path.Combine(root, "config.yaml");
        Dictionary<string, List<string>> all;
        try { all = Metadata.ReadSection<Dictionary<string, List<string>>>(yaml, "lanes") ?? []; }
        catch (MetadataException e) { return (false, e.Message); }   // not written over
        List<string> lanes = all.TryGetValue(column, out List<string>? l) ? l : all[column] = [];
        edit(lanes);
        if (lanes.Count == 0) all.Remove(column);
        return Metadata.WriteSection(yaml, "lanes", all.Count == 0 ? null : all);
    }

    internal static (bool Success, string Message) StampAgent(int id, string agent) =>
        Update(id, m => { m.AgentBy = agent; m.AgentAt = DateTime.Now.ToString("s", System.Globalization.CultureInfo.InvariantCulture); });

    internal static (bool Success, string Message) SetDeltaSpec(int id, string capability, string? markdown)
    {
        capability = (capability ?? "").Replace('\\', '/').Trim('/');
        if (capability.Length == 0 || Path.IsPathRooted(capability) || capability.Split('/').Contains("..")) return (false, "A capability is a path under openspec/specs, like billing or functional/banking");
        if (Queries.Get(id) is not WorkItem item) return (false, NotFound);
        string dir = Path.Combine(item.Path, "specs", capability.Replace('/', Path.DirectorySeparatorChar));
        (bool ok, string message) = markdown is null
            ? (Directory.Exists(dir) ? OpenSpecStoreApi.DeleteFolder(dir) : (true, ""))
            : OpenSpecStoreApi.WriteText(Path.Combine(dir, "spec.md"), markdown.Replace("\r\n", "\n"));
        return ok ? SyncSkipSpecs(item.Path) : (ok, message);
    }

    /// <summary>How Validate runs the CLI (tests swap it).</summary>
    internal static Func<string, (bool Success, string Message)> Validator = OpenSpecCli.Validate;

    internal static (bool Success, string Message) Validate(int id) =>
        Queries.Get(id) is WorkItem item ? Validator(item.Folder) : (false, NotFound);
}
