using System.ComponentModel;
using System.Globalization;
using System.Text.RegularExpressions;
using Features;
using Features.PublicDTOs;
using GitHistory;
using GitHistory.PublicDTOs;
using LinkedTestRuns;
using LinkedTestRuns.PublicDTOs;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using Notes;
using Notes.PublicDTOs;
using OpenSpecStore;
using WorkItems;
using WorkItems.PublicDTOs;

namespace TaskManager.Mcp;

/// <summary>
/// The board as eight tools over the module facades. Every call resolves its repository, takes the one global lock,
/// makes that repository current for the static module state, runs, and puts the previous one back.
/// </summary>
[McpServerToolType]
public sealed partial class BoardTools
{
    // ponytail: one global lock; per-repo locks if agents ever queue behind each other
    private static readonly SemaphoreSlim Gate = new(1, 1);

    private static readonly string[] CreateStatuses = ["Backlog", "Planned", "Confirmed", "In Progress"];
    private static readonly string[] Priorities = ["high", "low"];

    private const string RepoHelp = "Any path inside the repository or a git worktree of it; the folder holding openspec/ is found by walking up. Default: the RTM_REPO environment variable, else the server's working directory.";
    private const string AgentHelp = "Your name, written as the author of this change (default: the RTM_AGENT environment variable, else Claude). Letters, digits, spaces, dots, dashes and underscores only.";
    private const string FormatHelp = "concise (default) or detailed.";
    private const string RevisionHelp = "The revision board_get returned; the write is refused when the item changed since.";

    // ── Reads ──

    [McpServerTool(Name = "board_list", ReadOnly = true, UseStructuredContent = true)]
    [Description("List work items, filtered, ordered by id. Concise rows: id, title, status, feature, label, revision. Page with `after` (the last id of the previous page); `nextAfter` in the result says there is more.")]
    public Task<ListResult> BoardList(
        [Description("Only these statuses (Backlog, Planned, Confirmed, In Progress, Blocked, In Review, Completed).")] string[]? status = null,
        [Description("Only items in this feature (capability path, like functional/banking).")] string? feature = null,
        [Description("Only this label: bug, feature or enhancement.")] string? label = null,
        [Description("Only items this person or agent contributes to.")] string? contributor = null,
        [Description("Only items whose title contains this text.")] string? text = null,
        [Description("true: only items that can be started now (not In Progress, not waiting on an unresolved dependency).")] bool ready = false,
        [Description("Include archived (Completed) items. Asking for status Completed includes them too.")] bool includeArchived = false,
        [Description("The id of the last item of the previous page.")] int? after = null,
        [Description("Page size, default 50, at most 200.")] int limit = 50,
        [Description(FormatHelp)] string? format = null,
        [Description(RepoHelp)] string? repo = null) =>
        Run(repo, _ =>
        {
            bool detailed = IsDetailed(format);
            bool archived = includeArchived || (status?.Contains("Completed", StringComparer.OrdinalIgnoreCase) ?? false);
            List<WorkItem> hits = WorkItemsApi.GetAll()
                .Where(i => archived || !i.Archived)
                .Where(i => status is not { Length: > 0 } || status.Contains(i.Status, StringComparer.OrdinalIgnoreCase))
                .Where(i => string.IsNullOrWhiteSpace(feature) || string.Equals(FeatureOf(i), feature.Trim(), StringComparison.OrdinalIgnoreCase))
                .Where(i => string.IsNullOrWhiteSpace(label) || string.Equals(i.Label, label.Trim(), StringComparison.OrdinalIgnoreCase))
                .Where(i => string.IsNullOrWhiteSpace(contributor) || i.Contributors.Contains(contributor.Trim(), StringComparer.OrdinalIgnoreCase))
                .Where(i => string.IsNullOrWhiteSpace(text) || i.Title.Contains(text.Trim(), StringComparison.OrdinalIgnoreCase))
                .Where(i => !ready || IsReady(i))
                .OrderBy(i => i.Id).ThenBy(i => i.Folder, StringComparer.Ordinal)
                .ToList();
            int size = Math.Clamp(limit, 1, 200);
            List<WorkItem> page = hits.Where(i => after is null || i.Id > after).Take(size).ToList();
            bool more = page.Count > 0 && hits.Any(i => i.Id > page[^1].Id);
            return new ListResult(hits.Count, page.Count, more ? page[^1].Id : null, [.. page.Select(i => Row(i, detailed))]);
        });

    [McpServerTool(Name = "board_get", ReadOnly = true, UseStructuredContent = true)]
    [Description("One work item: header fields (always), description (proposal body; concise cuts it to 600 characters, `descriptionLength` and `descriptionCut` say so), subtasks with numbers and done flags (`subtasksDone` of `subtasksTotal`), notes, the `worklog` (its history: notes and events together, newest first; concise gives the newest 20 of `worklogTotal`), delta specs, linked tests with their outcome in the latest results (✓ passed, ✗ failed, ? not in the results), linked commits plus commits mentioning #wiN, linked `docs`, dependencies and what it blocks, and `revision` (pass it to board_update or board_note). Detailed adds note bodies, the whole worklog and delta spec text. Read only what you need: `sections` picks parts, `since` and `limit` cut the worklog and notes. Example: sections [\"specs\"] returns the delta spec markdown to edit; sections [\"worklog\",\"notes\"] with since \"2026-11-09T14:00\" returns what is new.")]
    public Task<GetResult> BoardGet(
        [Description("The work item number (the N of #wiN).")] int id,
        [Description(FormatHelp)] string? format = null,
        [Description("Only these parts, besides the header fields and revision, which always come back: header, description, subtasks, notes, worklog, specs (delta specs with their markdown, in any format), tests, links (commits, docs, dependsOn, blocking). Leave out for everything. The parts not asked for come back null or empty.")] string[]? sections = null,
        [Description("An ISO date or date-time (2026-11-09 or 2026-11-09T14:30): the worklog and notes only from that moment on. `worklogTotal` still counts every entry.")] string? since = null,
        [Description("At most this many worklog entries and notes, the newest first. Without it concise gives 20 worklog entries and detailed all.")] int? limit = null,
        [Description(RepoHelp)] string? repo = null) =>
        Run(repo, _ =>
        {
            bool detailed = IsDetailed(format);
            HashSet<string>? wanted = Sections(sections);
            bool Want(string section) => wanted is null || wanted.Contains(section);
            DateTime? from = Since(since);
            if (limit is < 1) throw new McpException("`limit` must be 1 or more.");
            WorkItem item = Item(id);
            TestRun run = LinkedTestRunsApi.Latest();
            List<CommitRow> commits = [];
            if (Want("links"))
            {
                foreach (CommitInfo c in GitHistoryApi.Commits([.. item.Commits ?? []])) commits.Add(Commit(c, false));
                foreach (CommitInfo c in GitHistoryApi.Mentioning($"#wi{id}"))
                    if (!commits.Any(k => k.Hash.StartsWith(c.Hash, StringComparison.OrdinalIgnoreCase) || c.Hash.StartsWith(k.Hash, StringComparison.OrdinalIgnoreCase))) commits.Add(Commit(c, true));
            }
            (IReadOnlyList<WorklogRow> worklog, int worklogTotal) = Worklog(id, detailed, Want("worklog"), from, limit);
            string description = WorkItemsApi.GetDescription(id);
            bool cut = !detailed && description.Length > ConciseDescription;
            IEnumerable<Note> notes = Want("notes") ? NotesApi.GetForWorkItem(id).Where(n => from is null || n.Created >= from).OrderBy(n => n.Id) : [];
            if (limit is int newest) notes = notes.OrderByDescending(n => n.Id).Take(newest).OrderBy(n => n.Id);
            return new GetResult(
                item.Id, item.Folder, item.Title, item.Status, item.Priority, item.Label, FeatureOf(item),
                item.Contributors, item.Plan, item.Started?.ToString("yyyy-MM-dd"), item.Due?.ToString("yyyy-MM-dd"), item.Moved?.ToString("yyyy-MM-dd"), item.Created.ToString("yyyy-MM-dd"),
                item.AgentBy, item.AgentAt?.ToString("s", CultureInfo.InvariantCulture), item.AgentUnreviewed, item.Archived, item.Hidden,
                RevisionOf(item), Want("description") ? (cut ? description[..ConciseDescription] : description) : null, description.Length, cut,
                item.Subtasks.Count(s => s.Done), item.Subtasks.Count,
                Want("subtasks") ? [.. item.Subtasks.Select(s => new SubtaskRow(s.Number, s.Text, s.Done, s.Group, s.Agent))] : [],
                [.. notes.Select(n => new NoteRow(n.Id, n.Title, n.By, n.Created.ToString("s", CultureInfo.InvariantCulture), detailed ? n.Body : null, n.Kind))],
                Want("specs") ? DeltaSpecs(item, detailed || wanted is not null && wanted.Contains("specs")) : [],
                Want("tests") ? run.File : null, Want("tests") ? [.. (item.Tests ?? []).Select(t => TestOutcome(run, t))] : [],
                commits,
                Want("links") ? [.. (item.DependsOn ?? []).Select(Dependency)] : [],
                Want("links") ? [.. WorkItemsApi.GetBlocking(id).Select(b => new DependencyRow(b.Id, b.Title, b.Status, WorkItemsApi.IsResolved(b)))] : [],
                Want("links") ? item.Docs ?? [] : [], worklog, worklogTotal);
        });

