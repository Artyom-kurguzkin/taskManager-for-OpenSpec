using Bunit;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using OpenSpecStore;
using Radzen;
using TaskManager.Components.Pages;
using TaskManager.Components.Shared;
using TaskManager.AppState;
using TaskManager.BackgroundJobs;
using TaskManager.Helpers;
using WorkItems;

namespace TaskManager.Tests;

/// <summary>
/// A project keeps its own list of statuses (openspec/config.yaml, x-rtm.statuses); Backlog, In Progress and Completed are always in it.
/// Nothing here waits for time: every step is a call or a click that is awaited.
/// </summary>
public sealed class KeepingProjectStatusesTests : IDisposable
{
    private const string Config = "openspec/config.yaml";
    private static readonly string[] Defaults = ["Backlog", "Planned", "Confirmed", "In Progress", "Blocked", "In Review", "Completed"];

    private readonly TestRepo repo = new();
    private readonly string settingsFile = Path.Combine(Path.GetTempPath(), "rtm-statuses-" + Guid.NewGuid().ToString("N") + ".json");

    public void Dispose()
    {
        repo.Dispose();
        File.Delete(settingsFile);
    }

    private string Yaml() => repo.Read(Config).Replace("\r\n", "\n");

    [Fact(DisplayName = "A project's status list comes from x-rtm.statuses and defaults to the built-in list")]
    public void TheListComesFromTheProjectsConfig()
    {
        OpenSpecStoreApi.Open(repo.Root);
        Assert.Equal(Defaults, WorkItemsApi.GetStatuses());   // no line: the built-in list

        repo.Write(Config, Yaml().Replace("x-rtm:\n", "x-rtm:\n  statuses: [Backlog, Todo, in progress, In Progress, completed, Completed]\n"));
        OpenSpecStoreApi.Refresh();   // an outside edit, noticed by the board's file watcher
        Assert.Equal(["Backlog", "Todo", "in progress", "In Progress", "completed", "Completed"], WorkItemsApi.GetStatuses());   // spelling is kept: look-alikes are separate statuses

        // a hand edit that leaves the fixed three out: they are put back, where they belong
        repo.Write(Config, Yaml().Replace("  statuses: [Backlog, Todo, in progress, In Progress, completed, Completed]\n", "  statuses: [Todo, Done, Todo]\n"));
        OpenSpecStoreApi.Refresh();   // an outside edit, noticed by the board's file watcher
        Assert.Equal(["Backlog", "In Progress", "Todo", "Done", "Completed"], WorkItemsApi.GetStatuses());   // and a name listed twice counts once
        Assert.Contains("  lanes:", Yaml());   // the rest of the block is not touched
    }

    [Fact(DisplayName = "Backlog, In Progress and Completed cannot be removed, another status can when no item has it")]
    public void FixedStatusesStayAndUnusedOnesGo()
    {
        OpenSpecStoreApi.Open(repo.Root);

        Assert.True(WorkItemsApi.AddStatus("Review ready").Success);
        Assert.Equal([.. Defaults, "Review ready"], WorkItemsApi.GetStatuses());
        Assert.Contains("statuses:", Yaml());
        Assert.Contains("  lanes:", Yaml());   // lanes stay beside it

        foreach (string fixedOne in WorkItemsApi.FixedStatuses)
        {
            (bool ok, string message) = WorkItemsApi.RemoveStatus(fixedOne);
            Assert.False(ok);
            Assert.Contains("stays", message);
        }
        Assert.Contains("Backlog", WorkItemsApi.GetStatuses());

        Assert.True(WorkItemsApi.RemoveStatus("Confirmed").Success);   // nobody has it
        Assert.Equal(["Backlog", "Planned", "In Progress", "Blocked", "In Review", "Completed", "Review ready"], WorkItemsApi.GetStatuses());

        Assert.False(WorkItemsApi.AddStatus("Review ready").Success);   // listed already
        Assert.False(WorkItemsApi.AddStatus("   ").Success);
        Assert.True(WorkItemsApi.AddStatus("completed").Success);   // spelling counts: this project may have both, and the board can merge them
        Assert.Equal("completed", WorkItemsApi.GetStatuses()[^1]);

        // back to the built-in list: the line goes away instead of repeating it
        Assert.True(WorkItemsApi.RemoveStatus("completed").Success);
        Assert.True(WorkItemsApi.RemoveStatus("Review ready").Success);
        Assert.True(WorkItemsApi.AddStatus("Confirmed").Success);
        Assert.True(WorkItemsApi.MoveStatus("Confirmed", -4).Success);   // back to its place
        Assert.Equal(Defaults, WorkItemsApi.GetStatuses());
        Assert.DoesNotContain("statuses:", Yaml());
    }

