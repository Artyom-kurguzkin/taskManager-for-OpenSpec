using System.Text.Json;
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
/// Reads the marks the list's <c>RowMarks</c> component carries (one style block outside the grid: the selected, examined and current rows), the way
/// app.js does. The grid's rows carry no mark of their own: a selection or an examine must not render them.
/// </summary>
internal static class RowMarkProbe
{
    private static string[] Keys<T>(IRenderedComponent<T> board, string attribute) where T : Microsoft.AspNetCore.Components.IComponent =>
        board.FindAll("style[data-rowmarks]").SingleOrDefault()?.GetAttribute(attribute) is string json ? JsonSerializer.Deserialize<string[]>(json)! : [];

    private static bool Has<T>(IRenderedComponent<T> board, IElement row, string attribute) where T : Microsoft.AspNetCore.Components.IComponent =>
        row.GetAttribute("data-row") is string key && Keys(board, attribute).Contains(key);

    /// <summary>A list row Shift-clicked: what app.js reads as <c>data-selected</c> before.</summary>
    public static bool Selected<T>(IRenderedComponent<T> board, IElement row) where T : Microsoft.AspNetCore.Components.IComponent => Has(board, row, "data-selected");

    /// <summary>A project's group row Shift-clicked (several projects).</summary>
    public static bool GroupSelected<T>(IRenderedComponent<T> board, IElement groupRow) where T : Microsoft.AspNetCore.Components.IComponent =>
        groupRow.GetAttribute("data-group") is string key && Keys(board, "data-selected-groups").Contains(key);

    /// <summary>The examined row (yellow outline).</summary>
    public static bool Examined<T>(IRenderedComponent<T> board, IElement row) where T : Microsoft.AspNetCore.Components.IComponent => Has(board, row, "data-examined");

    /// <summary>A row that waits on the examined one (red) / that the examined one waits on (blue).</summary>
    public static bool Waits<T>(IRenderedComponent<T> board, IElement row) where T : Microsoft.AspNetCore.Components.IComponent => Has(board, row, "data-waits");
    public static bool Needs<T>(IRenderedComponent<T> board, IElement row) where T : Microsoft.AspNetCore.Components.IComponent => Has(board, row, "data-needs");

    /// <summary>The row of the work item open beside the list (split view).</summary>
    public static bool Current<T>(IRenderedComponent<T> board, IElement row) where T : Microsoft.AspNetCore.Components.IComponent => Has(board, row, "data-current");

    /// <summary>Every selected list row's key.</summary>
    public static string[] SelectedKeys<T>(IRenderedComponent<T> board) where T : Microsoft.AspNetCore.Components.IComponent => Keys(board, "data-selected");
}

/// <summary>
/// What a click in the list costs, by what the user does (#wi189): the grid is drawn when the rows change, not when a row is examined or selected.
/// Counted with the components' own render counters, never timed.
/// </summary>
public sealed class ListRenderBudgetTests : IDisposable
{
    private readonly TestRepo repo = new();
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

    private void PickLayout(string name)
    {
        nav!.ClickWhenReady("button[aria-label=Layout]");
        nav.FireWhenReady(".menu-item", e => e.Click(), all => all.Single(i => i.TextContent.Trim() == name));
    }

    /// <summary>The fixture plus <paramref name="extra"/> created items, the menu expanded through its toggle, the list shown and settled.</summary>
    private async Task<IRenderedComponent<BoardPage>> ListWithItems(BunitContext ctx, int extra)
    {
        for (int i = 0; i < extra; i++) WorkItemsApi.Create("Row " + i);
        IRenderedComponent<BoardPage> board = ctx.Render<BoardPage>();
        PickLayout("List");
        nav!.ClickWhenReady("button.nav-toggle");
        board.WaitForAssertion(() => Assert.True(board.FindAll("tr[data-id]").Count >= extra));
        await board.InvokeAsync(() => { });
        return board;
    }

    private static IElement Row(IRenderedComponent<BoardPage> board, int id) => board.FindAll("tr[data-id]").First(r => r.GetAttribute("data-id") == id.ToString());

    private static int GridRows(IRenderedComponent<BoardPage> board) => board.FindAll("tr[data-id], tr[data-empty]").Count;

    /// <summary>The cost counters at one moment: what the grid, the boundary around it, the marks and the pills have drawn so far.</summary>
    private readonly record struct Counts(int RowRenders, int Boundary, int Marks, int Pills, int Page)
    {
        public static Counts Of(IRenderedComponent<BoardPage> board) =>
            new(board.Instance.RowRenders, RenderCounts.Of<RenderBoundary>(), RenderCounts.Of<RowMarks>(), RenderCounts.Of<ListPill>(), RenderCounts.Of<BoardPage>());
    }

