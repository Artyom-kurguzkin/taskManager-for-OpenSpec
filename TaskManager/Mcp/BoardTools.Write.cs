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

public sealed partial class BoardTools
{
    // ── Writes ──

    [McpServerTool(Name = "board_create", Destructive = false, Idempotent = false, UseStructuredContent = true)]
    [Description("Create a work item: one result that can be finished and archived on its own (steps toward it are `subtasks`). Allocates the next id, writes a valid change folder (proposal, tasks, delta specs), signs it as yours, adds you to contributors, runs the OpenSpec validator and returns id, folder and the validator's output. Not for archiving: the user does that from the app.")]
    public Task<CreateResult> BoardCreate(
        [Description("Short title.")] string title,
        [Description("The problem or opportunity, 50 to 1000 characters (the proposal's Why).")] string why,
        [Description("What changes, in markdown, usually a bullet list (the proposal's What Changes).")] string whatChanges,
        [Description("The feature (capability) this belongs to; see board_features. One per item.")] string? feature = null,
        [Description("bug, feature or enhancement.")] string? label = null,
        [Description("Backlog (default), Planned, Confirmed or In Progress, or another status of the project's own list. Use In Progress when you start now.")] string status = "Backlog",
        [Description("high or low (default low).")] string? priority = null,
        [Description("Repo-relative path of the plan file you wrote, in the project's plans folder (named in openspec/REPO-TASKS.md), like DevResources/plans/name-2026-11-09.md.")] string? plan = null,
        [Description("Ids of the work items this one waits for.")] int[]? dependsOn = null,
        [Description("The steps, each with an optional group heading; they are signed as yours. Leave out for none.")] SubtaskInput[]? subtasks = null,
        [Description("Delta specs by capability, e.g. {\"billing\": \"## ADDED Requirements\\n\\n### Requirement: ...\"}. Needed when the work changes what the product does; each requirement has SHALL or MUST and a #### Scenario with WHEN and THEN lines. Use the feature's own path as the key.")] Dictionary<string, string>? specs = null,
        [Description("The change folder name under openspec/changes: kebab-case (lowercase letters, digits and dashes); it must not exist yet. Default: the first words of the title (at most 6 words and 48 characters).")] string? folder = null,
        [Description(AgentHelp)] string? agent = null,
        [Description(RepoHelp)] string? repo = null) =>
        Run(repo, _ =>
        {
            string who = AgentName(agent);
            ActAs(who);
            CreatedWorkItem made = WorkItemsApi.CreateFromBrief(new NewWorkItem(
                title, why, whatChanges, feature, label, status, priority, plan, dependsOn,
                subtasks?.Select(s => new NewSubtask(s.Text ?? "", s.Group, s.Done == true)).ToList(),
                specs, folder), who);
            if (!made.Success) throw new McpException(made.Message);
            List<string> warnings = [.. made.Warnings];
            if (!string.IsNullOrWhiteSpace(plan) && NotesApi.PlanFolderWarning(plan) is string planWarning) warnings.Add(planWarning);
            WorkItem item = Item(made.Id);
            return new CreateResult(made.Id, item.Folder, made.Valid, made.Validation, warnings, RevisionOf(item));
        });

