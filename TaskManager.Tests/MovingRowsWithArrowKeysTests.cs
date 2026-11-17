using Bunit;
using Radzen;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using TaskManager.Components.Pages;
using TaskManager.Components.Layout;
using TaskManager.Components.Shared;
using TaskManager.AppState;
using TaskManager.BackgroundJobs;
using TaskManager.Helpers;
using WorkItems;

namespace TaskManager.Tests;

/// <summary>#wi194: an arrow key on a list row swaps it with the row shown next to it, and nothing else changes place.</summary>
public sealed class MovingRowsWithArrowKeysTests : IDisposable
{
    private readonly TestRepo repo = new();
    private readonly string settingsFile = Path.Combine(Path.GetTempPath(), "rtm-ui-" + Guid.NewGuid().ToString("N") + ".json");

    private BunitContext NewContext()
    {
        BunitContext ctx = new();
        ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        ctx.Services.AddSingleton<UiService>();
        ctx.Services.AddRadzenComponents();
        ctx.Services.AddSingleton(new SettingsStore(settingsFile));
        ctx.Services.AddSingleton<Workspace>();
        Assert.True(ctx.Services.GetRequiredService<Workspace>().Open(repo.Root).Success);
        ctx.Render<Overlays>();
        return ctx;
    }

    /// <summary>Four Planned items shown high, high, low, low (the default sort: priority, then the id); returns their ids in that order.</summary>
    private static int[] FourMixed(BunitContext ctx)
    {
        Workspace ws = ctx.Services.GetRequiredService<Workspace>();
        int[] ids = [.. Enumerable.Range(0, 4).Select(_ => ws.CreateItem("Planned").Id)];
        Assert.True(WorkItemsApi.SetPriority(ids[0], "high").Success);
        Assert.True(WorkItemsApi.SetPriority(ids[1], "high").Success);
        Assert.True(WorkItemsApi.SetPriority(ids[2], "low").Success);
        Assert.True(WorkItemsApi.SetPriority(ids[3], "low").Success);
        return ids;
    }

    private async Task<IRenderedComponent<BoardPage>> ListAsync(BunitContext ctx, string groupBy)
    {
        IRenderedComponent<BoardPage> board = ctx.Render<BoardPage>();
        IRenderedComponent<SideNav> nav = ctx.Render<SideNav>();
        nav.Find("button[aria-label=Layout]").Click();
        nav.FindAll(".menu-item").Single(i => i.TextContent.Trim() == "List").Click();
        SettingsStore settings = ctx.Services.GetRequiredService<SettingsStore>();
        settings.SaveBoard(repo.Root, settings.Board(repo.Root) with { ListGroupBy = groupBy, ListSwimlane = "none" });
        await board.InvokeAsync(() => { });
        board.WaitForAssertion(() => Assert.NotEmpty(board.FindAll("tr[data-id]")));
        return board;
    }

    private static List<int> Shown(IRenderedComponent<BoardPage> board) =>
        [.. board.FindAll("tr[data-id]").Select(r => int.Parse(r.GetAttribute("data-id")!))];

    /// <summary>The group headers and rows as drawn, top to bottom: G:<group key> and row ids, for a failure message.</summary>
    private static string Layout(IRenderedComponent<BoardPage> board) =>
        string.Join(' ', board.FindAll("tr[data-group], tr[data-id]").Select(r => r.GetAttribute("data-id") ?? "G:" + r.GetAttribute("data-group")));

    /// <summary>The row ids under each group header, by the header's data-group ("" when the list is flat).</summary>
    private static Dictionary<string, List<int>> Groups(IRenderedComponent<BoardPage> board)
    {
        Dictionary<string, List<int>> groups = [];
        string current = "";
        foreach (AngleSharp.Dom.IElement r in board.FindAll("tr[data-group], tr[data-id]"))
        {
            if (r.GetAttribute("data-id") is string id) { if (!groups.TryAdd(current, [int.Parse(id)])) groups[current].Add(int.Parse(id)); }
            else groups.TryAdd(current = r.GetAttribute("data-group")!, []);
        }
        return groups;
    }

    private static async Task Press(IRenderedComponent<BoardPage> board, int selected, string key)
    {
        await board.InvokeAsync(() => board.FindAll("tr[data-id]").First(r => r.GetAttribute("data-id") == selected.ToString()).QuerySelector("td.num")!.Click(new MouseEventArgs { ShiftKey = true }));
        await board.InvokeAsync(() => board.Find(".tbl-wrap").KeyDown(key));
    }

