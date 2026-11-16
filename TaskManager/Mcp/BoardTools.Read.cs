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
    // ── Reads ──

    [McpServerTool(Name = "board_list", ReadOnly = true, UseStructuredContent = true)]
    [Description("List work items, filtered, ordered by id. Concise rows: id, title, status, feature, label, revision. Page with `after` (the last id of the previous page); `nextAfter` in the result says there is more.")]
    public Task<ListResult> BoardList(
        [Description("Only these statuses (by default Backlog, Planned, Confirmed, In Progress, Blocked, In Review, Completed; a project can have its own list).")] string[]? status = null,
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
                .Where(i => string.IsNullOrWhiteSpace(feature) || string.Equals(WorkItemsApi.FeatureOf(i), feature.Trim(), StringComparison.OrdinalIgnoreCase))
                .Where(i => string.IsNullOrWhiteSpace(label) || string.Equals(i.Label, label.Trim(), StringComparison.OrdinalIgnoreCase))
                .Where(i => string.IsNullOrWhiteSpace(contributor) || i.Contributors.Contains(contributor.Trim(), StringComparer.OrdinalIgnoreCase))
                .Where(i => string.IsNullOrWhiteSpace(text) || i.Title.Contains(text.Trim(), StringComparison.OrdinalIgnoreCase))
                .Where(i => !ready || WorkItemsApi.IsReady(i))
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
                foreach (CommitInfo c in GitHistoryApi.Mentioning(OpenSpecStoreApi.MentionsItem(id)))
                    if (!commits.Any(k => k.Hash.StartsWith(c.Hash, StringComparison.OrdinalIgnoreCase) || c.Hash.StartsWith(k.Hash, StringComparison.OrdinalIgnoreCase))) commits.Add(Commit(c, true));
            }
            (IReadOnlyList<WorklogRow> worklog, int worklogTotal) = Worklog(id, detailed, Want("worklog"), from, limit);
            string description = WorkItemsApi.GetDescription(id);
            bool cut = !detailed && description.Length > ConciseDescription;
            IEnumerable<Note> notes = Want("notes") ? NotesApi.GetForWorkItem(id).Where(n => from is null || n.Created >= from).OrderBy(n => n.Id) : [];
            if (limit is int newest) notes = notes.OrderByDescending(n => n.Id).Take(newest).OrderBy(n => n.Id);
            return new GetResult(
                item.Id, item.Folder, item.Title, item.Status, item.Priority, item.Label, WorkItemsApi.FeatureOf(item),
                item.Contributors, item.Plan, item.Started?.ToString("yyyy-MM-dd"), item.Due?.ToString("yyyy-MM-dd"), item.Moved?.ToString("yyyy-MM-dd"), item.Created.ToString("yyyy-MM-dd"),
                item.AgentBy, item.AgentAt?.ToString("s", CultureInfo.InvariantCulture), item.AgentUnreviewed, item.Archived, item.Hidden,
                RevisionOf(item), Want("description") ? (cut ? description[..ConciseDescription] : description) : null, description.Length, cut,
                item.Subtasks.Count(s => s.Done), item.Subtasks.Count,
                Want("subtasks") ? [.. item.Subtasks.Select(s => new SubtaskRow(s.Number, s.Text, s.Done, s.Group, s.Agent))] : [],
                [.. notes.Select(n => new NoteRow(n.Id, n.Title, n.By, n.Created.ToString("s", CultureInfo.InvariantCulture), detailed ? n.Body : null, n.Kind))],
                Want("specs") ? [.. WorkItemsApi.GetDeltaSpecs(item, detailed || wanted is not null && wanted.Contains("specs")).Select(d => new DeltaSpecRow(d.Capability, d.Markdown))] : [],
                Want("tests") ? run.File : null, Want("tests") ? [.. (item.Tests ?? []).Select(TestOutcome)] : [],
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
                SpecRead spec = FeaturesApi.ReadSpec(feature, requirement);
                if (!spec.Success) throw new McpException(spec.Message);
                names = spec.Requirements;
                text = spec.RequirementText;
                all = [spec.Feature!];
            }
            return new FeaturesResult([.. all.Select(f => new FeatureRow(
                f.Path,
                [.. open.Where(i => string.Equals(WorkItemsApi.FeatureOf(i), f.Path, StringComparison.OrdinalIgnoreCase)).OrderBy(i => i.Id).Select(i => new FeatureItem(i.Id, i.Title, i.Status))],
                detailed ? f.Name : null, detailed ? f.Group : null, detailed ? f.Custom : null))], names, text);
        });
}
