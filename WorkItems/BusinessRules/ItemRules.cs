using System.Globalization;
using System.Text.RegularExpressions;
using OpenSpecStore;
using WorkItems.PublicDTOs;

namespace WorkItems;

/// <summary>The rules every caller of the board shares, the app and an agent's tool call alike: what a status, a label, a name or a folder may be, when an item can start, how tasks.md is written.</summary>
internal static partial class ItemRules
{
    internal static readonly IReadOnlyList<string> Priorities = ["high", "low"];
    internal const string NewItemTitle = "New work item";

    /// <summary>The statuses a new item can start in: the project's own, except the ones that mean blocked, in review or done.</summary>
    internal static IReadOnlyList<string> CreationStatuses() => [.. Queries.GetStatuses().Where(s => s is not ("Blocked" or "In Review" or "Completed"))];

    internal static string? CheckCreationStatus(string status)
    {
        IReadOnlyList<string> allowed = CreationStatuses();
        return allowed.Contains(status) ? null
            : $"Status \"{status}\" cannot be used to create an item: use {string.Join(", ", allowed)}. Done work is set to In Review with board_update; Completed is the user's, from the app.";
    }

    /// <summary>An agent moves an item to a status of the project's list, never to Completed (archiving is the user's).</summary>
    internal static string? CheckAgentStatus(string status)
    {
        if (status == "Completed") return "Completed means archived, and the user archives from the app: set In Review when the work is done.";
        IReadOnlyList<string> all = Queries.GetStatuses();
        return all.Contains(status) ? null : $"Unknown status \"{status}\" in this project: use {string.Join(", ", all.Where(s => s != "Completed"))}.";
    }

    internal static string? CheckLabel(string? label) =>
        string.IsNullOrEmpty(label) || WorkItemsApi.Labels.Contains(label) ? null : $"Unknown label \"{label}\": use {string.Join(", ", WorkItemsApi.Labels)}.";

    internal static string? CheckPriority(string? priority) =>
        priority is null || Priorities.Contains(priority) ? null : $"Unknown priority \"{priority}\": use high or low.";

    /// <summary>Why <paramref name="why"/> is not a proposal's Why: OpenSpec rejects anything but 50 to 1000 characters.</summary>
    internal static string? CheckWhy(string? why) =>
        why is null || why.Trim().Length is < 50 or > 1000
            ? $"`why` must be 50 to 1000 characters (it is {why?.Trim().Length ?? 0}): say why the work is needed in a few sentences. OpenSpec rejects anything else."
            : null;

    internal static string? CheckFolder(string? folder)
    {
        if (string.IsNullOrWhiteSpace(folder)) return null;
        folder = folder.Trim();
        if (folder.Length > 80 || !FolderPattern().IsMatch(folder)) return $"Folder \"{folder}\" is not kebab-case: use lowercase letters, digits and single dashes, like rate-limit-api, at most 80 characters.";
        return Commands.FolderExists(folder) ? $"A change folder named \"{folder}\" exists already (under changes/ or changes/archive/): choose another name, or leave `folder` out." : null;
    }

    [GeneratedRegex(@"^[a-z0-9]+(-[a-z0-9]+)*$")]
    private static partial Regex FolderPattern();

    /// <summary>Why the ids cannot be dependencies of <paramref name="self"/> (null when they can): an item that does not exist, or the item itself.</summary>
    internal static string? CheckDependencies(int? self, IEnumerable<int>? ids)
    {
        foreach (int dep in ids ?? [])
        {
            if (dep == self) return "An item cannot depend on itself.";
            if (Queries.Get(dep) is null) return $"There is no work item #wi{dep}. Use board_list to find the id.";
        }
        return null;
    }

    [GeneratedRegex(@"^[\p{L}\p{N} ._-]{1,40}$")]
    private static partial Regex AgentPattern();

    internal static string? CheckAgentName(string name) =>
        AgentPattern().IsMatch(name) ? null : $"Agent name \"{name}\" is not allowed: use letters, digits, spaces, dots, dashes or underscores, at most 40 characters, like Claude or Codex.";

    /// <summary>The item's feature (it has one at most): the path of the first.</summary>
    internal static string? FeatureOf(WorkItem item) => item.Features.FirstOrDefault()?.Path;

    /// <summary>Can be started now: not In Progress, not archived, and every dependency that exists is resolved.</summary>
    internal static bool IsReady(WorkItem item) =>
        item.Status != "In Progress" && !item.Archived
        && (item.DependsOn ?? []).All(d => Queries.Get(d) is not { } dep || WorkItemsApi.IsResolved(dep));

    /// <summary>Starting work adds the one who starts to the contributors: the list to write (null: leave it), from the list given or the item's own.</summary>
    internal static IReadOnlyList<string>? ContributorsAfter(WorkItem item, IEnumerable<string>? given, string? status, string who) =>
        given is null && status != "In Progress" ? null : [.. given ?? item.Contributors, .. status == "In Progress" ? [who] : Array.Empty<string>()];

