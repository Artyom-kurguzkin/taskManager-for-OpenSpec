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
    [Description("Create a work item (one deliverable; its steps are subtasks); returns the id, folder and the validator's output.")]
    public Task<CreateResult> BoardCreate(
        [Description("Short title.")] string title,
        [Description("The problem or opportunity, 50 to 1000 characters.")] string why,
        [Description("What changes, markdown, usually bullets.")] string whatChanges,
        [Description("Feature path (see board_features), one per item.")] string? feature = null,
        [Description("bug, feature or enhancement.")] string? label = null,
        [Description("Backlog (default), Planned, Confirmed, In Progress or another project status.")] string status = "Backlog",
        [Description("high or low (default low).")] string? priority = null,
        [Description("Repo-relative path of your plan file in the plans folder.")] string? plan = null,
        [Description("Ids of the items this one waits for.")] int[]? dependsOn = null,
        [Description("The steps, each with an optional group heading.")] SubtaskInput[]? subtasks = null,
        [Description("Delta specs: capability path to markdown.")] Dictionary<string, string>? specs = null,
        [Description("Change folder name, kebab-case, not taken yet (default: the first words of the title).")] string? folder = null,
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
    [Description("Change a work item: only the fields you pass change; returns what was applied, warnings and the new revision.")]
    public Task<UpdateResult> BoardUpdate(
        [Description("The work item number.")] int id,
        [Description(RevisionHelp)] string? revision = null,
        [Description("New title.")] string? title = null,
        [Description("A status, not Completed.")] string? status = null,
        [Description("high or low.")] string? priority = null,
        [Description("bug, feature or enhancement; empty removes it.")] string? label = null,
        [Description("Move to this feature path; empty removes it.")] string? feature = null,
        [Description("Start day, yyyy-MM-dd; empty clears it.")] string? started = null,
        [Description("Due day, yyyy-MM-dd; empty clears it.")] string? due = null,
        [Description("Plan file path; empty removes it.")] string? plan = null,
        [Description("Replaces the items it waits for.")] int[]? dependsOn = null,
        [Description("Replaces the contributors.")] string[]? contributors = null,
        [Description("The whole proposal body, replacing it.")] string? description = null,
        [Description("Replaces the linked tests.")] TestInput[]? tests = null,
        [Description("Replaces the linked commit hashes.")] string[]? commits = null,
        [Description("Replaces the linked documents (repo-relative paths).")] string[]? docs = null,
        [Description("Tests to add to the linked ones.")] TestInput[]? testsAdd = null,
        [Description("Tests to remove from the linked ones.")] TestInput[]? testsRemove = null,
        [Description("Documents to add.")] string[]? docsAdd = null,
        [Description("Documents to remove.")] string[]? docsRemove = null,
        [Description("Commit hashes to add.")] string[]? commitsAdd = null,
        [Description("true hides the item, false shows it.")] bool? hidden = null,
        [Description("cancelled, duplicate of #wiN or superseded by #wiN.")] string? resolution = null,
        [Description("Why, as the resolution note's body.")] string? resolutionWhy = null,
        [Description("A worklog note written after the other changes: body, title, kind.")] NoteInput? note = null,
        [Description("Subtask edits (see the instructions).")] SubtaskInput[]? subtasks = null,
        [Description("Delta specs by capability: markdown writes, null deletes.")] Dictionary<string, string?>? specs = null,
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
    [Description("Write a note on a work item's worklog for a hand-off, a decision or output worth keeping; answers the note id and path.")]
    public Task<NoteResult> BoardNote(
        [Description("The work item number.")] int id,
        [Description("Markdown; unticked `- [ ]` lines become subtasks.")] string body,
        [Description("Default: the first line of the body.")] string? title = null,
        [Description("References: wi12, n9 or a doc path.")] string[]? links = null,
        [Description("Hashes of commits the note is about.")] string[]? commits = null,
        [Description("http(s) URLs to attach.")] string[]? attachments = null,
        [Description("decision, or leave out.")] string? kind = null,
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

    /// <summary>The event standard: the kinds an agent's progress events take (anything else is refused); the worklog labels them. Without a kind an event is <c>log</c>.</summary>
    internal static readonly string[] EventKinds = ["started", "found", "decision", "tests-red", "tests-green", "verified", "blocked", "done"];

    private const int LogTextMax = 4000;

    [McpServerTool(Name = "board_log", Destructive = false, UseStructuredContent = false)]
    [Description("Log a progress event on a work item; answers one short line.")]
    public Task<string> BoardLog(
        [Description("The work item number.")] int id,
        [Description("The event, any lines; over 4,000 characters it becomes a note.")] string text,
        [Description("started, found, decision, tests-red, tests-green, verified, blocked or done; default log.")] string? kind = null,
        [Description(AgentHelp)] string? agent = null,
        [Description(RepoHelp)] string? repo = null) =>
        Run(repo, _ =>
        {
            string who = LogAgentName(agent);
            ActAs(who);
            Item(id);
            string what = string.IsNullOrWhiteSpace(kind) ? "log" : kind.Trim().ToLowerInvariant();
            if (what != "log" && !EventKinds.Contains(what)) throw new McpException($"Unknown kind \"{kind}\": use {string.Join(" | ", EventKinds)} (or leave it out for a plain log line).");
            string body = (text ?? "").Trim();
            if (body.Length == 0) throw new McpException("An event needs `text`: what happened.");
            if (body.Length > LogTextMax)
            {
                NoteResult note = WriteNote(id, who, new NoteDraft(body, null, Kind: what == "decision" ? "decision" : null));
                return $"#wi{id} n{note.NoteId} note (text over 4,000 characters, written as a note)";
            }
            (bool ok, string message) = WorkItemsApi.LogEvent(id, what, body);
            if (!ok) throw new McpException($"Could not log the event: {message}");
            return $"#wi{id} e{WorkItemsApi.GetEvents(id).Count} {what}";
        });

    /// <summary>The name an event is signed with: as <see cref="AgentName"/> picks it, parentheses allowed, anything the log's line format cannot hold dropped, at most 40 characters.</summary>
    private static string LogAgentName(string? agent)
    {
        string raw = !string.IsNullOrWhiteSpace(agent) ? agent : BoardMcp.Caller.Value.Agent is { Length: > 0 } caller ? caller : Environment.GetEnvironmentVariable("RTM_AGENT") is { Length: > 0 } env ? env : "Claude";
        string name = Regex.Replace(Regex.Replace(raw, @"[^\p{L}\p{N} ._()-]", ""), @" {2,}", " ").Trim();
        return name.Length == 0 ? "Claude" : name.Length > 40 ? name[..40].TrimEnd() : name;
    }
}