    [McpServerTool(Name = "board_features", ReadOnly = true, UseStructuredContent = true)]
    [Description("The features (capabilities under openspec/specs, plus the ones added from the board) with the open work items on each. Use a feature path as `feature` when creating an item; invent a new kebab-case name only for a capability that is really new. To write a MODIFIED requirement you must repeat the existing one: format detailed with `feature` returns that capability's `requirements` (names) from its main spec, and `requirement` adds that requirement's full text with its scenarios in `requirementText`. Example: format \"detailed\", feature \"functional/banking\", requirement \"Banking exists\".")]
    public Task<FeaturesResult> BoardFeatures(
        [Description(FormatHelp + " Detailed adds name, group and whether it was added from the board; it is also needed to read a capability's spec.")] string? format = null,
        [Description("With format detailed: the capability path (like functional/banking) whose main spec to read; the result lists its requirement names.")] string? feature = null,
        [Description("With `feature`: the name of one requirement (case does not matter); the result carries its full text with its scenarios.")] string? requirement = null,
        [Description(RepoHelp)] string? repo = null) =>
        Run(repo, _ =>
        {
            bool detailed = IsDetailed(format);
            bool reading = !string.IsNullOrWhiteSpace(feature) || !string.IsNullOrWhiteSpace(requirement);
            if (reading && !detailed) throw new McpException("Reading a capability's spec needs format \"detailed\": pass format \"detailed\" with `feature` (and `requirement`).");
            if (string.IsNullOrWhiteSpace(feature) && !string.IsNullOrWhiteSpace(requirement)) throw new McpException("`requirement` needs `feature`, the capability path whose spec holds it (see board_features).");
            IReadOnlyList<WorkItem> open = [.. WorkItemsApi.GetAll().Where(i => !i.Archived)];
            IEnumerable<Features.PublicDTOs.Feature> all = FeaturesApi.GetAll();
            IReadOnlyList<string>? names = null;
            string? text = null;
            if (!string.IsNullOrWhiteSpace(feature))
            {
                string path = feature.Replace('\\', '/').Trim('/', ' ');
                Features.PublicDTOs.Feature found = all.FirstOrDefault(f => string.Equals(f.Path, path, StringComparison.OrdinalIgnoreCase))
                    ?? throw new McpException($"There is no feature \"{feature}\". The features are: {string.Join(", ", all.Select(f => f.Path))}.");
                string file = Path.Combine(OpenSpecStoreApi.OpenSpecRoot!, "specs", found.Path.Replace('/', Path.DirectorySeparatorChar), "spec.md");
                if (!File.Exists(file)) throw new McpException($"The feature {found.Path} has no spec yet (it was added from the board): there are no requirements to read.");
                List<(string Name, string Text)> requirements = Requirements(OpenSpecStoreApi.ReadText(file));
                names = [.. requirements.Select(r => r.Name)];
                if (!string.IsNullOrWhiteSpace(requirement))
                {
                    string want = requirement.Trim();
                    if (want.StartsWith("Requirement:", StringComparison.OrdinalIgnoreCase)) want = want["Requirement:".Length..].Trim();
                    text = requirements.FirstOrDefault(r => string.Equals(r.Name, want, StringComparison.OrdinalIgnoreCase)).Text
                        ?? throw new McpException($"{found.Path} has no requirement \"{requirement}\". Its requirements are: {string.Join("; ", names)}.");
                }
                all = [found];
            }
            return new FeaturesResult([.. all.Select(f => new FeatureRow(
                f.Path,
                [.. open.Where(i => string.Equals(FeatureOf(i), f.Path, StringComparison.OrdinalIgnoreCase)).OrderBy(i => i.Id).Select(i => new FeatureItem(i.Id, i.Title, i.Status))],
                detailed ? f.Name : null, detailed ? f.Group : null, detailed ? f.Custom : null))], names, text);
        });

    // ── Writes ──

