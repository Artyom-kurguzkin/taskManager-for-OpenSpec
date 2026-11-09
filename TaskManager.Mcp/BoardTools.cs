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
/// The board as seven tools over the module facades. Every call resolves its repository, takes the one global lock,
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
    private const string AgentHelp = "Your name, written as the author of this change (default: the RTM_AGENT environment variable, else Claude).";
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
    [Description("One work item in full: header fields, description (proposal body), subtasks with numbers and done flags, notes, delta specs, linked tests with their outcome in the latest results (✓ passed, ✗ failed, ? not in the results), linked commits plus commits mentioning #wiN, dependencies and what it blocks, and `revision` (pass it to board_update or board_note). Detailed adds note bodies and delta spec text.")]
    public Task<GetResult> BoardGet(
        [Description("The work item number (the N of #wiN).")] int id,
        [Description(FormatHelp)] string? format = null,
        [Description(RepoHelp)] string? repo = null) =>
        Run(repo, _ =>
        {
            bool detailed = IsDetailed(format);
            WorkItem item = Item(id);
            TestRun run = LinkedTestRunsApi.Latest();
            List<CommitRow> commits = [];
            foreach (CommitInfo c in GitHistoryApi.Commits([.. item.Commits ?? []])) commits.Add(Commit(c, false));
            foreach (CommitInfo c in GitHistoryApi.Mentioning($"#wi{id}"))
                if (!commits.Any(k => k.Hash.StartsWith(c.Hash, StringComparison.OrdinalIgnoreCase) || c.Hash.StartsWith(k.Hash, StringComparison.OrdinalIgnoreCase))) commits.Add(Commit(c, true));
            return new GetResult(
                item.Id, item.Folder, item.Title, item.Status, item.Priority, item.Label, FeatureOf(item),
                item.Contributors, item.Plan, item.Started?.ToString("yyyy-MM-dd"), item.Due?.ToString("yyyy-MM-dd"), item.Moved?.ToString("yyyy-MM-dd"), item.Created.ToString("yyyy-MM-dd"),
                item.AgentBy, item.AgentAt?.ToString("s", CultureInfo.InvariantCulture), item.AgentUnreviewed, item.Archived, item.Hidden,
                RevisionOf(item), WorkItemsApi.GetDescription(id),
                [.. item.Subtasks.Select(s => new SubtaskRow(s.Number, s.Text, s.Done, s.Group, s.Agent))],
                [.. NotesApi.GetForWorkItem(id).OrderBy(n => n.Id).Select(n => new NoteRow(n.Id, n.Title, n.By, n.Created.ToString("s", CultureInfo.InvariantCulture), detailed ? n.Body : null))],
                DeltaSpecs(item, detailed),
                run.File, [.. (item.Tests ?? []).Select(t => TestOutcome(run, t))],
                commits,
                [.. (item.DependsOn ?? []).Select(Dependency)],
                [.. WorkItemsApi.GetBlocking(id).Select(b => new DependencyRow(b.Id, b.Title, b.Status, WorkItemsApi.IsResolved(b)))]);
        });

    [McpServerTool(Name = "board_features", ReadOnly = true, UseStructuredContent = true)]
    [Description("The features (capabilities under openspec/specs, plus the ones added from the board) with the open work items on each. Use a feature path as `feature` when creating an item; invent a new kebab-case name only for a capability that is really new.")]
    public Task<FeaturesResult> BoardFeatures(
        [Description(FormatHelp + " Detailed adds name, group and whether it was added from the board.")] string? format = null,
        [Description(RepoHelp)] string? repo = null) =>
        Run(repo, _ =>
        {
            bool detailed = IsDetailed(format);
            IReadOnlyList<WorkItem> open = [.. WorkItemsApi.GetAll().Where(i => !i.Archived)];
            return new FeaturesResult([.. FeaturesApi.GetAll().Select(f => new FeatureRow(
                f.Path,
                [.. open.Where(i => string.Equals(FeatureOf(i), f.Path, StringComparison.OrdinalIgnoreCase)).OrderBy(i => i.Id).Select(i => new FeatureItem(i.Id, i.Title, i.Status))],
                detailed ? f.Name : null, detailed ? f.Group : null, detailed ? f.Custom : null))]);
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
        [Description(AgentHelp)] string? agent = null,
        [Description(RepoHelp)] string? repo = null) =>
        Run(repo, root =>
        {
            string who = AgentName(agent);
            if (string.IsNullOrWhiteSpace(title)) throw new McpException("Give the work item a title.");
            if (why is null || why.Trim().Length is < 50 or > 1000) throw new McpException($"`why` must be 50 to 1000 characters (it is {why?.Trim().Length ?? 0}): say why the work is needed in a few sentences. OpenSpec rejects anything else.");
            if (string.IsNullOrWhiteSpace(whatChanges)) throw new McpException("`whatChanges` is empty: list what changes, e.g. \"- Each API key gets 100 requests a minute.\"");
            if (!CreateStatuses.Contains(status)) throw new McpException($"Status \"{status}\" cannot be used to create an item: use {string.Join(", ", CreateStatuses)}. Done work is set to In Review with board_update; Completed is the user's, from the app.");
            CheckLabel(label);
            CheckPriority(priority);
            foreach (int dep in dependsOn ?? []) Item(dep);
            foreach (string cap in (specs ?? []).Keys) CheckCapability(cap);
            if ((subtasks ?? []).FirstOrDefault(s => string.IsNullOrWhiteSpace(s.Text)) is not null) throw new McpException("Every subtask needs `text`.");

            List<string> warnings = [];
            (bool ok, string message, int id) = WorkItemsApi.Create(title.Trim(), status == "In Progress" ? "Planned" : status, string.IsNullOrWhiteSpace(feature) ? null : [feature.Trim()], [who]);
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
    [Description("Change a work item. Every field is optional; only what you pass changes. List fields (dependsOn, contributors, tests, commits) are replaced by what you pass, so read board_get first. Status In Progress adds you to contributors and stamps the start day; use In Review when the work is done (Completed is refused: the user archives from the app). Signs the item and your new subtasks as yours. Runs the OpenSpec validator when subtasks, description or specs changed. Pass `revision` from board_get to avoid overwriting someone else's edit.")]
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
        [Description("true hides the item from the board (the folder stays), false shows it again.")] bool? hidden = null,
        [Description("Subtask edits: with `number`, tick/untick (`done`) or retext (`text`) that subtask; without, add a new signed subtask (appended to the last group).")] SubtaskInput[]? subtasks = null,
        [Description("Delta specs by capability: markdown writes or replaces it, null deletes it.")] Dictionary<string, string?>? specs = null,
        [Description(AgentHelp)] string? agent = null,
        [Description(RepoHelp)] string? repo = null) =>
        Run(repo, _ =>
        {
            string who = AgentName(agent);
            WorkItem item = Item(id);
            if (WorkItemsApi.CheckRevision(id, revision) is (false, string stale)) throw new McpException(stale);
            if (status == "Completed") throw new McpException("Completed means archived, and the user archives from the app: set In Review when the work is done.");
            if (status is not null && !WorkItemsApi.Statuses.Contains(status)) throw new McpException($"Unknown status \"{status}\": use {string.Join(", ", WorkItemsApi.Statuses.Where(s => s != "Completed"))}.");
            CheckPriority(priority);
            CheckLabel(label);
            DateOnly? startedDay = Day(started, "started"), dueDay = Day(due, "due");
            foreach (int dep in dependsOn ?? []) { if (dep == id) throw new McpException("An item cannot depend on itself."); Item(dep); }
            foreach (string cap in (specs ?? []).Keys) CheckCapability(cap);
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
            if (tests is not null)
            {
                Step("tests", WorkItemsApi.SetLinkedTests(id, tests.Select(t => new TestLink(t.Suite, t.Test, t.Note))));
                warnings.AddRange(TestWarnings(tests.Select(t => (t.Suite, t.Test))));
            }
            if (commits is not null) Step("commits", WorkItemsApi.SetLinkedCommits(id, commits));
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

            bool? valid = null;
            string? output = null;
            if (files) (valid, output) = WorkItemsApi.Validate(id);
            if (valid == false) warnings.Add("The OpenSpec validator rejected the item: read `validation` and fix it with board_update.");
            return new UpdateResult(id, applied, warnings, valid, output, RevisionOf(Item(id)));
        });

    [McpServerTool(Name = "board_note", Destructive = false, UseStructuredContent = true)]
    [Description("Add a note (a change log: what changed, in plain words) to a work item. Allocates the note id, writes it with you as author and the agent stamp, and signs the item too. Pass `revision` from board_get to avoid writing against a stale read.")]
    public Task<NoteResult> BoardNote(
        [Description("The work item number (the N of #wiN).")] int id,
        [Description("Note title, like \"9 Nov — what changed\".")] string title,
        [Description("The note text in markdown. Write #wiN / #nN to link items and notes.")] string body,
        [Description("References to list in the note's links: wi12, n9 or a doc path.")] string[]? links = null,
        [Description("Hashes of commits the note is about.")] string[]? commits = null,
        [Description("http(s) URLs to attach.")] string[]? attachments = null,
        [Description(AgentHelp)] string? agent = null,
        [Description(RevisionHelp)] string? revision = null,
        [Description(RepoHelp)] string? repo = null) =>
        Run(repo, _ =>
        {
            string who = AgentName(agent);
            WorkItem item = Item(id);
            if (WorkItemsApi.CheckRevision(id, revision) is (false, string stale)) throw new McpException(stale);
            if (string.IsNullOrWhiteSpace(title)) throw new McpException("A note needs a title.");
            if (string.IsNullOrWhiteSpace(body)) throw new McpException("A note needs a body: say what changed, in plain words.");
            foreach (string url in attachments ?? [])
                if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out Uri? uri) || uri.Scheme is not ("http" or "https")) throw new McpException($"Attachment \"{url}\" is not a full link: use http:// or https:// URLs. Files go in the note body, not as attachments.");

            (bool ok, string message, int noteId) = NotesApi.Create(id, who);
            if (!ok) throw new McpException($"Could not create the note: {message}");
            void Step(string name, (bool Success, string Message) r) { if (!r.Success) throw new McpException($"Note #n{noteId} was created but {name} failed: {r.Message}"); }
            string text = body.Replace("\r\n", "\n").Trim();
            Step("setting the title", NotesApi.SetTitle(noteId, title));
            Step("writing the body", NotesApi.SaveBody(noteId, text.StartsWith("# ", StringComparison.Ordinal) ? text + "\n" : $"# {title.Trim()}\n\n{text}\n"));
            foreach (string link in links ?? []) Step($"adding the link {link}", NotesApi.AddManualLink(noteId, link));
            List<string> warnings = [];
            if (commits is { Length: > 0 })
            {
                Step("linking the commits", NotesApi.SetLinkedCommits(noteId, commits));
                IReadOnlyList<CommitInfo> known = GitHistoryApi.Commits([.. commits]);
                foreach (string c in commits.Where(c => !known.Any(k => k.Hash.StartsWith(c, StringComparison.OrdinalIgnoreCase) || c.StartsWith(k.Hash, StringComparison.OrdinalIgnoreCase)))) warnings.Add($"Commit {c} is not in this repository's history (yet).");
            }
            foreach (string url in attachments ?? []) Step($"attaching {url}", NotesApi.AttachLink(noteId, url));
            Step("signing the note", NotesApi.StampAgent(noteId, who));
            Step("signing the item", WorkItemsApi.StampAgent(id, who));
            return new NoteResult(noteId, id, Path.GetRelativePath(OpenSpecStoreApi.RepoRoot!, NotesApi.Get(noteId)?.Path ?? item.Path).Replace('\\', '/'), warnings);
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
            {   // the host (a desktop app) may show another repository: give it back
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

    private static string AgentName(string? agent)
    {
        string name = (!string.IsNullOrWhiteSpace(agent) ? agent : BoardMcp.Caller.Value.Agent is { Length: > 0 } caller ? caller : Environment.GetEnvironmentVariable("RTM_AGENT") is { Length: > 0 } env ? env : "Claude").Trim();
        if (!AgentPattern().IsMatch(name)) throw new McpException($"Agent name \"{name}\" is not allowed: use letters, digits, spaces, dots, dashes or underscores, at most 40 characters, like Claude or Codex.");
        return name;
    }

    [GeneratedRegex(@"^[\p{L}\p{N} ._-]{1,40}$")]
    private static partial Regex AgentPattern();

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
