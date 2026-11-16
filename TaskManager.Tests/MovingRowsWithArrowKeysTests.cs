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

    public void Dispose() => repo.Dispose();
}