    [McpServerTool(Name = "board_create", Destructive = false, Idempotent = false, UseStructuredContent = true)]
    [Description("Create a work item: one result that can be finished and archived on its own (steps toward it are `subtasks`). Allocates the next id, writes a valid change folder (proposal, tasks, delta specs), signs it as yours, adds you to contributors, runs the OpenSpec validator and returns id, folder and the validator's output. Not for archiving: the user does that from the app.")]
    public Task<CreateResult> BoardCreate(
        [Description("Short title.")] string title,
        [Description("The problem or opportunity, 50 to 1000 characters (the proposal's Why).")] string why,
        [Description("What changes, in markdown, usually a bullet list (the proposal's What Changes).")] string whatChanges,
        [Description("The feature (capability) this belongs to; see board_features. One per item.")] string? feature = null,
        [Description("bug, feature or enhancement.")] string? label = null,
        [Description("Backlog (default), Planned, Confirmed or In Progress. Use In Progress when you start now.")] string status = "Backlog",
        [Description("high or low (default low).")] string? priority = null,
        [Description("Repo-relative path of the plan file you wrote, like DevResources/plans/name-2026-11-09.md.")] string? plan = null,
        [Description("Ids of the work items this one waits for.")] int[]? dependsOn = null,
        [Description("The steps, each with an optional group heading; they are signed as yours. Leave out for none.")] SubtaskInput[]? subtasks = null,
        [Description("Delta specs by capability, e.g. {\"billing\": \"## ADDED Requirements\\n\\n### Requirement: ...\"}. Needed when the work changes what the product does; each requirement has SHALL or MUST and a #### Scenario with WHEN and THEN lines. Use the feature's own path as the key.")] Dictionary<string, string>? specs = null,
        [Description("The change folder name under openspec/changes: kebab-case (lowercase letters, digits and dashes); it must not exist yet. Default: the first words of the title (at most 6 words and 48 characters).")] string? folder = null,
        [Description(AgentHelp)] string? agent = null,
        [Description(RepoHelp)] string? repo = null) =>
        Run(repo, root =>
        {
            string who = AgentName(agent);
            ActAs(who);
            if (string.IsNullOrWhiteSpace(title)) throw new McpException("Give the work item a title.");
            if (why is null || why.Trim().Length is < 50 or > 1000) throw new McpException($"`why` must be 50 to 1000 characters (it is {why?.Trim().Length ?? 0}): say why the work is needed in a few sentences. OpenSpec rejects anything else.");
            if (string.IsNullOrWhiteSpace(whatChanges)) throw new McpException("`whatChanges` is empty: list what changes, e.g. \"- Each API key gets 100 requests a minute.\"");
            if (!CreateStatuses.Contains(status)) throw new McpException($"Status \"{status}\" cannot be used to create an item: use {string.Join(", ", CreateStatuses)}. Done work is set to In Review with board_update; Completed is the user's, from the app.");
            CheckLabel(label);
            CheckPriority(priority);
            foreach (int dep in dependsOn ?? []) Item(dep);
            foreach (string cap in (specs ?? []).Keys) CheckCapability(cap);
            if ((subtasks ?? []).FirstOrDefault(s => string.IsNullOrWhiteSpace(s.Text)) is not null) throw new McpException("Every subtask needs `text`.");
            if (!string.IsNullOrWhiteSpace(folder))
            {
                folder = folder.Trim();
                if (folder.Length > 80 || !FolderPattern().IsMatch(folder)) throw new McpException($"Folder \"{folder}\" is not kebab-case: use lowercase letters, digits and single dashes, like rate-limit-api, at most 80 characters.");
                if (WorkItemsApi.FolderExists(folder)) throw new McpException($"A change folder named \"{folder}\" exists already (under changes/ or changes/archive/): choose another name, or leave `folder` out.");
            }
            else folder = null;

            List<string> warnings = [];
            (bool ok, string message, int id) = WorkItemsApi.Create(title.Trim(), status == "In Progress" ? "Planned" : status, string.IsNullOrWhiteSpace(feature) ? null : [feature.Trim()], [who], folder);
            if (!ok) throw new McpException($"Could not create the work item: {message}");
            string Created() => $"Work item #wi{id} was created but";
            void Step(string name, (bool Success, string Message) r) { if (!r.Success) throw new McpException($"{Created()} {name} failed: {r.Message}. Fix it with board_update (id {id})."); }

            if (status == "In Progress") Step("starting it", WorkItemsApi.SetStatus(id, status));
            Step("writing the description", WorkItemsApi.SetDescription(id, $"## Why\n\n{why.Trim()}\n\n## What Changes\n\n{whatChanges.Trim()}\n"));
            if (priority is not null) Step("setting the priority", WorkItemsApi.SetPriority(id, priority));
            if (!string.IsNullOrEmpty(label)) Step("setting the label", WorkItemsApi.SetLabel(id, label));
            if (!string.IsNullOrWhiteSpace(plan))
            {
                Step("linking the plan", WorkItemsApi.SetPlan(id, plan));
                if (!File.Exists(Path.Combine(root, plan.Trim().Replace('/', Path.DirectorySeparatorChar)))) warnings.Add($"The plan file {plan.Trim()} does not exist yet; write it.");
            }
            if (dependsOn is { Length: > 0 })
            {
                Step("setting the dependencies", WorkItemsApi.SetDependencies(id, dependsOn));
                foreach (int dep in dependsOn.Where(d => !WorkItemsApi.IsResolved(Item(d)))) warnings.Add($"It waits on #wi{dep}, which is not resolved yet: consider status Blocked.");
            }
            if (subtasks is { Length: > 0 })
            {
                string tasks = TasksText(subtasks, who);
                string path = Path.Combine(Item(id).Path, "tasks.md");
                OpenSpecStoreApi.ReadText(path);
                Step("writing the subtasks", OpenSpecStoreApi.WriteText(path, tasks));
                foreach (Subtask s in Item(id).Subtasks) WorkItemsApi.LogEvent(id, "subtask-added", $"{s.Number} {s.Text}");   // written straight to tasks.md, so logged here like AddSubtask does
            }
            foreach ((string cap, string markdown) in specs ?? [])
            {
                Step($"writing the delta spec for {cap}", WorkItemsApi.SetDeltaSpec(id, cap, markdown));
                if (!string.IsNullOrWhiteSpace(feature) && !string.Equals(cap.Trim('/'), feature.Trim('/'), StringComparison.OrdinalIgnoreCase)) warnings.Add($"The delta spec is for {cap} but the feature is {feature}: use one name for both.");
            }
            Step("signing it", WorkItemsApi.StampAgent(id, who));
            (bool valid, string output) = WorkItemsApi.Validate(id);
            if (!valid) warnings.Add("The OpenSpec validator rejected the item: read `validation`, fix it with board_update, then board_validate.");
            WorkItem item = Item(id);
            return new CreateResult(id, item.Folder, valid, output, warnings, RevisionOf(item));
        });

