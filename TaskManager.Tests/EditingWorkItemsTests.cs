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

    [Fact(DisplayName = "A feature that comes from specs/ can be unlinked: it leaves the item, the spec stays, and it can be linked again")]
    public void UnlinkSpecFeature()
    {
        using TestRepo repo = new();
        OpenSpecStoreApi.Open(repo.Root);
        string spec = "openspec/changes/add-health-check/specs/non-functional/availability/spec.md";
        string before = repo.Read(spec);

        Assert.True(WorkItemsApi.UnlinkFeature(123, "non-functional/availability").Success);

        Assert.Equal([new FeatureLink("non-functional/security", false)], Item(123).Features);   // the manual one stays
        Assert.Equal(["non-functional/availability"], Item(123).UnlinkedFeatures);
        Assert.Contains("unlinkedFeatures: [non-functional/availability]", repo.Read(HealthYaml));
        Assert.Equal(before, repo.Read(spec));

        Assert.True(WorkItemsApi.RelinkFeature(123, "non-functional/availability").Success);

        Assert.Equal([new FeatureLink("non-functional/availability", true), new FeatureLink("non-functional/security", false)], Item(123).Features);
        Assert.Empty(Item(123).UnlinkedFeatures!);
        Assert.DoesNotContain("unlinkedFeatures", repo.Read(HealthYaml));
    }

    [Fact(DisplayName = "Only a linked feature from specs/ can be unlinked")]
    public void UnlinkRefusals()
    {
        using TestRepo repo = new();
        OpenSpecStoreApi.Open(repo.Root);
        WorkItemsApi.SetManualFeatures(123, ["functional/banking"]);

        Assert.False(WorkItemsApi.UnlinkFeature(123, "functional/banking").Success);   // manual ones are removed with SetManualFeatures
        Assert.False(WorkItemsApi.UnlinkFeature(123, "functional/nothing").Success);
        Assert.Equal(2, Item(123).Features.Count);
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

    [Fact(DisplayName = "After a change folder is archived, a new work item with the same title can take its old folder name")]
    public void FolderNameReusedAfterArchive()
    {
        using TestRepo repo = new();
        OpenSpecStoreApi.Open(repo.Root);
        WorkItemsApi.ConfigureArchiver(folder => { Directory.Move(repo.PathOf($"openspec/changes/{folder}"), repo.PathOf($"openspec/changes/archive/2026-10-23-{folder}")); return (true, ""); });
        try
        {
            (bool _, string _, int first) = WorkItemsApi.Create("New work item", "Backlog", null, null);
            Assert.True(WorkItemsApi.SetStatus(first, "Completed").Success);
            Assert.True(Directory.Exists(repo.PathOf("openspec/changes/archive/2026-10-23-new-work-item")));

            (bool ok, string message, int second) = WorkItemsApi.Create("New work item", "Backlog", null, null);

            Assert.True(ok, message);
            Assert.Equal(repo.PathOf("openspec/changes/new-work-item"), Item(second).Path);
            Assert.Contains($"id: {second}", repo.Read("openspec/changes/new-work-item/.openspec.yaml"));
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
        Assert.Equal(["functional/banking", "functional/remittances"], merged.Features.Select(f => f.Path).Order());
        Assert.Contains("Finance needs a monthly export.", WorkItemsApi.GetDescription(id));
        Assert.Contains("Created with the openspec CLI only.", WorkItemsApi.GetDescription(id));
    }

    [Fact(DisplayName = "Merging keeps every note (the change log) of the sources, under their own numbers")]
    public void MergeKeepsNotes()
    {
        using TestRepo repo = new();
        OpenSpecStoreApi.Open(repo.Root);
        (bool _, string _, int extra) = Notes.NotesApi.Create(107, "Tom");

        (bool ok, string message, int id) = WorkItemsApi.Merge([107, 123]);

        Assert.True(ok, message);
        Assert.Equal([123, extra], Notes.NotesApi.GetForWorkItem(id).Select(n => n.Id).Order());
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
        Assert.Contains("## 2. Health-check endpoint · Endpoint", tasks);
        Assert.Contains("## 3. Health-check endpoint · Hardening", tasks);
        Assert.Contains("- [ ] 3.1 Hide internals from anonymous callers", tasks);
        Assert.Equal(["1. Monthly remittance export", "2. Health-check endpoint · Endpoint", "3. Health-check endpoint · Hardening"], Item(id).Subtasks.Select(s => s.Group).Distinct());
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
        Assert.Contains("Session timeout", WorkItemsApi.GetDescription(id));
        Assert.Contains("Cleanup", WorkItemsApi.GetDescription(id));
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
        Assert.Contains("Old body.", WorkItemsApi.GetDescription(id));
    }

    [Fact(DisplayName = "Deleting a custom feature unlinks it from its work items, which become unassigned")]
    public void DeleteFeatureUnlinks()
    {
        using TestRepo repo = new();
        OpenSpecStoreApi.Open(repo.Root);
        Features.FeaturesApi.CreateFeature("Functional", "Payments");
        WorkItemsApi.SetManualFeatures(107, ["functional/payments"]);
        Assert.Contains(Item(107).Features, f => f.Path == "functional/payments");

        (bool ok, string _) = WorkItemsApi.DeleteFeature("functional/payments");

        Assert.True(ok);
        Assert.DoesNotContain(Item(107).Features, f => f.Path == "functional/payments");
        Assert.DoesNotContain(Features.FeaturesApi.GetAll(), f => f.Path == "functional/payments");
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
}
