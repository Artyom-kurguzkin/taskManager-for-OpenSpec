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
        nav!.ClickWhenReady("button.nav-toggle");
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
        Act(board, () => Card(board, A), e => e.Click());
        board.WaitForAssertion(() => Assert.Contains("ex-on", Card(board, A).ClassName));
        await board.InvokeAsync(() => { });
        Counts first = Counts.Now();
        Assert.Equal(1, first.Page - before.Page);
        Assert.Equal(1, first.Cards - before.Cards);   // the one examined

        Act(board, () => Card(board, B), e => e.Click());
        board.WaitForAssertion(() => Assert.Contains("ex-on", Card(board, B).ClassName));
        await board.InvokeAsync(() => { });
        Counts second = Counts.Now();
        Assert.Equal(1, second.Page - first.Page);
        Assert.Equal(2, second.Cards - first.Cards);   // the new one, and the one it left
        Assert.DoesNotContain("ex-on", Card(board, A).ClassName);

        board.ClickWhenReady(".board-wrap");   // let go: the examined card alone
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
        Act(board, () => Card(board, A), e => e.Click(new MouseEventArgs { ShiftKey = true }));
        board.WaitForAssertion(() => Assert.Contains("selected", Card(board, A).ClassName));
        await board.InvokeAsync(() => { });
        Counts after = Counts.Now();
        Assert.Equal(1, after.Page - before.Page);
        Assert.Equal(1, after.Cards - before.Cards);

        Act(board, () => Card(board, B), e => e.Click(new MouseEventArgs { ShiftKey = true }));   // a second one, and then letting go of the first
        board.WaitForAssertion(() => Assert.Contains("selected", Card(board, B).ClassName));
        Act(board, () => Card(board, A), e => e.Click(new MouseEventArgs { ShiftKey = true }));
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
        board.FireWhenReady(".head-row .menu-activator", e => e.Click(), all => all[1]);
        board.WaitForAssertion(() => Assert.Single(board.FindAll(".menu-pop")));
        await board.InvokeAsync(() => { });

        Assert.Equal(0, RenderCounts.Of<WorkItemCard>() - before.Cards);
        Assert.Equal(0, RenderCounts.Of<BoardPage>() - before.Page);

        board.ClickWhenReady(".menu-backdrop");   // closing it costs the same
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
        board.FireWhenReady(".proj-h .proj-name-btn", e => e.Click(), all => all[0]);
        board.WaitForAssertion(() => Assert.Single(board.FindAll(".menu-pop")));
        board.ClickWhenReady(".menu-backdrop");
        board.WaitForAssertion(() => Assert.Empty(board.FindAll(".menu-pop")));
        board.FireWhenReady(".proj-h", e => e.Click(), all => all[1]);   // and the heading's own space
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
        board.FireWhenReady(".swim-h", e => e.Click(), all => all[0]);
        await board.InvokeAsync(() => { });

        Assert.Equal(0, RenderCounts.Of<WorkItemCard>() - before.Cards);
        Assert.Equal(0, RenderCounts.Of<BoardPage>() - before.Page);

        Act(board, () => board.FindAll(".swim-h")[0], e => e.Click(new MouseEventArgs { ShiftKey = true }));   // a Shift-click does select it
        board.WaitForAssertion(() => Assert.Contains("selected", board.FindAll(".swim-h")[0].ClassName));
    }

    private static IElement Column(IRenderedComponent<BoardPage> board, string name) =>
        board.FindAll("section.col").Single(c => c.QuerySelector("h3")!.TextContent == name);

    [Fact(DisplayName = "A drop renders once and runs one Load")]
    public async Task ADropRendersOnceAndRunsOneLoad()
    {
        await using BunitContext ctx = NewContext();
        IRenderedComponent<BoardPage> board = await BoardWithItems(ctx, 6);
        int moving = int.Parse(Column(board, "Backlog").QuerySelectorAll("article.card")[0].GetAttribute("data-id")!);
        (int reads, long versions) = (board.Instance.Reads, board.Instance.Versions);

        await board.InvokeAsync(() => Column(board, "Backlog").QuerySelectorAll("article.card")[0].DragStart());
        await board.InvokeAsync(() => Column(board, "In Progress").Drop());
        board.WaitForAssertion(() => Assert.Contains(Column(board, "In Progress").QuerySelectorAll("article.card"), c => c.GetAttribute("data-id") == moving.ToString()));   // shown first ...
        board.WaitForAssertion(() => Assert.Equal(1, board.Instance.Reads - reads));   // ... then written, and read back once
        await board.InvokeAsync(() => { });

        Assert.Equal("In Progress", WorkItemsApi.Get(moving)!.Status);
        Assert.Equal(1, board.Instance.Reads - reads);          // one Load for the whole drop
        Assert.Equal(2, board.Instance.Versions - versions);    // and one stamp bump each for the cards and the grid
    }

    /// <summary>The write lock of an item's change folder, held by the test: the page's write waits on the pool for it, so a step can be checked "before the write" with no timing.</summary>
    private IDisposable HoldWrites(int id)
    {
        OpenSpecStore.FolderLock.WaitLimit.Value = TimeSpan.FromSeconds(10);   // the write waits for the holder; the test releases it long before
        return OpenSpecStore.FolderLock.Hold(repo.Root, WorkItemsApi.Get(id)!.Folder);
    }

    [Fact(DisplayName = "A drop into a feature column renders once before the write and runs one Load")]
    public async Task AFeatureDropRendersOnceBeforeTheWriteAndRunsOneLoad()
    {
        await using BunitContext ctx = NewContext();
        SettingsStore settings = ctx.Services.GetRequiredService<SettingsStore>();
        settings.SaveBoard(repo.Root, settings.Board(repo.Root) with { GroupBy = "feature" });
        IRenderedComponent<BoardPage> board = ctx.Render<BoardPage>();
        await board.InvokeAsync(() => { });
        IElement CardOf(string column) => Column(board, column).QuerySelectorAll("article.card").First(c => c.GetAttribute("data-id") == A.ToString());
        await board.InvokeAsync(() => CardOf("Security").DragStart());
        (int reads, long versions, int page) = (board.Instance.Reads, board.Instance.Versions, RenderCounts.Of<BoardPage>());
        IDisposable held = HoldWrites(A);
        try
        {
            await board.InvokeAsync(() => Column(board, "Banking").Drop());
            await board.InvokeAsync(() => { });

            Assert.Contains(Column(board, "Banking").QuerySelectorAll("article.card"), c => c.GetAttribute("data-id") == A.ToString());   // shown ...
            Assert.DoesNotContain(Column(board, "Security").QuerySelectorAll("article.card"), c => c.GetAttribute("data-id") == A.ToString());
            Assert.Equal(["non-functional/security"], WorkItemsApi.Get(A)!.Features.Select(f => f.Path));   // ... before the write
            Assert.Equal(reads, board.Instance.Reads);
            Assert.Equal(2, board.Instance.Versions - versions);   // one stamp bump each for the cards and the grid
            Assert.Equal(1, RenderCounts.Of<BoardPage>() - page);
        }
        finally { held.Dispose(); }

        board.WaitForAssertion(() => Assert.Equal(["functional/banking"], WorkItemsApi.Get(A)!.Features.Select(f => f.Path)));
        board.WaitForAssertion(() => Assert.Equal(1, board.Instance.Reads - reads));   // written, then read back once
        await board.InvokeAsync(() => { });
        Assert.Equal(1, board.Instance.Reads - reads);
        Assert.Equal(2, board.Instance.Versions - versions);   // the written card is the shown one: nothing drawn again
    }

    [Fact(DisplayName = "A swimlane drop renders once before the write")]
    public async Task ASwimlaneDropRendersOnceBeforeTheWrite()
    {
        await using BunitContext ctx = NewContext();
        Assert.True(WorkItemsApi.SetLabel(B, "bug").Success);   // a "bug" row for the card to cross to
        SettingsStore settings = ctx.Services.GetRequiredService<SettingsStore>();
        settings.SaveBoard(repo.Root, settings.Board(repo.Root) with { Swimlane = "label" });
        IRenderedComponent<BoardPage> board = ctx.Render<BoardPage>();
        await board.InvokeAsync(() => { });
        string status = WorkItemsApi.Get(A)!.Status;
        IElement Cell(string row) => board.Find($".swim[data-swim='{row}'] section.col[data-cell='{status}']");
        bool Has(string row) => Cell(row).QuerySelectorAll("article.card").Any(c => c.GetAttribute("data-id") == A.ToString());
        Assert.True(Has(BoardModel.NoRow));
        await board.InvokeAsync(() => Cell(BoardModel.NoRow).QuerySelectorAll("article.card").First(c => c.GetAttribute("data-id") == A.ToString()).DragStart());
        (int reads, long versions) = (board.Instance.Reads, board.Instance.Versions);
        IDisposable held = HoldWrites(A);
        try
        {
            await board.InvokeAsync(() => Cell("bug").QuerySelector(".lane")!.Drop());
            await board.InvokeAsync(() => { });

            Assert.True(Has("bug"));   // shown in the other row ...
            Assert.False(Has(BoardModel.NoRow));
            Assert.Null(WorkItemsApi.Get(A)!.Label);   // ... before the write
            Assert.Equal(reads, board.Instance.Reads);
            Assert.Equal(2, board.Instance.Versions - versions);
        }
        finally { held.Dispose(); }

        board.WaitForAssertion(() => Assert.Equal("bug", WorkItemsApi.Get(A)!.Label));
        board.WaitForAssertion(() => Assert.Equal(1, board.Instance.Reads - reads));
        await board.InvokeAsync(() => { });
        Assert.Equal(2, board.Instance.Versions - versions);
    }

    [Fact(DisplayName = "The echo of the page's own write bumps no version")]
    public async Task TheEchoOfThePagesOwnWriteBumpsNoVersion()
    {
        await using BunitContext ctx = NewContext();
        IRenderedComponent<BoardPage> board = await BoardWithItems(ctx, 6);
        (int reads, long versions, int cards) = (board.Instance.Reads, board.Instance.Versions, RenderCounts.Of<WorkItemCard>());

        await board.InvokeAsync(() => ctx.Services.GetRequiredService<Workspace>().Do((true, "")));   // the workspace says "changed", as it does after the page's own write
        await board.InvokeAsync(() => { });
        await board.InvokeAsync(() => { });

        Assert.Equal(1, board.Instance.Reads - reads);          // it read ...
        Assert.Equal(versions, board.Instance.Versions);        // ... found what it already shows, and stamped nothing
        Assert.Equal(cards, RenderCounts.Of<WorkItemCard>());
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