    [Fact(DisplayName = "Examining a row in the list renders no grid rows")]
    public async Task ExaminingARowRendersNoGridRows()
    {
        await using BunitContext ctx = NewContext();
        IRenderedComponent<BoardPage> board = await ListWithItems(ctx, 20);
        Counts before = Counts.Of(board);

        Act(board, () => Row(board, 123).QuerySelector(".row-title")!, e => e.Click());
        board.WaitForAssertion(() => Assert.True(RowMarkProbe.Examined(board, Row(board, 123))));
        await board.InvokeAsync(() => { });
        Counts after = Counts.Of(board);

        Assert.Equal(0, after.RowRenders - before.RowRenders);   // not one grid row asked for its attributes again
        Assert.Equal(0, after.Boundary - before.Boundary);       // the grid was not given its parameters again
        Assert.Equal(0, after.Pills - before.Pills);
        Assert.Equal(1, after.Marks - before.Marks);             // the one component that shows it
        Assert.Equal(1, after.Page - before.Page);
        Assert.False(Row(board, 123).HasAttribute("data-examined"));   // the row itself is untouched: the mark is a style rule outside the grid
        Assert.Contains("[data-row=\"" + Row(board, 123).GetAttribute("data-row") + "\"]", board.Find("style[data-rowmarks]").InnerHtml);
    }

    [Fact(DisplayName = "Shift-clicking a row in the list renders no grid rows")]
    public async Task ShiftClickingARowRendersNoGridRows()
    {
        await using BunitContext ctx = NewContext();
        IRenderedComponent<BoardPage> board = await ListWithItems(ctx, 20);
        Counts before = Counts.Of(board);

        Act(board, () => Row(board, 123).QuerySelector("td.num")!, e => e.Click(new MouseEventArgs { ShiftKey = true }));
        board.WaitForAssertion(() => Assert.True(RowMarkProbe.Selected(board, Row(board, 123))));
        await board.InvokeAsync(() => { });
        Counts after = Counts.Of(board);

        Assert.Equal(0, after.RowRenders - before.RowRenders);
        Assert.Equal(0, after.Boundary - before.Boundary);
        Assert.Equal(0, after.Pills - before.Pills);
        Assert.Equal(1, after.Marks - before.Marks);
        Assert.False(Row(board, 123).HasAttribute("data-selected"));

        // and letting go (Shift-click again) is as cheap
        Act(board, () => Row(board, 123).QuerySelector("td.num")!, e => e.Click(new MouseEventArgs { ShiftKey = true }));
        board.WaitForAssertion(() => Assert.False(RowMarkProbe.Selected(board, Row(board, 123))));
        await board.InvokeAsync(() => { });
        Assert.Equal(0, Counts.Of(board).RowRenders - before.RowRenders);
    }

    [Fact(DisplayName = "A status change re-renders the list once")]
    public async Task AStatusChangeReRendersTheListOnce()
    {
        await using BunitContext ctx = NewContext();
        IRenderedComponent<BoardPage> board = await ListWithItems(ctx, 20);
        Counts before = Counts.Of(board);

        Act(board, () => Row(board, 107).QuerySelector("button[data-pill=status]")!, e => e.Click());
        board.FireWhenReady(".menu-pop .menu-item", e => e.Click(), all => all.Single(i => i.TextContent.Trim() == "Blocked"));
        board.WaitForAssertion(() => Assert.Equal("Blocked", WorkItemsApi.Get(107)!.Status));
        board.WaitForAssertion(() => Assert.Empty(board.FindAll(".menu-pop")));
        board.WaitForAssertion(() => Assert.Contains("Blocked", Row(board, 107).TextContent));
        await board.InvokeAsync(() => { });
        Counts after = Counts.Of(board);

        Assert.Equal(1, after.Boundary - before.Boundary);   // one new set of rows, one draw of the grid for it
        Assert.Equal(2 * GridRows(board), after.RowRenders - before.RowRenders);   // Radzen's two passes over that one draw
    }

    [Fact(DisplayName = "A label pill renders once before the write")]
    public async Task ALabelPillRendersOnceBeforeTheWrite()
    {
        await using BunitContext ctx = NewContext();
        IRenderedComponent<BoardPage> board = await ListWithItems(ctx, 20);
        Counts before = Counts.Of(board);
        OpenSpecStore.FolderLock.WaitLimit.Value = TimeSpan.FromSeconds(10);   // the write waits for the holder; the test releases it long before
        IDisposable held = OpenSpecStore.FolderLock.Hold(repo.Root, WorkItemsApi.Get(107)!.Folder);   // the write of the label waits on the pool
        try
        {
            Act(board, () => Row(board, 107).QuerySelector("button[data-pill=label]")!, e => e.Click());
            board.FireWhenReady(".menu-pop .menu-item", e => e.Click(), all => all.Single(i => i.TextContent.Trim() == "bug"));
            board.WaitForAssertion(() => Assert.Empty(board.FindAll(".menu-pop")));
            await board.InvokeAsync(() => { });

            Assert.Contains("bug", Row(board, 107).QuerySelector("button[data-pill=label]")!.TextContent);   // shown ...
            Assert.Null(WorkItemsApi.Get(107)!.Label);   // ... before the write
            Assert.Equal(1, Counts.Of(board).Boundary - before.Boundary);
        }
        finally { held.Dispose(); }

        board.WaitForAssertion(() => Assert.Equal("bug", WorkItemsApi.Get(107)!.Label));
        await ctx.Services.GetRequiredService<Workspace>().Resolved;
        await board.InvokeAsync(() => { });
        Assert.Equal(1, Counts.Of(board).Boundary - before.Boundary);   // the write's echo drew nothing more
    }

