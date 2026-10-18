using OpenSpecStore;
using WorkItems;
using WorkItems.PublicDTOs;

namespace TaskManager.Tests;

[Collection(nameof(ModuleStateCollection))]
public sealed class EditingWorkItemsTests
{
    private const string HealthYaml = "openspec/changes/add-health-check/.openspec.yaml";
    private const string HealthTasks = "openspec/changes/add-health-check/tasks.md";

    private static WorkItem Item(int id) => WorkItemsApi.Get(id)!;

    [Fact(DisplayName = "Changing the status saves it in x-rtm and keeps the rest of the file")]
    public void ChangeStatus()
    {
        using TestRepo repo = new();
        OpenSpecStoreApi.Open(repo.Root);

        (bool ok, string _) = WorkItemsApi.SetStatus(123, "Blocked");

        Assert.True(ok);
        Assert.Equal("Blocked", Item(123).Status);
        string yaml = repo.Read(HealthYaml);
        Assert.StartsWith("schema: spec-driven\ncreated: 2026-09-12\n# keep this comment\nx-rtm:\n", yaml);
        Assert.Contains("  status: Blocked\n", yaml);
        Assert.Contains("  title: Health-check endpoint\n", yaml);
    }

    [Fact(DisplayName = "Saving metadata keeps lists on one line, so the git diff shows only what changed")]
    public void FlowLists()
    {
        using TestRepo repo = new();
        string before = repo.Read(HealthYaml);
        OpenSpecStoreApi.Open(repo.Root);

        WorkItemsApi.SetStatus(123, "Blocked");

        Assert.Equal(before.Replace("status: In Progress", "status: Blocked"), repo.Read(HealthYaml));
    }

    [Fact(DisplayName = "The first edit of a change created outside the app writes its provisional number")]
    public void FirstEditWritesNumber()
    {
        using TestRepo repo = new();
        OpenSpecStoreApi.Open(repo.Root);
        WorkItemsApi.SetPriority(125, "high");
        Assert.Contains("x-rtm:\n  id: 125\n", repo.Read("openspec/changes/plain-change/.openspec.yaml"));
        Assert.Equal("high", Item(125).Priority);
    }

    [Fact(DisplayName = "Ticking a subtask writes [x] to that line of tasks.md only")]
    public void TickSubtask()
    {
        using TestRepo repo = new();
        string before = repo.Read(HealthTasks);
        OpenSpecStoreApi.Open(repo.Root);

        WorkItemsApi.SetSubtaskDone(123, "2.1", true);

        Assert.Equal(before.Replace("- [ ] 2.1", "- [x] 2.1"), repo.Read(HealthTasks));
    }

    [Fact(DisplayName = "Adding a subtask appends it to the last group with the next number")]
    public void AddSubtask()
    {
        using TestRepo repo = new();
        OpenSpecStoreApi.Open(repo.Root);
        WorkItemsApi.AddSubtask(123, "Write docs");
        Assert.EndsWith("- [ ] 2.1 Hide internals from anonymous callers\n- [ ] 2.2 Write docs\n", repo.Read(HealthTasks));
    }

    [Fact(DisplayName = "Renaming and editing the description update x-rtm and the proposal body")]
    public void TitleAndDescription()
    {
        using TestRepo repo = new();
        OpenSpecStoreApi.Open(repo.Root);
        WorkItemsApi.SetTitle(123, "Health endpoint");
        WorkItemsApi.SetDescription(123, "## Why\n\nNew text.\n");
        Assert.Equal("Health endpoint", Item(123).Title);
        Assert.Equal("# Proposal\n\n## Why\n\nNew text.\n", repo.Read("openspec/changes/add-health-check/proposal.md"));
    }

    [Fact(DisplayName = "Manual features and contributors are saved; features from specs/ cannot be removed")]
    public void FeaturesAndContributors()
    {
        using TestRepo repo = new();
        OpenSpecStoreApi.Open(repo.Root);
        WorkItemsApi.SetContributors(123, ["Mira"]);
        (bool ok, string message) = WorkItemsApi.SetManualFeatures(123, ["functional/banking"]);
        Assert.True(ok, message);
        Assert.Equal(["Mira"], Item(123).Contributors);
        Assert.Equal([new FeatureLink("non-functional/availability", true), new FeatureLink("functional/banking", false)], Item(123).Features);
    }