    [Theory(DisplayName = "In the list, an arrow key swaps the selected row with the row shown next to it, across the priority groups, and no other row moves")]
    [InlineData("status", 2, "ArrowUp")]
    [InlineData("status", 1, "ArrowDown")]
    [InlineData("none", 2, "ArrowUp")]
    [InlineData("none", 1, "ArrowDown")]
    [InlineData("none", 3, "ArrowUp")]
    public async Task ArrowSwapsWithTheNeighbourShown(string groupBy, int pick, string key)
    {
        await using BunitContext ctx = NewContext();
        int[] ids = FourMixed(ctx);
        IRenderedComponent<BoardPage> board = await ListAsync(ctx, groupBy);
        List<int> before = Shown(board);
        int at = before.IndexOf(ids[pick]), to = at + (key == "ArrowUp" ? -1 : 1);
        List<int> expected = [.. before];
        (expected[at], expected[to]) = (expected[to], expected[at]);

        await Press(board, ids[pick], key);

        board.WaitForAssertion(() => Assert.Equal(expected, Shown(board)));
    }

    [Theory(DisplayName = "In a list sorted by one of its columns, an arrow key swaps the selected row with the row shown next to it, and no other row moves")]
    [InlineData("none", "ArrowUp")]
    [InlineData("none", "ArrowDown")]
    public async Task ArrowStepsInTheShownOrderOfASortedList(string groupBy, string key)
    {
        await using BunitContext ctx = NewContext();
        int[] ids = FourMixed(ctx);
        IRenderedComponent<BoardPage> board = await ListAsync(ctx, groupBy);
        await board.InvokeAsync(() => board.FindAll("th.rz-sortable-column").First(h => h.TextContent.Contains("ID")).QuerySelector("div")!.Click());   // the grid's own sort: by number, which is not the board's order
        board.WaitForAssertion(() => Assert.Equal(Shown(board).Order(), Shown(board)));
        List<int> before = Shown(board);
        int pick = ids[1], at = before.IndexOf(pick), to = at + (key == "ArrowUp" ? -1 : 1);
        List<int> expected = [.. before];
        (expected[at], expected[to]) = (expected[to], expected[at]);

        await Press(board, pick, key);

        board.WaitForAssertion(() => Assert.Equal(expected, Shown(board)));
    }

    [Theory(DisplayName = "In a group of six rows mixing high and low priority, an arrow key on a middle row swaps it with the row shown next to it, with or without a saved order that holds other columns' cards")]
    [InlineData(false, "ArrowUp")]
    [InlineData(true, "ArrowUp")]
    [InlineData(true, "ArrowDown")]
    public async Task ArrowOnAMiddleRowOfAMixedGroup(bool staleOrder, string key)
    {
        await using BunitContext ctx = NewContext();
        Workspace ws = ctx.Services.GetRequiredService<Workspace>();
        int[] ids = [.. Enumerable.Range(0, 6).Select(_ => ws.CreateItem("Planned").Id)];
        for (int n = 0; n < ids.Length; n++) Assert.True(WorkItemsApi.SetPriority(ids[n], n % 2 == 0 ? "high" : "low").Success);
        IRenderedComponent<BoardPage> board = await ListAsync(ctx, "status");
        if (staleOrder)   // my order left by moves in another column: it names cards that are not in this group
        {
            SettingsStore settings = ctx.Services.GetRequiredService<SettingsStore>();
            settings.SaveBoard(repo.Root, settings.Board(repo.Root) with { SortBy = "manual", Order = ["item:107", .. ids.Take(2).Select(BoardModel.ItemKey)] });
            await board.InvokeAsync(() => { });
        }
        List<int> before = Shown(board);
        int pick = before.Where(ids.Contains).ElementAt(3), at = before.IndexOf(pick), to = at + (key == "ArrowUp" ? -1 : 1);   // the fourth of the six as shown, by position: which id that is depends on the creation seconds (equal ones tie on the id, a later one sorts first), and the last row of a group goes on into the next group
        List<int> expected = [.. before];
        (expected[at], expected[to]) = (expected[to], expected[at]);

        await Press(board, pick, key);

        board.WaitForAssertion(() => Assert.Equal(expected, Shown(board)));
    }

