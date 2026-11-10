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

    [Fact(DisplayName = "A feature from the change's specs/ folder is locked; one picked by hand is not")]
    public void DerivedAndManualFeatures()
    {
        using TestRepo repo = new();
        OpenSpecStoreApi.Open(repo.Root);
        Assert.Equal([new FeatureLink("non-functional/security", true)], Item(124).Features);
        Assert.Equal([new FeatureLink("non-functional/security", false)], Item(123).Features);
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

    [Fact(DisplayName = "Fixture files are read with LF line endings on every checkout, so file comparisons in tests do not depend on core.autocrlf")]
    public void FixturesUseLf()
    {
        string root = Path.Combine(AppContext.BaseDirectory, "Fixtures", "sample-repo");
        string[] withCr = Directory.GetFiles(root, "*", SearchOption.AllDirectories)
            .Where(f => File.ReadAllText(f).Contains('\r'))
            .Select(f => Path.GetRelativePath(root, f))
            .ToArray();
        Assert.Empty(withCr);
    }

    [Fact(DisplayName = "A change without x-rtm.moved reads as moved on its creation date")]
    public void MovedMissing()
    {
        using TestRepo repo = new();
        OpenSpecStoreApi.Open(repo.Root);

        Assert.Null(Item(123).Moved);   // nothing is written for it: the UI falls back to Created
        Assert.Equal(new DateTime(2026, 9, 12), Item(123).Created);
        string yaml = Path.Combine(Item(123).Path, ".openspec.yaml");
        OpenSpecStoreApi.WriteText(yaml, OpenSpecStoreApi.ReadText(yaml).Replace("\r\n", "\n").Replace("  id: 123", "  id: 123\n  moved: 2026-10-03"));
        Assert.Equal(new DateOnly(2026, 10, 3), Item(123).Moved);
    }

    // an unclosed list: no one can tell what was meant, so it is not repaired
    private const string BrokenYaml = "schema: spec-driven\nx-rtm:\n  id: 130\n  features: [unclosed\n  status: In Progress\n";

    [Fact(DisplayName = "An x-rtm value with an unquoted colon is repaired on read: the item shows its real title with a warning, and the next save writes it quoted")]
    public void UnquotedColonIsRepaired()
    {
        using TestRepo repo = new();
        const string file = "openspec/changes/broken-one/.openspec.yaml";
        repo.Write(file, "schema: spec-driven\r\nx-rtm:\r\n  id: 130\r\n  title: The app answers at once: no second-long waits #12\r\n  status: In Progress\r\n  label: bug\r\n  plan: docs/a: b.md\r\n");
        OpenSpecStoreApi.Open(repo.Root);

        WorkItem it = Assert.Single(WorkItemsApi.GetAll(includeHidden: true), w => w.Folder == "broken-one");

        Assert.Equal(130, it.Id);
        Assert.Equal("The app answers at once: no second-long waits #12", it.Title);
        Assert.Equal(("In Progress", "bug", "docs/a: b.md"), (it.Status, it.Label, it.Plan));
        Assert.Contains("title on line 4 was not valid YAML and was read as text", it.Warning);
        Assert.Contains("plan on line 7", it.Warning);
        Assert.Contains("the file is fixed on the next save", it.Warning);
        Assert.Contains(WorkItemsApi.GetWarnings(), w => w.StartsWith("#wi130: title on line 4"));
        Assert.Contains("  title: The app answers at once: no second-long waits #12\r\n", repo.Read(file));   // reading changes nothing

        Assert.True(WorkItemsApi.SetPriority(130, "high").Success);

        string after = repo.Read(file);
        Assert.Matches("(?m)^  title: [\"']The app answers at once: no second-long waits #12[\"']\r$", after);   // quoted, whichever quote the writer picks
        Assert.Matches("(?m)^  plan: [\"']docs/a: b.md[\"']\r$", after);
        Assert.Contains("  priority: high\r\n", after);
        Assert.StartsWith("schema: spec-driven\r\n", after);
        Assert.Contains("x-rtm:\r\n  id: 130\r\n", after);
        YamlChecks.Valid(after, []);
        WorkItem fixedIt = WorkItemsApi.Get(130)!;
        Assert.Equal("The app answers at once: no second-long waits #12", fixedIt.Title);
        Assert.Null(fixedIt.Warning);
    }

    [Fact(DisplayName = "A valid value with a trailing comment is not touched by the repair of another line")]
    public void RepairLeavesValidLinesAlone()
    {
        using TestRepo repo = new();
        const string file = "openspec/changes/broken-one/.openspec.yaml";
        repo.Write(file, "schema: spec-driven\nx-rtm:\n  id: 130\n  title: Fine title # a comment\n  status: In Progress\n  plan: docs/a: b.md\n");
        OpenSpecStoreApi.Open(repo.Root);

        WorkItem it = Item(130);

        Assert.Equal("Fine title", it.Title);
        Assert.Contains("plan on line 6", it.Warning);
        Assert.DoesNotContain("title on line", it.Warning);
    }

    [Fact(DisplayName = "A change whose .openspec.yaml does not parse still shows, with a warning naming the file and the line, and the other items are unaffected")]
    public void UnparsableYamlStillShows()
    {
        using TestRepo repo = new();
        repo.Write("openspec/changes/broken-one/.openspec.yaml", BrokenYaml);
        OpenSpecStoreApi.Open(repo.Root);

        WorkItem it = Assert.Single(WorkItemsApi.GetAll(includeHidden: true), w => w.Folder == "broken-one");

        Assert.Equal("Broken one", it.Title);   // the folder's name: the fields could not be read
        Assert.Equal("Backlog", it.Status);
        Assert.Contains(".openspec.yaml", it.Warning);
        Assert.Contains("line 4", it.Warning);
        Assert.Contains("fix it", it.Warning);
        Assert.Contains(WorkItemsApi.GetWarnings(), w => w.Contains("line 4"));
        Assert.Equal("Health-check endpoint", Item(123).Title);
        Assert.Equal("In Progress", Item(123).Status);
        Assert.Null(Item(123).Warning);
    }

    [Fact(DisplayName = "A .openspec.yaml with mixed line endings is read")]
    public void MixedLineEndings()
    {
        using TestRepo repo = new();
        repo.Write("openspec/changes/mixed-ends/.openspec.yaml", "schema: spec-driven\r\nx-rtm:\n  id: 140\r\n  title: Mixed endings\n  status: In Progress\r\n  priority: high\n");
        OpenSpecStoreApi.Open(repo.Root);

        WorkItem it = Item(140);

        Assert.Equal(("Mixed endings", "In Progress", "high"), (it.Title, it.Status, it.Priority));
        Assert.Null(it.Warning);
    }
}
