using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using TaskManager.Board;
using TaskManager.Components.Layout;
using TaskManager.Components.Pages;
using TaskManager.Components.Shared;
using TaskManager.Services;
using WorkItems;

namespace TaskManager.Tests;

/// <summary>
/// Renders the real pages against a temp copy of the fixture repo. Each test owns its bUnit context and disposes
/// it asynchronously.
/// </summary>
[Collection(nameof(ModuleStateCollection))]
public sealed class UsingTheBoardTests : IDisposable
{
    private readonly TestRepo repo = new();
    private readonly string settingsFile = Path.Combine(Path.GetTempPath(), "rtm-ui-" + Guid.NewGuid().ToString("N") + ".json");

    private IRenderedComponent<Overlays>? overlays;
    private IRenderedComponent<SideNav>? nav;

    private BunitContext NewContext()
    {
        BunitContext ctx = new();
        ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        ctx.Services.AddSingleton<UiService>();
        ctx.Services.AddSingleton(new SettingsStore(settingsFile));
        ctx.Services.AddSingleton<Workspace>();
        Assert.True(ctx.Services.GetRequiredService<Workspace>().Open(repo.Root).Success);
        overlays = ctx.Render<Overlays>();
        nav = ctx.Render<SideNav>();
        return ctx;
    }

    [Fact(DisplayName = "A card shows its title on a band coloured by status, its features and its #wi tag")]
    public async Task Card()
    {
        await using BunitContext ctx = NewContext();
        IRenderedComponent<WorkItemCard> card = ctx.Render<WorkItemCard>(p => p.Add(c => c.Item, WorkItemsApi.Get(123)!).Add(c => c.Options, new BoardOptions()));

        Assert.Equal("Health-check endpoint", card.Find(".band").TextContent.Trim());
        Assert.Contains("--sun", card.Find(".band").GetAttribute("style"));
        Assert.Equal(["Availability", "Security"], card.FindAll(".card-features .feat").Select(f => f.TextContent.Trim()));   // each feature is a chip
        Assert.DoesNotContain("/", card.Find(".meta-row").TextContent);   // no done/total subtask count in the expanded card
        Assert.Contains("#wi123", card.Find(".tags").TextContent);
        Assert.Contains("high priority", card.Find(".tags").TextContent);
    }

    [Fact(DisplayName = "Compact cards show only the title band")]
    public async Task CompactCard()
    {
        await using BunitContext ctx = NewContext();
        IRenderedComponent<WorkItemCard> card = ctx.Render<WorkItemCard>(p => p.Add(c => c.Item, WorkItemsApi.Get(123)!).Add(c => c.Options, new BoardOptions(Compact: true)));
        Assert.Empty(card.FindAll(".meta-row"));
    }

    [Fact(DisplayName = "A card shows a failing-tests marker when a linked test failed in the latest run")]
    public async Task FailingTestsMarker()
    {
        await using BunitContext ctx = NewContext();
        repo.Write("results/run.trx", ReadingLinkedTestRunsTests.Trx);
        ReadingLinkedTestRunsTests.UseResults(repo, "results/*.trx");

        WorkItemsApi.SetLinkedTests(123, [new WorkItems.PublicDTOs.TestLink("UptimeTests", "Ping test", null)]);
        Assert.Empty(ctx.Render<WorkItemCard>(p => p.Add(c => c.Item, WorkItemsApi.Get(123)!).Add(c => c.Options, new BoardOptions())).FindAll(".tests-failing"));

        WorkItemsApi.SetLinkedTests(123, [new WorkItems.PublicDTOs.TestLink("UptimeTests", "Page load test", null)]);
        Assert.NotEmpty(ctx.Render<WorkItemCard>(p => p.Add(c => c.Item, WorkItemsApi.Get(123)!).Add(c => c.Options, new BoardOptions())).FindAll(".tests-failing"));
    }

    [Fact(DisplayName = "The board shows a column per status with the repository's work items")]
    public async Task BoardColumns()
    {
        await using BunitContext ctx = NewContext();
        IRenderedComponent<BoardPage> board = ctx.Render<BoardPage>();

        Assert.Equal(["Backlog", "Planned", "Confirmed", "In Progress", "Blocked", "In Review", "Completed"], board.FindAll(".col-h h3").Select(h => h.TextContent.Trim()));
        Assert.Equal(4, board.FindAll("article.card").Count);
        Assert.Equal(["High priority", "Lower priority"], board.FindAll(".lane-h span:first-child").Select(s => s.TextContent.Trim()));
    }

    [Fact(DisplayName = "Switching to List shows a grid row per work item, with sortable headers and a filter row")]
    public async Task ListView()
    {
        await using BunitContext ctx = NewContext();
        IRenderedComponent<BoardPage> board = ctx.Render<BoardPage>();
        ShowList();

        Assert.Equal(4, board.FindAll("tr[data-id]").Count);
        Assert.Contains(board.FindAll(".rz-sortable-column"), h => h.TextContent.Contains("Title"));   // the grid's own sort, checked in the real app (3-state)
        Assert.Contains(board.FindAll("thead th"), h => h.QuerySelector("[class*=filter]") is not null);   // and a filter control per column
    }

