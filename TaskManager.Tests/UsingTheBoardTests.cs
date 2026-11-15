using AngleSharp.Dom;
using Notes;
using WorkItems.PublicDTOs;
using Radzen;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.Time.Testing;
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
public sealed class UsingTheBoardTests : IDisposable
{
    private readonly TestRepo repo = new();
    private readonly TestRepo repo2 = new();   // a second project, opened as a tab by TwoProjects
    private readonly string settingsFile = Path.Combine(Path.GetTempPath(), "rtm-ui-" + Guid.NewGuid().ToString("N") + ".json");

    private IRenderedComponent<Overlays>? overlays;
    private IRenderedComponent<SideNav>? nav;

    private BunitContext NewContext(TimeProvider? time = null)
    {
        BunitContext ctx = new();
        if (time is not null) ctx.Services.AddSingleton(time);   // the clock of the pages: a test moves it by hand
        ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        ctx.Services.AddSingleton<UiService>();
        ctx.Services.AddRadzenComponents();   // Radzen components ask for its services (the markdown editor wants DialogService)
        ctx.Services.AddSingleton(new SettingsStore(settingsFile));
        ctx.Services.AddSingleton<Workspace>();
        Assert.True(ctx.Services.GetRequiredService<Workspace>().Open(repo.Root).Success);
        overlays = ctx.Render<Overlays>();
        nav = ctx.Render<SideNav>();
        return ctx;
    }

    [Fact(DisplayName = "What the board is grouped by is not repeated: no Status column in a list grouped by status, no Label column by label, no Features by bucket, no Contributors by contributor")]
    public async Task GroupedColumnNotRepeatedInList()
    {
        await using BunitContext ctx = NewContext();
        IRenderedComponent<BoardPage> board = ctx.Render<BoardPage>();
        ShowList();
        List<string> Heads() => board.FindAll("thead th .th-l").Select(h => h.TextContent.Trim()).ToList();
        board.WaitForAssertion(() => Assert.DoesNotContain("Status", Heads()));
        Assert.Contains("Label", Heads());

        SettingsStore settings = ctx.Services.GetRequiredService<SettingsStore>();
        foreach ((string by, string column) in new[] { ("label", "Label"), ("feature", "Feature"), ("contributor", "Contributors") })
        {
            settings.SaveBoard(repo.Root, settings.Board(repo.Root) with { ListGroupBy = by });
            board.WaitForAssertion(() => Assert.DoesNotContain(column, Heads()));
            Assert.Contains("Status", Heads());
        }
    }

    [Fact(DisplayName = "A card leaves out what its column already says: the label when grouped by label, the contributors when grouped by contributor")]
    public async Task GroupedFieldNotRepeatedOnCard()
    {
        await using BunitContext ctx = NewContext();
        WorkItem item = WorkItemsApi.Get(123)! with { Label = "bug" };
        IRenderedComponent<WorkItemCard> byLabel = ctx.Render<WorkItemCard>(p => p.Add(c => c.Item, item).Add(c => c.Options, new BoardOptions(GroupBy: "label")));
        Assert.DoesNotContain("bug", byLabel.Find(".tags").TextContent);
        IRenderedComponent<WorkItemCard> byWho = ctx.Render<WorkItemCard>(p => p.Add(c => c.Item, item).Add(c => c.Options, new BoardOptions(GroupBy: "contributor")));
        Assert.Empty(byWho.FindAll(".meta-row .who-list"));
        IRenderedComponent<WorkItemCard> byStatus = ctx.Render<WorkItemCard>(p => p.Add(c => c.Item, item).Add(c => c.Options, new BoardOptions()));
        Assert.Contains("bug", byStatus.Find(".tags").TextContent);
        Assert.NotEmpty(byStatus.FindAll(".meta-row .who-list"));
    }

    [Fact(DisplayName = "A card shows its title on a band coloured by status, its feature, its date and people, its subtask count, its coloured label tag and its #wi tag, and no priority tag")]
    public async Task Card()
    {
        await using BunitContext ctx = NewContext();
        WorkItem item = WorkItemsApi.Get(123)! with { Label = "feature" };   // the fixture item has no label
        IRenderedComponent<WorkItemCard> card = ctx.Render<WorkItemCard>(p => p.Add(c => c.Item, item).Add(c => c.Options, new BoardOptions()));

        Assert.Equal("Health-check endpoint", card.Find(".band").TextContent.Trim());
        Assert.Contains("--sun", card.Find(".band").GetAttribute("style"));
        Assert.Equal(["Security"], card.FindAll(".card-features .feat").Select(f => f.TextContent.Trim()));   // its one feature, as a chip
        string meta = card.Find(".meta-row").TextContent;
        Assert.Contains("2/4", meta);   // done/total subtasks (the fixture has two done)
        Assert.Contains("Artyom, Tom", meta);
        Assert.DoesNotContain("high priority", meta);
        Assert.Empty(card.FindAll(".meta-row .ico"));   // plain text, no icons
        Assert.Equal("feature", card.Find(".tag.lbl").TextContent.Trim());
        Assert.Contains("--sky", card.Find(".tag.lbl").GetAttribute("style"));   // filled with the label's colour
        Assert.Contains("#wi123", card.Find(".tags").TextContent);
        Assert.DoesNotContain(card.FindAll(".tag.hot"), t => t.TextContent.Contains("high priority"));
    }

    [Fact(DisplayName = "A card without subtasks or a label shows no count and no label tag")]
    public async Task BareCard()
    {
        await using BunitContext ctx = NewContext();
        (_, _, int id) = WorkItemsApi.Create("Bare", "Backlog", [], []);   // fixture item 107 has subtasks, so make an empty one
        IRenderedComponent<WorkItemCard> card = ctx.Render<WorkItemCard>(p => p.Add(c => c.Item, WorkItemsApi.Get(id)!).Add(c => c.Options, new BoardOptions()));

        Assert.Empty(card.FindAll(".meta-row .subs"));
        Assert.DoesNotContain("/", card.Find(".meta-row").TextContent);
        Assert.Empty(card.FindAll(".tag.lbl"));
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
        Assert.Equal(7, board.FindAll(".lane").Count);   // one lane of cards per column, no subgroups
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

    // The side menu's Organise block: the label of each option and what it shows now.
    private List<string> OrganiseLabels()
    {
        if (nav!.FindAll(".nav-view").Count == 0) nav.Find("button.nav-toggle").Click();
        return nav.FindAll(".organise .field label").Select(l => l.TextContent.Trim()).ToList();
    }

    private string OrganiseValue(string field) =>
        nav!.FindAll(".organise .field").Single(f => f.QuerySelector("label")!.TextContent.Trim() == field).QuerySelector(".rz-dropdown-label")!.TextContent.Trim();

    private List<string> OrganiseChoices(string field)
    {
        AngleSharp.Dom.IElement Field() => nav!.FindAll(".organise .field").Single(f => f.QuerySelector("label")!.TextContent.Trim() == field);
        Field().QuerySelector(".dd")!.Click();
        nav!.WaitForAssertion(() => Assert.NotEmpty(Field().QuerySelectorAll(".rz-dropdown-item")));
        return Field().QuerySelectorAll(".rz-dropdown-item").Select(i => i.TextContent.Trim()).ToList();
    }

    [Fact(DisplayName = "In the list view the organise options read groups and subgroups and both offer none first")]
    public async Task ListOrganiseOptionsAreGroupsAndSubgroups()
    {
        await using BunitContext ctx = NewContext();
        ctx.Render<BoardPage>();
        OpenFreshList();

        List<string> labels = OrganiseLabels();
        Assert.Equal(["sort by", "groups", "subgroups", "colour"], labels);
        Assert.Equal(("none", "none"), (OrganiseValue("groups"), OrganiseValue("subgroups")));
        List<string> fields = ["status", "feature", "label", "priority", "contributor", "due date", "start date"];
        Assert.Equal(["none", .. fields], OrganiseChoices("groups"));
        Assert.Equal(["none", .. fields], OrganiseChoices("subgroups"));
    }

    [Fact(DisplayName = "The board view keeps columns and rows, with status as the default columns and no rows")]
    public async Task BoardOrganiseOptionsStayColumnsAndRows()
    {
        await using BunitContext ctx = NewContext();
        IRenderedComponent<BoardPage> board = ctx.Render<BoardPage>();

        Assert.Equal(["sort by", "columns", "rows", "colour"], OrganiseLabels());
        Assert.Equal(("status", "none"), (OrganiseValue("columns"), OrganiseValue("rows")));
        Assert.Equal(["status", "feature", "label", "priority", "contributor", "due date", "start date"], OrganiseChoices("columns"));   // the board's columns have no "none"
        Assert.Equal(7, board.FindAll("section.col").Count);
    }

    [Fact(DisplayName = "A fresh list view is flat: no group headers, every item once in sort order, and the Status column shows")]
    public async Task FreshListIsFlat()
    {
        await using BunitContext ctx = NewContext();
        IRenderedComponent<BoardPage> board = ctx.Render<BoardPage>();
        OpenFreshList();

        List<int> expected = [.. BoardModel.Sort(WorkItemsApi.GetAll(), new BoardOptions()).Select(i => i.Id)];
        board.WaitForAssertion(() => Assert.Equal(expected, board.FindAll("tr[data-id]").Select(r => int.Parse(r.GetAttribute("data-id")!))));
        Assert.Empty(board.FindAll("tr[data-group]"));
        Assert.Contains("Status", board.FindAll("thead th .th-l").Select(h => h.TextContent.Trim()));

        // sorted another way, the same rows follow it
        SettingsStore settings = ctx.Services.GetRequiredService<SettingsStore>();
        settings.SaveBoard(repo.Root, settings.Board(repo.Root) with { SortBy = "id" });
        board.WaitForAssertion(() => Assert.Equal(expected.Order(), board.FindAll("tr[data-id]").Select(r => int.Parse(r.GetAttribute("data-id")!))));
        Assert.Empty(board.FindAll("tr[data-group]"));
    }

    [Fact(DisplayName = "Choosing a group in the list view groups the rows, a subgroup splits each group, a saved choice is kept, and the board's columns are not touched")]
    public async Task ChoosingListGroups()
    {
        await using BunitContext ctx = NewContext();
        IRenderedComponent<BoardPage> board = ctx.Render<BoardPage>();
        OpenFreshList();
        board.WaitForAssertion(() => Assert.NotEmpty(board.FindAll("tr[data-id]")));
        Assert.Empty(board.FindAll("tr[data-group]"));
        string status = WorkItemsApi.Get(123)!.Status;

        PickField("groups", "status");
        board.WaitForAssertion(() => Assert.Contains(status, board.FindAll("tr[data-group]").Select(g => g.GetAttribute("data-group"))));
        Assert.Contains("123", GroupRows(board, status).Select(r => r.GetAttribute("data-id")));
        Assert.DoesNotContain("Status", board.FindAll("thead th .th-l").Select(h => h.TextContent.Trim()));   // the group says it

        PickField("subgroups", "priority");
        board.WaitForAssertion(() => Assert.NotEmpty(board.FindAll("tr[data-group^='swim:']")));

        PickField("subgroups", "none");
        PickField("groups", "none");
        board.WaitForAssertion(() => Assert.Empty(board.FindAll("tr[data-group]")));
        Assert.NotEmpty(board.FindAll("tr[data-id]"));

        PickField("groups", "label");
        board.WaitForAssertion(() => Assert.NotEmpty(board.FindAll("tr[data-group]")));
        SettingsStore reopened = new(settingsFile);
        BoardOptions saved = reopened.Board(repo.Root);
        Assert.Equal(("label", "none", "status", "none"), (saved.ListGroupBy, saved.ListSwimlane, saved.GroupBy, saved.Swimlane));   // the board's columns stay status

        // the board still has its status columns
        ctx.Services.GetRequiredService<SettingsStore>().SaveBoard(repo.Root, saved with { View = "board" });
        board.WaitForAssertion(() => Assert.Equal(7, board.FindAll("section.col").Count));
    }

    [Fact(DisplayName = "A list saved with groups before the list had its own keeps them when the app opens it")]
    public async Task SavedListKeepsItsGroups()
    {
        await using BunitContext ctx = NewContext();
        SettingsStore settings = ctx.Services.GetRequiredService<SettingsStore>();
        settings.SaveBoard(repo.Root, new BoardOptions(GroupBy: "status", View: "list"));
        IRenderedComponent<BoardPage> board = ctx.Render<BoardPage>();

        string status = WorkItemsApi.Get(123)!.Status;
        board.WaitForAssertion(() => Assert.Contains(status, board.FindAll("tr[data-group]").Select(g => g.GetAttribute("data-group"))));
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

    [Fact(DisplayName = "List rows drag like cards from anywhere in the row after a short hold: in front of another row to reorder, onto a group to change status")]
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
        Assert.Empty(board.FindAll(".grip"));   // no handle: the script grabs the row itself
        string js = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "wwwroot", "js", "app.js"));
        Assert.DoesNotContain(".grip", js);
        Assert.DoesNotContain("HOLD", js);                       // no hold-to-lift any more: a selected row drags after a few pixels
        Assert.Contains("kind: \"row\"", js);                     // a press on a (selected) row is one

        // A render between finding a row and sending it the event drops the event's handler: find the row again until the event lands.
        board.WaitForAssertion(() => GroupRows(board, "Planned")[2].DragStart());
        await board.InvokeAsync(() => { });   // let the dispatcher drain, so the drop targets the current render tree
        board.WaitForAssertion(() => GroupRows(board, "Planned")[0].Drop());
        board.WaitForAssertion(() => Assert.Equal([before[2], before[0], before[1]], RowsIn(board, "Planned")), TimeSpan.FromSeconds(5));   // the grid reloads after a drop; slow under a full-suite load

