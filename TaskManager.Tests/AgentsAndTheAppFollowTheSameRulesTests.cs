using Features;
using Features.PublicDTOs;
using LinkedTestRuns;
using Notes;
using Notes.PublicDTOs;
using OpenSpecStore;
using TaskManager.Mcp;
using WorkItems;
using WorkItems.PublicDTOs;

namespace TaskManager.Tests;

/// <summary>
/// A rule that an agent's tool call needs, with nobody at the window, lives in the module that owns it; the app and the agent call the same function.
/// These tests call the module Apis the way both callers do, and compare what each leaves behind.
/// </summary>
public sealed class AgentsAndTheAppFollowTheSameRulesTests : IDisposable
{
    private readonly TestRepo repo = new();

    public AgentsAndTheAppFollowTheSameRulesTests()
    {
        WorkItemsApi.ConfigureValidator(folder => (true, $"valid: {folder}"));
        WorkItemsApi.ConfigureContributors(null);
        OpenSpecStoreApi.Open(repo.Root);
    }

    public void Dispose()
    {
        WorkItemsApi.ConfigureValidator(null);
        repo.Dispose();
    }

    private const string Why = "Agents need the same board the user sees, with the same stamps, so that nothing they do goes unreviewed.";

    private static NewWorkItem Brief(string status, string title = "Rate-limit the API") =>
        new(title, Why, "- Each key gets 100 requests a minute.", null, null, status, null, null, null, null, null, null);

    private static IEnumerable<string> Events(int id, string kind) => WorkItemsApi.GetEvents(id).Where(e => e.Kind == kind).Select(e => e.Text);

    [Fact(DisplayName = "An item created by an agent and one created in the app start with the same status rules")]
    public void SameStatusRules()
    {
        CreatedWorkItem agent = WorkItemsApi.CreateFromBrief(Brief("In Progress"), "Codex");
        (bool ok, string message, int appId) = WorkItemsApi.CreateNew("In Progress", null, ["Me"]);

        Assert.True(agent.Success, agent.Message);
        Assert.True(ok, message);
        foreach (int id in new[] { agent.Id, appId })
        {
            WorkItem item = WorkItemsApi.Get(id)!;
            Assert.Equal("In Progress", item.Status);
            Assert.Equal(DateOnly.FromDateTime(DateTime.Today), item.Started);   // starting work stamps the day, whoever started it
            Assert.Equal(["Planned -> In Progress"], Events(id, "status"));
        }
        Assert.DoesNotContain(WorkItemsApi.CreationStatuses(), s => s is "Blocked" or "In Review" or "Completed");

        CreatedWorkItem refused = WorkItemsApi.CreateFromBrief(Brief("Blocked", "Another one"), "Codex");

        Assert.False(refused.Success);
        Assert.Equal(0, refused.Id);
        Assert.Contains("cannot be used to create an item", refused.Message);
        Assert.Contains("Completed is the user's", refused.Message);
    }

    [Fact(DisplayName = "An item created by an agent from a brief is checked by the module: why, folder name, label, priority and dependencies")]
    public void BriefIsChecked()
    {
        Assert.Contains("50 to 1000 characters", WorkItemsApi.CreateFromBrief(Brief("Backlog") with { Why = "too short" }, "Codex").Message);
        Assert.Contains("not kebab-case", WorkItemsApi.CreateFromBrief(Brief("Backlog") with { Folder = "Not Kebab" }, "Codex").Message);
        Assert.Contains("exists already", WorkItemsApi.CreateFromBrief(Brief("Backlog") with { Folder = "add-health-check" }, "Codex").Message);
        Assert.Contains("Unknown label", WorkItemsApi.CreateFromBrief(Brief("Backlog") with { Label = "chore" }, "Codex").Message);
        Assert.Contains("Unknown priority", WorkItemsApi.CreateFromBrief(Brief("Backlog") with { Priority = "urgent" }, "Codex").Message);
        Assert.Contains("There is no work item #wi999", WorkItemsApi.CreateFromBrief(Brief("Backlog") with { DependsOn = [999] }, "Codex").Message);
        Assert.Contains("is not a capability", WorkItemsApi.CreateFromBrief(Brief("Backlog") with { Specs = new Dictionary<string, string> { ["../x"] = "text" } }, "Codex").Message);
        Assert.Equal(["Backlog", "Planned", "Confirmed", "In Progress"], WorkItemsApi.CreationStatuses());
        Assert.Equal(["high", "low"], WorkItemsApi.Priorities);
    }