    [Fact(DisplayName = "A status moves up and down the list, and a status that work items have is removed after they are moved to the status picked")]
    public void MovingAndMovingItemsOut()
    {
        OpenSpecStoreApi.Open(repo.Root);

        Assert.True(WorkItemsApi.MoveStatus("Planned", 1).Success);
        Assert.Equal(["Backlog", "Confirmed", "Planned", "In Progress", "Blocked", "In Review", "Completed"], WorkItemsApi.GetStatuses());
        Assert.True(WorkItemsApi.MoveStatus("Backlog", -1).Success);   // already first: stays
        Assert.Equal("Backlog", WorkItemsApi.GetStatuses()[0]);

        Assert.False(WorkItemsApi.RemoveStatus("Planned", "Planned").Success);      // not to itself
        Assert.False(WorkItemsApi.RemoveStatus("Planned", "Completed").Success);    // Completed is archiving, not a place to put work
        Assert.False(WorkItemsApi.RemoveStatus("Planned", "Nowhere").Success);
        Assert.False(WorkItemsApi.RemoveStatus("In Progress", "Backlog").Success);  // fixed
        Assert.Equal("Planned", WorkItemsApi.Get(107)!.Status);

        Assert.True(WorkItemsApi.RemoveStatus("Planned", "Backlog").Success);
        Assert.Equal("Backlog", WorkItemsApi.Get(107)!.Status);
        Assert.DoesNotContain("Planned", WorkItemsApi.GetStatuses());
        Assert.Contains(WorkItemsApi.GetEvents(107), e => e.Kind == "status" && e.Text == "Planned -> Backlog");
    }

    [Fact(DisplayName = "Deleting a status that open items have moves them to the project's first status; archived items keep the name and still show as Completed")]
    public void DeletingAUsedStatusMovesOpenItemsToTheFirstStatus()
    {
        const string archivedYaml = "openspec/changes/archive/2026-09-18-session-timeout/.openspec.yaml";
        repo.Write(archivedYaml, repo.Read(archivedYaml).Replace("x-rtm:", "x-rtm:\n  status: Review ready"));   // it was in Review ready when it was archived
        OpenSpecStoreApi.Open(repo.Root);
        Assert.True(WorkItemsApi.AddStatus("Review ready").Success);
        Assert.True(WorkItemsApi.SetStatus(107, "Review ready").Success);
        Assert.Equal(1, WorkItemsApi.CountWithStatus("Review ready"));   // the archived one shows as Completed: not counted

        Assert.True(WorkItemsApi.RemoveStatus("Review ready").Success);

        Assert.Equal("Backlog", WorkItemsApi.Get(107)!.Status);
        Assert.DoesNotContain("Review ready", WorkItemsApi.GetStatuses());
        Assert.Contains("status: Review ready", repo.Read(archivedYaml));   // history is not rewritten
        Assert.Equal("Completed", WorkItemsApi.Get(124)!.Status);
        Assert.Contains(WorkItemsApi.GetEvents(107), e => e.Kind == "status" && e.Text == "Review ready -> Backlog");

        // the first status is the first column, not always Backlog
        Assert.True(WorkItemsApi.MoveStatus("Confirmed", -2).Success);
        Assert.Equal("Confirmed", WorkItemsApi.GetStatuses()[0]);
        Assert.True(WorkItemsApi.SetStatus(123, "Blocked").Success);
        Assert.True(WorkItemsApi.RemoveStatus("Blocked").Success);
        Assert.Equal("Confirmed", WorkItemsApi.Get(123)!.Status);
    }

