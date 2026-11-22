using System.Text.RegularExpressions;
using OpenSpecStore;
using OpenSpecStore.PublicDTOs;
using WorkItems.BusinessRules;
using WorkItems.Infrastructure;
using WorkItems.PublicDTOs;

namespace WorkItems;

internal static partial class Commands
{
    private const string NotFound = "That work item no longer exists";
    private const string Archived = "Completed work items are archived and can't be moved back";

    /// <summary>Loads the item's x-rtm block, applies <paramref name="change"/>, and writes it back (stamping a provisional id).</summary>
    internal static (bool Success, string Message) Update(int id, Action<Rtm> change, bool syncSkipSpecs = true)
    {
        if (Queries.Get(id) is not WorkItem item) return (false, NotFound);
        string yaml = Queries.Yaml(item.Path);
        Rtm meta;
        try { meta = Metadata.Read<Rtm>(yaml); }
        catch (MetadataException e) { return (false, item.Warning ?? e.Message); }   // a yaml that does not parse is never written over: it is the user's to fix
        meta.Id ??= item.Id;
        change(meta);
        (bool ok, string message) = Metadata.Write(yaml, meta);
        return ok && syncSkipSpecs ? SyncSkipSpecs(item.Path) : (ok, message);
    }

    /// <summary>
    /// Points the plan and the linked documents of every work item (open and archived) that <paramref name="newPath"/> answers for (null = leave it) to the path it gives.
    /// Silent: no event, and nothing else in the item's files changes. Returns how many links changed.
    /// </summary>
    internal static int RewritePaths(Func<string, string?> newPath)
    {
        int count = 0;
        foreach (WorkItem w in Queries.GetAll(includeHidden: true).ToList())
        {
            string? plan = w.Plan is { Length: > 0 } p ? newPath(p) : null;
            List<string> docs = [.. (w.Docs ?? []).Select(d => newPath(d) ?? d)];
            int docsChanged = docs.Where((d, i) => d != w.Docs![i]).Count();
            if (plan is null && docsChanged == 0) continue;
            (bool ok, string _) = Update(w.Id, m => { if (plan is not null) m.Plan = plan; if (docsChanged > 0) m.Docs = docs; }, syncSkipSpecs: false);
            if (ok) count += (plan is null ? 0 : 1) + docsChanged;
        }
        return count;
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

    /// <summary>A header field's value as an event writes it: the text, or "none".</summary>
    private static string Shown(string? value) => string.IsNullOrWhiteSpace(value) ? "none" : value.Trim();

    /// <summary>Changes a header field and records <c>old -&gt; new</c> as one event of <paramref name="kind"/>, or nothing when the value is the same.</summary>
    private static (bool Success, string Message) UpdateLogged(int id, string kind, string? before, string? after, Action<Rtm> change)
    {
        (bool ok, string message) = Update(id, change);
        if (ok && Shown(before) != Shown(after)) Log(id, (kind, $"{Shown(before)} -> {Shown(after)}"));
        return (ok, message);
    }

    internal static (bool Success, string Message) SetTitle(int id, string title) =>
        UpdateLogged(id, "title", Queries.Get(id)?.Title, title, m => m.Title = title.Trim());

    internal static (bool Success, string Message) SetPriority(int id, string priority) =>
        UpdateLogged(id, "priority", Queries.Get(id)?.Priority, priority, m => m.Priority = priority);

    internal static (bool Success, string Message) SetLabel(int id, string? label) =>
        UpdateLogged(id, "label", Queries.Get(id)?.Label, label, m => m.Label = string.IsNullOrEmpty(label) ? null : label);

    /// <summary>Sets one end of the item's dates; the event shows the whole range before and after.</summary>
    internal static (bool Success, string Message) SetDate(int id, bool start, DateOnly? day)
    {
        WorkItem? item = Queries.Get(id);
        string? text = day?.ToString("yyyy-MM-dd");
        return UpdateLogged(id, "dates", Range(item?.Started, item?.Due), start ? Range(day, item?.Due) : Range(item?.Started, day),
            m => { if (start) m.Started = text; else m.Due = text; });
    }

    /// <summary>Sets both ends at once and records one event for the whole change.</summary>
    internal static (bool Success, string Message) SetDates(int id, DateOnly? start, DateOnly? due)
    {
        WorkItem? item = Queries.Get(id);
        return UpdateLogged(id, "dates", Range(item?.Started, item?.Due), Range(start, due),
            m => { m.Started = start?.ToString("yyyy-MM-dd"); m.Due = due?.ToString("yyyy-MM-dd"); });
    }

    private static string Range(DateOnly? start, DateOnly? due)
    {
        string Day(DateOnly? d) => d?.ToString("d MMM", System.Globalization.CultureInfo.InvariantCulture) ?? "none";
        return start is null && due is null ? "none" : $"{Day(start)} – {Day(due)}";
    }

    internal static (bool Success, string Message) SetContributors(int id, IEnumerable<string> people)
    {
        List<string> now = people.Distinct().ToList(), before = [.. Queries.Get(id)?.Contributors ?? []];   // with none saved, the commit authors count as the contributors
        (bool ok, string message) = Update(id, m => m.Contributors = now);
        if (ok) Log(id, Diff("contributor", before, now, "added", "removed"));
        return (ok, message);
    }

    internal static (bool Success, string Message) SetPlan(int id, string? path)
    {
        string? before = null;
        (bool ok, string message) = Update(id, m => { before = m.Plan; m.Plan = string.IsNullOrEmpty(path) ? null : path; });
        if (ok) Log(id, Diff("plan", before is { Length: > 0 } ? [before] : [], string.IsNullOrEmpty(path) ? [] : [path]));
        return (ok, message);
    }

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

    /// <param name="ignoreDependencies">Deleting a status moves its items whatever they wait on: their status is going away, not chosen.</param>
    internal static (bool Success, string Message) SetStatus(int id, string status, bool ignoreDependencies = false)
    {
        if (!Queries.GetStatuses().Contains(status)) return (false, $"Unknown status \"{status}\": this project's statuses are {string.Join(", ", Queries.GetStatuses())}");
        if (Queries.Get(id) is not WorkItem item) return (false, NotFound);
        if (item.Archived) return item.Status == status ? (true, "") : (false, Archived);
        if (!ignoreDependencies && status != "Blocked" && (item.DependsOn ?? []).Select(Queries.Get).OfType<WorkItem>().FirstOrDefault(d => !WorkItemsApi.IsResolved(d)) is WorkItem dep)
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
        (bool ok, string message) = Update(id, m =>
        {
            m.UnlinkedFeatures = specced.Where(p => p != to).Distinct().ToList();
            m.Features = to is null || specced.Contains(to) ? [] : [to];
        });
        string? before = item.Features.FirstOrDefault()?.Path;   // an item has one feature at most
        if (ok && Shown(before) != Shown(to)) Log(id, ("feature", $"{Shown(before)} -> {Shown(to)}"));
        return (ok, message);
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
        string was = OpenSpecStoreApi.ReadText(path), text = Proposal.WithBody(was, body);
        (bool ok, string message) = OpenSpecStoreApi.WriteText(path, text);
        if (ok && text.Replace("\r", "") != was.Replace("\r", "")) Log(id, ("description-edited", "proposal.md"));
        return (ok, message);
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

    internal static (bool Success, string Message) RemoveSubtask(int id, string number)
    {
        Subtask? subtask = Queries.Get(id)?.Subtasks.FirstOrDefault(s => s.Number == number && !s.Number.StartsWith('#'));
        if (subtask is null) return (false, Queries.Get(id) is null ? NotFound : "That subtask no longer exists");
        (bool ok, string message) = EditTasks(id, text => TasksFile.Remove(text, [number]));
        if (ok) Log(id, ("subtask-removed", $"{subtask.Number} {subtask.Text}"));
        return (ok, message);
    }

    private static (bool Success, string Message) EditTasks(int id, Func<string, string> edit)
    {
        if (Queries.Get(id) is not WorkItem item) return (false, NotFound);
        string path = Path.Combine(item.Path, "tasks.md");
        return OpenSpecStoreApi.WriteText(path, edit(OpenSpecStoreApi.ReadText(path)));
    }

    internal static (bool Success, string Message, int Id) Create(string title, string status, IEnumerable<string>? features, IEnumerable<string>? contributors, string? folder = null) =>
        CreateFolder(title, status, features, contributors, priority: null, proposalBody: "", tasks: "# Tasks\n\n## 1. Tasks\n\n", deltas: new Dictionary<string, string>(), folder: folder, more: m => m.Moved = Today());

    /// <summary>
    /// A folder name no change has ever had: not under changes/ and not under changes/archive/ either (there with its
    /// yyyy-MM-dd- prefix stripped, any date), and not one a merge removed (x-rtm.mergedFolders). Numbers duplicates: name, name-2, name-3, ...
    /// </summary>
    private static string FreeFolderName(string baseName)
    {
        HashSet<string> used = UsedFolderNames();
        string name = baseName;
        for (int n = 2; used.Contains(name); n++) name = $"{baseName}-{n}";
        return name;
    }

    internal static bool FolderExists(string name) => UsedFolderNames().Contains(name);

    private static HashSet<string> UsedFolderNames()
    {
        string changes = Path.Combine(OpenSpecStoreApi.OpenSpecRoot!, "changes"), archive = Path.Combine(changes, "archive");
        HashSet<string> used = new(StringComparer.OrdinalIgnoreCase);
        if (Directory.Exists(changes)) used.UnionWith(Directory.EnumerateDirectories(changes).Select(d => Path.GetFileName(d)));
        if (Directory.Exists(archive)) used.UnionWith(Directory.EnumerateDirectories(archive).Select(d => DatePrefix().Replace(Path.GetFileName(d), "")));
        used.UnionWith(Queries.GetAll(includeHidden: true).SelectMany(w => w.MergedFolders ?? []));   // a merge deletes its sources' folders, but git remembers them
        return used;
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

    /// <summary>One repair at a time: two callers (the host's change handler and a tool call) must not renumber the same change twice.</summary>
    private static readonly object RepairGate = new();

    /// <summary>
    /// Two changes carrying one number (created in two places, a copied folder, a merge): every one but the keeper takes the
    /// next free number. The keeper is an archived change first (its number is history), then the earliest created, then the
    /// first folder name, so every copy of the repository resolves the same way. What the renumbered change says about itself
    /// (#wiN in its proposal, tasks, notes, events and title) follows it, and its worklog records the renumbering. References other
    /// items hold to the number stay as they are: they most likely meant the keeper. With nothing duplicated nothing is read
    /// again and nothing is written. Returns what was renumbered.
    /// </summary>
    internal static IReadOnlyList<(int OldId, int NewId, string Folder)> ResolveDuplicateIds()
    {
        WorkItemsModule.DuplicateScans++;
        if (OpenSpecStoreApi.OpenSpecRoot is null) return [];
        lock (RepairGate)
        {
            static List<List<WorkItem>> Twins() => [.. Queries.GetAll(includeHidden: true).GroupBy(w => w.Id).Where(g => g.Count() > 1).Select(g => g.OrderBy(w => w.Archived ? 0 : 1).ThenBy(w => w.Created).ThenBy(w => w.Folder, StringComparer.Ordinal).ToList())];
            List<List<WorkItem>> groups = Twins();
            if (groups.Count == 0) return [];
            StampProvisionalIds();   // before a new number is handed out, which would shift the provisional ones
            List<(int, int, string)> renumbered = [];
            foreach (List<WorkItem> twins in groups)
            foreach (WorkItem w in twins.Skip(1))
            {
                string yaml = Queries.Yaml(w.Path);
                Rtm meta = Metadata.TryRead<Rtm>(yaml, out string? problem);
                if (problem is not null) continue;   // does not parse: left as it is until fixed, and the warning stays
                int newId = OpenSpecStoreApi.Allocate("work-item", roots => roots.Max(r => Queries.MaxIdUnder(r)));
                FollowId(w.Path, w.Id, newId);
                meta.Id = newId;
                if (meta.Title is not null) meta.Title = OpenSpecStoreApi.RenameReference(meta.Title, "item", w.Id, newId);
                if (!Metadata.Write(yaml, meta).Success) continue;
                EventLog.Append(w.Path, Actor(), [("renumbered", $"#wi{w.Id} -> #wi{newId}: the id was taken by \"{twins[0].Title}\" ({twins[0].Folder}) after a merge")]);
                renumbered.Add((w.Id, newId, w.Folder));
            }
            return renumbered;
        }
    }

    /// <summary>Rewrites the references to the old number in what the change wrote about itself: proposal, tasks, notes and events.</summary>
    private static void FollowId(string dir, int oldId, int newId)
    {
        string notes = Path.Combine(dir, "notes");
        foreach (string file in (IEnumerable<string>)[Path.Combine(dir, "proposal.md"), Path.Combine(dir, "tasks.md"), EventLog.PathIn(dir), .. (Directory.Exists(notes) ? Directory.EnumerateFiles(notes, "n*.md") : [])])
        {
            if (!File.Exists(file)) continue;
            string text = OpenSpecStoreApi.ReadText(file), now = OpenSpecStoreApi.RenameReference(text, "item", oldId, newId);
            if (now != text) OpenSpecStoreApi.WriteText(file, now);
        }
    }

    /// <summary>
    /// Provisional numbers are "highest stored + n", so storing a new number would shift them. Before allocating,
    /// write every provisional number into its change so links like #wi125 never start pointing elsewhere.
    /// </summary>
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
        if (ok)
        {
            // one entry per subtask in each feed, by its number before the move, so a person reading either feed can find it again
            Log(id, [.. moved.Select(s => ("subtask-moved", $"{s.Number} -> #wi{newId}"))]);
            Log(newId, [.. moved.Select(s => ("made-from-subtask", $"{s.Number} of #wi{id}"))]);
        }
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
        lock (OpenSpecStoreApi.StepsGate) return MergeSteps(ids, into);   // readers that repair note ids wait: halfway through, a note is in two folders
    }

    /// <summary>
    /// Features that were deleted stay on archived items. Two different deleted features cannot merge (the reason is returned); the same deleted
    /// one can; and when an existing feature is among the items it takes over: <paramref name="carried"/> says which feature paths the merged item keeps.
    /// </summary>
    private static string? CategoryRule(IReadOnlyList<WorkItem> items, out Func<string, bool> carried)
    {
        HashSet<string> existing = [.. Features.FeaturesApi.GetAll().Select(f => f.Path)];
        List<(WorkItem Item, string Path)> gone = [.. items.SelectMany(i => i.Features, (i, f) => (Item: i, f.Path)).Where(x => !existing.Contains(x.Path))];
        bool existingWins = items.SelectMany(i => i.Features).Any(f => existing.Contains(f.Path));
        carried = p => !existingWins || existing.Contains(p);
        if (gone.Select(g => g.Path).Distinct().Count() < 2) return null;
        (WorkItem a, string pa) = gone[0];
        (WorkItem b, string pb) = gone.First(g => g.Path != pa);
        return $"#wi{a.Id} and #wi{b.Id} have different features that no longer exist ({pa} and {pb}): move one of them to an existing feature first";
    }

    private static (bool Success, string Message, int Id) MergeSteps(IReadOnlyList<int> ids, int into)
    {
        List<WorkItem> items = ids.Select(Queries.Get).OfType<WorkItem>().ToList();
        if (items.Count < 2) return (false, "Select at least two work items to merge", 0);
        if (items.FirstOrDefault(i => i.Id == into) is not WorkItem main) return (false, $"#wi{into} is not one of the work items to merge", 0);
        bool allArchived = items.All(i => i.Archived);
        if (CategoryRule(items, out Func<string, bool> carried) is string refusal) return (false, refusal, 0);

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
            items.SelectMany(i => i.Features.Where(f => !f.Locked).Select(f => f.Path).Where(carried)),
            items.SelectMany(i => i.Contributors),
            items.Any(i => i.Priority == "high") ? "high" : null,
            body, tasks, deltas, folder: "merge-" + string.Join("-", items.Select(i => $"wi{i.Id}")), inArchive: allArchived,
            more: m => KeepLinks(m, items, sources));
        if (!ok) return (ok, message, 0);

        // Nothing but the folders is dropped: notes and any other files move over, and items that waited on a source now wait on this one.
        string merged = Queries.Get(id)!.Path;
        EventLog.Prepend(merged, EventLog.SortedLines(items.Select(i => (i.Path, i.Id == main.Id ? (int?)null : i.Id))));   // every source's events, oldest first, a source's that is not the main item's marked "from #wiN"; then the merged item's own created
        EventLog.Append(merged, Actor(), [.. items.Where(i => i.Id != main.Id).Select(i => ("merged-from", $"#wi{i.Id} {i.Title}"))]);   // one per item merged into the main one
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
        string numbered = $"#wi{it.Id} {it.Title}";
        bool Starts(string s) => name == s || name.StartsWith(s + " · ", StringComparison.Ordinal);
        string heading;
        if (Starts(numbered)) heading = name;   // already says whose it is
        else if (Starts(it.Title)) heading = isMain ? name : $"#wi{it.Id} {name}";   // "Title · Group" from an earlier merge: the title is not put on again
        else
        {
            string title = isMain ? it.Title : numbered;
            heading = several && name.Length > 0 ? $"{title} · {name}" : title;
        }
        heading = TasksFile.CollapseSegments(heading);
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
        m.MergedFolders = [.. items.SelectMany(i => (i.MergedFolders ?? []).Prepend(i.Folder)).Distinct()];
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
            OpenSpecStoreApi.CreateFolder(Path.GetDirectoryName(target)!);
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
        if (OpenSpecStoreApi.RepoRoot is null) return (false, "No repository is open");
        ConfigSection<Dictionary<string, List<string>>> read = OpenSpecStoreApi.ReadConfigSection<Dictionary<string, List<string>>>("lanes");
        if (read.Problem is not null) return (false, read.Problem);   // not written over
        Dictionary<string, List<string>> all = read.Value ?? [];
        List<string> lanes = all.TryGetValue(column, out List<string>? l) ? l : all[column] = [];
        edit(lanes);
        if (lanes.Count == 0) all.Remove(column);
        return OpenSpecStoreApi.WriteConfigSection("lanes", all.Count == 0 ? null : all, flowLists: true);
    }

    // ── The project's statuses (x-rtm.statuses in openspec/config.yaml) ──

    private const int MaxStatusLength = 40;

    /// <summary>Reads the project's list, lets <paramref name="edit"/> change it (returning a reason to refuse, or null) and saves it; the built-in list is saved as no line at all.</summary>
    private static (bool Success, string Message) EditStatuses(Func<List<string>, string?> edit)
    {
        if (OpenSpecStoreApi.RepoRoot is null) return (false, "No repository is open");
        ConfigSection<List<string>> read = OpenSpecStoreApi.ReadConfigSection<List<string>>("statuses");
        if (read.Problem is not null) return (false, read.Problem);   // not written over
        List<string> list = [.. Queries.Normalise(read.Value)];
        if (edit(list) is string refusal) return (false, refusal);
        IReadOnlyList<string> saved = Queries.Normalise(list);
        return OpenSpecStoreApi.WriteConfigSection("statuses", saved.SequenceEqual(WorkItemsApi.Statuses) ? null : saved.ToList(), flowLists: true);
    }

    internal static (bool Success, string Message) AddStatus(string name, bool beforeCompleted = false)
    {
        name = (name ?? "").Trim();
        if (name.Length == 0) return (false, "Type a name first");
        if (name.Length > MaxStatusLength) return (false, $"A status name is at most {MaxStatusLength} characters");
        if (name.Any(char.IsControl)) return (false, "A status name is one line of plain text");
        return EditStatuses(l =>
        {
            if (l.Contains(name)) return $"This project has a status \"{name}\" already";
            if (beforeCompleted && l.Count > 0 && l[^1] == "Completed") l.Insert(l.Count - 1, name);   // Completed means archived: it stays last
            else l.Add(name);
            return null;
        });
    }

    internal static int CountWithStatus(string status) => Queries.GetAll(includeHidden: true).Count(w => w.Status == status);

    /// <summary>
    /// Takes a status out of the project's list: not a fixed one. Open work items (hidden ones included) that have it move to the project's first status
    /// (the uncategorised one); archived items are not touched: they keep the name in their file and show as Completed.
    /// </summary>
    internal static (bool Success, string Message) RemoveStatus(string name)
    {
        if (WorkItemsApi.FixedStatuses.Contains(name)) return (false, $"{name} stays: Backlog, In Progress and Completed are in every project");
        string? first = Queries.GetStatuses().FirstOrDefault(s => s != name && s != "Completed");
        return first is null ? RemoveFromList(name) : RemoveStatusMoving(name, first);
    }

    private static (bool Success, string Message) RemoveFromList(string name) => EditStatuses(l => l.Remove(name) ? null : $"This project has no status \"{name}\"");

    /// <summary>Moves every work item that has <paramref name="name"/> to <paramref name="to"/>, then removes the status. Stops at the first item that cannot move (nothing is removed then).</summary>
    internal static (bool Success, string Message) RemoveStatusMoving(string name, string to)
    {
        if (WorkItemsApi.FixedStatuses.Contains(name)) return (false, $"{name} stays: Backlog, In Progress and Completed are in every project");
        IReadOnlyList<string> statuses = Queries.GetStatuses();
        if (!statuses.Contains(name)) return (false, $"This project has no status \"{name}\"");
        if (to == name || !statuses.Contains(to)) return (false, "Pick another of this project's statuses to move the work items to");
        if (to == "Completed") return (false, "Completed means archived: move them to another status");
        foreach (WorkItem item in Queries.GetAll(includeHidden: true).Where(w => w.Status == name).ToList())
            if (SetStatus(item.Id, to, ignoreDependencies: true) is { Success: false } failed) return (false, $"#wi{item.Id} did not move: {failed.Message}");
        return RemoveFromList(name);
    }

    /// <summary>Moves a status up (<paramref name="by"/> -1) or down (+1) in the project's order; any status, the fixed ones too.</summary>
    internal static (bool Success, string Message) MoveStatus(string name, int by) =>
        EditStatuses(l =>
        {
            int at = l.IndexOf(name), to = Math.Clamp(at + by, 0, l.Count - 1);
            if (at < 0) return $"This project has no status \"{name}\"";
            l.RemoveAt(at);
            l.Insert(to, name);
            return null;
        });

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