    [Fact(DisplayName = "The subtasks of an item an agent creates are written signed, in groups, and logged like the ones added in the app")]
    public void SubtasksAreSignedAndLogged()
    {
        NewSubtask[] steps = [new("Count requests per key", "Limiter", false), new("Refuse the 101st", "Limiter", true), new("Describe the limit", "Docs", false)];

        CreatedWorkItem made = WorkItemsApi.CreateFromBrief(Brief("Backlog") with { Subtasks = steps }, "Codex");

        Assert.True(made.Success, made.Message);
        string tasks = File.ReadAllText(Path.Combine(WorkItemsApi.Get(made.Id)!.Path, "tasks.md"));
        Assert.Contains("## 1. Limiter", tasks);
        Assert.Contains("## 2. Docs", tasks);
        Assert.Contains("- [x] 1.2 Refuse the 101st <!-- agent: Codex -->", tasks);
        Assert.Equal(["1.1 Count requests per key", "1.2 Refuse the 101st", "2.1 Describe the limit"], Events(made.Id, "subtask-added"));
        Assert.Equal(["2.1", "1.2"], WorkItemsApi.RemoveSubtasks(made.Id, ["1.2", "2.1"]).Select(r => r.Number));   // highest first
    }

    [Fact(DisplayName = "The worklog an agent reads and the one on the item page list the same entries in the same order")]
    public async Task SameWorklog()
    {
        WorkItemsApi.LogEvent(123, "log", "ran the suite: 525 passed");
        NotesApi.Write(123, "Codex", new NoteDraft("Chunk A finished\n\nThe Passed line was 12."));
        WorkItemsApi.LogEvent(123, "log", "integrated");
        NotesApi.Write(123, "Codex", new NoteDraft("Use the sync lock", Kind: "decision"));

        IReadOnlyList<WorklogEntry> page = NotesApi.GetWorklog(123);   // what the item page draws its feed from
        GetResult read = await new BoardTools().BoardGet(123, "detailed", repo: repo.Root);

        Assert.Equal(page.Select(e => (e.Kind, e.Text, e.By)), read.Worklog.Select(r => (r.Kind, r.Text, r.By)));
        Assert.Equal(page.Count, read.WorklogTotal);
        for (int i = 1; i < page.Count; i++)   // newest first; at one moment the later entry first (a note after the events written with it)
            Assert.True(page[i - 1].At > page[i].At || (page[i - 1].At == page[i].At && page[i - 1].Order > page[i].Order), $"entry {i}");
        Assert.Equal(3, page.Count(e => e.Note is not null));   // the fixture's note and the two written here
        Assert.Equal(page.Where(e => e.Note is not null).Select(e => e.Note!.Id), read.Worklog.Where(r => r.NoteId is not null).Select(r => r.NoteId!.Value));
    }

    [Fact(DisplayName = "A duplicate id is repaired the same way whether the app or an agent finds it first")]
    public async Task SameDuplicateRepair()
    {
        EditingWorkItemsTests.Duplicate(repo);
        // an agent's call finds it first: the call repairs before it reads
        await new BoardTools().BoardList(repo: repo.Root);
        string byAgent = RenumberedNoteOf(126);
        IReadOnlyList<string> agentFolders = [.. WorkItemsApi.GetAll(includeHidden: true).Select(w => $"{w.Id} {w.Folder}").Order()];
        OpenSpecStoreApi.Close();

        // the app finds it first: its change handler runs the same function
        using TestRepo other = new();
        OpenSpecStoreApi.Open(other.Root);
        EditingWorkItemsTests.Duplicate(other);
        DuplicateRepairs app = NotesApi.RepairDuplicates();

        Assert.Equal([new RenumberedItem(123, 126, "second-check")], app.Items);
        Assert.Equal(byAgent, RenumberedNoteOf(126));
        Assert.Equal(agentFolders, WorkItemsApi.GetAll(includeHidden: true).Select(w => $"{w.Id} {w.Folder}").Order());
        Assert.Single(Events(126, "renumbered"));
        Assert.Empty(NotesApi.RepairDuplicates().Items);   // nothing left to repair, nothing written
    }

    private static string RenumberedNoteOf(int id) =>
        NotesApi.GetForWorkItem(id).Single(n => n.Title == "Renumbered from #wi123") is { } n ? $"{n.Title}|{n.By}|{n.Body.Trim()}" : "";

