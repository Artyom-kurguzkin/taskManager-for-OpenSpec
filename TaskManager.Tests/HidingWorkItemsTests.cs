using OpenSpecStore;
using WorkItems;

namespace TaskManager.Tests;

[Collection(nameof(ModuleStateCollection))]
public sealed class HidingWorkItemsTests
{
    [Fact(DisplayName = "Deleting a work item only hides it: the change folder stays and x-rtm records hidden")]
    public void HideKeepsFolder()
    {
        using TestRepo repo = new();
        OpenSpecStoreApi.Open(repo.Root);

        (bool ok, string _) = WorkItemsApi.Hide(107);

        Assert.True(ok);
        Assert.True(Directory.Exists(repo.PathOf("openspec/changes/export-remittances")));
        Assert.Contains("  hidden: true\n", repo.Read("openspec/changes/export-remittances/.openspec.yaml"));
        Assert.DoesNotContain(WorkItemsApi.GetAll(), w => w.Id == 107);
        Assert.True(Assert.Single(WorkItemsApi.GetAll(includeHidden: true), w => w.Id == 107).Hidden);
        Assert.NotNull(WorkItemsApi.Get(107));   // its page still opens, so it can be restored
    }

    [Fact(DisplayName = "A hidden work item can be shown again")]
    public void Unhide()
    {
        using TestRepo repo = new();
        OpenSpecStoreApi.Open(repo.Root);
        WorkItemsApi.Hide(107);

        WorkItemsApi.Unhide(107);

        Assert.Contains(WorkItemsApi.GetAll(), w => w.Id == 107);
        Assert.DoesNotContain("hidden", repo.Read("openspec/changes/export-remittances/.openspec.yaml"));
    }

    [Fact(DisplayName = "Hidden work items keep their number, so new ones never reuse it")]
    public void HiddenNumbersReserved()
    {
        using TestRepo repo = new();
        OpenSpecStoreApi.Open(repo.Root);
        WorkItemsApi.Hide(125);
        Assert.Equal(126, WorkItemsApi.NextId());
    }
}