    [McpServerTool(Name = "board_update", Destructive = false, Idempotent = true, UseStructuredContent = true)]
    [Description("Change a work item. Every field is optional; only what you pass changes. List fields (dependsOn, contributors, tests, commits, docs) are replaced by what you pass, so read board_get first; to change a list without re-sending it use testsAdd, testsRemove, docsAdd, docsRemove or commitsAdd (not together with the field they change). A test link whose suite is wrong but whose display name is unique in the latest results gets the right suite, and the warnings say so. `note` writes a worklog note in the same call, after the other changes (the hand-off in one call); the result has its `noteId`. `resolution` (cancelled, duplicate of #wiN, superseded by #wiN) hides the item, keeps its status and writes a decision note. Status changes, links and ticked subtasks are logged in the worklog under your name. Status In Progress adds you to contributors and stamps the start day; use In Review when the work is done (Completed is refused: the user archives from the app). Signs the item and your new subtasks as yours. Runs the OpenSpec validator when subtasks, description or specs changed. Pass `revision` from board_get to avoid overwriting someone else's edit.")]
    public Task<UpdateResult> BoardUpdate(
        [Description("The work item number (the N of #wiN).")] int id,
        [Description(RevisionHelp)] string? revision = null,
        [Description("New title.")] string? title = null,
        [Description("Backlog, Planned, Confirmed, In Progress, Blocked or In Review. Not Completed.")] string? status = null,
        [Description("high or low.")] string? priority = null,
        [Description("bug, feature or enhancement; empty string removes it.")] string? label = null,
        [Description("Move the item to this feature (capability path); empty string removes it from its feature.")] string? feature = null,
        [Description("Start day, yyyy-MM-dd; empty string clears it.")] string? started = null,
        [Description("Due day, yyyy-MM-dd; empty string clears it.")] string? due = null,
        [Description("Repo-relative path of the plan file; empty string removes the link.")] string? plan = null,
        [Description("Ids of the items this one waits for (replaces the list; empty array clears it).")] int[]? dependsOn = null,
        [Description("People and agents who did the work (replaces the list).")] string[]? contributors = null,
        [Description("The whole proposal body in markdown (## Why, ## What Changes); replaces the current one.")] string? description = null,
        [Description("Linked tests (replaces the list). Names not in the latest results are still written and come back as warnings.")] TestInput[]? tests = null,
        [Description("Hashes of commits that do not mention #wiN (replaces the list); commits mentioning #wiN link themselves.")] string[]? commits = null,
        [Description("Linked documents: repo-relative paths like docs/guide.md (replaces the list). A path that does not exist yet is written and comes back as a warning.")] string[]? docs = null,
        [Description("Linked tests to add to the current list (same shape as `tests`); not together with `tests`.")] TestInput[]? testsAdd = null,
        [Description("Linked tests to remove from the current list (matched on suite and test); not together with `tests`.")] TestInput[]? testsRemove = null,
        [Description("Documents to add to the current list; not together with `docs`.")] string[]? docsAdd = null,
        [Description("Documents to remove from the current list; not together with `docs`.")] string[]? docsRemove = null,
        [Description("Commit hashes to add to the current list; not together with `commits`.")] string[]? commitsAdd = null,
        [Description("true hides the item from the board (the folder stays), false shows it again.")] bool? hidden = null,
        [Description("How the item ends without being done: \"cancelled\", \"duplicate of #wiN\" or \"superseded by #wiN\". Hides the item (as `hidden: true`), keeps its status, writes a decision note titled Cancelled / Duplicate of #wiN / Superseded by #wiN and logs it.")] string? resolution = null,
        [Description("Why, as the body of the resolution note (default: the resolution itself).")] string? resolutionWhy = null,
        [Description("A worklog note written after the other changes of this call, like board_note: body, optional title, optional kind \"decision\". The result reports its note id.")] NoteInput? note = null,
        [Description("Subtask edits: with `number`, tick/untick (`done`) or retext (`text`) that subtask; without, add a new signed subtask (appended to the last group).")] SubtaskInput[]? subtasks = null,
        [Description("Delta specs by capability: markdown writes or replaces it, null deletes it.")] Dictionary<string, string?>? specs = null,
        [Description(AgentHelp)] string? agent = null,
        [Description(RepoHelp)] string? repo = null) =>
        Run(repo, root =>
        {
            string who = AgentName(agent);
            ActAs(who);
            WorkItem item = Item(id);
            if (WorkItemsApi.CheckRevision(id, revision) is (false, string stale)) throw new McpException(stale);
            if (status == "Completed") throw new McpException("Completed means archived, and the user archives from the app: set In Review when the work is done.");
            if (status is not null && !WorkItemsApi.Statuses.Contains(status)) throw new McpException($"Unknown status \"{status}\": use {string.Join(", ", WorkItemsApi.Statuses.Where(s => s != "Completed"))}.");
            CheckPriority(priority);
            CheckLabel(label);
            DateOnly? startedDay = Day(started, "started"), dueDay = Day(due, "due");
            foreach (int dep in dependsOn ?? []) { if (dep == id) throw new McpException("An item cannot depend on itself."); Item(dep); }
            foreach (string cap in (specs ?? []).Keys) CheckCapability(cap);
            if (tests is not null && (testsAdd is not null || testsRemove is not null)) throw new McpException("Pass either `tests` (replaces the whole list) or `testsAdd` / `testsRemove` (change the current list), not both.");
            if (docs is not null && (docsAdd is not null || docsRemove is not null)) throw new McpException("Pass either `docs` (replaces the whole list) or `docsAdd` / `docsRemove` (change the current list), not both.");
            if (commits is not null && commitsAdd is not null) throw new McpException("Pass either `commits` (replaces the whole list) or `commitsAdd` (adds to the current list), not both.");
            (string Event, string Title)? ended = resolution is null ? null : ParseResolution(resolution, id);
            if (ended is not null && hidden == false) throw new McpException("`resolution` hides the item: do not pass `hidden: false` with it.");
            string? noteKind = note is null ? null : NoteKind(note.Body, note.Kind, null);
            foreach (SubtaskInput s in subtasks ?? [])
            {
                if (s.Number is null && string.IsNullOrWhiteSpace(s.Text)) throw new McpException("A new subtask needs `text`; to change an existing one give its `number` (see board_get).");
                if (s.Number is not null && !item.Subtasks.Any(k => k.Number == s.Number)) throw new McpException($"There is no subtask {s.Number} on #wi{id}; its subtasks are {(item.Subtasks.Count == 0 ? "none" : string.Join(", ", item.Subtasks.Select(k => k.Number)))}.");
                if (s.Number is not null && s.Done is null && s.Text is null) throw new McpException($"Subtask {s.Number}: pass `done` and/or `text` to change it.");
            }

            List<string> applied = [], warnings = [];
            void Step(string name, (bool Success, string Message) r)
            {
                if (!r.Success) throw new McpException($"{name} failed: {r.Message}" + (applied.Count > 0 ? $" Already applied: {string.Join(", ", applied)}." : " Nothing was changed.") + " Fix the call and send the rest again.");
                applied.Add(name);
            }

            if (status is not null && status != item.Status) Step($"status {status}", WorkItemsApi.SetStatus(id, status));
            if (title is not null) Step("title", WorkItemsApi.SetTitle(id, title));
            if (priority is not null) Step("priority", WorkItemsApi.SetPriority(id, priority));
            if (label is not null) Step("label", WorkItemsApi.SetLabel(id, label));
            if (feature is not null) Step("feature", WorkItemsApi.MoveFeature(id, string.IsNullOrWhiteSpace(feature) ? null : feature.Trim()));
            if (started is not null) Step("started", WorkItemsApi.SetStarted(id, startedDay));
            if (due is not null) Step("due", WorkItemsApi.SetDue(id, dueDay));
            if (plan is not null)
            {
                Step("plan", WorkItemsApi.SetPlan(id, plan));
            }
            if (dependsOn is not null) Step("dependsOn", WorkItemsApi.SetDependencies(id, dependsOn));
            if (contributors is not null || status == "In Progress")
                Step("contributors", WorkItemsApi.SetContributors(id, [.. contributors ?? Item(id).Contributors, .. status == "In Progress" ? [who] : Array.Empty<string>()]));
            bool files = false;
            if (description is not null) { Step("description", WorkItemsApi.SetDescription(id, description.Trim() + "\n")); files = true; }
            if (tests is not null || testsAdd is not null || testsRemove is not null)
            {
                List<TestInput> list = tests is not null ? CheckTests(tests, warnings) : [.. (item.Tests ?? []).Select(t => new TestInput(t.Suite, t.Test, t.Note))];
                foreach (TestInput gone in testsRemove ?? [])
                    if (list.RemoveAll(t => t.Suite == gone.Suite && t.Test == gone.Test) == 0) warnings.Add($"Test {gone.Suite} / \"{gone.Test}\" is not linked, so it was not removed (suite and test must match the link exactly).");
                foreach (TestInput add in CheckTests(testsAdd ?? [], warnings))
                    if (!list.Any(t => t.Suite == add.Suite && t.Test == add.Test)) list.Add(add);
                Step("tests", WorkItemsApi.SetLinkedTests(id, list.Select(t => new TestLink(t.Suite, t.Test, t.Note))));
            }
            if (commits is not null || commitsAdd is not null)
                Step("commits", WorkItemsApi.SetLinkedCommits(id, [.. (commits ?? item.Commits ?? []), .. commitsAdd ?? []]));
            if (docs is not null || docsAdd is not null || docsRemove is not null)
            {
                static string Norm(string d) => d.Replace('\\', '/').Trim();
                List<string> list = [.. (docs ?? item.Docs ?? []).Select(Norm).Where(d => d.Length > 0).Distinct()];
                list.RemoveAll(d => (docsRemove ?? []).Any(r => Norm(r) == d));
                list.AddRange((docsAdd ?? []).Select(Norm).Where(d => d.Length > 0 && !list.Contains(d)).Distinct());
                Step("docs", WorkItemsApi.SetLinkedDocs(id, list));
                foreach (string doc in (docs ?? []).Concat(docsAdd ?? []).Select(Norm).Where(d => d.Length > 0).Distinct())
                    if (!File.Exists(Path.Combine(root, doc.Replace('/', Path.DirectorySeparatorChar))) && !Directory.Exists(Path.Combine(root, doc.Replace('/', Path.DirectorySeparatorChar)))) warnings.Add($"The document {doc} does not exist in the repository (yet); write it.");
            }
            if (hidden is bool h) Step(h ? "hidden" : "shown", h ? WorkItemsApi.Hide(id) : WorkItemsApi.Unhide(id));
            foreach (SubtaskInput s in subtasks ?? [])
            {
                files = true;
                if (s.Number is string number)
                {
                    if (s.Text is not null) Step($"subtask {number} text", WorkItemsApi.SetSubtaskText(id, number, Signed(s.Text, who)));
                    if (s.Done is bool done) Step($"subtask {number} {(done ? "done" : "open")}", WorkItemsApi.SetSubtaskDone(id, number, done));
                }
                else
                {
                    Step($"subtask \"{s.Text!.Trim()}\"", WorkItemsApi.AddSubtask(id, Signed(s.Text, who)));
                    if (s.Done == true && Item(id).Subtasks is [.., { } added]) Step($"subtask {added.Number} done", WorkItemsApi.SetSubtaskDone(id, added.Number, true));
                }
            }
            foreach ((string cap, string? markdown) in specs ?? [])
            {
                files = true;
                Step(markdown is null ? $"delta spec {cap} removed" : $"delta spec {cap}", WorkItemsApi.SetDeltaSpec(id, cap, markdown));
            }
            Step("agent stamp", WorkItemsApi.StampAgent(id, who));
            if (ended is { } end)
            {
                Step($"resolution {end.Event}", WorkItemsApi.Hide(id));
                WorkItemsApi.LogEvent(id, "resolution", end.Event);
                NoteResult why = WriteNote(id, who, string.IsNullOrWhiteSpace(resolutionWhy) ? $"{end.Title}." : resolutionWhy, end.Title, null, null, null, NoteKinds.Decision);
                applied.Add($"resolution note #n{why.NoteId}");
                warnings.AddRange(why.Warnings);
            }
            int? noteId = null;
            if (note is not null)
            {
                NoteResult written = WriteNote(id, who, note.Body, note.Title, null, null, null, noteKind);
                noteId = written.NoteId;
                applied.Add($"note #n{written.NoteId}");
                warnings.AddRange(written.Warnings);
                files |= written.SubtasksCreated.Count > 0;
            }

            bool? valid = null;
            string? output = null;
            if (files) (valid, output) = WorkItemsApi.Validate(id);
            if (valid == false) warnings.Add("The OpenSpec validator rejected the item: read `validation` and fix it with board_update.");
            return new UpdateResult(id, applied, warnings, valid, output, RevisionOf(Item(id)), noteId);
        });

