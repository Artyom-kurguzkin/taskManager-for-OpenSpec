using Features;
using Features.PublicDTOs;
using OpenSpecStore;
using WorkItems;
using WorkItems.PublicDTOs;

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

    [Fact(DisplayName = "A feature backed by a spec folder is deleted with that folder")]
    public void DeleteSpecFeature()
    {
        using TestRepo repo = new();
        OpenSpecStoreApi.Open(repo.Root);

        (bool ok, string message) = FeaturesApi.DeleteFeature("functional/banking");

        Assert.True(ok, message);
        Assert.DoesNotContain(FeaturesApi.GetAll(), f => f.Path == "functional/banking");
        Assert.False(Directory.Exists(repo.PathOf("openspec/specs/functional/banking")));
        Assert.True(Directory.Exists(repo.PathOf("openspec/specs/functional/remittances")));   // its neighbours stay
    }

    [Fact(DisplayName = "Renaming a spec feature moves its folder and every change's delta folder for it, and the work items keep their link")]
    public void RenameSpecFeature()
    {
        using TestRepo repo = new();
        OpenSpecStoreApi.Open(repo.Root);
        const string archived = "openspec/changes/archive/2026-09-18-session-timeout/specs/non-functional";

        (bool ok, string message, _) = WorkItemsApi.RenameFeature("non-functional/security", "Safety");

        Assert.True(ok, message);
        Assert.False(Directory.Exists(repo.PathOf("openspec/specs/non-functional/security")));
        Assert.True(File.Exists(repo.PathOf("openspec/specs/non-functional/safety/spec.md")));
        Assert.True(File.Exists(repo.PathOf($"{archived}/safety/spec.md")));
        Assert.False(Directory.Exists(repo.PathOf($"{archived}/security")));
        Assert.Contains(FeaturesApi.GetAll(), f => f.Path == "non-functional/safety" && f.Name == "Safety");
        Assert.Contains(new FeatureLink("non-functional/safety", true), WorkItemsApi.Get(124)!.Features);   // its archived delta moved with it
        Assert.Contains(new FeatureLink("non-functional/safety", false), WorkItemsApi.Get(123)!.Features);   // a manual link is renamed in x-rtm
    }

    [Fact(DisplayName = "Renaming a feature added from the board renames it in config.yaml; a name already taken is refused")]
    public void RenameCustomFeature()
    {
        using TestRepo repo = new();
        OpenSpecStoreApi.Open(repo.Root);
        FeaturesApi.CreateFeature(FeaturesApi.Ungrouped, "Exports");

        Assert.True(WorkItemsApi.RenameFeature("exports", "Reports").Success);

        Assert.Contains("- reports", repo.Read("openspec/config.yaml"));
        Assert.DoesNotContain("- exports", repo.Read("openspec/config.yaml"));
        Assert.False(WorkItemsApi.RenameFeature("reports", "Audit").Success);   // audit is a spec already
        Assert.False(WorkItemsApi.RenameFeature("reports", " ").Success);
    }

    [Fact(DisplayName = "A feature whose folder holds other features is not deleted")]
    public void DeleteNestedRefused()
    {
        using TestRepo repo = new();
        repo.Write("openspec/specs/functional/banking/cards/spec.md", "# cards\n\n## Requirements\n\n### Requirement: Cards\nCards SHALL work.\n\n#### Scenario: S\n- **WHEN** a\n- **THEN** b\n");
        OpenSpecStoreApi.Open(repo.Root);

        (bool ok, string message) = FeaturesApi.DeleteFeature("functional/banking");

        Assert.False(ok);
        Assert.Contains("other features", message);
        Assert.True(File.Exists(repo.PathOf("openspec/specs/functional/banking/spec.md")));
    }
}