    [Fact(DisplayName = "Switching to the list builds each pill once")]
    public async Task SwitchingToTheListBuildsEachPillOnce()
    {
        await using BunitContext ctx = NewContext();
        for (int i = 0; i < 20; i++) WorkItemsApi.Create("Row " + i);
        IRenderedComponent<BoardPage> board = ctx.Render<BoardPage>();
        Assert.Empty(board.FindAll("tr[data-id]"));   // the board

        PickLayout("List");
        board.WaitForAssertion(() => Assert.True(board.FindAll("tr[data-id]").Count >= 20));
        await board.InvokeAsync(() => { });
        await board.InvokeAsync(() => { });

        int pills = board.FindComponents<ListPill>().Count;
        Assert.True(pills >= 60);
        Assert.Equal(pills, RenderCounts.Of<ListPill>());   // each one drawn once: none built again for Radzen's second pass over the rows
    }

    [Fact(DisplayName = "Switching to the list draws the grid once and leaves no second draw behind")]
    public async Task SwitchingToTheListLeavesNoSecondDrawBehind()
    {
        await using BunitContext ctx = NewContext();
        IRenderedComponent<BoardPage> board = await ListWithItems(ctx, 20);

        // Radzen's two passes over the rows of the one draw; the page's after-render step had nothing to collapse or expand, so it asked for no draw of its own
        Assert.Equal(2 * GridRows(board), board.Instance.RowRenders);
    }

    [Fact(DisplayName = "ArrowUp renders the page once and reads no repository folder")]
    public async Task ArrowUpRendersThePageOnceAndReadsNothing()
    {
        await using BunitContext ctx = NewContext();
        IRenderedComponent<BoardPage> board = await ListWithItems(ctx, 20);
        List<int> shown = [.. board.FindAll("tr[data-id]").Select(r => int.Parse(r.GetAttribute("data-id")!))];
        int pick = shown[shown.Count / 2], above = shown[shown.Count / 2 - 1];
        Act(board, () => Row(board, pick).QuerySelector("td.num")!, e => e.Click(new MouseEventArgs { ShiftKey = true }));
        board.WaitForAssertion(() => Assert.True(RowMarkProbe.Selected(board, Row(board, pick))));
        await board.InvokeAsync(() => { });
        (int reads, int loads, int folders, int page) = (board.Instance.Reads, board.Instance.Loads, ItemsCache.FolderReads, RenderCounts.Of<BoardPage>());

        board.KeyDownWhenReady(".tbl-wrap", "ArrowUp");
        board.WaitForAssertion(() => Assert.Equal(pick, int.Parse(board.FindAll("tr[data-id]")[shown.Count / 2 - 1].GetAttribute("data-id")!)));
        await board.InvokeAsync(() => { });

        Assert.Equal(above, int.Parse(board.FindAll("tr[data-id]")[shown.Count / 2].GetAttribute("data-id")!));   // swapped with the row above
        Assert.Equal((reads, loads, folders), (board.Instance.Reads, board.Instance.Loads, ItemsCache.FolderReads));   // the order is arranged from what the page holds
        Assert.Equal(1, RenderCounts.Of<BoardPage>() - page);
    }

    [Fact(DisplayName = "A render-budget test's store runs no real file watcher")]
    public async Task ARenderBudgetTestsStoreRunsNoRealFileWatcher()
    {
        await using BunitContext ctx = NewContext();
        Assert.Null(OpenSpecStore.OpenSpecStoreModule.Current!.Watcher);   // a late event from the disk would reload the page between two counts

        OpenSpecStore.OpenSpecStoreApi.Close();   // the seam works both ways: a test that is about the watcher switches it back on
        OpenSpecStore.OpenSpecStoreModule.State.RealWatcher = true;
        OpenSpecStore.OpenSpecStoreApi.Open(repo.Root);
        Assert.NotNull(OpenSpecStore.OpenSpecStoreModule.Current!.Watcher);
    }

    public void Dispose()
    {
        repo.Dispose();
        File.Delete(settingsFile);
    }
    private static void Act<T>(Bunit.IRenderedComponent<T> cut, Func<AngleSharp.Dom.IElement?> find, Action<AngleSharp.Dom.IElement> fire) where T : Microsoft.AspNetCore.Components.IComponent
    {
        cut.Eventually(() => Xunit.Assert.NotNull(find()));   // rendered and present, or the test fails
        cut.InvokeAsync(() => fire(find()!)).GetAwaiter().GetResult();
    }
}