    internal static IReadOnlyList<DeltaSpecFile> DeltaSpecs(WorkItem item, bool withText)
    {
        string specs = Path.Combine(item.Path, "specs");
        if (!Directory.Exists(specs)) return [];
        return [.. Directory.EnumerateFiles(specs, "spec.md", SearchOption.AllDirectories)
            .Select(f => (Capability: Path.GetRelativePath(specs, Path.GetDirectoryName(f)!).Replace('\\', '/'), File: f))
            .OrderBy(s => s.Capability, StringComparer.Ordinal)
            .Select(s => new DeltaSpecFile(s.Capability, withText ? OpenSpecStoreApi.ReadText(s.File) : null))];
    }

    // ── linked documents ──

    private static string NormDoc(string d) => d.Replace('\\', '/').Trim();

    /// <summary>The linked documents after an edit: the list given (else the current one) with slashes normalised, blanks dropped and no repeats, then the ones to remove taken out and the ones to add put at the end.</summary>
    internal static List<string> DocsAfter(IEnumerable<string>? current, IEnumerable<string>? replace, IEnumerable<string>? add, IEnumerable<string>? remove)
    {
        List<string> list = [.. (replace ?? current ?? []).Select(NormDoc).Where(d => d.Length > 0).Distinct()];
        list.RemoveAll(d => (remove ?? []).Any(r => NormDoc(r) == d));
        list.AddRange((add ?? []).Select(NormDoc).Where(d => d.Length > 0 && !list.Contains(d)).Distinct());
        return list;
    }

    /// <summary>A warning for each linked document that is neither a file nor a folder of the repository (yet).</summary>
    internal static IReadOnlyList<string> MissingDocs(IEnumerable<string> paths)
    {
        string root = OpenSpecStoreApi.RepoRoot ?? "";
        return [.. paths.Select(NormDoc).Where(d => d.Length > 0).Distinct()
            .Where(d => Path.Combine(root, d.Replace('/', Path.DirectorySeparatorChar)) is var full && !File.Exists(full) && !Directory.Exists(full))
            .Select(d => $"The document {d} does not exist in the repository (yet); write it.")];
    }

    // ── resolutions ──

    [GeneratedRegex(@"^(?<kind>duplicate of|superseded by) (?<ref>\S+)$")]
    private static partial Regex ResolutionPattern();

    /// <summary>"cancelled", "duplicate of #wiN" or "superseded by #wiN" (the other item must exist) as the logged text and the note title.</summary>
    internal static Resolution ParseResolution(string resolution, int id)
    {
        string text = string.Join(' ', resolution.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).ToLowerInvariant();
        if (text is "cancelled" or "canceled") return new(true, "", "cancelled", "Cancelled");
        Match m = ResolutionPattern().Match(text);
        if (!m.Success || OpenSpecStoreApi.ParseItemReference(m.Groups["ref"].Value) is not int other)
            return new(false, $"Unknown resolution \"{resolution}\": use \"cancelled\", \"duplicate of #wiN\" or \"superseded by #wiN\" (N is the other item's number).", "", "");
        if (other == id) return new(false, "An item cannot be a duplicate of, or superseded by, itself.", "", "");
        if (Queries.Get(other) is null) return new(false, $"There is no work item #wi{other}. Use board_list to find the id.", "", "");
        bool duplicate = m.Groups["kind"].Value == "duplicate of";
        string reference = OpenSpecStoreApi.FormatReference("item", other.ToString(CultureInfo.InvariantCulture));
        return new(true, "", $"{m.Groups["kind"].Value} {reference}", $"{(duplicate ? "Duplicate of" : "Superseded by")} {reference}");
    }

    // ── tasks.md as an agent writes it ──

    /// <summary>A subtask line: one line, signed with the agent's name.</summary>
    internal static string Signed(string? text, string agent) =>
        $"{NewLines().Replace((text ?? "").Trim(), " ")} <!-- agent: {agent} -->";

    [GeneratedRegex(@"\s*[\r\n]+\s*")]
    private static partial Regex NewLines();

    /// <summary>"2.10" as (2, 10), so subtask numbers sort numerically, not as text.</summary>
    internal static (int Group, int Index) SubtaskOrder(string number)
    {
        string[] parts = number.Split('.');
        return (int.TryParse(parts[0], out int g) ? g : 0, parts.Length > 1 && int.TryParse(parts[^1], out int n) ? n : 0);
    }