    [McpServerTool(Name = "board_note", Destructive = false, UseStructuredContent = true)]
    [Description("Add an entry to a work item's worklog (its history). Status changes, links and ticked subtasks are logged for you; write a note yourself for what the tools cannot see: a command you ran against a server or environment and what came back (in a code block), a decision and why, a blocker, the result of a chunk of work. One short entry per event, as you go, not one summary at the end. Without a `title` the first line of the body is the title. Allocates the note id, writes it with you as author and the agent stamp, and signs the item too. Unticked `- [ ]` lines in the body become subtasks (numbers in `subtasksCreated`) and stay in the note marked '(created subtask)'. Pass `revision` from board_get to avoid writing against a stale read.")]
    public Task<NoteResult> BoardNote(
        [Description("The work item number (the N of #wiN).")] int id,
        [Description("The entry in markdown: what happened, in plain words; put command output in a code block. Write #wiN / #nN to link items and notes.")] string body,
        [Description("Short title, like \"Ran the migration on staging\". Default: the first non-empty line of the body without leading # and spaces, cut to 80 characters.")] string? title = null,
        [Description("References to list in the note's links: wi12, n9 or a doc path.")] string[]? links = null,
        [Description("Hashes of commits the note is about.")] string[]? commits = null,
        [Description("http(s) URLs to attach.")] string[]? attachments = null,
        [Description("null for an ordinary entry, or \"decision\" for a decision: line one what was decided, line two why, at most a third with what it rules out. Untitled, a decision is titled \"Decision\"; the app emphasises it in the worklog.")] string? kind = null,
        [Description(AgentHelp)] string? agent = null,
        [Description(RevisionHelp)] string? revision = null,
        [Description(RepoHelp)] string? repo = null) =>
        Run(repo, _ =>
        {
            string who = AgentName(agent);
            ActAs(who);
            Item(id);
            if (WorkItemsApi.CheckRevision(id, revision) is (false, string stale)) throw new McpException(stale);
            kind = NoteKind(body, kind, attachments);
            return WriteNote(id, who, body, title, links, commits, attachments, kind);
        });

