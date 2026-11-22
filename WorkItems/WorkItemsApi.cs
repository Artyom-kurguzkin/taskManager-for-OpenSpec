using WorkItems.PublicDTOs;

namespace WorkItems;

/// <summary>
/// The only public type in this assembly. OpenSpec changes are work items: x-rtm metadata in .openspec.yaml,
/// tasks.md checkboxes as subtasks, archived changes as Completed. Reads come straight from disk; every write
/// goes through OpenSpecStore so outside edits are never overwritten. No DI; delegates to Queries/Commands.
/// </summary>
public static class WorkItemsApi
{
    /// <summary>The pinned OpenSpec CLI the app archives with; the agent guide tells agents to validate with the same one.</summary>
    public const string OpenSpecCliVersion = Infrastructure.OpenSpecCli.Version;
    /// <summary>The built-in statuses: the list of a project whose openspec/config.yaml has no <c>x-rtm.statuses</c>. A project's own list is <see cref="GetStatuses"/>.</summary>
    public static readonly IReadOnlyList<string> Statuses = ["Backlog", "Planned", "Confirmed", "In Progress", "Blocked", "In Review", "Completed"];
    /// <summary>The statuses no project is without: they cannot be removed or renamed (Completed also means archived).</summary>
    public static readonly IReadOnlyList<string> FixedStatuses = ["Backlog", "In Progress", "Completed"];
    public static readonly IReadOnlyList<string> Labels = ["bug", "feature", "enhancement"];

    /// <summary>Git commit authors stand in as contributors until the list is edited. The token must change on each commit so the cache refreshes.</summary>
    public static void ConfigureContributors(Func<(string Token, Func<string, IReadOnlyList<string>> AuthorsOf)?>? git)
    {
        WorkItemsModule.Git = git;
        WorkItemsModule.GitEpoch++;   // items take their authors again; no folder is parsed again for it
    }

    /// <summary>
    /// How a change gets archived. Default runs <c>openspec archive &lt;folder&gt; --yes</c> in the repo root; pass null to restore it.
    /// When the CLI refuses, the app retries with <c>--no-validate</c>, then with <c>--no-validate --skip-specs</c>, and only if all
    /// three fail returns the first message. This fake is called again on each try.
    /// </summary>
    public static void ConfigureArchiver(Func<string, (bool Success, string Message)>? archive) =>
        WorkItemsModule.Archive = archive is null ? Infrastructure.OpenSpecCli.Archive : (folder, _) => { (bool ok, string message) = archive(folder); return (ok, message, true); };

    /// <summary>
    /// Like <see cref="ConfigureArchiver(Func{string, ValueTuple{bool, string}})"/>, but the fake also receives the try's extra flags (none, then <c>--no-validate</c>, then
    /// <c>--no-validate --skip-specs</c>), and CouldRun false says the CLI never decided (no Node.js, timeout): no retry then.
    /// </summary>
    public static void ConfigureArchiverWithFlags(Func<string, IReadOnlyList<string>, (bool Success, string Message, bool CouldRun)>? archive) =>
        WorkItemsModule.Archive = archive ?? Infrastructure.OpenSpecCli.Archive;

    // ── The board as the host's cache reads it (the module keeps no cache of its own) ──
    /// <summary>
    /// Hands the module the host's cached read of the board (<paramref name="current"/>, refreshed when what it depends on moved) and its look at the last board without refreshing
    /// (<paramref name="lastSeen"/>, null when none was read yet), for <see cref="GetAll"/> and the module's own commands and rules. Null = read the board afresh on every call.
    /// </summary>
    public static void UseBoard(Func<BoardRead>? current, Func<BoardRead?>? lastSeen)
    {
        WorkItemsModule.Board = current;
        WorkItemsModule.LastSeen = lastSeen;
    }

    /// <summary>
    /// Hands the module the host's cache of item descriptions (<paramref name="description"/>, which calls <see cref="ReadDescription"/> when it has to read): <see cref="GetDescription"/> answers from it.
    /// Null = read the proposal on every call.
    /// </summary>
    public static void UseDescriptions(Func<int, string>? description) => WorkItemsModule.Description = description;

