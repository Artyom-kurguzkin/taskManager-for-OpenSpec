using OpenSpecStore;
using WorkItems;

namespace TaskManager.Tests;

/// <summary>
/// A feature that was deleted stays on the archived items that had it (history is not rewritten). Merging such items: the same deleted feature
/// merges, an existing feature takes over a deleted one, two different deleted features are refused.
/// </summary>
public sealed class MergingDeletedCategoriesTests : IDisposable
{
    private readonly TestRepo repo = new();

    public void Dispose() => repo.Dispose();

    /// <summary>An archived change numbered <paramref name="id"/> that lists <paramref name="feature"/> (a feature that may not exist any more) in x-rtm.features.</summary>
    private void Archive(int id, string feature)
    {
        string dir = $"openspec/changes/archive/2026-10-01-old-{id}";
        repo.Write($"{dir}/.openspec.yaml", $"schema: spec-driven\ncreated: 2026-10-01\nx-rtm:\n  id: {id}\n  title: Old {id}\n  features: [{feature}]\n");
        repo.Write($"{dir}/proposal.md", "## Why\n\nold\n");
    }

    [Fact(DisplayName = "Deleting a feature unassigns its open items and leaves its archived items on it")]
    public void DeletingAFeatureLeavesArchivedItemsOnIt()
    {
        Archive(110, "functional/payments");
        OpenSpecStoreApi.Open(repo.Root);
        Features.FeaturesApi.CreateFeature("Functional", "Payments");
        Assert.True(WorkItemsApi.MoveFeature(107, "functional/payments").Success);
        Assert.Equal(["functional/payments"], WorkItemsApi.Get(110)!.Features.Select(f => f.Path));

        Assert.True(WorkItemsApi.DeleteFeature("functional/payments").Success);

        Assert.Empty(WorkItemsApi.Get(107)!.Features);
        Assert.Equal(["functional/payments"], WorkItemsApi.Get(110)!.Features.Select(f => f.Path));
        Assert.Contains("functional/payments", repo.Read("openspec/changes/archive/2026-10-01-old-110/.openspec.yaml"));
    }

    [Fact(DisplayName = "Archived items with the same deleted feature merge and keep it")]
    public void TheSameDeletedFeatureMerges()
    {
        Archive(110, "functional/old-a");
        Archive(111, "functional/old-a");
        OpenSpecStoreApi.Open(repo.Root);

        (bool ok, string message, int id) = WorkItemsApi.Merge([110, 111]);

        Assert.True(ok, message);
        Assert.Equal(["functional/old-a"], WorkItemsApi.Get(id)!.Features.Select(f => f.Path));
    }

    [Fact(DisplayName = "Merging an item whose feature was deleted with one whose feature exists gives the merged item the existing feature")]
    public void AnExistingFeatureTakesOver()
    {
        Archive(110, "functional/old-a");
        OpenSpecStoreApi.Open(repo.Root);
        Assert.Contains(WorkItemsApi.Get(125)!.Features, f => f.Path == "functional/banking");   // its spec is for Banking, which exists

        (bool ok, string message, int id) = WorkItemsApi.Merge([110, 125]);   // the deleted one is first: it would win if nothing said otherwise

        Assert.True(ok, message);
        Assert.Equal(["functional/banking"], WorkItemsApi.Get(id)!.Features.Select(f => f.Path));
    }

    [Fact(DisplayName = "Merging items with two different deleted features is refused, naming both, and nothing is merged")]
    public void TwoDifferentDeletedFeaturesAreRefused()
    {
        Archive(110, "functional/old-a");
        Archive(111, "functional/old-b");
        OpenSpecStoreApi.Open(repo.Root);

        (bool ok, string message, int _) = WorkItemsApi.Merge([110, 111]);

        Assert.False(ok);
        Assert.Contains("#wi110", message);
        Assert.Contains("#wi111", message);
        Assert.Contains("no longer exist", message);
        Assert.NotNull(WorkItemsApi.Get(110));
        Assert.NotNull(WorkItemsApi.Get(111));
    }
}