    [McpServerTool(Name = "board_update", Destructive = false, Idempotent = true, UseStructuredContent = true)]
    [Description("Change a work item. Every field is optional; only what you pass changes. List fields (dependsOn, contributors, tests, commits, docs) are replaced by what you pass, so read board_get first; to change a list without re-sending it use testsAdd, testsRemove, docsAdd, docsRemove or commitsAdd (not together with the field they change). A test link whose suite is wrong but whose display name is unique in the latest results gets the right suite, and the warnings say so. `note` writes a worklog note in the same call, after the other changes (the hand-off in one call); the result has its `noteId`. `resolution` (cancelled, duplicate of #wiN, superseded by #wiN) hides the item, keeps its status and writes a decision note. Status changes, links and ticked subtasks are logged in the worklog under your name. Status In Progress adds you to contributors and stamps the start day; use In Review when the work is done (Completed is refused: the user archives from the app). Signs the item and your new subtasks as yours. Runs the OpenSpec validator when subtasks, description or specs changed. Pass `revision` from board_get to avoid overwriting someone else's edit.")]
    public Task<UpdateResult> BoardUpdate(
        [Description("The work item number (the N of #wiN).")] int id,
        [Description(RevisionHelp)] string? revision = null,
        [Description("New title.")] string? title = null,
        [Description("Backlog, Planned, Confirmed, In Progress, Blocked or In Review, or another status of the project's own list (an unknown one is refused with the list). Not Completed.")] string? status = null,
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
        [Description("Subtask edits: with `number`, tick/untick (`done`), retext (`text`) or delete (`remove: true`, alone; the rest of its group is renumbered) that subtask; without, add a new signed subtask (appended to the last group). Removals are applied last, highest number first, so the numbers in one call all refer to the numbering before it.")] SubtaskInput[]? subtasks = null,
        [Description("Delta specs by capability: markdown writes or replaces it, null deletes it.")] Dictionary<string, string?>? specs = null,
        [Description(AgentHelp)] string? agent = null,
        [Description(RepoHelp)] string? repo = null) =>
        Run(repo, _ =>
        {
            string who = AgentName(agent);
            ActAs(who);
            WorkItem item = Item(id);
            if (WorkItemsApi.CheckRevision(id, revision) is (false, string stale)) throw new McpException(stale);
            if (status is not null) Refuse(WorkItemsApi.CheckAgentStatus(status));
            Refuse(WorkItemsApi.CheckPriority(priority));
            Refuse(WorkItemsApi.CheckLabel(label));
            DateOnly? startedDay = Day(started, "started"), dueDay = Day(due, "due");
            Refuse(WorkItemsApi.CheckDependencies(id, dependsOn));
            foreach (string cap in (specs ?? []).Keys) Refuse(FeaturesApi.CheckCapability(cap));
            if (tests is not null && (testsAdd is not null || testsRemove is not null)) throw new McpException("Pass either `tests` (replaces the whole list) or `testsAdd` / `testsRemove` (change the current list), not both.");
            if (docs is not null && (docsAdd is not null || docsRemove is not null)) throw new McpException("Pass either `docs` (replaces the whole list) or `docsAdd` / `docsRemove` (change the current list), not both.");
            if (commits is not null && commitsAdd is not null) throw new McpException("Pass either `commits` (replaces the whole list) or `commitsAdd` (adds to the current list), not both.");
            Resolution? ended = resolution is null ? null : WorkItemsApi.ParseResolution(resolution, id);
            if (ended is { Success: false }) throw new McpException(ended.Message);
            if (ended is not null && hidden == false) throw new McpException("`resolution` hides the item: do not pass `hidden: false` with it.");
            NoteDraft? draft = note is null ? null : new NoteDraft(note.Body, note.Title, Kind: note.Kind);
            if (draft is not null) Refuse(NotesApi.CheckDraft(draft));
            foreach (SubtaskInput s in subtasks ?? [])
            {
                if (s.Number is null && string.IsNullOrWhiteSpace(s.Text)) throw new McpException("A new subtask needs `text`; to change an existing one give its `number` (see board_get).");
                if (s.Number is not null && !item.Subtasks.Any(k => k.Number == s.Number)) throw new McpException($"There is no subtask {s.Number} on #wi{id}; its subtasks are {(item.Subtasks.Count == 0 ? "none" : string.Join(", ", item.Subtasks.Select(k => k.Number)))}.");
                if (s.Remove == true && s.Number is null) throw new McpException("`remove` needs the `number` of the subtask to remove (see board_get).");
                if (s.Remove == true && (s.Done is not null || s.Text is not null)) throw new McpException($"Subtask {s.Number}: `remove` cannot go together with `done` or `text`; send them as separate entries.");
                if (s.Number is not null && s.Remove != true && s.Done is null && s.Text is null) throw new McpException($"Subtask {s.Number}: pass `done` and/or `text` to change it.");
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
                if (NotesApi.PlanFolderWarning(plan) is string planWarning) warnings.Add(planWarning);
            }
            if (dependsOn is not null) Step("dependsOn", WorkItemsApi.SetDependencies(id, dependsOn));
            if (WorkItemsApi.ContributorsAfter(Item(id), contributors, status, who) is { } people) Step("contributors", WorkItemsApi.SetContributors(id, people));
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
                Step("docs", WorkItemsApi.SetLinkedDocs(id, WorkItemsApi.DocsAfter(item.Docs, docs, docsAdd, docsRemove)));
                warnings.AddRange(WorkItemsApi.MissingDocs((docs ?? []).Concat(docsAdd ?? [])));
            }
            if (hidden is bool h) Step(h ? "hidden" : "shown", h ? WorkItemsApi.Hide(id) : WorkItemsApi.Unhide(id));
            foreach (SubtaskInput s in subtasks ?? [])
            {
                files = true;
                if (s.Remove == true) continue;   // after the other edits, below
                if (s.Number is string number)
                {
                    if (s.Text is not null) Step($"subtask {number} text", WorkItemsApi.SetSubtaskText(id, number, s.Text, who));
                    if (s.Done is bool done) Step($"subtask {number} {(done ? "done" : "open")}", WorkItemsApi.SetSubtaskDone(id, number, done));
                }
                else
                {
                    Step($"subtask \"{s.Text!.Trim()}\"", WorkItemsApi.AddSubtask(id, s.Text!, who));
                    if (s.Done == true && Item(id).Subtasks is [.., { } added]) Step($"subtask {added.Number} done", WorkItemsApi.SetSubtaskDone(id, added.Number, true));
                }
            }
            // descending, so renumbering the rest of a group never shifts a number still to remove
            foreach ((string number, bool removed, string why) in WorkItemsApi.RemoveSubtasks(id, (subtasks ?? []).Where(s => s.Remove == true).Select(s => s.Number!)))
                Step($"subtask {number} removed", (removed, why));
            foreach ((string cap, string? markdown) in specs ?? [])
            {
                files = true;
                Step(markdown is null ? $"delta spec {cap} removed" : $"delta spec {cap}", WorkItemsApi.SetDeltaSpec(id, cap, markdown));
            }
            Step("agent stamp", WorkItemsApi.StampAgent(id, who));
            if (ended is { } end)
            {
                ResolvedItem resolved = NotesApi.Resolve(id, end, resolutionWhy, who);
                Step($"resolution {end.Event}", (resolved.Hidden, resolved.Message));
                NoteResult why = Written(resolved.Note!);
                applied.Add($"resolution note #n{why.NoteId}");
                warnings.AddRange(why.Warnings);
            }
            int? noteId = null;
            if (draft is not null)
            {
                NoteResult written = WriteNote(id, who, draft);
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
            return WriteNote(id, who, new NoteDraft(body, title, links, commits, attachments, kind));
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
}
