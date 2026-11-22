using AngleSharp.Dom;
using Bunit;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Radzen;
using TaskManager.Components.Pages;
using TaskManager.Components.Layout;
using TaskManager.Components.Shared;
using TaskManager.AppState;
using TaskManager.BackgroundJobs;
using TaskManager.Helpers;
using WorkItems;

namespace TaskManager.Tests;

/// <summary>The list's archived rows, the click on a list pill with the menu expanded, and the "all projects" choice of + status.</summary>
public sealed class ListBugFixesTests : IDisposable
{
    private readonly TestRepo repo = new();
    private readonly TestRepo repo2 = new();
    private readonly string settingsFile = Path.Combine(Path.GetTempPath(), "rtm-ui-" + Guid.NewGuid().ToString("N") + ".json");

    private IRenderedComponent<Overlays>? overlays;

    private BunitContext NewContext(bool twoProjects = false)
    {
        BunitContext ctx = new();
        ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        ctx.Services.AddSingleton<UiService>();
        ctx.Services.AddRadzenComponents();
        ctx.Services.AddSingleton(new SettingsStore(settingsFile));
        ctx.Services.AddSingleton<Workspace>();
        Workspace ws = ctx.Services.GetRequiredService<Workspace>();
        Assert.True(ws.Open(repo.Root).Success);
        if (twoProjects)
        {
            Assert.True(ws.OpenTab(repo2.Root).Success);
            Assert.True(ws.Switch(repo.Root).Success);
        }
        overlays = ctx.Render<Overlays>();
        return ctx;
    }

    /// <summary>The list as it opens on a fresh repository, grouped by label so the archived item shows among the others.</summary>
    private async Task<IRenderedComponent<BoardPage>> ListAsync(BunitContext ctx)
    {
        IRenderedComponent<BoardPage> board = ctx.Render<BoardPage>();
        IRenderedComponent<SideNav> nav = ctx.Render<SideNav>();
        nav.ClickWhenReady("button[aria-label=Layout]");
        nav.FireWhenReady(".menu-item", e => e.Click(), all => all.Single(i => i.TextContent.Trim() == "List"));
        SettingsStore settings = ctx.Services.GetRequiredService<SettingsStore>();
        BoardOptions o = settings.Board(repo.Root);
        settings.SaveBoard(repo.Root, o with { ListGroupBy = "label", ListSwimlane = o.Swimlane });
        await board.InvokeAsync(() => { });
        board.WaitForAssertion(() => Assert.NotEmpty(board.FindAll("tr[data-id]")));
        return board;
    }

    private static IElement Row(IRenderedComponent<BoardPage> board, int id) => board.FindAll("tr[data-id]").First(r => r.GetAttribute("data-id") == id.ToString());

    private static IElement Pill(IRenderedComponent<BoardPage> board, int id, string kind) => Row(board, id).QuerySelector($"button[data-pill={kind}]")!;

    private static string NameOf(TestRepo r) => Path.GetFileName(r.Root);

    [Fact(DisplayName = "An archived row in a two-project list shows its own project's status name")]
    public async Task ArchivedRowShowsItsOwnProjectsStatus()
    {
        await using BunitContext ctx = NewContext(twoProjects: true);
        Workspace ws = ctx.Services.GetRequiredService<Workspace>();
        Assert.True(ws.In(repo2.Root, () => WorkItemsApi.AddStatus("Shipped")).Success);   // the second project's list is not the first's
        IRenderedComponent<BoardPage> board = await ListAsync(ctx);
        board.WaitForAssertion(() => Assert.Equal(2, board.FindAll("tr[data-id='124']").Count));   // the archived item of each project

        // a redraw (a select click) keeps each row's status as it was
        Act(board, () => board.FindAll("tr[data-id='123']").First().QuerySelector("td.num")!, e => e.Click(new MouseEventArgs { ShiftKey = true }));
        await board.InvokeAsync(() => { });

        Assert.All(board.FindAll("tr[data-id='124']"), r =>
        {
            Assert.Equal("Completed", r.QuerySelector("span.box.cell.filled")!.TextContent.Trim());
            Assert.Null(r.QuerySelector("button[data-pill=status]"));
        });
        Assert.True(RowMarkProbe.Selected(board, board.FindAll("tr[data-id='123']").First()));
        Assert.Equal(2, board.FindAll("tr[data-id='124']").Count);
    }

