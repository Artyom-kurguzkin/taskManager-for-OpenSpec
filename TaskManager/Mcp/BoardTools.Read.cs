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
    [Description("List work items ordered by id; page with `after`.")]
    public Task<ListResult> BoardList(
        [Description("Only these statuses.")] string[]? status = null,
        [Description("Only this feature path.")] string? feature = null,
        [Description("Only this label.")] string? label = null,
        [Description("Only this contributor.")] string? contributor = null,
        [Description("Only titles containing this.")] string? text = null,
        [Description("Only items that can be started now.")] bool ready = false,
        [Description("Include archived (Completed) items.")] bool includeArchived = false,
        [Description("Id of the last item of the previous page.")] int? after = null,
        [Description("Page size (default 50, at most 200).")] int limit = 50,
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
    [Description("One work item: header, description, subtasks, notes, worklog, delta specs, linked tests with outcomes, commits, docs, dependencies and the `revision`.")]
    public Task<GetResult> BoardGet(
        [Description("The work item number.")] int id,
        [Description(FormatHelp)] string? format = null,
        [Description("Only these parts: header, description, subtasks, notes, worklog, specs, tests, links.")] string[]? sections = null,
        [Description("ISO date or date-time: only the worklog and notes from then on.")] string? since = null,
        [Description("At most this many worklog entries and notes, newest first.")] int? limit = null,
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
    [Description("The features (capabilities) with their open items; with `feature` and detailed, one capability's requirement names or one requirement's text.")]
    public Task<FeaturesResult> BoardFeatures(
        [Description("concise (default) or detailed.")] string? format = null,
        [Description("With detailed: the capability path whose spec to read.")] string? feature = null,
        [Description("With `feature`: one requirement's name, for its full text.")] string? requirement = null,
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
