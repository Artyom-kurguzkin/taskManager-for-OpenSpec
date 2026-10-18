using Features;
using Features.PublicDTOs;
using OpenSpecStore;

namespace TaskManager.Tests;

[Collection(nameof(ModuleStateCollection))]
public sealed class BrowsingFeaturesTests
{
    [Fact(DisplayName = "Capabilities in two-level folders show as features under their group")]
    public void GroupedFeatures()
    {
        using TestRepo repo = new();
        OpenSpecStoreApi.Open(repo.Root);

        IReadOnlyList<Feature> all = FeaturesApi.GetAll();

        Assert.Contains(new Feature("functional/remittances", "Remittances", "Functional"), all);
        Assert.Contains(new Feature("non-functional/availability", "Availability", "Non-Functional"), all);
    }

    [Fact(DisplayName = "A capability directly under specs/ belongs to Ungrouped")]
    public void UngroupedFeature()
    {
        using TestRepo repo = new();
        OpenSpecStoreApi.Open(repo.Root);
        Assert.Contains(new Feature("audit", "Audit", "Ungrouped"), FeaturesApi.GetAll());
    }

    [Fact(DisplayName = "Groups are listed in folder order with their features")]
    public void Groups()
    {
        using TestRepo repo = new();
        OpenSpecStoreApi.Open(repo.Root);
        Assert.Equal(["Ungrouped", "Functional", "Non-Functional"], FeaturesApi.GetAll().Select(f => f.Group).Distinct().OrderBy(g => g == "Ungrouped" ? "" : g));
    }

    [Fact(DisplayName = "Adding a sub-group lists the new feature in config.yaml instead of writing an empty (invalid) spec")]
    public void CreateSubgroup()
    {
        using TestRepo repo = new();
        OpenSpecStoreApi.Open(repo.Root);

        (bool ok, string _, Feature? created) = FeaturesApi.CreateSubgroup("Functional", "Unbilled follow-up");

        Assert.True(ok);
        Assert.Equal(new Feature("functional/unbilled-follow-up", "Unbilled follow up", "Functional"), created);
        Assert.False(File.Exists(repo.PathOf("openspec/specs/functional/unbilled-follow-up/spec.md")));
        Assert.Contains("  features:\n    - functional/unbilled-follow-up\n", repo.Read("openspec/config.yaml"));
        Assert.Contains("  lanes:\n", repo.Read("openspec/config.yaml"));   // the lanes section is untouched
        Assert.Contains(created, FeaturesApi.GetAll());
    }

    [Fact(DisplayName = "A listed feature shows only once after OpenSpec creates its real spec")]
    public void DeclaredFeatureBecomesReal()
    {
        using TestRepo repo = new();
        OpenSpecStoreApi.Open(repo.Root);
        FeaturesApi.CreateSubgroup("Functional", "Exports");
        repo.Write("openspec/specs/functional/exports/spec.md", "# Exports\n");

        Assert.Single(FeaturesApi.GetAll(), f => f.Path == "functional/exports");
    }

    [Fact(DisplayName = "Adding a sub-group that already exists is refused")]
    public void DuplicateSubgroup()
    {
        using TestRepo repo = new();
        OpenSpecStoreApi.Open(repo.Root);
        (bool ok, string message, Feature? _) = FeaturesApi.CreateSubgroup("Functional", "Remittances");
        Assert.False(ok);
        Assert.Equal("Remittances already exists in Functional", message);
    }
}