    [Fact(DisplayName = "Creating a work item makes a change folder with the next number")]
    public void Create()
    {
        using TestRepo repo = new();
        OpenSpecStoreApi.Open(repo.Root);

        (bool ok, string _, int id) = WorkItemsApi.Create("Rate-limit the API", status: "Blocked", features: ["non-functional/availability"], contributors: ["Artyom"]);

        Assert.True(ok);
        Assert.Equal(126, id);   // 125 is plain-change's provisional number
        WorkItem it = Item(id);
        Assert.Equal(("rate-limit-the-api", "Blocked"), (it.Folder, it.Status));
        Assert.Contains("schema: spec-driven", repo.Read("openspec/changes/rate-limit-the-api/.openspec.yaml"));
        Assert.StartsWith("# Proposal\n\n## Why\n", repo.Read("openspec/changes/rate-limit-the-api/proposal.md"));
        Assert.StartsWith("# Tasks\n", repo.Read("openspec/changes/rate-limit-the-api/tasks.md"));
    }

    [Fact(DisplayName = "Creating a work item never changes the number of a change created outside the app")]
    public void ProvisionalNumberIsStable()
    {
        using TestRepo repo = new();
        OpenSpecStoreApi.Open(repo.Root);
        Assert.Equal(125, WorkItemsApi.GetAll().Single(w => w.Folder == "plain-change").Id);

        WorkItemsApi.Create("Zebra item");   // sorts after plain-change, so a moving provisional number would show

        Assert.Equal(125, WorkItemsApi.GetAll().Single(w => w.Folder == "plain-change").Id);
        Assert.Contains("x-rtm:\n  id: 125\n", repo.Read("openspec/changes/plain-change/.openspec.yaml"));
    }

    [Fact(DisplayName = "A new work item without specs is marked skip_specs so OpenSpec accepts it")]
    public void NewItemSkipsSpecs()
    {
        using TestRepo repo = new();
        OpenSpecStoreApi.Open(repo.Root);
        WorkItemsApi.Create("Rate-limit the API");
        Assert.StartsWith("schema: spec-driven\nskip_specs: true\ncreated: ", repo.Read("openspec/changes/rate-limit-the-api/.openspec.yaml"));
        Assert.Contains("## What Changes", repo.Read("openspec/changes/rate-limit-the-api/proposal.md"));
    }

    [Fact(DisplayName = "Once a work item has delta specs, the next save removes skip_specs")]
    public void SpecsRemoveSkip()
    {
        using TestRepo repo = new();
        OpenSpecStoreApi.Open(repo.Root);
        (_, _, int id) = WorkItemsApi.Create("Rate-limit the API");
        repo.Write("openspec/changes/rate-limit-the-api/specs/functional/banking/spec.md", "## ADDED Requirements\n");

        WorkItemsApi.SetPriority(id, "high");

        Assert.DoesNotContain("skip_specs", repo.Read("openspec/changes/rate-limit-the-api/.openspec.yaml"));
    }

    [Fact(DisplayName = "Moving selected subtasks into a new work item renumbers both lists")]
    public void SplitSubtasks()
    {
        using TestRepo repo = new();
        OpenSpecStoreApi.Open(repo.Root);

        (bool ok, string _, int newId) = WorkItemsApi.MoveSubtasksToNew(123, ["1.3", "2.1"]);

        Assert.True(ok);
        Assert.Equal(["1.1", "1.2"], Item(123).Subtasks.Select(s => s.Number));
        Assert.Equal([("1.1", "Ping test in suite 543"), ("1.2", "Hide internals from anonymous callers")], Item(newId).Subtasks.Select(s => (s.Number, s.Text)));
        Assert.Equal(Item(123).Contributors, Item(newId).Contributors);
    }