    /// <summary>
    /// Six Planned items alternating high and low, each with its own Modified: the work item's events.log is written with a fixed time per item (item n at 2040-01-0(n+1) 10:00),
    /// so the order never depends on the wall-clock second the items were made in. Returns their ids, oldest Modified first.
    /// </summary>
    private static int[] SixMixedWithDistinctModified(BunitContext ctx)
    {
        Workspace ws = ctx.Services.GetRequiredService<Workspace>();
        int[] ids = [.. Enumerable.Range(0, 6).Select(_ => ws.CreateItem("Planned").Id)];
        for (int n = 0; n < ids.Length; n++) Assert.True(WorkItemsApi.SetPriority(ids[n], n % 2 == 0 ? "high" : "low").Success);
        for (int n = 0; n < ids.Length; n++)
            Assert.True(OpenSpecStore.OpenSpecStoreApi.WriteText(Path.Combine(WorkItemsApi.Get(ids[n])!.Path, "events.log"), $"2040-01-0{n + 1}T10:00:00 | test | created | item {n}\n").Success);   // the store's own write: it reads the folder again, a file written behind its back is seen only by the watcher
        return ids;
    }

    [Theory(DisplayName = "In a group of mixed priority, with or without the grid's header sort on Updated, an arrow key on a middle row swaps it with the row shown next to it and no other row moves")]
    [InlineData("none", false, "ArrowUp")]
    [InlineData("none", false, "ArrowDown")]
    [InlineData("none", true, "ArrowUp")]
    [InlineData("none", true, "ArrowDown")]
    [InlineData("status", false, "ArrowUp")]
    [InlineData("status", false, "ArrowDown")]
    [InlineData("status", true, "ArrowUp")]
    [InlineData("status", true, "ArrowDown")]
    public async Task ArrowOnAMiddleRowOfAMixedGroupInEverySort(string groupBy, bool headerSort, string key)
    {
        await using BunitContext ctx = NewContext();
        int[] ids = SixMixedWithDistinctModified(ctx);
        IRenderedComponent<BoardPage> board = await ListAsync(ctx, groupBy);
        if (headerSort)
        {
            await board.InvokeAsync(() => board.FindAll("th.rz-sortable-column").First(h => h.TextContent.Contains("Updated")).QuerySelector("div")!.Click());
            board.WaitForAssertion(() => Assert.Equal(ids, Shown(board).Where(ids.Contains)));   // oldest first: not the board's order (high first, newest first), so the shown order and the internal one differ
        }
        else board.WaitForAssertion(() => Assert.Equal([ids[4], ids[2], ids[0], ids[5], ids[3], ids[1]], Shown(board).Where(ids.Contains)));   // the default: high first, each newest first
        Dictionary<string, List<int>> before = Groups(board);
        string layoutBefore = Layout(board);
        int pick = Shown(board).Where(ids.Contains).ElementAt(3);   // the fourth of the six as shown: two of them are above it and two below, so it is never at the edge of its group
        string home = before.Single(g => g.Value.Contains(pick)).Key;
        List<int> expected = [.. before[home]];
        int at = expected.IndexOf(pick), to = at + (key == "ArrowUp" ? -1 : 1);
        (expected[at], expected[to]) = (expected[to], expected[at]);

        await Press(board, pick, key);

        // Only the pressed row and its neighbour changed places. A group keeps its rows; the order of the groups themselves is not the assertion: dropping the grid's header sort puts them back in the board's order.
        board.WaitForAssertion(() =>
        {
            Dictionary<string, List<int>> after = Groups(board);
            string why = string.Join(Environment.NewLine, $"pressed {key} on {pick} in group '{home}'", "before: " + layoutBefore, "after:  " + Layout(board));
            Assert.True(before.Keys.Order().SequenceEqual(after.Keys.Order()), why);
            foreach ((string group, List<int> rows) in before)
                Assert.True((group == home ? expected : rows).SequenceEqual(after[group]), why);
        }, TimeSpan.FromSeconds(5));
    }

    private static async Task SortByUpdated(IRenderedComponent<BoardPage> board, int[] ids)
    {
        await board.InvokeAsync(() => board.FindAll("th.rz-sortable-column").First(h => h.TextContent.Contains("Updated")).QuerySelector("div")!.Click());
        board.WaitForAssertion(() => Assert.All(Groups(board).Values, g => Assert.Equal(g.Where(ids.Contains).Order(), g.Where(ids.Contains))));   // oldest first within each group: not the board's order (newest first)
        Assert.NotNull(UpdatedSort(board));
    }

    private static string? UpdatedSort(IRenderedComponent<BoardPage> board) =>
        board.FindAll("th.rz-sortable-column").First(h => h.TextContent.Contains("Updated")).GetAttribute("aria-sort");

