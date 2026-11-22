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
        nav.ClickWhenReady("button[aria-label=Layout]");
        nav.FireWhenReady(".menu-item", e => e.Click(), all => all.Single(i => i.TextContent.Trim() == "List"));
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
        board.FireWhenReady($"tr[data-group='{group}']", e => e.Click(new MouseEventArgs { ShiftKey = true }));

    private static async Task Arrow(IRenderedComponent<BoardPage> board, string key) =>
        board.KeyDownWhenReady(".tbl-wrap", key);

    private static async Task SortByUpdated(IRenderedComponent<BoardPage> board) =>
        board.FireWhenReady("th.rz-sortable-column", e => e.QuerySelector("div")!.Click(), all => all.First(h => h.TextContent.Contains("Updated")));

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

        board.WaitForAssertion(() => Assert.Equal(expected, GroupOrder(board)), WaitThen.Bound);
        Assert.Contains("\"Planned\"", SelectedGroups(board));   // and still selected, so another press moves it on

        IRenderedComponent<SideNav> nav = ctx.Render<SideNav>();
        nav.ClickWhenReady("button[aria-label=Layout]");
        nav.FireWhenReady(".menu-item", e => e.Click(), all => all.Single(i => i.TextContent.Trim() == "Board"));
        board.WaitForAssertion(() => Assert.Equal(expected, board.FindAll("header.col-h h3").Select(h => h.TextContent.Trim()).ToList()), WaitThen.Bound);
    }

    [Fact(DisplayName = "A plain click on a list group header selects nothing, and Down on a selected group moves it below the next one")]
    public async Task APlainClickDoesNotSelectAndDownMovesTheGroupOn()
    {
        await using BunitContext ctx = NewContext();
        Items(ctx);
        IRenderedComponent<BoardPage> board = await ListAsync(ctx, "status");
        List<string> before = GroupOrder(board);
        int at = before.IndexOf("Planned");
        board.ClickWhenReady("tr[data-group='Planned']");
        Assert.Null(SelectedGroups(board));
        await Arrow(board, "ArrowDown");
        Assert.Equal(before, GroupOrder(board));   // nothing selected: nothing moves

        await ShiftClickGroup(board, "Planned");
        await Arrow(board, "ArrowDown");

        List<string> expected = [.. before];
        (expected[at], expected[at + 1]) = (expected[at + 1], expected[at]);
        board.WaitForAssertion(() => Assert.Equal(expected, GroupOrder(board)), WaitThen.Bound);
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
        }, WaitThen.Bound);
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
        }, WaitThen.Bound);
    }

    private static async Task DragGroup(IRenderedComponent<BoardPage> board, string from, string? onto)
    {
        board.Find($"tr[data-group='{from}']").DragStart();
        (onto is null ? board.Find(".tbl-wrap") : board.Find($"tr[data-group='{onto}']")).Drop();   // the end: the script drops on the table, as the board's header row takes a drop at its end
    }

    private static string AppJs() => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "wwwroot", "js", "app.js"));

    [Fact(DisplayName = "A list group header carries the drag handler, and only a Shift-selected one is pressed as a group by the drag script")]
    public async Task OnlyASelectedGroupHeaderDrags()
    {
        await using BunitContext ctx = NewContext();
        Items(ctx);
        IRenderedComponent<BoardPage> board = await ListAsync(ctx, "status");
        Assert.NotNull(board.Find("tr[data-group='Planned']").GetAttribute("blazor:ondragstart"));
        Assert.Null(SelectedGroups(board));   // the script reads the marks: nothing selected, nothing is pressed as a group
        await ShiftClickGroup(board, "Planned");
        Assert.Contains("\"Planned\"", SelectedGroups(board));
        string js = AppJs(), down = js[js.IndexOf("document.addEventListener(\"pointerdown\"", StringComparison.Ordinal)..];
        down = down[..down.IndexOf("if (e.target.closest(\"input, button", StringComparison.Ordinal)];
        Assert.Contains("_groupSelected(", down);
        Assert.Contains("kind: \"group\"", down);
        Assert.Contains("function _startGroupDrag(", js);
        Assert.Contains("function _groupSpot(", js);
    }

    [Fact(DisplayName = "A selected group dragged above another reorders the board's columns, and a group dragged without being selected moves nothing")]
    public async Task ADraggedGroupReordersTheColumns()
    {
        await using BunitContext ctx = NewContext();
        Items(ctx);
        IRenderedComponent<BoardPage> board = await ListAsync(ctx, "status");
        List<string> before = GroupOrder(board);
        int at = before.IndexOf("Planned");
        Assert.True(at > 0, string.Join(' ', before));
        await DragGroup(board, "Planned", before[0]);   // not selected: it does not drag
        Assert.Equal(before, GroupOrder(board));

        await ShiftClickGroup(board, "Planned");
        await DragGroup(board, "Planned", before[0]);

        List<string> expected = ["Planned", .. before.Where(g => g != "Planned")];
        board.WaitForAssertion(() => Assert.Equal(expected, GroupOrder(board)), WaitThen.Bound);
        Assert.Contains("\"Planned\"", SelectedGroups(board));
        IRenderedComponent<SideNav> nav = ctx.Render<SideNav>();
        nav.ClickWhenReady("button[aria-label=Layout]");
        nav.FireWhenReady(".menu-item", e => e.Click(), all => all.Single(i => i.TextContent.Trim() == "Board"));
        board.WaitForAssertion(() => Assert.Equal(expected, board.FindAll("header.col-h h3").Select(h => h.TextContent.Trim()).ToList()), WaitThen.Bound);
    }

    [Fact(DisplayName = "Two selected groups dragged together keep their order, and a drop at the table's end puts them last")]
    public async Task SelectedGroupsDragTogether()
    {
        await using BunitContext ctx = NewContext();
        Items(ctx);
        IRenderedComponent<BoardPage> board = await ListAsync(ctx, "status");
        List<string> before = GroupOrder(board);
        Assert.True(before.IndexOf("Planned") < before.IndexOf("Confirmed"), string.Join(' ', before));
        await ShiftClickGroup(board, "Confirmed");
        await ShiftClickGroup(board, "Planned");

        await DragGroup(board, "Confirmed", before[0] == "Planned" ? before[1] : before[0]);   // onto the first group that is not one of them
        string first = before[0] == "Planned" ? before[1] : before[0];
        List<string> expected = [.. before.TakeWhile(g => g != first), "Planned", "Confirmed", .. before.SkipWhile(g => g != first).Where(g => g != "Planned" && g != "Confirmed")];
        board.WaitForAssertion(() => Assert.Equal(expected, GroupOrder(board)), WaitThen.Bound);

        await DragGroup(board, "Planned", null);
        List<string> last = [.. expected.Where(g => g != "Planned" && g != "Confirmed"), "Planned", "Confirmed"];
        board.WaitForAssertion(() => Assert.Equal(last, GroupOrder(board)), WaitThen.Bound);
    }

    [Fact(DisplayName = "A selected subgroup dragged onto its sibling swaps them in every group, and no group or row moves")]
    public async Task ADraggedSubgroupMovesAmongItsSiblingsOnly()
    {
        await using BunitContext ctx = NewContext();
        int[] ids = Items(ctx);
        IRenderedComponent<BoardPage> board = await ListAsync(ctx, "status", "priority");
        List<string> groups = GroupOrder(board);
        Dictionary<int, string> home = Shown(board).ToDictionary(i => i, i => Home(board, i));
        await DragGroup(board, "swim:low", "swim:high");   // not selected: nothing
        Assert.Equal([ids[1], ids[2]], Shown(board).Where(i => i == ids[1] || i == ids[2]).ToList());

        await ShiftClickGroup(board, "swim:low");
        await DragGroup(board, "swim:low", "swim:high");

        board.WaitForAssertion(() =>
        {
            Assert.Equal(groups, GroupOrder(board));
            Assert.Equal([ids[2], ids[1]], Shown(board).Where(i => i == ids[1] || i == ids[2]).ToList());
            Assert.All(Shown(board), i => Assert.Equal(home[i], Home(board, i)));
        }, WaitThen.Bound);
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