    /// <summary>A fresh tasks.md: one numbered group per distinct group heading (in order of appearance), each subtask signed.</summary>
    internal static string TasksText(IEnumerable<NewSubtask> subtasks, string agent)
    {
        System.Text.StringBuilder sb = new("# Tasks\n");
        int g = 0;
        foreach (IGrouping<string, NewSubtask> group in subtasks.GroupBy(s => string.IsNullOrWhiteSpace(s.Group) ? "Tasks" : s.Group.Trim()))
        {
            sb.Append($"\n## {++g}. {group.Key}\n\n");
            int n = 0;
            foreach (NewSubtask s in group) sb.Append($"- [{(s.Done ? "x" : " ")}] {g}.{++n} {Signed(s.Text, agent)}\n");
        }
        return sb.ToString();
    }

    // ── what the log says about an item's past ──

    /// <summary>
    /// The item's status at the end of each moment in <paramref name="ends"/> (null where it did not exist yet), from its events: the last status move before it, else the status it was created with.
    /// An item archived by the CLI with no move to Completed in its log counts as Completed from the moment it was last modified.
    /// </summary>
    internal static IReadOnlyList<string?> StatusAt(WorkItem item, IReadOnlyList<WorkItemEvent> log, IReadOnlyList<DateTime> ends)
    {
        DateTime created = log.FirstOrDefault(e => e.Kind == "created")?.At ?? item.Created;
        List<(DateTime At, string Old, string New)> moves = [.. log.Where(e => e.Kind == "status").Select(e => (e.At, Parts: e.Text.Split(" -> ", 2)))
            .Where(t => t.Parts.Length == 2).Select(t => (t.At, t.Parts[0], t.Parts[1])).OrderBy(m => m.At)];
        string first = moves.Count > 0 ? moves[0].Old : item.Status;
        List<string?> result = [];
        foreach (DateTime end in ends)
        {
            if (created >= end) { result.Add(null); continue; }
            string status = moves.LastOrDefault(m => m.At < end) is { New: not null } last ? last.New : first;
            if (item.Archived && item.Modified < end && moves.All(m => m.New != "Completed")) status = "Completed";
            result.Add(status);
        }
        return result;
    }

    /// <summary>
    /// The ids of the items that changed from <paramref name="from"/> to <paramref name="to"/>, both days included, by local day: some of the
    /// times <paramref name="logged"/> gives for it, its creation, or its files' last write falls in it. With times, the exact window.
    /// </summary>
    internal static IReadOnlySet<int> ChangedIn(IReadOnlyList<WorkItem> items, Func<WorkItem, IEnumerable<DateTime>> logged, DateOnly from, DateOnly to,
        TimeOnly? fromTime, TimeOnly? toTime, Func<WorkItem, bool>? hasCreated)
    {
        if (fromTime is null && toTime is null)
        {
            bool In(DateTime t) => DateOnly.FromDateTime(t) is var d && d >= from && d <= to;
            return items.Where(i => In(i.Created) || In(i.Modified) || logged(i).Any(In)).Select(i => i.Id).ToHashSet();
        }
        // with times: the exact window, its end inclusive to the end of the minute (the inputs take minutes)
        DateTime start = from.ToDateTime(fromTime ?? TimeOnly.MinValue);
        DateTime end = toTime is TimeOnly tt ? to.ToDateTime(tt).AddMinutes(1) : to.AddDays(1).ToDateTime(TimeOnly.MinValue);   // exclusive
        bool Within(DateTime t) => t >= start && t < end;
        // creation counts through its created event (in the log); with none, only when the whole creation day lies inside the window
        bool CreatedIn(WorkItem i) => !(hasCreated?.Invoke(i) ?? false)
            && DateOnly.FromDateTime(i.Created) is var d && d.ToDateTime(TimeOnly.MinValue) >= start && d.AddDays(1).ToDateTime(TimeOnly.MinValue) <= end;
        return items.Where(i => CreatedIn(i) || Within(i.Modified) || logged(i).Any(Within)).Select(i => i.Id).ToHashSet();
    }

    /// <summary>The filter group a worklog entry of an event kind belongs to: events (created, status, ...), links, subtasks, or edits (everything else, kinds not known yet included).</summary>
    internal static string EventGroup(string kind) => kind switch
    {
        "converted" or "subtask-removed" or "spec-removed" or "spec-moved" or "attachment-removed" => "edits",   // a note turned into a subtask or the other way round, and the subtask taken out: housekeeping
        "created" or "status" or "archived" or "merged-from" or "renumbered" or "sync-conflict" or "sync" => "events",
        "commit-linked" or "commit-unlinked" or "test-linked" or "test-unlinked" or "doc-linked" or "doc-unlinked" or "plan-linked" or "plan-unlinked" or "commit" or "mention" or "test" or "doc" => "links",
        "subtask-done" or "subtask-reopened" or "subtask-added" or "subtask-moved" or "made-from-subtask" or "dependency-added" or "dependency-removed" => "subtasks",
        _ => "edits",
    };
}
