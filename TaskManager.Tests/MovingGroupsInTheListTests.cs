using Bunit;
using Radzen;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using TaskManager.Components.Pages;
using TaskManager.Components.Layout;
using TaskManager.Components.Shared;
using TaskManager.AppState;
using WorkItems;

namespace TaskManager.Tests;

/// <summary>#wi153 (#n700): a list's group headers select with Shift-click and move with Up / Down as the board's columns do, and the grid's header sort leaves the group order alone.</summary>
public sealed class MovingGroupsInTheListTests : IDisposable
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

    /// <summary>
    /// Items in three statuses, each with its own Modified (the events.log is written with a fixed time, so no order depends on the wall clock): Backlog holds the newest,
    /// Confirmed the oldest, Planned two in between, a high and a low one. Sorted by Updated, the groups' first rows come in the reverse of the saved group order.
    /// Returns the ids: Backlog, Planned high, Planned low, Confirmed.
    /// </summary>
    private static int[] Items(BunitContext ctx)
    {
        Workspace ws = ctx.Services.GetRequiredService<Workspace>();
        int[] ids = [ws.CreateItem("Backlog").Id, ws.CreateItem("Planned").Id, ws.CreateItem("Planned").Id, ws.CreateItem("Confirmed").Id];
        Assert.True(WorkItemsApi.SetPriority(ids[1], "high").Success);
        Assert.True(WorkItemsApi.SetPriority(ids[2], "low").Success);
        int[] day = [4, 3, 2, 1];
        for (int n = 0; n < ids.Length; n++)
            Assert.True(OpenSpecStore.OpenSpecStoreApi.WriteText(Path.Combine(WorkItemsApi.Get(ids[n])!.Path, "events.log"), $"2040-01-0{day[n]}T10:00:00 | test | created | item {n}\n").Success);
        return ids;
    }

    private async Task<IRenderedComponent<BoardPage>> ListAsync(BunitContext ctx, string groups, string subgroups = "none")
    {
        IRenderedComponent<BoardPage> board = ctx.Render<BoardPage>();
        IRenderedComponent<SideNav> nav = ctx.Render<SideNav>();
        nav.Find("button[aria-label=Layout]").Click();
        nav.FindAll(".menu-item").Single(i => i.TextContent.Trim() == "List").Click();
        SettingsStore settings = ctx.Services.GetRequiredService<SettingsStore>();
        settings.SaveBoard(repo.Root, settings.Board(repo.Root) with { ListGroupBy = groups, ListSwimlane = subgroups });
        await board.InvokeAsync(() => { });
        board.WaitForAssertion(() => Assert.NotEmpty(board.FindAll("tr[data-id]")));
        return board;
    }

    /// <summary>The group headers as drawn, top to bottom, by their data-group: the groups ("Planned"), or with a prefix the subgroups ("swim:high").</summary>
    private static List<string> GroupOrder(IRenderedComponent<BoardPage> board, string prefix = "") =>
        [.. board.FindAll("tr[data-group]").Select(r => r.GetAttribute("data-group")!).Where(g => g.Length > 0 && (prefix.Length > 0 ? g.StartsWith(prefix) : !g.Contains(':')))];

    private static List<int> Shown(IRenderedComponent<BoardPage> board) =>
        [.. board.FindAll("tr[data-id]").Select(r => int.Parse(r.GetAttribute("data-id")!))];

    private static string? SelectedGroups(IRenderedComponent<BoardPage> board) => board.Find("style[data-rowmarks]").GetAttribute("data-selected-groups");

    private static async Task ShiftClickGroup(IRenderedComponent<BoardPage> board, string group) =>
        await board.InvokeAsync(() => board.Find($"tr[data-group='{group}']").Click(new MouseEventArgs { ShiftKey = true }));

    private static async Task Arrow(IRenderedComponent<BoardPage> board, string key) =>
        await board.InvokeAsync(() => board.Find(".tbl-wrap").KeyDown(key));

    private static async Task SortByUpdated(IRenderedComponent<BoardPage> board) =>
        await board.InvokeAsync(() => board.FindAll("th.rz-sortable-column").First(h => h.TextContent.Contains("Updated")).QuerySelector("div")!.Click());

    [Fact(DisplayName = "Shift-click on a list group header selects it, Up swaps it with the group above, and the board's column order follows")]
    public async Task ShiftClickOnAGroupHeaderSelectsItAndUpSwapsItWithTheGroupAbove()
    {
        await using BunitContext ctx = NewContext();
        Items(ctx);
        IRenderedComponent<BoardPage> board = await ListAsync(ctx, "status");
        List<string> before = GroupOrder(board);
        int at = before.IndexOf("Planned");
        Assert.True(at > 0, string.Join(' ', before));
        List<string> expected = [.. before];
        (expected[at], expected[at - 1]) = (expected[at - 1], expected[at]);

        await ShiftClickGroup(board, "Planned");
        Assert.Contains("\"Planned\"", SelectedGroups(board));   // marked as a selected row is
        await Arrow(board, "ArrowUp");

        board.WaitForAssertion(() => Assert.Equal(expected, GroupOrder(board)), TimeSpan.FromSeconds(5));
        Assert.Contains("\"Planned\"", SelectedGroups(board));   // and still selected, so another press moves it on

        IRenderedComponent<SideNav> nav = ctx.Render<SideNav>();
        nav.Find("button[aria-label=Layout]").Click();
        nav.FindAll(".menu-item").Single(i => i.TextContent.Trim() == "Board").Click();
        board.WaitForAssertion(() => Assert.Equal(expected, board.FindAll("header.col-h h3").Select(h => h.TextContent.Trim()).ToList()), TimeSpan.FromSeconds(5));
    }

    [Fact(DisplayName = "A plain click on a list group header selects nothing, and Down on a selected group moves it below the next one")]
    public async Task APlainClickDoesNotSelectAndDownMovesTheGroupOn()
    {
        await using BunitContext ctx = NewContext();
        Items(ctx);
        IRenderedComponent<BoardPage> board = await ListAsync(ctx, "status");
        List<string> before = GroupOrder(board);
        int at = before.IndexOf("Planned");
        await board.InvokeAsync(() => board.Find("tr[data-group='Planned']").Click());
        Assert.Null(SelectedGroups(board));
        await Arrow(board, "ArrowDown");
        Assert.Equal(before, GroupOrder(board));   // nothing selected: nothing moves

        await ShiftClickGroup(board, "Planned");
        await Arrow(board, "ArrowDown");

        List<string> expected = [.. before];
        (expected[at], expected[at + 1]) = (expected[at + 1], expected[at]);
        board.WaitForAssertion(() => Assert.Equal(expected, GroupOrder(board)), TimeSpan.FromSeconds(5));
    }

    [Theory(DisplayName = "With the grid's header sort on, the groups keep their saved order and only the rows inside a group sort")]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AHeaderSortKeepsTheGroupOrder(bool afterAMove)
    {
        await using BunitContext ctx = NewContext();
        int[] ids = Items(ctx);
        IRenderedComponent<BoardPage> board = await ListAsync(ctx, "status");
        if (afterAMove) { await ShiftClickGroup(board, "Planned"); await Arrow(board, "ArrowUp"); }
        board.WaitForAssertion(() => Assert.Equal(afterAMove ? ["Planned", "Backlog"] : ["Backlog", "Planned"], GroupOrder(board).Intersect(["Backlog", "Planned"]).ToList()));
        List<string> groups = GroupOrder(board);
        Assert.Equal([ids[1], ids[2]], Shown(board).Where(i => i == ids[1] || i == ids[2]).ToList());   // high first: the default

        await SortByUpdated(board);

        board.WaitForAssertion(() =>
        {
            Assert.NotNull(board.FindAll("th.rz-sortable-column").First(h => h.TextContent.Contains("Updated")).GetAttribute("aria-sort"));
            Assert.Equal([ids[2], ids[1]], Shown(board).Where(i => i == ids[1] || i == ids[2]).ToList());   // oldest first inside the group
            Assert.Equal(groups, GroupOrder(board));   // not the order of the groups' first rows (Confirmed, the oldest, would come first)
        }, TimeSpan.FromSeconds(5));
    }

    [Fact(DisplayName = "A subgroup moves among its siblings only: the groups keep their order and no row changes group")]
    public async Task ASubgroupMovesAmongItsSiblingsOnly()
    {
        await using BunitContext ctx = NewContext();
        int[] ids = Items(ctx);
        IRenderedComponent<BoardPage> board = await ListAsync(ctx, "status", "priority");
        List<string> groups = GroupOrder(board);
        Assert.Equal([ids[1], ids[2]], Shown(board).Where(i => i == ids[1] || i == ids[2]).ToList());   // in Planned: the high subgroup above the low one
        Dictionary<int, string> home = Shown(board).ToDictionary(i => i, i => Home(board, i));

        await ShiftClickGroup(board, "swim:low");
        await Arrow(board, "ArrowUp");

        board.WaitForAssertion(() =>
        {
            Assert.Equal(groups, GroupOrder(board));
            Assert.Equal([ids[2], ids[1]], Shown(board).Where(i => i == ids[1] || i == ids[2]).ToList());   // inside Planned, low now above high
            Assert.All(Shown(board), i => Assert.Equal(home[i], Home(board, i)));
        }, TimeSpan.FromSeconds(5));
    }

    /// <summary>The group header a row is drawn under.</summary>
    private static string Home(IRenderedComponent<BoardPage> board, int id)
    {
        string group = "";
        foreach (AngleSharp.Dom.IElement r in board.FindAll("tr[data-group], tr[data-id]"))
        {
            if (r.GetAttribute("data-id") == id.ToString()) return group;
            if (r.GetAttribute("data-group") is { Length: > 0 } g && !g.Contains(':')) group = g;
        }
        return "";
    }

    public void Dispose() => repo.Dispose();
}
