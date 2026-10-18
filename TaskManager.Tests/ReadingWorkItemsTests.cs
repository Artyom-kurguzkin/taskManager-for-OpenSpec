using OpenSpecStore;
using WorkItems;
using WorkItems.PublicDTOs;

namespace TaskManager.Tests;

[Collection(nameof(ModuleStateCollection))]
public sealed class ReadingWorkItemsTests
{
    private static WorkItem Item(int id) => WorkItemsApi.Get(id) ?? throw new Xunit.Sdk.XunitException($"#wi{id} not found");

    [Fact(DisplayName = "An active change shows as a work item with the metadata from its x-rtm block")]
    public void ActiveChange()
    {
        using TestRepo repo = new();
        OpenSpecStoreApi.Open(repo.Root);

        WorkItem it = Item(123);

        Assert.Equal("Health-check endpoint", it.Title);
        Assert.Equal("In Progress", it.Status);
        Assert.Equal("high", it.Priority);
        Assert.Equal(["Artyom", "Tom"], it.Contributors);
        Assert.Equal("add-health-check", it.Folder);
        Assert.False(it.Archived);
    }

    [Fact(DisplayName = "An archived change shows as Completed")]
    public void ArchivedChange()
    {
        using TestRepo repo = new();
        OpenSpecStoreApi.Open(repo.Root);
        WorkItem it = Item(124);
        Assert.Equal("Completed", it.Status);
        Assert.True(it.Archived);
    }

    [Fact(DisplayName = "A change created outside the app shows as Backlog with lower priority and a provisional number")]
    public void ChangeWithoutMetadata()
    {
        using TestRepo repo = new();
        OpenSpecStoreApi.Open(repo.Root);

        WorkItem it = Assert.Single(WorkItemsApi.GetAll(), w => w.Folder == "plain-change");

        Assert.Equal("Backlog", it.Status);
        Assert.Equal("low", it.Priority);
        Assert.Empty(it.Contributors);
        Assert.Equal(125, it.Id);                  // one more than the highest stored id (124, archived)
        Assert.Equal("Plain change", it.Title);    // "# Proposal" is the template heading, so the folder name is used
    }

    [Fact(DisplayName = "A stored Completed status on an active change shows as Blocked with a warning")]
    public void StoredCompletedIgnored()
    {
        using TestRepo repo = new();
        repo.Write("openspec/changes/export-remittances/.openspec.yaml", "schema: spec-driven\nx-rtm:\n  id: 107\n  status: Completed\n");
        OpenSpecStoreApi.Open(repo.Root);
        WorkItem it = Item(107);
        Assert.Equal("Blocked", it.Status);
        Assert.Equal("Marked Completed but not archived", it.Warning);
    }

    [Fact(DisplayName = "Features from the change's specs/ folder are locked; features listed in x-rtm can be removed")]
    public void DerivedAndManualFeatures()
    {
        using TestRepo repo = new();
        OpenSpecStoreApi.Open(repo.Root);
        Assert.Equal([new FeatureLink("non-functional/availability", true), new FeatureLink("non-functional/security", false)], Item(123).Features);
    }

    [Fact(DisplayName = "Only a box holding just x counts as a done subtask")]
    public void Subtasks()
    {
        using TestRepo repo = new();
        OpenSpecStoreApi.Open(repo.Root);

        IReadOnlyList<Subtask> subs = Item(123).Subtasks;

        Assert.Equal(["1.1", "1.2", "1.3", "2.1"], subs.Select(s => s.Number));
        Assert.Equal([true, true, false, false], subs.Select(s => s.Done));
        Assert.Equal("Add /health route", subs[0].Text);
        Assert.Equal(["1. Endpoint", "1. Endpoint", "1. Endpoint", "2. Hardening"], subs.Select(s => s.Group));
    }

    [Fact(DisplayName = "A new work item number is one more than the highest, archived ones included")]
    public void NextNumber()
    {
        using TestRepo repo = new();
        Directory.Delete(repo.PathOf("openspec/changes/plain-change"), true);
        OpenSpecStoreApi.Open(repo.Root);
        Assert.Equal(125, WorkItemsApi.NextId());
    }

    [Fact(DisplayName = "Two changes with the same number both show and a warning names their folders")]
    public void DuplicateNumbers()
    {
        using TestRepo repo = new();
        repo.Write("openspec/changes/plain-change/.openspec.yaml", "schema: spec-driven\nx-rtm:\n  id: 107\n");
        OpenSpecStoreApi.Open(repo.Root);

        Assert.Equal(2, WorkItemsApi.GetAll().Count(w => w.Id == 107));
        Assert.Contains("#wi107 is used by export-remittances and plain-change", WorkItemsApi.GetWarnings());
    }

    [Fact(DisplayName = "Column sub-categories come from config.yaml and each item remembers its lane")]
    public void Lanes()
    {
        using TestRepo repo = new();
        OpenSpecStoreApi.Open(repo.Root);
        Assert.Equal(["High priority", "Lower priority"], WorkItemsApi.GetLanes()["In Progress"]);
        Assert.Equal("High priority", Item(123).Lanes["In Progress"]);
    }

    [Fact(DisplayName = "The description is the proposal body below its title heading")]
    public void Description()
    {
        using TestRepo repo = new();
        OpenSpecStoreApi.Open(repo.Root);
        Assert.StartsWith("## Why\n\nExpose a lightweight /health endpoint", WorkItemsApi.GetDescription(123));
    }
}