    [Fact(DisplayName = "Opening a pill in the list with the menu expanded does not examine the row")]
    public async Task PillClickDoesNotExamine()
    {
        await using BunitContext ctx = NewContext();
        IRenderedComponent<BoardPage> board = await ListAsync(ctx);
        UiService ui = ctx.Services.GetRequiredService<UiService>();
        ui.SetMenuExpanded(true);
        await board.InvokeAsync(() => { });

        Act(board, () => Pill(board, 107, "status"), e => e.Click());
        board.WaitForAssertion(() => Assert.Single(board.FindAll(".menu-pop")));   // the picker opened
        Assert.Null(ui.Examined);   // and the row was not examined

        board.FireWhenReady(".menu-pop .menu-item", e => e.Click(), all => all.Single(i => i.TextContent.Trim() == "Blocked"));   // choosing a value is the pill's too
        board.WaitForAssertion(() => Assert.Equal("Blocked", WorkItemsApi.Get(107)!.Status));
        Assert.Null(ui.Examined);

        Act(board, () => Pill(board, 123, "priority"), e => e.Click());
        board.WaitForAssertion(() => Assert.Single(board.FindAll(".menu-pop")));
        board.FireWhenReady(".menu-backdrop", e => e.Click(), all => all.Single());   // so is the click outside that closes it
        board.WaitForAssertion(() => Assert.Empty(board.FindAll(".menu-pop")));
        Assert.Null(ui.Examined);
    }

    [Fact(DisplayName = "Opening a pill in the list leaves the examined item as it was")]
    public async Task PillClickKeepsTheExaminedItem()
    {
        await using BunitContext ctx = NewContext();
        IRenderedComponent<BoardPage> board = await ListAsync(ctx);
        UiService ui = ctx.Services.GetRequiredService<UiService>();
        ui.SetMenuExpanded(true);
        await board.InvokeAsync(() => ui.Examine((repo.Root, 123)));
        await board.InvokeAsync(() => { });
        (string Root, int Id)? before = ui.Examined;
        Assert.Equal(123, before!.Value.Id);

        Act(board, () => Pill(board, 107, "status"), e => e.Click());
        board.WaitForAssertion(() => Assert.Single(board.FindAll(".menu-pop")));

        Assert.Equal(before, ui.Examined);   // neither moved to the pill's row nor let go
    }

    [Fact(DisplayName = "Shift-clicking a pill in the list still selects its row")]
    public async Task ShiftClickOnPillSelectsTheRow()
    {
        await using BunitContext ctx = NewContext();
        IRenderedComponent<BoardPage> board = await ListAsync(ctx);
        UiService ui = ctx.Services.GetRequiredService<UiService>();
        ui.SetMenuExpanded(true);
        await board.InvokeAsync(() => { });

        Act(board, () => Pill(board, 107, "status"), e => e.Click(new MouseEventArgs { ShiftKey = true }));
        board.WaitForAssertion(() => Assert.True(RowMarkProbe.Selected(board, Row(board, 107))));
        Assert.Empty(board.FindAll(".menu-pop"));
        Assert.Null(ui.Examined);
    }

    [Fact(DisplayName = "A plain click on a list row beside its pills still examines it with the menu expanded")]
    public async Task RowClickStillExamines()
    {
        await using BunitContext ctx = NewContext();
        IRenderedComponent<BoardPage> board = await ListAsync(ctx);
        UiService ui = ctx.Services.GetRequiredService<UiService>();
        ui.SetMenuExpanded(true);
        await board.InvokeAsync(() => { });

        Act(board, () => Row(board, 107).QuerySelector("td.num")!, e => e.Click());
        board.WaitForAssertion(() => Assert.Equal(107, ui.Examined?.Id));
    }

    [Fact(DisplayName = "Choosing 'all projects' for a new status still adds it to every project after the sentinel change")]
    public async Task AllProjectsStillWorks()
    {
        await using BunitContext ctx = NewContext(twoProjects: true);
        Workspace ws = ctx.Services.GetRequiredService<Workspace>();
        IRenderedComponent<BoardPage> board = ctx.Render<BoardPage>();
        board.WaitForAssertion(() => Assert.NotEmpty(board.FindAll(".head-row")));

        board.ClickWhenReady(".head-row > button.add-col");
        overlays!.WaitForAssertion(() => Assert.Equal([NameOf(repo), NameOf(repo2), "All projects"], overlays.FindAll(".dialog .choice").Select(c => c.TextContent.Trim())));
        overlays.FireWhenReady(".dialog .choice", e => e.Click(), all => all.Single(c => c.TextContent.Trim() == "All projects"));
        overlays.WaitForAssertion(() => Assert.Contains("New status", overlays.Find(".dialog").TextContent));
        overlays.InputWhenReady(".dialog input", "Design");
        overlays.ClickWhenReady(".dialog button.primary");

        board.WaitForAssertion(() => Assert.Contains("Design", ws.In(repo.Root, () => WorkItemsApi.GetStatuses())));
        Assert.Contains("Design", ws.In(repo2.Root, () => WorkItemsApi.GetStatuses()));
    }

    public void Dispose()
    {
        repo.Dispose();
        repo2.Dispose();
        File.Delete(settingsFile);
    }
    private static void Act<T>(Bunit.IRenderedComponent<T> cut, Func<AngleSharp.Dom.IElement?> find, Action<AngleSharp.Dom.IElement> fire) where T : Microsoft.AspNetCore.Components.IComponent
    {
        cut.Eventually(() => Xunit.Assert.NotNull(find()));   // rendered and present, or the test fails
        cut.InvokeAsync(() => fire(find()!)).GetAwaiter().GetResult();
    }
}