    [Fact(DisplayName = "Merging work items combines proposals, subtasks, specs and people, and removes the sources")]
    public void Merge()
    {
        using TestRepo repo = new();
        OpenSpecStoreApi.Open(repo.Root);

        (bool ok, string message, int id) = WorkItemsApi.Merge([107, 125]);

        Assert.True(ok, message);
        WorkItem merged = Item(id);
        Assert.Null(WorkItemsApi.Get(107));
        Assert.Equal(["CSV columns agreed", "Export job", "First step"], merged.Subtasks.Select(s => s.Text));
        Assert.Equal(["functional/banking", "functional/remittances"], merged.Features.Select(f => f.Path).Order());
        Assert.Contains("Finance needs a monthly export.", WorkItemsApi.GetDescription(id));
        Assert.Contains("Created with the openspec CLI only.", WorkItemsApi.GetDescription(id));
    }

    [Fact(DisplayName = "Merging is refused when two items change the same requirement")]
    public void MergeConflict()
    {
        using TestRepo repo = new();
        repo.Write("openspec/changes/plain-change/specs/functional/remittances/spec.md", repo.Read("openspec/changes/export-remittances/specs/functional/remittances/spec.md"));
        OpenSpecStoreApi.Open(repo.Root);

        (bool ok, string message, int _) = WorkItemsApi.Merge([107, 125]);

        Assert.False(ok);
        Assert.Equal("#wi107 and #wi125 both change requirement \"Monthly export\" in functional/remittances", message);
        Assert.NotNull(WorkItemsApi.Get(107));
    }

    [Fact(DisplayName = "Adding a sub-category to a column saves it in config.yaml, and dropping an item into it is remembered")]
    public void Lanes()
    {
        using TestRepo repo = new();
        OpenSpecStoreApi.Open(repo.Root);

        WorkItemsApi.AddLane("Blocked", "Needs review");
        WorkItemsApi.SetLane(107, "Blocked", "Needs review");

        Assert.Equal(["Needs review"], WorkItemsApi.GetLanes()["Blocked"]);
        Assert.Equal("Needs review", Item(107).Lanes["Blocked"]);
        Assert.StartsWith("schema: spec-driven\n\n# lanes per column, written by the app\nx-rtm:\n", repo.Read("openspec/config.yaml"));
        WorkItemsApi.RemoveLane("Blocked", "Needs review");
        Assert.False(WorkItemsApi.GetLanes().ContainsKey("Blocked"));
    }

    [Fact(DisplayName = "Completing a work item archives the change through the OpenSpec CLI")]
    public void CompleteArchives()
    {
        using TestRepo repo = new();
        OpenSpecStoreApi.Open(repo.Root);
        string? archived = null;
        WorkItemsApi.ConfigureArchiver(folder => { archived = folder; Directory.Move(repo.PathOf($"openspec/changes/{folder}"), repo.PathOf($"openspec/changes/archive/2026-10-18-{folder}")); return (true, ""); });
        try
        {
            (bool ok, string _) = WorkItemsApi.SetStatus(107, "Completed");
            Assert.True(ok);
            Assert.Equal("export-remittances", archived);
            Assert.Equal("Completed", Item(107).Status);
        }
        finally { WorkItemsApi.ConfigureArchiver(null); }
    }

    [Fact(DisplayName = "A failed archive leaves the work item where it was and reports the CLI's message")]
    public void ArchiveFails()
    {
        using TestRepo repo = new();
        OpenSpecStoreApi.Open(repo.Root);
        WorkItemsApi.ConfigureArchiver(_ => (false, "1 task incomplete"));
        try
        {
            (bool ok, string message) = WorkItemsApi.SetStatus(107, "Completed");
            Assert.False(ok);
            Assert.Equal("1 task incomplete", message);
            Assert.Equal("Planned", Item(107).Status);
        }
        finally { WorkItemsApi.ConfigureArchiver(null); }
    }

    [Fact(DisplayName = "A completed work item cannot be moved back")]
    public void CannotReopen()
    {
        using TestRepo repo = new();
        OpenSpecStoreApi.Open(repo.Root);
        (bool ok, string message) = WorkItemsApi.SetStatus(124, "Planned");
        Assert.False(ok);
        Assert.Equal("Completed work items are archived and can't be moved back", message);
    }
}