    [Fact(DisplayName = "Deleting a status moves its items to the first status even when one of them waits on an open dependency, and none stays behind half-moved")]
    public void DeletingAStatusIgnoresWaitingOnADependency()
    {
        OpenSpecStoreApi.Open(repo.Root);
        Assert.True(WorkItemsApi.AddStatus("Review ready").Success);
        Assert.True(WorkItemsApi.SetStatus(125, "Review ready").Success);
        Assert.True(WorkItemsApi.AddDependency(107, 123).Success);   // #wi107 waits on the open #wi123: Blocked, and no other status is allowed for it
        Assert.False(WorkItemsApi.SetStatus(107, "Review ready").Success);   // the gate is real

        (bool ok, string message) = WorkItemsApi.RemoveStatus("Blocked");

        Assert.True(ok, message);
        Assert.DoesNotContain("Blocked", WorkItemsApi.GetStatuses());
        Assert.Equal("Backlog", WorkItemsApi.Get(107)!.Status);
        Assert.Equal("Review ready", WorkItemsApi.Get(125)!.Status);   // other statuses are untouched
    }

    [Fact(DisplayName = "A default status cannot be deleted, with or without items in it")]
    public void DefaultStatusesCannotBeDeleted()
    {
        OpenSpecStoreApi.Open(repo.Root);
        foreach (string fixedOne in WorkItemsApi.FixedStatuses)
        {
            (bool ok, string message) = WorkItemsApi.RemoveStatus(fixedOne);
            Assert.False(ok);
            Assert.Contains("stays", message);
        }
        Assert.Equal(Defaults, WorkItemsApi.GetStatuses());
    }

    [Fact(DisplayName = "A status outside the project's list is refused with the list named, and an item that has one shows in Backlog until the list has it")]
    public void StatusesOutsideTheList()
    {
        repo.Write("openspec/changes/export-remittances/.openspec.yaml", repo.Read("openspec/changes/export-remittances/.openspec.yaml").Replace("status: Planned", "status: Design"));
        OpenSpecStoreApi.Open(repo.Root);
        Assert.Equal("Backlog", WorkItemsApi.Get(107)!.Status);   // the file says Design, which this project does not list

        Assert.True(WorkItemsApi.RemoveStatus("Confirmed").Success);
        (bool ok, string message) = WorkItemsApi.SetStatus(123, "Confirmed");
        Assert.False(ok);
        Assert.Contains("Backlog, Planned, In Progress, Blocked, In Review, Completed", message);

        Assert.True(WorkItemsApi.AddStatus("Design").Success);
        Assert.Equal("Design", WorkItemsApi.Get(107)!.Status);
        Assert.True(WorkItemsApi.SetStatus(123, "Design").Success);
    }

    [Fact(DisplayName = "Names that look like a fixed status are told: completed, InProgress and in progress stand for Completed and In Progress, Completed itself for none")]
    public void LookAlikesOfTheFixedStatuses()
    {
        Assert.Equal("Completed", WorkItemsApi.LookAlikeOf("completed"));
        Assert.Equal("In Progress", WorkItemsApi.LookAlikeOf("InProgress"));
        Assert.Equal("In Progress", WorkItemsApi.LookAlikeOf("in progress"));
        Assert.Equal("In Progress", WorkItemsApi.LookAlikeOf("In-Progress"));
        Assert.Equal("Backlog", WorkItemsApi.LookAlikeOf("BACKLOG"));
        Assert.Null(WorkItemsApi.LookAlikeOf("Completed"));
        Assert.Null(WorkItemsApi.LookAlikeOf("Done"));
        Assert.Null(WorkItemsApi.LookAlikeOf("Planned"));
    }

    // ── the Settings page ──

