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

    /// <summary>A second change carrying the same number, as a copied folder or one created elsewhere would.</summary>
    internal static void Duplicate(TestRepo repo, string folder = "second-check", string created = "2026-10-20")
    {
        string from = repo.PathOf("openspec/changes/add-health-check"), to = repo.PathOf("openspec/changes/" + folder);
        foreach (string f in Directory.GetFiles(from, "*", SearchOption.AllDirectories))
        {
            if (f.Contains(Path.DirectorySeparatorChar + "notes" + Path.DirectorySeparatorChar)) continue;   // notes have numbers of their own
            string target = Path.Combine(to, Path.GetRelativePath(from, f));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(f, target);
        }
        repo.Write("openspec/changes/" + folder + "/.openspec.yaml", repo.Read("openspec/changes/" + folder + "/.openspec.yaml").Replace("created: 2026-09-12", "created: " + created));
    }

    [Fact(DisplayName = "Two changes with one number: the one created later takes the next free number, the other keeps it, and the warning goes")]
    public void DuplicateIdsAreResolved()
    {
        using TestRepo repo = new();
        OpenSpecStoreApi.Open(repo.Root);
        Duplicate(repo);
        Assert.Contains(WorkItemsApi.GetWarnings(), w => w.StartsWith("#wi123 is used by"));

        IReadOnlyList<(int OldId, int NewId, string Folder)> renumbered = WorkItemsApi.ResolveDuplicateIds();

        Assert.Equal([(123, 126, "second-check")], renumbered);   // the next free number after every change, archived and provisional ones included
        Assert.Equal("add-health-check", Item(123).Folder);
        Assert.Equal("second-check", Item(126).Folder);
        Assert.Contains("  id: 126", repo.Read("openspec/changes/second-check/.openspec.yaml"));
        Assert.Contains("  id: 125", repo.Read("openspec/changes/plain-change/.openspec.yaml"));   // its provisional number, stamped on the way
        Assert.DoesNotContain(WorkItemsApi.GetWarnings(), w => w.Contains("is used by"));
        Assert.Empty(WorkItemsApi.ResolveDuplicateIds());
    }

    [Fact(DisplayName = "When numbers collide, an archived change keeps its number, whatever was created when")]
    public void ArchivedKeepsItsNumber()
    {
        using TestRepo repo = new();
        OpenSpecStoreApi.Open(repo.Root);
        string archived = "openspec/changes/archive/2026-10-01-old-check";
        Directory.CreateDirectory(repo.PathOf(archived));
        repo.Write(archived + "/.openspec.yaml", "schema: spec-driven\ncreated: 2026-10-25\nx-rtm:\n  id: 123\n  title: Old check\n");
        repo.Write(archived + "/proposal.md", "## Why\n\nold\n");

        IReadOnlyList<(int OldId, int NewId, string Folder)> renumbered = WorkItemsApi.ResolveDuplicateIds();

        Assert.Equal([(123, 126, "add-health-check")], renumbered);
        Assert.Equal("Old check", Item(123).Title);
        Assert.Equal("Health-check endpoint", Item(126).Title);
    }

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

        // the status line changes and the move is stamped (x-rtm.moved); nothing else moves
        Assert.Equal(before.Replace("status: In Progress", "status: Blocked") + $"  moved: {DateOnly.FromDateTime(DateTime.Today):yyyy-MM-dd}\n", repo.Read(HealthYaml));
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

    [Fact(DisplayName = "Duplicate subtask numbers never collide: the later line reads as the next free number and the file is renumbered on the next write")]
    public void DuplicateSubtaskNumbers()
    {
        using TestRepo repo = new();
        string tasks = "# Tasks\n\n## 1. Endpoint\n\n- [ ] 1.1 First\n- [ ] 1.2 Second\n- [ ] 1.2 Twin of second\n- [x] 1.3 Third\n\n## 2. Hardening\n\n- [ ] 2.1 Hide\n- [ ] 2.1 Hide twin\n";
        repo.Write(HealthTasks, tasks);
        OpenSpecStoreApi.Open(repo.Root);

        Assert.Equal(["1.1", "1.2", "1.4", "1.3", "2.1", "2.2"], Item(123).Subtasks.Select(s => s.Number));
        Assert.Equal("Twin of second", Item(123).Subtasks.Single(s => s.Number == "1.4").Text);
        Assert.Equal(tasks, repo.Read(HealthTasks));   // reading changes nothing

        WorkItemsApi.SetSubtaskDone(123, "1.4", true);

        Assert.Equal(tasks.Replace("- [ ] 1.2 Twin of second", "- [x] 1.4 Twin of second").Replace("- [ ] 2.1 Hide twin", "- [ ] 2.2 Hide twin"), repo.Read(HealthTasks));
        WorkItemsApi.AddSubtask(123, "Write docs");
        Assert.EndsWith("- [ ] 2.2 Hide twin\n- [ ] 2.3 Write docs\n", repo.Read(HealthTasks));

        repo.Write(HealthTasks, "# Tasks\n\n## 1. Endpoint\n\n- [ ] 1.2 Later\n- [ ] 1.1 Earlier\n");   // the last line is not the highest: the next number must still be free
        OpenSpecStoreApi.MarkOwnChange(repo.PathOf(HealthTasks));
        WorkItemsApi.AddSubtask(123, "Newest");
        Assert.EndsWith("- [ ] 1.1 Earlier\n- [ ] 1.3 Newest\n", repo.Read(HealthTasks));
    }

    [Fact(DisplayName = "The first subtask of a new work item goes under its existing group heading")]
    public void FirstSubtaskOfNewItem()
    {
        using TestRepo repo = new();
        OpenSpecStoreApi.Open(repo.Root);
        (_, _, int id) = WorkItemsApi.Create("Rate-limit the API");

        WorkItemsApi.AddSubtask(id, "Pick a limiter");
        WorkItemsApi.AddSubtask(id, "Load test");

        Assert.Equal("# Tasks\n\n## 1. Tasks\n\n- [ ] 1.1 Pick a limiter\n- [ ] 1.2 Load test\n", repo.Read("openspec/changes/rate-limit-the-api/tasks.md"));
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

    [Fact(DisplayName = "Contributors are saved")]
    public void Contributors()
    {
        using TestRepo repo = new();
        OpenSpecStoreApi.Open(repo.Root);
        Assert.True(WorkItemsApi.SetContributors(123, ["Mira"]).Success);
        Assert.Equal(["Mira"], Item(123).Contributors);
    }

    [Fact(DisplayName = "A work item has one feature: one picked by hand wins over its specs, otherwise its first spec")]
    public void OneFeature()
    {
        using TestRepo repo = new();
        OpenSpecStoreApi.Open(repo.Root);

        Assert.Equal([new FeatureLink("non-functional/security", false)], Item(123).Features);   // x-rtm lists Security; its spec is for Availability
        Assert.True(WorkItemsApi.MoveFeature(123, null).Success);
        Assert.Empty(Item(123).Features);   // Security left x-rtm, Availability is unlinked: none
        Assert.True(WorkItemsApi.MoveFeature(123, "non-functional/availability").Success);
        Assert.Equal([new FeatureLink("non-functional/availability", true)], Item(123).Features);
    }

    [Fact(DisplayName = "Moving an item to a feature makes that its only feature, completed or open: spec features are unlinked (the spec stays), moving back relinks")]
    public void MoveCompletedBetweenFeatures()
    {
        using TestRepo repo = new();
        OpenSpecStoreApi.Open(repo.Root);
        const string yaml = "openspec/changes/archive/2026-09-18-session-timeout/.openspec.yaml";
        Assert.Equal([new FeatureLink("non-functional/security", true)], Item(124).Features);

        Assert.True(WorkItemsApi.MoveFeature(124, "non-functional/availability").Success);
        Assert.Equal([new FeatureLink("non-functional/availability", false)], Item(124).Features);
        Assert.Contains("unlinkedFeatures: [non-functional/security]", repo.Read(yaml));

        Assert.True(WorkItemsApi.MoveFeature(124, "non-functional/security").Success);
        Assert.Equal([new FeatureLink("non-functional/security", true)], Item(124).Features);

        Assert.True(WorkItemsApi.MoveFeature(124, null).Success);   // to Unassigned
        Assert.Empty(Item(124).Features);

        // an item with a spec for Availability and Security picked by hand ends up in the target only; its spec stays
        Assert.True(WorkItemsApi.MoveFeature(123, "functional/banking").Success);
        Assert.Equal([new FeatureLink("functional/banking", false)], Item(123).Features);
        Assert.Equal(["non-functional/availability"], Item(123).UnlinkedFeatures);
        Assert.True(File.Exists(repo.PathOf("openspec/changes/add-health-check/specs/non-functional/availability/spec.md")));
        Assert.True(WorkItemsApi.MoveFeature(123, "non-functional/availability").Success);
        Assert.Equal([new FeatureLink("non-functional/availability", true)], Item(123).Features);
    }

    [Fact(DisplayName = "Moving an item to In Progress stamps the day it started, once; start and due dates are set as dates and written as yyyy-MM-dd")]
    public void StartedAndDue()
    {
        using TestRepo repo = new();
        OpenSpecStoreApi.Open(repo.Root);
        string Yaml() => File.ReadAllText(Path.Combine(Item(107).Path, ".openspec.yaml"));
        DateOnly today = DateOnly.FromDateTime(DateTime.Today);
        Assert.Null(Item(107).Started);

        Assert.True(WorkItemsApi.SetStatus(107, "In Progress").Success);
        Assert.Equal(today, Item(107).Started);
        Assert.Contains($"started: {today:yyyy-MM-dd}", Yaml());

        Assert.True(WorkItemsApi.SetStarted(107, new DateOnly(2026, 10, 20)).Success);
        Assert.True(WorkItemsApi.SetStatus(107, "Planned").Success);
        Assert.True(WorkItemsApi.SetStatus(107, "In Progress").Success);
        Assert.Equal(new DateOnly(2026, 10, 20), Item(107).Started);   // a start date already there is kept

        Assert.True(WorkItemsApi.SetDue(107, new DateOnly(2026, 11, 5)).Success);
        Assert.Equal(new DateOnly(2026, 11, 5), Item(107).Due);
        Assert.Contains("due: 2026-11-05", Yaml());
        Assert.True(WorkItemsApi.SetDue(107, null).Success);
        Assert.Null(Item(107).Due);
        Assert.DoesNotContain("due:", Yaml());
    }

    [Fact(DisplayName = "A work item links a plan file: SetPlan writes x-rtm.plan as a repo-relative path, it reads back, and null removes the key")]
    public void PlanLink()
    {
        using TestRepo repo = new();
        OpenSpecStoreApi.Open(repo.Root);
        string Yaml() => File.ReadAllText(Path.Combine(Item(107).Path, ".openspec.yaml"));
        Assert.Null(Item(107).Plan);

        Assert.True(WorkItemsApi.SetPlan(107, "DevResources/plans/export-2026-11-05.md").Success);
        Assert.Equal("DevResources/plans/export-2026-11-05.md", Item(107).Plan);
        Assert.Contains("plan: DevResources/plans/export-2026-11-05.md", Yaml());

        Assert.False(WorkItemsApi.SetPlan(107, "../x.md").Success);   // a plan stays inside the repository
        Assert.False(WorkItemsApi.SetPlan(107, "C:/x.md").Success);
        Assert.Equal("DevResources/plans/export-2026-11-05.md", Item(107).Plan);

        Assert.True(WorkItemsApi.SetPlan(107, null).Success);
        Assert.Null(Item(107).Plan);
        Assert.DoesNotContain("plan:", Yaml());
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

    [Fact(DisplayName = "Renaming a subtask rewrites only its text in tasks.md, keeping its box, number and the rest of the file")]
    public void RenameSubtask()
    {
        using TestRepo repo = new();
        OpenSpecStoreApi.Open(repo.Root);
        string before = repo.Read(HealthTasks);

        Assert.True(WorkItemsApi.SetSubtaskText(123, "1.1", "  Add /healthz route  ").Success);

        Assert.Equal(before.Replace("- [x] 1.1 Add /health route", "- [x] 1.1 Add /healthz route"), repo.Read(HealthTasks));
        Assert.Equal("Add /healthz route", Item(123).Subtasks[0].Text);
        Assert.True(Item(123).Subtasks[0].Done);
    }

    [Fact(DisplayName = "A subtask can't be renamed to nothing")]
    public void RenameSubtaskBlank()
    {
        using TestRepo repo = new();
        OpenSpecStoreApi.Open(repo.Root);
        string before = repo.Read(HealthTasks);

        (bool ok, string message) = WorkItemsApi.SetSubtaskText(123, "2.1", "   ");

        Assert.False(ok);
        Assert.Equal("A subtask needs some text", message);
        Assert.Equal(before, repo.Read(HealthTasks));
    }

    [Fact(DisplayName = "A single subtask made into a work item or a dependency keeps its text as the new item's title")]
    public void SplitOneKeepsText()
    {
        using TestRepo repo = new();
        OpenSpecStoreApi.Open(repo.Root);

        (bool _, string _, int item) = WorkItemsApi.MoveSubtasksToNew(123, ["1.3"]);
        (bool _, string _, int dependency) = WorkItemsApi.MoveSubtasksToDependency(123, ["2.1"]);

        Assert.Equal("Ping test in suite 543", Item(item).Title);
        Assert.Equal("Hide internals from anonymous callers", Item(dependency).Title);
        Assert.EndsWith("ping-test-in-suite-543", Item(item).Path);
    }

    [Fact(DisplayName = "Several subtasks moved together get a title that names the item they came from")]
    public void SplitSeveralNamesSource()
    {
        using TestRepo repo = new();
        OpenSpecStoreApi.Open(repo.Root);

        (bool _, string _, int newId) = WorkItemsApi.MoveSubtasksToNew(123, ["1.3", "2.1"]);

        Assert.Equal("Split from #wi123", Item(newId).Title);
    }

    [Fact(DisplayName = "Turning a subtask into a dependency creates a new item and blocks the parent on it")]
    public void SplitAsDependency()
    {
        using TestRepo repo = new();
        OpenSpecStoreApi.Open(repo.Root);

        (bool ok, string _, int newId) = WorkItemsApi.MoveSubtasksToDependency(123, ["1.3"]);

        Assert.True(ok);
        Assert.Equal(["1.1", "1.2", "2.1"], Item(123).Subtasks.Select(s => s.Number));
        Assert.Equal("Blocked", Item(123).Status);
        Assert.Equal([newId], Item(123).DependsOn);
    }

    [Fact(DisplayName = "A work item blocked by an unresolved dependency refuses other status changes")]
    public void BlockedByDependencyRefusesStatus()
    {
        using TestRepo repo = new();
        OpenSpecStoreApi.Open(repo.Root);
        (bool _, string _, int newId) = WorkItemsApi.MoveSubtasksToDependency(123, ["1.3"]);

        (bool ok, string message) = WorkItemsApi.SetStatus(123, "Planned");

        Assert.False(ok);
        Assert.Contains($"#wi{newId}", message);
        Assert.Equal("Blocked", Item(123).Status);
    }

    [Fact(DisplayName = "A change folder name is never reused: a name that exists in the archive, under any date, is skipped when a work item is created")]
    public void FolderNameReusedAfterArchive()
    {
        using TestRepo repo = new();
        OpenSpecStoreApi.Open(repo.Root);
        WorkItemsApi.ConfigureArchiver(folder => { Directory.Move(repo.PathOf($"openspec/changes/{folder}"), repo.PathOf($"openspec/changes/archive/2026-10-23-{folder}")); return (true, ""); });
        try
        {
            Assert.True(WorkItemsApi.SetStatus(107, "Completed").Success);
            Assert.True(Directory.Exists(repo.PathOf("openspec/changes/archive/2026-10-23-export-remittances")));

            (bool ok, string message, int again) = WorkItemsApi.Create("Export remittances", "Backlog", null, null);
            Assert.True(ok, message);
            Assert.Equal(repo.PathOf("openspec/changes/export-remittances-2"), Item(again).Path);

            (_, _, int first) = WorkItemsApi.Create("New work item", "Backlog", null, null);
            (_, _, int second) = WorkItemsApi.Create("New work item", "Backlog", null, null);
            Assert.Equal(repo.PathOf("openspec/changes/new-work-item"), Item(first).Path);
            Assert.Equal(repo.PathOf("openspec/changes/new-work-item-2"), Item(second).Path);
        }
        finally { WorkItemsApi.ConfigureArchiver(null); }
    }

    [Fact(DisplayName = "Completing an item whose folder was archived earlier today renames the folder first, so the archive does not clash")]
    public void CompleteDodgesSameDayArchiveClash()
    {
        using TestRepo repo = new();
        OpenSpecStoreApi.Open(repo.Root);
        string today = DateTime.Today.ToString("yyyy-MM-dd");
        repo.Write($"openspec/changes/archive/{today}-export-remittances/.openspec.yaml", "schema: spec-driven\ncreated: 2026-01-01\n");
        string? archived = null;
        WorkItemsApi.ConfigureArchiver(folder => { archived = folder; Directory.Move(repo.PathOf($"openspec/changes/{folder}"), repo.PathOf($"openspec/changes/archive/{today}-{folder}")); return (true, ""); });
        try
        {
            (bool ok, string message) = WorkItemsApi.SetStatus(107, "Completed");

            Assert.True(ok, message);
            Assert.Equal("export-remittances-2", archived);
            Assert.Equal(107, Item(107).Id);
            Assert.Equal("Completed", Item(107).Status);
            Assert.True(Directory.Exists(repo.PathOf($"openspec/changes/archive/{today}-export-remittances")));
            Assert.True(Directory.Exists(repo.PathOf($"openspec/changes/archive/{today}-export-remittances-2")));
        }
        finally { WorkItemsApi.ConfigureArchiver(null); }
    }

    [Fact(DisplayName = "Once the dependency is archived, the parent can change status again")]
    public void DependencyResolvedUnblocks()
    {
        using TestRepo repo = new();
        OpenSpecStoreApi.Open(repo.Root);
        WorkItemsApi.ConfigureArchiver(folder => { Directory.Move(repo.PathOf($"openspec/changes/{folder}"), repo.PathOf($"openspec/changes/archive/2026-10-21-{folder}")); return (true, ""); });
        try
        {
            (bool _, string _, int newId) = WorkItemsApi.MoveSubtasksToDependency(123, ["1.3"]);
            WorkItemsApi.SetStatus(newId, "Completed");

            (bool ok, string message) = WorkItemsApi.SetStatus(123, "Planned");

            Assert.True(ok, message);
            Assert.Equal("Planned", Item(123).Status);
        }
        finally { WorkItemsApi.ConfigureArchiver(null); }
    }

    [Fact(DisplayName = "An existing work item can be made a dependency by its id, which blocks this one")]
    public void AddExistingDependency()
    {
        using TestRepo repo = new();
        OpenSpecStoreApi.Open(repo.Root);

        (bool ok, string message) = WorkItemsApi.AddDependency(107, 123);

        Assert.True(ok, message);
        Assert.Equal([123], Item(107).DependsOn);
        Assert.Equal("Blocked", Item(107).Status);
        Assert.Equal(2, Item(107).Subtasks.Count);   // no subtask line added
    }

    [Theory(DisplayName = "A dependency is refused when it's unknown or this item itself")]
    [InlineData(999, "No work item")]
    [InlineData(107, "itself")]
    public void RefusedDependencies(int dependency, string expected)
    {
        using TestRepo repo = new();
        OpenSpecStoreApi.Open(repo.Root);

        (bool ok, string message) = WorkItemsApi.AddDependency(107, dependency);

        Assert.False(ok);
        Assert.Contains(expected, message);
        Assert.Empty(Item(107).DependsOn!);
        Assert.Equal("Planned", Item(107).Status);
    }

    [Theory(DisplayName = "A completed or in-review item is linked as a dependency but doesn't block, so status stays free")]
    [InlineData(124)]   // archived (Completed)
    [InlineData(125)]   // set to In Review below
    public void DoneDependencyDoesNotBlock(int dependency)
    {
        using TestRepo repo = new();
        OpenSpecStoreApi.Open(repo.Root);
        WorkItemsApi.SetStatus(125, "In Review");

        Assert.True(WorkItemsApi.AddDependency(107, dependency).Success);

        Assert.Equal([dependency], Item(107).DependsOn);
        Assert.Equal("Planned", Item(107).Status);
        Assert.True(WorkItemsApi.SetStatus(107, "In Progress").Success);
    }

    [Fact(DisplayName = "A dependency moving to In Review unblocks the item waiting on it")]
    public void InReviewUnblocks()
    {
        using TestRepo repo = new();
        OpenSpecStoreApi.Open(repo.Root);
        WorkItemsApi.AddDependency(107, 123);
        Assert.False(WorkItemsApi.SetStatus(107, "Planned").Success);

        WorkItemsApi.SetStatus(123, "In Review");

        Assert.True(WorkItemsApi.SetStatus(107, "Planned").Success);
    }

    [Fact(DisplayName = "The same dependency isn't added twice, and two items can't wait on each other")]
    public void RefusedDuplicatesAndCycles()
    {
        using TestRepo repo = new();
        OpenSpecStoreApi.Open(repo.Root);

        Assert.True(WorkItemsApi.AddDependency(107, 123).Success);
        Assert.Contains("Already", WorkItemsApi.AddDependency(107, 123).Message);
        Assert.Contains("waits on", WorkItemsApi.AddDependency(123, 107).Message);
    }

    [Fact(DisplayName = "A dependency's work items list shows what it is blocking")]
    public void BlockingList()
    {
        using TestRepo repo = new();
        OpenSpecStoreApi.Open(repo.Root);
        (bool _, string _, int newId) = WorkItemsApi.MoveSubtasksToDependency(123, ["1.3"]);

        IReadOnlyList<WorkItem> blocking = WorkItemsApi.GetBlocking(newId);

        Assert.Equal([123], blocking.Select(b => b.Id));
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
        Assert.Equal(["functional/banking"], merged.Features.Select(f => f.Path));   // both specs come along; one feature: the first
        Assert.Contains("Finance needs a monthly export.", WorkItemsApi.GetDescription(id));
        Assert.Contains("Created with the openspec CLI only.", Notes.NotesApi.GetForWorkItem(id).Single(n => n.By == "Merge").Body);   // the other source's description is a note
    }

    [Fact(DisplayName = "Merging into a chosen item keeps that item's title and description; the other sources' descriptions become notes on the merged item")]
    public void MergeIntoChosenItem()
    {
        using TestRepo repo = new();
        OpenSpecStoreApi.Open(repo.Root);
        string title = Item(123).Title, description = WorkItemsApi.GetDescription(123), otherDescription = WorkItemsApi.GetDescription(107);
        int lastNote = Notes.NotesApi.GetAll().Max(n => n.Id);

        Assert.False(WorkItemsApi.Merge([107, 123], 999).Success);
        Assert.NotNull(WorkItemsApi.Get(107));
        (bool ok, string message, int id) = WorkItemsApi.Merge([107, 123], 123);

        Assert.True(ok, message);
        Assert.Equal(title, Item(id).Title);
        Assert.Equal(description.Trim(), WorkItemsApi.GetDescription(id).Trim());
        Notes.PublicDTOs.Note note = Notes.NotesApi.GetForWorkItem(id).Single(n => n.Id > lastNote);
        Assert.StartsWith("# Merged from #wi107", note.Body);
        Assert.Contains(otherDescription.Trim(), note.Body);
        Assert.Equal(Path.Combine(Item(id).Path, "notes", $"n{note.Id}.md"), note.Path);
    }

    [Fact(DisplayName = "Merging keeps every note (the change log) of the sources, under their own numbers")]
    public void MergeKeepsNotes()
    {
        using TestRepo repo = new();
        OpenSpecStoreApi.Open(repo.Root);
        (bool _, string _, int extra) = Notes.NotesApi.Create(107, "Tom");

        (bool ok, string message, int id) = WorkItemsApi.Merge([107, 123]);

        Assert.True(ok, message);
        Assert.Equal([123, extra, extra + 1], Notes.NotesApi.GetForWorkItem(id).Select(n => n.Id).Order());   // extra + 1: the note holding 123's description
        Assert.Contains("See [[runbook/uptime]] and #wi107.", Notes.NotesApi.Get(123)!.Body);
    }

    [Fact(DisplayName = "Merging keeps the label, linked commits and tests, lanes and dependencies of the sources")]
    public void MergeKeepsLinks()
    {
        using TestRepo repo = new();
        OpenSpecStoreApi.Open(repo.Root);
        WorkItemsApi.SetLabel(125, "bug");
        WorkItemsApi.SetLinkedCommits(107, ["abc1234"]);
        WorkItemsApi.SetLinkedCommits(125, ["abc1234", "def5678"]);
        WorkItemsApi.SetLinkedTests(123, [new TestLink("Suite", "Ping test", null)]);
        WorkItemsApi.SetLinkedTests(107, [new TestLink("Suite", "Ping test", 7), new TestLink("Suite", "Other", null)]);
        WorkItemsApi.AddLane("In Progress", "Needs review");
        WorkItemsApi.SetLane(123, "In Progress", "Needs review");
        (bool _, string _, int dep) = WorkItemsApi.Create("Dependency", "Backlog", null, null);
        WorkItemsApi.AddDependency(107, dep);
        WorkItemsApi.AddDependency(123, 125);   // inside the merge: must not point at itself afterwards

        (bool ok, string message, int id) = WorkItemsApi.Merge([107, 123, 125]);

        Assert.True(ok, message);
        WorkItem merged = Item(id);
        Assert.Equal("bug", merged.Label);
        Assert.Equal(["abc1234", "def5678"], merged.Commits);
        Assert.Equal(2, merged.Tests!.Count);
        Assert.Equal(7, merged.Tests!.Single(t => t.Test == "Ping test").Note);   // the copy that has a note wins
        Assert.Equal("Needs review", merged.Lanes["In Progress"]);
        Assert.Equal([dep], merged.DependsOn);
        Assert.Equal([107, 123, 125], merged.MergedFrom);
    }

    [Fact(DisplayName = "Merging points items that waited on a source at the merged item instead")]
    public void MergeRepointsDependants()
    {
        using TestRepo repo = new();
        OpenSpecStoreApi.Open(repo.Root);
        (bool _, string _, int waiting) = WorkItemsApi.Create("Waits on 107", "Backlog", null, null);
        WorkItemsApi.AddDependency(waiting, 107);

        (bool ok, string message, int id) = WorkItemsApi.Merge([107, 123]);

        Assert.True(ok, message);
        Assert.Equal([id], Item(waiting).DependsOn);
    }

    [Fact(DisplayName = "Merging keeps each source's task groups, headed by the item they came from")]
    public void MergeKeepsTaskGroups()
    {
        using TestRepo repo = new();
        OpenSpecStoreApi.Open(repo.Root);

        (bool ok, string message, int id) = WorkItemsApi.Merge([107, 123]);

        Assert.True(ok, message);
        string tasks = repo.Read($"openspec/changes/{Path.GetFileName(Item(id).Path)}/tasks.md");
        Assert.Contains("## 1. Monthly remittance export", tasks);
        string today = DateTime.Now.ToString("yyyy-MM-dd");
        Assert.Contains($"## 2. #wi123 Health-check endpoint · Endpoint · merged {today}", tasks);
        Assert.Contains($"## 3. #wi123 Health-check endpoint · Hardening · merged {today}", tasks);
        Assert.Contains("- [ ] 3.1 Hide internals from anonymous callers", tasks);
        Assert.Equal(["1. Monthly remittance export", $"2. #wi123 Health-check endpoint · Endpoint · merged {today}", $"3. #wi123 Health-check endpoint · Hardening · merged {today}"], Item(id).Subtasks.Select(s => s.Group).Distinct());
    }

    [Fact(DisplayName = "Merging keeps other files in a source's folder, under from-wi<id>/")]
    public void MergeKeepsOtherFiles()
    {
        using TestRepo repo = new();
        repo.Write("openspec/changes/export-remittances/design.md", "# Design\n\nUse a queue.\n");
        repo.Write("openspec/changes/export-remittances/extra/diagram.txt", "a -> b\n");
        OpenSpecStoreApi.Open(repo.Root);

        (bool ok, string message, int id) = WorkItemsApi.Merge([107, 123]);

        Assert.True(ok, message);
        string folder = Path.GetFileName(Item(id).Path);
        Assert.Contains("Use a queue.", repo.Read($"openspec/changes/{folder}/from-wi107/design.md"));
        Assert.Contains("a -> b", repo.Read($"openspec/changes/{folder}/from-wi107/extra/diagram.txt"));
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

    [Fact(DisplayName = "Adding a subgroup to a column saves it in config.yaml, and dropping an item into it is remembered")]
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

    [Fact(DisplayName = "Adding a subgroup that already exists is a quiet no-op, and placing an item in an unknown subgroup creates it")]
    public void LaneNamesNeverDuplicate()
    {
        using TestRepo repo = new();
        OpenSpecStoreApi.Open(repo.Root);

        Assert.True(WorkItemsApi.AddLane("In Progress", "  high PRIORITY ").Success);   // already there, whatever the case and spacing
        Assert.True(WorkItemsApi.AddLane("Blocked", "Needs review").Success);
        Assert.True(WorkItemsApi.AddLane("Blocked", "needs review").Success);

        Assert.Equal(["High priority", "Lower priority"], WorkItemsApi.GetLanes()["In Progress"]);
        Assert.Equal(["Needs review"], WorkItemsApi.GetLanes()["Blocked"]);

        Assert.True(WorkItemsApi.SetLane(107, "Backlog", "Someday").Success);   // no such subgroup yet
        Assert.True(WorkItemsApi.SetLane(125, "Blocked", "NEEDS REVIEW").Success);   // the existing spelling wins

        Assert.Equal(["Someday"], WorkItemsApi.GetLanes()["Backlog"]);
        Assert.Equal(["Needs review"], WorkItemsApi.GetLanes()["Blocked"]);
        Assert.Equal("Someday", Item(107).Lanes["Backlog"]);
        Assert.Equal("Needs review", Item(125).Lanes["Blocked"]);
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

    internal const string RemittancesMain = "# Remittances Specification\n\n## Purpose\nSending money abroad from the sample finance app the tests use, fees and limits included.\n\n## Requirements\n\n### Requirement: Send money\nThe system SHALL send money.\n\n#### Scenario: Works\n- **WHEN** sent\n- **THEN** it arrives\n\n#### Scenario: Fee shown\n- **WHEN** the user enters an amount\n- **THEN** the fee is shown first\n\n#### Scenario: Limit\n- **WHEN** over the limit\n- **THEN** it is refused\n";
    internal const string RemittancesDelta ="## MODIFIED Requirements\n\n### Requirement: Send money\nThe system SHALL send money and export it.\n\n#### Scenario: Works\n- **WHEN** sent\n- **THEN** it arrives and is exported\n\n## ADDED Requirements\n\n### Requirement: Export\nThe system SHALL export.\n\n#### Scenario: Export works\n- **WHEN** exported\n- **THEN** a file appears\n";

    [Fact(DisplayName = "Completing an item first puts back into its modified requirements every scenario the main spec has gained, so the archive never drops one")]
    public void CompleteRefreshesStaleDeltas()
    {
        using TestRepo repo = new();
        OpenSpecStoreApi.Open(repo.Root);
        // another item's archive gave the main spec two more scenarios; #wi107's delta still holds the old one-scenario copy
        repo.Write("openspec/specs/functional/remittances/spec.md", RemittancesMain);
        const string delta = "openspec/changes/export-remittances/specs/functional/remittances/spec.md";
        repo.Write(delta, RemittancesDelta);
        string? seen = null;
        WorkItemsApi.ConfigureArchiver(folder => { seen = repo.Read(delta); Directory.Move(repo.PathOf($"openspec/changes/{folder}"), repo.PathOf($"openspec/changes/archive/2026-11-01-{folder}")); return (true, ""); });
        try
        {
            Assert.True(WorkItemsApi.SetStatus(107, "Completed").Success);
            Assert.NotNull(seen);
            Assert.Contains("it arrives and is exported", seen);   // the change's own text stays
            Assert.True(seen.IndexOf("#### Scenario: Works") < seen.IndexOf("#### Scenario: Fee shown"), seen);
            Assert.True(seen.IndexOf("#### Scenario: Fee shown") < seen.IndexOf("#### Scenario: Limit"), seen);
            Assert.True(seen.IndexOf("- **THEN** it is refused") < seen.IndexOf("## ADDED Requirements"), seen);   // appended inside the block, before the next section
            Assert.Single(System.Text.RegularExpressions.Regex.Matches(seen, "#### Scenario: Export works"));   // ADDED blocks untouched
        }
        finally { WorkItemsApi.ConfigureArchiver(null); }
    }

    [Fact(DisplayName = "Refreshing a delta changes nothing when it already has every scenario, and a modified requirement the main spec no longer has becomes an added one")]
    public void RefreshIsIdempotent()
    {
        string once = WorkItems.BusinessRules.DeltaSpec.Refresh(RemittancesDelta, RemittancesMain);
        Assert.Equal(once, WorkItems.BusinessRules.DeltaSpec.Refresh(once, RemittancesMain));
        string gone = WorkItems.BusinessRules.DeltaSpec.Refresh(RemittancesDelta, RemittancesMain.Replace("Send money", "Receive money"));
        Assert.DoesNotContain("## MODIFIED Requirements", gone);   // the CLI refuses a MODIFIED block for a requirement that is not there
        Assert.True(gone.IndexOf("### Requirement: Send money") > gone.IndexOf("## ADDED Requirements"), gone);
        Assert.Contains("it arrives and is exported", gone);
        string crlf = RemittancesDelta.Replace("\n", "\r\n");
        Assert.DoesNotContain("\n\n\n", WorkItems.BusinessRules.DeltaSpec.Refresh(crlf, RemittancesMain).Replace("\r\n", "\n"));
        Assert.Contains("\r\n#### Scenario: Limit\r\n", WorkItems.BusinessRules.DeltaSpec.Refresh(crlf, RemittancesMain));   // the file's own line endings
    }

    [Fact(DisplayName = "An ADDED requirement that the main spec already has is archived as a modification: the block moves to MODIFIED and keeps every main-spec scenario")]
    public void AddedRequirementTheMainSpecHasBecomesModified()
    {
        const string delta = "## ADDED Requirements\n\n### Requirement: Send money\nThe system SHALL send money and export it.\n\n#### Scenario: Works\n- **WHEN** sent\n- **THEN** it arrives and is exported\n";
        string refreshed = WorkItems.BusinessRules.DeltaSpec.Refresh(delta, RemittancesMain);

        Assert.DoesNotContain("## ADDED Requirements", refreshed);
        Assert.True(refreshed.IndexOf("## MODIFIED Requirements") < refreshed.IndexOf("### Requirement: Send money"), refreshed);
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(refreshed, "### Requirement: Send money"));
        Assert.Contains("it arrives and is exported", refreshed);   // the change's own text stays
        int works = refreshed.IndexOf("#### Scenario: Works"), fee = refreshed.IndexOf("#### Scenario: Fee shown"), limit = refreshed.IndexOf("#### Scenario: Limit");
        Assert.True(works >= 0 && works < fee && fee < limit, refreshed);
        Assert.Equal(refreshed, WorkItems.BusinessRules.DeltaSpec.Refresh(refreshed, RemittancesMain));
        // a new requirement next to it stays ADDED; the MODIFIED section is created once
        string mixed = WorkItems.BusinessRules.DeltaSpec.Refresh(RemittancesDelta.Replace("## MODIFIED Requirements", "## ADDED Requirements"), RemittancesMain);
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(mixed, "## MODIFIED Requirements"));
        Assert.True(mixed.IndexOf("### Requirement: Send money") > mixed.IndexOf("## MODIFIED Requirements"), mixed);
        Assert.True(mixed.IndexOf("### Requirement: Export") > mixed.IndexOf("## ADDED Requirements"), mixed);
    }

    [Fact(DisplayName = "A requirement listed twice in one delta is merged into one block, and a REMOVED requirement the main spec does not have is dropped")]
    public void DuplicateRequirementsMergeAndDeadRemovalsGo()
    {
        const string delta = "## ADDED Requirements\n\n### Requirement: Export\nThe system SHALL export.\n\n#### Scenario: Export works\n- **WHEN** exported\n- **THEN** a file appears\n\n"
            + "### Requirement: Export\nThe system SHALL export twice.\n\n#### Scenario: Export works\n- **WHEN** again\n- **THEN** ignored duplicate\n\n#### Scenario: Export fails\n- **WHEN** the disk is full\n- **THEN** it says so\n\n"
            + "## MODIFIED Requirements\n\n### Requirement: Export\nThe system SHALL export, stronger.\n\n#### Scenario: Export is signed\n- **WHEN** exported\n- **THEN** it is signed\n\n"
            + "## REMOVED Requirements\n\n### Requirement: Fax\n**Reason**: gone\n\n### Requirement: Send money\n**Reason**: kept in main, so this one stays\n\n"
            + "## RENAMED Requirements\n\n- FROM: `### Requirement: Telex`\n- TO: `### Requirement: Email`\n";
        string refreshed = WorkItems.BusinessRules.DeltaSpec.Refresh(delta, RemittancesMain);

        Assert.Single(System.Text.RegularExpressions.Regex.Matches(refreshed, "### Requirement: Export"));
        Assert.Contains("The system SHALL export.", refreshed);   // the first text wins
        Assert.DoesNotContain("export twice", refreshed);
        Assert.DoesNotContain("ignored duplicate", refreshed);   // same scenario name: first occurrence wins
        foreach (string s in new[] { "Export works", "Export fails", "Export is signed" })
            Assert.Single(System.Text.RegularExpressions.Regex.Matches(refreshed, "#### Scenario: " + s));
        Assert.DoesNotContain("## MODIFIED Requirements", refreshed);   // one block, and the main spec has no "Export": it is an addition
        Assert.True(refreshed.IndexOf("### Requirement: Export") > refreshed.IndexOf("## ADDED Requirements"), refreshed);
        Assert.DoesNotContain("Fax", refreshed);
        Assert.DoesNotContain("RENAMED", refreshed);
        Assert.DoesNotContain("Telex", refreshed);
        Assert.Contains("## REMOVED Requirements", refreshed);
        Assert.Contains("### Requirement: Send money", refreshed);   // the main spec has it: the removal stays
        Assert.Equal(refreshed, WorkItems.BusinessRules.DeltaSpec.Refresh(refreshed, RemittancesMain));
        string crlf = WorkItems.BusinessRules.DeltaSpec.Refresh(delta.Replace("\n", "\r\n"), RemittancesMain);
        Assert.Equal(refreshed.Replace("\n", "\r\n"), crlf);
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

    [Fact(DisplayName = "An archived item's label can be edited")]
    public void EditArchivedLabel()
    {
        using TestRepo repo = new();
        OpenSpecStoreApi.Open(repo.Root);
        Assert.True(Item(124).Archived);
        (bool ok, string message) = WorkItemsApi.SetLabel(124, "bug");
        Assert.True(ok, message);
        Assert.Equal("bug", Item(124).Label);
    }

    [Fact(DisplayName = "An archived item's priority can be changed")]
    public void EditArchivedPriority()
    {
        using TestRepo repo = new();
        OpenSpecStoreApi.Open(repo.Root);
        (bool ok, string message) = WorkItemsApi.SetPriority(124, "high");
        Assert.True(ok, message);
        Assert.Equal("high", Item(124).Priority);
    }

    [Fact(DisplayName = "In Review is a valid status between Blocked and Completed")]
    public void InReviewStatus()
    {
        using TestRepo repo = new();
        OpenSpecStoreApi.Open(repo.Root);
        (bool ok, string _) = WorkItemsApi.SetStatus(123, "In Review");
        Assert.True(ok);
        Assert.Equal("In Review", Item(123).Status);
    }

    [Fact(DisplayName = "Archived items can be merged into a larger completed task")]
    public void MergeArchived()
    {
        using TestRepo repo = new();
        string dir = repo.PathOf("openspec/changes/archive/2026-09-20-old-auth");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, ".openspec.yaml"), "schema: spec-driven\ncreated: 2026-09-01\nx-rtm:\n  id: 200\n  title: Old auth cleanup\n  priority: high\n  contributors: [Tom]\n");
        File.WriteAllText(Path.Combine(dir, "proposal.md"), "# Proposal\n\nCleanup.\n");
        File.WriteAllText(Path.Combine(dir, "tasks.md"), "# Tasks\n\n## 1. Tasks\n\n- [x] 1.1 Remove old middleware\n");
        OpenSpecStoreApi.Open(repo.Root);

        (bool ok, string message, int id) = WorkItemsApi.Merge([124, 200]);

        Assert.True(ok, message);
        WorkItem merged = Item(id);
        Assert.True(merged.Archived);
        Assert.Contains("archive", merged.Path.Replace('\\', '/'));
        Assert.Contains("Idle sessions must expire.", WorkItemsApi.GetDescription(id));
        Assert.Contains("Cleanup", Notes.NotesApi.GetForWorkItem(id).Single(n => n.By == "Merge").Body);
    }

    // An archived change as the OpenSpec CLI leaves it: the delta specs stay in its folder, already merged into openspec/specs.
    private static void Archived(TestRepo repo, string folder, int id, string title, string requirement, string text)
    {
        string dir = repo.PathOf($"openspec/changes/archive/{folder}");
        Directory.CreateDirectory(Path.Combine(dir, "specs", "board"));
        File.WriteAllText(Path.Combine(dir, ".openspec.yaml"), $"schema: spec-driven\ncreated: 2026-09-01\nx-rtm:\n  id: {id}\n  title: {title}\n  contributors: [Tom]\n");
        File.WriteAllText(Path.Combine(dir, "proposal.md"), $"# Proposal\n\n{title} body.\n");
        File.WriteAllText(Path.Combine(dir, "tasks.md"), "# Tasks\n\n## 1. Tasks\n\n- [x] 1.1 Done step\n");
        File.WriteAllText(Path.Combine(dir, "specs", "board", "spec.md"),
            $"## MODIFIED Requirements\n\n### Requirement: {requirement}\n{text}\n\n#### Scenario: Works\n- **WHEN** a\n- **THEN** b\n");
    }

    [Fact(DisplayName = "Archived items that changed the same requirement can still be merged: their specs are already in openspec/specs")]
    public void MergeArchivedSameRequirement()
    {
        using TestRepo repo = new();
        Archived(repo, "2026-09-20-first", 201, "First", "Keyboard hints", "Ctrl+Alt toggles hints.");
        Archived(repo, "2026-09-21-second", 202, "Second", "Keyboard hints", "F toggles hints.");
        OpenSpecStoreApi.Open(repo.Root);

        (bool ok, string message, int id) = WorkItemsApi.Merge([201, 202]);

        Assert.True(ok, message);
        Assert.True(Item(id).Archived);
        string spec = repo.Read($"openspec/changes/archive/{Path.GetFileName(Item(id).Path)}/specs/board/spec.md");
        Assert.Contains("Ctrl+Alt toggles hints.", spec);   // nothing is dropped
        Assert.Contains("F toggles hints.", spec);
        Assert.Null(WorkItemsApi.Get(201));
    }

    [Fact(DisplayName = "Merging an archived item into an active one keeps its specs out of the active change, so archiving can't apply them twice")]
    public void MergeArchivedIntoActive()
    {
        using TestRepo repo = new();
        Archived(repo, "2026-09-20-old", 201, "Old", "Session timeout", "Sessions end after 30 minutes.");
        OpenSpecStoreApi.Open(repo.Root);
        string active = Item(107).Path;

        (bool ok, string message, int id) = WorkItemsApi.Merge([107, 201]);

        Assert.True(ok, message);
        WorkItem merged = Item(id);
        Assert.False(merged.Archived);
        string folder = $"openspec/changes/{Path.GetFileName(merged.Path)}";
        Assert.DoesNotContain("Sessions end after 30 minutes.", File.Exists(repo.PathOf($"{folder}/specs/board/spec.md")) ? repo.Read($"{folder}/specs/board/spec.md") : "");
        Assert.Contains("Sessions end after 30 minutes.", repo.Read($"{folder}/from-wi201/specs/board/spec.md"));
        Assert.Contains("Old body.", Notes.NotesApi.GetForWorkItem(id).Single(n => n.By == "Merge").Body);
    }

    [Fact(DisplayName = "Deleting a custom feature unlinks it from its work items, which become unassigned")]
    public void DeleteFeatureUnlinks()
    {
        using TestRepo repo = new();
        OpenSpecStoreApi.Open(repo.Root);
        Features.FeaturesApi.CreateFeature("Functional", "Payments");
        WorkItemsApi.MoveFeature(107, "functional/payments");
        Assert.Contains(Item(107).Features, f => f.Path == "functional/payments");

        (bool ok, string _) = WorkItemsApi.DeleteFeature("functional/payments");

        Assert.True(ok);
        Assert.DoesNotContain(Item(107).Features, f => f.Path == "functional/payments");
        Assert.DoesNotContain(Features.FeaturesApi.GetAll(), f => f.Path == "functional/payments");
    }

    /// <summary>What an agent writes when it creates or changes a work item: who and when, in x-rtm.</summary>
    internal static void StampAgent(TestRepo repo, string yaml = HealthYaml, string at = "2026-10-30T09:00:00") =>
        repo.Write(yaml, repo.Read(yaml).Replace("x-rtm:\n", $"x-rtm:\n  agentBy: Claude\n  agentAt: {at}\n"));

    [Fact(DisplayName = "Work an agent stamped is flagged until the user marks it reviewed, and flagged again when the agent changes it later")]
    public void AgentWorkUntilReviewed()
    {
        using TestRepo repo = new();
        StampAgent(repo);
        OpenSpecStoreApi.Open(repo.Root);
        Assert.Equal(("Claude", true), (Item(123).AgentBy, Item(123).AgentUnreviewed));
        Assert.False(Item(107).AgentUnreviewed);   // nobody stamped it

        Assert.True(WorkItemsApi.MarkReviewed(123).Success);

        Assert.False(Item(123).AgentUnreviewed);
        string yaml = repo.Read(HealthYaml);
        Assert.Contains("  reviewedAt: ", yaml);
        Assert.Contains("  agentBy: Claude\n", yaml);   // the agent's stamp stays; only the user's review is added

        repo.Write(HealthYaml, yaml.Replace("agentAt: 2026-10-30T09:00:00", "agentAt: 2999-01-01T00:00:00"));   // the agent edits it again
        OpenSpecStoreApi.MarkOwnChange(repo.PathOf(HealthYaml));
        Assert.True(Item(123).AgentUnreviewed);
        Assert.True(WorkItemsApi.MarkReviewed(123).Success);   // reviewing an agent whose clock is far ahead still sticks
        Assert.False(Item(123).AgentUnreviewed);
    }

    [Fact(DisplayName = "A subtask line ending in an agent comment is flagged; ticking keeps the stamp, editing its text or marking it reviewed removes it")]
    public void AgentSubtasks()
    {
        using TestRepo repo = new();
        repo.Write(HealthTasks, repo.Read(HealthTasks).TrimEnd() + "\n- [ ] 2.2 Agent drafted this <!-- agent: Claude -->\n- [ ] 2.3 And this <!-- agent: Claude -->\n");
        OpenSpecStoreApi.Open(repo.Root);
        Subtask s = Item(123).Subtasks.Single(x => x.Number == "2.2");
        Assert.Equal(("Agent drafted this", "Claude"), (s.Text, s.Agent));
        Assert.Null(Item(123).Subtasks.Single(x => x.Number == "2.1").Agent);

        WorkItemsApi.SetSubtaskDone(123, "2.2", true);
        Assert.Contains("- [x] 2.2 Agent drafted this <!-- agent: Claude -->", repo.Read(HealthTasks));

        WorkItemsApi.MarkSubtaskReviewed(123, "2.2");
        Assert.Contains("- [x] 2.2 Agent drafted this\n", repo.Read(HealthTasks));
        Assert.Null(Item(123).Subtasks.Single(x => x.Number == "2.2").Agent);

        WorkItemsApi.SetSubtaskText(123, "2.3", "Rewritten by me");
        Assert.Contains("- [ ] 2.3 Rewritten by me\n", repo.Read(HealthTasks));
        Assert.DoesNotContain("<!-- agent", repo.Read(HealthTasks));
    }

    [Fact(DisplayName = "Deleting a feature that open changes still spec deletes their delta folders too, keeps the changes valid, and leaves archived changes alone")]
    public void DeleteFeatureWithOpenSpecDeletesDeltas()
    {
        using TestRepo repo = new();
        OpenSpecStoreApi.Open(repo.Root);
        Assert.Equal([123], WorkItemsApi.FeatureSpecOwners("non-functional/availability"));
        Assert.Empty(WorkItemsApi.FeatureSpecOwners("non-functional/security"));   // only an archived change specs it

        (bool ok, string message) = WorkItemsApi.DeleteFeature("non-functional/availability");

        Assert.True(ok, message);
        Assert.False(Directory.Exists(repo.PathOf("openspec/changes/add-health-check/specs/non-functional/availability")));
        Assert.False(Directory.Exists(repo.PathOf("openspec/changes/add-health-check/specs")));
        Assert.Contains("skip_specs: true", repo.Read(HealthYaml));   // no deltas left: OpenSpec needs the flag
        Assert.DoesNotContain(Item(123).Features, f => f.Path == "non-functional/availability");
        Assert.DoesNotContain(Features.FeaturesApi.GetAll(), f => f.Path == "non-functional/availability");

        (bool ok2, string message2) = WorkItemsApi.DeleteFeature("non-functional/security");
        Assert.True(ok2, message2);
        Assert.True(Directory.Exists(repo.PathOf("openspec/changes/archive/2026-09-18-session-timeout/specs/non-functional/security")));
    }

    [Fact(DisplayName = "Commit authors fill in contributors until the list is edited in the app; after that the saved list wins")]
    public void ContributorsFromGit()
    {
        using TestRepo repo = new();
        OpenSpecStoreApi.Open(repo.Root);
        string token = "t1";
        IReadOnlyList<string> authors = ["Alice"];
        try
        {
            WorkItemsApi.ConfigureContributors(() => (token, _ => authors));
            Assert.Equal(["Alice"], Item(125).Contributors);            // plain-change: nothing saved, so git authors
            Assert.Null(Item(125).Assigned);
            Assert.Equal(["Artyom", "Tom"], Item(123).Contributors);    // saved x-rtm list, Alice not added

            authors = ["Bob", "Alice"];
            token = "t2";   // a new commit
            Assert.Equal(["Bob", "Alice"], Item(125).Contributors);
        }
        finally { WorkItemsApi.ConfigureContributors(null); }

        Assert.Empty(Item(125).Contributors);   // outside git nobody
    }

    [Fact(DisplayName = "Removing every contributor leaves nobody, even when someone has commits on the change")]
    public void RemoveEveryone()
    {
        using TestRepo repo = new();
        OpenSpecStoreApi.Open(repo.Root);
        try
        {
            WorkItemsApi.ConfigureContributors(() => ("t1", _ => ["Alice"]));

            Assert.True(WorkItemsApi.SetContributors(125, []).Success);

            Assert.Empty(Item(125).Contributors);
            Assert.Contains("contributors: []", repo.Read("openspec/changes/plain-change/.openspec.yaml"));
        }
        finally { WorkItemsApi.ConfigureContributors(null); }
    }

    [Fact(DisplayName = "A newcomer with no commits yet can assign themselves")]
    public void NewcomerAssignsThemselves()
    {
        using TestRepo repo = new();
        OpenSpecStoreApi.Open(repo.Root);
        try
        {
            WorkItemsApi.ConfigureContributors(() => ("t1", _ => []));   // fresh clone: nobody has committed to this change

            WorkItemsApi.SetContributors(107, [.. Item(107).Assigned!, "Newcomer"]);

            Assert.Contains("Newcomer", Item(107).Contributors);
        }
        finally { WorkItemsApi.ConfigureContributors(null); }
    }

    [Fact(DisplayName = "Commits and tests linked by hand are saved in x-rtm, each list on one line")]
    public void LinkByHand()
    {
        using TestRepo repo = new();
        OpenSpecStoreApi.Open(repo.Root);

        Assert.True(WorkItemsApi.SetLinkedCommits(123, ["a1b2c3d"]).Success);
        Assert.True(WorkItemsApi.SetLinkedTests(123, [new TestLink("suite 543", "ping test", 123)]).Success);

        Assert.Equal(["a1b2c3d"], Item(123).Commits);
        Assert.Equal([new TestLink("suite 543", "ping test", 123)], Item(123).Tests);
        string yaml = repo.Read(HealthYaml);
        Assert.Contains("  commits: [a1b2c3d]\n", yaml);
        Assert.Contains("  tests: [{suite: suite 543, test: ping test, note: 123}]\n", yaml);
    }

    [Fact(DisplayName = "A work item records when it last moved to its status: creation stamps x-rtm.moved, every status change restamps it, and an unchanged status leaves it")]
    public void MovedStamp()
    {
        using TestRepo repo = new();
        OpenSpecStoreApi.Open(repo.Root);
        DateOnly today = DateOnly.FromDateTime(DateTime.Today);
        int id = WorkItemsApi.Create("Stamp me", "Planned").Id;
        string YamlPath() => Path.Combine(Item(id).Path, ".openspec.yaml");
        void Backdate() => OpenSpecStoreApi.WriteText(YamlPath(), OpenSpecStoreApi.ReadText(YamlPath()).Replace($"moved: {today:yyyy-MM-dd}", "moved: 2026-01-02"));

        Assert.Equal(today, Item(id).Moved);
        Assert.Contains($"moved: {today:yyyy-MM-dd}", File.ReadAllText(YamlPath()));

        Backdate();
        Assert.Equal(new DateOnly(2026, 1, 2), Item(id).Moved);
        Assert.True(WorkItemsApi.SetStatus(id, "Planned").Success);   // same status: nothing moves
        Assert.Equal(new DateOnly(2026, 1, 2), Item(id).Moved);

        Assert.True(WorkItemsApi.SetStatus(id, "In Review").Success);
        Assert.Equal(today, Item(id).Moved);
        Assert.Contains($"moved: {today:yyyy-MM-dd}", File.ReadAllText(YamlPath()));
    }

    [Fact(DisplayName = "Merging heads each source's task group with its number, title and merge date, and stamps the notes it moves in with the source number and merge date")]
    public void MergeHeadsGroupsAndStampsNotes()
    {
        using TestRepo repo = new();
        OpenSpecStoreApi.Open(repo.Root);
        (bool _, string _, int own) = Notes.NotesApi.Create(107, "Tom");   // the main item's own note
        string today = DateTime.Now.ToString("yyyy-MM-dd");
        string original = repo.Read("openspec/changes/add-health-check/notes/n123.md");

        (bool ok, string message, int id) = WorkItemsApi.Merge([107, 123, 125], 107);

        Assert.True(ok, message);
        string folder = $"openspec/changes/{Path.GetFileName(Item(id).Path)}";
        string tasks = repo.Read($"{folder}/tasks.md");
        Assert.Contains("## 1. Monthly remittance export", tasks);   // the main item keeps its heading
        Assert.Contains($"## 2. #wi123 Health-check endpoint · Endpoint · merged {today}", tasks);
        Assert.Contains($"## 3. #wi123 Health-check endpoint · Hardening · merged {today}", tasks);
        Assert.Matches($@"## 4\. #wi125 .+ · merged {today}\r?\n", tasks);   // one group: no group name

        string moved = repo.Read($"{folder}/notes/n123.md");
        Assert.Equal(original.Replace("created: 2026-10-02T09:14:00\n", $"created: 2026-10-02T09:14:00\nmergedFrom: 123\nmergedAt: {today}\n"), moved);   // every other key as it was
        Notes.PublicDTOs.Note source = Notes.NotesApi.Get(123)!;
        Assert.Equal(123, source.MergedFrom);
        Assert.Equal(DateTime.Parse(today), source.MergedAt);
        IReadOnlyList<Notes.PublicDTOs.Note> described = [.. Notes.NotesApi.GetForWorkItem(id).Where(n => n.By == "Merge")];
        Assert.Equal([123, 125], described.Select(n => n.MergedFrom!.Value).Order());
        Assert.All(described, n => Assert.Equal(DateTime.Parse(today), n.MergedAt));
        Assert.Null(Notes.NotesApi.Get(own)!.MergedFrom);
        Assert.Null(Notes.NotesApi.Get(own)!.MergedAt);
    }

    [Fact(DisplayName = "A work item's revision changes when any of its files changes, and stays the same when nothing changed")]
    public void RevisionTracksFiles()
    {
        using TestRepo repo = new();
        OpenSpecStoreApi.Open(repo.Root);

        string first = Item(123).Revision;
        Assert.Equal(first, Item(123).Revision);
        Assert.Matches("^[0-9a-f]{12}$", first);

        WorkItemsApi.SetTitle(123, "A different title");
        string afterTitle = Item(123).Revision;
        Assert.NotEqual(first, afterTitle);

        OpenSpecStoreApi.WriteText(repo.PathOf("openspec/changes/add-health-check/notes/n999.md"), "# a note\n");
        Assert.NotEqual(afterTitle, Item(123).Revision);
    }

    [Fact(DisplayName = "A write guarded by a stale revision is refused with the current revision in the message")]
    public void StaleRevisionIsRefused()
    {
        using TestRepo repo = new();
        OpenSpecStoreApi.Open(repo.Root);

        (bool success, string message) = WorkItemsApi.CheckRevision(123, "000000000000");

        Assert.False(success);
        Assert.Contains(Item(123).Revision, message);
        Assert.False(WorkItemsApi.CheckRevision(999999, "000000000000").Success);
    }

    [Fact(DisplayName = "A matching or absent revision passes the guard")]
    public void MatchingRevisionPasses()
    {
        using TestRepo repo = new();
        OpenSpecStoreApi.Open(repo.Root);

        Assert.True(WorkItemsApi.CheckRevision(123, Item(123).Revision).Success);
        Assert.True(WorkItemsApi.CheckRevision(123, null).Success);
        Assert.True(WorkItemsApi.CheckRevision(123, "").Success);
    }

    private const string HealthEvents = "openspec/changes/add-health-check/events.log";

    private static IEnumerable<(string Kind, string Text)> Events(int id) => WorkItemsApi.GetEvents(id).Select(e => (e.Kind, e.Text));

    [Fact(DisplayName = "Reading the board while a merge or archive removes a change folder skips the folder that is gone instead of failing")]
    public void ReadWhileAFolderVanishes()
    {
        using TestRepo repo = new();
        OpenSpecStoreApi.Open(repo.Root);
        string folder = repo.PathOf("openspec/changes/vanishing");
        void Make()
        {
            Directory.CreateDirectory(Path.Combine(folder, "notes"));
            File.WriteAllText(Path.Combine(folder, ".openspec.yaml"), "schema: spec-driven\ncreated: 2026-11-10\nx-rtm:\n  id: 900\n  title: Vanishing\n");
            File.WriteAllText(Path.Combine(folder, "proposal.md"), "# Vanishing\n");
            for (int i = 0; i < 40; i++) File.WriteAllText(Path.Combine(folder, "notes", $"n{9000 + i}.md"), "---\nid: 1\n---\n# x\n");
        }

        // another thread makes and removes the folder over and over, as a merge (or the user in Explorer) does, while the app reads
        using CancellationTokenSource stop = new(TimeSpan.FromSeconds(3));
        Task churn = Task.Run(() =>
        {
            while (!stop.IsCancellationRequested)
                try { Make(); Directory.Delete(folder, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        });
        List<Exception> failures = [];
        while (!stop.IsCancellationRequested)
            try { OpenSpecStoreApi.Refresh(); WorkItemsApi.GetAll(includeHidden: true); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { failures.Add(e); }
        churn.Wait();

        Assert.Empty(failures.Select(e => e.GetType().Name + " at " + string.Join(" < ", (e.StackTrace ?? "").Split('\n').Select(l => l.Trim()).Where(l => l.StartsWith("at WorkItems") || l.StartsWith("at OpenSpecStore")).Take(3))).Distinct());
        Assert.Contains(WorkItemsApi.GetAll(), w => w.Id == 123);   // the rest of the board is read as usual
    }

    [Fact(DisplayName = "Changing an item's status is recorded as an event with the old and the new status, who did it and when")]
    public void StatusIsRecorded()
    {
        using TestRepo repo = new();
        OpenSpecStoreApi.Open(repo.Root);
        WorkItemsApi.ConfigureActor(() => "Tester");
        WorkItemsApi.ConfigureArchiver(folder => { Directory.Move(repo.PathOf($"openspec/changes/{folder}"), repo.PathOf($"openspec/changes/archive/2026-10-23-{folder}")); return (true, ""); });
        try
        {
            DateTime from = DateTime.Now.AddSeconds(-2);
            Assert.True(WorkItemsApi.SetStatus(123, "In Review").Success);

            WorkItemEvent e = Assert.Single(WorkItemsApi.GetEvents(123));
            Assert.Equal(("Tester", "status", "In Progress -> In Review"), (e.By, e.Kind, e.Text));
            Assert.InRange(e.At, from, DateTime.Now.AddSeconds(2));
            Assert.Matches(@"^\d{4}-\d\d-\d\dT\d\d:\d\d:\d\d \| Tester \| status \| In Progress -> In Review\r?\n$", repo.Read(HealthEvents));

            Assert.True(WorkItemsApi.SetStatus(123, "In Review").Success);   // nothing changed, nothing recorded
            Assert.Single(WorkItemsApi.GetEvents(123));
            Assert.Empty(WorkItemsApi.GetEvents(999));
            Assert.Empty(WorkItemsApi.GetEvents(107));

            Assert.True(WorkItemsApi.SetStatus(107, "Completed").Success);   // the event goes with the folder into the archive
            Assert.Equal([("status", "Planned -> Completed")], Events(107));
            Assert.True(File.Exists(repo.PathOf("openspec/changes/archive/2026-10-23-export-remittances/events.log")));
        }
        finally { WorkItemsApi.ConfigureActor(null); WorkItemsApi.ConfigureArchiver(null); }
    }

    [Fact(DisplayName = "Linking and unlinking a commit, a test or a document records one event each, and nothing when the list did not change")]
    public void LinkingIsRecorded()
    {
        using TestRepo repo = new();
        OpenSpecStoreApi.Open(repo.Root);
        WorkItemsApi.ConfigureActor(() => "Tester");
        try
        {
            Assert.True(WorkItemsApi.SetLinkedCommits(123, ["abc1234", "def5678", "abc1234"]).Success);
            Assert.True(WorkItemsApi.SetLinkedCommits(123, ["abc1234", "def5678"]).Success);   // same list
            Assert.True(WorkItemsApi.SetLinkedCommits(123, ["abc1234"]).Success);
            Assert.Equal([("commit-linked", "abc1234"), ("commit-linked", "def5678"), ("commit-unlinked", "def5678")], Events(123));

            Assert.True(WorkItemsApi.SetLinkedTests(123, [new TestLink("PingTests", "Pings", null)]).Success);
            Assert.True(WorkItemsApi.SetLinkedTests(123, [new TestLink("PingTests", "Pings", 7)]).Success);   // only its note changed
            Assert.True(WorkItemsApi.SetLinkedTests(123, []).Success);

            Assert.True(WorkItemsApi.SetLinkedDocs(123, ["docs/guide.md"]).Success);
            Assert.True(WorkItemsApi.SetLinkedDocs(123, ["docs/guide.md"]).Success);
            Assert.True(WorkItemsApi.SetLinkedDocs(123, []).Success);

            Assert.Equal(
                [("commit-linked", "abc1234"), ("commit-linked", "def5678"), ("commit-unlinked", "def5678"),
                 ("test-linked", "PingTests / Pings"), ("test-unlinked", "PingTests / Pings"),
                 ("doc-linked", "docs/guide.md"), ("doc-unlinked", "docs/guide.md")],
                Events(123));
            Assert.All(WorkItemsApi.GetEvents(123), e => Assert.Equal("Tester", e.By));
        }
        finally { WorkItemsApi.ConfigureActor(null); }
    }

    [Fact(DisplayName = "Ticking, reopening and adding a subtask and adding or removing a dependency are recorded as events")]
    public void SubtasksAndDependenciesAreRecorded()
    {
        using TestRepo repo = new();
        OpenSpecStoreApi.Open(repo.Root);
        WorkItemsApi.ConfigureActor(() => "Tester");
        try
        {
            Assert.True(WorkItemsApi.SetSubtaskDone(123, "2.1", true).Success);
            Assert.True(WorkItemsApi.SetSubtaskDone(123, "2.1", true).Success);   // already done
            Assert.True(WorkItemsApi.SetSubtaskDone(123, "1.1", false).Success);
            Assert.True(WorkItemsApi.AddSubtask(123, "Write the docs").Success);
            Assert.Equal(
                [("subtask-done", "2.1 Hide internals from anonymous callers"), ("subtask-reopened", "1.1 Add /health route"), ("subtask-added", "2.2 Write the docs")],
                Events(123));

            Assert.True(WorkItemsApi.AddDependency(123, 107).Success);   // 107 is not resolved: 123 becomes Blocked
            Assert.True(WorkItemsApi.RemoveDependency(123, 107).Success);
            Assert.True(WorkItemsApi.SetDependencies(123, [107, 125]).Success);
            Assert.True(WorkItemsApi.SetDependencies(123, [125]).Success);
            Assert.Equal(
                [("dependency-added", "#wi107"), ("status", "In Progress -> Blocked"), ("dependency-removed", "#wi107"),
                 ("dependency-added", "#wi107"), ("dependency-added", "#wi125"), ("dependency-removed", "#wi107")],
                Events(123).Skip(3));
        }
        finally { WorkItemsApi.ConfigureActor(null); }
    }

    [Fact(DisplayName = "A new work item starts its event log with created, and a merged item keeps the events of the items it was made from")]
    public void CreatedAndMergedEvents()
    {
        using TestRepo repo = new();
        OpenSpecStoreApi.Open(repo.Root);
        WorkItemsApi.ConfigureActor(() => "Tester");
        try
        {
            (bool ok, string message, int fresh) = WorkItemsApi.Create("Brand new", "Backlog", null, null);
            Assert.True(ok, message);
            Assert.Equal([("created", "Brand new")], Events(fresh));
            Assert.Equal("Tester", WorkItemsApi.GetEvents(fresh)[0].By);

            repo.Write("openspec/changes/export-remittances/events.log", "2026-02-01T10:00:00 | Bob | status | Backlog -> Planned\n");
            repo.Write(HealthEvents, "2026-01-01T10:00:00 | Ann | created | Health-check endpoint\n");
            (ok, message, int merged) = WorkItemsApi.Merge([107, 123]);
            Assert.True(ok, message);

            IReadOnlyList<WorkItemEvent> events = WorkItemsApi.GetEvents(merged);
            Assert.Equal([("Ann", "created"), ("Bob", "status"), ("Tester", "created")], events.Select(e => (e.By, e.Kind)));   // sorted by time, then its own created
            Assert.Equal(WorkItemsApi.Get(merged)!.Title, events[^1].Text);
            Assert.False(Directory.Exists(Path.Combine(WorkItemsApi.Get(merged)!.Path, "from-wi107")));   // the log is carried over, not copied as a stray file
        }
        finally { WorkItemsApi.ConfigureActor(null); }
    }

    [Fact(DisplayName = "Linked documents are stored as repo-relative paths in x-rtm.docs; a path outside the repository is refused")]
    public void LinkedDocuments()
    {
        using TestRepo repo = new();
        OpenSpecStoreApi.Open(repo.Root);

        Assert.True(WorkItemsApi.SetLinkedDocs(123, [@"docs\guide.md ", "docs/guide.md", "DevResources/plans/p.md", " "]).Success);

        Assert.Equal(["docs/guide.md", "DevResources/plans/p.md"], Item(123).Docs);
        Assert.Contains("  docs: [docs/guide.md, DevResources/plans/p.md]", repo.Read(HealthYaml));
        foreach (string bad in new[] { "/etc/passwd", @"C:\x.md", "../x.md", "a/../b.md" })
            Assert.False(WorkItemsApi.SetLinkedDocs(123, [bad]).Success, bad);
        Assert.Equal(["docs/guide.md", "DevResources/plans/p.md"], Item(123).Docs);
        Assert.False(WorkItemsApi.SetLinkedDocs(999, ["a.md"]).Success);
        Assert.True(WorkItemsApi.SetLinkedDocs(123, []).Success);
        Assert.Empty(Item(123).Docs!);
        Assert.DoesNotContain("docs:", repo.Read(HealthYaml));
    }

    [Fact(DisplayName = "An events.log line that cannot be read is skipped and kept in the file when the next event is appended")]
    public void UnreadableLineIsKept()
    {
        using TestRepo repo = new();
        OpenSpecStoreApi.Open(repo.Root);
        WorkItemsApi.ConfigureActor(() => "Tester");
        try
        {
            repo.Write(HealthEvents, "this is not an event\n2026-10-01T10:00:00 | Ann | commit-linked | a | b\r\nnot-a-date | Ann | created | x\n2026-10-01T11:00:00 | Ann | created | Last line without a newline");

            Assert.Equal([("commit-linked", "a | b"), ("created", "Last line without a newline")], Events(123));   // the text may hold the separator

            Assert.True(WorkItemsApi.SetStatus(123, "In Review").Success);

            string[] lines = repo.Read(HealthEvents).Replace("\r", "").TrimEnd('\n').Split('\n');
            Assert.Equal(["this is not an event", "2026-10-01T10:00:00 | Ann | commit-linked | a | b", "not-a-date | Ann | created | x", "2026-10-01T11:00:00 | Ann | created | Last line without a newline"], lines[..4]);
            Assert.EndsWith(" | Tester | status | In Progress -> In Review", lines[4]);
            Assert.Equal(5, lines.Length);
            Assert.Equal(3, WorkItemsApi.GetEvents(123).Count);
        }
        finally { WorkItemsApi.ConfigureActor(null); }
    }

    [Fact(DisplayName = "Without a configured actor an event is recorded as someone's, and a failing event write never fails the command")]
    public void ActorAndFailingWrite()
    {
        using TestRepo repo = new();
        OpenSpecStoreApi.Open(repo.Root);
        try
        {
            WorkItemsApi.ConfigureActor(null);
            Assert.True(WorkItemsApi.SetStatus(123, "In Review").Success);
            WorkItemsApi.ConfigureActor(() => "  ");
            Assert.True(WorkItemsApi.SetStatus(123, "In Progress").Success);
            WorkItemsApi.ConfigureActor(() => " Claude ");
            Assert.True(WorkItemsApi.LogEvent(123, "custom", "two\nlines").Success);
            Assert.False(WorkItemsApi.LogEvent(123, " ", "x").Success);
            Assert.False(WorkItemsApi.LogEvent(999, "custom", "x").Success);
            Assert.Equal(["someone", "someone", "Claude"], WorkItemsApi.GetEvents(123).Select(e => e.By));
            Assert.Equal(("custom", "two lines"), (WorkItemsApi.GetEvents(123)[2].Kind, WorkItemsApi.GetEvents(123)[2].Text));

            File.Delete(repo.PathOf(HealthEvents));
            Directory.CreateDirectory(repo.PathOf(HealthEvents));   // a folder where the log should be: no event can be written
            (bool ok, string message) = WorkItemsApi.SetStatus(123, "In Review");
            Assert.True(ok, message);
            Assert.Equal("In Review", Item(123).Status);
            Assert.Empty(WorkItemsApi.GetEvents(123));
            Assert.False(WorkItemsApi.LogEvent(123, "custom", "x").Success);
        }
        finally { WorkItemsApi.ConfigureActor(null); }
    }

    [Fact(DisplayName = "A metadata write to an item whose yaml does not parse is refused with the warning, and the file is left as it is")]
    public void WriteToUnparsableYamlIsRefused()
    {
        using TestRepo repo = new();
        const string broken = "schema: spec-driven\r\nx-rtm:\r\n  id: 130\r\n  features: [unclosed\r\n  title: Broken one\r\n";
        repo.Write("openspec/changes/broken-one/.openspec.yaml", broken);
        OpenSpecStoreApi.Open(repo.Root);
        WorkItem it = WorkItemsApi.GetAll(includeHidden: true).Single(w => w.Folder == "broken-one");

        (bool ok, string message) = WorkItemsApi.SetPriority(it.Id, "high");

        Assert.False(ok);
        Assert.Equal(it.Warning, message);
        Assert.Equal(broken, repo.Read("openspec/changes/broken-one/.openspec.yaml"));
        Assert.False(WorkItemsApi.SetStatus(it.Id, "In Progress").Success);
        Assert.Equal(broken, repo.Read("openspec/changes/broken-one/.openspec.yaml"));
        Assert.Empty(WorkItemsApi.ResolveDuplicateIds());   // the startup scan never writes it either
        Assert.Equal(broken, repo.Read("openspec/changes/broken-one/.openspec.yaml"));
    }

    [Fact(DisplayName = "Every value the app writes into the x-rtm block reads back identical, also with colons, quotes, hashes, brackets and words YAML would take for booleans or numbers")]
    public void EveryWrittenValueReadsBackIdentical()
    {
        using TestRepo repo = new();
        OpenSpecStoreApi.Open(repo.Root);
        foreach (string v in YamlChecks.Nasty)
        {
            string t = v.Trim(), because = $"(value: {v})";
            Assert.True(WorkItemsApi.SetTitle(123, v).Success, because);
            Assert.True(WorkItemsApi.SetPriority(123, v).Success, because);
            Assert.True(WorkItemsApi.SetLabel(123, v).Success, because);
            Assert.True(WorkItemsApi.SetContributors(123, [v, "Tom"]).Success, because);
            Assert.True(WorkItemsApi.MoveFeature(123, v).Success, because);
            Assert.True(WorkItemsApi.SetLinkedCommits(123, [v]).Success, because);
            Assert.True(WorkItemsApi.SetLinkedTests(123, [new TestLink(v, v, null)]).Success, because);
            Assert.True(WorkItemsApi.StampAgent(123, v).Success, because);
            string path = v.Replace('\\', '/').Trim();
            bool pathy = !Path.IsPathRooted(path) && !path.StartsWith('/') && !path.Contains("..") && !(path.Length > 1 && path[1] == ':');
            if (pathy)
            {
                Assert.True(WorkItemsApi.SetPlan(123, v).Success, because);
                Assert.True(WorkItemsApi.SetLinkedDocs(123, [v, "docs/a.md"]).Success, because);
            }
            Assert.True(WorkItemsApi.SetLane(123, "In Progress", v).Success, because);

            YamlChecks.Valid(repo.Read(HealthYaml), YamlChecks.Nasty, because);
            OpenSpecStoreApi.Open(repo.Root);   // from the files, not from the cache
            WorkItem it = Item(123);
            Assert.Equal(t, it.Title);
            Assert.Equal(v, it.Priority);
            Assert.Equal(v, it.Label);
            Assert.Equal([v, "Tom"], it.Assigned);
            Assert.Equal(v, Assert.Single(it.Features).Path);
            Assert.Equal([v], it.Commits);
            Assert.Equal([new TestLink(v, v, null)], it.Tests);
            Assert.Equal(v, it.AgentBy);
            if (pathy) { Assert.Equal(path, it.Plan); Assert.Equal([path, "docs/a.md"], it.Docs); }
            Assert.Equal(t, it.Lanes["In Progress"]);
            Assert.Contains(t, WorkItemsApi.GetLanes()["In Progress"]);
            Assert.Null(it.Warning);
        }
        YamlChecks.Valid(repo.Read("openspec/config.yaml"), YamlChecks.Nasty);

        // words a slug can leave a feature's name as: the config.yaml list of declared features must not turn them into booleans or numbers
        string[] slugs = ["yes", "null", "123", "on", "1e5"];
        foreach (string name in new[] { "yes", "null", "123", "On", "1e5" })
        {
            (bool ok, string message, Features.PublicDTOs.Feature? _) = Features.FeaturesApi.CreateFeature(Features.FeaturesApi.Ungrouped, name);
            Assert.True(ok, message);
        }
        YamlChecks.Valid(repo.Read("openspec/config.yaml"), slugs);
        OpenSpecStoreApi.Open(repo.Root);
        Assert.All(slugs, p => Assert.Contains(Features.FeaturesApi.GetAll(), f => f.Path == p));
    }

    [Fact(DisplayName = "An empty string, a blank and a multi-line text in the x-rtm block survive a write and a read")]
    public void EmptyAndMultiLineValuesSurvive()
    {
        using TestRepo repo = new();
        string yaml = repo.PathOf("openspec/changes/add-health-check/.openspec.yaml");
        WorkItems.Infrastructure.Rtm before = new() { Id = 123, Title = "Line one\nLine two: with a colon", Plan = "", Label = "   ", Contributors = ["", "Tom", " lead "], Commits = ["abc1234"] };

        Assert.True(WorkItems.Infrastructure.Metadata.Write(yaml, before).Success);

        WorkItems.Infrastructure.Rtm after = WorkItems.Infrastructure.Metadata.Read<WorkItems.Infrastructure.Rtm>(yaml);
        Assert.Equal((before.Title, before.Label), (after.Title, after.Label));
        Assert.Null(after.Plan);   // an empty text is left out, as null is: no plan
        Assert.Equal(before.Contributors, after.Contributors);
        YamlChecks.Valid(File.ReadAllText(yaml), ["", "abc1234"]);
    }
}