    /// <summary>
    /// Hands the module the host's cache of item events (<paramref name="events"/>, which calls <see cref="ReadEvents"/> when it has to read): <see cref="GetEvents"/> answers from it, so an item's events.log
    /// is read again only when it changed. Null = read events.log on every call.
    /// </summary>
    public static void UseEvents(Func<int, IReadOnlyList<WorkItemEvent>>? events) => WorkItemsModule.Events = events;

    /// <summary>The change folders in board order: open changes by folder name, then archived ones.</summary>
    public static IReadOnlyList<(string Dir, bool Archived)> ListFolders() => Queries.ListFolders();

    /// <summary>One change folder parsed from disk now (nothing is kept); null when it is being moved or written while read.</summary>
    public static FolderRead? ReadFolder(string dir, bool archived) => Queries.ReadFolder(dir, archived);

    /// <summary>The number each folder shows, in the order given: its stored x-rtm.id, else the next free one after the highest stored.</summary>
    public static IReadOnlyList<int> Number(IReadOnlyList<FolderRead> folders) => Queries.Number(folders);

    /// <summary>What a folder shows as: its number, its commit authors as contributors until the list is edited (<paramref name="git"/>, from <see cref="ContributorSource"/>), and Backlog for a status outside <paramref name="statuses"/>.</summary>
    public static WorkItem Present(FolderRead folder, int id, IReadOnlyList<string> statuses, (string Token, Func<string, IReadOnlyList<string>> AuthorsOf)? git) => Queries.Present(folder, id, statuses, git);

    /// <summary>An item read back from the host's saved index: what the board shows is as saved, and its folder revision (a body, worked out when asked) is attached again, so a write against it is checked as for any item.</summary>
    public static WorkItem Restore(WorkItem saved) => Queries.Restore(saved);

    /// <summary>The commit authors per change folder with the token that changes on each commit (see <see cref="ConfigureContributors"/>); null outside git.</summary>
    public static (string Token, Func<string, IReadOnlyList<string>> AuthorsOf)? ContributorSource() => WorkItemsModule.Git?.Invoke();

    /// <summary>Goes up each time <see cref="ConfigureContributors"/> is called: every item takes its commit authors again.</summary>
    public static int ContributorEpoch => WorkItemsModule.GitEpoch;

    // ── Reads ──
    /// <summary>Visible work items; pass includeHidden to also get the ones "deleted" in the app.</summary>
    public static IReadOnlyList<WorkItem> GetAll(bool includeHidden = false) => Queries.GetAll(includeHidden);
    public static WorkItem? Get(int id) => Queries.Get(id);
    public static int NextId() => Queries.NextId();
    public static IReadOnlyList<string> GetWarnings() => Queries.GetWarnings();

    /// <summary>Compare-and-swap guard for agents: succeeds when <paramref name="expected"/> is null/empty or equals the item's current revision.</summary>
    public static (bool Success, string Message) CheckRevision(int id, string? expected)
    {
        WorkItem? item = Queries.Get(id);
        if (item is null) return (false, "That work item no longer exists");
        if (string.IsNullOrEmpty(expected) || expected == item.Revision) return (true, "");
        return (false, $"#wi{id} changed since you read it (revision {item.Revision} now); read it again before writing");
    }

    /// <summary>Gives every change that shares its number with an earlier one the next free number; returns what changed.</summary>
    public static IReadOnlyList<(int OldId, int NewId, string Folder)> ResolveDuplicateIds() => Commands.ResolveDuplicateIds();
    public static string GetDescription(int id) => WorkItemsModule.Description?.Invoke(id) ?? Queries.GetDescription(id);
    /// <summary>The item's description read from its proposal.md now (nothing kept): what the host's cache of descriptions calls when it has to read.</summary>
    public static string ReadDescription(int id) => Queries.GetDescription(id);