    private BunitContext NewContext(out IRenderedComponent<Overlays> overlays)
    {
        BunitContext ctx = new();
        ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        ctx.Services.AddSingleton<UiService>();
        ctx.Services.AddRadzenComponents();
        ctx.Services.AddSingleton(new SettingsStore(settingsFile));
        ctx.Services.AddSingleton<Workspace>();
        Assert.True(ctx.Services.GetRequiredService<Workspace>().Open(repo.Root).Success);
        overlays = ctx.Render<Overlays>();
        return ctx;
    }

    private static List<string> Rows(IRenderedComponent<SettingsPage> page) => page.FindAll("[data-statuses] .st-row").Select(r => r.GetAttribute("data-status")!).ToList();

    [Fact(DisplayName = "The Settings page lists a project's statuses; the fixed ones cannot be removed, a new one is added at the end, moved up and removed again")]
    public async Task TheSettingsPageEditsTheList()
    {
        await using BunitContext ctx = NewContext(out _);
        IRenderedComponent<SettingsPage> page = ctx.Render<SettingsPage>().OnProject();

        Assert.Equal(Defaults, Rows(page));
        foreach (string fixedOne in WorkItemsApi.FixedStatuses) Assert.Empty(page.Find($"[data-status='{fixedOne}']").QuerySelectorAll("button.st-remove"));
        Assert.NotEmpty(page.Find("[data-status='Blocked']").QuerySelectorAll("button.st-remove"));
        Assert.True(page.Find("[data-status='Backlog'] button.st-up").HasAttribute("disabled"));
        Assert.Equal("1 work item", page.Find("[data-status='Planned'] .st-count").TextContent);

        page.InputWhenReady("input.st-new", "Review ready");
        page.ClickWhenReady("button.st-add-btn");
        page.WaitForAssertion(() => Assert.Equal([.. Defaults, "Review ready"], Rows(page)));
        Assert.Contains("Review ready", Yaml());
        Assert.Equal("", page.Find("input.st-new").GetAttribute("value") ?? "");

        page.ClickWhenReady("[data-status='Review ready'] button.st-up");
        page.WaitForAssertion(() => Assert.Equal("Review ready", Rows(page)[^2]));
        Assert.Equal("Review ready", WorkItemsApi.GetStatuses()[^2]);

        page.ClickWhenReady("[data-status='Review ready'] button.st-remove");   // nobody has it: gone at once
        page.WaitForAssertion(() => Assert.Equal(Defaults, Rows(page)));
    }

    [Fact(DisplayName = "Removing a status that work items have asks first, naming how many move to the first status, then moves them and removes it")]
    public async Task RemovingAUsedStatusAsksBeforeMovingTheItems()
    {
        await using BunitContext ctx = NewContext(out IRenderedComponent<Overlays> overlays);
        IRenderedComponent<SettingsPage> page = ctx.Render<SettingsPage>().OnProject();

        page.ClickWhenReady("[data-status='Planned'] button.st-remove");   // #wi107 is Planned

        overlays.WaitForAssertion(() => Assert.Contains("1 work item", overlays.Find(".dialog").TextContent));
        Assert.Contains("Backlog", overlays.Find(".dialog").TextContent);   // where it goes
        overlays.FireWhenReady(".dialog button", e => e.Click(), all => all.Single(b => b.TextContent.Trim() == "Cancel"));
        overlays.WaitForAssertion(() => Assert.Empty(overlays.FindAll(".dialog")));
        Assert.Contains("Planned", WorkItemsApi.GetStatuses());   // nothing moved

        page.ClickWhenReady("[data-status='Planned'] button.st-remove");   // found and clicked in one dispatcher step: a late Changed (git watcher, scan) re-renders the list and re-issues its handler ids between a Find and a Click
        overlays.WaitForAssertion(() => Assert.Contains("1 work item", overlays.Find(".dialog").TextContent));
        overlays.FireWhenReady(".dialog button", e => e.Click(), all => all.Single(b => b.TextContent.Trim() == "Remove"));

        page.WaitForAssertion(() => Assert.DoesNotContain("Planned", Rows(page)));
        Assert.Equal("Backlog", WorkItemsApi.Get(107)!.Status);
        Assert.Equal("2 work items", page.Find("[data-status='Backlog'] .st-count").TextContent);   // #wi125 was there already
    }

