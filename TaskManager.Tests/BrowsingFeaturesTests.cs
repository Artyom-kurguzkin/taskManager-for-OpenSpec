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

    [Fact(DisplayName = "Adding a feature from the board lists it in config.yaml instead of writing an empty (invalid) spec")]
    public void CreateFeature()
    {
        using TestRepo repo = new();
        OpenSpecStoreApi.Open(repo.Root);

        (bool ok, string _, Feature? created) = FeaturesApi.CreateFeature("Functional", "Unbilled follow-up");

        Assert.True(ok);
        Assert.Equal(new Feature("functional/unbilled-follow-up", "Unbilled follow up", "Functional", Custom: true), created);
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
        FeaturesApi.CreateFeature("Functional", "Exports");
        repo.Write("openspec/specs/functional/exports/spec.md", "# Exports\n");

        Assert.Single(FeaturesApi.GetAll(), f => f.Path == "functional/exports");
    }

    [Fact(DisplayName = "Adding a feature that already exists is refused")]
    public void DuplicateFeature()
    {
        using TestRepo repo = new();
        OpenSpecStoreApi.Open(repo.Root);
        (bool ok, string message, Feature? _) = FeaturesApi.CreateFeature("Functional", "Remittances");
        Assert.False(ok);
        Assert.Equal("Remittances already exists in Functional", message);
    }

    [Fact(DisplayName = "A feature added from the board is custom; a spec folder is not")]
    public void CustomFlag()
    {
        using TestRepo repo = new();
        OpenSpecStoreApi.Open(repo.Root);
        FeaturesApi.CreateFeature("Functional", "Payments");

        Assert.True(FeaturesApi.GetAll().Single(f => f.Path == "functional/payments").Custom);
        Assert.False(FeaturesApi.GetAll().Single(f => f.Path == "functional/banking").Custom);
    }

    [Fact(DisplayName = "Adding a feature under a new side name creates that side")]
    public void NewGroup()
    {
        using TestRepo repo = new();
        OpenSpecStoreApi.Open(repo.Root);

        (bool ok, string _, Feature? created) = FeaturesApi.CreateFeature("Operations", "On-call");

        Assert.True(ok);
        Assert.Equal(new Feature("operations/on-call", "On call", "Operations", Custom: true), created);
        Assert.Contains("Operations", FeaturesApi.GetAll().Select(f => f.Group));
    }

    [Fact(DisplayName = "Deleting a custom feature removes it from config.yaml")]
    public void DeleteCustom()
    {
        using TestRepo repo = new();
        OpenSpecStoreApi.Open(repo.Root);
        FeaturesApi.CreateFeature("Functional", "Payments");

        (bool ok, string _) = FeaturesApi.DeleteFeature("functional/payments");

        Assert.True(ok);
        Assert.DoesNotContain(FeaturesApi.GetAll(), f => f.Path == "functional/payments");
        Assert.DoesNotContain("functional/payments", repo.Read("openspec/config.yaml"));
    }

    [Fact(DisplayName = "A feature backed by a spec folder can't be deleted from the app")]
    public void DeleteSpecRefused()
    {
        using TestRepo repo = new();
        OpenSpecStoreApi.Open(repo.Root);

        (bool ok, string message) = FeaturesApi.DeleteFeature("functional/banking");

        Assert.False(ok);
        Assert.Equal("Banking has a spec in openspec/specs, so only OpenSpec can remove it", message);
        Assert.Contains(FeaturesApi.GetAll(), f => f.Path == "functional/banking");
    }
}