    [McpServerTool(Name = "board_log", Destructive = false, UseStructuredContent = true)]
    [Description("Append one line to a work item's worklog: a progress event that does not deserve a note file, like \"ran the suite: 525 passed\". One line, at most 300 characters (longer or several lines: use board_note). Returns the event as logged and the item's new `revision`.")]
    public Task<LogResult> BoardLog(
        [Description("The work item number (the N of #wiN).")] int id,
        [Description("The event, one line, at most 300 characters.")] string text,
        [Description(AgentHelp)] string? agent = null,
        [Description(RepoHelp)] string? repo = null) =>
        Run(repo, _ =>
        {
            string who = AgentName(agent);
            ActAs(who);
            Item(id);
            string line = (text ?? "").Trim();
            if (line.Length == 0) throw new McpException("An event needs `text`: one line saying what happened.");
            if (line.Length > 300 || line.Contains('\n') || line.Contains('\r')) throw new McpException($"`text` is {(line.Contains('\n') || line.Contains('\r') ? "several lines" : $"{line.Length} characters")}: board_log takes one line of at most 300 characters. Use board_note for anything longer.");
            (bool ok, string message) = WorkItemsApi.LogEvent(id, "log", line);
            if (!ok) throw new McpException($"Could not log the event: {message}");
            WorkItemEvent e = WorkItemsApi.GetEvents(id)[^1];
            return new LogResult(id, new LogEventRow(Stamp(e.At), e.By, e.Kind, e.Text), RevisionOf(Item(id)));
        });

    [McpServerTool(Name = "board_validate", ReadOnly = true, UseStructuredContent = true)]
    [Description("Run the pinned OpenSpec validator (validate --strict) on one work item, or on every open item when `id` is left out (slow: about a second per item), plus the app's own warnings: duplicate ids, items that are Completed but not archived, linked test names that are not in the latest results. Run it before you stop.")]
    public Task<ValidateResult> BoardValidate(
        [Description("The work item number; leave out to validate every open item.")] int? id = null,
        [Description(FormatHelp + " Concise gives the validator's output only for items it rejects.")] string? format = null,
        [Description(RepoHelp)] string? repo = null) =>
        Run(repo, _ =>
        {
            bool detailed = IsDetailed(format);
            IReadOnlyList<WorkItem> items = id is int one ? [Item(one)] : [.. WorkItemsApi.GetAll().Where(i => !i.Archived).OrderBy(i => i.Id)];
            List<ValidationRow> rows = [];
            List<string> warnings = [.. WorkItemsApi.GetWarnings()];
            TestRun run = LinkedTestRunsApi.Latest();
            foreach (WorkItem item in items)
            {
                (bool valid, string output) = WorkItemsApi.Validate(item.Id);
                rows.Add(new ValidationRow(item.Id, item.Folder, valid, detailed || !valid ? output : null));
                warnings.AddRange(TestWarnings((item.Tests ?? []).Select(t => (t.Suite, t.Test)), run).Select(w => $"#wi{item.Id}: {w}"));
            }
            return new ValidateResult(rows.All(r => r.Valid), rows, warnings);
        });

    // ── Plumbing ──

    private static async Task<T> Run<T>(string? repo, Func<string, T> body)
    {
        string root = ResolveRoot(repo);
        await Gate.WaitAsync();
        try
        {
            string? previous = OpenSpecStoreApi.RepoRoot;
            (bool ok, string message) = OpenSpecStoreApi.Open(root, keepOthers: true);
            if (!ok) throw new McpException($"Could not open {root}: {message}");
            GitHistoryApi.Configure(root);
            OpenSpecStoreApi.Refresh();   // ponytail: every call re-reads the repository (the watcher lags 300 ms and can drop events in a server that runs for hours); cache per call if it ever shows
            try { return body(root); }
            finally
            {
                WorkItemsApi.ConfigureActor(null);   // the agent's name was only for this call (the tools never run inside the desktop app, whose actor is the user)
                // the host (a desktop app) may show another repository: give it back
                if (previous is not null && !string.Equals(previous, root, StringComparison.OrdinalIgnoreCase) && OpenSpecStoreApi.Use(previous)) GitHistoryApi.Configure(previous);
            }
        }
        finally { Gate.Release(); }
    }

    /// <summary>The repository root for a path inside it: the nearest folder, walking up, that holds openspec/.</summary>
    internal static string ResolveRoot(string? repo)
    {
        string? env = Environment.GetEnvironmentVariable("RTM_REPO");
        (string start, string source) = !string.IsNullOrWhiteSpace(repo) ? (repo.Trim(), "the `repo` argument")
            : BoardMcp.Caller.Value.Repo is { Length: > 0 } caller ? (caller, "the client's working directory")
            : !BoardMcp.DefaultToWorkingDirectory ? throw new McpException("Pass `repo` (any path inside the repository): this server serves every repository, so it has no default one.")
            : !string.IsNullOrWhiteSpace(env) ? (env.Trim(), "the RTM_REPO environment variable")
            : (Directory.GetCurrentDirectory(), "the server's working directory");
        string full = Path.GetFullPath(start);
        if (File.Exists(full)) full = Path.GetDirectoryName(full)!;
        for (string? dir = full; dir is not null; dir = Path.GetDirectoryName(dir))
            if (Directory.Exists(Path.Combine(dir, "openspec"))) return dir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        throw new McpException($"No repository found: neither {full} (from {source}) nor any folder above it has an openspec/ folder. Pass `repo` as a path inside a repository that uses OpenSpec.");
    }

    /// <summary>The events the commands of this call write carry the agent's name. The actor is one static, which is safe: every tool runs under <see cref="Gate"/>, one at a time, and <see cref="Run{T}"/> hands it back.</summary>
    private static void ActAs(string who) => WorkItemsApi.ConfigureActor(() => who);

