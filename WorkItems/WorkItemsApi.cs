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
    public static readonly IReadOnlyList<string> Statuses = ["Backlog", "Planned", "Confirmed", "In Progress", "Blocked", "In Review", "Completed"];
    public static readonly IReadOnlyList<string> Labels = ["bug", "feature", "enhancement"];

    /// <summary>Git commit authors stand in as contributors until the list is edited. The token must change on each commit so the cache refreshes.</summary>
    public static void ConfigureContributors(Func<(string Token, Func<string, IReadOnlyList<string>> AuthorsOf)?>? git)
    {
        WorkItemsModule.Git = git;
        WorkItemsModule.Cache.Clear();
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

    // ── Reads ──
    /// <summary>Visible work items; pass includeHidden to also get the ones "deleted" in the app.</summary>
    public static IReadOnlyList<WorkItem> GetAll(bool includeHidden = false) => Queries.GetAll(includeHidden);
    public static WorkItem? Get(int id) => Queries.Get(id);
    public static int NextId() => Queries.NextId();
    public static IReadOnlyList<string> GetWarnings() => Queries.GetWarnings();

    /// <summary>Gives every change that shares its number with an earlier one the next free number; returns what changed.</summary>
    public static IReadOnlyList<(int OldId, int NewId, string Folder)> ResolveDuplicateIds() => Commands.ResolveDuplicateIds();
    public static string GetDescription(int id) => Queries.GetDescription(id);
    /// <summary>Sub-categories per board column, from openspec/config.yaml x-rtm.lanes.</summary>
    public static IReadOnlyDictionary<string, IReadOnlyList<string>> GetLanes() => Queries.GetLanes();

    // ── Header fields ──
    public static (bool Success, string Message) SetStatus(int id, string status) => Commands.SetStatus(id, status);
    public static (bool Success, string Message) SetPriority(int id, string priority) => Commands.Update(id, m => m.Priority = priority);
    public static (bool Success, string Message) SetTitle(int id, string title) => Commands.Update(id, m => m.Title = title.Trim());
    public static (bool Success, string Message) SetContributors(int id, IEnumerable<string> people) => Commands.Update(id, m => m.Contributors = people.Distinct().ToList());
    public static (bool Success, string Message) SetLabel(int id, string? label) => Commands.Update(id, m => m.Label = string.IsNullOrEmpty(label) ? null : label);
    public static (bool Success, string Message) SetStarted(int id, DateOnly? day) => Commands.Update(id, m => m.Started = day?.ToString("yyyy-MM-dd"));
    public static (bool Success, string Message) SetDue(int id, DateOnly? day) => Commands.Update(id, m => m.Due = day?.ToString("yyyy-MM-dd"));
    /// <summary>Links a plan file by repo-relative path (slashes normalised); null or blank removes the link. Paths outside the repository are refused.</summary>
    public static (bool Success, string Message) SetPlan(int id, string? path)
    {
        path = path?.Replace('\\', '/').Trim();
        if (string.IsNullOrEmpty(path)) return Commands.Update(id, m => m.Plan = null);
        if (Path.IsPathRooted(path) || path.StartsWith('/') || path.Split('/').Contains("..")) return (false, "A plan is a path inside the repository, like DevResources/plans/name.md");
        return Commands.Update(id, m => m.Plan = path);
    }
    /// <summary>Puts the item in one feature (null = none); a work item has one feature at most.</summary>
    public static (bool Success, string Message) MoveFeature(int id, string? to) => Commands.MoveFeature(id, to);

    /// <summary>Ids of open (not archived) work items with a delta spec folder for the feature; DeleteFeature deletes those folders too.</summary>
    public static IReadOnlyList<int> FeatureSpecOwners(string path) => Commands.FeatureSpecOwners(path);

    /// <summary>Deletes a feature (see FeaturesApi.DeleteFeature) and its delta spec folders in open changes, and unlinks it from active work items, which become unassigned.</summary>
    public static (bool Success, string Message) DeleteFeature(string path) => Commands.DeleteFeature(path);
    public static (bool Success, string Message, string? NewPath) RenameFeature(string path, string name) => Commands.RenameFeature(path, name);
    public static (bool Success, string Message) SetLinkedCommits(int id, IEnumerable<string> hashes) =>
        Commands.Update(id, m => m.Commits = hashes.Distinct().ToList());

    public static (bool Success, string Message) SetLinkedTests(int id, IEnumerable<TestLink> tests) =>
        Commands.Update(id, m => m.Tests = tests.Distinct().Select(t => new Infrastructure.RtmTest { Suite = t.Suite, Test = t.Test, Note = t.Note }).ToList());

    public static (bool Success, string Message) SetDescription(int id, string body) => Commands.SetDescription(id, body);

    // ── Subtasks ──
    public static (bool Success, string Message) SetSubtaskDone(int id, string number, bool done) => Commands.SetSubtaskDone(id, number, done);
    public static (bool Success, string Message) SetSubtaskText(int id, string number, string text) => Commands.SetSubtaskText(id, number, text);
    public static (bool Success, string Message) AddSubtask(int id, string text) => Commands.AddSubtask(id, text);

    /// <summary>Marks the agent's work on an item as reviewed (x-rtm.reviewedAt = now).</summary>
    public static (bool Success, string Message) MarkReviewed(int id) => Commands.MarkReviewed(id);

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

    // ── Whole items ──
    public static (bool Success, string Message, int Id) Create(string title, string status = "Backlog", IEnumerable<string>? features = null, IEnumerable<string>? contributors = null)
        => Commands.Create(title, status, features, contributors);
    public static (bool Success, string Message, int Id) Merge(IReadOnlyList<int> ids) => Commands.Merge(ids);
    /// <summary>Merges the items into one that keeps the title and description of <paramref name="into"/> (one of <paramref name="ids"/>); the others' descriptions become notes on it.</summary>
    public static (bool Success, string Message, int Id) Merge(IReadOnlyList<int> ids, int into) => Commands.Merge(ids, into);
    /// <summary>"Delete" without data loss: marks x-rtm.hidden so the board skips it; the change folder stays.</summary>
    public static (bool Success, string Message) Hide(int id) => Commands.Update(id, m => m.Hidden = true);
    public static (bool Success, string Message) Unhide(int id) => Commands.Update(id, m => m.Hidden = null);

    // ── Lanes (custom sub-categories in board columns) ──
    public static (bool Success, string Message) AddLane(string column, string lane) => Commands.EditLanes(column, l => { if (!l.Contains(lane)) l.Add(lane); });
    public static (bool Success, string Message) RemoveLane(string column, string lane) => Commands.EditLanes(column, l => l.Remove(lane));
    public static (bool Success, string Message) SetLane(int id, string column, string lane) => Commands.Update(id, m => (m.Lanes ??= [])[column] = lane);
}