    // The list is a Radzen DataGrid: one tbody, where each group row (tr[data-group]) is followed by its item rows (tr[data-id]).
    private static AngleSharp.Dom.IElement Group(IRenderedComponent<BoardPage> board, string key) => board.Find($"tr[data-group='{key}']");

    private static List<AngleSharp.Dom.IElement> GroupRows(IRenderedComponent<BoardPage> board, string key)
    {
        List<AngleSharp.Dom.IElement> rows = [];
        bool inGroup = false;
        foreach (AngleSharp.Dom.IElement tr in board.FindAll(".rz-data-grid tbody tr"))
        {
            if (tr.HasAttribute("data-group")) { inGroup = tr.GetAttribute("data-group") == key; continue; }
            if (inGroup && tr.HasAttribute("data-id")) rows.Add(tr);
        }
        return rows;
    }

    private static List<string> RowsIn(IRenderedComponent<BoardPage> board, string group) =>
        GroupRows(board, group).Select(r => r.QuerySelector(".row-title")!.TextContent).ToList();

    [Fact(DisplayName = "List rows drag like cards: by their grip, in front of another row to reorder, onto a group to change status")]
    public async Task ListRowDrag()
    {
        await using BunitContext ctx = NewContext();
        Workspace workspace = ctx.Services.GetRequiredService<Workspace>();
        Assert.True(workspace.CreateItem("Planned").Ok);
        Assert.True(workspace.CreateItem("Planned").Ok);
        IRenderedComponent<BoardPage> board = ctx.Render<BoardPage>();
        ShowList();
        board.WaitForAssertion(() => Assert.Equal(3, RowsIn(board, "Planned").Count));
        List<string> before = RowsIn(board, "Planned");
        Assert.All(board.FindAll("tr[data-id]"), r => Assert.NotNull(r.QuerySelector(".grip")));   // the handle the drag script grabs

        GroupRows(board, "Planned")[2].DragStart();
        GroupRows(board, "Planned")[0].Drop();
        board.WaitForAssertion(() => Assert.Equal([before[2], before[0], before[1]], RowsIn(board, "Planned")));

        GroupRows(board, "Planned")[0].DragStart();
        Group(board, "In Progress").Drop();   // the group row stands for the group's end
        board.WaitForAssertion(() => Assert.Equal([before[0], before[1]], RowsIn(board, "Planned")));
        Assert.Equal("In Progress", WorkItemsApi.GetAll().Single(i => i.Title == before[2]).Status);
    }