    /// <summary>Checks what a note needs before anything is written and returns its kind: null or "decision".</summary>
    private static string? NoteKind(string? body, string? kind, string[]? attachments)
    {
        if (string.IsNullOrWhiteSpace(body)) throw new McpException("A note needs a body: say what happened, in plain words.");
        kind = string.IsNullOrWhiteSpace(kind) ? null : kind.Trim().ToLowerInvariant();
        if (kind is not null && kind != NoteKinds.Decision) throw new McpException($"Unknown note kind \"{kind}\": the only kind is \"{NoteKinds.Decision}\"; leave `kind` out for an ordinary entry.");
        foreach (string url in attachments ?? [])
            if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out Uri? uri) || uri.Scheme is not ("http" or "https")) throw new McpException($"Attachment \"{url}\" is not a full link: use http:// or https:// URLs. Files go in the note body, not as attachments.");
        return kind;
    }

    /// <summary>Writes a note on the item as <paramref name="who"/> (<paramref name="kind"/> from <see cref="NoteKind"/>), signs the note and the item.</summary>
    private static NoteResult WriteNote(int id, string who, string body, string? title, string[]? links, string[]? commits, string[]? attachments, string? kind)
    {
        bool titled = !string.IsNullOrWhiteSpace(title);
        title = titled ? title!.Trim() : kind == NoteKinds.Decision ? "Decision" : TitleFrom(body);
        (bool ok, string message, int noteId) = NotesApi.Create(id, who, DateTime.Now, kind);
        if (!ok) throw new McpException($"Could not create the note: {message}");
        void Step(string name, (bool Success, string Message) r) { if (!r.Success) throw new McpException($"Note #n{noteId} was created but {name} failed: {r.Message}"); }
        string text = body.Replace("\r\n", "\n").Trim();
        // the first line became the title: the text under the heading is the rest, so the entry does not say it twice
        if (!titled && !text.StartsWith('#') && text.Split('\n')[0].Trim() == title) text = text[text.Split('\n')[0].Length..].TrimStart();
        Step("setting the title", NotesApi.SetTitle(noteId, title));
        bool heading = text.StartsWith("# ", StringComparison.Ordinal) || (!titled && text.StartsWith('#'));   // a body that opens with its own heading is not given a second one
        Step("writing the body", NotesApi.SaveBody(noteId, heading ? text + "\n" : $"# {title}\n\n{text}\n"));
        foreach (string link in links ?? []) Step($"adding the link {link}", NotesApi.AddManualLink(noteId, link));
        List<string> warnings = [];
        if (commits is { Length: > 0 })
        {
            Step("linking the commits", NotesApi.SetLinkedCommits(noteId, commits));
            IReadOnlyList<CommitInfo> known = GitHistoryApi.Commits([.. commits]);
            foreach (string c in commits.Where(c => !known.Any(k => k.Hash.StartsWith(c, StringComparison.OrdinalIgnoreCase) || c.StartsWith(k.Hash, StringComparison.OrdinalIgnoreCase)))) warnings.Add($"Commit {c} is not in this repository's history (yet).");
        }
        foreach (string url in attachments ?? []) Step($"attaching {url}", NotesApi.AttachLink(noteId, url));
        (bool promoted, string promoteMessage, IReadOnlyList<string> subtasks) = NotesApi.PromoteSubtasks(noteId);
        if (!promoted) warnings.Add($"Could not turn the note's checkboxes into subtasks: {promoteMessage}");
        Step("signing the note", NotesApi.StampAgent(noteId, who));
        Step("signing the item", WorkItemsApi.StampAgent(id, who));
        WorkItem item = Item(id);
        return new NoteResult(noteId, id, Path.GetRelativePath(OpenSpecStoreApi.RepoRoot!, NotesApi.Get(noteId)?.Path ?? item.Path).Replace('\\', '/'), warnings, subtasks, RevisionOf(item), kind);
    }

    /// <summary>"cancelled", "duplicate of #wiN" or "superseded by #wiN" (the other item must exist) as the logged text and the note title.</summary>
    private static (string Event, string Title) ParseResolution(string resolution, int id)
    {
        string text = string.Join(' ', resolution.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).ToLowerInvariant();
        if (text is "cancelled" or "canceled") return ("cancelled", "Cancelled");
        Match m = ResolutionPattern().Match(text);
        if (!m.Success) throw new McpException($"Unknown resolution \"{resolution}\": use \"cancelled\", \"duplicate of #wiN\" or \"superseded by #wiN\" (N is the other item's number).");
        int other = int.Parse(m.Groups["n"].Value, CultureInfo.InvariantCulture);
        if (other == id) throw new McpException("An item cannot be a duplicate of, or superseded by, itself.");
        Item(other);
        bool duplicate = m.Groups["kind"].Value == "duplicate of";
        return ($"{m.Groups["kind"].Value} #wi{other}", $"{(duplicate ? "Duplicate of" : "Superseded by")} #wi{other}");
    }

    [GeneratedRegex(@"^(?<kind>duplicate of|superseded by) #?wi(?<n>\d{1,9})$")]
    private static partial Regex ResolutionPattern();

    /// <summary>Test links as given, each with its suite corrected when only one class in the latest results has that display name; warnings for the rest.</summary>
    private static List<TestInput> CheckTests(IEnumerable<TestInput> tests, List<string> warnings)
    {
        TestRun run = LinkedTestRunsApi.Latest();
        List<TestInput> result = [];
        foreach (TestInput t in tests)
        {
            string[] classes = [.. run.Results.Where(r => r.Test == t.Test).Select(r => r.Suite).Distinct().Order(StringComparer.Ordinal)];
            if (classes.Length == 1 && classes[0] != t.Suite)
            {
                warnings.Add($"Test \"{t.Test}\": suite corrected from {t.Suite} to {classes[0]}.");
                result.Add(t with { Suite = classes[0] });
                continue;
            }
            if (classes.Length > 1 && !classes.Contains(t.Suite))
                warnings.Add($"Test {t.Suite} / \"{t.Test}\" is not in the latest results under that class; the display name is in {string.Join(", ", classes)}: use one of them as `suite`.");
            else
                warnings.AddRange(TestWarnings([(t.Suite, t.Test)], run));
            result.Add(t);
        }
        return result;
    }

    /// <summary>The title of an untitled note: its first non-empty line without leading # and spaces, at most 80 characters.</summary>
    private static string TitleFrom(string body)
    {
        string line = body.Replace("\r\n", "\n").Split('\n').Select(l => l.Trim()).FirstOrDefault(l => l.Length > 0) ?? "";
        line = line.TrimStart('#', ' ');
        return line.Length == 0 ? "Note" : line.Length > 80 ? line[..80].TrimEnd() : line;
    }

    /// <summary>The item's notes and events as one feed, newest first (a note wins a tie); concise keeps the newest 20.</summary>
    private static (IReadOnlyList<WorklogRow> Rows, int Total) Worklog(int id, bool detailed, bool include = true, DateTime? since = null, int? limit = null)
    {
        List<(DateTime At, WorklogRow Row)> feed = [.. WorkItemsApi.GetEvents(id).Select(e => (e.At, new WorklogRow(Stamp(e.At), e.By, e.Kind, e.Text)))];
        feed.AddRange(NotesApi.GetForWorkItem(id).OrderBy(n => n.Id).Select(n => (n.Created, new WorklogRow(Stamp(n.Created), n.By, "note", n.Title, n.Id, detailed ? n.Body : null))));
        List<WorklogRow> rows = [.. feed.Select((f, i) => (f.At, f.Row, Order: i)).OrderByDescending(f => f.At).ThenByDescending(f => f.Order).Where(f => since is null || f.At >= since).Select(f => f.Row)];
        int total = feed.Count;
        if (!include) return ([], total);
        int? cap = limit ?? (detailed ? null : 20);
        return (cap is int n ? rows.Take(n).ToList() : rows, total);
    }

    private const int ConciseDescription = 600;
    private static readonly string[] SectionNames = ["header", "description", "subtasks", "notes", "worklog", "specs", "tests", "links"];

    /// <summary>The sections board_get was asked for (null = all). Header is always there; an unknown name is refused with the valid ones.</summary>
    private static HashSet<string>? Sections(string[]? sections)
    {
        if (sections is not { Length: > 0 }) return null;
        HashSet<string> wanted = new(StringComparer.OrdinalIgnoreCase) { "header" };
        foreach (string s in sections)
        {
            string name = (s ?? "").Trim();
            if (!SectionNames.Contains(name, StringComparer.OrdinalIgnoreCase)) throw new McpException($"Unknown section \"{s}\": use {string.Join(", ", SectionNames)}.");
            wanted.Add(name);
        }
        return wanted;
    }

    private static DateTime? Since(string? since)
    {
        if (string.IsNullOrWhiteSpace(since)) return null;
        return DateTime.TryParse(since.Trim(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out DateTime at)
            ? at
            : throw new McpException($"`since` must be an ISO date or date-time like 2026-11-09 or 2026-11-09T14:30, not \"{since}\".");
    }

    /// <summary>The requirements of a main spec: each name with its text, from its "### Requirement:" line up to the next requirement or "## " heading.</summary>
    private static List<(string Name, string Text)> Requirements(string spec)
    {
        List<(string Name, string Text)> found = [];
        string? name = null;
        List<string> lines = [];
        void Close() { if (name is not null) found.Add((name, string.Join("\n", lines).Trim())); name = null; lines = []; }
        foreach (string line in spec.Replace("\r\n", "\n").Split('\n'))
        {
            if (line.StartsWith("### Requirement:", StringComparison.Ordinal)) { Close(); name = line["### Requirement:".Length..].Trim(); }
            else if (line.StartsWith("## ", StringComparison.Ordinal)) { Close(); continue; }
            if (name is not null) lines.Add(line);
        }
        Close();
        return found;
    }

    private static string Stamp(DateTime at) => at.ToString("s", CultureInfo.InvariantCulture);

    private static string AgentName(string? agent)
    {
        string name = (!string.IsNullOrWhiteSpace(agent) ? agent : BoardMcp.Caller.Value.Agent is { Length: > 0 } caller ? caller : Environment.GetEnvironmentVariable("RTM_AGENT") is { Length: > 0 } env ? env : "Claude").Trim();
        if (!AgentPattern().IsMatch(name)) throw new McpException($"Agent name \"{name}\" is not allowed: use letters, digits, spaces, dots, dashes or underscores, at most 40 characters, like Claude or Codex.");
        return name;
    }

    [GeneratedRegex(@"^[\p{L}\p{N} ._-]{1,40}$")]
    private static partial Regex AgentPattern();

    [GeneratedRegex(@"^[a-z0-9]+(-[a-z0-9]+)*$")]
    private static partial Regex FolderPattern();

    private static bool IsDetailed(string? format) => format?.Trim().ToLowerInvariant() switch
    {
        null or "" or "concise" => false,
        "detailed" => true,
        _ => throw new McpException($"Unknown format \"{format}\": use concise or detailed."),
    };

    private static WorkItem Item(int id) =>
        WorkItemsApi.Get(id) ?? throw new McpException($"There is no work item #wi{id}. Use board_list to find the id.");

    private static void CheckLabel(string? label)
    {
        if (!string.IsNullOrEmpty(label) && !WorkItemsApi.Labels.Contains(label)) throw new McpException($"Unknown label \"{label}\": use {string.Join(", ", WorkItemsApi.Labels)}.");
    }

    private static void CheckPriority(string? priority)
    {
        if (priority is not null && !Priorities.Contains(priority)) throw new McpException($"Unknown priority \"{priority}\": use high or low.");
    }

    private static void CheckCapability(string capability)
    {
        string c = (capability ?? "").Replace('\\', '/').Trim('/');
        if (c.Length == 0 || Path.IsPathRooted(c) || c.Split('/').Contains("..")) throw new McpException($"\"{capability}\" is not a capability: use a path under openspec/specs, like billing or functional/banking (see board_features).");
    }

    private static DateOnly? Day(string? text, string name)
    {
        if (string.IsNullOrEmpty(text)) return null;
        return DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateOnly day) ? day : throw new McpException($"`{name}` must be a day like 2026-11-09 (or an empty string to clear it), not \"{text}\".");
    }

    private static string? FeatureOf(WorkItem item) => item.Features.FirstOrDefault()?.Path;

    private static string RevisionOf(WorkItem item) => item.Revision;

    private static bool IsReady(WorkItem item) =>
        item.Status != "In Progress" && !item.Archived
        && (item.DependsOn ?? []).All(d => WorkItemsApi.Get(d) is not { } dep || WorkItemsApi.IsResolved(dep));

    private static ListRow Row(WorkItem i, bool detailed) => detailed
        ? new ListRow(i.Id, i.Title, i.Status, FeatureOf(i), i.Label, RevisionOf(i), i.Priority, i.Contributors, i.DependsOn, i.Plan, i.Folder, i.AgentBy, i.Archived)
        : new ListRow(i.Id, i.Title, i.Status, FeatureOf(i), i.Label, RevisionOf(i));

    private static CommitRow Commit(CommitInfo c, bool mentions) => new(c.Hash, c.Subject, c.Author, c.Date.ToString("s", CultureInfo.InvariantCulture), mentions);

    private static DependencyRow Dependency(int id) =>
        WorkItemsApi.Get(id) is { } d ? new DependencyRow(d.Id, d.Title, d.Status, WorkItemsApi.IsResolved(d)) : new DependencyRow(id, "(no such item)", "", true);

    private static TestRow TestOutcome(TestRun run, TestLink t)
    {
        string outcome = run.Results.FirstOrDefault(r => r.Suite == t.Suite && r.Test == t.Test)?.Outcome ?? "missing";
        return new TestRow(t.Suite, t.Test, outcome, outcome switch { "passed" => "✓", "failed" => "✗", "skipped" => "-", _ => "?" }, t.Note);
    }

    private static IEnumerable<string> TestWarnings(IEnumerable<(string Suite, string Test)> tests, TestRun? run = null)
    {
        run ??= LinkedTestRunsApi.Latest();
        foreach ((string suite, string test) in tests)
            if (!run.Results.Any(r => r.Suite == suite && r.Test == test))
                yield return run.File is null
                    ? $"Test {suite} / \"{test}\" cannot be checked: no results file was found (see x-rtm.testResults in openspec/config.yaml). Run the tests first."
                    : $"Test {suite} / \"{test}\" is not in the latest results ({run.File}): use the class short name and the display name exactly as the file has them, and run the tests first.";
    }

    private static IReadOnlyList<DeltaSpecRow> DeltaSpecs(WorkItem item, bool detailed)
    {
        string specs = Path.Combine(item.Path, "specs");
        if (!Directory.Exists(specs)) return [];
        return [.. Directory.EnumerateFiles(specs, "spec.md", SearchOption.AllDirectories)
            .Select(f => (Capability: Path.GetRelativePath(specs, Path.GetDirectoryName(f)!).Replace('\\', '/'), File: f))
            .OrderBy(s => s.Capability, StringComparer.Ordinal)
            .Select(s => new DeltaSpecRow(s.Capability, detailed ? OpenSpecStoreApi.ReadText(s.File) : null))];
    }

    private static string Signed(string? text, string agent) =>
        $"{NewLines().Replace((text ?? "").Trim(), " ")} <!-- agent: {agent} -->";

    [GeneratedRegex(@"\s*[\r\n]+\s*")]
    private static partial Regex NewLines();

    /// <summary>A fresh tasks.md: one numbered group per distinct group heading (in order of appearance), each subtask signed.</summary>
    private static string TasksText(IEnumerable<SubtaskInput> subtasks, string agent)
    {
        System.Text.StringBuilder sb = new("# Tasks\n");
        int g = 0;
        foreach (IGrouping<string, SubtaskInput> group in subtasks.GroupBy(s => string.IsNullOrWhiteSpace(s.Group) ? "Tasks" : s.Group.Trim()))
        {
            sb.Append($"\n## {++g}. {group.Key}\n\n");
            int n = 0;
            foreach (SubtaskInput s in group) sb.Append($"- [{(s.Done == true ? "x" : " ")}] {g}.{++n} {Signed(s.Text, agent)}\n");
        }
        return sb.ToString();
    }
}