    [Theory(DisplayName = "With the grid's header sort on, an arrow key that has nowhere to move a row changes nothing: the shown order stays and the sort stays on")]
    [InlineData("none", "ArrowUp")]
    [InlineData("none", "ArrowDown")]
    [InlineData("priority", "ArrowUp")]
    [InlineData("priority", "ArrowDown")]
    public async Task ArrowAtTheEndOfASortedListChangesNothing(string groupBy, string key)
    {
        await using BunitContext ctx = NewContext();
        int[] ids = SixMixedWithDistinctModified(ctx);
        IRenderedComponent<BoardPage> board = await ListAsync(ctx, groupBy);
        await SortByUpdated(board, ids);
        string layoutBefore = Layout(board);
        string? sortBefore = UpdatedSort(board);
        Assert.NotNull(sortBefore);
        SettingsStore settings = ctx.Services.GetRequiredService<SettingsStore>();
        string? savedBefore = settings.Board(repo.Root).ListSettings;   // the grid's sort is saved with the board
        Assert.Contains("SortOrder\":0", savedBefore);
        int pick = key == "ArrowUp" ? Shown(board).First() : Shown(board).Last();   // the first row shown (nothing above it) or the last (nothing below it)

        await Press(board, pick, key);
        await board.InvokeAsync(() => { });

        Assert.Equal(layoutBefore, Layout(board));
        Assert.Equal(sortBefore, UpdatedSort(board));
        Assert.Equal(savedBefore, settings.Board(repo.Root).ListSettings);   // nothing was dropped or saved
    }

    [Theory(DisplayName = "With the grid's header sort on, a row first in the board's order but shown last in its group swaps with the row shown above it on Up, and the same on Down for the one shown first")]
    [InlineData("ArrowUp")]
    [InlineData("ArrowDown")]
    public async Task ArrowSwapsInTheShownOrderWhereTheBoardOrderIsAtTheEdge(string key)
    {
        await using BunitContext ctx = NewContext();
        int[] ids = SixMixedWithDistinctModified(ctx);   // the board shows each group newest first, the header sort oldest first: the newest row of a group is first in the board's order and last shown
        IRenderedComponent<BoardPage> board = await ListAsync(ctx, "priority");
        await SortByUpdated(board, ids);
        Dictionary<string, List<int>> before = Groups(board);
        string layoutBefore = Layout(board);
        string home = key == "ArrowUp" ? before.Keys.Last() : before.Keys.First();   // the last group (low) for Up: the group above it is where the bug sends the row; the first (high) for Down
        int pick = key == "ArrowUp" ? before[home].Last() : before[home].First();
        List<int> expected = [.. before[home]];
        int at = expected.IndexOf(pick), to = at + (key == "ArrowUp" ? -1 : 1);
        (expected[at], expected[to]) = (expected[to], expected[at]);

        await Press(board, pick, key);

        board.WaitForAssertion(() =>
        {
            Dictionary<string, List<int>> after = Groups(board);
            string why = string.Join(Environment.NewLine, $"pressed {key} on {pick} in '{home}'", "before: " + layoutBefore, "after:  " + Layout(board));
            foreach ((string group, List<int> rows) in before)
                Assert.True((group == home ? expected : rows).SequenceEqual(after[group]), why);
        }, TimeSpan.FromSeconds(5));
    }

    [Theory(DisplayName = "With no header sort, an arrow key that takes a row across a group edge moves that row only: every other group keeps its shown order, whatever the saved order holds")]
    [InlineData("ArrowUp")]
    [InlineData("ArrowDown")]
    public async Task ArrowAcrossAGroupEdgeKeepsTheOtherRowsPlaces(string key)
    {
        await using BunitContext ctx = NewContext();
        int[] ids = SixMixedWithDistinctModified(ctx);
        IRenderedComponent<BoardPage> board = await ListAsync(ctx, "priority");
        Dictionary<string, List<int>> before = Groups(board);
        string high = before.Keys.First(), low = before.Keys.Last();
        string source = key == "ArrowUp" ? low : high;   // the group the row leaves
        int pick = key == "ArrowUp" ? before[low].First() : before[high].Last();
        SettingsStore settings = ctx.Services.GetRequiredService<SettingsStore>();
        settings.SaveBoard(repo.Root, settings.Board(repo.Root) with { Order = [BoardModel.ItemKey(before[source][key == "ArrowUp" ? 2 : 1])] });   // a saved order left by moves elsewhere: it names a row that is not first in the group the key leaves
        await board.InvokeAsync(() => { });
        board.WaitForAssertion(() => Assert.Equal(before, Groups(board)));
        List<int> expectedHigh = key == "ArrowUp" ? [.. before[high], pick] : [.. before[high].Where(i => i != pick)];
        List<int> expectedLow = key == "ArrowUp" ? [.. before[low].Where(i => i != pick)] : [pick, .. before[low]];

        await Press(board, pick, key);

        board.WaitForAssertion(() =>
        {
            Dictionary<string, List<int>> after = Groups(board);
            string why = $"pressed {key} on {pick}, after: {Layout(board)}";
            Assert.True(expectedHigh.SequenceEqual(after[high]), why + " | after high: " + string.Join(' ', after[high]));
            Assert.True(expectedLow.SequenceEqual(after[low]), why + " | after low: " + string.Join(' ', after[low]));
        }, TimeSpan.FromSeconds(5));
    }
    /// <summary>The group of the archived row (the sample's one, in Completed) and its first row: an archived work item can't change status, so it can't leave a status group.</summary>
    private static (string Group, int Row) ArchivedFirst(IRenderedComponent<BoardPage> board)
    {
        KeyValuePair<string, List<int>> g = Groups(board).Single(x => x.Value.Any(id => WorkItemsApi.Get(id)!.Archived));
        return (g.Key, g.Value.First(id => WorkItemsApi.Get(id)!.Archived));
    }