    [Fact(DisplayName = "In the list, arrow keys move the selected rows, and Ctrl+C copies the selected cells (the script's part)")]
    public async Task ListArrowKeys()
    {
        await using BunitContext ctx = NewContext();
        Workspace workspace = ctx.Services.GetRequiredService<Workspace>();
        Assert.True(workspace.CreateItem("Planned").Ok);
        IRenderedComponent<BoardPage> board = ctx.Render<BoardPage>();
        ShowList();
        board.WaitForAssertion(() => Assert.Equal(2, RowsIn(board, "Planned").Count));
        List<string> before = RowsIn(board, "Planned");
        GroupRows(board, "Planned")[1].QuerySelector("td.num")!.Click(new MouseEventArgs { ShiftKey = true });   // selected as on the board: no tick boxes

        board.Find(".tbl-wrap").KeyDown("ArrowUp");

        board.WaitForAssertion(() => Assert.Equal([before[1], before[0]], RowsIn(board, "Planned")));
        Assert.Equal("0", board.Find(".tbl-wrap").GetAttribute("tabindex"));   // focusable, so keys and Ctrl+C reach it
        string js = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "wwwroot", "js", "app.js"));
        Assert.Contains("addEventListener(\"copy\"", js);
        Assert.Contains("tr[data-id] .grip", js);
        Assert.Contains(".tbl-wrap thead .rz-column-drag", js);   // the whole header reorders, and a click on it still sorts
        Assert.Contains("_cells.moved", js);
        Assert.Contains("is-resizing", js);   // a column resize shows a guide line down the table, not a header highlight   // a drag from any cell selects cells; only a still click works the cell's control
    }

    [Fact(DisplayName = "In the list, Shift-click on a row selects it and a plain click on its title opens it, as on the board")]
    public async Task ListRowShiftClick()
    {
        await using BunitContext ctx = NewContext();
        IRenderedComponent<BoardPage> board = ctx.Render<BoardPage>();
        ShowList();
        AngleSharp.Dom.IElement row = board.FindAll("tr[data-id]").First(r => r.TextContent.Contains("#wi123"));

        row.QuerySelector("td.num")!.Click(new MouseEventArgs { ShiftKey = true });
        board.WaitForAssertion(() => Assert.NotNull(board.FindAll("tr[data-id]").First(r => r.TextContent.Contains("#wi123")).GetAttribute("data-selected")));
        Assert.Contains("1 selected", board.Find(".actionbar:not(.select-hint)").TextContent);
        Assert.Empty(board.FindAll("tr[data-id] input[type=checkbox]"));   // no tick boxes, as on the board
        board.FindAll("tr[data-id]").First(r => r.TextContent.Contains("#wi123")).QuerySelector("td.num")!.Click(new MouseEventArgs { CtrlKey = true });
        board.WaitForAssertion(() => Assert.Contains("Shift-click rows", board.Find(".select-hint").TextContent));   // the Shift hint is there in the list too

        board.FindAll("tr[data-id]").First(r => r.TextContent.Contains("#wi123")).QuerySelector(".row-title")!.Click(new MouseEventArgs { ShiftKey = true });
        board.WaitForAssertion(() => Assert.Null(board.FindAll("tr[data-id]").First(r => r.TextContent.Contains("#wi123")).GetAttribute("data-selected")));   // toggled off, not opened

        board.FindAll("tr[data-id]").First(r => r.TextContent.Contains("#wi123")).QuerySelector(".row-title")!.Click();
        Assert.EndsWith("/item/123", ctx.Services.GetRequiredService<NavigationManager>().Uri);
    }

    [Fact(DisplayName = "List cells wear their colours: status, label and priority are filled pills, and a status group's name wears its band")]
    public async Task ListColours()
    {
        await using BunitContext ctx = NewContext();
        IRenderedComponent<BoardPage> board = ctx.Render<BoardPage>();
        ShowList();

        AngleSharp.Dom.IElement row = board.FindAll("tr[data-id]").First(r => r.TextContent.Contains("#wi123"));
        Assert.Equal(3, row.QuerySelectorAll("button.box.cell.filled[style*='--fill']").Length);   // status, label, priority
        Assert.Contains("--fill:var(--sun)", Group(board, "In Progress").QuerySelector(".gname")!.GetAttribute("style"));
    }

    [Fact(DisplayName = "In the list, an item's features stay on one line: the first as a tag, the rest as a count, all of them in the tooltip")]
    public async Task ListFeaturesOneLine()
    {
        await using BunitContext ctx = NewContext();
        IRenderedComponent<BoardPage> board = ctx.Render<BoardPage>();
        ShowList();

        AngleSharp.Dom.IElement line = board.FindAll("tr[data-id]").Single(r => r.TextContent.Contains("Health-check")).QuerySelector("td .feat-line")!;

        Assert.Equal(["Availability"], line.QuerySelectorAll(".tag").Select(t => t.TextContent.Trim()));
        Assert.Equal("+1", line.QuerySelector(".more")!.TextContent.Trim());
        Assert.Equal("Availability, Security", line.GetAttribute("title"));
    }

    [Fact(DisplayName = "Hidden work items leave the board until 'show hidden' is ticked")]
    public async Task HiddenItems()
    {
        await using BunitContext ctx = NewContext();
        WorkItemsApi.Hide(107);
        IRenderedComponent<BoardPage> board = ctx.Render<BoardPage>();
        Assert.Equal(3, board.FindAll("article.card").Count);

        nav!.Find("button.nav-toggle").Click();   // the expanded side menu holds the view options
        nav.Find("input#f-hidden").Change(true);

        Assert.Equal(4, board.FindAll("article.card").Count);
        Assert.Single(board.FindAll("article.card.hidden-item"));
    }

    [Fact(DisplayName = "The collapsed side menu has a compact-cards button that toggles it and shows when it's on")]
    public async Task CompactFromRail()
    {
        await using BunitContext ctx = NewContext();
        IRenderedComponent<BoardPage> board = ctx.Render<BoardPage>();
        Assert.Empty(board.FindAll("article.card.collapsed"));

        nav!.Find("button.compact-btn").Click();

        Assert.Equal(board.FindAll("article.card").Count, board.FindAll("article.card.collapsed").Count);
        Assert.Contains("is-sel", nav.Find("button.compact-btn").ClassList);

        nav.Find("button.compact-btn").Click();
        Assert.Empty(board.FindAll("article.card.collapsed"));
    }

    [Fact(DisplayName = "A dropdown whose current value isn't in its options still shows that value, not blank")]
    public void DropdownShowsUnknownValue()
    {
        using BunitContext ctx = new();
        IRenderedComponent<Dropdown> dd = ctx.Render<Dropdown>(p => p
            .Add(c => c.Label, "example")
            .Add(c => c.Options, [("a", "Alpha"), ("b", "Beta")])
            .Add(c => c.Value, "mystery")
            .Add(c => c.Placeholder, "pick one"));

        Assert.Equal("mystery", dd.Find("button.dd span").TextContent.Trim());
    }

    [Fact(DisplayName = "Opening the group-by dropdown and picking bucket regroups the board into sections")]
    public async Task DropdownOpensAndPicks()
    {
        await using BunitContext ctx = NewContext();
        IRenderedComponent<BoardPage> board = ctx.Render<BoardPage>();

        nav!.Find("button.nav-toggle").Click();
        nav.FindAll(".field").Single(f => f.TextContent.Contains("group by")).QuerySelector("button.dd")!.Click();
        nav.WaitForAssertion(() => Assert.Contains(nav.FindAll(".menu-item"), i => i.TextContent.Trim() == "bucket"));
        nav.FindAll(".menu-item").First(i => i.TextContent.Trim() == "bucket").Click();

        board.WaitForAssertion(() => Assert.NotEmpty(board.FindAll(".cat-h")));
    }

    [Fact(DisplayName = "Every board column and subgroup lane is a drop zone the drag script can accept drops on")]
    public async Task DropZonesMarked()
    {
        await using BunitContext ctx = NewContext();
        IRenderedComponent<BoardPage> board = ctx.Render<BoardPage>();

        Assert.All(board.FindAll("section.col"), c => Assert.True(c.HasAttribute("data-drop")));
        Assert.All(board.FindAll(".lane"), l => Assert.True(l.HasAttribute("data-drop")));
        Assert.Contains("data-drop", File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "wwwroot", "js", "app.js")));
    }

    [Fact(DisplayName = "Bucket grouping shows all buckets simultaneously as sections with their features")]
    public async Task BucketSections()
    {
        await using BunitContext ctx = NewContext();
        IRenderedComponent<BoardPage> board = ctx.Render<BoardPage>();
        GroupByBucket();

        List<string> headers = board.FindAll(".cat-h h2").Select(h => h.TextContent.Trim()).ToList();
        Assert.Contains("Functional", headers);
        Assert.Contains("Non-Functional", headers);
        Assert.Contains("Banking", Columns(board));
        Assert.Contains("Availability", Columns(board));
    }

    [Fact(DisplayName = "An expanded list row numbers its subtasks 1.1, 1.2 …")]
    public async Task ListSubtaskNumbers()
    {
        await using BunitContext ctx = NewContext();
        IRenderedComponent<BoardPage> board = ctx.Render<BoardPage>();
        ShowList();
        nav!.Find("button.compact-btn").Click();   // compact: groups shut, no subtasks; open one group and one row by hand
        board.WaitForAssertion(() => Assert.Empty(board.FindAll(".subrow")));
        board.Find("tr[data-group='In Progress'] button").Click();
        board.WaitForAssertion(() => board.FindAll("tr[data-id]").First(r => r.TextContent.Contains("#wi123")).QuerySelector("button.chev")!.Click());

        board.WaitForAssertion(() => Assert.Equal(["1.1", "1.2", "1.3", "2.1"], board.FindAll(".subrow .num").Select(td => td.TextContent.Trim())));
    }

    [Fact(DisplayName = "Completed rows have editable label and priority but status stays read-only, and Completed has no '+ Add item'")]
    public async Task CompletedRowsReadOnly()
    {
        await using BunitContext ctx = NewContext();
        IRenderedComponent<BoardPage> board = ctx.Render<BoardPage>();
        ShowList();

        AngleSharp.Dom.IElement row = board.FindAll("tr[data-id]").Single(r => r.TextContent.Contains("#wi124"));
        Assert.NotEmpty(row.QuerySelectorAll("button.dd"));   // label and priority dropdowns
        Assert.Contains("Completed", row.TextContent);
        // status cell is a read-only span, not a dropdown
        AngleSharp.Dom.IElement statusCell = row.QuerySelectorAll("td").First(td => td.QuerySelector(".filled") is not null);
        Assert.NotNull(statusCell.QuerySelector("span.box"));
        Assert.Null(statusCell.QuerySelector("button.dd"));
        int groups = board.FindAll("tr[data-group]").Count;
        Assert.Equal(groups - 1, board.FindAll("button").Count(b => b.TextContent.Trim() == "+ Add item"));   // every group but Completed
    }

    [Fact(DisplayName = "In the list, the compact switch hides every row's subtasks and keeps the work items; off again, every subtask shows")]
    public async Task CompactHidesOnlySubtasks()
    {
        await using BunitContext ctx = NewContext();
        IRenderedComponent<BoardPage> board = ctx.Render<BoardPage>();
        ShowList();
        Assert.Equal(4, board.FindAll("tr[data-id]").Count);

        nav!.Find("button.compact-btn").Click();
        board.WaitForAssertion(() => Assert.Empty(board.FindAll(".subrow")));
        Assert.Equal(4, board.FindAll("tr[data-id]").Count);   // the work items stay; only their subtasks fold away

        nav.Find("button.compact-btn").Click();
        board.WaitForAssertion(() => Assert.Equal(4 + 2 + 1 + 1, board.FindAll(".subrow").Count));
    }

    [Fact(DisplayName = "A grid column is hidden from the × in its header and listed under Hidden in the side menu, where the eye brings it back")]
    public async Task HideGridColumn()
    {
        await using BunitContext ctx = NewContext();
        IRenderedComponent<BoardPage> board = ctx.Render<BoardPage>();
        ShowList();
        Assert.Contains(board.FindAll("thead th"), h => h.TextContent.Contains("Progress"));

        board.Find("button[aria-label='Hide Progress column']").Click();

        board.WaitForAssertion(() => Assert.DoesNotContain(board.FindAll("thead th"), h => h.TextContent.Contains("Progress")));
        Assert.Contains("col:Progress", ctx.Services.GetRequiredService<SettingsStore>().Board(repo.Root).Hidden!);
        nav!.Find("button.nav-toggle").Click();
        Assert.Contains("Progress", nav.Find(".nav-hidden").TextContent);
        nav.Find("button[aria-label='Show Progress']").Click();
        board.WaitForAssertion(() => Assert.Contains(board.FindAll("thead th"), h => h.TextContent.Contains("Progress")));
        Assert.Empty(board.FindAll(".rz-column-picker"));   // no toolbar row
    }

    [Fact(DisplayName = "In the list, the side menu's compact button hides every subtask row; off again, every subtask shows")]
    public async Task CompactHidesSubtasksInList()
    {
        await using BunitContext ctx = NewContext();
        IRenderedComponent<BoardPage> board = ctx.Render<BoardPage>();
        ShowList();
        board.WaitForAssertion(() => Assert.Equal(4 + 2 + 1 + 1, board.FindAll(".subrow").Count));   // details on by default

        nav!.Find("button.compact-btn").Click();
        board.WaitForAssertion(() => Assert.Empty(board.FindAll(".subrow")));
        board.Find("tr[data-group='In Progress'] button").Click();   // open that one group, then one row: its subtasks show, nothing else's
        board.WaitForAssertion(() => board.FindAll("tr[data-id]").First(r => r.TextContent.Contains("#wi123")).QuerySelector("button.chev")!.Click());
        board.WaitForAssertion(() => Assert.Equal(4, board.FindAll(".subrow").Count));

        nav.Find("button.compact-btn").Click();
        board.WaitForAssertion(() => Assert.Equal(4 + 2 + 1 + 1, board.FindAll(".subrow").Count));
    }

    [Fact(DisplayName = "A hidden work item's row is marked as hidden in the list")]
    public async Task HiddenRowMarked()
    {
        await using BunitContext ctx = NewContext();
        WorkItemsApi.Hide(107);
        IRenderedComponent<BoardPage> board = ctx.Render<BoardPage>();
        ShowList();
        nav!.Find("button.nav-toggle").Click();   // the expanded side menu holds the view options
        nav.Find("input#f-hidden").Change(true);

        Assert.True(board.FindAll("tr[data-id]").Single(r => r.TextContent.Contains("#wi107")).HasAttribute("data-hidden"));
    }

    [Fact(DisplayName = "Adding a subgroup asks for its name in a dialog")]
    public async Task SubCategoryPrompt()
    {
        await using BunitContext ctx = NewContext();
        IRenderedComponent<BoardPage> board = ctx.Render<BoardPage>();
        board.FindAll("header.col-h").First(h => h.TextContent.Contains("Blocked")).Click();
        board.FindAll(".menu-item").First(i => i.TextContent.Contains("Add subgroup")).Click();

        overlays!.WaitForAssertion(() => Assert.Contains("Add a subgroup to Blocked", overlays.Find(".dialog").TextContent));
        overlays.Find(".dialog input").Input("Needs review");
        overlays.FindAll(".dialog button").Single(b => b.TextContent.Trim() == "Add").Click();

        board.WaitForAssertion(() => Assert.Contains(board.FindAll(".lane-h span:first-child"), s => s.TextContent.Trim() == "Needs review"));
        Assert.Empty(overlays.FindAll(".dialog"));
    }

    [Fact(DisplayName = "Merging work items confirms it with a toast")]
    public async Task MergeToast()
    {
        await using BunitContext ctx = NewContext();
        IRenderedComponent<BoardPage> board = ctx.Render<BoardPage>();
        for (int i = 0; i < 2; i++) board.FindAll("article.card")[i].Click(new MouseEventArgs { CtrlKey = true });   // re-found: each click re-renders

        board.Find(".actionbar button.primary").Click();

        overlays!.WaitForAssertion(() => Assert.Contains("Merged into #wi", overlays.Find(".toast").TextContent));
    }

    [Fact(DisplayName = "Hiding selected work items removes them from the board and clears the selection")]
    public async Task HideSelectedCards()
    {
        await using BunitContext ctx = NewContext();
        IRenderedComponent<BoardPage> board = ctx.Render<BoardPage>();
        int before = board.FindAll("article.card").Count;
        board.FindAll("article.card").First().Click(new MouseEventArgs { ShiftKey = true });

        board.Find(".actionbar button.hide-sel").Click();

        Assert.Empty(board.FindAll(".actionbar:not(.select-hint)"));   // only the Shift hint bar is left
        Assert.Equal(before - 1, board.FindAll("article.card").Count);
    }

    [Fact(DisplayName = "Clicking a column header opens its menu")]
    public async Task ColumnMenuOpens()
    {
        await using BunitContext ctx = NewContext();
        IRenderedComponent<BoardPage> board = ctx.Render<BoardPage>();

        board.FindAll("header.col-h").First(h => h.TextContent.Contains("Blocked")).Click();

        board.WaitForAssertion(() => Assert.Contains(board.FindAll(".menu-item"), i => i.TextContent.Contains("Add subgroup")));
    }

    [Fact(DisplayName = "Showing subtasks puts one card per tasks.md checkbox on the board")]
    public async Task SubtaskCards()
    {
        await using BunitContext ctx = NewContext();
        IRenderedComponent<BoardPage> board = ctx.Render<BoardPage>();
        nav!.Find("button.nav-toggle").Click();
        nav.Find("input[name=unit][value=subtasks]").Change("subtasks");
        Assert.Equal(4 + 2 + 1 + 1, board.FindAll("article.subcard").Count);
    }

    [Fact(DisplayName = "The menu button expands the side menu to labels and view options, and collapses it again")]
    public async Task SideMenuExpands()
    {
        await using BunitContext ctx = NewContext();
        Assert.DoesNotContain("expanded", nav!.Find("nav.side").ClassName);
        Assert.Empty(nav.FindAll(".nav-view"));

        nav.Find("button.nav-toggle").Click();
        Assert.Contains("expanded", nav.Find("nav.side").ClassName);
        Assert.Contains("group by", nav.Find(".nav-view").TextContent);
        Assert.Contains("contributor", nav.Find(".nav-view").TextContent);

        nav.Find("button.nav-toggle").Click();
        Assert.Empty(nav.FindAll(".nav-view"));

        nav.Find("button[aria-label='View options']").Click();   // the view icon opens the same expanded menu
        Assert.NotEmpty(nav.FindAll(".nav-view"));
    }

    [Fact(DisplayName = "+ in the side menu creates a work item and opens it")]
    public async Task SideMenuNewItem()
    {
        await using BunitContext ctx = NewContext();

        nav!.Find("button[aria-label='New work item']").Click();

        overlays!.WaitForAssertion(() => Assert.Contains("Created #wi", overlays.Find(".toast").TextContent));
        Assert.Matches(@"/item/\d+$", ctx.Services.GetRequiredService<Microsoft.AspNetCore.Components.NavigationManager>().Uri);
    }

    [Fact(DisplayName = "Board and List are one dropdown in the side menu")]
    public async Task LayoutDropdown()
    {
        await using BunitContext ctx = NewContext();
        IRenderedComponent<BoardPage> board = ctx.Render<BoardPage>();
        Assert.Empty(nav!.FindAll("button[aria-label=List]"));
        Assert.Contains("Board", nav.Find("button[aria-label=Layout]").TextContent);

        ShowList();

        Assert.NotEmpty(board.FindAll("tr[data-id]"));
        Assert.Contains("List", nav.Find("button[aria-label=Layout]").TextContent);
    }

    [Fact(DisplayName = "Clicking a column header offers Hide; the hidden column then shows in the side menu to bring back")]
    public async Task HideColumn()
    {
        await using BunitContext ctx = NewContext();
        IRenderedComponent<BoardPage> board = ctx.Render<BoardPage>();
        nav!.Find("button.nav-toggle").Click();
        Assert.Empty(nav.FindAll(".nav-hidden"));   // nothing hidden, nothing listed

        board.FindAll("header.col-h").First(h => h.TextContent.Contains("Blocked")).Click();
        board.FindAll(".menu-item").Single(i => i.TextContent.Trim() == "Hide column").Click();

        Assert.DoesNotContain("Blocked", Columns(board));
        Assert.Equal("0", nav.Find(".nav-hidden .grp-row .count").TextContent.Trim());   // how many items it holds
        nav.Find("button[aria-label='Show Blocked']").Click();
        Assert.Contains("Blocked", Columns(board));
        Assert.Empty(nav.FindAll(".nav-hidden"));
    }

    // The drag script (wwwroot/js/app.js) delivers each drop to what sits right after the gap: a column, a card, or the
    // row / lane itself when the gap is at the end. These tests drop on those elements directly.
    [Fact(DisplayName = "Dragging the Backlog header past the last column moves it to the end; onto a column puts it in front")]
    public async Task DragColumn()
    {
        await using BunitContext ctx = NewContext();
        IRenderedComponent<BoardPage> board = ctx.Render<BoardPage>();

        board.FindAll("header.col-h").First(h => h.TextContent.Contains("Backlog")).DragStart();
        board.Find(".cat-cols").Drop();
        Assert.Equal(["Planned", "Confirmed", "In Progress", "Blocked", "In Review", "Completed", "Backlog"], Columns(board));

        board.FindAll("header.col-h").First(h => h.TextContent.Contains("Completed")).DragStart();
        board.FindAll("section.col").Single(c => c.QuerySelector("h3")!.TextContent == "Planned").Drop();
        Assert.Equal(["Completed", "Planned", "Confirmed", "In Progress", "Blocked", "In Review", "Backlog"], Columns(board));
    }

    [Fact(DisplayName = "Dragging the last card in front of the first moves it to the top of its column, and it stays there")]
    public async Task DragCardWithinColumn()
    {
        await using BunitContext ctx = NewContext();
        Workspace workspace = ctx.Services.GetRequiredService<Workspace>();
        Assert.True(workspace.CreateItem("Planned").Ok);
        Assert.True(workspace.CreateItem("Planned").Ok);
        IRenderedComponent<BoardPage> board = ctx.Render<BoardPage>();
        List<string> before = CardsIn(board, "Planned");
        Assert.Equal(3, before.Count);

        Col(board, "Planned").QuerySelectorAll("article.card")[2].DragStart();
        Col(board, "Planned").QuerySelectorAll("article.card")[0].Drop();

        Assert.Equal([before[2], before[0], before[1]], CardsIn(board, "Planned"));
        Assert.Equal([before[2], before[0], before[1]], CardsIn(ctx.Render<BoardPage>(), "Planned"));   // remembered in the settings file
    }

    [Fact(DisplayName = "Dragging one of several selected cards moves them all to the gap in another column")]
    public async Task DragSelectedCards()
    {
        await using BunitContext ctx = NewContext();
        Workspace workspace = ctx.Services.GetRequiredService<Workspace>();
        Assert.True(workspace.CreateItem("Backlog").Ok);
        Assert.True(workspace.CreateItem("Backlog").Ok);
        IRenderedComponent<BoardPage> board = ctx.Render<BoardPage>();
        List<string> moving = CardsIn(board, "Backlog");
        List<string> staying = CardsIn(board, "In Progress");
        for (int i = 0; i < moving.Count; i++) Col(board, "Backlog").QuerySelectorAll("article.card")[i].Click(new MouseEventArgs { CtrlKey = true });

        Col(board, "Backlog").QuerySelectorAll("article.card")[1].DragStart();
        Col(board, "In Progress").QuerySelectorAll("article.card")[0].Drop();

        Assert.Empty(CardsIn(board, "Backlog"));
        Assert.Equal([.. moving, .. staying], CardsIn(board, "In Progress"));
    }

    [Fact(DisplayName = "Clicking a card selects it; clicking it again opens it")]
    public async Task ClickSelectsThenOpens()
    {
        await using BunitContext ctx = NewContext();
        IRenderedComponent<BoardPage> board = ctx.Render<BoardPage>();
        NavigationManager nav = ctx.Services.GetRequiredService<NavigationManager>();
        string home = nav.Uri;

        board.FindAll("article.card")[0].Click(new MouseEventArgs { ShiftKey = true });
        Assert.Single(board.FindAll("article.card.selected"));
        Assert.Equal(home, nav.Uri);
        Assert.Empty(board.FindAll("article.card input"));   // no tick boxes

        board.FindAll("article.card")[1].Click();   // a plain click opens, even while something is selected
        Assert.Matches(@"/item/\d+$", nav.Uri);
    }

    [Fact(DisplayName = "Shift-click (or Ctrl/⌘-click) adds cards to the selection or removes them")]
    public async Task ClickToSelectSeveral()
    {
        await using BunitContext ctx = NewContext();
        IRenderedComponent<BoardPage> board = ctx.Render<BoardPage>();

        board.FindAll("article.card")[1].Click(new MouseEventArgs { ShiftKey = true });
        Assert.Equal([1], Selected(board));

        board.FindAll("article.card")[0].Click(new MouseEventArgs { MetaKey = true });
        Assert.Equal([0, 1], Selected(board));

        board.FindAll("article.card")[1].Click(new MouseEventArgs { ShiftKey = true });
        Assert.Equal([0], Selected(board));
        Assert.Contains("1 selected", board.Find(".actionbar").TextContent);
    }

    [Fact(DisplayName = "Arrow keys move the selected card: up/down inside its column, left/right into the next column at the same height")]
    public async Task ArrowKeysMoveCard()
    {
        await using BunitContext ctx = NewContext();
        Workspace workspace = ctx.Services.GetRequiredService<Workspace>();
        Assert.True(workspace.CreateItem("Planned").Ok);
        Assert.True(workspace.CreateItem("Planned").Ok);
        IRenderedComponent<BoardPage> board = ctx.Render<BoardPage>();
        List<string> before = CardsIn(board, "Planned");
        Col(board, "Planned").QuerySelectorAll("article.card")[2].Click(new MouseEventArgs { ShiftKey = true });

        board.Find(".board-wrap").KeyDown("ArrowUp");
        Assert.Equal([before[0], before[2], before[1]], CardsIn(board, "Planned"));

        board.Find(".board-wrap").KeyDown("ArrowRight");   // Confirmed is empty, so it lands there as the only card
        Assert.Equal([before[0], before[1]], CardsIn(board, "Planned"));
        Assert.Equal([before[2]], CardsIn(board, "Confirmed"));
        Assert.Equal("Confirmed", WorkItemsApi.GetAll().Single(i => i.Title == before[2]).Status);

        board.Find(".board-wrap").KeyDown("ArrowLeft");   // back at row 0: the top of Planned
        Assert.Equal([before[2], before[0], before[1]], CardsIn(board, "Planned"));
        Assert.Single(board.FindAll("article.card.selected"));   // still selected, so the next arrow moves it again
    }

    [Fact(DisplayName = "Arrow keys move several selected cards only when they share a column; a scattered selection gathers first")]
    public async Task ArrowKeysMoveGroup()
    {
        await using BunitContext ctx = NewContext();
        Workspace workspace = ctx.Services.GetRequiredService<Workspace>();
        Assert.True(workspace.CreateItem("Planned").Ok);
        Assert.True(workspace.CreateItem("Planned").Ok);
        IRenderedComponent<BoardPage> board = ctx.Render<BoardPage>();
        List<string> before = CardsIn(board, "Planned");

        Col(board, "Planned").QuerySelectorAll("article.card")[0].Click(new MouseEventArgs { ShiftKey = true });
        Col(board, "In Progress").QuerySelectorAll("article.card")[0].Click(new MouseEventArgs { CtrlKey = true });
        board.Find(".board-wrap").KeyDown("ArrowDown");
        overlays!.WaitForAssertion(() => Assert.Contains("one column", overlays.Find(".toast").TextContent));
        Assert.Equal(before, CardsIn(board, "Planned"));

        Col(board, "In Progress").QuerySelectorAll("article.card")[0].Click(new MouseEventArgs { ShiftKey = true });   // toggles it off: only Planned 0 stays
        Col(board, "Planned").QuerySelectorAll("article.card")[2].Click(new MouseEventArgs { CtrlKey = true });
        board.Find(".board-wrap").KeyDown("ArrowDown");
        Assert.Equal([before[1], before[0], before[2]], CardsIn(board, "Planned"));   // gathered at the bottom card
    }

    [Fact(DisplayName = "With nothing selected the board carries a select-mode hint bar for the Shift key; a selection replaces it")]
    public async Task SelectModeHint()
    {
        await using BunitContext ctx = NewContext();
        IRenderedComponent<BoardPage> board = ctx.Render<BoardPage>();

        Assert.Contains("Select mode", board.Find(".actionbar.select-hint").TextContent);
        Assert.Contains("body.shift-held .select-hint", File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "wwwroot", "css", "app.css")));   // shown only while Shift is held

        board.FindAll("article.card")[0].Click(new MouseEventArgs { ShiftKey = true });
        Assert.Empty(board.FindAll(".select-hint"));
        Assert.Contains("1 selected", board.Find(".actionbar").TextContent);
    }

    private static List<int> Selected(IRenderedComponent<BoardPage> board) =>
        board.FindAll("article.card").Select((c, i) => (c, i)).Where(t => t.c.ClassList.Contains("selected")).Select(t => t.i).ToList();

    private static AngleSharp.Dom.IElement Col(IRenderedComponent<BoardPage> board, string name) =>
        board.FindAll("section.col").Single(c => c.QuerySelector("h3")!.TextContent == name);

    private static List<string> CardsIn(IRenderedComponent<BoardPage> board, string column) =>
        Col(board, column).QuerySelectorAll("article.card .band").Select(b => b.TextContent).ToList();

    [Fact(DisplayName = "Deleting my own feature from its header asks first, then its work items move to Unassigned")]
    public async Task DeleteFeature()
    {
        await using BunitContext ctx = NewContext();
        Features.FeaturesApi.CreateFeature("Functional", "Payments");
        (bool _, string _, int id) = WorkItemsApi.Create("Pay run", features: ["functional/payments"]);
        IRenderedComponent<BoardPage> board = ctx.Render<BoardPage>();
        GroupByBucket();
        board.FindAll("header.col-h").First(h => h.TextContent.Contains("Banking")).Click();
        Assert.DoesNotContain(board.FindAll(".menu-item"), i => i.TextContent.Contains("Delete"));   // spec-backed: hide only
        board.Find(".menu-backdrop").Click();

        board.FindAll("header.col-h").First(h => h.TextContent.Contains("Payments")).Click();
        board.FindAll(".menu-item").Single(i => i.TextContent.Trim() == "Delete feature…").Click();
        overlays!.WaitForAssertion(() => Assert.Contains("Delete Payments?", overlays.Find(".dialog").TextContent));
        overlays.FindAll(".dialog button").Single(b => b.TextContent.Trim() == "Delete").Click();

        board.WaitForAssertion(() => Assert.DoesNotContain("Payments", Columns(board)));
        Assert.Contains($"#wi{id}", board.FindAll("section.col").Single(c => c.QuerySelector("h3")!.TextContent == "Unassigned").TextContent);
    }

    // The layout dropdown in the side menu switches the board to the list.
    private void ShowList()
    {
        nav!.Find("button[aria-label=Layout]").Click();
        nav.FindAll(".menu-item").Single(i => i.TextContent.Trim() == "List").Click();
    }

    private void GroupByBucket()
    {
        nav!.Find("button.nav-toggle").Click();
        nav.FindAll(".field").Single(f => f.TextContent.Contains("group by")).QuerySelector("button.dd")!.Click();
        nav.FindAll(".menu-item").First(i => i.TextContent.Trim() == "bucket").Click();
    }

    private static List<string> Columns(IRenderedComponent<BoardPage> board) => board.FindAll("section.col h3").Select(h => h.TextContent).ToList();

    public void Dispose()
    {
        repo.Dispose();
        File.Delete(settingsFile);
    }
}
