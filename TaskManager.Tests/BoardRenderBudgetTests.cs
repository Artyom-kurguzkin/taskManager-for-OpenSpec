using AngleSharp.Dom;
using Bunit;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Radzen;
using TaskManager.Components;
using TaskManager.Components.Layout;
using TaskManager.Components.Pages;
using TaskManager.Components.Shared;
using TaskManager.AppState;
using TaskManager.BackgroundJobs;
using TaskManager.Helpers;
using WorkItems;

namespace TaskManager.Tests;

/// <summary>
/// What a click on the board costs, by what the user does (#wi191): a card draws only when what it shows changed, and a click on a column
/// or project heading that changes no selection draws nothing. Counted with the components' own render counters, never timed.
/// </summary>
public sealed class BoardRenderBudgetTests : IDisposable
{
    private readonly TestRepo repo = new();
    private readonly TestRepo repo2 = new();   // a second project for the shared header row
    private readonly string settingsFile = Path.Combine(Path.GetTempPath(), "rtm-ui-" + Guid.NewGuid().ToString("N") + ".json");
    private readonly FakeTimeProvider time = new();   // the page's 50 ms reload window moves only when a test moves it

    private IRenderedComponent<SideNav>? nav;

    private BunitContext NewContext()
    {
        BunitContext ctx = new();
        ctx.Services.AddSingleton<TimeProvider>(time);
        ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        ctx.Services.AddSingleton<UiService>();
        ctx.Services.AddRadzenComponents();
        ctx.Services.AddSingleton(new SettingsStore(settingsFile));
        ctx.Services.AddSingleton<Workspace>();
        OpenSpecStore.OpenSpecStoreModule.State.RealWatcher = false;   // the counts are read between steps: a late event from the disk would reload the page in the middle of one
        Assert.True(ctx.Services.GetRequiredService<Workspace>().Open(repo.Root).Success);
        ctx.Render<Overlays>();
        nav = ctx.Render<SideNav>();
        return ctx;
    }

    /// <summary>The fixture plus <paramref name="extra"/> created items on the board, the menu expanded (so a click examines) and settled.</summary>
    private async Task<IRenderedComponent<BoardPage>> BoardWithItems(BunitContext ctx, int extra, bool twoProjects = false)
    {
        for (int i = 0; i < extra; i++) WorkItemsApi.Create("Card " + i);
        if (twoProjects)
        {
            Workspace ws = ctx.Services.GetRequiredService<Workspace>();
            Assert.True(ws.OpenTab(repo2.Root).Success);
            Assert.True(ws.Switch(repo.Root).Success);
        }
        IRenderedComponent<BoardPage> board = ctx.Render<BoardPage>();
        nav!.Find("button.nav-toggle").Click();
        board.WaitForAssertion(() => Assert.True(board.FindAll("article.card").Count >= extra));
        await board.InvokeAsync(() => { });
        await board.InvokeAsync(() => { });
        return board;
    }

    // two fixture items
    private const int A = 123, B = 107;

    private static IElement Card(IRenderedComponent<BoardPage> board, int id) => board.FindAll("article.card").First(c => c.GetAttribute("data-id") == id.ToString());

    /// <summary>The draws so far of the page, the cards and the menus.</summary>
    private readonly record struct Counts(int Page, int Cards, int Menus)
    {
        public static Counts Now() => new(RenderCounts.Of<BoardPage>(), RenderCounts.Of<WorkItemCard>(), RenderCounts.Of<Menu>());
    }

    [Fact(DisplayName = "Examining a card redraws only that card and the one it left")]
    public async Task ExaminingACardRedrawsOnlyThatCardAndTheOneItLeft()
    {
        await using BunitContext ctx = NewContext();
        IRenderedComponent<BoardPage> board = await BoardWithItems(ctx, 30);
        Assert.True(board.FindAll("article.card").Count >= 30);

        Counts before = Counts.Now();
        await board.InvokeAsync(() => Card(board, A).Click());
        board.WaitForAssertion(() => Assert.Contains("ex-on", Card(board, A).ClassName));
        await board.InvokeAsync(() => { });
        Counts first = Counts.Now();
        Assert.Equal(1, first.Page - before.Page);
        Assert.Equal(1, first.Cards - before.Cards);   // the one examined

        await board.InvokeAsync(() => Card(board, B).Click());
        board.WaitForAssertion(() => Assert.Contains("ex-on", Card(board, B).ClassName));
        await board.InvokeAsync(() => { });
        Counts second = Counts.Now();
        Assert.Equal(1, second.Page - first.Page);
        Assert.Equal(2, second.Cards - first.Cards);   // the new one, and the one it left
        Assert.DoesNotContain("ex-on", Card(board, A).ClassName);

        await board.InvokeAsync(() => board.Find(".board-wrap").Click());   // let go: the examined card alone
        board.WaitForAssertion(() => Assert.DoesNotContain("ex-on", Card(board, B).ClassName));
        await board.InvokeAsync(() => { });
        Assert.Equal(1, Counts.Now().Cards - second.Cards);
    }