    [Fact(DisplayName = "Up on the first row of a group that cannot leave (archived) with a header sort on changes nothing: the shown order and the sort stay")]
    public async Task UpOnAnArchivedRowWithAHeaderSortChangesNothing()
    {
        await using BunitContext ctx = NewContext();
        int[] ids = SixMixedWithDistinctModified(ctx);
        IRenderedComponent<BoardPage> board = await ListAsync(ctx, "status");
        await SortByUpdated(board, ids);
        string layoutBefore = Layout(board);
        string? sortBefore = UpdatedSort(board);
        SettingsStore settings = ctx.Services.GetRequiredService<SettingsStore>();
        BoardOptions savedBefore = settings.Board(repo.Root);
        Assert.Contains("SortOrder\":0", savedBefore.ListSettings);
        (string _, int pick) = ArchivedFirst(board);

        await Press(board, pick, "ArrowUp");
        await board.InvokeAsync(() => { });

        Assert.Equal(layoutBefore, Layout(board));
        Assert.Equal(sortBefore, UpdatedSort(board));
        Assert.Equal(savedBefore.ListSettings, settings.Board(repo.Root).ListSettings);
        Assert.Equal(savedBefore.Order, settings.Board(repo.Root).Order);
        Assert.Equal(savedBefore.SortBy, settings.Board(repo.Root).SortBy);
    }

    [Fact(DisplayName = "With no header sort, a refused cross-group move leaves the saved order and the shown order unchanged")]
    public async Task ARefusedCrossGroupMoveLeavesTheOrderAlone()
    {
        await using BunitContext ctx = NewContext();
        SixMixedWithDistinctModified(ctx);
        IRenderedComponent<BoardPage> board = await ListAsync(ctx, "status");
        string layoutBefore = Layout(board);
        SettingsStore settings = ctx.Services.GetRequiredService<SettingsStore>();
        BoardOptions savedBefore = settings.Board(repo.Root);
        (string _, int pick) = ArchivedFirst(board);

        await Press(board, pick, "ArrowUp");
        await board.InvokeAsync(() => { });

        Assert.Equal(layoutBefore, Layout(board));
        Assert.Equal(savedBefore.Order, settings.Board(repo.Root).Order);
        Assert.Equal(savedBefore.SortBy, settings.Board(repo.Root).SortBy);
    }

    [Fact(DisplayName = "After an arrow key took the header sort away, a click on the Updated header sorts the list again")]
    public async Task TheHeaderSortsAgainAfterAnArrowKeyDroppedIt()
    {
        await using BunitContext ctx = NewContext();
        int[] ids = SixMixedWithDistinctModified(ctx);
        IRenderedComponent<BoardPage> board = await ListAsync(ctx, "none");
        await SortByUpdated(board, ids);
        await Press(board, ids[2], "ArrowUp");   // a real move: the sort is dropped
        board.WaitForAssertion(() => Assert.Equal([ids[0], ids[2], ids[1], ids[3], ids[4], ids[5]], Shown(board).Where(ids.Contains)));
        Assert.Null(UpdatedSort(board));

        await board.InvokeAsync(() => board.FindAll("th.rz-sortable-column").First(h => h.TextContent.Contains("Updated")).QuerySelector("div")!.Click());

        board.WaitForAssertion(() =>
        {
            Assert.Equal(ids, Shown(board).Where(ids.Contains));
            Assert.NotNull(UpdatedSort(board));
        }, TimeSpan.FromSeconds(5));
    }

    public void Dispose() => repo.Dispose();
}