    [Fact(DisplayName = "A note an agent writes without a title is titled by its first line, and a decision is titled Decision")]
    public void NoteComposition()
    {
        WrittenNote plain = NotesApi.Write(123, "Codex", new NoteDraft("# Ran the migration\n\nIt took 3 s."));
        WrittenNote untitled = NotesApi.Write(123, "Codex", new NoteDraft("Chunk A finished\nThe Passed line was 12."));
        WrittenNote decision = NotesApi.Write(123, "Codex", new NoteDraft("Use the lock.\nBecause two writers raced.", Kind: " Decision "));

        Assert.Equal("Ran the migration", NotesApi.Get(plain.NoteId)!.Title);
        Assert.Equal("Chunk A finished", NotesApi.Get(untitled.NoteId)!.Title);
        Assert.Equal("# Chunk A finished\n\nThe Passed line was 12.", NotesApi.Get(untitled.NoteId)!.Body.Trim());   // the title is not said twice
        Assert.Equal("Decision", NotesApi.Get(decision.NoteId)!.Title);
        Assert.True(NotesApi.Get(decision.NoteId)!.IsDecision);
        Assert.Equal("Codex", NotesApi.Get(decision.NoteId)!.AgentBy);
        Assert.Equal("Codex", WorkItemsApi.Get(123)!.AgentBy);   // the item is signed too

        Assert.Contains("Unknown note kind", NotesApi.CheckDraft(new NoteDraft("x", Kind: "idea")));
        Assert.Contains("needs a body", NotesApi.CheckDraft(new NoteDraft("  ")));
        Assert.Contains("not a full link", NotesApi.CheckDraft(new NoteDraft("x", Attachments: ["ftp://x"])));
        Assert.False(NotesApi.Write(123, "Codex", new NoteDraft("x", Kind: "idea")).Success);
    }

    [Fact(DisplayName = "An item an agent ends without finishing it is hidden, logged and given a decision note, by the module")]
    public void Resolution()
    {
        Resolution parsed = WorkItemsApi.ParseResolution("Duplicate of #wi107", 123);
        ResolvedItem ended = NotesApi.Resolve(123, parsed, null, "Codex");

        Assert.Equal("duplicate of #wi107", parsed.Event);
        Assert.True(ended.Hidden);
        Assert.True(ended.Note!.Success, ended.Note.Message);
        Assert.Contains("duplicate of #wi107", Events(123, "resolution"));
        Assert.True(NotesApi.Get(ended.Note.NoteId)!.IsDecision);
        Assert.Equal("Duplicate of #wi107", NotesApi.Get(ended.Note.NoteId)!.Title);
        Assert.False(WorkItemsApi.ParseResolution("duplicate of #wi123", 123).Success);
        Assert.False(WorkItemsApi.ParseResolution("duplicate of #wi999", 123).Success);
        Assert.False(WorkItemsApi.ParseResolution("whatever", 123).Success);
    }

    [Fact(DisplayName = "The charts and an agent read an item's past from the same events")]
    public void StatusFromEvents()
    {
        WorkItem item = WorkItemsApi.Get(123)! with { Status = "In Review", Archived = false, Created = new DateTime(2026, 11, 2, 9, 0, 0) };
        WorkItemEvent[] log =
        [
            new(new DateTime(2026, 11, 2, 9, 0, 0), "me", "created", "Health"),
            new(new DateTime(2026, 11, 4, 9, 0, 0), "me", "status", "Backlog -> In Progress"),
            new(new DateTime(2026, 11, 6, 9, 0, 0), "me", "status", "In Progress -> In Review"),
        ];
        DateTime[] ends = [new(2026, 11, 2), new(2026, 11, 5), new(2026, 11, 9)];

        Assert.Equal(new List<string?> { null, "Backlog", "In Review" }, WorkItemsApi.StatusAt(item, log, [ends[0], new DateTime(2026, 11, 3), ends[2]]).ToList());
        Assert.Equal(new List<string?> { "In Progress" }, WorkItemsApi.StatusAt(item, log, [ends[1]]).ToList());

        WorkItem archived = item with { Archived = true, Status = "Completed", Modified = new DateTime(2026, 11, 7) };   // archived by the CLI: no move to Completed in its log
        Assert.Equal(new List<string?> { "In Review", "Completed" }, WorkItemsApi.StatusAt(archived, log, [new DateTime(2026, 11, 7), new DateTime(2026, 11, 9)]).ToList());
    }

