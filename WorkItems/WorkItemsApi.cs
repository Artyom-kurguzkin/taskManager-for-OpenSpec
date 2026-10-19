using WorkItems.PublicDTOs;

namespace WorkItems;

/// <summary>
/// The only public type in this assembly. OpenSpec changes are work items: x-rtm metadata in .openspec.yaml,
/// tasks.md checkboxes as subtasks, archived changes as Completed. Reads come straight from disk; every write
/// goes through OpenSpecStore so outside edits are never overwritten. No DI; delegates to Queries/Commands.
/// </summary>
public static class WorkItemsApi
{
    public static readonly IReadOnlyList<string> Statuses = ["Backlog", "Planned", "Confirmed", "In Progress", "Blocked", "Completed"];

    /// <summary>
    /// How a change gets archived. Default runs <c>openspec archive &lt;folder&gt; --yes</c> in the repo root;
    /// pass null to restore it. Returns the CLI's message on failure.
    /// </summary>
    /// <summary>Adds git commit authors to contributors. The token must change on each commit so the cache refreshes.</summary>
    public static void ConfigureContributors(Func<(string Token, Func<string, IReadOnlyList<string>> AuthorsOf)?>? git)
    {
        WorkItemsModule.Git = git;
        WorkItemsModule.Cache = null;
    }

    public static void ConfigureArchiver(Func<string, (bool Success, string Message)>? archive) => WorkItemsModule.Archive = archive ?? Infrastructure.OpenSpecCli.Archive;

    // ── Reads ──
    /// <summary>Visible work items; pass includeHidden to also get the ones "deleted" in the app.</summary>
    public static IReadOnlyList<WorkItem> GetAll(bool includeHidden = false) => Queries.GetAll(includeHidden);
    public static WorkItem? Get(int id) => Queries.Get(id);
    public static int NextId() => Queries.NextId();
    public static IReadOnlyList<string> GetWarnings() => Queries.GetWarnings();
    public static string GetDescription(int id) => Queries.GetDescription(id);
    /// <summary>Sub-categories per board column, from openspec/config.yaml x-rtm.lanes.</summary>
    public static IReadOnlyDictionary<string, IReadOnlyList<string>> GetLanes() => Queries.GetLanes();

    // ── Header fields ──
    public static (bool Success, string Message) SetStatus(int id, string status) => Commands.SetStatus(id, status);
    public static (bool Success, string Message) SetPriority(int id, string priority) => Commands.Update(id, m => m.Priority = priority);
    public static (bool Success, string Message) SetTitle(int id, string title) => Commands.Update(id, m => m.Title = title.Trim());
    public static (bool Success, string Message) SetContributors(int id, IEnumerable<string> people) => Commands.Update(id, m => m.Contributors = people.Distinct().ToList());
    public static (bool Success, string Message) SetManualFeatures(int id, IEnumerable<string> featurePaths) => Commands.SetManualFeatures(id, featurePaths);

    /// <summary>Deletes a custom feature (see FeaturesApi.DeleteFeature) and unlinks it from active work items, which become unassigned.</summary>
    public static (bool Success, string Message) DeleteFeature(string path) => Commands.DeleteFeature(path);
    public static (bool Success, string Message) SetLinkedCommits(int id, IEnumerable<string> hashes) =>
        Commands.Update(id, m => m.Commits = hashes.Distinct().ToList());

    public static (bool Success, string Message) SetLinkedTests(int id, IEnumerable<TestLink> tests) =>
        Commands.Update(id, m => m.Tests = tests.Distinct().Select(t => new Infrastructure.RtmTest { Suite = t.Suite, Test = t.Test, Note = t.Note }).ToList());

    public static (bool Success, string Message) SetDescription(int id, string body) => Commands.SetDescription(id, body);

    // ── Subtasks ──
    public static (bool Success, string Message) SetSubtaskDone(int id, string number, bool done) => Commands.SetSubtaskDone(id, number, done);
    public static (bool Success, string Message) AddSubtask(int id, string text) => Commands.AddSubtask(id, text);
    public static (bool Success, string Message, int NewId) MoveSubtasksToNew(int id, IReadOnlyCollection<string> numbers) => Commands.MoveSubtasksToNew(id, numbers);

    // ── Whole items ──
    public static (bool Success, string Message, int Id) Create(string title, string status = "Backlog", IEnumerable<string>? features = null, IEnumerable<string>? contributors = null)
        => Commands.Create(title, status, features, contributors);
    public static (bool Success, string Message, int Id) Merge(IReadOnlyList<int> ids) => Commands.Merge(ids);
    /// <summary>"Delete" without data loss: marks x-rtm.hidden so the board skips it; the change folder stays.</summary>
    public static (bool Success, string Message) Hide(int id) => Commands.Update(id, m => m.Hidden = true);
    public static (bool Success, string Message) Unhide(int id) => Commands.Update(id, m => m.Hidden = null);

    // ── Lanes (custom sub-categories in board columns) ──
    public static (bool Success, string Message) AddLane(string column, string lane) => Commands.EditLanes(column, l => { if (!l.Contains(lane)) l.Add(lane); });
    public static (bool Success, string Message) RemoveLane(string column, string lane) => Commands.EditLanes(column, l => l.Remove(lane));
    public static (bool Success, string Message) SetLane(int id, string column, string lane) => Commands.Update(id, m => (m.Lanes ??= [])[column] = lane);
}