    // ---- add-status chunk ----

    [Fact(DisplayName = "A status added before Completed goes left of it, one that exists already is refused whatever the position, and Completed stays last")]
    public void AddingAStatusBeforeCompleted()
    {
        OpenSpecStoreApi.Open(repo.Root);

        Assert.True(WorkItemsApi.AddStatus("Review ready", beforeCompleted: true).Success);
        Assert.Equal(["Backlog", "Planned", "Confirmed", "In Progress", "Blocked", "In Review", "Review ready", "Completed"], WorkItemsApi.GetStatuses());

        (bool ok, string message) = WorkItemsApi.AddStatus("Review ready", beforeCompleted: true);
        Assert.False(ok);
        Assert.Contains("already", message);
        Assert.False(WorkItemsApi.AddStatus("Planned", beforeCompleted: true).Success);

        // a list that does not end with Completed: the new one goes at the end, nothing is moved
        Assert.True(WorkItemsApi.MoveStatus("Completed", -1).Success);
        Assert.True(WorkItemsApi.AddStatus("Parked", beforeCompleted: true).Success);
        Assert.Equal(["Backlog", "Planned", "Confirmed", "In Progress", "Blocked", "In Review", "Completed", "Review ready", "Parked"], WorkItemsApi.GetStatuses());
    }

    [Fact(DisplayName = "A status column the project does not have is marked absent under a shared header row and left out otherwise; a merged column counts when the project has one of its statuses")]
    public void ColumnsOfOtherProjectsStatusesAreMarkedAbsent()
    {
        BoardOptions o = new() { GroupBy = "status", Merges = [new("Done", ["Completed", "Finished"])] };
        IReadOnlyList<BoardColumn> columns = [.. new[] { "Backlog", "Research", "Done" }.Select(k => new BoardColumn(k, k, [], []))];
        IReadOnlyList<BoardSection> sections = [new BoardSection(null, columns)];
        IReadOnlyList<string> mine = ["Backlog", "In Progress", "Completed"];

        Assert.Equal(["Research"], BoardModel.MarkAbsent(sections, o, mine, keep: true)[0].Columns.Where(c => c.Absent).Select(c => c.Key));
        Assert.Equal(["Backlog", "Done"], BoardModel.MarkAbsent(sections, o, mine, keep: false)[0].Columns.Select(c => c.Key));
        Assert.Same(sections, BoardModel.MarkAbsent(sections, o with { GroupBy = "feature" }, mine, keep: true));   // other groupings are never touched
    }

    [Fact(DisplayName = "A status column can be deleted from where it is seen only when it is the project's own custom status: not a fixed one, not a merged column, not one the project lacks")]
    public void OnlyACustomStatusOfTheProjectCanBeDeletedFromAColumn()
    {
        BoardOptions o = new() { GroupBy = "status", Merges = [new("Done", ["Completed", "Finished"])] };
        IReadOnlyList<string> mine = ["Backlog", "Planned", "In Progress", "Completed"];

        Assert.True(BoardModel.CanDeleteStatus(o, mine, "Planned"));
        Assert.False(BoardModel.CanDeleteStatus(o, mine, "Backlog"));
        Assert.False(BoardModel.CanDeleteStatus(o, mine, "In Progress"));
        Assert.False(BoardModel.CanDeleteStatus(o, mine, "Completed"));
        Assert.False(BoardModel.CanDeleteStatus(o, mine, "Research"));   // another project's status
        Assert.False(BoardModel.CanDeleteStatus(o, mine, "Done"));   // a merged column
        Assert.False(BoardModel.CanDeleteStatus(o with { GroupBy = "feature" }, mine, "Planned"));   // only a status grouping has statuses
    }
}