    [Fact(DisplayName = "The changed period counts the events and notes of an item's worklog, in the app and for an agent alike")]
    public void ChangedPeriodFromWorklog()
    {
        WorkItem quiet = WorkItemsApi.Get(107)! with { Created = new DateTime(2026, 1, 1), Modified = new DateTime(2026, 1, 1) };
        WorkItem busy = WorkItemsApi.Get(123)! with { Created = new DateTime(2026, 1, 1), Modified = new DateTime(2026, 1, 1) };
        WorkItemsApi.LogEvent(123, "log", "today");

        IReadOnlySet<int> changed = NotesApi.ChangedIn([quiet, busy], DateOnly.FromDateTime(DateTime.Today), DateOnly.FromDateTime(DateTime.Today));

        Assert.Equal([123], changed);
    }

    [Fact(DisplayName = "A repository is found from any path inside it, a file or a folder, the same for the app and an agent")]
    public void RepositoryFromAPath()
    {
        Assert.Equal(repo.Root, OpenSpecStoreApi.FindRepoRoot(repo.PathOf("openspec/changes/add-health-check")));
        Assert.Equal(repo.Root, OpenSpecStoreApi.FindRepoRoot(repo.PathOf("openspec/config.yaml")));
        Assert.Null(OpenSpecStoreApi.FindRepoRoot(Path.GetTempPath() + "rtm-nowhere-" + Guid.NewGuid().ToString("N")));
    }

    [Fact(DisplayName = "An agent reads a capability's spec through the module that lists the features, with the same capability check")]
    public void ReadingASpec()
    {
        SpecRead banking = FeaturesApi.ReadSpec("functional\\banking", "requirement: banking EXISTS");
        SpecRead whole = FeaturesApi.ReadSpec("functional/banking");

        Assert.True(banking.Success, banking.Message);
        Assert.Equal(["Banking exists"], banking.Requirements);
        Assert.Contains("The system SHALL provide Banking.", banking.RequirementText);
        Assert.Null(whole.RequirementText);
        Assert.Contains("There is no feature", FeaturesApi.ReadSpec("nowhere").Message);
        Assert.Contains("has no requirement", FeaturesApi.ReadSpec("functional/banking", "Nope").Message);
        Assert.NotNull(FeaturesApi.CheckCapability("../x"));
        Assert.NotNull(FeaturesApi.CheckCapability("/"));
        Assert.Null(FeaturesApi.CheckCapability("functional/banking"));
    }

    [Fact(DisplayName = "A linked test is checked against the latest results by the module that reads them")]
    public void CheckingTestLinks()
    {
        // no results file in the fixture: every link is reported as one that cannot be checked
        IReadOnlyList<LinkedTestRuns.PublicDTOs.CheckedTest> checks = LinkedTestRunsApi.CheckLinks([("Suite", "A test")]);

        Assert.Equal("Suite", checks[0].Suite);
        Assert.Contains("cannot be checked", checks[0].Warning);
        Assert.Single(LinkedTestRunsApi.MissingLinks([("Suite", "A test")]));
        Assert.Equal(("missing", "?"), (LinkedTestRunsApi.OutcomeOf("Suite", "A test").Outcome, LinkedTestRunsApi.OutcomeOf("Suite", "A test").Mark));
    }

    [Fact(DisplayName = "An agent's start of work adds it to the contributors, and an agent can never complete an item")]
    public void AgentStatusRules()
    {
        WorkItem item = WorkItemsApi.Get(123)!;

        Assert.Equal([.. item.Contributors, "Codex"], WorkItemsApi.ContributorsAfter(item, null, "In Progress", "Codex"));
        Assert.Null(WorkItemsApi.ContributorsAfter(item, null, "In Review", "Codex"));
        Assert.Contains("the user archives from the app", WorkItemsApi.CheckAgentStatus("Completed"));
        Assert.Contains("Unknown status", WorkItemsApi.CheckAgentStatus("Doing"));
        Assert.Null(WorkItemsApi.CheckAgentStatus("In Review"));
        Assert.Null(WorkItemsApi.CheckAgentName("Claude chunk W3a"));
        Assert.NotNull(WorkItemsApi.CheckAgentName("bad/name"));
    }
}