        board.WaitForAssertion(() => GroupRows(board, "Planned")[0].DragStart());
        await board.InvokeAsync(() => { });
        board.WaitForAssertion(() => Group(board, "In Progress").Drop());   // the group row stands for the group's end
        board.WaitForAssertion(() => Assert.Equal([before[0], before[1]], RowsIn(board, "Planned")), TimeSpan.FromSeconds(5));
        Assert.Equal("In Progress", WorkItemsApi.GetAll().Single(i => i.Title == before[2]).Status);
    }

    private static string AppJs() => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "wwwroot", "js", "app.js"));

    /// <summary>The text of one function in app.js (from "function name(" to the next top-level "function" or listener).</summary>
    private static string JsFunction(string js, string name)
    {
        int start = js.IndexOf("function " + name + "(", StringComparison.Ordinal);
        Assert.True(start >= 0, $"app.js has no function {name}");
        int end = js.IndexOf("\n}", start, StringComparison.Ordinal);
        return js[start..(end < 0 ? js.Length : end)];
    }

    [Fact(DisplayName = "In the list view only a Shift-click selected row is a drag handle: it drags after a few pixels wherever the press lands")]
    public async Task ListRowIsItsOwnDragHandle()
    {
        await using BunitContext ctx = NewContext();
        IRenderedComponent<BoardPage> board = ctx.Render<BoardPage>();
        ShowList();
        board.WaitForAssertion(() => Assert.NotEmpty(board.FindAll("tr[data-id]")));

        // the row itself is what the script grabs: it carries the id and the key the script finds it by, and no handle column or grip
        foreach (AngleSharp.Dom.IElement row in board.FindAll("tr[data-id]"))
        {
            Assert.True(row.HasAttribute("data-row"));
            Assert.Empty(row.QuerySelectorAll(".grip"));
            Assert.False(row.HasAttribute("data-selected"));
        }
        board.FindAll("tr[data-id]").First(r => r.TextContent.Contains("#wi123")).QuerySelector("td.num")!.Click(new MouseEventArgs { ShiftKey = true });
        board.WaitForAssertion(() => Assert.True(board.FindAll("tr[data-id]").First(r => r.TextContent.Contains("#wi123")).HasAttribute("data-selected")));   // what the script reads

        string js = AppJs();
        // text no longer decides and nothing is held: the selection does
        Assert.DoesNotContain("_overText", js);
        Assert.DoesNotContain("_pressOnText", js);
        Assert.DoesNotContain("_liftRow", js);
        string down = js[js.IndexOf("const tr = !(e.shiftKey", StringComparison.Ordinal)..];
        down = down[..down.IndexOf("if (e.target.closest(\"input, button", StringComparison.Ordinal)];
        Assert.Contains("hasAttribute(\"data-selected\")", down);   // only a selected row is pressed as a row
        Assert.DoesNotContain("setTimeout", down);
        string move = js[js.IndexOf("document.addEventListener(\"pointermove\"", StringComparison.Ordinal)..];
        move = move[..move.IndexOf("const d = _drag;", StringComparison.Ordinal)];
        Assert.Contains("moved >= GRAB", move);
        Assert.Contains("_startDrag(e)", move);
        // every selected row goes along: list rows carry data-selected, not the board's "selected" class
        string start = JsFunction(js, "_startDrag");
        Assert.Contains("tr[data-id][data-selected]", start);
        Assert.Contains("p.el.matches(sel)", start);
        // a pill is no control here: a press on it that moves drags the selected row, a still click opens its dropdown
        Assert.DoesNotContain(".dd, .menu-wrap", JsFunction(js, "_rowAt"));
        // over a group's own header row means that group (its end), not the group above it
        Assert.Contains("target = head;", JsFunction(js, "_findSpot"));
    }

    [Fact(DisplayName = "Dragging a selected list row moves every selected row to the drop group in their order")]
    public async Task DraggingASelectedListRowMovesEverySelectedRow()
    {
        await using BunitContext ctx = NewContext();
        Workspace workspace = ctx.Services.GetRequiredService<Workspace>();
        Assert.True(workspace.CreateItem("Planned").Ok);
        Assert.True(workspace.CreateItem("Planned").Ok);
        IRenderedComponent<BoardPage> board = ctx.Render<BoardPage>();
        ShowList();
        board.WaitForAssertion(() => Assert.Equal(3, RowsIn(board, "Planned").Count));
        List<int> ids = GroupRows(board, "Planned").Select(r => int.Parse(r.GetAttribute("data-id")!)).ToList();

        board.WaitForAssertion(() => GroupRows(board, "Planned")[0].QuerySelector("td.num")!.Click(new MouseEventArgs { ShiftKey = true }));
        board.WaitForAssertion(() => Assert.True(GroupRows(board, "Planned")[0].HasAttribute("data-selected")));
        await board.InvokeAsync(() => { });
        board.WaitForAssertion(() => GroupRows(board, "Planned")[2].QuerySelector("td.num")!.Click(new MouseEventArgs { ShiftKey = true }));
        board.WaitForAssertion(() => Assert.True(GroupRows(board, "Planned")[2].HasAttribute("data-selected")));
        Assert.False(GroupRows(board, "Planned")[1].HasAttribute("data-selected"));

        board.WaitForAssertion(() => GroupRows(board, "Planned")[2].DragStart());   // the press is on the last selected row; the first one goes with it
        await board.InvokeAsync(() => { });
        board.WaitForAssertion(() => Group(board, "Confirmed").Drop());

        List<int> Ids(string group) => GroupRows(board, group).Select(r => int.Parse(r.GetAttribute("data-id")!)).ToList();   // the new items share a title
        board.WaitForAssertion(() => Assert.Equal([ids[1]], Ids("Planned")), TimeSpan.FromSeconds(5));
        board.WaitForAssertion(() => Assert.Contains(ids[0], Ids("Confirmed")), TimeSpan.FromSeconds(5));
        List<int> confirmed = Ids("Confirmed");
        Assert.Contains(ids[2], confirmed);
        Assert.True(confirmed.IndexOf(ids[0]) < confirmed.IndexOf(ids[2]));   // in the order they had
        Assert.All(new[] { ids[0], ids[2] }, id => Assert.Equal("Confirmed", WorkItemsApi.GetAll().Single(i => i.Id == id).Status));
        Assert.Equal("Planned", WorkItemsApi.GetAll().Single(i => i.Id == ids[1]).Status);
    }

    [Fact(DisplayName = "A press on an unselected list row selects cells and never starts a drag")]
    public async Task APressOnAnUnselectedListRowSelectsCells()
    {
        await using BunitContext ctx = NewContext();
        IRenderedComponent<BoardPage> board = ctx.Render<BoardPage>();
        ShowList();
        board.WaitForAssertion(() => Assert.NotEmpty(board.FindAll("tr[data-id]")));
        Assert.Empty(board.FindAll("tr[data-id][data-selected]"));   // nothing is selected: every press is a cell selection's

        string js = AppJs();
        // the row press is for selected rows only, so an unselected row's press never reaches _startDrag
        string down = js[js.IndexOf("const tr = !(e.shiftKey", StringComparison.Ordinal)..];
        down = down[..down.IndexOf("if (e.target.closest(\"input, button", StringComparison.Ordinal)];
        Assert.Contains("hasAttribute(\"data-selected\")", down);
        // the cell selection starts from any press on an unselected row, over text or not, as the grid did before
        string cell = js[js.IndexOf("const cell = _cellAt(e.target);", StringComparison.Ordinal)..];
        cell = cell[..cell.IndexOf("_cells.dragging = true;", StringComparison.Ordinal)];
        Assert.DoesNotContain("_pressOnText", cell);
        Assert.Contains("tr[data-selected]", cell);   // a selected row's press is the drag's: it ends a selection instead
    }

    [Fact(DisplayName = "Dropping a list row on another group moves the work item there")]
    public async Task DroppingAListRowOnAnotherGroupMovesIt()
    {
        await using BunitContext ctx = NewContext();
        Workspace workspace = ctx.Services.GetRequiredService<Workspace>();
        Assert.True(workspace.CreateItem("Planned").Ok);
        IRenderedComponent<BoardPage> board = ctx.Render<BoardPage>();
        ShowList();
        board.WaitForAssertion(() => Assert.Equal(2, RowsIn(board, "Planned").Count));
        List<string> before = RowsIn(board, "Planned");

        board.WaitForAssertion(() => GroupRows(board, "Planned")[1].DragStart());
        await board.InvokeAsync(() => { });
        board.WaitForAssertion(() => Group(board, "Confirmed").Drop());
        board.WaitForAssertion(() => Assert.Equal([before[0]], RowsIn(board, "Planned")), TimeSpan.FromSeconds(5));
        board.WaitForAssertion(() => Assert.Contains(before[1], RowsIn(board, "Confirmed")), TimeSpan.FromSeconds(5));
        Assert.Equal("Confirmed", WorkItemsApi.GetAll().Single(i => i.Title == before[1]).Status);
    }

    [Fact(DisplayName = "Dropping a list row above another row in my order reorders them")]
    public async Task DroppingAListRowAboveAnotherReordersThem()
    {
        await using BunitContext ctx = NewContext();
        Workspace workspace = ctx.Services.GetRequiredService<Workspace>();
        Assert.True(workspace.CreateItem("Planned").Ok);
        IRenderedComponent<BoardPage> board = ctx.Render<BoardPage>();
        ShowList();
        board.WaitForAssertion(() => Assert.Equal(2, RowsIn(board, "Planned").Count));
        List<string> before = RowsIn(board, "Planned");

        board.WaitForAssertion(() => GroupRows(board, "Planned")[1].DragStart());
        await board.InvokeAsync(() => { });
        board.WaitForAssertion(() => GroupRows(board, "Planned")[0].Drop());   // dropped on a row: in front of it
        board.WaitForAssertion(() => Assert.Equal([before[1], before[0]], RowsIn(board, "Planned")), TimeSpan.FromSeconds(5));
        Assert.Equal("Planned", WorkItemsApi.GetAll().Single(i => i.Title == before[1]).Status);   // still in the same group
    }

    [Fact(DisplayName = "The list has no grip column, and a title lines up under the Title header whether or not its row has subtasks")]
    public async Task ListTitlesLineUp()
    {
        await using BunitContext ctx = NewContext();
        Workspace workspace = ctx.Services.GetRequiredService<Workspace>();
        int bare = workspace.CreateItem("Planned").Id;   // no subtasks; the fixture's 107 and 123 have some
        IRenderedComponent<BoardPage> board = ctx.Render<BoardPage>();
        ShowList();
        board.WaitForAssertion(() => Assert.NotEmpty(board.FindAll($"tr[data-id='{bare}']")));

        Assert.Empty(board.FindAll(".grip, td.rowctl, th.rowctl"));   // no gutter column with a drag handle
        Assert.Empty(board.FindAll(".chev-slot"));                    // no reserved slot: it pushed every title to the right
        AngleSharp.Dom.IElement bareCell = board.Find($"tr[data-id='{bare}'] .tcell");
        Assert.Empty(bareCell.QuerySelectorAll(".chev"));
        Assert.Equal("row-title", bareCell.FirstElementChild!.ClassName);   // the title is the cell's first child: nothing before it pushes it right
        AngleSharp.Dom.IElement withSubtasks = board.Find("tr[data-id='123'] .tcell");
        Assert.Equal("tcell", withSubtasks.QuerySelector(".chev")!.ParentElement!.ClassName);   // the chevron hangs off the cell, not out of a slot

        // the header and the cell share one left padding, and the chevron sits in it (css is not evaluated here: read the rule)
        Assert.Contains(board.FindAll("thead th"), th => th.ClassList.Contains("title"));
        string css = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "wwwroot", "css", "app.css"));
        Assert.Contains(".tbl-wrap th.title, .tbl-wrap td.title { padding-left:", css);
        Assert.Contains(".tbl-wrap .tcell .chev { position: absolute;", css);
        Assert.DoesNotContain(".grip", css);
    }

    [Fact(DisplayName = "The list's group rows carry their column and row labels, and the sticky header has a place that shows the path of the rows scrolled under it")]
    public async Task ListHeaderShowsPath()
    {
        await using BunitContext ctx = NewContext();
        Assert.True(WorkItemsApi.SetLabel(123, "bug").Success);
        TwoProjects(ctx);
        IRenderedComponent<BoardPage> board = ctx.Render<BoardPage>();
        PickSwimlanes("label");
        ShowList();
        board.WaitForAssertion(() => Assert.NotEmpty(board.FindAll("tr[data-group='swim:bug']")));

        Assert.Equal([NameOf(repo), NameOf(repo2)], board.FindAll("tr[data-group^='proj:']").Select(r => r.GetAttribute("data-path-proj")));   // the project, when several
        string status = WorkItemsApi.Get(123)!.Status;
        AngleSharp.Dom.IElement column = board.FindAll("tr[data-group]").First(r => r.GetAttribute("data-group") == status);
        Assert.Equal(status, column.GetAttribute("data-path-col"));
        Assert.StartsWith("var(--", column.GetAttribute("data-path-fill"));   // a status column's pill colour
        Assert.Contains("bug", board.FindAll("tr[data-group='swim:bug']").Select(r => r.GetAttribute("data-path-swim")));
        Assert.All(board.FindAll("tr[data-group^='swim:']").Where(r => r.GetAttribute("data-group") != "swim:"), r => Assert.NotNull(r.GetAttribute("data-path-swim")));

        Assert.Single(board.FindAll(".tbl-wrap .hdr-path"));   // the sticky header's place for the path; app.js fills it as the list scrolls
        string js = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "wwwroot", "js", "app.js"));
        Assert.Contains("_updatePath", js);
        Assert.Contains("pathCol", js);
        Assert.Contains("pathSwim", js);
        Assert.Contains("pathProj", js);
        Assert.Contains("passive: true", js);   // the scroll listener never blocks scrolling
        string css = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "wwwroot", "css", "app.css"));
        Assert.Contains(".tbl-wrap .hdr-path { position: sticky; top: 0;", css);
    }

    [Fact(DisplayName = "Every list column's header can hide it: a long label in a narrow column is shortened so its × stays inside the header and clickable")]
    public async Task EveryListColumnHides()
    {
        await using BunitContext ctx = NewContext();
        IRenderedComponent<BoardPage> board = ctx.Render<BoardPage>();
        ShowList();
        board.WaitForAssertion(() => Assert.NotEmpty(board.FindAll("thead th .th-hide")));
        foreach (AngleSharp.Dom.IElement th in board.FindAll("thead th").Where(t => t.QuerySelector(".th-l") is not null))
            Assert.NotNull(th.QuerySelector(".th-hide"));   // every labelled header has its ×

        // in the real app the label sits in Radzen's title box, which clips what overflows it: a label wider than a narrow column
        // pushed the × past the clip, where clicks reached the reorder layer under it. The label shrinks instead, the × never does.
        string css = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "wwwroot", "css", "app.css")).Replace("\r\n", "\n");
        string label = System.Text.RegularExpressions.Regex.Matches(css, @"\n\s*\.tbl-wrap \.th-l\s*\{([^}]*)\}").Last().Groups[1].Value;
        Assert.Matches(@"min-width:\s*0", label);
        Assert.Matches(@"overflow:\s*hidden", label);
        Assert.Matches(@"text-overflow:\s*ellipsis", label);

        // and the × is the last thing in the header, never inside the clipped label
        Assert.All(board.FindAll("thead th .th-hide"), b => Assert.Null(b.Closest(".th-l")));
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

    [Fact(DisplayName = "A number used twice is resolved as soon as the app sees it: a toast says which change was renumbered, and that change gets a note saying so")]
    public async Task DuplicateIdsResolvedOnChange()
    {
        await using BunitContext ctx = NewContext();
        Workspace workspace = ctx.Services.GetRequiredService<Workspace>();
        IRenderedComponent<BoardPage> board = ctx.Render<BoardPage>();
        EditingWorkItemsTests.Duplicate(repo);

        workspace.Notify();   // what the file watcher does when the folder appears

        overlays!.WaitForAssertion(() => Assert.Contains("second-check is now #wi126", overlays.Find(".toast").TextContent));
        Assert.Contains(Notes.NotesApi.GetForWorkItem(126), n => n.Title.Contains("#wi123"));
        board.WaitForAssertion(() => Assert.Empty(board.FindAll(".warning")));
    }

    [Fact(DisplayName = "List cells wear their colours: status, label and priority are filled pills, and a status group's name wears its band")]
    public async Task ListColours()
    {
        await using BunitContext ctx = NewContext();
        IRenderedComponent<BoardPage> board = ctx.Render<BoardPage>();
        ShowList();

        AngleSharp.Dom.IElement row = board.FindAll("tr[data-id]").First(r => r.TextContent.Contains("#wi123"));
        Assert.Equal(2, row.QuerySelectorAll(".cell.filled[style*='--fill']").Length);   // label and priority; the status is the group's
        Assert.Contains("--fill:var(--sun)", Group(board, "In Progress").QuerySelector(".gname")!.GetAttribute("style"));
    }

    [Fact(DisplayName = "In the list, an item's one feature shows in the Feature column")]
    public async Task ListFeaturesOneLine()
    {
        await using BunitContext ctx = NewContext();
        IRenderedComponent<BoardPage> board = ctx.Render<BoardPage>();
        ShowList();

        AngleSharp.Dom.IElement row = board.FindAll("tr[data-id]").Single(r => r.TextContent.Contains("Health-check"));

        Assert.Equal(["Security"], row.QuerySelectorAll(".feat").Select(t => t.TextContent.Trim()));
        Assert.Contains(board.FindAll("th"), h => h.TextContent.Contains("Feature") && !h.TextContent.Contains("Features"));   // the header also holds its hide control
    }

    [Fact(DisplayName = "Hidden work items leave the board until 'show all hidden' is switched on")]
    public async Task HiddenItems()
    {
        await using BunitContext ctx = NewContext();
        WorkItemsApi.Hide(107);
        IRenderedComponent<BoardPage> board = ctx.Render<BoardPage>();
        Assert.Equal(3, board.FindAll("article.card").Count);

        nav!.Find("button.nav-toggle").Click();   // the expanded side menu holds the view options
        nav.Find("button#f-hidden").Click();

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
        ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        IRenderedComponent<Dropdown> dd = ctx.Render<Dropdown>(p => p
            .Add(c => c.Label, "example")
            .Add(c => c.Options, [("a", "Alpha"), ("b", "Beta")])
            .Add(c => c.Value, "mystery")
            .Add(c => c.Placeholder, "pick one"));

        Assert.Equal("mystery", dd.Find(".dd .rz-dropdown-label").TextContent.Trim());
    }

    [Fact(DisplayName = "Opening the group-by dropdown and picking feature regroups the board into one column per feature")]
    public async Task DropdownOpensAndPicks()
    {
        await using BunitContext ctx = NewContext();
        IRenderedComponent<BoardPage> board = ctx.Render<BoardPage>();

        nav!.Find("button.nav-toggle").Click();
        PickField("columns", "feature");

        board.WaitForAssertion(() => Assert.Contains("Banking", Columns(board)));
        Assert.Empty(board.FindAll(".cat-h"));   // no supergroups
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

    [Fact(DisplayName = "Feature grouping shows every feature as a column in one row, with no supergroup headings")]
    public async Task FeatureColumns()
    {
        await using BunitContext ctx = NewContext();
        IRenderedComponent<BoardPage> board = ctx.Render<BoardPage>();
        GroupByFeature();

        Assert.Empty(board.FindAll(".cat-h"));
        Assert.Single(board.FindAll(".cats"));
        Assert.Contains("Banking", Columns(board));
        Assert.Contains("Availability", Columns(board));
    }

    [Fact(DisplayName = "An expanded list row numbers its subtasks 1.1, 1.2 …, and only that row unfolds")]
    public async Task ListSubtaskNumbers()
    {
        await using BunitContext ctx = NewContext();
        IRenderedComponent<BoardPage> board = ctx.Render<BoardPage>();
        ShowList();
        board.WaitForAssertion(() => Assert.Empty(board.FindAll(".subrow")));   // folded by default: open one row by hand, only its subtasks show
        board.WaitForAssertion(() => board.FindAll("tr[data-id]").First(r => r.TextContent.Contains("#wi123")).QuerySelector("button.chev")!.Click());

        board.WaitForAssertion(() => Assert.Equal(["1.1", "1.2", "1.3", "2.1"], board.FindAll(".subrow .num").Select(td => td.TextContent.Trim())));
    }

    [Fact(DisplayName = "Completed rows have editable label and priority but status stays read-only, and Completed has no '+ Add item'")]
    public async Task CompletedRowsReadOnly()
    {
        await using BunitContext ctx = NewContext();
        IRenderedComponent<BoardPage> board = ctx.Render<BoardPage>();
        ShowList();

        int groups = board.FindAll("tr[data-group]").Count;
        Assert.Equal(groups - 1, board.FindAll("button").Count(b => b.TextContent.Trim() == "+ Add item"));   // every group but Completed

        // grouped by label the Status column shows: a completed row's status is a read-only pill, its priority stays editable
        SettingsStore settings = ctx.Services.GetRequiredService<SettingsStore>();
        settings.SaveBoard(repo.Root, settings.Board(repo.Root) with { ListGroupBy = "label" });
        board.WaitForAssertion(() => Assert.Contains(board.FindAll("tr[data-id]"), r => r.TextContent.Contains("#wi124") && r.TextContent.Contains("Completed")));
        AngleSharp.Dom.IElement row = board.FindAll("tr[data-id]").First(r => r.TextContent.Contains("#wi124"));
        Assert.NotEmpty(row.QuerySelectorAll(".dd"));   // priority dropdown
        Assert.NotNull(row.QuerySelector("span.box.cell.filled"));   // status: read-only
        Assert.DoesNotContain(row.QuerySelectorAll(".dd"), d => d.TextContent.Contains("Completed"));
    }

    [Fact(DisplayName = "In the list, subtasks start folded away; the rail's subtasks button, selected, shows every row's subtasks, and unselected folds them again")]
    public async Task CompactHidesOnlySubtasks()
    {
        await using BunitContext ctx = NewContext();
        IRenderedComponent<BoardPage> board = ctx.Render<BoardPage>();
        ShowList();
        Assert.Equal(4, board.FindAll("tr[data-id]").Count);
        board.WaitForAssertion(() => Assert.Empty(board.FindAll(".subrow")));
        Assert.Equal("false", nav!.Find("button.compact-btn").GetAttribute("aria-pressed"));

        nav.Find("button.compact-btn").Click();
        board.WaitForAssertion(() => Assert.Equal(4 + 2 + 1 + 1, board.FindAll(".subrow").Count));
        Assert.Equal("true", nav.Find("button.compact-btn").GetAttribute("aria-pressed"));
        Assert.Contains("is-sel", nav.Find("button.compact-btn").ClassList);
        Assert.Equal(4, board.FindAll("tr[data-id]").Count);   // the work items stay; only their subtasks unfold

        nav.Find("button.compact-btn").Click();
        board.WaitForAssertion(() => Assert.Empty(board.FindAll(".subrow")));
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

    [Fact(DisplayName = "A hidden work item's row is marked as hidden in the list")]
    public async Task HiddenRowMarked()
    {
        await using BunitContext ctx = NewContext();
        WorkItemsApi.Hide(107);
        IRenderedComponent<BoardPage> board = ctx.Render<BoardPage>();
        ShowList();
        nav!.Find("button.nav-toggle").Click();   // the expanded side menu holds the view options
        nav.Find("button#f-hidden").Click();

        Assert.True(board.FindAll("tr[data-id]").Single(r => r.TextContent.Contains("#wi107")).HasAttribute("data-hidden"));
    }

    [Fact(DisplayName = "Merging work items confirms it with a toast")]
    public async Task MergeToast()
    {
        await using BunitContext ctx = NewContext();
        IRenderedComponent<BoardPage> board = ctx.Render<BoardPage>();
        for (int i = 0; i < 2; i++) board.FindAll("article.card")[i].Click(new MouseEventArgs { CtrlKey = true });   // re-found: each click re-renders

        board.Find(".actionbar button.primary").Click();
        overlays!.WaitForAssertion(() => Assert.Equal(2, overlays.FindAll(".dialog .choice").Count));   // asks which one is the main item
        overlays.FindAll(".dialog .choice")[0].Click();

        overlays.WaitForAssertion(() => Assert.Contains("Merged into #wi", overlays.Find(".toast").TextContent));
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

        board.WaitForAssertion(() => Assert.Contains(board.FindAll(".menu-item"), i => i.TextContent.Contains("Hide column")));
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
        Assert.Contains("Organise", nav.Find(".nav-view").TextContent);
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
        Assert.Empty(nav.FindAll(".nav-hidden .grp-row"));   // nothing hidden, nothing listed under the Hidden header

        board.FindAll("header.col-h").First(h => h.TextContent.Contains("Blocked")).Click();
        board.FindAll(".menu-item").Single(i => i.TextContent.Trim() == "Hide column").Click();

        Assert.DoesNotContain("Blocked", Columns(board));
        Assert.Equal("0", nav.Find(".nav-hidden .grp-row .count").TextContent.Trim());   // how many items it holds
        nav.Find("button[aria-label='Show Blocked']").Click();
        Assert.Contains("Blocked", Columns(board));
        Assert.Empty(nav.FindAll(".nav-hidden .grp-row"));
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

    [Fact(DisplayName = "Grouped by feature, a completed card drags to another feature like any other")]
    public async Task DragCompletedBetweenFeatures()
    {
        await using BunitContext ctx = NewContext();
        SettingsStore settings = ctx.Services.GetRequiredService<SettingsStore>();
        settings.SaveBoard(repo.Root, settings.Board(repo.Root) with { GroupBy = "feature" });
        IRenderedComponent<BoardPage> board = ctx.Render<BoardPage>();
        string timeout = WorkItemsApi.Get(124)!.Title;
        Assert.Contains(timeout, CardsIn(board, "Security"));

        Col(board, "Security").QuerySelectorAll("article.card").First(c => c.TextContent.Contains("#wi124")).DragStart();
        Col(board, "Availability").Drop();

        board.WaitForAssertion(() => Assert.Contains(timeout, CardsIn(board, "Availability")));
        Assert.DoesNotContain(timeout, CardsIn(board, "Security"));
    }

    [Fact(DisplayName = "A card shows in one feature column only, and dragged to another feature it leaves no copy behind, not even under its own spec")]
    public async Task DragMultiFeatureCard()
    {
        await using BunitContext ctx = NewContext();
        SettingsStore settings = ctx.Services.GetRequiredService<SettingsStore>();
        settings.SaveBoard(repo.Root, settings.Board(repo.Root) with { GroupBy = "feature" });
        IRenderedComponent<BoardPage> board = ctx.Render<BoardPage>();
        string health = WorkItemsApi.Get(123)!.Title;
        Assert.Contains(health, CardsIn(board, "Security"));   // picked by hand
        Assert.DoesNotContain(health, CardsIn(board, "Availability"));   // its spec, not shown as a second feature

        Col(board, "Security").QuerySelectorAll("article.card").First(c => c.TextContent.Contains("#wi123")).DragStart();
        Col(board, "Banking").Drop();

        board.WaitForAssertion(() => Assert.Contains(health, CardsIn(board, "Banking")));
        Assert.DoesNotContain(health, CardsIn(board, "Security"));
        Assert.DoesNotContain(health, CardsIn(board, "Availability"));
    }

    [Fact(DisplayName = "Grouped by due date the columns are Overdue, Today, This week, Next week, Later and No due date, and the period filter narrows the board to one of them")]
    public async Task GroupByDueAndPeriodFilter()
    {
        await using BunitContext ctx = NewContext();
        DateOnly today = DateOnly.FromDateTime(DateTime.Today);
        WorkItemsApi.SetDue(123, today);
        WorkItemsApi.SetDue(107, today.AddDays(-3));
        IRenderedComponent<BoardPage> board = ctx.Render<BoardPage>();

        nav!.Find("button.nav-toggle").Click();
        PickField("columns", "due date");
        board.WaitForAssertion(() => Assert.Equal(["Overdue", "Today", "This week", "Next week", "Later", "No due date"], Columns(board)));
        Assert.Contains("Health-check endpoint", CardsIn(board, "Today"));
        Assert.Contains(WorkItemsApi.Get(107)!.Title, CardsIn(board, "Overdue"));
        Assert.Single(board.FindAll("tr[data-id], article.card").Where(c => c.TextContent.Contains("Health-check")));

        nav.FindAll(".field").Single(f => f.TextContent.Contains("period")).QuerySelector(".dd")!.Click();
        nav.FindAll(".rz-dropdown-item").First(i => i.TextContent.Trim() == "overdue").Click();
        board.WaitForAssertion(() => Assert.Single(board.FindAll("article.card")));
        Assert.Contains(WorkItemsApi.Get(107)!.Title, board.Find("article.card").TextContent);
    }

    [Fact(DisplayName = "A card shows its due date, flagged when overdue; the list has Started and Due columns")]
    public async Task DueOnCardsAndRows()
    {
        await using BunitContext ctx = NewContext();
        DateOnly today = DateOnly.FromDateTime(DateTime.Today);
        WorkItemsApi.SetDue(123, today.AddDays(-1));
        WorkItemsApi.SetStarted(123, today.AddDays(-5));
        WorkItemsApi.SetDue(107, today.AddDays(2));
        IRenderedComponent<BoardPage> board = ctx.Render<BoardPage>();

        AngleSharp.Dom.IElement overdue = board.FindAll("article.card").First(c => c.TextContent.Contains("Health-check")).QuerySelector(".tag.due")!;
        Assert.Contains("overdue", overdue.ClassList);
        Assert.Contains(today.AddDays(-1).ToString("d MMM"), overdue.TextContent);
        AngleSharp.Dom.IElement soon = board.FindAll("article.card").First(c => c.TextContent.Contains("#wi107")).QuerySelector(".tag.due")!;
        Assert.DoesNotContain("overdue", soon.ClassList);
        Assert.Empty(board.FindAll("article.card").Where(c => c.TextContent.Contains("#wi125")).Select(c => c.QuerySelector(".tag.due")).Where(t => t is not null));

        ShowList();
        board.WaitForAssertion(() => Assert.Contains(board.FindAll("thead th"), h => h.TextContent.Contains("Due")));
        Assert.Contains(board.FindAll("thead th"), h => h.TextContent.Contains("Started"));
        AngleSharp.Dom.IElement row = board.FindAll("tr[data-id='123']").First();
        Assert.Contains(today.AddDays(-5).ToString("d MMM"), row.TextContent);
        Assert.Contains(today.AddDays(-1).ToString("d MMM"), row.TextContent);
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
        GroupByFeature();
        board.FindAll("header.col-h").First(h => h.TextContent.Contains("Payments")).Click();
        board.FindAll(".menu-item").Single(i => i.TextContent.Trim() == "Delete feature…").Click();
        overlays!.WaitForAssertion(() => Assert.Contains("Delete Payments?", overlays.Find(".dialog").TextContent));
        overlays.FindAll(".dialog button").Single(b => b.TextContent.Trim() == "Delete").Click();

        board.WaitForAssertion(() => Assert.DoesNotContain("Payments", Columns(board)));
        Assert.Contains($"#wi{id}", board.FindAll("section.col").Single(c => c.QuerySelector("h3")!.TextContent == "Unassigned").TextContent);
    }

    [Fact(DisplayName = "A feature with a spec folder can be deleted too: the dialog says its folder goes, and the column leaves the board")]
    public async Task DeleteSpecFeature()
    {
        await using BunitContext ctx = NewContext();
        repo.Write("openspec/specs/functional/ledger/spec.md", "# ledger Specification\n\n## Purpose\nBooks.\n\n## Requirements\n\n### Requirement: Ledger\nThe ledger SHALL balance.\n\n#### Scenario: Balanced\n- **WHEN** posted\n- **THEN** it balances\n");
        IRenderedComponent<BoardPage> board = ctx.Render<BoardPage>();
        GroupByFeature();
        board.WaitForAssertion(() => Assert.Contains("Ledger", Columns(board)));

        board.FindAll("header.col-h").First(h => h.TextContent.Contains("Ledger")).Click();
        board.FindAll(".menu-item").Single(i => i.TextContent.Trim() == "Delete feature…").Click();
        overlays!.WaitForAssertion(() => Assert.Contains("openspec/specs/functional/ledger", overlays.Find(".dialog").TextContent));
        overlays.FindAll(".dialog button").Single(b => b.TextContent.Trim() == "Delete").Click();

        board.WaitForAssertion(() => Assert.DoesNotContain("Ledger", Columns(board)));
        Assert.False(Directory.Exists(repo.PathOf("openspec/specs/functional/ledger")));
    }

    [Fact(DisplayName = "Deleting a feature that an open item still specs says which item in the dialog, then removes the column")]
    public async Task DeleteFeatureSpeccedByOpenItem()
    {
        await using BunitContext ctx = NewContext();
        IRenderedComponent<BoardPage> board = ctx.Render<BoardPage>();
        GroupByFeature();
        board.WaitForAssertion(() => Assert.Contains("Availability", Columns(board)));

        board.FindAll("header.col-h").First(h => h.TextContent.Contains("Availability")).Click();
        board.FindAll(".menu-item").Single(i => i.TextContent.Trim() == "Delete feature…").Click();
        overlays!.WaitForAssertion(() => Assert.Contains("#wi123 still describes it in its specs", overlays.Find(".dialog").TextContent));
        overlays.FindAll(".dialog button").Single(b => b.TextContent.Trim() == "Delete").Click();

        board.WaitForAssertion(() => Assert.DoesNotContain("Availability", Columns(board)));
    }

    [Fact(DisplayName = "Rename feature… in a feature column's menu asks for the new name, prefilled, and the column takes it")]
    public async Task RenameFeatureColumn()
    {
        await using BunitContext ctx = NewContext();
        IRenderedComponent<BoardPage> board = ctx.Render<BoardPage>();
        GroupByFeature();
        board.WaitForAssertion(() => Assert.Contains("Banking", Columns(board)));

        board.FindAll("header.col-h").First(h => h.TextContent.Contains("Banking")).Click();
        board.FindAll(".menu-item").Single(i => i.TextContent.Trim() == "Rename feature…").Click();
        overlays!.WaitForAssertion(() => Assert.Equal("Banking", overlays.Find(".dialog input").GetAttribute("value")));
        overlays.Find(".dialog input").Input("Core banking");
        overlays.FindAll(".dialog button").Single(b => b.TextContent.Trim() == "Rename").Click();

        board.WaitForAssertion(() => Assert.Contains("Core banking", Columns(board)));
        Assert.DoesNotContain("Banking", Columns(board));
        Assert.True(Directory.Exists(repo.PathOf("openspec/specs/functional/core-banking")));
    }

    [Fact(DisplayName = "Grouped by feature, + feature at the end of the row adds a feature and its column appears")]
    public async Task AddFeatureColumn()
    {
        await using BunitContext ctx = NewContext();
        IRenderedComponent<BoardPage> board = ctx.Render<BoardPage>();
        GroupByFeature();

        board.Find("button.add-col").Click();
        overlays!.WaitForAssertion(() => overlays.Find(".dialog input").Input("Exports"));
        overlays.FindAll(".dialog button").Single(b => b.TextContent.Trim() == "Create").Click();

        board.WaitForAssertion(() => Assert.Contains("Exports", Columns(board)));
    }

    // The layout dropdown in the side menu switches the board to the list, as it opens on a fresh repository: flat.
    private void OpenFreshList()
    {
        nav!.Find("button[aria-label=Layout]").Click();
        nav.FindAll(".menu-item").Single(i => i.TextContent.Trim() == "List").Click();
    }

    // The list grouped like the board is: the list's groups and subgroups take the board's columns and rows (most list tests are about grouped rows).
    private void ShowList()
    {
        OpenFreshList();
        SettingsStore settings = nav!.Services.GetRequiredService<SettingsStore>();
        BoardOptions o = settings.Board(repo.Root);
        settings.SaveBoard(repo.Root, o with { ListGroupBy = o.GroupBy, ListSwimlane = o.Swimlane });
    }

    private void GroupByFeature()
    {
        nav!.Find("button.nav-toggle").Click();
        PickField("columns", "feature");
    }

    private static List<string> Columns(IRenderedComponent<BoardPage> board) => board.FindAll("section.col h3").Select(h => h.TextContent).ToList();

    public void Dispose()
    {
        repo.Dispose();
        repo2.Dispose();
        File.Delete(settingsFile);
    }

    /// <summary>Opens repo2 as a second tab and comes back to repo as the active one.</summary>
    private void TwoProjects(BunitContext ctx)
    {
        Workspace ws = ctx.Services.GetRequiredService<Workspace>();
        Assert.True(ws.OpenTab(repo2.Root).Success);
        Assert.True(ws.Switch(repo.Root).Success);   // back to the first project, as its name's menu does: its arrangement is shown, the tabs keep their order
    }

    private static string NameOf(TestRepo r) => Path.GetFileName(r.Root);

    [Fact(DisplayName = "Projects reorder like cards: Shift-click a project's heading, then the up arrow moves it above the next unselected project; the order is remembered")]
    public async Task ReorderProjectsOnTheBoard()
    {
        await using BunitContext ctx = NewContext();
        TwoProjects(ctx);
        IRenderedComponent<BoardPage> board = ctx.Render<BoardPage>();
        board.WaitForAssertion(() => Assert.Equal([NameOf(repo), NameOf(repo2)], board.FindAll(".proj-h h2").Select(h => h.TextContent.Trim())));

        board.FindAll(".proj-h")[1].Click(new MouseEventArgs { ShiftKey = true });   // anywhere in the heading row
        board.WaitForAssertion(() => Assert.Contains("selected", board.FindAll(".proj-h")[1].ClassName));
        await board.InvokeAsync(() => { });
        board.Find(".board-wrap").KeyDown("ArrowUp");

        board.WaitForAssertion(() => Assert.Equal([NameOf(repo2), NameOf(repo)], board.FindAll(".proj-h h2").Select(h => h.TextContent.Trim())));
        Assert.Equal([repo2.Root, repo.Root], ctx.Services.GetRequiredService<SettingsStore>().Current.OpenRepos);
        board.Find(".board-wrap").KeyDown("ArrowUp");   // already first: stays
        Assert.Equal([repo2.Root, repo.Root], ctx.Services.GetRequiredService<SettingsStore>().Current.OpenRepos);
        Assert.Contains(".proj-h.selected", File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "wwwroot", "js", "app.js")));   // arrows reach the page while a project is selected
    }

    [Fact(DisplayName = "In the list, Shift-click a project's group row and the down arrow moves the project below the next one")]
    public async Task ReorderProjectsInTheList()
    {
        await using BunitContext ctx = NewContext();
        TwoProjects(ctx);
        IRenderedComponent<BoardPage> board = ctx.Render<BoardPage>();
        ShowList();
        board.WaitForAssertion(() => Assert.Equal(2, board.FindAll("tr[data-group^='proj:']").Count));

        board.FindAll("tr[data-group^='proj:']")[0].Click(new MouseEventArgs { ShiftKey = true });   // anywhere in the group row
        board.WaitForAssertion(() => Assert.NotNull(board.FindAll("tr[data-group^='proj:']")[0].GetAttribute("data-selected")));
        await board.InvokeAsync(() => { });
        board.Find(".tbl-wrap").KeyDown("ArrowDown");

        board.WaitForAssertion(() => Assert.Contains(NameOf(repo2), board.FindAll("tr[data-group^='proj:']")[0].TextContent));
        Assert.Equal([repo2.Root, repo.Root], ctx.Services.GetRequiredService<SettingsStore>().Current.OpenRepos);
    }

    [Fact(DisplayName = "Selecting projects and selecting cards are one or the other, and a plain click on the board clears a project selection")]
    public async Task ProjectSelectionClears()
    {
        await using BunitContext ctx = NewContext();
        TwoProjects(ctx);
        IRenderedComponent<BoardPage> board = ctx.Render<BoardPage>();
        board.WaitForAssertion(() => Assert.Equal(2, board.FindAll(".proj").Count));
        bool ProjSelected() => board.FindAll(".proj-h.selected").Count > 0;

        board.FindAll(".proj-h")[1].Click(new MouseEventArgs { ShiftKey = true });
        board.WaitForAssertion(() => Assert.True(ProjSelected()));
        board.FindAll(".proj article.card")[0].Click(new MouseEventArgs { ShiftKey = true });   // a card: the project lets go
        board.WaitForAssertion(() => Assert.False(ProjSelected()));
        Assert.Single(board.FindAll("article.card.selected"));

        board.FindAll(".proj-h")[1].Click(new MouseEventArgs { ShiftKey = true });   // a project: the card lets go
        board.WaitForAssertion(() => Assert.True(ProjSelected()));
        Assert.Empty(board.FindAll("article.card.selected"));
        board.Find(".board-wrap").Click();   // a plain click elsewhere
        board.WaitForAssertion(() => Assert.False(ProjSelected()));
    }

    [Fact(DisplayName = "Selection has one look everywhere: a solid line in the text colour round the item and a yellow halo outside it, never a fill")]
    public void OneSelectionLook()
    {
        string css = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "wwwroot", "css", "app.css"));
        Assert.Matches(@"\.card\.selected[^{]*\.proj-h\.selected[^{]*tr\[data-selected\][^{]*\{[^}]*outline: 3px solid var\(--ink\)[^}]*box-shadow: 0 0 0 7px var\(--sun\)", css);
        Assert.DoesNotMatch(@"tr\[data-selected\][^{]*\{[^}]*background", css);
    }

    [Fact(DisplayName = "Agent-added work carries no agent chip on its card or list row; the review click is on the item page, the filter still finds it")]
    public async Task AgentChipOnCard()
    {
        EditingWorkItemsTests.StampAgent(repo);
        await using BunitContext ctx = NewContext();
        IRenderedComponent<BoardPage> board = ctx.Render<BoardPage>();
        Assert.Empty(board.FindAll("article.card .agent-chip"));
        Assert.True(WorkItemsApi.Get(123)!.AgentUnreviewed);   // still flagged for the filter and the item page

        ShowList();
        board.WaitForAssertion(() => Assert.NotEmpty(board.FindAll("tr[data-id]")));
        Assert.Empty(board.FindAll("tr[data-id] .tcell .agent-chip"));
    }

    [Fact(DisplayName = "In the list an agent subtask carries a chip whose click clears that subtask; the row itself has none")]
    public async Task AgentChipsInList()
    {
        EditingWorkItemsTests.StampAgent(repo);
        repo.Write("openspec/changes/add-health-check/tasks.md", repo.Read("openspec/changes/add-health-check/tasks.md").TrimEnd() + "\n- [ ] 2.2 Agent drafted this <!-- agent: Claude -->\n");
        await using BunitContext ctx = NewContext();
        IRenderedComponent<BoardPage> board = ctx.Render<BoardPage>();
        ShowList();
        board.WaitForAssertion(() => Assert.NotEmpty(board.FindAll("tr[data-id]")));
        board.FindAll("tr[data-id]").First(r => r.TextContent.Contains("#wi123")).QuerySelector("button.chev")!.Click();   // subtasks start folded
        board.WaitForAssertion(() => Assert.NotEmpty(board.FindAll(".subrow")));
        Assert.Single(board.FindAll(".subrow .agent-chip"));

        board.Find(".subrow .agent-chip").Click();

        board.WaitForAssertion(() => Assert.Empty(board.FindAll(".subrow .agent-chip")));
        Assert.True(WorkItemsApi.Get(123)!.AgentUnreviewed);   // the item itself is still flagged
        Assert.DoesNotContain("<!-- agent", repo.Read("openspec/changes/add-health-check/tasks.md"));
    }

    [Fact(DisplayName = "The board's column headers stay at the top while the board scrolls")]
    public void StickyColumnHeaders()
    {
        string css = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "wwwroot", "css", "app.css"));
        Assert.Matches(@"\.board-wrap \.head-row[^{]*\{[^}]*position: sticky", css);   // the shared header row (several projects)
        Assert.Matches(@"\.board-wrap \.col > \.menu-wrap[^{]*\{[^}]*position: sticky", css);   // each column's own header
    }

    [Fact(DisplayName = "With two repositories open, the board stacks a section per project under one shared header row; alone, a project shows no header")]
    public async Task TwoProjectsOnTheBoard()
    {
        await using BunitContext ctx = NewContext();
        IRenderedComponent<BoardPage> board = ctx.Render<BoardPage>();
        Assert.Empty(board.FindAll(".proj-h"));
        TwoProjects(ctx);

        board.WaitForAssertion(() => Assert.Equal([NameOf(repo), NameOf(repo2)], board.FindAll(".proj-h h2").Select(h => h.TextContent.Trim())));
        Assert.Equal(7, board.FindAll(".head-row header.col-h").Count);   // status columns align, so one header row serves both
        Assert.Empty(board.FindAll(".proj header.col-h"));
        Assert.Equal(2, board.FindAll(".proj article.card").Count(c => c.TextContent.Contains("Health-check endpoint")));   // the same fixture twice
    }

    [Fact(DisplayName = "A project collapses from its chevron and hides from the menu of its name; the side menu lists a hidden project with an eye to bring it back")]
    public async Task CollapseAndHideProject()
    {
        await using BunitContext ctx = NewContext();
        TwoProjects(ctx);
        IRenderedComponent<BoardPage> board = ctx.Render<BoardPage>();
        board.WaitForAssertion(() => Assert.Equal(2, board.FindAll(".proj").Count));

        board.FindAll(".proj .proj-h .chev")[1].Click();
        board.WaitForAssertion(() => Assert.Empty(board.FindAll(".proj")[1].QuerySelectorAll("section.col")));
        board.FindAll(".proj .proj-h .chev")[1].Click();
        board.WaitForAssertion(() => Assert.NotEmpty(board.FindAll(".proj")[1].QuerySelectorAll("section.col")));

        board.FindAll(".proj .proj-h h2 button")[1].Click();
        board.FindAll(".menu-item").Single(i => i.TextContent.Trim() == "Hide project").Click();
        board.WaitForAssertion(() => Assert.Single(board.FindAll(".proj")));
        Assert.Empty(board.FindAll(".proj-h"));   // alone again: no header
        nav!.Find("button.nav-toggle").Click();
        nav.WaitForAssertion(() => Assert.Contains(NameOf(repo2), nav.Find(".nav-hidden").TextContent));
        nav.Find($"button[aria-label='Show project {NameOf(repo2)}']").Click();
        board.WaitForAssertion(() => Assert.Equal(2, board.FindAll(".proj").Count));
    }

    [Fact(DisplayName = "A card dropped into another project's column stays where it was, and a message says why")]
    public async Task NoDropAcrossProjects()
    {
        await using BunitContext ctx = NewContext();
        TwoProjects(ctx);
        IRenderedComponent<BoardPage> board = ctx.Render<BoardPage>();
        board.WaitForAssertion(() => Assert.Equal(2, board.FindAll(".proj").Count));

        board.FindAll(".proj")[0].QuerySelectorAll("article.card").First(c => c.TextContent.Contains("Health-check endpoint")).DragStart();
        board.FindAll(".proj")[1].QuerySelectorAll("section.col").Single(c => c.GetAttribute("data-key") == "Planned").Drop();

        overlays!.WaitForAssertion(() => Assert.Contains("stays in its repository", overlays.Find(".toast").TextContent));
        Workspace ws = ctx.Services.GetRequiredService<Workspace>();
        Assert.Equal("In Progress", ws.In(repo.Root, () => WorkItemsApi.Get(123)!.Status));
        Assert.Equal("In Progress", ws.In(repo2.Root, () => WorkItemsApi.Get(123)!.Status));
    }

    [Fact(DisplayName = "Opening a card of another project makes that project the active repository")]
    public async Task OpenCardSwitchesProject()
    {
        await using BunitContext ctx = NewContext();
        TwoProjects(ctx);
        IRenderedComponent<BoardPage> board = ctx.Render<BoardPage>();
        board.WaitForAssertion(() => Assert.Equal(2, board.FindAll(".proj").Count));
        AngleSharp.Dom.IElement card = board.FindAll(".proj")[1].QuerySelector("article.card")!;
        string id = card.GetAttribute("data-id")!;

        card.Click();

        Assert.EndsWith($"/item/{id}", ctx.Services.GetRequiredService<NavigationManager>().Uri);
        Assert.Equal(repo2.Root, ctx.Services.GetRequiredService<Workspace>().Root);
    }

    [Fact(DisplayName = "In the list, a group's chevron collapses it and expands it again, and it stays collapsed after the data reloads")]
    public async Task CollapseListGroup()
    {
        await using BunitContext ctx = NewContext();
        IRenderedComponent<BoardPage> board = ctx.Render<BoardPage>();
        ShowList();
        board.WaitForAssertion(() => Assert.NotEmpty(GroupRows(board, "In Progress")));

        Group(board, "In Progress").QuerySelector("button[aria-label='Expand group']")!.Click();
        board.WaitForAssertion(() => Assert.Empty(GroupRows(board, "In Progress")));
        Assert.NotEmpty(GroupRows(board, "Backlog"));   // only that group

        WorkItemsApi.SetPriority(123, "low");   // a write reloads the rows
        board.WaitForAssertion(() => Assert.Empty(GroupRows(board, "In Progress")));

        Group(board, "In Progress").QuerySelector("button[aria-label='Expand group']")!.Click();
        board.WaitForAssertion(() => Assert.NotEmpty(GroupRows(board, "In Progress")));
    }

    [Fact(DisplayName = "In the list, a project's chevron collapses all of its rows, like on the board, and a group collapsed inside a project stays so, in that project only")]
    public async Task CollapseListProject()
    {
        await using BunitContext ctx = NewContext();
        TwoProjects(ctx);
        IRenderedComponent<BoardPage> board = ctx.Render<BoardPage>();
        ShowList();
        board.WaitForAssertion(() => Assert.Equal(2, board.FindAll("tr[data-id='123']").Count));

        board.FindAll("tr[data-group^='proj:']")[0].QuerySelector("button[aria-label='Expand group']")!.Click();

        board.WaitForAssertion(() => Assert.Single(board.FindAll("tr[data-id='123']")));
        Assert.True(ctx.Services.GetRequiredService<SettingsStore>().ProjectCollapsed(repo.Root));   // the same setting as the board's project chevron

        // a group collapsed inside the other project stays collapsed when that project folds and unfolds, and only there
        AngleSharp.Dom.IElement Proj2() => board.FindAll("tr[data-group^='proj:']")[1];
        int Rows123() => board.FindAll("tr[data-id='123']").Count;
        board.FindAll("tr[data-group='In Progress']").Last().QuerySelector("button[aria-label='Expand group']")!.Click();
        board.WaitForAssertion(() => Assert.Equal(0, Rows123()));
        Proj2().QuerySelector("button[aria-label='Expand group']")!.Click();
        board.WaitForAssertion(() => Assert.True(ctx.Services.GetRequiredService<SettingsStore>().ProjectCollapsed(repo2.Root)));
        Proj2().QuerySelector("button[aria-label='Expand group']")!.Click();
        board.WaitForAssertion(() => Assert.False(ctx.Services.GetRequiredService<SettingsStore>().ProjectCollapsed(repo2.Root)));
        Assert.Equal(0, Rows123());
        board.FindAll("tr[data-group^='proj:']")[0].QuerySelector("button[aria-label='Expand group']")!.Click();
        board.WaitForAssertion(() => Assert.Equal(1, Rows123()));   // the first project's In Progress was never collapsed
    }

    [Fact(DisplayName = "With two repositories, the list groups rows by project first, then by column")]
    public async Task TwoProjectsInTheList()
    {
        await using BunitContext ctx = NewContext();
        TwoProjects(ctx);
        IRenderedComponent<BoardPage> board = ctx.Render<BoardPage>();
        ShowList();

        board.WaitForAssertion(() => Assert.Equal(2, board.FindAll("tr[data-group^='proj:']").Count));
        Assert.Contains(NameOf(repo), board.FindAll("tr[data-group^='proj:']")[0].TextContent);
        Assert.Contains(NameOf(repo2), board.FindAll("tr[data-group^='proj:']")[1].TextContent);
        Assert.Equal(2, board.FindAll("tr[data-id='123']").Count);
    }

    [Fact(DisplayName = "Merge asks which selected item is the main one, then merges the others into it")]
    public async Task MergeAsksForTheMainItem()
    {
        await using BunitContext ctx = NewContext();
        IRenderedComponent<BoardPage> board = ctx.Render<BoardPage>();
        for (int i = 0; i < 2; i++) board.FindAll("article.card")[i].Click(new MouseEventArgs { CtrlKey = true });   // re-found: each click re-renders

        board.Find(".actionbar button.primary").Click();

        overlays!.WaitForAssertion(() => Assert.Equal(2, overlays.FindAll(".dialog .choice").Count));
        List<int> ids = overlays.FindAll(".dialog .choice").Select(c => int.Parse(System.Text.RegularExpressions.Regex.Match(c.TextContent, @"#wi(\d+)").Groups[1].Value)).ToList();
        Assert.Equal($"#wi{ids[1]} {WorkItemsApi.Get(ids[1])!.Title}", overlays.FindAll(".dialog .choice")[1].TextContent.Trim());
        string mainTitle = WorkItemsApi.Get(ids[1])!.Title;
        overlays.FindAll(".dialog .choice")[1].Click();

        overlays.WaitForAssertion(() => Assert.Contains("Merged into #wi", overlays.Find(".toast").TextContent));
        WorkItem merged = WorkItemsApi.GetAll().Single(w => (w.MergedFrom ?? []).Contains(ids[0]));
        Assert.Equal(mainTitle, merged.Title);
        Assert.Null(WorkItemsApi.Get(ids[0]));
    }

    [Fact(DisplayName = "List cells keep their content inside the column: long text is cut with an ellipsis and shows in full as a tooltip")]
    public async Task ListCellsClip()
    {
        await using BunitContext ctx = NewContext();
        string longTitle = "A very long work item title that will not fit in any column of the list however wide " + new string('x', 120);
        string people = "Alexandria Montgomery-Featherstonehaugh, Bartholomew Wolfeschlegelsteinhausenbergerdorff";
        WorkItemsApi.SetTitle(123, longTitle);
        WorkItemsApi.SetContributors(123, people.Split(", "));
        IRenderedComponent<BoardPage> board = ctx.Render<BoardPage>();
        ShowList();

        AngleSharp.Dom.IElement row = board.FindAll("tr[data-id]").First(r => r.TextContent.Contains("#wi123"));
        Assert.Equal(longTitle, row.QuerySelector("td.title .row-title")!.GetAttribute("title"));
        Assert.Equal(people, row.QuerySelector("td .clip[title*='Montgomery']")!.GetAttribute("title"));
        // every cell that holds plain text wraps it in the clipping span, with the full text as its tooltip
        foreach (AngleSharp.Dom.IElement td in row.QuerySelectorAll("td.num, td.muted"))
        {
            AngleSharp.Dom.IElement clip = td.QuerySelector(".clip")!;
            Assert.Equal(clip.TextContent.Trim(), clip.GetAttribute("title"));
        }
    }

    [Fact(DisplayName = "The list has an 'In status since' column after Due, with the date the item last moved to its status")]
    public async Task InStatusSinceColumn()
    {
        await using BunitContext ctx = NewContext();
        DateOnly today = DateOnly.FromDateTime(DateTime.Today);
        Assert.True(WorkItemsApi.SetStatus(107, "In Review").Success);
        IRenderedComponent<BoardPage> board = ctx.Render<BoardPage>();

        ShowList();
        board.WaitForAssertion(() => Assert.Contains(board.FindAll("thead th"), h => h.TextContent.Contains("In status since")));
        List<string> heads = board.FindAll("thead th").Select(h => h.TextContent).ToList();
        Assert.Equal(heads.FindIndex(h => h.Contains("Due")) + 1, heads.FindIndex(h => h.Contains("In status since")));
        Assert.Contains(today.ToString("d MMM"), board.FindAll("tr[data-id='107']").First().TextContent);
        Assert.Contains(new DateOnly(2026, 9, 12).ToString("d MMM"), board.FindAll("tr[data-id='123']").First().TextContent);   // never moved: its creation date
    }

    // One of the side menu's Organise dropdowns (columns, rows or colour): open it, pick a value.
    private void PickField(string field, string value)
    {
        if (nav!.FindAll(".nav-view").Count == 0) nav.Find("button.nav-toggle").Click();
        AngleSharp.Dom.IElement Field() => nav.FindAll(".field").Single(f => f.QuerySelector("label")!.TextContent.Trim() == field);   // "label" is a choice in several fields: stay in this one
        Field().QuerySelector(".dd")!.Click();
        nav.WaitForAssertion(() => Assert.Contains(Field().QuerySelectorAll(".rz-dropdown-item"), i => i.TextContent.Trim() == value));
        Field().QuerySelectorAll(".rz-dropdown-item").First(i => i.TextContent.Trim() == value).Click();
    }

    private void PickSwimlanes(string value) => PickField("rows", value);

    [Fact(DisplayName = "With swimlanes by priority the board shows a row per priority across every column, and dropping a card into another row's column changes both its status and its priority")]
    public async Task SwimlanesByPriority()
    {
        await using BunitContext ctx = NewContext();
        Workspace workspace = ctx.Services.GetRequiredService<Workspace>();
        int urgent = workspace.CreateItem("Backlog").Id, other = workspace.CreateItem("Backlog").Id;
        Assert.True(WorkItemsApi.SetPriority(urgent, "high").Success);
        IRenderedComponent<BoardPage> board = ctx.Render<BoardPage>();

        PickSwimlanes("priority");

        board.WaitForAssertion(() => Assert.Equal(["High priority", "Lower priority"], board.FindAll(".swim-h span:first-child").Select(s => s.TextContent.Trim())));
        Assert.Equal(7, board.FindAll("section.col h3").Count);   // the column headers once, on top
        Assert.All(board.FindAll(".swim"), row => Assert.Equal(7, row.QuerySelectorAll("section.col[data-drop]").Length));   // a drop cell per column in every row, empty ones too
        Assert.All(board.FindAll(".swim .lane"), l => Assert.True(l.HasAttribute("data-drop")));
        Assert.Empty(board.FindAll(".lane-h"));
        AngleSharp.Dom.IElement Cell(string row, string column) => board.Find($".swim[data-swim={row}] section.col[data-cell='{column}']");
        Assert.Single(Cell("high", "Backlog").QuerySelectorAll("article.card"));
        Assert.Equal(2, Cell("low", "Backlog").QuerySelectorAll("article.card").Length);   // the fixture's own Backlog item and the new one

        Cell("low", "Backlog").QuerySelectorAll("article.card").First(c => c.TextContent.Contains($"#wi{other}")).DragStart();
        Cell("high", "In Progress").QuerySelector(".lane")!.Drop();

        board.WaitForAssertion(() => Assert.Equal("In Progress", WorkItemsApi.Get(other)!.Status));
        board.WaitForAssertion(() => Assert.Equal("high", WorkItemsApi.Get(other)!.Priority));
        board.WaitForAssertion(() => Assert.Contains($"#wi{other}", Cell("high", "In Progress").TextContent));
        Assert.DoesNotContain($"#wi{other}", Cell("low", "Backlog").TextContent);
    }

    [Fact(DisplayName = "The column menu no longer offers subgroups, and a column shows its cards in one lane whatever their priorities")]
    public async Task NoSubgroups()
    {
        await using BunitContext ctx = NewContext();
        Workspace workspace = ctx.Services.GetRequiredService<Workspace>();
        int urgent = workspace.CreateItem("Backlog").Id;
        Assert.True(WorkItemsApi.SetPriority(urgent, "high").Success);
        IRenderedComponent<BoardPage> board = ctx.Render<BoardPage>();

        board.FindAll("header.col-h").First(h => h.TextContent.Contains("Backlog")).Click();
        board.WaitForAssertion(() => Assert.Contains(board.FindAll(".menu-item"), i => i.TextContent.Contains("Hide column")));
        Assert.DoesNotContain(board.FindAll(".menu-item"), i => i.TextContent.Contains("subgroup", StringComparison.OrdinalIgnoreCase));

        Assert.Single(Col(board, "Backlog").QuerySelectorAll(".lane"));
        Assert.Equal(2, Col(board, "Backlog").QuerySelectorAll(".lane > article.card").Length);   // the high and the fixture's low-priority item, side by side
        Assert.Empty(board.FindAll(".lane-h, .swim-h, .swim"));
    }

    // The list's rows in order: group rows by their data-group, item rows as "#<id>" (the hidden placeholder group of an empty column is left out).
    private static List<string> ListOrder(IRenderedComponent<BoardPage> board) =>
        [.. board.FindAll(".rz-data-grid tbody tr").Select(tr => tr.GetAttribute("data-group") ?? (tr.GetAttribute("data-id") is string id ? "#" + id : "")).Where(k => k.Length > 0)];

    // The row-value group (swim:<value>) of a column's group: the first one after the column's own group row.
    private static AngleSharp.Dom.IElement SwimIn(IRenderedComponent<BoardPage> board, string column, string swim)
    {
        List<AngleSharp.Dom.IElement> trs = [.. board.FindAll(".rz-data-grid tbody tr")];
        int at = trs.FindIndex(tr => tr.GetAttribute("data-group") == column);
        return trs.Skip(at + 1).TakeWhile(tr => tr.GetAttribute("data-group") is null or "" || tr.GetAttribute("data-group")!.StartsWith("swim:")).Single(tr => tr.GetAttribute("data-group") == "swim:" + swim);
    }

    [Fact(DisplayName = "With rows by label the list groups by column first, then by label inside each column")]
    public async Task RowsByLabelInTheList()
    {
        await using BunitContext ctx = NewContext();
        Assert.True(WorkItemsApi.SetLabel(123, "bug").Success);
        IRenderedComponent<BoardPage> board = ctx.Render<BoardPage>();
        PickSwimlanes("label");
        ShowList();

        string status = WorkItemsApi.Get(123)!.Status;
        board.WaitForAssertion(() => Assert.Contains("swim:bug", ListOrder(board)));
        List<string> order = ListOrder(board);
        Assert.Equal(1, order.Count(g => g == "swim:bug"));   // only one column holds a bug
        Assert.Equal(["Unlabeled", "bug"], board.FindAll("tr[data-group^='swim:'] .swim-name").Select(n => n.TextContent.Trim()).Distinct());   // row values with no items are left out
        Assert.DoesNotContain("swim:feature", order);
        Assert.DoesNotContain("Label", board.FindAll("thead th .th-l").Select(h => h.TextContent.Trim()));   // the grouping says it

        // the column group comes first, its label groups follow, then the rows of each label: Planned (unlabeled) before In Progress (bug)
        int planned = order.IndexOf("Planned"), progress = order.IndexOf(status), bug = order.IndexOf("swim:bug");
        Assert.True(planned >= 0 && progress > planned);
        Assert.Equal(["Planned", "swim:~", "#107"], order.Skip(planned).Take(3));
        Assert.Equal([status, "swim:bug", "#123"], order.Skip(progress).Take(3));
        Assert.Equal(progress + 1, bug);
        Assert.Contains("1", board.Find("tr[data-group='swim:bug']").TextContent);   // its count
    }

    [Fact(DisplayName = "In the list with rows set, a column's header counts all its items, and dropping on a row-value group inside a column moves the item to that column and that row value")]
    public async Task ListColumnCountsAndRowGroupDrop()
    {
        await using BunitContext ctx = NewContext();
        Workspace workspace = ctx.Services.GetRequiredService<Workspace>();
        int bugInPlanned = workspace.CreateItem("Planned").Id, featureInPlanned = workspace.CreateItem("Planned").Id;
        Assert.True(WorkItemsApi.SetLabel(bugInPlanned, "bug").Success);
        Assert.True(WorkItemsApi.SetLabel(featureInPlanned, "feature").Success);
        Assert.True(WorkItemsApi.SetLabel(123, "bug").Success);   // In Progress
        IRenderedComponent<BoardPage> board = ctx.Render<BoardPage>();
        PickSwimlanes("label");
        ShowList();

        // Planned holds the fixture's unlabeled item and the two new ones: three in the header, one in each row-value group
        board.WaitForAssertion(() => Assert.Equal("3", Group(board, "Planned").QuerySelector(".count")!.TextContent.Trim()));
        Assert.All(new[] { "~", "bug", "feature" }, v => Assert.Equal("1", SwimIn(board, "Planned", v).QuerySelector(".count")!.TextContent.Trim()));

        // the bug from In Progress, dropped on Planned's feature group: it takes both the column and the row value
        board.Find("tr[data-id='123']").DragStart();
        await board.InvokeAsync(() => { });   // let the dispatcher drain, so the drop targets the current render tree
        SwimIn(board, "Planned", "feature").Drop();

        board.WaitForAssertion(() => Assert.Equal("Planned", WorkItemsApi.Get(123)!.Status), TimeSpan.FromSeconds(5));
        board.WaitForAssertion(() => Assert.Equal("feature", WorkItemsApi.Get(123)!.Label));
        board.WaitForAssertion(() => Assert.Equal("4", Group(board, "Planned").QuerySelector(".count")!.TextContent.Trim()));
        Assert.Equal("2", SwimIn(board, "Planned", "feature").QuerySelector(".count")!.TextContent.Trim());
    }

    [Fact(DisplayName = "In the list with rows set, collapsing a row-value group inside one column leaves the same row value open in another column")]
    public async Task ListRowGroupCollapsesPerColumn()
    {
        await using BunitContext ctx = NewContext();
        Workspace workspace = ctx.Services.GetRequiredService<Workspace>();
        int bugInPlanned = workspace.CreateItem("Planned").Id;
        Assert.True(WorkItemsApi.SetLabel(bugInPlanned, "bug").Success);
        Assert.True(WorkItemsApi.SetLabel(123, "bug").Success);   // In Progress
        IRenderedComponent<BoardPage> board = ctx.Render<BoardPage>();
        PickSwimlanes("label");
        ShowList();
        board.WaitForAssertion(() => Assert.Equal(2, board.FindAll("tr[data-group='swim:bug']").Count));

        SwimIn(board, "Planned", "bug").QuerySelector("button[aria-label='Expand group']")!.Click();

        board.WaitForAssertion(() => Assert.Empty(board.FindAll($"tr[data-id='{bugInPlanned}']")));
        Assert.Single(board.FindAll("tr[data-id='123']"));   // the same label in In Progress stays open
        Assert.Equal(2, board.FindAll("tr[data-group='swim:bug']").Count);

        WorkItemsApi.SetPriority(123, "low");   // a write reloads the rows: the group stays shut
        board.WaitForAssertion(() => Assert.Empty(board.FindAll($"tr[data-id='{bugInPlanned}']")));
        Assert.Single(board.FindAll("tr[data-id='123']"));

        SwimIn(board, "Planned", "bug").QuerySelector("button[aria-label='Expand group']")!.Click();
        board.WaitForAssertion(() => Assert.Single(board.FindAll($"tr[data-id='{bugInPlanned}']")));
    }

    [Fact(DisplayName = "The calendar view is a roadmap: items listed by group on the left, bars from start to due on the right, a group bar spanning the group's dates, a today line, and Today / previous / next / zoom controls")]
    public async Task CalendarIsARoadmap()
    {
        await using BunitContext ctx = NewContext();
        DateOnly today = DateOnly.FromDateTime(DateTime.Today);
        WorkItemsApi.SetStarted(123, today);
        WorkItemsApi.SetDue(123, today.AddDays(1));
        IRenderedComponent<BoardPage> board = ctx.Render<BoardPage>();

        nav!.Find("button[aria-label=Layout]").Click();
        nav.FindAll(".menu-item").Single(i => i.TextContent.Trim() == "Calendar").Click();

        board.WaitForAssertion(() => Assert.NotEmpty(board.FindAll(".rm")));
        string Range() => board.Find(".rm-range").TextContent;
        Assert.Contains(today.ToString("MMM yyyy"), Range());
        Assert.NotEmpty(board.FindAll(".rm-group"));   // the board's groups (statuses) head the left pane
        AngleSharp.Dom.IElement row = board.Find(".rm-row[data-id='123']");
        Assert.Contains("Health-check endpoint", row.TextContent);
        Assert.Contains("#wi123", row.TextContent);
        Assert.NotEmpty(board.FindAll(".rm-row[data-id='107']"));   // no dates: a row ...
        Assert.Empty(board.FindAll(".rm-bar[data-id='107']"));      // ... and no bar

        // the bar is two day-columns wide, starting at today's column of the window
        RoadmapWindow month = RoadmapModel.Window(today, "month");
        AngleSharp.Dom.IElement bar = board.Find(".rm-bar[data-id='123']");
        Assert.Contains("Health-check endpoint", bar.TextContent);
        Assert.Contains($"--col:{month.ColumnOf(today)}; --span:2;", bar.GetAttribute("style"));
        Assert.Single(board.FindAll(".rm-today"));
        Assert.Contains(today.ToString("ddd, MMM d") + " - " + today.AddDays(1).ToString("ddd, MMM d"), board.FindAll(".rm-gbar").Select(g => g.TextContent).First(t => t.Contains(today.ToString("ddd, MMM d"))));   // the group's span as text

        board.Find(".rm-next").Click();
        board.WaitForAssertion(() => Assert.Contains(today.AddMonths(1).ToString("MMM yyyy"), Range()));
        board.Find(".rm-prev").Click();
        board.WaitForAssertion(() => Assert.Contains(today.ToString("MMM yyyy"), Range()));
        board.Find(".rm-next").Click();
        board.Find(".rm-today-btn").Click();
        board.WaitForAssertion(() => Assert.Equal(today.ToString("MMM yyyy"), Range().Split(" – ")[0]));

        board.Find(".rm-zoom [data-zoom=quarter]").Click();
        RoadmapWindow quarter = RoadmapModel.Window(today, "quarter");
        board.WaitForAssertion(() => Assert.Contains($"--col:{quarter.ColumnOf(today)}; --span:1;", board.Find(".rm-bar[data-id='123']").GetAttribute("style")));   // week columns now

        AngleSharp.Dom.IElement chevron = board.FindAll(".rm-lgroup").First(g => g.QuerySelector(".rm-row[data-id='123']") is not null).QuerySelector(".rm-chev")!;
        chevron.Click();   // collapsing a group hides its rows and bars
        board.WaitForAssertion(() => Assert.Empty(board.FindAll(".rm-row[data-id='123']")));
        Assert.Empty(board.FindAll(".rm-bar[data-id='123']"));
        board.Find(".rm-chev[aria-expanded=false]").Click();

        board.WaitForAssertion(() => Assert.NotEmpty(board.FindAll(".rm-bar[data-id='123']")));
        board.Find(".rm-bar[data-id='123']").Click();
        Assert.EndsWith("/item/123", ctx.Services.GetRequiredService<NavigationManager>().Uri);
    }

    [Fact(DisplayName = "With swimlanes by priority the roadmap groups its items by priority instead of the board's columns")]
    public async Task RoadmapGroupsBySwimlane()
    {
        await using BunitContext ctx = NewContext();
        Assert.True(WorkItemsApi.SetPriority(123, "high").Success);
        IRenderedComponent<BoardPage> board = ctx.Render<BoardPage>();

        PickSwimlanes("priority");
        nav!.Find("button[aria-label=Layout]").Click();
        nav.FindAll(".menu-item").Single(i => i.TextContent.Trim() == "Calendar").Click();

        board.WaitForAssertion(() => Assert.NotEmpty(board.FindAll(".rm-row[data-id='123']")));
        List<string> groups = board.FindAll(".rm-gname").Select(g => g.TextContent).ToList();
        Assert.Contains(groups, g => g.Contains("High priority"));
        Assert.Contains(groups, g => g.Contains("Lower priority"));
        Assert.DoesNotContain(groups, g => g.Contains("Backlog"));   // the swimlane field replaces the board's columns as the roadmap's groups
        Assert.Contains("#wi123", board.FindAll(".rm-lgroup").First(g => g.QuerySelector(".rm-gname")!.TextContent.Contains("High priority")).TextContent);
    }

    [Fact(DisplayName = "The side menu organises work by columns, rows and colour; with columns by status, rows by feature and colour by label the board shows a row per feature and cards banded by label")]
    public async Task OrganiseByColumnsRowsAndColour()
    {
        await using BunitContext ctx = NewContext();
        Assert.True(WorkItemsApi.SetLabel(123, "bug").Success);
        IRenderedComponent<BoardPage> board = ctx.Render<BoardPage>();

        nav!.Find("button.nav-toggle").Click();
        Assert.Equal(["sort by", "columns", "rows", "colour"], nav.FindAll(".nav-view .organise .field label").Select(l => l.TextContent.Trim()));
        Assert.Contains("Organise", nav.Find(".nav-view .organise .nav-h").TextContent);
        Assert.DoesNotContain("swimlanes", nav.Find(".nav-view").TextContent);
        Assert.DoesNotContain("group by", nav.Find(".nav-view").TextContent);
        Assert.Equal(["priority", "status", "none", "status"], nav.FindAll(".organise .field .rz-dropdown-label").Select(l => l.TextContent.Trim()));   // today's board: columns by status, no rows, coloured by status

        PickField("rows", "feature");
        PickField("colour", "label");

        board.WaitForAssertion(() => Assert.Contains("Availability", board.FindAll(".swim-h span:first-child").Select(s => s.TextContent.Trim())));
        Assert.Contains("Banking", board.FindAll(".swim-h span:first-child").Select(s => s.TextContent.Trim()));
        Assert.Equal(board.FindAll(".swim-h").Count, board.FindAll(".swim").Count);
        Assert.Equal(7, board.FindAll("section.col h3").Count);   // the status columns, once, on top
        AngleSharp.Dom.IElement Band(int id) => board.FindAll("article.card").First(c => c.TextContent.Contains($"#wi{id}")).QuerySelector(".band")!;
        Assert.Contains("var(--peach)", Band(123).GetAttribute("style"));   // bug
        Assert.Contains("var(--stone)", Band(107).GetAttribute("style"));   // no label
        Assert.Contains("bug", nav.Find(".legend").TextContent);            // the legend explains the colours shown
        Assert.Contains("var(--peach)", nav.FindAll(".legend .lg").First(l => l.TextContent.Contains("bug")).QuerySelector("i")!.GetAttribute("style"));
        Assert.Equal("label", ctx.Services.GetRequiredService<SettingsStore>().Board(repo.Root).ColorBy);   // remembered
    }

    [Fact(DisplayName = "The side menu's changed picker sets the period, the legend names it and counts what is shown, and Show all in the picker clears it; the choice is remembered per board")]
    public async Task ChangedPickerFiltersToWhatChanged()
    {
        await using BunitContext ctx = NewContext();
        SettingsStore settings = ctx.Services.GetRequiredService<SettingsStore>();
        DateOnly first = new(DateTime.Today.Year, DateTime.Today.Month, 1), last = first.AddDays(6);
        DateOnly today = DateOnly.FromDateTime(DateTime.Today);
        WorkItemsApi.SetStarted(123, today);   // dated, so the calendar has bars
        WorkItemsApi.SetStarted(107, today);
        // every file was last written long ago, and only 123 has something logged in the period: it is the one item that changed in it
        foreach (string file in Directory.EnumerateFiles(repo.OpenSpec, "*", SearchOption.AllDirectories)) File.SetLastWriteTime(file, new DateTime(2026, 1, 1));
        string log123 = Path.Combine(repo.OpenSpec, "changes", "add-health-check", "events.log");
        File.WriteAllText(log123, $"{first.AddDays(3):yyyy-MM-dd}T10:00:00 | Tester | custom | something happened\n");
        File.SetLastWriteTime(log123, new DateTime(2026, 1, 1));
        File.Delete(Path.Combine(repo.OpenSpec, "changes", "export-remittances", "events.log"));
        IRenderedComponent<BoardPage> board = ctx.Render<BoardPage>();
        bool Card(int id) => board.FindAll("article.card").Any(c => c.TextContent.Contains($"#wi{id}"));
        int shown = board.FindAll("article.card").Count;
        Assert.True(Card(107) && Card(123) && shown > 1);   // before any period

        nav!.Find("button.nav-toggle").Click();
        Assert.Contains("changed", nav.FindAll(".nav-view .field label").Select(l => l.TextContent.Trim()));
        Assert.Contains("any time", nav.Find("button[aria-label='changed']").TextContent);
        Assert.Empty(nav.FindAll("button[aria-label='Clear changed period']"));   // no × beside the field: Show all is in the picker
        Assert.Empty(nav.FindAll(".legend .lg.head"));

        // the day picker of the item page: first day, then last day; with nothing chosen yet it offers no Show all
        nav.Find("button[aria-label='changed']").Click();
        nav.WaitForAssertion(() => Assert.NotEmpty(nav.FindAll(".range-pop")));
        Assert.Empty(nav.FindAll(".range-pop button.range-clear"));
        foreach (DateOnly day in new[] { first, last })
        {
            await nav.InvokeAsync(() => { });
            nav.WaitForAssertion(() => nav.Find($".range-pop button.rd[data-day='{day:yyyy-MM-dd}']").Click());
        }
        nav.WaitForAssertion(() => Assert.Equal((first, last), (settings.Board(repo.Root).ChangedFrom, settings.Board(repo.Root).ChangedTo)));   // remembered per board
        nav.WaitForAssertion(() => Assert.Contains($"{first:d MMM} – {last:d MMM}", nav.Find("button[aria-label='changed']").TextContent));
        Assert.Equal($"changed {first:d MMM} – {last:d MMM}", nav.Find(".legend .lg.head").TextContent.Trim());   // the legend's first line names it
        nav.WaitForAssertion(() => Assert.Equal($"1 of {shown} shown", nav.Find(".legend .lg.count").TextContent.Trim()));   // the second line counts what is shown of what the other filters leave
        Assert.Equal(["In Progress"], nav.FindAll(".legend .lg:not(.head)").Select(l => l.TextContent.Trim()));   // and keeps only the colours of the shown items

        // the board: 123 alone is left, with its colour
        board.WaitForAssertion(() => Assert.Single(board.FindAll("article.card")));
        Assert.True(Card(123) && !Card(107));
        Assert.Contains("var(--sun)", board.Find("article.card .band").GetAttribute("style"));
        // the list: one row
        settings.SaveBoard(repo.Root, settings.Board(repo.Root) with { View = "list" });
        board.WaitForAssertion(() => Assert.NotEmpty(board.FindAll("tr[data-id='123']")));
        Assert.Empty(board.FindAll("tr[data-id='107']"));
        // the calendar: the bar of 123, none for 107
        settings.SaveBoard(repo.Root, settings.Board(repo.Root) with { View = "calendar" });
        board.WaitForAssertion(() => Assert.NotEmpty(board.FindAll(".rm-bar[data-id='123']")));
        Assert.Empty(board.FindAll(".rm-bar[data-id='107'], .rm-row[data-id='107']"));

        // Show all in the picker puts everything back
        nav.Find("button[aria-label='changed']").Click();
        nav.WaitForAssertion(() => Assert.Equal(["Show all"], nav.FindAll(".range-pop button.range-clear").Select(b => b.TextContent.Trim())));
        await nav.InvokeAsync(() => { });
        nav.Find(".range-pop button.range-clear").Click();
        nav.WaitForAssertion(() => Assert.Equal((null, null), (settings.Board(repo.Root).ChangedFrom, settings.Board(repo.Root).ChangedTo)));
        nav.WaitForAssertion(() => Assert.Contains("any time", nav.Find("button[aria-label='changed']").TextContent));
        nav.WaitForAssertion(() => Assert.Empty(nav.FindAll(".range-pop")));   // and the popup closes
        Assert.Empty(nav.FindAll(".legend .lg.head"));
        board.WaitForAssertion(() => Assert.NotEmpty(board.FindAll(".rm-bar[data-id='107']")));
        settings.SaveBoard(repo.Root, settings.Board(repo.Root) with { View = "board" });
        board.WaitForAssertion(() => Assert.Equal(shown, board.FindAll("article.card").Count));
    }

    [Fact(DisplayName = "Dropping a card into a cell sets both the column's field and the row's field")]
    public async Task DropSetsColumnAndRowFields()
    {
        await using BunitContext ctx = NewContext();
        SettingsStore settings = ctx.Services.GetRequiredService<SettingsStore>();
        (bool _, string _, int pay) = WorkItemsApi.Create("Pay run", features: ["functional/banking"]);
        IRenderedComponent<BoardPage> board = ctx.Render<BoardPage>();
        AngleSharp.Dom.IElement Cell(string row, string column) => board.Find($".swim[data-swim='{row}'] section.col[data-cell='{column}']");
        // Found and fired on the renderer's thread: a render from elsewhere (the board reloading after the settings or the drop) cannot come between
        // finding a cell and firing its event and leave the event without a handler.
        Task Drag((string Row, string Column) from, int id, (string Row, string Column) to) => board.InvokeAsync(() =>
        {
            Cell(from.Row, from.Column).QuerySelectorAll("article.card").First(c => c.TextContent.Contains($"#wi{id}")).DragStart();
            Cell(to.Row, to.Column).QuerySelector(".lane")!.Drop();
        });

        // columns by status, rows by feature (a field with several values: the card leaves its old feature for the new one)
        PickField("rows", "feature");
        board.WaitForAssertion(() => Assert.Contains($"#wi{pay}", Cell("functional/banking", "Backlog").TextContent));
        await Drag(("functional/banking", "Backlog"), pay, ("non-functional/availability", "In Progress"));
        board.WaitForAssertion(() => Assert.Equal("In Progress", WorkItemsApi.Get(pay)!.Status));
        board.WaitForAssertion(() => Assert.Equal(["non-functional/availability"], WorkItemsApi.Get(pay)!.Features.Select(f => f.Path)));
        board.WaitForAssertion(() => Assert.Contains($"#wi{pay}", Cell("non-functional/availability", "In Progress").TextContent));

        // columns by priority, rows by label: two single-valued fields
        settings.SaveBoard(repo.Root, settings.Board(repo.Root) with { GroupBy = "priority", Swimlane = "label" });
        board.WaitForAssertion(() => Assert.Contains($"#wi{pay}", Cell(BoardModel.NoRow, "low").TextContent));
        await Drag((BoardModel.NoRow, "low"), pay, ("bug", "high"));
        board.WaitForAssertion(() => Assert.Equal("high", WorkItemsApi.Get(pay)!.Priority));
        board.WaitForAssertion(() => Assert.Equal("bug", WorkItemsApi.Get(pay)!.Label));
        Assert.Equal("In Progress", WorkItemsApi.Get(pay)!.Status);   // the status was not a dimension this time

        // a field a drop cannot set (due dates) stays read-only: the other dimension still applies
        settings.SaveBoard(repo.Root, settings.Board(repo.Root) with { GroupBy = "status", Swimlane = "due" });
        board.WaitForAssertion(() => Assert.Contains($"#wi{pay}", Cell("none", "In Progress").TextContent));
        await Drag(("none", "In Progress"), pay, ("overdue", "Blocked"));
        board.WaitForAssertion(() => Assert.Equal("Blocked", WorkItemsApi.Get(pay)!.Status));
        Assert.Null(WorkItemsApi.Get(pay)!.Due);
    }

    [Fact(DisplayName = "The list groups by columns then rows, the roadmap by rows then columns, and both colour their rows and bars by the colour field")]
    public async Task ListAndRoadmapFollowRowsColumnsAndColour()
    {
        await using BunitContext ctx = NewContext();
        SettingsStore settings = ctx.Services.GetRequiredService<SettingsStore>();
        DateOnly today = DateOnly.FromDateTime(DateTime.Today);
        Assert.True(WorkItemsApi.SetLabel(123, "bug").Success);
        WorkItemsApi.SetStarted(123, today);
        WorkItemsApi.SetDue(123, today.AddDays(1));
        IRenderedComponent<BoardPage> board = ctx.Render<BoardPage>();
        settings.SaveBoard(repo.Root, settings.Board(repo.Root) with { Swimlane = "label", ListSwimlane = "label", ListGroupBy = "status", ColorBy = "label", View = "list" });

        // the list: a group per column, the row-value groups inside it, a colour stripe per row
        board.WaitForAssertion(() => Assert.Contains("swim:bug", board.FindAll("tr[data-group]").Select(g => g.GetAttribute("data-group"))));
        List<string> order = board.FindAll(".rz-data-grid tbody tr").Select(tr => tr.GetAttribute("data-group") ?? "#" + tr.GetAttribute("data-id")).ToList();
        string status = WorkItemsApi.Get(123)!.Status;
        Assert.True(order.IndexOf(status) < order.IndexOf("swim:bug") && order.IndexOf("swim:bug") < order.IndexOf("#123"), string.Join(",", order));
        Assert.Contains("var(--peach)", board.Find("tr[data-id='123']").GetAttribute("style"));   // bug
        Assert.Contains("var(--stone)", board.Find("tr[data-id='107']").GetAttribute("style"));   // no label

        // the roadmap: a row header with the columns as sub-headers, bars coloured by label
        settings.SaveBoard(repo.Root, settings.Board(repo.Root) with { View = "calendar" });
        board.WaitForAssertion(() => Assert.NotEmpty(board.FindAll(".rm-bar[data-id='123']")));
        Assert.Contains("bug", board.FindAll(".rm-group .rm-gname").Select(g => g.TextContent));
        AngleSharp.Dom.IElement bug = board.FindAll(".rm-lgroup").First(g => g.QuerySelector(".rm-gname")!.TextContent == "bug");
        Assert.Contains(bug.QuerySelectorAll(".rm-subgroup .rm-sname"), s => s.TextContent == status);
        Assert.NotNull(bug.QuerySelector(".rm-subgroup ~ .rm-row.deep[data-id='123']"));   // under its column's sub-header, a level in
        Assert.Contains("var(--peach)", board.Find(".rm-bar[data-id='123']").GetAttribute("style"));
        Assert.Contains("var(--peach)", board.Find(".rm-row[data-id='123']").GetAttribute("style"));   // the left dot

        // no rows: just the columns as groups, no sub-headers
        settings.SaveBoard(repo.Root, settings.Board(repo.Root) with { Swimlane = "none" });
        board.WaitForAssertion(() => Assert.Empty(board.FindAll(".rm-subgroup")));
        Assert.Contains(status, board.FindAll(".rm-group .rm-gname").Select(g => g.TextContent));
    }

    [Fact(DisplayName = "The side menu separates the organise block (sort first), the filters, and the display switches with dashed lines")]
    public async Task SideMenuSeparators()
    {
        await using BunitContext ctx = NewContext();
        ctx.Render<BoardPage>();
        nav!.Find("button.nav-toggle").Click();

        List<AngleSharp.Dom.IElement> parts = nav.Find(".nav-view").Children.ToList();
        List<AngleSharp.Dom.IElement> seps = parts.Where(p => p.ClassList.Contains("nav-sep")).ToList();
        Assert.Equal(2, seps.Count);
        Assert.All(seps, s => Assert.Equal("separator", s.GetAttribute("role")));
        Assert.True(parts[parts.IndexOf(seps[0]) - 1].ClassList.Contains("organise"));          // after the organise block (sort by, columns, rows, colour, legend)
        Assert.Contains("contributor", parts[parts.IndexOf(seps[0]) + 1].TextContent);          // before the filters
        Assert.True(parts[parts.IndexOf(seps[1]) - 1].ClassList.Contains("seg"));               // after the Work items / Subtasks switch
        Assert.Contains("compact cards", parts[parts.IndexOf(seps[1]) + 1].TextContent);        // before the pills

        string css = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "wwwroot", "css", "app.css"));
        Assert.Matches(@"\.nav-sep\s*\{[^}]*2px dashed var\(--line-soft\)", css);               // the same dashed line as the menu's other separators
        Assert.Matches(@"\.nav-hidden\s*\{[^}]*2px dashed var\(--line-soft\)", css);
    }

    [Fact(DisplayName = "Shift-clicking a swimlane header selects it, the arrow keys move it past its neighbour, and its × hides it until the side menu brings it back")]
    public async Task SwimlaneHeadersSelectMoveAndHide()
    {
        await using BunitContext ctx = NewContext();
        SettingsStore settings = ctx.Services.GetRequiredService<SettingsStore>();
        IRenderedComponent<BoardPage> board = ctx.Render<BoardPage>();
        PickSwimlanes("label");
        List<string> Rows() => board.FindAll(".swim-h").Select(h => h.GetAttribute("data-swim")!).ToList();
        AngleSharp.Dom.IElement Head(string row) => board.Find($".swim-h[data-swim='{row}']");
        board.WaitForAssertion(() => Assert.Equal(["~", "bug", "feature", "enhancement"], Rows()));

        // Shift-click selects (tinted); the up arrow moves the row past its neighbour and the order is remembered per row field
        Head("feature").Click(new MouseEventArgs { ShiftKey = true });
        board.WaitForAssertion(() => Assert.Contains("selected", Head("feature").ClassName));
        await board.InvokeAsync(() => { });
        board.Find(".board-wrap").KeyDown("ArrowUp");
        board.WaitForAssertion(() => Assert.Equal(["~", "feature", "bug", "enhancement"], Rows()));
        Assert.Contains("selected", Head("feature").ClassName);   // still selected, so the next press moves it again
        Assert.Equal(["row:label:~", "row:label:feature", "row:label:bug", "row:label:enhancement"], settings.Board(repo.Root).Order!.Where(k => k.StartsWith("row:label:")));
        board.Find(".board-wrap").KeyDown("ArrowDown");
        board.WaitForAssertion(() => Assert.Equal(["~", "bug", "feature", "enhancement"], Rows()));
        Assert.Contains(".swim-h.selected", File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "wwwroot", "js", "app.js")));   // arrows reach the page while a row is selected

        // a plain click lets go of the selection
        Head("bug").Click();
        board.WaitForAssertion(() => Assert.Empty(board.FindAll(".swim-h.selected")));

        // dragging a header onto another moves it in front of that one
        Head("enhancement").DragStart();
        Head("bug").Drop();
        board.WaitForAssertion(() => Assert.Equal(["~", "enhancement", "bug", "feature"], Rows()));

        // the × hides the row (its cells too); the side menu lists it with an eye that brings it back in its place
        Head("bug").QuerySelector("button.swim-hide")!.Click();
        board.WaitForAssertion(() => Assert.Equal(["~", "enhancement", "feature"], Rows()));
        Assert.Equal(Rows().Count, board.FindAll(".swim").Count);
        Assert.Contains("row:label:bug", settings.Board(repo.Root).Hidden!);
        nav!.WaitForAssertion(() => Assert.Equal(["bug"], nav.FindAll(".nav-hidden .grp-row .nm").Select(n => n.TextContent.Trim())));
        nav.Find(".nav-hidden button[aria-label='Show bug']").Click();
        board.WaitForAssertion(() => Assert.Equal(["~", "enhancement", "bug", "feature"], Rows()));
        Assert.Empty(nav.FindAll(".nav-hidden .grp-row"));
    }

    private static string[] UiPeople(BunitContext ctx) => [.. ctx.Services.GetRequiredService<UiService>().WorklogPeople];

    private IRenderedComponent<Worklog> OpenWorklog(BunitContext ctx) =>
        ctx.Render<Worklog>(p => p.Add(c => c.Item, WorkItemsApi.Get(123)!).Add(c => c.Root, repo.Root));

    [Fact(DisplayName = "While a worklog is open the side menu shows a Worklog block with its filter pills and Add entry button first, and the block goes when the worklog closes")]
    public async Task SideMenuShowsTheWorklog()
    {
        await using BunitContext ctx = NewContext();
        ctx.Services.GetRequiredService<SettingsStore>().Update(s => s with { Split = false });
        ctx.Services.GetRequiredService<NavigationManager>().NavigateTo("item/123");   // an item page, not the board
        UiService ui = ctx.Services.GetRequiredService<UiService>();
        nav!.Find("button.nav-toggle").Click();
        Assert.Empty(nav.FindAll(".nav-worklog"));

        IRenderedComponent<Worklog> log = OpenWorklog(ctx);
        nav.WaitForAssertion(() => Assert.NotEmpty(nav.FindAll(".nav-worklog")));
        IElement block = nav.Find(".nav-worklog");
        Assert.Equal("Worklog", block.QuerySelector(".nav-h")!.TextContent.Trim());
        Assert.Empty(nav.FindAll(".organise"));   // not split: the page has no list, so no list options
        Assert.Equal(["Notes", "Decisions", "Bugs", "Events", "Links", "Subtasks", "Edits"], block.QuerySelectorAll("button.nav-pill").Select(l => l.TextContent.Trim()));
        Assert.All(block.QuerySelectorAll("button.nav-pill"), b => Assert.Equal("true", b.GetAttribute("aria-pressed")));
        Assert.Contains("everyone", block.QuerySelector(".field .dd")!.TextContent);
        Assert.Empty(block.QuerySelectorAll("button.nav-act"));   // the list of actions is one Add entry menu now
        Assert.Equal("Add entry", block.QuerySelector("button.nav-add")!.TextContent.Trim().TrimStart('+').Trim());

        // a filter acts on the feed at once
        string[] kinds() => [.. log.FindAll(".wk-day .wk-e").Select(e => e.GetAttribute("data-kind")!)];
        Assert.Contains("note", kinds());
        nav.FindAll(".nav-worklog button.nav-pill")[0].Click();   // Notes
        log.WaitForAssertion(() => Assert.DoesNotContain("note", kinds()));
        Assert.False(ui.WorklogFilter.Notes);
        Assert.True(ui.WorklogFilter.Events);

        // who: a dropdown of the people in the feed
        nav.Find(".nav-worklog .field .dd").Click();
        nav.WaitForAssertion(() => Assert.True(nav.FindAll(".rz-dropdown-item").Count > 1));
        Assert.Equal("everyone", nav.FindAll(".rz-dropdown-item")[0].TextContent.Trim());
        Assert.Equal(UiPeople(ctx).AsEnumerable(), nav.FindAll(".rz-dropdown-item").Skip(1).Select(i => i.TextContent.Trim()));   // each person seen in the feed

        // an action does what the day menu does for today
        int before = NotesApi.GetForWorkItem(123).Count;
        nav.Find(".nav-worklog button.nav-add").Click();
        nav.FindAll("button[role=menuitem]").Single(b => b.TextContent.Trim() == "Note").Click();
        log.WaitForAssertion(() => Assert.Equal(before + 1, NotesApi.GetForWorkItem(123).Count));
        log.WaitForAssertion(() => Assert.NotEmpty(log.FindAll(".wk-edit")));   // opened for writing, in today's group

        // the block goes with the worklog
        ((IDisposable)log.Instance).Dispose();   // the view goes away; the side menu stays
        Assert.Null(ui.WorklogOpen);
        nav.WaitForAssertion(() => Assert.Empty(nav.FindAll(".nav-worklog")));
    }

    [Fact(DisplayName = "In split view the Worklog block comes before the list's organise, sort and display blocks")]
    public async Task SplitViewPutsTheWorklogFirst()
    {
        await using BunitContext ctx = NewContext();
        SettingsStore settings = ctx.Services.GetRequiredService<SettingsStore>();
        settings.Update(s => s with { Split = true });
        ctx.Services.GetRequiredService<NavigationManager>().NavigateTo("item/123");
        nav!.Find("button.nav-toggle").Click();

        // the item is open beside the list, with no worklog: the menu holds the list's options as before
        nav.WaitForAssertion(() => Assert.NotEmpty(nav.FindAll(".organise")));
        Assert.Empty(nav.FindAll(".nav-worklog"));

        IRenderedComponent<Worklog> log = OpenWorklog(ctx);
        nav.WaitForAssertion(() => Assert.NotEmpty(nav.FindAll(".nav-worklog")));
        string html = nav.Find("nav.side").InnerHtml;
        int worklog = html.IndexOf("nav-worklog", StringComparison.Ordinal), organise = html.IndexOf("class=\"organise\"", StringComparison.Ordinal);
        int sort = html.IndexOf("sort by", StringComparison.Ordinal), show = html.IndexOf("show subtasks", StringComparison.Ordinal);
        Assert.InRange(worklog, 0, organise - 1);
        Assert.InRange(organise, worklog, sort - 1);
        Assert.True(show > sort, "the display switches follow the sort and filters");

        // the list's blocks stay usable under it
        Assert.NotEmpty(nav.FindAll(".organise .dd"));

        // a window too narrow for the split view hides the list, so its options leave the menu; the Worklog block stays
        UiService ui = ctx.Services.GetRequiredService<UiService>();
        ui.SetNarrowWindow(true);
        nav.WaitForAssertion(() => Assert.Empty(nav.FindAll(".organise")));
        Assert.NotEmpty(nav.FindAll(".nav-worklog"));
        ui.SetNarrowWindow(false);
        nav.WaitForAssertion(() => Assert.NotEmpty(nav.FindAll(".organise")));

        // the worklog closes: the menu is the list's alone
        ((IDisposable)log.Instance).Dispose();   // the view goes away; the side menu stays
        nav.WaitForAssertion(() => Assert.Empty(nav.FindAll(".nav-worklog")));
        Assert.NotEmpty(nav.FindAll(".organise"));
    }

    // ── worklog days ──

    [Fact(DisplayName = "The Worklog block lists the feed's days newest first with their entry counts, and clicking a day jumps the feed to it")]
    public async Task SideMenuListsTheWorklogDays()
    {
        await using BunitContext ctx = NewContext();
        ctx.Services.GetRequiredService<SettingsStore>().Update(s => s with { Split = false });
        ctx.Services.GetRequiredService<NavigationManager>().NavigateTo("item/123");
        nav!.Find("button.nav-toggle").Click();
        IRenderedComponent<Worklog> log = OpenWorklog(ctx);
        nav.WaitForAssertion(() => Assert.NotEmpty(nav.FindAll(".nav-worklog .nav-day")));

        // one row per day of the feed, in the feed's order, each with the number of entries it shows
        IElement[] feed = [.. log.FindAll(".wk-day[data-day]")];
        IElement[] rows() => [.. nav.FindAll(".nav-worklog button.nav-day")];
        string label(IElement b) => b.FirstChild!.TextContent.Trim();
        Assert.Equal("Days", nav.FindAll(".nav-worklog .nav-h")[1].TextContent.Trim());
        Assert.True(feed.Length >= 2);
        Assert.Equal(feed.Select(d => d.QuerySelector(".wk-date-t")!.TextContent.Trim()), rows().Take(feed.Length).Select(label));
        Assert.Equal(feed.Select(d => d.QuerySelectorAll(".wk-e").Length.ToString()), rows().Take(feed.Length).Select(b => b.QuerySelector(".count")!.TextContent.Trim()));
        Assert.Contains("Today", label(rows()[0]));

        // clicking the oldest day unfolds it if folded and scrolls the feed to it
        string day = feed[^1].GetAttribute("data-day")!;
        feed[^1].QuerySelector(".wk-date")!.Click();   // fold it
        log.WaitForAssertion(() => Assert.Empty(log.Find($".wk-day[data-day='{day}']").QuerySelectorAll(".wk-e")));
        rows()[feed.Length - 1].Click();
        log.WaitForAssertion(() => Assert.NotEmpty(log.Find($".wk-day[data-day='{day}']").QuerySelectorAll(".wk-e")));
        log.WaitForAssertion(() => Assert.Equal($".wk-day[data-day='{day}']", ctx.JSInterop.Invocations["rtm.scrollToDay"].Single().Arguments[0]));
    }

    // ── decisions and edits ──

    [Fact(DisplayName = "The Worklog block shows Decisions and Edits filter pills and a Decision entry in the Add entry menu")]
    public async Task SideMenuShowsDecisionsAndEdits()
    {
        await using BunitContext ctx = NewContext();
        ctx.Services.GetRequiredService<SettingsStore>().Update(s => s with { Split = false });
        ctx.Services.GetRequiredService<NavigationManager>().NavigateTo("item/123");
        UiService ui = ctx.Services.GetRequiredService<UiService>();
        nav!.Find("button.nav-toggle").Click();
        IRenderedComponent<Worklog> log = OpenWorklog(ctx);
        nav.WaitForAssertion(() => Assert.NotEmpty(nav.FindAll(".nav-worklog")));
        IElement block = nav.Find(".nav-worklog");

        Assert.Equal(["Notes", "Decisions", "Bugs", "Events", "Links", "Subtasks", "Edits"], block.QuerySelectorAll("button.nav-pill").Select(l => l.TextContent.Trim()));

        nav.FindAll(".nav-worklog button.nav-pill")[1].Click();   // Decisions
        Assert.False(ui.WorklogFilter.Decisions);
        Assert.True(ui.WorklogFilter.Notes);
        nav.FindAll(".nav-worklog button.nav-pill")[6].Click();   // Edits
        Assert.False(ui.WorklogFilter.Edits);
        Assert.True(ui.WorklogFilter.Events);
        nav.FindAll(".nav-worklog button.nav-pill")[2].Click();   // Bugs
        Assert.False(ui.WorklogFilter.Bugs);
        nav.FindAll(".nav-worklog button.nav-pill")[1].Click();
        nav.FindAll(".nav-worklog button.nav-pill")[6].Click();
        nav.FindAll(".nav-worklog button.nav-pill")[2].Click();
        Assert.Equal(new WorklogFilters(), ui.WorklogFilter);

        // the Decision entry does what the day menu's does for today
        nav.Find(".nav-worklog button.nav-add").Click();
        nav.FindAll("button[role=menuitem]").Single(b => b.TextContent.Trim() == "Decision").Click();
        log.WaitForAssertion(() => Assert.Single(NotesApi.GetForWorkItem(123), n => n.IsDecision));
        log.WaitForAssertion(() => Assert.NotEmpty(log.FindAll(".wk-edit.wk-decision")));
    }

    // ── analytics view ──

    private void ShowAnalytics()
    {
        nav!.Find("button[aria-label=Layout]").Click();
        nav.FindAll(".menu-item").Single(i => i.TextContent.Trim() == "Analytics").Click();
    }

    [Fact(DisplayName = "The Analytics view shows the two default charts, each an SVG with its title and legend")]
    public async Task AnalyticsShowsTheDefaultCharts()
    {
        await using BunitContext ctx = NewContext();
        IRenderedComponent<BoardPage> board = ctx.Render<BoardPage>();
        ShowAnalytics();

        board.WaitForAssertion(() => Assert.Equal(2, board.FindAll(".an-chart").Count));
        Assert.Equal(["Items by status", "Status over time"], board.FindAll(".an-chart h3").Select(h => h.TextContent.Trim()));
        foreach (IElement chart in board.FindAll(".an-chart"))
        {
            IElement svg = chart.QuerySelector("svg[role=img]")!;
            Assert.Contains(chart.QuerySelector("h3")!.TextContent.Trim(), svg.GetAttribute("aria-label"));
            Assert.NotEmpty(svg.QuerySelectorAll("title"));   // hover text on the marks
        }
        Assert.Empty(board.FindAll(".an-chart")[0].QuerySelectorAll(".an-legend li"));   // not grouped: no legend
        IElement[] legend = [.. board.FindAll(".an-chart")[1].QuerySelectorAll(".an-legend li")];
        Assert.NotEmpty(legend);
        Assert.All(legend, l => Assert.Contains(l.TextContent.Trim(), WorkItemsApi.Statuses));
        Assert.Empty(board.FindAll(".board-wrap"));   // the board is gone
    }

    [Fact(DisplayName = "Editing a chart's layout, axis or grouping redraws it and the choice is remembered per board; Add chart adds one and Delete removes it")]
    public async Task AnalyticsChartsAreEditable()
    {
        await using BunitContext ctx = NewContext();
        SettingsStore settings = ctx.Services.GetRequiredService<SettingsStore>();
        IRenderedComponent<BoardPage> board = ctx.Render<BoardPage>();
        ShowAnalytics();
        board.WaitForAssertion(() => Assert.Equal(2, board.FindAll(".an-chart").Count));

        IElement Chart(int n) => board.FindAll(".an-chart")[n];
        void Pick(string field, string value)
        {
            IElement Field() => board.Find(".an-edit").QuerySelectorAll(".field").Single(f => f.QuerySelector("label")!.TextContent.Trim() == field);
            Field().QuerySelector(".dd")!.Click();
            board.WaitForAssertion(() => Assert.Contains(Field().QuerySelectorAll(".rz-dropdown-item"), i => i.TextContent.Trim() == value));
            Field().QuerySelectorAll(".rz-dropdown-item").First(i => i.TextContent.Trim() == value).Click();
        }

        Chart(0).QuerySelector("button[aria-label='Edit chart']")!.Click();
        board.WaitForAssertion(() => Assert.NotNull(board.Find(".an-edit")));
        Assert.Equal("column", Chart(0).QuerySelector("svg.chart")!.GetAttribute("data-layout"));
        Pick("Layout", "Bar");
        board.WaitForAssertion(() => Assert.Equal("bar", Chart(0).QuerySelector("svg.chart")!.GetAttribute("data-layout")));
        Pick("X-axis", "Feature");
        Pick("Group by", "Status");
        board.WaitForAssertion(() => Assert.NotEmpty(Chart(0).QuerySelectorAll(".an-legend li")));
        Assert.Contains("feature", Chart(0).QuerySelector("svg.chart")!.GetAttribute("aria-label")!, StringComparison.OrdinalIgnoreCase);
        ChartSpec saved = settings.Board(repo.Root).Charts![0];
        Assert.Equal(("bar", "feature", "status"), (saved.Layout, saved.X, saved.Group));

        // another view of the same board remembers it
        IRenderedComponent<BoardPage> again = ctx.Render<BoardPage>();
        Assert.Equal("bar", again.FindAll(".an-chart")[0].QuerySelector("svg.chart")!.GetAttribute("data-layout"));

        board.Find(".an-add").Click();
        board.WaitForAssertion(() => Assert.Equal(3, board.FindAll(".an-chart").Count));
        Assert.Equal(3, settings.Board(repo.Root).Charts!.Count);
        Assert.NotEmpty(board.FindAll(".an-edit"));   // the new chart opens in its editor
        board.Find(".an-edit button.an-del").Click();
        board.WaitForAssertion(() => Assert.Equal(2, board.FindAll(".an-chart").Count));
        Assert.Equal(2, settings.Board(repo.Root).Charts!.Count);
        Assert.Equal("Items by status", board.FindAll(".an-chart h3")[0].TextContent.Trim());
    }

    // ── changed picker: the reported case ──

    [Fact(DisplayName = "A changed period that covers nearly every item still shows the picker working: the legend counts how many items are shown of those the other filters leave")]
    public async Task ChangedLegendCountsTheItemsThatChanged()
    {
        await using BunitContext ctx = NewContext();
        SettingsStore settings = ctx.Services.GetRequiredService<SettingsStore>();
        DateOnly first = new(DateTime.Today.Year, DateTime.Today.Month, 1), last = first.AddDays(6), today = DateOnly.FromDateTime(DateTime.Today);
        // every file was last written long ago; only 123 has something logged in the first week of the month
        foreach (string file in Directory.EnumerateFiles(repo.OpenSpec, "*", SearchOption.AllDirectories)) File.SetLastWriteTime(file, new DateTime(2026, 1, 1));
        string log123 = Path.Combine(repo.OpenSpec, "changes", "add-health-check", "events.log");
        File.WriteAllText(log123, $"{first.AddDays(3):yyyy-MM-dd}T10:00:00 | Tester | custom | something happened\n");
        File.SetLastWriteTime(log123, new DateTime(2026, 1, 1));
        File.Delete(Path.Combine(repo.OpenSpec, "changes", "export-remittances", "events.log"));
        IRenderedComponent<BoardPage> board = ctx.Render<BoardPage>();
        nav!.Find("button.nav-toggle").Click();
        int shown = board.FindAll("article.card").Count;
        Assert.True(shown > 1);

        settings.SaveBoard(repo.Root, settings.Board(repo.Root) with { ChangedFrom = first, ChangedTo = last });
        nav.WaitForAssertion(() => Assert.Equal($"1 of {shown} shown", nav.Find(".legend .lg.count").TextContent.Trim()));
        board.WaitForAssertion(() => Assert.Single(board.FindAll("article.card")));

        // a period that takes in every day since the start of the year: everything is in it, and the legend says so
        settings.SaveBoard(repo.Root, settings.Board(repo.Root) with { ChangedFrom = new DateOnly(2026, 1, 1), ChangedTo = today });
        nav.WaitForAssertion(() => Assert.Equal($"{shown} of {shown} shown", nav.Find(".legend .lg.count").TextContent.Trim()));
        board.WaitForAssertion(() => Assert.Equal(shown, board.FindAll("article.card").Count));
        Assert.Equal($"changed {new DateOnly(2026, 1, 1):d MMM} – {today:d MMM}", nav.Find(".legend .lg.head").TextContent.Trim());   // the head line is unchanged
        settings.SaveBoard(repo.Root, settings.Board(repo.Root) with { ChangedFrom = null, ChangedTo = null });
        nav.WaitForAssertion(() => Assert.Empty(nav.FindAll(".legend .lg.count")));
    }

    // ── no agent filter ──

    [Fact(DisplayName = "The side menu has no agent work to review option, and a settings file that still has AgentOnly loads")]
    public async Task NoAgentFilter()
    {
        await using BunitContext ctx = NewContext();
        SettingsStore settings = ctx.Services.GetRequiredService<SettingsStore>();
        IRenderedComponent<BoardPage> board = ctx.Render<BoardPage>();
        nav!.Find("button.nav-toggle").Click();
        Assert.Empty(nav.FindAll("input#f-agent"));
        Assert.DoesNotContain("agent work to review", nav.Find(".nav-view").TextContent);
        Assert.NotEmpty(nav.FindAll("button#f-hidden"));   // the other options stay
        int shown = board.FindAll("article.card").Count;

        // a settings file written before the checkbox went: its AgentOnly is ignored, and every item shows
        settings.SaveBoard(repo.Root, settings.Board(repo.Root) with { Compact = true });
        string json = File.ReadAllText(settingsFile);
        Assert.Contains("\"Compact\": true", json);
        File.WriteAllText(settingsFile, json.Replace("\"Compact\": true", "\"Compact\": true,\n \"AgentOnly\": true"));
        BoardOptions loaded = new SettingsStore(settingsFile).Board(repo.Root);
        Assert.True(loaded.Compact);   // the rest of the board's options came through
        Assert.Equal(shown, BoardModel.Filtered(WorkItemsApi.GetAll(), loaded).Count);
    }

    // ── changed period with times ──

    [Fact(DisplayName = "The changed picker takes a from and a to time once a range is picked, shows them in the field and the legend, and Show all clears them; the times are remembered per board")]
    public async Task ChangedPickerTakesTimes()
    {
        await using BunitContext ctx = NewContext();
        SettingsStore settings = ctx.Services.GetRequiredService<SettingsStore>();
        DateOnly day = new(DateTime.Today.Year, DateTime.Today.Month, 1);
        day = day.AddDays(3);
        foreach (string file in Directory.EnumerateFiles(repo.OpenSpec, "*", SearchOption.AllDirectories)) File.SetLastWriteTime(file, new DateTime(2026, 1, 1));
        string log123 = Path.Combine(repo.OpenSpec, "changes", "add-health-check", "events.log");
        File.WriteAllText(log123, $"{day:yyyy-MM-dd}T10:00:00 | Tester | custom | something happened\n");   // the one thing that happened, at 10:00
        File.SetLastWriteTime(log123, new DateTime(2026, 1, 1));
        File.Delete(Path.Combine(repo.OpenSpec, "changes", "export-remittances", "events.log"));
        IRenderedComponent<BoardPage> board = ctx.Render<BoardPage>();
        int shown = board.FindAll("article.card").Count;
        BoardOptions Saved() => settings.Board(repo.Root);
        AngleSharp.Dom.IElement From() => nav!.Find("input[type='time'][aria-label='from time']");
        AngleSharp.Dom.IElement To() => nav!.Find("input[type='time'][aria-label='to time']");

        nav!.Find("button.nav-toggle").Click();
        nav.Find("button[aria-label='changed']").Click();
        nav.WaitForAssertion(() => Assert.NotEmpty(nav.FindAll(".range-pop")));
        Assert.True(From().HasAttribute("disabled") && To().HasAttribute("disabled"));   // no range yet: nothing to time
        for (int n = 0; n < 2; n++)   // one day: its first click and its second
        {
            await nav.InvokeAsync(() => { });
            nav.WaitForAssertion(() => nav.Find($".range-pop button.rd[data-day='{day:yyyy-MM-dd}']").Click());
        }
        nav.WaitForAssertion(() => Assert.Equal((day, day), (Saved().ChangedFrom, Saved().ChangedTo)));
        board.WaitForAssertion(() => Assert.Single(board.FindAll("article.card")));   // the whole day: 123's event at 10:00 is in
        Assert.Equal($"{day:d MMM}", nav.Find("button[aria-label='changed']").TextContent.Trim());

        nav.Find("button[aria-label='changed']").Click();
        nav.WaitForAssertion(() => Assert.False(From().HasAttribute("disabled") || To().HasAttribute("disabled")));   // a day is picked: the times open up
        await nav.InvokeAsync(() => { });
        From().Change("10:30");   // after the event
        nav.WaitForAssertion(() => Assert.Equal(new TimeOnly(10, 30), Saved().ChangedFromTime));
        nav.WaitForAssertion(() => Assert.Equal($"{day:d MMM} from 10:30", nav.Find("button[aria-label='changed']").TextContent.Trim()));
        await nav.InvokeAsync(() => { });
        To().Change("18:00");
        nav.WaitForAssertion(() => Assert.Equal((new TimeOnly(10, 30), new TimeOnly(18, 0)), (Saved().ChangedFromTime, Saved().ChangedToTime)));   // remembered per board
        nav.WaitForAssertion(() => Assert.Equal($"{day:d MMM} 10:30 – 18:00", nav.Find("button[aria-label='changed']").TextContent.Trim()));
        Assert.Equal($"changed {day:d MMM} 10:30 – {day:d MMM} 18:00", nav.Find(".legend .lg.head").TextContent.Trim());
        nav.WaitForAssertion(() => Assert.Equal($"0 of {shown} shown", nav.Find(".legend .lg.count").TextContent.Trim()));
        board.WaitForAssertion(() => Assert.Empty(board.FindAll("article.card")));   // the event is before the window

        await nav.InvokeAsync(() => { });
        From().Change("09:00");   // before it: 123 is back
        nav.WaitForAssertion(() => Assert.Equal(new TimeOnly(9, 0), Saved().ChangedFromTime));
        board.WaitForAssertion(() => Assert.Single(board.FindAll("article.card")));
        nav.WaitForAssertion(() => Assert.Equal($"1 of {shown} shown", nav.Find(".legend .lg.count").TextContent.Trim()));

        await nav.InvokeAsync(() => { });
        nav.Find(".range-pop button.range-clear").Click();   // Show all
        nav.WaitForAssertion(() => Assert.Equal((null, null, null, null), (Saved().ChangedFrom, Saved().ChangedTo, Saved().ChangedFromTime, Saved().ChangedToTime)));
        nav.WaitForAssertion(() => Assert.Contains("any time", nav.Find("button[aria-label='changed']").TextContent));
        Assert.Empty(nav.FindAll(".legend .lg.head"));
        board.WaitForAssertion(() => Assert.Equal(shown, board.FindAll("article.card").Count));
    }

    // ── menu patch ──

    [Fact(DisplayName = "The side menu has no checkboxes: filters and options are pills, filled when on and underlined when off")]
    public async Task MenuHasPillsNotCheckboxes()
    {
        await using BunitContext ctx = NewContext();
        IRenderedComponent<BoardPage> board = ctx.Render<BoardPage>();
        nav!.Find("button.nav-toggle").Click();

        Assert.Empty(nav.FindAll("input[type=checkbox]"));
        Assert.Empty(nav.FindAll("label.check"));
        IElement compact = nav.Find("button.nav-pill[data-opt=compact]");
        Assert.Equal("compact cards", compact.TextContent.Trim());
        Assert.Equal("false", compact.GetAttribute("aria-pressed"));
        Assert.DoesNotContain("is-sel", compact.ClassList);

        compact.Click();   // a real button: a click turns it on, and the board follows
        compact = nav.Find("button.nav-pill[data-opt=compact]");
        Assert.Equal("true", compact.GetAttribute("aria-pressed"));
        Assert.Contains("is-sel", compact.ClassList);
        board.WaitForAssertion(() => Assert.NotEmpty(board.FindAll("article.card.collapsed")));

        // the worklog's filters are pills too
        ctx.Services.GetRequiredService<SettingsStore>().Update(s => s with { Split = false });
        ctx.Services.GetRequiredService<NavigationManager>().NavigateTo("item/123");
        ctx.Services.GetRequiredService<UiService>().OpenWorklog(new WorklogActions(() => { }, () => { }, () => { }, () => { }, () => { }, () => { }));
        nav.WaitForAssertion(() => Assert.NotEmpty(nav.FindAll(".nav-worklog button.nav-pill")));
        Assert.Empty(nav.FindAll("input[type=checkbox]"));
        nav.FindAll(".nav-worklog button.nav-pill")[0].Click();
        IElement notes = nav.FindAll(".nav-worklog button.nav-pill")[0];
        Assert.Equal("false", notes.GetAttribute("aria-pressed"));
        Assert.DoesNotContain("is-sel", notes.ClassList);
        Assert.Contains("is-sel", nav.FindAll(".nav-worklog button.nav-pill")[1].ClassList);
    }

    [Fact(DisplayName = "Sort by is the first field under Organise, and show all hidden sits under the Hidden header")]
    public async Task SortFirstAndShowAllHiddenUnderHidden()
    {
        await using BunitContext ctx = NewContext();
        WorkItemsApi.Hide(107);
        IRenderedComponent<BoardPage> board = ctx.Render<BoardPage>();
        nav!.Find("button.nav-toggle").Click();

        string[] fields = [.. nav.FindAll(".organise .field > label").Select(l => l.TextContent.Trim())];
        Assert.Equal(["sort by", "columns", "rows", "colour"], fields);

        // the option is under the Hidden header, named for what it does, and is not among the display switches
        IElement hidden = nav.Find(".nav-hidden");
        Assert.Equal("Hidden", hidden.QuerySelector(".nav-h")!.TextContent.Trim());
        IElement show = hidden.QuerySelector("button#f-hidden")!;
        Assert.Equal("show all hidden", show.TextContent.Trim());
        Assert.Equal("false", show.GetAttribute("aria-pressed"));
        Assert.Single(nav.FindAll("button#f-hidden"));
        Assert.Equal(3, board.FindAll("article.card").Count);

        show.Click();
        board.WaitForAssertion(() => Assert.Equal(4, board.FindAll("article.card").Count));
        Assert.Equal("true", nav.Find(".nav-hidden button#f-hidden").GetAttribute("aria-pressed"));
    }

    // The dropdown of an Organise or filter field in the expanded side menu, by its label.
    private IElement NavDropdown(string field) => nav!.FindAll(".nav-view .field").First(f => f.QuerySelector("label")!.TextContent.Trim() == field).QuerySelector(".dd")!;

    [Fact(DisplayName = "Side menu dropdown values are pills: grey for none or the neutral choice, yellow for any other value")]
    public async Task SideMenuDropdownValuesArePills()
    {
        await using BunitContext ctx = NewContext();
        SettingsStore settings = ctx.Services.GetRequiredService<SettingsStore>();
        BoardOptions o = settings.Board(repo.Root);
        settings.SaveBoard(repo.Root, o with { View = "list", ListGroupBy = "none", ListSwimlane = "status", ColorBy = "none", Who = "all", Period = "all" });
        nav!.Find("button.nav-toggle").Click();

        void Is(string field, string look)
        {
            IElement dd = NavDropdown(field);
            Assert.Contains(look, dd.ClassList);
            Assert.DoesNotContain(look == "is-on" ? "is-off" : "is-on", dd.ClassList);
        }
        nav.WaitForAssertion(() => Is("groups", "is-off"));
        Is("subgroups", "is-on");
        Is("colour", "is-off");
        Is("contributor", "is-off");
        Is("period", "is-off");
        Is("sort by", "is-on");   // a sort order is always a choice: nothing in it is neutral

        settings.SaveBoard(repo.Root, settings.Board(repo.Root) with { ListGroupBy = "label", ColorBy = "priority", Period = "overdue", Who = "Alice" });
        nav.WaitForAssertion(() => Is("groups", "is-on"));
        Is("colour", "is-on");
        Is("period", "is-on");
        Is("contributor", "is-on");
        Assert.Contains("nav-dd", NavDropdown("groups").ClassList);

        string css = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "wwwroot", "css", "app.css"));
        Assert.Matches(@"nav\.side \.dd\.is-on\s*\{[^}]*var\(--sun\)", css);
        Assert.Matches(@"nav\.side \.dd\.is-off\s*\{[^}]*var\(--stone\)", css);
    }

    [Fact(DisplayName = "The changed selector is a grey pill at any time and yellow once a range is set")]
    public async Task ChangedSelectorIsAPill()
    {
        await using BunitContext ctx = NewContext();
        SettingsStore settings = ctx.Services.GetRequiredService<SettingsStore>();
        nav!.Find("button.nav-toggle").Click();
        IElement Btn() => nav.Find(".field.changed .dates-btn");
        Assert.Equal("any time", Btn().TextContent.Trim());
        Assert.Contains("is-off", Btn().ClassList);
        Assert.DoesNotContain("is-on", Btn().ClassList);

        settings.SaveBoard(repo.Root, settings.Board(repo.Root) with { ChangedFrom = new DateOnly(2026, 11, 2), ChangedTo = new DateOnly(2026, 11, 8), ChangedFromTime = new TimeOnly(9, 30) });
        nav.WaitForAssertion(() => Assert.Contains("is-on", Btn().ClassList));
        Assert.DoesNotContain("is-off", Btn().ClassList);
        Assert.Contains("09:30", Btn().TextContent);
    }

    [Fact(DisplayName = "Show all hidden brings the hidden columns, rows and projects back on the board and the list")]
    public async Task ShowAllHiddenBringsEverythingBack()
    {
        await using BunitContext ctx = NewContext();
        TwoProjects(ctx);
        SettingsStore settings = ctx.Services.GetRequiredService<SettingsStore>();
        IRenderedComponent<BoardPage> board = ctx.Render<BoardPage>();
        board.WaitForAssertion(() => Assert.Equal(2, board.FindAll(".proj").Count));
        nav!.Find("button.nav-toggle").Click();
        IElement Switch() => nav.Find("button#f-hidden");
        Assert.Equal("show all hidden", Switch().TextContent.Trim());

        // a hidden column and a hidden project
        board.FindAll("header.col-h").First(h => h.TextContent.Contains("Blocked")).Click();
        board.FindAll(".menu-item").Single(i => i.TextContent.Trim() == "Hide column").Click();
        settings.ToggleProjectHidden(repo2.Root);
        board.WaitForAssertion(() => Assert.Single(board.FindAll(".proj")));
        Assert.DoesNotContain("Blocked", Columns(board));
        Assert.Equal("show all hidden", Switch().TextContent.Trim());   // the label is the same in every state

        Switch().Click();
        board.WaitForAssertion(() => Assert.Equal(2, board.FindAll(".proj").Count));
        Assert.Contains("Blocked", Columns(board));
        Assert.Equal("true", Switch().GetAttribute("aria-pressed"));
        Assert.Equal("show all hidden", Switch().TextContent.Trim());
        Assert.True(settings.Board(repo.Root).ShowHidden);   // remembered

        // a hidden row
        settings.SaveBoard(repo.Root, settings.Board(repo.Root) with { ShowHidden = false, Swimlane = "label", Hidden = [.. settings.Board(repo.Root).Hidden!, "row:label:bug"] });
        List<string> Rows() => board.FindAll(".swim-h").Select(h => h.GetAttribute("data-swim")!).ToList();
        board.WaitForAssertion(() => Assert.DoesNotContain("bug", Rows()));
        Switch().Click();
        board.WaitForAssertion(() => Assert.Contains("bug", Rows()));

        // the list: a hidden grid column
        settings.SaveBoard(repo.Root, settings.Board(repo.Root) with { ShowHidden = false, Swimlane = "none", View = "list", Hidden = [.. settings.Board(repo.Root).Hidden!, BoardModel.ListColumnKey("Progress")] });
        List<string> Heads() => board.FindAll("thead th .th-l").Select(h => h.TextContent.Trim()).ToList();
        board.WaitForAssertion(() => Assert.DoesNotContain("Progress", Heads()));
        Switch().Click();
        board.WaitForAssertion(() => Assert.Contains("Progress", Heads()));
    }

    /// <summary>Two projects, each with its work item 107 "deleted" (hidden in the app), so show all hidden has an item of its own to bring back in each.</summary>
    private (Workspace Ws, SettingsStore Settings) HiddenSetup(BunitContext ctx)
    {
        TwoProjects(ctx);
        Workspace ws = ctx.Services.GetRequiredService<Workspace>();
        foreach (string root in new[] { repo.Root, repo2.Root }) Assert.True(ws.In(root, () => WorkItemsApi.Hide(107)).Item1);
        return (ws, ctx.Services.GetRequiredService<SettingsStore>());
    }

    private static List<string> CardTitles(IRenderedComponent<BoardPage> board, string project) =>
        [.. board.FindAll($".proj[data-proj='{project}'] article.card .band").Select(b => b.TextContent.Trim()).Order()];

    [Fact(DisplayName = "With show all hidden on every work item of a hidden project renders as a card and the column count equals the cards shown")]
    public async Task ShowAllHiddenRendersEveryCardAndCountsMatch()
    {
        await using BunitContext ctx = NewContext();
        (Workspace ws, SettingsStore settings) = HiddenSetup(ctx);
        settings.SaveBoard(repo.Root, settings.Board(repo.Root) with { Hidden = ["status:Planned", "status:Confirmed"], ShowHidden = true });   // a hidden column
        settings.ToggleProjectHidden(repo2.Root);                                                                                          // a hidden project
        IRenderedComponent<BoardPage> board = ctx.Render<BoardPage>();
        board.WaitForAssertion(() => Assert.Equal(2, board.FindAll(".proj").Count));

        foreach (TestRepo r in new[] { repo, repo2 })
        {
            List<string> every = ws.In(r.Root, () => WorkItemsApi.GetAll(includeHidden: true).Select(i => i.Title).Order().ToList());
            Assert.Contains(every, t => t.Length > 0);
            Assert.Equal(every, CardTitles(board, NameOf(r)));   // the deleted item, the hidden column's and the hidden project's included
        }
        Assert.Contains("Planned", Columns(board));
        // the header's number is the number of cards under it, across the projects
        foreach (IElement head in board.FindAll(".head-row section.col"))
        {
            string key = head.GetAttribute("data-key")!;
            int shown = board.FindAll($".proj section.col[data-key='{key}'] article.card").Count;
            Assert.Equal(shown, int.Parse(head.QuerySelector(".count")!.TextContent));
        }
    }

    [Fact(DisplayName = "With show all hidden on only contributor, period, changed and a project's collapse can leave a work item out")]
    public async Task ShowAllHiddenOnlyContributorPeriodAndChangedLeaveItemsOut()
    {
        await using BunitContext ctx = NewContext();
        (Workspace ws, SettingsStore settings) = HiddenSetup(ctx);
        // everything that can be put aside: a column, a row (the unlabeled one holds every fixture item), a project that is also folded (the only one that stays aside)
        settings.ToggleProjectHidden(repo2.Root);
        settings.ToggleProjectCollapsed(repo2.Root);
        IRenderedComponent<BoardPage> board = ctx.Render<BoardPage>();
        BoardOptions all = settings.Board(repo.Root) with { ShowHidden = true, Swimlane = "label", Hidden = ["status:Planned", $"row:label:{BoardModel.NoRow}"] };
        settings.SaveBoard(repo.Root, all);
        board.WaitForAssertion(() => Assert.Equal(2, board.FindAll(".proj").Count));

        List<string> Expected(Func<WorkItem, bool> allowed) =>
            [.. new[] { repo }.SelectMany(r => ws.In(r.Root, () => WorkItemsApi.GetAll(includeHidden: true).Where(allowed).Select(i => NameOf(r) + ":" + i.Title))).Order()];
        List<string> Shown() => [.. new[] { repo, repo2 }.SelectMany(r => CardTitles(board, NameOf(r)).Select(t => NameOf(r) + ":" + t)).Order()];

        board.WaitForAssertion(() => Assert.Equal(Expected(_ => true), Shown()));   // no filter: everything, hidden or not

        foreach ((BoardOptions o, Func<WorkItem, bool> allowed) in new (BoardOptions, Func<WorkItem, bool>)[]
        {
            (all with { Who = "Tom" }, i => i.Contributors.Contains("Tom")),                      // contributor
            (all with { Period = "none" }, i => i.Due is null),                                  // period
            (all with { Who = "Artyom", Period = "none" }, i => i.Contributors.Contains("Artyom") && i.Due is null),
            (all with { ChangedFrom = new DateOnly(2000, 1, 1), ChangedTo = new DateOnly(2000, 1, 2) }, _ => false),   // changed: nothing changed in 2000
        })
        {
            settings.SaveBoard(repo.Root, o);
            board.WaitForAssertion(() => Assert.Equal(Expected(allowed), Shown()));
        }
    }

    [Fact(DisplayName = "A collapsed project stays collapsed while show all hidden is on")]
    public async Task CollapsedProjectStaysCollapsedWhileShowAllHiddenIsOn()
    {
        await using BunitContext ctx = NewContext();
        (Workspace ws, SettingsStore settings) = HiddenSetup(ctx);
        settings.ToggleProjectCollapsed(repo2.Root);
        IRenderedComponent<BoardPage> board = ctx.Render<BoardPage>();
        board.WaitForAssertion(() => Assert.Equal(2, board.FindAll(".proj").Count));
        Assert.Empty(CardTitles(board, NameOf(repo2)));   // folded: its heading only
        Assert.NotEmpty(CardTitles(board, NameOf(repo)));

        settings.SaveBoard(repo.Root, settings.Board(repo.Root) with { ShowHidden = true });
        board.WaitForAssertion(() => Assert.Equal(ws.In(repo.Root, () => WorkItemsApi.GetAll(includeHidden: true).Select(i => i.Title).Order().ToList()), CardTitles(board, NameOf(repo))));   // the open project got its deleted item back
        Assert.Empty(CardTitles(board, NameOf(repo2)));   // the folded one did not unfold
        Assert.Equal("false", board.Find($".proj[data-proj='{NameOf(repo2)}'] .proj-h .chev").GetAttribute("aria-expanded"));
        Assert.True(settings.ProjectCollapsed(repo2.Root));   // and the setting is untouched

        settings.SaveBoard(repo.Root, settings.Board(repo.Root) with { ShowHidden = false });
        board.WaitForAssertion(() => Assert.Equal(3, CardTitles(board, NameOf(repo)).Count));
        Assert.Empty(CardTitles(board, NameOf(repo2)));
        Assert.Equal("false", board.Find($".proj[data-proj='{NameOf(repo2)}'] .proj-h .chev").GetAttribute("aria-expanded"));
    }

    [Fact(DisplayName = "In the list a collapsed project's rows stay folded while show all hidden is on")]
    public async Task CollapsedProjectKeepsItsRowsFoldedInTheListWhileShowAllHiddenIsOn()
    {
        await using BunitContext ctx = NewContext();
        (Workspace ws, SettingsStore settings) = HiddenSetup(ctx);
        settings.ToggleProjectCollapsed(repo2.Root);
        IRenderedComponent<BoardPage> board = ctx.Render<BoardPage>();
        ShowList();
        board.WaitForAssertion(() => Assert.Equal(2, board.FindAll("tr[data-group^='proj:']").Count));
        int others = board.FindAll("tr[data-id]").Count;

        settings.SaveBoard(repo.Root, settings.Board(repo.Root) with { ShowHidden = true });
        board.WaitForAssertion(() => Assert.True(board.FindAll("tr[data-id]").Count > others));   // the open project's deleted item is back
        Assert.Equal(ws.In(repo.Root, () => WorkItemsApi.GetAll(includeHidden: true).Count), board.FindAll("tr[data-id]").Count);   // the folded project's rows are not
        Assert.True(settings.ProjectCollapsed(repo2.Root));
    }

    [Fact(DisplayName = "A collapsed project's work items count in no column header, with show all hidden on or off")]
    public async Task CollapsedProjectCountsInNoColumnHeader()
    {
        await using BunitContext ctx = NewContext();
        (Workspace ws, SettingsStore settings) = HiddenSetup(ctx);
        settings.ToggleProjectCollapsed(repo2.Root);
        IRenderedComponent<BoardPage> board = ctx.Render<BoardPage>();
        board.WaitForAssertion(() => Assert.Equal(2, board.FindAll(".proj").Count));

        void HeadersCountTheCardsDrawn(bool showAll)
        {
            int total = 0;
            foreach (IElement head in board.FindAll(".head-row section.col"))
            {
                string key = head.GetAttribute("data-key")!;
                int shown = board.FindAll($".proj section.col[data-key='{key}'] article.card").Count;
                Assert.Equal(shown, int.Parse(head.QuerySelector(".count")!.TextContent));
                total += shown;
            }
            Assert.Equal(ws.In(repo.Root, () => WorkItemsApi.GetAll(includeHidden: showAll).Count), total);   // the open project's items only
        }
        HeadersCountTheCardsDrawn(showAll: false);

        settings.SaveBoard(repo.Root, settings.Board(repo.Root) with { ShowHidden = true });
        board.WaitForAssertion(() => Assert.Equal(ws.In(repo.Root, () => WorkItemsApi.GetAll(includeHidden: true).Count), board.FindAll(".proj article.card").Count));
        HeadersCountTheCardsDrawn(showAll: true);

        settings.ToggleProjectCollapsed(repo2.Root);   // unfolded: its items count again
        board.WaitForAssertion(() => Assert.Equal(ws.In(repo.Root, () => WorkItemsApi.GetAll(includeHidden: true).Count) + ws.In(repo2.Root, () => WorkItemsApi.GetAll(includeHidden: true).Count), board.FindAll(".proj article.card").Count));
        foreach (IElement head in board.FindAll(".head-row section.col"))
            Assert.Equal(board.FindAll($".proj section.col[data-key='{head.GetAttribute("data-key")}'] article.card").Count, int.Parse(head.QuerySelector(".count")!.TextContent));
    }

    [Fact(DisplayName = "A collapsed project's work items count in no side menu Hidden count, with show all hidden on or off")]
    public async Task CollapsedProjectCountsInNoSideMenuHiddenCount()
    {
        await using BunitContext ctx = NewContext();
        (_, SettingsStore settings) = HiddenSetup(ctx);
        settings.SaveBoard(repo.Root, settings.Board(repo.Root) with { Hidden = ["status:In Progress"] });
        IRenderedComponent<BoardPage> board = ctx.Render<BoardPage>();
        board.WaitForAssertion(() => Assert.Equal(2, board.FindAll(".proj").Count));
        nav!.Find("button.nav-toggle").Click();
        int Held() => int.Parse(nav.Find(".nav-hidden .grp-row .count").TextContent.Trim());
        nav.WaitForAssertion(() => Assert.True(Held() > 0));   // the open project's In Progress items

        settings.ToggleProjectCollapsed(repo.Root);
        nav.WaitForAssertion(() => Assert.Equal(0, Held()));   // folded: counted nowhere

        nav.Find("button#f-hidden").Click();
        nav.WaitForAssertion(() => Assert.Equal("true", nav.Find("button#f-hidden").GetAttribute("aria-pressed")));
        Assert.Equal(0, Held());

        settings.ToggleProjectCollapsed(repo.Root);
        nav.WaitForAssertion(() => Assert.True(Held() > 0));
    }

    [Fact(DisplayName = "The collapsed menu has a show all hidden button that is pressed when on")]
    public async Task CollapsedMenuShowAllHidden()
    {
        await using BunitContext ctx = NewContext();
        WorkItemsApi.Hide(107);
        IRenderedComponent<BoardPage> board = ctx.Render<BoardPage>();
        IElement Btn() => nav!.Find("button.hidden-btn");
        Assert.Equal("Show all hidden", Btn().GetAttribute("aria-label"));
        Assert.Equal("false", Btn().GetAttribute("aria-pressed"));
        Assert.DoesNotContain("is-sel", Btn().ClassList);
        Assert.NotNull(Btn().QuerySelector("svg"));
        Assert.Equal(3, board.FindAll("article.card").Count);

        Btn().Click();
        board.WaitForAssertion(() => Assert.Equal(4, board.FindAll("article.card").Count));
        Assert.Equal("true", Btn().GetAttribute("aria-pressed"));
        Assert.Contains("is-sel", Btn().ClassList);

        nav!.Find("button.nav-toggle").Click();   // expanded, the same switch is the pill under Hidden
        Assert.Equal("true", nav.Find("button#f-hidden").GetAttribute("aria-pressed"));
        Assert.Empty(nav.FindAll("button.hidden-btn"));
    }

    [Fact(DisplayName = "The Worklog block has one Add entry button whose menu offers note, decision, bug, subtask and the three links")]
    public async Task WorklogHasOneAddEntryMenu()
    {
        await using BunitContext ctx = NewContext();
        ctx.Services.GetRequiredService<SettingsStore>().Update(s => s with { Split = false });
        ctx.Services.GetRequiredService<NavigationManager>().NavigateTo("item/123");
        UiService ui = ctx.Services.GetRequiredService<UiService>();
        nav!.Find("button.nav-toggle").Click();
        List<string> done = [];
        ui.OpenWorklog(new WorklogActions(() => done.Add("note"), () => done.Add("decision"), () => done.Add("document"), () => done.Add("test"), () => done.Add("commit"), () => done.Add("subtask"),
            CanLinkTest: () => false, AddBug: () => done.Add("bug")));
        nav.WaitForAssertion(() => Assert.NotEmpty(nav.FindAll(".nav-worklog")));

        Assert.Empty(nav.FindAll(".nav-worklog button.nav-act"));
        Assert.Single(nav.FindAll(".nav-worklog button.nav-add"));
        Assert.Empty(nav.FindAll("button[role=menuitem]"));   // closed until clicked
        nav.Find(".nav-worklog button.nav-add").Click();
        IElement[] items = [.. nav.FindAll("button[role=menuitem]")];
        Assert.Equal(["Note", "Decision", "Bug", "Subtask", "Link document", "Link test", "Link commit"], items.Select(i => i.TextContent.Trim()));
        Assert.True(items[5].HasAttribute("disabled"));    // no test results file: the reason stays as the title
        Assert.False(items[6].HasAttribute("disabled"));

        items.Single(i => i.TextContent.Trim() == "Bug").Click();
        Assert.Equal(["bug"], done);
        nav.Find(".nav-worklog button.nav-add").Click();
        nav.FindAll("button[role=menuitem]").Single(i => i.TextContent.Trim() == "Subtask").Click();
        Assert.Equal(["bug", "subtask"], done);

        // no bug action wired: no Bug entry
        ui.CloseWorklog(ui.WorklogOpen!);
        ui.OpenWorklog(new WorklogActions(() => { }, () => { }, () => { }, () => { }, () => { }, () => { }));
        nav.WaitForAssertion(() => Assert.NotEmpty(nav.FindAll(".nav-worklog")));
        nav.Find(".nav-worklog button.nav-add").Click();
        Assert.DoesNotContain("Bug", nav.FindAll("button[role=menuitem]").Select(i => i.TextContent.Trim()));
    }

    [Fact(DisplayName = "With a work item filling the window the menu hides the projects, layout and new item rows, and shows them again in split view")]
    public async Task ItemPageHidesTheFirstSectionUnlessSplit()
    {
        await using BunitContext ctx = NewContext();
        SettingsStore settings = ctx.Services.GetRequiredService<SettingsStore>();
        NavigationManager location = ctx.Services.GetRequiredService<NavigationManager>();
        UiService ui = ctx.Services.GetRequiredService<UiService>();
        string[] first = ["button[aria-label='Open repositories']", "button[aria-label=Layout]", "button[aria-label='New work item']"];
        nav!.Find("button.nav-toggle").Click();
        Assert.All(first, q => Assert.NotEmpty(nav.FindAll(q)));   // the board has them

        settings.Update(s => s with { Split = false });
        location.NavigateTo("item/123");
        nav.WaitForAssertion(() => Assert.All(first, q => Assert.Empty(nav.FindAll(q))));
        Assert.NotEmpty(nav.FindAll("button.nav-toggle"));
        Assert.NotEmpty(nav.FindAll("button[aria-label=Settings]"));

        settings.Update(s => s with { Split = true });   // beside the list
        nav.WaitForAssertion(() => Assert.All(first, q => Assert.NotEmpty(nav.FindAll(q))));

        ui.SetNarrowWindow(true);   // too narrow for the split: the item fills the window again
        nav.WaitForAssertion(() => Assert.All(first, q => Assert.Empty(nav.FindAll(q))));
        ui.SetNarrowWindow(false);

        location.NavigateTo("");   // and back on the board
        nav.WaitForAssertion(() => Assert.All(first, q => Assert.NotEmpty(nav.FindAll(q))));
    }

    [Fact(DisplayName = "The expanded side menu has no row of its own for the collapse icon: it sits at the end of the first row, and the collapsed rail keeps it on top")]
    public async Task CollapseIconSitsInTheFirstRowWhenExpanded()
    {
        await using BunitContext ctx = NewContext();
        SettingsStore settings = ctx.Services.GetRequiredService<SettingsStore>();
        NavigationManager location = ctx.Services.GetRequiredService<NavigationManager>();

        // collapsed: the rail's first row, icon only, with its label and state
        IElement toggle = nav!.Find("button.nav-toggle");
        Assert.Empty(toggle.QuerySelectorAll(".nav-l"));
        Assert.DoesNotContain("openspec", toggle.TextContent);
        Assert.Equal("Menu", toggle.GetAttribute("aria-label"));
        Assert.Equal("Expand menu", toggle.GetAttribute("title"));
        Assert.Equal("false", toggle.GetAttribute("aria-expanded"));
        Assert.Single(nav.FindAll("nav.side > button.nav-toggle:first-child"));

        // expanded on the board: in the row of the "N open" button, after it, and no longer a row of its own
        toggle.Click();
        toggle = nav.Find("button.nav-toggle");
        Assert.Equal("Collapse menu", toggle.GetAttribute("title"));
        Assert.Equal("true", toggle.GetAttribute("aria-expanded"));
        Assert.Empty(nav.FindAll("nav.side > button.nav-toggle"));
        Assert.NotNull(toggle.ParentElement!.QuerySelector("button.tabs-btn"));
        Assert.Single(nav.FindAll(".nav-row > button.nav-toggle:last-child"));
        Assert.Single(nav.FindAll("nav.side > .nav-row:first-child"));   // that row is the menu's first
        toggle.Click();
        Assert.Single(nav.FindAll("nav.side > button.nav-toggle:first-child"));   // and back on top of the rail

        // an item filling the window: no first section, so the Worklog header's row has it
        nav.Find("button.nav-toggle").Click();
        settings.Update(s => s with { Split = false });
        location.NavigateTo("item/123");
        ctx.Services.GetRequiredService<UiService>().OpenWorklog(new WorklogActions(() => { }, () => { }, () => { }, () => { }, () => { }, () => { }));
        nav.WaitForAssertion(() => Assert.NotEmpty(nav.FindAll(".nav-worklog .nav-h button.nav-toggle")));
        Assert.Single(nav.FindAll("button.nav-toggle"));
        Assert.Equal("Worklog", nav.Find(".nav-worklog .nav-h").TextContent.Trim());
        Assert.Single(nav.FindAll(".nav-worklog > .nav-h:first-child > button.nav-toggle"));
        nav.Find("button.nav-toggle").Click();   // it still collapses
        nav.WaitForAssertion(() => Assert.Single(nav.FindAll("nav.side > button.nav-toggle:first-child")));
    }
    // ---- hook-badge chunk ----

    private static readonly DateTime AgentWrote = new(2026, 10, 30, 9, 0, 0);

    private static FakeTimeProvider ClockAt(TimeSpan after) => new(new DateTimeOffset(AgentWrote + after, TimeSpan.Zero));

    [Fact(DisplayName = "A card whose agent wrote three minutes ago says so, and the badge disappears after a day or once reviewed")]
    public async Task CardCarriesTheAgentBadge()
    {
        FakeTimeProvider time = ClockAt(TimeSpan.FromMinutes(3));
        await using BunitContext ctx = NewContext(time);
        WorkItem item = WorkItemsApi.Get(123)! with { AgentBy = "Claude", AgentAt = AgentWrote, ReviewedAt = null };
        IRenderedComponent<WorkItemCard> Card(WorkItem i) => ctx.Render<WorkItemCard>(p => p.Add(c => c.Item, i).Add(c => c.Options, new BoardOptions()));

        Assert.Equal("Claude, 3 min ago", Card(item).Find(".agent-badge").TextContent.Trim());
        time.Advance(TimeSpan.FromMinutes(117));
        Assert.Equal("Claude, 2 h ago", Card(item).Find(".agent-badge").TextContent.Trim());
        Assert.Empty(Card(item with { ReviewedAt = AgentWrote.AddMinutes(30) }).FindAll(".agent-badge"));   // the user has looked at it since
        Assert.Empty(Card(item with { AgentBy = null, AgentAt = null }).FindAll(".agent-badge"));          // nobody stamped it
        time.Advance(TimeSpan.FromHours(23));
        Assert.Empty(Card(item).FindAll(".agent-badge"));   // a day on: the stamp is history, not "working now"
    }

    [Fact(DisplayName = "The list row carries the same agent badge")]
    public async Task ListRowCarriesTheAgentBadge()
    {
        EditingWorkItemsTests.StampAgent(repo);
        await using BunitContext ctx = NewContext(ClockAt(TimeSpan.FromMinutes(3)));
        IRenderedComponent<BoardPage> board = ctx.Render<BoardPage>();
        ShowList();

        board.WaitForAssertion(() => Assert.Equal("Claude, 3 min ago", board.Find("tr[data-id='123'] .agent-badge").TextContent.Trim()));
    }

    // Runs the repository's PreToolUse hook (.claude/hooks/deny-board-edits.js) on a sample tool call, as Claude Code does: JSON on stdin.
    private static string RunHook(string json, bool allowDirect = false)
    {
        string dir = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(dir, ".claude", "hooks", "deny-board-edits.js"))) dir = Path.GetDirectoryName(dir) ?? throw new FileNotFoundException("deny-board-edits.js");
        System.Diagnostics.ProcessStartInfo start = new("node") { RedirectStandardInput = true, RedirectStandardOutput = true, UseShellExecute = false, WorkingDirectory = dir };
        start.ArgumentList.Add(Path.Combine(dir, ".claude", "hooks", "deny-board-edits.js"));
        if (allowDirect) start.Environment["RTM_ALLOW_DIRECT_EDIT"] = "1"; else start.Environment.Remove("RTM_ALLOW_DIRECT_EDIT");
        using System.Diagnostics.Process hook = System.Diagnostics.Process.Start(start)!;
        hook.StandardInput.Write(json);
        hook.StandardInput.Close();
        string output = hook.StandardOutput.ReadToEnd();
        Assert.True(hook.WaitForExit(10_000));
        Assert.Equal(0, hook.ExitCode);
        return output;
    }

    [Fact(DisplayName = "The edit hook denies direct edits under openspec/changes unless RTM_ALLOW_DIRECT_EDIT is set")]
    public void EditHookDeniesDirectBoardEdits()
    {
        string inChange = """{"tool_name":"Edit","tool_input":{"file_path":"openspec/changes/some-item/proposal.md"}}""";
        string elsewhere = """{"tool_name":"Edit","tool_input":{"file_path":"TaskManager/Components/Pages/BoardPage.razor"}}""";
        string redirect = """{"tool_name":"Bash","tool_input":{"command":"echo done >> openspec/changes/some-item/tasks.md"}}""";
        string reading = """{"tool_name":"Bash","tool_input":{"command":"cat openspec/changes/some-item/tasks.md 2>&1 | head"}}""";

        string denied = RunHook(inChange);
        Assert.Contains("\"permissionDecision\":\"deny\"", denied);
        Assert.Contains("Board files are written through the board_* tools (repo-tasks MCP), not edited directly.", denied);
        Assert.Contains("deny", RunHook(redirect));
        Assert.Contains("deny", RunHook(inChange.Replace("Edit", "Write")));
        Assert.Equal("", RunHook(elsewhere));
        Assert.Equal("", RunHook(reading));
        Assert.Equal("", RunHook(inChange, allowDirect: true));   // the user's escape hatch
    }
}