    /// <summary>
    /// A short text that changes when a change folder appears, goes, is renamed or archived, or an item's number moves, and not when an item is merely edited.
    /// The app runs <see cref="ResolveDuplicateIds"/> only when it changed. Built from the cached items, so it costs no disk read of its own.
    /// </summary>
    public static string FolderStamp() => Queries.FolderStamp();
    /// <summary>Sub-categories per board column, from openspec/config.yaml x-rtm.lanes.</summary>
    public static IReadOnlyDictionary<string, IReadOnlyList<string>> GetLanes() => Queries.GetLanes();

    // ── Statuses of the open project ──
    /// <summary>The open project's statuses in board order: <c>x-rtm.statuses</c> in its openspec/config.yaml, else <see cref="Statuses"/>; the fixed three are always in it.</summary>
    public static IReadOnlyList<string> GetStatuses() => Queries.GetStatuses();
    /// <summary>Adds a status at the end of the list, or with <paramref name="beforeCompleted"/> just before Completed when the list ends with it. A name the project has already is refused (spelling counts: "completed" is not "Completed").</summary>
    public static (bool Success, string Message) AddStatus(string name, bool beforeCompleted = false) => Commands.AddStatus(name, beforeCompleted);
    /// <summary>Removes a status that is not a fixed one and that no work item (hidden ones included) has.</summary>
    public static (bool Success, string Message) RemoveStatus(string name) => Commands.RemoveStatus(name);
    /// <summary>Moves the work items that have the status to another, then removes it.</summary>
    public static (bool Success, string Message) RemoveStatus(string name, string moveItemsTo) => Commands.RemoveStatusMoving(name, moveItemsTo);
    /// <summary>Moves a status one place up (-1) or down (+1) in the project's order.</summary>
    public static (bool Success, string Message) MoveStatus(string name, int by) => Commands.MoveStatus(name, by);
    /// <summary>How many work items of the open project (hidden ones included) have the status.</summary>
    public static int CountWithStatus(string status) => Commands.CountWithStatus(status);

    /// <summary>
    /// The fixed status a name stands for when it is spelled differently (case, spaces, dashes and underscores ignored: "completed", "InProgress", "in progress"), or null for
    /// a name that is a fixed status itself or looks like none.
    /// </summary>
    public static string? LookAlikeOf(string status) =>
        FixedStatuses.FirstOrDefault(f => f != status && Squash(f) == Squash(status));

