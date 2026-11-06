using OpenSpecStore;
using WorkItems;
using WorkItems.PublicDTOs;

namespace TaskManager.Tests;

/// <summary>
/// Completing a work item whose change the OpenSpec CLI refuses as written, end to end through the real CLI (via npx):
/// the user moves the card and it archives, whatever is wrong with the specs. Slow and needs Node.js.
/// </summary>
[Collection(nameof(ModuleStateCollection))]
[Trait("Category", "OpenSpecCli")]
public sealed class CompletingItemsTheCliWouldRefuseTests
{
    private const string Requirement = """
        ### Requirement: One public entry point per module
        Each module SHALL expose one `*Api` class, and other modules MUST call nothing else.

        #### Scenario: Payments module
        - **WHEN** another module needs a payment
        - **THEN** it calls `PaymentsApi` and nothing else in the module

        """;

    /// <summary>A change folder <c>stripe-checkout-poc</c> (#wi900) with the given delta specs, opened in the app.</summary>
    private static WorkItem Change(TestRepo repo, params (string Capability, string Delta)[] deltas)
    {
        const string dir = "openspec/changes/stripe-checkout-poc/";
        repo.Write(dir + ".openspec.yaml", "schema: spec-driven\ncreated: 2026-11-06\nx-rtm:\n  id: 900\n  title: Stripe checkout POC\n  status: In Progress\n");
        repo.Write(dir + "proposal.md", "## Why\n\nClients cannot pay online today; a proof of concept with Stripe Checkout shows whether they would.\n\n## What Changes\n\n- A checkout page.\n");
        repo.Write(dir + "tasks.md", "## 1. Checkout\n\n- [x] 1.1 Checkout page\n- [ ] 1.2 Webhook\n");
        foreach ((string capability, string delta) in deltas) repo.Write($"{dir}specs/{capability}/spec.md", delta.Replace("\r\n", "\n"));
        OpenSpecStoreApi.Open(repo.Root);
        return Assert.Single(WorkItemsApi.GetAll(), i => i.Folder == "stripe-checkout-poc");
    }

    private static string ArchivedDir(TestRepo repo) =>
        Assert.Single(Directory.GetDirectories(repo.PathOf("openspec/changes/archive"), "*stripe-checkout-poc"));

    [Fact(DisplayName = "An item whose delta modifies a feature that has no spec yet completes: the spec is created from it and no error is shown")]
    public void ModifiedAgainstMissingSpec()
    {
        using TestRepo repo = new();
        WorkItem item = Change(repo,
            ("module-boundaries", "## MODIFIED Requirements\n\n" + Requirement),
            ("client-payments", "## ADDED Requirements\n\n" + Requirement.Replace("One public entry point per module", "Clients pay by card")));

        (bool ok, string message) = WorkItemsApi.SetStatus(item.Id, "Completed");

        Assert.Equal((true, ""), (ok, message));
        Assert.True(WorkItemsApi.Get(900)!.Archived);
        Assert.Contains("### Requirement: One public entry point per module", repo.Read("openspec/specs/module-boundaries/spec.md"));
        Assert.Contains("### Requirement: Clients pay by card", repo.Read("openspec/specs/client-payments/spec.md"));
        Assert.Empty(Directory.GetFiles(ArchivedDir(repo), "n*.md", SearchOption.AllDirectories));   // merged the normal way: nothing to record
    }

    [Fact(DisplayName = "An item whose requirement fails OpenSpec validation still completes, and its spec is merged")]
    public void InvalidRequirementStillMerges()
    {
        using TestRepo repo = new();
        WorkItem item = Change(repo, ("client-payments", "## ADDED Requirements\n\n### Requirement: Clients pay by card\nClients pay by card on a checkout page.\n\n#### Scenario: Pay\n- **WHEN** a client pays\n- **THEN** the invoice is settled\n"));

        (bool ok, string message) = WorkItemsApi.SetStatus(item.Id, "Completed");

        Assert.Equal((true, ""), (ok, message));
        Assert.True(WorkItemsApi.Get(900)!.Archived);
        Assert.Contains("### Requirement: Clients pay by card", repo.Read("openspec/specs/client-payments/spec.md"));
    }

    [Fact(DisplayName = "An item whose specs cannot be merged at all still completes, and a note on it says the specs were left out and why")]
    public void UnmergeableSpecsLeaveANote()
    {
        using TestRepo repo = new();
        WorkItem item = Change(repo, ("client-payments", "## ADDED Requirements\n\nTo be written.\n"));

        (bool ok, string message) = WorkItemsApi.SetStatus(item.Id, "Completed");

        Assert.Equal((true, ""), (ok, message));
        Assert.True(WorkItemsApi.Get(900)!.Archived);
        Assert.False(File.Exists(repo.PathOf("openspec/specs/client-payments/spec.md")));
        string note = File.ReadAllText(Assert.Single(Directory.GetFiles(ArchivedDir(repo), "n*.md", SearchOption.AllDirectories)));
        Assert.Contains("# Archived without merging its specs", note);
        Assert.Contains("by: App", note);
        Assert.DoesNotContain("â", note);   // the CLI's ⚠ and ✗ arrive as written, not as mis-decoded bytes
        Assert.True(File.Exists(Path.Combine(ArchivedDir(repo), "specs", "client-payments", "spec.md")));   // the text is kept with the item
    }
}