    [Fact(DisplayName = "Shift-clicking a card redraws only that card")]
    public async Task ShiftClickingACardRedrawsOnlyThatCard()
    {
        await using BunitContext ctx = NewContext();
        IRenderedComponent<BoardPage> board = await BoardWithItems(ctx, 30);

        Counts before = Counts.Now();
        await board.InvokeAsync(() => Card(board, A).Click(new MouseEventArgs { ShiftKey = true }));
        board.WaitForAssertion(() => Assert.Contains("selected", Card(board, A).ClassName));
        await board.InvokeAsync(() => { });
        Counts after = Counts.Now();
        Assert.Equal(1, after.Page - before.Page);
        Assert.Equal(1, after.Cards - before.Cards);

        await board.InvokeAsync(() => Card(board, B).Click(new MouseEventArgs { ShiftKey = true }));   // a second one, and then letting go of the first
        board.WaitForAssertion(() => Assert.Contains("selected", Card(board, B).ClassName));
        await board.InvokeAsync(() => Card(board, A).Click(new MouseEventArgs { ShiftKey = true }));
        board.WaitForAssertion(() => Assert.DoesNotContain("selected", Card(board, A).ClassName));
        await board.InvokeAsync(() => { });
        Assert.Equal(2, Counts.Now().Cards - after.Cards);   // one per click
    }

    [Fact(DisplayName = "Opening a column menu does not redraw the cards")]
    public async Task OpeningAColumnMenuDoesNotRedrawTheCards()
    {
        await using BunitContext ctx = NewContext();
        IRenderedComponent<BoardPage> board = await BoardWithItems(ctx, 30, twoProjects: true);
        Assert.NotEmpty(board.FindAll(".head-row .col-h"));   // two projects with the same columns: one shared header row

        Counts before = Counts.Now();
        await board.InvokeAsync(() => board.FindAll(".head-row .menu-activator")[1].Click());
        board.WaitForAssertion(() => Assert.Single(board.FindAll(".menu-pop")));
        await board.InvokeAsync(() => { });

        Assert.Equal(0, RenderCounts.Of<WorkItemCard>() - before.Cards);
        Assert.Equal(0, RenderCounts.Of<BoardPage>() - before.Page);

        await board.InvokeAsync(() => board.Find(".menu-backdrop").Click());   // closing it costs the same
        board.WaitForAssertion(() => Assert.Empty(board.FindAll(".menu-pop")));
        await board.InvokeAsync(() => { });
        Assert.Equal(0, RenderCounts.Of<WorkItemCard>() - before.Cards);
        Assert.Equal(0, RenderCounts.Of<BoardPage>() - before.Page);
    }

    [Fact(DisplayName = "A plain click on a project name redraws nothing")]
    public async Task APlainClickOnAProjectNameRedrawsNothing()
    {
        await using BunitContext ctx = NewContext();
        IRenderedComponent<BoardPage> board = await BoardWithItems(ctx, 30, twoProjects: true);

        Counts before = Counts.Now();
        await board.InvokeAsync(() => board.FindAll(".proj-h .proj-name-btn")[0].Click());
        board.WaitForAssertion(() => Assert.Single(board.FindAll(".menu-pop")));
        await board.InvokeAsync(() => board.Find(".menu-backdrop").Click());
        board.WaitForAssertion(() => Assert.Empty(board.FindAll(".menu-pop")));
        await board.InvokeAsync(() => board.FindAll(".proj-h")[1].Click());   // and the heading's own space
        await board.InvokeAsync(() => { });

        Assert.Equal(0, RenderCounts.Of<WorkItemCard>() - before.Cards);
        Assert.Equal(0, RenderCounts.Of<BoardPage>() - before.Page);
    }

    [Fact(DisplayName = "A plain click on a swimlane heading redraws nothing")]
    public async Task APlainClickOnASwimlaneHeadingRedrawsNothing()
    {
        await using BunitContext ctx = NewContext();
        ctx.Services.GetRequiredService<SettingsStore>().SaveBoard(repo.Root, ctx.Services.GetRequiredService<SettingsStore>().Board(repo.Root) with { Swimlane = "label" });
        IRenderedComponent<BoardPage> board = await BoardWithItems(ctx, 10);
        Assert.NotEmpty(board.FindAll(".swim-h"));

        Counts before = Counts.Now();
        await board.InvokeAsync(() => board.FindAll(".swim-h")[0].Click());
        await board.InvokeAsync(() => { });

        Assert.Equal(0, RenderCounts.Of<WorkItemCard>() - before.Cards);
        Assert.Equal(0, RenderCounts.Of<BoardPage>() - before.Page);

        await board.InvokeAsync(() => board.FindAll(".swim-h")[0].Click(new MouseEventArgs { ShiftKey = true }));   // a Shift-click does select it
        board.WaitForAssertion(() => Assert.Contains("selected", board.FindAll(".swim-h")[0].ClassName));
    }

    public void Dispose()
    {
        repo.Dispose();
        repo2.Dispose();
        File.Delete(settingsFile);
    }
}
