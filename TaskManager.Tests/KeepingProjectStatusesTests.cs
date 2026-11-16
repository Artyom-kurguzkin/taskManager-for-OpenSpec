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
        Assert.Equal(["Backlog", "Todo", "in progress", "In Progress", "completed", "Completed"], WorkItemsApi.GetStatuses());   // spelling is kept: look-alikes are separate statuses

        // a hand edit that leaves the fixed three out: they are put back, where they belong
        repo.Write(Config, Yaml().Replace("  statuses: [Backlog, Todo, in progress, In Progress, completed, Completed]\n", "  statuses: [Todo, Done, Todo]\n"));
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

        (bool used, string why) = WorkItemsApi.RemoveStatus("Planned");   // #wi107 is Planned
        Assert.False(used);
        Assert.Contains("1 work item has the status \"Planned\"", why);
        Assert.Contains("Planned", WorkItemsApi.GetStatuses());

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
        IRenderedComponent<SettingsPage> page = ctx.Render<SettingsPage>();

        Assert.Equal(Defaults, Rows(page));
        foreach (string fixedOne in WorkItemsApi.FixedStatuses) Assert.Empty(page.Find($"[data-status='{fixedOne}']").QuerySelectorAll("button.st-remove"));
        Assert.NotEmpty(page.Find("[data-status='Blocked']").QuerySelectorAll("button.st-remove"));
        Assert.True(page.Find("[data-status='Backlog'] button.st-up").HasAttribute("disabled"));
        Assert.Equal("1 work item", page.Find("[data-status='Planned'] .st-count").TextContent);

        page.Find("input.st-new").Input("Review ready");
        page.Find("button.st-add-btn").Click();
        page.WaitForAssertion(() => Assert.Equal([.. Defaults, "Review ready"], Rows(page)));
        Assert.Contains("Review ready", Yaml());
        Assert.Equal("", page.Find("input.st-new").GetAttribute("value") ?? "");

        page.Find("[data-status='Review ready'] button.st-up").Click();
        page.WaitForAssertion(() => Assert.Equal("Review ready", Rows(page)[^2]));
        Assert.Equal("Review ready", WorkItemsApi.GetStatuses()[^2]);

        page.Find("[data-status='Review ready'] button.st-remove").Click();   // nobody has it: gone at once
        page.WaitForAssertion(() => Assert.Equal(Defaults, Rows(page)));
    }

    [Fact(DisplayName = "Removing a status that work items have asks where they go, moves them there and then removes it")]
    public async Task RemovingAUsedStatusAsksWhereTheItemsGo()
    {
        await using BunitContext ctx = NewContext(out IRenderedComponent<Overlays> overlays);
        IRenderedComponent<SettingsPage> page = ctx.Render<SettingsPage>();

        page.Find("[data-status='Planned'] button.st-remove").Click();   // #wi107 is Planned

        overlays.WaitForAssertion(() => Assert.NotEmpty(overlays.FindAll(".dialog .choice")));
        List<string> choices = overlays.FindAll(".dialog .choice").Select(c => c.TextContent.Trim()).ToList();
        Assert.Equal(["Backlog", "Confirmed", "In Progress", "Blocked", "In Review"], choices);   // not itself, not Completed
        overlays.FindAll(".dialog .choice").Single(c => c.TextContent.Trim() == "Confirmed").Click();

        page.WaitForAssertion(() => Assert.DoesNotContain("Planned", Rows(page)));
        Assert.Equal("Confirmed", WorkItemsApi.Get(107)!.Status);
        Assert.Equal("1 work item", page.Find("[data-status='Confirmed'] .st-count").TextContent);
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
}