    private static string Squash(string s) => new([.. s.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant)]);

    // ── Header fields ──
    public static (bool Success, string Message) SetStatus(int id, string status) => Commands.SetStatus(id, status);
    public static (bool Success, string Message) SetPriority(int id, string priority) => Commands.SetPriority(id, priority);
    public static (bool Success, string Message) SetTitle(int id, string title) => Commands.SetTitle(id, title);
    public static (bool Success, string Message) SetContributors(int id, IEnumerable<string> people) => Commands.SetContributors(id, people);
    public static (bool Success, string Message) SetLabel(int id, string? label) => Commands.SetLabel(id, label);
    public static (bool Success, string Message) SetStarted(int id, DateOnly? day) => Commands.SetDate(id, start: true, day);
    /// <summary>Sets the start and the due day together (null = none); one event records the change.</summary>
    public static (bool Success, string Message) SetDates(int id, DateOnly? started, DateOnly? due) => Commands.SetDates(id, started, due);
    public static (bool Success, string Message) SetDue(int id, DateOnly? day) => Commands.SetDate(id, start: false, day);
    /// <summary>Links a plan file by repo-relative path (slashes normalised); null or blank removes the link. Paths outside the repository are refused.</summary>
    public static (bool Success, string Message) SetPlan(int id, string? path)
    {
        path = path?.Replace('\\', '/').Trim();
        if (string.IsNullOrEmpty(path)) return Commands.SetPlan(id, null);
        if (Path.IsPathRooted(path) || path.StartsWith('/') || path.Split('/').Contains("..")) return (false, "A plan is a path inside the repository, like DevResources/plans/name.md");
        return Commands.SetPlan(id, path);
    }
    /// <summary>
    /// Points the plan and the linked documents of every work item, archived ones included, to the path <paramref name="newPath"/> gives for them (null = leave it as it is),
    /// for when a folder of plans moves. Records no event and touches nothing else in the item. Returns how many links changed.
    /// </summary>
    public static int RewritePlanLinks(Func<string, string?> newPath) => Commands.RewritePaths(newPath);
    /// <summary>Puts the item in one feature (null = none); a work item has one feature at most.</summary>
    public static (bool Success, string Message) MoveFeature(int id, string? to) => Commands.MoveFeature(id, to);

    /// <summary>Ids of open (not archived) work items with a delta spec folder for the feature; DeleteFeature deletes those folders too.</summary>
    public static IReadOnlyList<int> FeatureSpecOwners(string path) => Commands.FeatureSpecOwners(path);

    /// <summary>Deletes a feature (see FeaturesApi.DeleteFeature) and its delta spec folders in open changes, and unlinks it from active work items, which become unassigned.</summary>
    public static (bool Success, string Message) DeleteFeature(string path) => Commands.DeleteFeature(path);
    public static (bool Success, string Message, string? NewPath) RenameFeature(string path, string name) => Commands.RenameFeature(path, name);
    public static (bool Success, string Message) SetLinkedCommits(int id, IEnumerable<string> hashes) => Commands.SetLinkedCommits(id, hashes);

    public static (bool Success, string Message) SetLinkedTests(int id, IEnumerable<TestLink> tests) => Commands.SetLinkedTests(id, tests);

    /// <summary>Replaces the item's linked documents (x-rtm.docs): repo-relative paths, slashes normalised, trimmed, distinct. A path outside the repository is refused.</summary>
    public static (bool Success, string Message) SetLinkedDocs(int id, IEnumerable<string> paths) => Commands.SetLinkedDocs(id, paths);

    // ── Events ──
    /// <summary>Who an event is recorded as: the actor's name, trimmed ("someone" when it is null or blank). Null restores the default (nobody configured).</summary>
    public static void ConfigureActor(Func<string?>? actor) => WorkItemsModule.Actor = actor;

    /// <summary>Records the events the code running from here writes as <paramref name="who"/> (an agent's tool call) until the scope is disposed. Only this call is affected: the actor <see cref="ConfigureActor"/> set (the app's user), and the actor of every other call, stay.</summary>
    public static IDisposable ActingAs(string who) => WorkItemsModule.ActingAs(who);

    /// <summary>
    /// What happened to the item, oldest first, from events.log in its change folder (empty for an unknown item or no log). The app records, with
    /// these kinds and texts: <c>created</c> (the title), <c>status</c> ("Old -> New"), <c>commit-linked</c> / <c>commit-unlinked</c> (the hash),
    /// <c>test-linked</c> / <c>test-unlinked</c> ("Suite / Test"), <c>doc-linked</c> / <c>doc-unlinked</c> (the repo-relative path),
    /// <c>subtask-done</c> / <c>subtask-reopened</c> / <c>subtask-added</c> ("number text"), <c>dependency-added</c> / <c>dependency-removed</c> ("#wiN").
    /// Other kinds can be added with <see cref="LogEvent"/>.
    /// </summary>
    public static IReadOnlyList<WorkItemEvent> GetEvents(int id) => WorkItemsModule.Events?.Invoke(id) ?? ReadEvents(id);

    /// <summary>The item's events read from events.log now (nothing kept): what the host's cache of events calls when it has to read.</summary>
    public static IReadOnlyList<WorkItemEvent> ReadEvents(int id) => Queries.Get(id) is { } item ? Infrastructure.EventLog.Read(item.Path) : [];

    /// <summary>Appends one event (now, by the configured actor) to the item's events.log. A blank kind is refused.</summary>
    public static (bool Success, string Message) LogEvent(int id, string kind, string text) => Commands.LogEvent(id, kind, text);

    public static (bool Success, string Message) SetDescription(int id, string body) => Commands.SetDescription(id, body);

    // ── Subtasks ──
    public static (bool Success, string Message) SetSubtaskDone(int id, string number, bool done) => Commands.SetSubtaskDone(id, number, done);
    public static (bool Success, string Message) SetSubtaskText(int id, string number, string text) => Commands.SetSubtaskText(id, number, text);
    public static (bool Success, string Message) AddSubtask(int id, string text) => Commands.AddSubtask(id, text);
    /// <summary>Takes the subtask out of tasks.md (the rest of its group is renumbered) and records <c>subtask-removed</c> with its number and text.</summary>
    public static (bool Success, string Message) RemoveSubtask(int id, string number) => Commands.RemoveSubtask(id, number);

    /// <summary>Marks the agent's work on an item as reviewed (x-rtm.reviewedAt = now).</summary>
    public static (bool Success, string Message) MarkReviewed(int id) => Commands.MarkReviewed(id);

    /// <summary>An agent created or changed this item: x-rtm.agentBy = <paramref name="agent"/>, agentAt = now.</summary>
    public static (bool Success, string Message) StampAgent(int id, string agent) => Commands.StampAgent(id, agent);

    /// <summary>Writes the change's delta spec for a capability (specs/&lt;capability&gt;/spec.md); null deletes it. skip_specs is kept in sync.</summary>
    public static (bool Success, string Message) SetDeltaSpec(int id, string capability, string? markdown) => Commands.SetDeltaSpec(id, capability, markdown);

    /// <summary>Runs the pinned OpenSpec CLI, <c>validate &lt;folder&gt; --strict</c>, in the repository root; Message is its output.</summary>
    public static (bool Success, string Message) Validate(int id) => Commands.Validate(id);

    /// <summary>How <see cref="Validate"/> runs the CLI; tests replace it so npx never starts. Null restores the CLI.</summary>
    public static void ConfigureValidator(Func<string, (bool Success, string Message)>? validate) =>
        Commands.Validator = validate ?? Infrastructure.OpenSpecCli.Validate;

    /// <summary>Takes the agent stamp off one subtask.</summary>
    public static (bool Success, string Message) MarkSubtaskReviewed(int id, string number) => Commands.MarkSubtaskReviewed(id, number);
    public static (bool Success, string Message, int NewId) MoveSubtasksToNew(int id, IReadOnlyCollection<string> numbers) => Commands.MoveSubtasksToNew(id, numbers);
    /// <summary>Like MoveSubtasksToNew, but the new item becomes a dependency and the source moves to Blocked until it is archived.</summary>
    public static (bool Success, string Message, int NewId) MoveSubtasksToDependency(int id, IReadOnlyCollection<string> numbers) => Commands.MoveSubtasksToDependency(id, numbers);
    public static (bool Success, string Message) SetDependencies(int id, IEnumerable<int> ids) => Commands.SetDependencies(id, ids);
    /// <summary>Work items that depend on this one (its x-rtm.dependsOn names them).</summary>
    public static IReadOnlyList<WorkItem> GetBlocking(int id) => Queries.GetBlocking(id);

    /// <summary>A dependency stops blocking once it's In Review or Completed (archived).</summary>
    public static bool IsResolved(WorkItem dependency) => dependency.Archived || dependency.Status is "Completed" or "In Review";

    /// <summary>Makes an existing work item a dependency of this one; this one goes to Blocked only if that item is unresolved.</summary>
    public static (bool Success, string Message) AddDependency(int id, int dependencyId) => Commands.AddDependency(id, dependencyId);

    /// <summary>Stops waiting on that item. The status is left as it is (Blocked stays Blocked until changed).</summary>
    public static (bool Success, string Message) RemoveDependency(int id, int dependencyId) =>
        Get(id) is { } item ? Commands.SetDependencies(id, (item.DependsOn ?? []).Where(d => d != dependencyId)) : (false, "That work item no longer exists");

    // ── The rules every caller shares (the app, an agent's tool call) ──
    /// <summary>The priorities a work item can have, the highest first.</summary>
    public static IReadOnlyList<string> Priorities => ItemRules.Priorities;
    /// <summary>The statuses a new item can start in: the project's own, except Blocked, In Review and Completed.</summary>
    public static IReadOnlyList<string> CreationStatuses() => ItemRules.CreationStatuses();
    /// <summary>Why an agent may not move an item to this status (Completed is archiving, the user's; or the project has no such status), or null.</summary>
    public static string? CheckAgentStatus(string status) => ItemRules.CheckAgentStatus(status);
    public static string? CheckLabel(string? label) => ItemRules.CheckLabel(label);
    public static string? CheckPriority(string? priority) => ItemRules.CheckPriority(priority);
    /// <summary>Why the ids cannot be dependencies of the item <paramref name="self"/> (null for a new one): one that does not exist, or itself; null when they can.</summary>
    public static string? CheckDependencies(int? self, IEnumerable<int>? ids) => ItemRules.CheckDependencies(self, ids);
    /// <summary>Why a name cannot be an agent's (letters, digits, spaces, dots, dashes and underscores, at most 40), or null.</summary>
    public static string? CheckAgentName(string name) => ItemRules.CheckAgentName(name);
    /// <summary>The feature the item is in (its first; an item has one at most), or null.</summary>
    public static string? FeatureOf(WorkItem item) => ItemRules.FeatureOf(item);
    /// <summary>The item can be started now: not In Progress, not archived, and nothing it waits on is unresolved.</summary>
    public static bool IsReady(WorkItem item) => ItemRules.IsReady(item);
    /// <summary>The contributors to write when work starts or they are edited: the list given (or the item's own) plus <paramref name="who"/> when <paramref name="status"/> is In Progress; null when neither applies.</summary>
    public static IReadOnlyList<string>? ContributorsAfter(WorkItem item, IEnumerable<string>? given, string? status, string who) => ItemRules.ContributorsAfter(item, given, status, who);
    /// <summary>The linked documents after an edit: the list given (else <paramref name="current"/>) tidied (slashes, blanks, repeats), the ones to remove taken out, the ones to add put at the end.</summary>
    public static IReadOnlyList<string> DocsAfter(IEnumerable<string>? current, IEnumerable<string>? replace, IEnumerable<string>? add, IEnumerable<string>? remove) => ItemRules.DocsAfter(current, replace, add, remove);
    /// <summary>A warning for each document that is neither a file nor a folder of the open repository yet.</summary>
    public static IReadOnlyList<string> MissingDocs(IEnumerable<string> paths) => ItemRules.MissingDocs(paths);
    /// <summary>The delta specs of the item's change folder, by capability; with <paramref name="withText"/> their markdown too.</summary>
    public static IReadOnlyList<DeltaSpecFile> GetDeltaSpecs(WorkItem item, bool withText) => ItemRules.DeltaSpecs(item, withText);
    /// <summary>"cancelled", "duplicate of #wiN" or "superseded by #wiN" as the text for the worklog and the title of the decision note; refused for another text, for the item itself, or an item that does not exist.</summary>
    public static Resolution ParseResolution(string resolution, int id) => ItemRules.ParseResolution(resolution, id);

    /// <summary>
    /// A new item titled "New work item" (or <paramref name="title"/>): the one road both the app's "+" and an agent take. One that starts In Progress is created Planned and then moved,
    /// so it has its start day, its move and its status event like any item that starts work.
    /// </summary>
    public static (bool Success, string Message, int Id) CreateNew(string status = "Backlog", IEnumerable<string>? features = null, IEnumerable<string>? contributors = null, string? folder = null, string? title = null) =>
        NewItems.CreateNew(status, features, contributors, folder, title);

    /// <summary>
    /// Creates an item from an agent's brief: checks it (the Why's 50 to 1000 characters, the starting status, label, priority, dependencies, capabilities, the kebab-case folder that must not exist yet),
    /// writes the change folder (description, plan, dependencies, signed subtasks in tasks.md, delta specs), signs it as <paramref name="who"/> and runs the validator.
    /// </summary>
    public static CreatedWorkItem CreateFromBrief(NewWorkItem brief, string who) => NewItems.FromBrief(brief, who);

    /// <summary>Adds a subtask signed as the agent's (one line, ending in the agent mark), logged like <see cref="AddSubtask(int, string)"/>.</summary>
    public static (bool Success, string Message) AddSubtask(int id, string text, string agent) => Commands.AddSubtask(id, ItemRules.Signed(text, agent));
    /// <summary>Retexts a subtask, signed as the agent's.</summary>
    public static (bool Success, string Message) SetSubtaskText(int id, string number, string text, string agent) => Commands.SetSubtaskText(id, number, ItemRules.Signed(text, agent));
    /// <summary>Removes the subtasks, highest number first (numbers sort as 2.10 after 2.9), so renumbering the rest of a group never shifts one still to remove. One result per number, each done as it is read (stop reading at the first failure and the rest stays).</summary>
    public static IEnumerable<(string Number, bool Success, string Message)> RemoveSubtasks(int id, IEnumerable<string> numbers) =>
        numbers.Distinct().OrderByDescending(ItemRules.SubtaskOrder).Select(n => { (bool ok, string m) = Commands.RemoveSubtask(id, n); return (n, ok, m); });

    /// <summary>
    /// The item's status at the end of each moment given (null where it did not exist yet), read from its events: the last status move before it, else the status it was created with.
    /// An item archived by the CLI with no move to Completed in its log counts as Completed from when it was last modified.
    /// </summary>
    public static IReadOnlyList<string?> StatusAt(WorkItem item, IReadOnlyList<WorkItemEvent> events, IReadOnlyList<DateTime> ends) => ItemRules.StatusAt(item, events, ends);

    /// <summary>The ids of the items that changed from one day to another, both included, by local day (with times: the exact window): some of the times <paramref name="logged"/> gives for it, its creation, or its files' last write falls in it.</summary>
    public static IReadOnlySet<int> ChangedIn(IReadOnlyList<WorkItem> items, Func<WorkItem, IEnumerable<DateTime>> logged, DateOnly from, DateOnly to,
        TimeOnly? fromTime = null, TimeOnly? toTime = null, Func<WorkItem, bool>? hasCreated = null) => ItemRules.ChangedIn(items, logged, from, to, fromTime, toTime, hasCreated);

    /// <summary>The filter group a worklog entry of this event kind answers to: "events", "links", "subtasks" or "edits".</summary>
    public static string EventGroup(string kind) => ItemRules.EventGroup(kind);

    // ── Whole items ──
    public static (bool Success, string Message, int Id) Create(string title, string status = "Backlog", IEnumerable<string>? features = null, IEnumerable<string>? contributors = null, string? folder = null)
        => Commands.Create(title, status, features, contributors, folder);
    /// <summary>Whether a change folder of that name exists, under changes/ or (any date) under changes/archive/.</summary>
    public static bool FolderExists(string name) => Commands.FolderExists(name);
    public static (bool Success, string Message, int Id) Merge(IReadOnlyList<int> ids) => Commands.Merge(ids);
    /// <summary>Merges the items into one that keeps the title and description of <paramref name="into"/> (one of <paramref name="ids"/>); the others' descriptions become notes on it.</summary>
    public static (bool Success, string Message, int Id) Merge(IReadOnlyList<int> ids, int into) => Commands.Merge(ids, into);
    /// <summary>"Delete" without data loss: marks x-rtm.hidden so the board skips it; the change folder stays.</summary>
    public static (bool Success, string Message) Hide(int id) => Commands.Update(id, m => m.Hidden = true);
    public static (bool Success, string Message) Unhide(int id) => Commands.Update(id, m => m.Hidden = null);

    // ── Lanes (custom sub-categories in board columns) ──
    public static (bool Success, string Message) AddLane(string column, string lane) => Commands.AddLane(column, lane);
    public static (bool Success, string Message) RemoveLane(string column, string lane) => Commands.EditLanes(column, l => l.Remove(lane));
    public static (bool Success, string Message) SetLane(int id, string column, string lane) => Commands.SetLane(id, column, lane);
}
