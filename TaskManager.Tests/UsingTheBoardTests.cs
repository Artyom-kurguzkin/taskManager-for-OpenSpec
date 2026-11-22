using AngleSharp.Dom;
using Notes;
using WorkItems.PublicDTOs;
using Radzen;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.Time.Testing;
using Microsoft.Extensions.DependencyInjection;
using TaskManager.Components.Pages;
using TaskManager.Components.Layout;
using TaskManager.Components.Shared;
using TaskManager.AppState;
using TaskManager.BackgroundJobs;
using TaskManager.Helpers;
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

    /// <param name="sectionsOpen">The menu's Organise, Filters and Hidden sections start open (a folded section draws nothing, #wi190); false: as a new user has them, folded.</param>
    private BunitContext NewContext(TimeProvider? time = null, bool sectionsOpen = true)
    {
        BunitContext ctx = new();
        if (time is not null) ctx.Services.AddSingleton(time);   // the clock of the pages: a test moves it by hand
        ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        ctx.Services.AddSingleton<UiService>();
        ctx.Services.AddRadzenComponents();   // Radzen components ask for its services (the markdown editor wants DialogService)
        SettingsStore settings = new(settingsFile);
        if (sectionsOpen) foreach (string section in new[] { "organise", "filters", "hidden" }) settings.ToggleMenuSection(section);
        ctx.Services.AddSingleton(settings);
        ctx.Services.AddSingleton<Workspace>();
        Assert.True(ctx.Services.GetRequiredService<Workspace>().Open(repo.Root).Success);
        overlays = ctx.Render<Overlays>();
        nav = ctx.Render<SideNav>();
        return ctx;
    }

    [Fact(DisplayName = "What the list is grouped by is not repeated, except the Status column: no Label column by label, no Features by bucket, no Contributors by contributor")]
    public async Task GroupedColumnNotRepeatedInList()
    {
        await using BunitContext ctx = NewContext();
        IRenderedComponent<BoardPage> board = ctx.Render<BoardPage>();
        ShowList();
        List<string> Heads() => board.FindAll("thead th .th-l").Select(h => h.TextContent.Trim()).ToList();
        board.Eventually(() => Assert.Contains("Status", Heads()));   // grouped by status, the column stays: it is what the organising operators work on
        Assert.Contains("Label", Heads());

        SettingsStore settings = ctx.Services.GetRequiredService<SettingsStore>();
        foreach ((string by, string column) in new[] { ("label", "Label"), ("feature", "Feature"), ("contributor", "Contributors") })
        {
            settings.SaveBoard(repo.Root, settings.Board(repo.Root) with { ListGroupBy = by });
            board.Eventually(() => Assert.DoesNotContain(column, Heads()));
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

    [Fact(DisplayName = "A card shows its title on a band coloured by status, its feature, its date and people, its subtask count, its coloured label tag, and no priority tag")]
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
        Assert.DoesNotContain("#wi", card.Find(".tags").TextContent);   // the id is for the list and the item page, not the card
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
        if (nav!.FindAll(".nav-view").Count == 0) nav.ClickWhenReady("button.nav-toggle");
        return nav.FindAll(".organise .field label").Select(l => l.TextContent.Trim()).ToList();
    }

    private string OrganiseValue(string field) =>
        nav!.FindAll(".organise .field").Single(f => f.QuerySelector("label")!.TextContent.Trim() == field).QuerySelector(".rz-dropdown-label")!.TextContent.Trim();

    private List<string> OrganiseChoices(string field)
    {
        AngleSharp.Dom.IElement Field() => nav!.FindAll(".organise .field").Single(f => f.QuerySelector("label")!.TextContent.Trim() == field);
        nav.Eventually(() => Field().QuerySelector(".dd")!.Click());
        nav!.Eventually(() => Assert.NotEmpty(Field().QuerySelectorAll(".rz-dropdown-item")));
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
        board.Eventually(() => Assert.Equal(expected, board.FindAll("tr[data-id]").Select(r => int.Parse(r.GetAttribute("data-id")!))));
        Assert.Empty(board.FindAll("tr[data-group]"));
        Assert.Contains("Status", board.FindAll("thead th .th-l").Select(h => h.TextContent.Trim()));

        // sorted another way, the same rows follow it
        SettingsStore settings = ctx.Services.GetRequiredService<SettingsStore>();
        settings.SaveBoard(repo.Root, settings.Board(repo.Root) with { SortBy = "id" });
        board.Eventually(() => Assert.Equal(expected.Order(), board.FindAll("tr[data-id]").Select(r => int.Parse(r.GetAttribute("data-id")!))));
        Assert.Empty(board.FindAll("tr[data-group]"));
    }

    [Fact(DisplayName = "Choosing a group in the list view groups the rows, a subgroup splits each group, a saved choice is kept, and the board's columns are not touched")]
    public async Task ChoosingListGroups()
    {
        await using BunitContext ctx = NewContext();
        IRenderedComponent<BoardPage> board = ctx.Render<BoardPage>();
        OpenFreshList();
        board.Eventually(() => Assert.NotEmpty(board.FindAll("tr[data-id]")));
        Assert.Empty(board.FindAll("tr[data-group]"));
        string status = WorkItemsApi.Get(123)!.Status;

        PickField("groups", "status");
        board.Eventually(() => Assert.Contains(status, board.FindAll("tr[data-group]").Select(g => g.GetAttribute("data-group"))));
        Assert.Contains("123", GroupRows(board, status).Select(r => r.GetAttribute("data-id")));
        Assert.Contains("Status", board.FindAll("thead th .th-l").Select(h => h.TextContent.Trim()));   // the group says it too, and the column stays

        PickField("subgroups", "priority");
        board.Eventually(() => Assert.NotEmpty(board.FindAll("tr[data-group^='swim:']")));

        PickField("subgroups", "none");
        PickField("groups", "none");
        board.Eventually(() => Assert.Empty(board.FindAll("tr[data-group]")));
        Assert.NotEmpty(board.FindAll("tr[data-id]"));

        PickField("groups", "label");
        board.Eventually(() => Assert.NotEmpty(board.FindAll("tr[data-group]")));
        SettingsStore reopened = new(settingsFile);
        BoardOptions saved = reopened.Board(repo.Root);
        Assert.Equal(("label", "none", "status", "none"), (saved.ListGroupBy, saved.ListSwimlane, saved.GroupBy, saved.Swimlane));   // the board's columns stay status

        // the board still has its status columns
        ctx.Services.GetRequiredService<SettingsStore>().SaveBoard(repo.Root, saved with { View = "board" });
        board.Eventually(() => Assert.Equal(7, board.FindAll("section.col").Count));
    }

    [Fact(DisplayName = "A list saved with groups before the list had its own keeps them when the app opens it")]
    public async Task SavedListKeepsItsGroups()
    {
        await using BunitContext ctx = NewContext();
        SettingsStore settings = ctx.Services.GetRequiredService<SettingsStore>();
        settings.SaveBoard(repo.Root, new BoardOptions(GroupBy: "status", View: "list"));
        IRenderedComponent<BoardPage> board = ctx.Render<BoardPage>();

        string status = WorkItemsApi.Get(123)!.Status;
        board.Eventually(() => Assert.Contains(status, board.FindAll("tr[data-group]").Select(g => g.GetAttribute("data-group"))));
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
        board.Eventually(() => Assert.Equal(3, RowsIn(board, "Planned").Count));
        List<string> before = RowsIn(board, "Planned");
        Assert.Empty(board.FindAll(".grip"));   // no handle: the script grabs the row itself
        string js = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "wwwroot", "js", "app.js"));
        Assert.DoesNotContain(".grip", js);
        Assert.DoesNotContain("HOLD", js);                       // no hold-to-lift any more: a selected row drags after a few pixels
        Assert.Contains("kind: \"row\"", js);                     // a press on a (selected) row is one

        // A render between finding a row and sending it the event drops the event's handler: find the row again until the event lands.
        board.Eventually(() => GroupRows(board, "Planned")[2].DragStart());
        await board.InvokeAsync(() => { });   // let the dispatcher drain, so the drop targets the current render tree
        board.Eventually(() => GroupRows(board, "Planned")[0].Drop());
        board.Eventually(() => Assert.Equal([before[2], before[0], before[1]], RowsIn(board, "Planned")));   // the grid reloads after a drop; slow under a full-suite load

        board.Eventually(() => GroupRows(board, "Planned")[0].DragStart());
        await board.InvokeAsync(() => { });
        board.Eventually(() => Group(board, "In Progress").Drop());   // the group row stands for the group's end
        board.Eventually(() => Assert.Equal([before[0], before[1]], RowsIn(board, "Planned")));
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
        board.Eventually(() => Assert.NotEmpty(board.FindAll("tr[data-id]")));

        // the row itself is what the script grabs: it carries the id and the key the script finds it by, and no handle column or grip
        foreach (AngleSharp.Dom.IElement row in board.FindAll("tr[data-id]"))
        {
            Assert.True(row.HasAttribute("data-row"));
            Assert.Empty(row.QuerySelectorAll(".grip"));
            Assert.False(RowMarkProbe.Selected(board, row));
        }
        board.Eventually(() => board.FindAll("tr[data-id]").First(r => r.TextContent.Contains("#wi123")).QuerySelector("td.num")!.Click(new MouseEventArgs { ShiftKey = true }));
        board.Eventually(() => Assert.True(RowMarkProbe.Selected(board, board.FindAll("tr[data-id]").First(r => r.TextContent.Contains("#wi123")))));   // what the script reads: the marks block names the row

        string js = AppJs();
        // text no longer decides and nothing is held: the selection does
        Assert.DoesNotContain("_overText", js);
        Assert.DoesNotContain("_pressOnText", js);
        Assert.DoesNotContain("_liftRow", js);
        string down = js[js.IndexOf("const tr = !(e.shiftKey", StringComparison.Ordinal)..];
        down = down[..down.IndexOf("if (e.target.closest(\"input, button", StringComparison.Ordinal)];
        Assert.Contains("_rowSelected(tr)", down);   // only a selected row is pressed as a row
        Assert.DoesNotContain("setTimeout", down);
        string move = js[js.IndexOf("document.addEventListener(\"pointermove\"", StringComparison.Ordinal)..];
        move = move[..move.IndexOf("const d = _drag;", StringComparison.Ordinal)];
        Assert.Contains("moved >= GRAB", move);
        Assert.Contains("_startDrag(e)", move);
        // every selected row goes along: list rows are named by the marks block (RowMarks), not by the board's "selected" class
        string start = JsFunction(js, "_startDrag");
        Assert.Contains("_selectedRows()", start);
        Assert.Contains("_rowSelected(p.el)", start);
        // a pill is no control here: a press on it that moves drags the selected row, a still click opens its dropdown
        Assert.DoesNotContain(".dd, .menu-wrap", JsFunction(js, "_rowAt"));
        Assert.Contains("button:not(.row-title):not(.pick)", JsFunction(js, "_rowAt"));   // the list's pills are buttons (ListPill, #wi179)
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
        board.Eventually(() => Assert.Equal(3, RowsIn(board, "Planned").Count));
        List<int> ids = GroupRows(board, "Planned").Select(r => int.Parse(r.GetAttribute("data-id")!)).ToList();

        board.Eventually(() => GroupRows(board, "Planned")[0].QuerySelector("td.num")!.Click(new MouseEventArgs { ShiftKey = true }));
        board.Eventually(() => Assert.True(RowMarkProbe.Selected(board, GroupRows(board, "Planned")[0])));
        await board.InvokeAsync(() => { });
        board.Eventually(() => GroupRows(board, "Planned")[2].QuerySelector("td.num")!.Click(new MouseEventArgs { ShiftKey = true }));
        board.Eventually(() => Assert.True(RowMarkProbe.Selected(board, GroupRows(board, "Planned")[2])));
        Assert.False(RowMarkProbe.Selected(board, GroupRows(board, "Planned")[1]));

        board.Eventually(() => GroupRows(board, "Planned")[2].DragStart());   // the press is on the last selected row; the first one goes with it
        await board.InvokeAsync(() => { });
        board.Eventually(() => Group(board, "Confirmed").Drop());

        List<int> Ids(string group) => GroupRows(board, group).Select(r => int.Parse(r.GetAttribute("data-id")!)).ToList();   // the new items share a title
        board.Eventually(() => Assert.Equal([ids[1]], Ids("Planned")));
        board.Eventually(() => Assert.Contains(ids[0], Ids("Confirmed")));
        List<int> confirmed = Ids("Confirmed");
        Assert.Contains(ids[2], confirmed);
        Assert.True(confirmed.IndexOf(ids[0]) < confirmed.IndexOf(ids[2]));   // in the order they had
        // the page shows the drop before it writes it, one card after the other on the thread pool: the files are checked once the last write is down
        board.Eventually(() => Assert.All(new[] { ids[0], ids[2] }, id => Assert.Equal("Confirmed", WorkItemsApi.GetAll().Single(i => i.Id == id).Status)));
        Assert.Equal("Planned", WorkItemsApi.GetAll().Single(i => i.Id == ids[1]).Status);
    }

    [Fact(DisplayName = "A press on an unselected list row selects cells and never starts a drag")]
    public async Task APressOnAnUnselectedListRowSelectsCells()
    {
        await using BunitContext ctx = NewContext();
        IRenderedComponent<BoardPage> board = ctx.Render<BoardPage>();
        ShowList();
        board.Eventually(() => Assert.NotEmpty(board.FindAll("tr[data-id]")));
        Assert.Empty(RowMarkProbe.SelectedKeys(board));   // nothing is selected: every press is a cell selection's

        string js = AppJs();
        // the row press is for selected rows only, so an unselected row's press never reaches _startDrag
        string down = js[js.IndexOf("const tr = !(e.shiftKey", StringComparison.Ordinal)..];
        down = down[..down.IndexOf("if (e.target.closest(\"input, button", StringComparison.Ordinal)];
        Assert.Contains("_rowSelected(tr)", down);
        // the cell selection starts from any press on an unselected row, over text or not, as the grid did before
        string cell = js[js.IndexOf("const cell = _cellAt(e.target);", StringComparison.Ordinal)..];
        cell = cell[..cell.IndexOf("_cells.dragging = true;", StringComparison.Ordinal)];
        Assert.DoesNotContain("_pressOnText", cell);
        Assert.Contains("_rowSelected(", cell);   // a selected row's press is the drag's: it ends a selection instead
    }

    [Fact(DisplayName = "Dropping a list row on another group moves the work item there")]
    public async Task DroppingAListRowOnAnotherGroupMovesIt()
    {
        await using BunitContext ctx = NewContext();
        Workspace workspace = ctx.Services.GetRequiredService<Workspace>();
        Assert.True(workspace.CreateItem("Planned").Ok);
        IRenderedComponent<BoardPage> board = ctx.Render<BoardPage>();
        ShowList();
        board.Eventually(() => Assert.Equal(2, RowsIn(board, "Planned").Count));
        List<string> before = RowsIn(board, "Planned");

        board.Eventually(() => GroupRows(board, "Planned")[1].DragStart());
        await board.InvokeAsync(() => { });
        board.Eventually(() => Group(board, "Confirmed").Drop());
        board.Eventually(() => Assert.Equal([before[0]], RowsIn(board, "Planned")));
        board.Eventually(() => Assert.Contains(before[1], RowsIn(board, "Confirmed")));
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
        board.Eventually(() => Assert.Equal(2, RowsIn(board, "Planned").Count));
        List<string> before = RowsIn(board, "Planned");

        board.Eventually(() => GroupRows(board, "Planned")[1].DragStart());
        await board.InvokeAsync(() => { });
        board.Eventually(() => GroupRows(board, "Planned")[0].Drop());   // dropped on a row: in front of it
        board.Eventually(() => Assert.Equal([before[1], before[0]], RowsIn(board, "Planned")));
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
        board.Eventually(() => Assert.NotEmpty(board.FindAll($"tr[data-id='{bare}']")));

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
        board.Eventually(() => Assert.NotEmpty(board.FindAll("tr[data-group='swim:bug']")));

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
        board.Eventually(() => Assert.NotEmpty(board.FindAll("thead th .th-hide")));
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
        board.Eventually(() => Assert.Equal(2, RowsIn(board, "Planned").Count));
        List<string> before = RowsIn(board, "Planned");
        board.Eventually(() => GroupRows(board, "Planned")[1].QuerySelector("td.num")!.Click(new MouseEventArgs { ShiftKey = true }));   // selected as on the board: no tick boxes

        board.KeyDownWhenReady(".tbl-wrap", "ArrowUp");

        board.Eventually(() => Assert.Equal([before[1], before[0]], RowsIn(board, "Planned")));
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

        board.Eventually(() => row.QuerySelector("td.num")!.Click(new MouseEventArgs { ShiftKey = true }));
        board.Eventually(() => Assert.True(RowMarkProbe.Selected(board, board.FindAll("tr[data-id]").First(r => r.TextContent.Contains("#wi123")))));
        Assert.Contains("1 selected", board.Find(".actionbar:not(.select-hint)").TextContent);
        Assert.Empty(board.FindAll("tr[data-id] input[type=checkbox]"));   // no tick boxes, as on the board
        board.Eventually(() => board.FindAll("tr[data-id]").First(r => r.TextContent.Contains("#wi123")).QuerySelector("td.num")!.Click(new MouseEventArgs { CtrlKey = true }));
        board.Eventually(() => Assert.Contains("Shift-click rows", board.Find(".select-hint").TextContent));   // the Shift hint is there in the list too

        board.Eventually(() => board.FindAll("tr[data-id]").First(r => r.TextContent.Contains("#wi123")).QuerySelector(".row-title")!.Click(new MouseEventArgs { ShiftKey = true }));
        board.Eventually(() => Assert.True(RowMarkProbe.Selected(board, board.FindAll("tr[data-id]").First(r => r.TextContent.Contains("#wi123")))));   // the title button leaves a modifier click to the row, which selects it again (the one before had let go): not opened
        Assert.DoesNotContain("/item/", ctx.Services.GetRequiredService<NavigationManager>().Uri);

        board.Eventually(() => board.FindAll("tr[data-id]").First(r => r.TextContent.Contains("#wi123")).QuerySelector(".row-title")!.Click());
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

        overlays!.Eventually(() => Assert.Contains("second-check is now #wi126", overlays.Find(".toast").TextContent));
        Assert.Contains(Notes.NotesApi.GetForWorkItem(126), n => n.Title.Contains("#wi123"));
        board.Eventually(() => Assert.Empty(board.FindAll(".warning")));
    }

    [Fact(DisplayName = "List cells wear their colours: status, label and priority are filled pills, and a status group's name wears its band")]
    public async Task ListColours()
    {
        await using BunitContext ctx = NewContext();
        IRenderedComponent<BoardPage> board = ctx.Render<BoardPage>();
        ShowList();

        AngleSharp.Dom.IElement row = board.FindAll("tr[data-id]").First(r => r.TextContent.Contains("#wi123"));
        Assert.Equal(3, row.QuerySelectorAll(".cell.filled[style*='--fill']").Length);   // status, label and priority: the status stays beside the group that names it
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

    [Fact(DisplayName = "Hidden work items leave the board until 'show all hidden' is pressed, which brings them back and draws nothing dashed")]
    public async Task HiddenItems()
    {
        await using BunitContext ctx = NewContext();
        WorkItemsApi.Hide(107);
        ctx.Services.GetRequiredService<Workspace>().Notify();
        IRenderedComponent<BoardPage> board = ctx.Render<BoardPage>();
        Assert.Equal(3, board.FindAll("article.card").Count);

        nav.ClickWhenReady("button.nav-toggle");   // the expanded side menu holds the view options
        nav.ClickWhenReady("button#f-hidden");

        board.Eventually(() => Assert.Equal(4, board.FindAll("article.card").Count));
        Assert.Empty(board.FindAll("article.card.hidden-item"));
    }

    [Fact(DisplayName = "The collapsed side menu has a compact-cards button that toggles it and shows when it's on")]
    public async Task CompactFromRail()
    {
        await using BunitContext ctx = NewContext();
        IRenderedComponent<BoardPage> board = ctx.Render<BoardPage>();
        Assert.Empty(board.FindAll("article.card.collapsed"));

        nav.ClickWhenReady("button.compact-btn");

        Assert.Equal(board.FindAll("article.card").Count, board.FindAll("article.card.collapsed").Count);
        Assert.Contains("is-sel", nav.Find("button.compact-btn").ClassList);

        nav.ClickWhenReady("button.compact-btn");
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

        nav.ClickWhenReady("button.nav-toggle");
        PickField("columns", "feature");

        board.Eventually(() => Assert.Contains("Banking", Columns(board)));
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
        board.Eventually(() => Assert.Empty(board.FindAll(".subrow")));   // folded by default: open one row by hand, only its subtasks show
        board.Eventually(() => board.FindAll("tr[data-id]").First(r => r.TextContent.Contains("#wi123")).QuerySelector("button.chev")!.Click());

        board.Eventually(() => Assert.Equal(["1.1", "1.2", "1.3", "2.1"], board.FindAll(".subrow .num").Select(td => td.TextContent.Trim())));
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
        board.Eventually(() => Assert.Contains(board.FindAll("tr[data-id]"), r => r.TextContent.Contains("#wi124") && r.TextContent.Contains("Completed")));
        AngleSharp.Dom.IElement row = board.FindAll("tr[data-id]").First(r => r.TextContent.Contains("#wi124"));
        Assert.NotNull(row.QuerySelector("button[data-pill=priority]"));   // priority pill, opens the list's picker
        Assert.NotNull(row.QuerySelector("span.box.cell.filled"));   // status: read-only
        Assert.Null(row.QuerySelector("button[data-pill=status]"));
    }

    [Fact(DisplayName = "In the list, subtasks start folded away; the rail's subtasks button, selected, shows every row's subtasks, and unselected folds them again")]
    public async Task CompactHidesOnlySubtasks()
    {
        await using BunitContext ctx = NewContext();
        IRenderedComponent<BoardPage> board = ctx.Render<BoardPage>();
        ShowList();
        Assert.Equal(4, board.FindAll("tr[data-id]").Count);
        board.Eventually(() => Assert.Empty(board.FindAll(".subrow")));
        Assert.Equal("false", nav!.Find("button.compact-btn").GetAttribute("aria-pressed"));

        nav.ClickWhenReady("button.compact-btn");
        board.Eventually(() => Assert.Equal(4 + 2 + 1 + 1, board.FindAll(".subrow").Count));
        Assert.Equal("true", nav.Find("button.compact-btn").GetAttribute("aria-pressed"));
        Assert.Contains("is-sel", nav.Find("button.compact-btn").ClassList);
        Assert.Equal(4, board.FindAll("tr[data-id]").Count);   // the work items stay; only their subtasks unfold

        nav.ClickWhenReady("button.compact-btn");
        board.Eventually(() => Assert.Empty(board.FindAll(".subrow")));
    }

    [Fact(DisplayName = "A grid column is hidden from the × in its header and listed under Hidden in the side menu, where the eye brings it back")]
    public async Task HideGridColumn()
    {
        await using BunitContext ctx = NewContext();
        IRenderedComponent<BoardPage> board = ctx.Render<BoardPage>();
        ShowList();
        Assert.Contains(board.FindAll("thead th"), h => h.TextContent.Contains("Progress"));

        board.ClickWhenReady("button[aria-label='Hide Progress column']");

        board.Eventually(() => Assert.DoesNotContain(board.FindAll("thead th"), h => h.TextContent.Contains("Progress")));
        Assert.Contains("col:Progress", ctx.Services.GetRequiredService<SettingsStore>().Board(repo.Root).Hidden!);
        nav.ClickWhenReady("button.nav-toggle");
        Assert.Contains("Progress", nav.Find(".nav-hidden").TextContent);
        nav.ClickWhenReady("button[aria-label='Show Progress']");
        board.Eventually(() => Assert.Contains(board.FindAll("thead th"), h => h.TextContent.Contains("Progress")));
        Assert.Empty(board.FindAll(".rz-column-picker"));   // no toolbar row
    }

    [Fact(DisplayName = "Merging work items confirms it with a toast")]
    public async Task MergeToast()
    {
        await using BunitContext ctx = NewContext();
        IRenderedComponent<BoardPage> board = ctx.Render<BoardPage>();
        for (int i = 0; i < 2; i++) board.FireWhenReady("article.card", e => e.Click(new MouseEventArgs { CtrlKey = true }), all => all[i]);   // re-found: each click re-renders

        board.ClickWhenReady(".actionbar button.primary");
        overlays!.Eventually(() => Assert.Equal(2, overlays.FindAll(".dialog .choice").Count));   // asks which one is the main item
        overlays.FireWhenReady(".dialog .choice", e => e.Click(), all => all[0]);

        overlays.Eventually(() => Assert.Contains("Merged into #wi", overlays.Find(".toast").TextContent));
    }

    [Fact(DisplayName = "Hiding selected work items removes them from the board and clears the selection")]
    public async Task HideSelectedCards()
    {
        await using BunitContext ctx = NewContext();
        IRenderedComponent<BoardPage> board = ctx.Render<BoardPage>();
        int before = board.FindAll("article.card").Count;
        board.FireWhenReady("article.card", e => e.Click(new MouseEventArgs { ShiftKey = true }));

        board.ClickWhenReady(".actionbar button.hide-sel");

        Assert.Empty(board.FindAll(".actionbar:not(.select-hint)"));   // only the Shift hint bar is left
        Assert.Equal(before - 1, board.FindAll("article.card").Count);
    }

    [Fact(DisplayName = "Clicking a column header opens its menu")]
    public async Task ColumnMenuOpens()
    {
        await using BunitContext ctx = NewContext();
        IRenderedComponent<BoardPage> board = ctx.Render<BoardPage>();

        board.ClickWhenReady("header.col-h", "Blocked");

        board.Eventually(() => Assert.Contains(board.FindAll(".menu-item"), i => i.TextContent.Contains("Hide column")));
    }

    [Fact(DisplayName = "Showing subtasks puts one card per tasks.md checkbox on the board")]
    public async Task SubtaskCards()
    {
        await using BunitContext ctx = NewContext();
        IRenderedComponent<BoardPage> board = ctx.Render<BoardPage>();
        nav.ClickWhenReady("button.nav-toggle");
        ctx.Services.GetRequiredService<SettingsStore>().SaveBoard(repo.Root, ctx.Services.GetRequiredService<SettingsStore>().Board(repo.Root) with { Unit = "subtasks" });   // the switch lives in the examine panel now (tested below)
        board.Eventually(() => Assert.Equal(4 + 2 + 1 + 1, board.FindAll("article.subcard").Count));
    }

    [Fact(DisplayName = "The menu button expands the side menu to labels and view options, and collapses it again")]
    public async Task SideMenuExpands()
    {
        await using BunitContext ctx = NewContext();
        Assert.DoesNotContain("expanded", nav!.Find("nav.side").ClassName);
        Assert.Empty(nav.FindAll(".nav-view"));

        nav.ClickWhenReady("button.nav-toggle");
        Assert.Contains("expanded", nav.Find("nav.side").ClassName);
        Assert.Contains("Organise", nav.Find(".nav-view").TextContent);
        Assert.Contains("contributor", nav.Find(".nav-view").TextContent);

        nav.ClickWhenReady("button.nav-toggle");
        Assert.Empty(nav.FindAll(".nav-view"));

        nav.ClickWhenReady("button[aria-label='View options']");   // the view icon opens the same expanded menu
        Assert.NotEmpty(nav.FindAll(".nav-view"));
    }

    [Fact(DisplayName = "+ in the side menu creates a work item and opens it")]
    public async Task SideMenuNewItem()
    {
        await using BunitContext ctx = NewContext();

        nav.ClickWhenReady("button[aria-label='New work item']");

        overlays!.Eventually(() => Assert.Contains("Created #wi", overlays.Find(".toast").TextContent));
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
        nav.ClickWhenReady("button.nav-toggle");
        Assert.Empty(nav.FindAll(".nav-hidden .grp-row"));   // nothing hidden, nothing listed under the Hidden header

        board.ClickWhenReady("header.col-h", "Blocked");
        board.FireWhenReady(".menu-item", e => e.Click(), all => all.Single(x => x.TextContent.Trim() == "Hide column"));

        Assert.DoesNotContain("Blocked", Columns(board));
        Assert.Equal("0", nav.Find(".nav-hidden .grp-row .count").TextContent.Trim());   // how many items it holds
        nav.ClickWhenReady("button[aria-label='Show Blocked']");
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
        for (int i = 0; i < moving.Count; i++) board.Eventually(() => Col(board, "Backlog").QuerySelectorAll("article.card")[i].Click(new MouseEventArgs { CtrlKey = true }));

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

        Col(board, "Security").QuerySelectorAll("article.card").First(c => c.GetAttribute("data-id") == "124").DragStart();
        Col(board, "Availability").Drop();

        board.Eventually(() => Assert.Contains(timeout, CardsIn(board, "Availability")));
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

        Col(board, "Security").QuerySelectorAll("article.card").First(c => c.GetAttribute("data-id") == "123").DragStart();
        Col(board, "Banking").Drop();

        board.Eventually(() => Assert.Contains(health, CardsIn(board, "Banking")));
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

        nav.ClickWhenReady("button.nav-toggle");
        PickField("columns", "due date");
        board.Eventually(() => Assert.Equal(["Overdue", "Today", "This week", "Next week", "Later", "No due date"], Columns(board)));
        Assert.Contains("Health-check endpoint", CardsIn(board, "Today"));
        Assert.Contains(WorkItemsApi.Get(107)!.Title, CardsIn(board, "Overdue"));
        Assert.Single(board.FindAll("tr[data-id], article.card").Where(c => c.TextContent.Contains("Health-check")));

        nav.Eventually(() => nav.FindAll(".field").Single(f => f.TextContent.Contains("period")).QuerySelector(".dd")!.Click());
        nav.FireWhenReady(".rz-dropdown-item", e => e.Click(), all => all.First(x => x.TextContent.Trim() == "overdue"));
        board.Eventually(() => Assert.Single(board.FindAll("article.card")));
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
        Assert.Equal(TaskManager.Helpers.Ui.Period(today.AddDays(-5), today.AddDays(-1)), overdue.TextContent.Trim());   // started and due are one period
        AngleSharp.Dom.IElement soon = board.FindAll("article.card").First(c => c.GetAttribute("data-id") == "107").QuerySelector(".tag.due")!;
        Assert.DoesNotContain("overdue", soon.ClassList);
        Assert.Empty(board.FindAll("article.card").Where(c => c.GetAttribute("data-id") == "125").Select(c => c.QuerySelector(".tag.due")).Where(t => t is not null));

        ShowList();
        board.Eventually(() => Assert.Contains(board.FindAll("thead th"), h => h.TextContent.Contains("Due")));
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

        board.FireWhenReady("article.card", e => e.Click(new MouseEventArgs { ShiftKey = true }), all => all[0]);
        Assert.Single(board.FindAll("article.card.selected"));
        Assert.Equal(home, nav.Uri);
        Assert.Empty(board.FindAll("article.card input"));   // no tick boxes

        board.FireWhenReady("article.card", e => e.Click(), all => all[1]);   // a plain click opens, even while something is selected
        Assert.Matches(@"/item/\d+$", nav.Uri);
    }

    [Fact(DisplayName = "Shift-click (or Ctrl/⌘-click) adds cards to the selection or removes them")]
    public async Task ClickToSelectSeveral()
    {
        await using BunitContext ctx = NewContext();
        IRenderedComponent<BoardPage> board = ctx.Render<BoardPage>();

        board.FireWhenReady("article.card", e => e.Click(new MouseEventArgs { ShiftKey = true }), all => all[1]);
        Assert.Equal([1], Selected(board));

        board.FireWhenReady("article.card", e => e.Click(new MouseEventArgs { MetaKey = true }), all => all[0]);
        Assert.Equal([0, 1], Selected(board));

        board.FireWhenReady("article.card", e => e.Click(new MouseEventArgs { ShiftKey = true }), all => all[1]);
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
        board.Eventually(() => Col(board, "Planned").QuerySelectorAll("article.card")[2].Click(new MouseEventArgs { ShiftKey = true }));

        board.KeyDownWhenReady(".board-wrap", "ArrowUp");
        board.Eventually(() => Assert.Equal([before[0], before[2], before[1]], CardsIn(board, "Planned")));   // a key's handler can still be running when the call returns on a busy machine

        board.KeyDownWhenReady(".board-wrap", "ArrowRight");   // Confirmed is empty, so it lands there as the only card
        board.Eventually(() => Assert.Equal([before[0], before[1]], CardsIn(board, "Planned")));
        board.Eventually(() => Assert.Equal([before[2]], CardsIn(board, "Confirmed")));
        board.Eventually(() => Assert.Equal("Confirmed", WorkItemsApi.GetAll().Single(i => i.Title == before[2]).Status));   // the frame goes out first, the write follows

        board.KeyDownWhenReady(".board-wrap", "ArrowLeft");   // back at row 0: the top of Planned
        board.Eventually(() => Assert.Equal([before[2], before[0], before[1]], CardsIn(board, "Planned")));
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

        board.Eventually(() => Col(board, "Planned").QuerySelectorAll("article.card")[0].Click(new MouseEventArgs { ShiftKey = true }));
        board.Eventually(() => Col(board, "In Progress").QuerySelectorAll("article.card")[0].Click(new MouseEventArgs { CtrlKey = true }));
        board.KeyDownWhenReady(".board-wrap", "ArrowDown");
        overlays!.Eventually(() => Assert.Contains("one column", overlays.Find(".toast").TextContent));
        Assert.Equal(before, CardsIn(board, "Planned"));

        board.Eventually(() => Col(board, "In Progress").QuerySelectorAll("article.card")[0].Click(new MouseEventArgs { ShiftKey = true }));   // toggles it off: only Planned 0 stays
        board.Eventually(() => Col(board, "Planned").QuerySelectorAll("article.card")[2].Click(new MouseEventArgs { CtrlKey = true }));
        board.KeyDownWhenReady(".board-wrap", "ArrowDown");
        Assert.Equal([before[1], before[0], before[2]], CardsIn(board, "Planned"));   // gathered at the bottom card
    }

    [Fact(DisplayName = "With nothing selected the board carries a select-mode hint bar for the Shift key; a selection replaces it")]
    public async Task SelectModeHint()
    {
        await using BunitContext ctx = NewContext();
        IRenderedComponent<BoardPage> board = ctx.Render<BoardPage>();

        Assert.Contains("Select mode", board.Find(".actionbar.select-hint").TextContent);
        Assert.Contains("body.shift-held .select-hint", File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "wwwroot", "css", "app.css")));   // shown only while Shift is held

        board.FireWhenReady("article.card", e => e.Click(new MouseEventArgs { ShiftKey = true }), all => all[0]);
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
        board.ClickWhenReady("header.col-h", "Payments");
        board.Eventually(() => Assert.Contains(board.FindAll(".menu-item"), i => i.TextContent.Trim() == "Delete feature…"));   // the menu opens in the next render
        board.FireWhenReady(".menu-item", e => e.Click(), all => all.Single(x => x.TextContent.Trim() == "Delete feature…"));   // find and click in one dispatcher turn; ClickAsync would wait for the handler, which waits for the dialog
        overlays!.Eventually(() => Assert.Contains("Delete Payments?", overlays.Find(".dialog").TextContent));
        overlays.FireWhenReady(".dialog button", e => e.Click(), all => all.Single(x => x.TextContent.Trim() == "Delete"));

        board.Eventually(() => Assert.DoesNotContain("Payments", Columns(board)));
        Assert.True(HasCard(board.FindAll("section.col").Single(c => c.QuerySelector("h3")!.TextContent == "Unassigned"), id));
    }

    [Fact(DisplayName = "A feature with a spec folder can be deleted too: the dialog says its folder goes, and the column leaves the board")]
    public async Task DeleteSpecFeature()
    {
        await using BunitContext ctx = NewContext();
        repo.Write("openspec/specs/functional/ledger/spec.md", "# ledger Specification\n\n## Purpose\nBooks.\n\n## Requirements\n\n### Requirement: Ledger\nThe ledger SHALL balance.\n\n#### Scenario: Balanced\n- **WHEN** posted\n- **THEN** it balances\n");
        IRenderedComponent<BoardPage> board = ctx.Render<BoardPage>();
        GroupByFeature();
        board.Eventually(() => Assert.Contains("Ledger", Columns(board)));

        board.ClickWhenReady("header.col-h", "Ledger");
        board.Eventually(() => Assert.Contains(board.FindAll(".menu-item"), i => i.TextContent.Trim() == "Delete feature…"));   // the menu opens in the next render
        board.FireWhenReady(".menu-item", e => e.Click(), all => all.Single(x => x.TextContent.Trim() == "Delete feature…"));   // find and click in one dispatcher turn; ClickAsync would wait for the handler, which waits for the dialog
        overlays!.Eventually(() => Assert.Contains("openspec/specs/functional/ledger", overlays.Find(".dialog").TextContent));
        overlays.FireWhenReady(".dialog button", e => e.Click(), all => all.Single(x => x.TextContent.Trim() == "Delete"));

        board.Eventually(() => Assert.DoesNotContain("Ledger", Columns(board)));
        Assert.False(Directory.Exists(repo.PathOf("openspec/specs/functional/ledger")));
    }

    [Fact(DisplayName = "Deleting a feature that an open item still specs says which item in the dialog, then removes the column")]
    public async Task DeleteFeatureSpeccedByOpenItem()
    {
        await using BunitContext ctx = NewContext();
        IRenderedComponent<BoardPage> board = ctx.Render<BoardPage>();
        GroupByFeature();
        board.Eventually(() => Assert.Contains("Availability", Columns(board)));

        board.ClickWhenReady("header.col-h", "Availability");
        board.Eventually(() => Assert.Contains(board.FindAll(".menu-item"), i => i.TextContent.Trim() == "Delete feature…"));   // the menu opens in the next render
        board.FireWhenReady(".menu-item", e => e.Click(), all => all.Single(x => x.TextContent.Trim() == "Delete feature…"));   // find and click in one dispatcher turn; ClickAsync would wait for the handler, which waits for the dialog
        overlays!.Eventually(() => Assert.Contains("#wi123 still describes it in its specs", overlays.Find(".dialog").TextContent));
        overlays.FireWhenReady(".dialog button", e => e.Click(), all => all.Single(x => x.TextContent.Trim() == "Delete"));

        board.Eventually(() => Assert.DoesNotContain("Availability", Columns(board)));
    }

    // ── deleting a status or a feature where it is seen: the board's column header and the list's group header (#wi207) ──

    private async Task ChooseInMenu(IRenderedComponent<BoardPage> board, string item)
    {
        board.Eventually(() => Assert.Contains(board.FindAll(".menu-item"), i => i.TextContent.Trim() == item));   // the menu opens in the next render
        board.FireWhenReady(".menu-item", e => e.Click(), all => all.Single(i => i.TextContent.Trim() == item));   // find and click in one dispatcher turn; ClickAsync would wait for the handler, which waits for the dialog
    }

    [Fact(DisplayName = "A status column's menu offers Delete status… for a custom status and not for Backlog; confirming moves its items to the first status and removes it from the project's config")]
    public async Task DeleteStatusFromTheBoardColumn()
    {
        await using BunitContext ctx = NewContext();
        Assert.True(WorkItemsApi.AddStatus("Review ready").Success);
        Assert.True(WorkItemsApi.SetStatus(107, "Review ready").Success);
        IRenderedComponent<BoardPage> board = ctx.Render<BoardPage>();
        board.Eventually(() => Assert.Contains("Review ready", Columns(board)));

        board.Eventually(() => board.FindAll("header.col-h").First(h => h.QuerySelector("h3")!.TextContent == "Backlog").Click());
        board.Eventually(() => Assert.Contains(board.FindAll(".menu-item"), i => i.TextContent.Trim() == "Select all in column"));
        Assert.DoesNotContain(board.FindAll(".menu-item"), i => i.TextContent.Contains("Delete status"));   // a default status has no delete
        board.ClickWhenReady(".menu-backdrop");

        board.Eventually(() => board.FindAll("header.col-h").First(h => h.QuerySelector("h3")!.TextContent == "Review ready").Click());
        await ChooseInMenu(board, "Delete status…");
        overlays!.Eventually(() => Assert.Contains("Remove Review ready?", overlays.Find(".dialog").TextContent));
        Assert.Contains("1 work item has this status and moves to Backlog", overlays.Find(".dialog").TextContent);   // the same words as Settings
        overlays.FireWhenReady(".dialog button", e => e.Click(), all => all.Single(x => x.TextContent.Trim() == "Remove"));

        board.Eventually(() => Assert.DoesNotContain("Review ready", Columns(board)));
        Assert.True(HasCard(Col(board, "Backlog"), 107));
        Assert.DoesNotContain("Review ready", repo.Read("openspec/config.yaml"));
    }

    [Fact(DisplayName = "Cancelling the Delete status… confirm from a column menu leaves the status and its items alone")]
    public async Task CancelDeleteStatusFromTheBoardColumn()
    {
        await using BunitContext ctx = NewContext();
        IRenderedComponent<BoardPage> board = ctx.Render<BoardPage>();
        board.Eventually(() => Assert.Contains("Planned", Columns(board)));

        board.Eventually(() => board.FindAll("header.col-h").First(h => h.QuerySelector("h3")!.TextContent == "Planned").Click());
        await ChooseInMenu(board, "Delete status…");
        overlays!.Eventually(() => Assert.Contains("Remove Planned?", overlays.Find(".dialog").TextContent));
        overlays.FireWhenReady(".dialog button", e => e.Click(), all => all.Single(x => x.TextContent.Trim() == "Cancel"));

        overlays.Eventually(() => Assert.Empty(overlays.FindAll(".dialog")));
        Assert.Contains("Planned", Columns(board));
        Assert.True(HasCard(Col(board, "Planned"), 107));
    }

    [Fact(DisplayName = "The list's group header offers Delete status… for a custom status, and a default status offers none")]
    public async Task DeleteStatusFromTheListGroupHeader()
    {
        await using BunitContext ctx = NewContext();
        Assert.True(WorkItemsApi.AddStatus("Review ready").Success);
        Assert.True(WorkItemsApi.SetStatus(107, "Review ready").Success);
        IRenderedComponent<BoardPage> board = ctx.Render<BoardPage>();
        ShowList();
        board.Eventually(() => Assert.NotEmpty(board.FindAll("tr[data-group='Review ready']")));
        Assert.Empty(board.FindAll("tr[data-group='Backlog'] button.gh-menu"));
        Assert.Empty(board.FindAll("tr[data-group='Completed'] button.gh-menu"));

        board.ClickWhenReady("tr[data-group='Review ready'] button.gh-menu");
        await ChooseInMenu(board, "Delete status…");
        overlays!.Eventually(() => Assert.Contains("Remove Review ready?", overlays.Find(".dialog").TextContent));
        overlays.FireWhenReady(".dialog button", e => e.Click(), all => all.Single(x => x.TextContent.Trim() == "Remove"));

        board.Eventually(() => Assert.Empty(board.FindAll("tr[data-group='Review ready']")));
        Assert.Contains("107", GroupRows(board, "Backlog").Select(r => r.GetAttribute("data-id")));
        Assert.DoesNotContain("Review ready", repo.Read("openspec/config.yaml"));
    }

    [Fact(DisplayName = "In the list grouped by feature a feature's group header offers Delete feature…, which asks first and moves its open items to Unassigned; Unassigned offers none")]
    public async Task DeleteFeatureFromTheListGroupHeader()
    {
        await using BunitContext ctx = NewContext();
        Features.FeaturesApi.CreateFeature("Functional", "Payments");
        (bool _, string _, int id) = WorkItemsApi.Create("Pay run", features: ["functional/payments"]);
        IRenderedComponent<BoardPage> board = ctx.Render<BoardPage>();
        GroupByFeature();
        ShowList();
        board.Eventually(() => Assert.NotEmpty(board.FindAll("tr[data-group='functional/payments']")));
        Assert.Empty(board.FindAll($"tr[data-group='{BoardModel.Unassigned}'] button.gh-menu"));

        board.ClickWhenReady("tr[data-group='functional/payments'] button.gh-menu");
        await ChooseInMenu(board, "Delete feature…");
        overlays!.Eventually(() => Assert.Contains("Delete Payments?", overlays.Find(".dialog").TextContent));
        overlays.FireWhenReady(".dialog button", e => e.Click(), all => all.Single(x => x.TextContent.Trim() == "Delete"));

        board.Eventually(() => Assert.Empty(board.FindAll("tr[data-group='functional/payments']")));
        Assert.Contains(id.ToString(), GroupRows(board, BoardModel.Unassigned).Select(r => r.GetAttribute("data-id")));
    }

    [Fact(DisplayName = "With two projects open Delete status… on the shared header asks which project, and only that project loses the status")]
    public async Task DeleteStatusInTheProjectItBelongsTo()
    {
        await using BunitContext ctx = NewContext();
        TwoProjects(ctx);
        IRenderedComponent<BoardPage> board = ctx.Render<BoardPage>();
        board.Eventually(() => Assert.Contains("Planned", Heads(board)));

        board.Eventually(() => HeadOf(board, "Planned").Click());
        await ChooseInMenu(board, "Delete status…");
        overlays!.Eventually(() => Assert.Contains(NameOf(repo2), overlays.Find(".dialog").TextContent));   // both projects have Planned: pick one
        overlays.Eventually(() => overlays.FindAll(".dialog .choice").Single(b => b.TextContent.Trim() == NameOf(repo2)).Click());
        overlays.Eventually(() => Assert.Contains("Remove Planned?", overlays.Find(".dialog").TextContent));
        overlays.FireWhenReady(".dialog button", e => e.Click(), all => all.Single(x => x.TextContent.Trim() == "Remove"));

        board.Eventually(() => Assert.Contains("statuses:", repo2.Read("openspec/config.yaml")));   // its list is no longer the built-in one
        Assert.DoesNotContain("Planned", repo2.Read("openspec/config.yaml").Split("statuses:")[^1].Split('\n')[0]);
        Assert.Contains("Planned", WorkItemsApi.GetStatuses());   // the first project keeps it
        Assert.DoesNotContain("statuses:", repo.Read("openspec/config.yaml"));
    }

    [Fact(DisplayName = "Rename feature… in a feature column's menu asks for the new name, prefilled, and the column takes it")]
    public async Task RenameFeatureColumn()
    {
        await using BunitContext ctx = NewContext();
        IRenderedComponent<BoardPage> board = ctx.Render<BoardPage>();
        GroupByFeature();
        board.Eventually(() => Assert.Contains("Banking", Columns(board)));

        board.ClickWhenReady("header.col-h", "Banking");
        board.FireWhenReady(".menu-item", e => e.Click(), all => all.Single(x => x.TextContent.Trim() == "Rename feature…"));
        overlays!.Eventually(() => Assert.Equal("Banking", overlays.Find(".dialog input").GetAttribute("value")));
        overlays.InputWhenReady(".dialog input", "Core banking");
        overlays.FireWhenReady(".dialog button", e => e.Click(), all => all.Single(x => x.TextContent.Trim() == "Rename"));

        board.Eventually(() => Assert.Contains("Core banking", Columns(board)));
        Assert.DoesNotContain("Banking", Columns(board));
        Assert.True(Directory.Exists(repo.PathOf("openspec/specs/functional/core-banking")));
    }

    [Fact(DisplayName = "Grouped by feature, + feature at the end of the row adds a feature and its column appears")]
    public async Task AddFeatureColumn()
    {
        await using BunitContext ctx = NewContext();
        IRenderedComponent<BoardPage> board = ctx.Render<BoardPage>();
        GroupByFeature();

        board.ClickWhenReady("button.add-col");
        overlays!.Eventually(() => overlays.InputWhenReady(".dialog input", "Exports"));
        overlays.FireWhenReady(".dialog button", e => e.Click(), all => all.Single(x => x.TextContent.Trim() == "Create"));

        board.Eventually(() => Assert.Contains("Exports", Columns(board)));
    }

    // The layout dropdown in the side menu switches the board to the list, as it opens on a fresh repository: flat.
    private void OpenFreshList()
    {
        nav.ClickWhenReady("button[aria-label=Layout]");
        nav.FireWhenReady(".menu-item", e => e.Click(), all => all.Single(x => x.TextContent.Trim() == "List"));
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
        nav.ClickWhenReady("button.nav-toggle");
        PickField("columns", "feature");
    }

    // ── Statuses of each project, and merging look-alike columns ──

    /// <summary>Two projects, the second with a status "completed" of its own beside Completed (and #wi107 in it) and, with <paramref name="todo"/>, a status "Todo".</summary>
    private void TwoProjectsWithLookAlikes(BunitContext ctx, bool todo = false)
    {
        Workspace ws = ctx.Services.GetRequiredService<Workspace>();
        Assert.True(ws.OpenTab(repo2.Root).Success);
        Assert.True(ws.In(repo2.Root, () => WorkItemsApi.AddStatus("completed")).Success);
        Assert.True(ws.In(repo2.Root, () => WorkItemsApi.SetStatus(107, "completed")).Success);
        if (todo) Assert.True(ws.In(repo2.Root, () => WorkItemsApi.AddStatus("Todo")).Success);
        Assert.True(ws.Switch(repo.Root).Success);
    }

    private static AngleSharp.Dom.IElement HeadOf(IRenderedComponent<BoardPage> board, string column) =>
        board.FindAll(".head-row header.col-h").First(h => h.QuerySelector("h3")!.TextContent == column);

    private static List<string> Heads(IRenderedComponent<BoardPage> board) => board.FindAll(".head-row header.col-h h3").Select(h => h.TextContent).ToList();

    private static AngleSharp.Dom.IElement MenuItem(IRenderedComponent<BoardPage> board, string text) => board.FindAll(".menu-item").Single(i => i.TextContent.Trim() == text);

    [Fact(DisplayName = "The board and the list use the project's statuses: its columns in its order, and the work item in the status it has")]
    public async Task TheBoardUsesTheProjectsStatuses()
    {
        await using BunitContext ctx = NewContext();
        Assert.True(WorkItemsApi.AddStatus("Review ready").Success);
        Assert.True(WorkItemsApi.RemoveStatus("Confirmed").Success);
        Assert.True(WorkItemsApi.RemoveStatus("Blocked").Success);
        Assert.True(WorkItemsApi.MoveStatus("Review ready", -2).Success);
        Assert.True(WorkItemsApi.SetStatus(107, "Review ready").Success);

        IRenderedComponent<BoardPage> board = ctx.Render<BoardPage>();

        string[] expected = ["Backlog", "Planned", "In Progress", "Review ready", "In Review", "Completed"];
        board.Eventually(() => Assert.Equal(expected, Columns(board)));
        AngleSharp.Dom.IElement column = board.FindAll("section.col").Single(c => c.GetAttribute("data-key") == "Review ready");
        Assert.Equal(["107"], column.QuerySelectorAll("article.card").Select(c => c.GetAttribute("data-id")));

        ShowList();
        board.Eventually(() => Assert.Equal(expected, board.FindAll("tr[data-group]").Select(g => g.GetAttribute("data-group"))));
        Assert.Equal(["107"], GroupRows(board, "Review ready").Select(r => r.GetAttribute("data-id")));
    }

    [Fact(DisplayName = "Shift-selecting two status columns and choosing Merge shows one column with both statuses' items, saved per board, and Unmerge splits it again")]
    public async Task MergeAndUnmergeStatusColumns()
    {
        FakeTimeProvider time = new();   // the settings file follows an arrangement after a pause: the test moves it
        await using BunitContext ctx = NewContext(time);
        TwoProjectsWithLookAlikes(ctx);
        IRenderedComponent<BoardPage> board = ctx.Render<BoardPage>();
        string[] split = ["Backlog", "Planned", "Confirmed", "In Progress", "Blocked", "In Review", "Completed", "completed"];
        board.Eventually(() => Assert.Equal(split, Heads(board)));   // the projects' lists side by side: one shared header row
        AngleSharp.Dom.IElement Items(int project, string key) => board.FindAll(".proj")[project].QuerySelectorAll("section.col").Single(c => c.GetAttribute("data-key") == key);
        Assert.Equal(1, Items(1, "completed").QuerySelectorAll("article.card").Length);
        Assert.Equal(1, Items(1, "Completed").QuerySelectorAll("article.card").Length);   // the archived #wi124 of the fixture

        board.Eventually(() => HeadOf(board, "completed").Click(new MouseEventArgs { ShiftKey = true }));
        board.Eventually(() => HeadOf(board, "Completed").Click(new MouseEventArgs { ShiftKey = true }));
        board.Eventually(() => Assert.Equal(2, board.FindAll(".head-row header.col-h.selected").Count));

        board.Eventually(() => HeadOf(board, "completed").Click());   // a plain click opens the menu
        board.Eventually(() => Assert.NotEmpty(board.FindAll(".menu-item")));
        board.Eventually(() => MenuItem(board, "Merge").Click());
        overlays!.Eventually(() => Assert.Equal("Completed", overlays.Find(".dialog input").GetAttribute("value")));   // named after the first selected
        overlays.ClickWhenReady(".dialog button.primary");

        board.Eventually(() => Assert.Equal(["Backlog", "Planned", "Confirmed", "In Progress", "Blocked", "In Review", "Completed"], Heads(board)));
        Assert.Equal(2, Items(1, "Completed").QuerySelectorAll("article.card").Length);   // #wi107 ("completed") and the archived one
        Assert.Equal(1, Items(0, "Completed").QuerySelectorAll("article.card").Length);
        Assert.Equal("3", HeadOf(board, "Completed").QuerySelector(".count")!.TextContent);

        SettingsStore settings = ctx.Services.GetRequiredService<SettingsStore>();
        StatusMerge merge = Assert.Single(settings.Board(repo.Root).Merges!);
        Assert.Equal("Completed", merge.Name);
        Assert.Equal(["Completed", "completed"], merge.Statuses);
        time.Advance(TimeSpan.FromMilliseconds(250));
        Assert.Equal("Completed", Assert.Single(new SettingsStore(settingsFile).Board(repo.Root).Merges!).Name);   // it is in the user's settings file
        Assert.DoesNotContain("erge", repo2.Read("openspec/config.yaml"));   // and in no project
        Assert.Equal("completed", ctx.Services.GetRequiredService<Workspace>().In(repo2.Root, () => WorkItemsApi.Get(107)!.Status));   // no item changed

        board.Eventually(() => HeadOf(board, "Completed").Click());
        board.Eventually(() => Assert.NotEmpty(board.FindAll(".menu-item")));
        board.Eventually(() => MenuItem(board, "Unmerge").Click());

        board.Eventually(() => Assert.Equal(split, Heads(board)));
        Assert.Null(settings.Board(repo.Root).Merges);
        Assert.Equal(1, Items(1, "completed").QuerySelectorAll("article.card").Length);
    }

    [Fact(DisplayName = "A column that looks like a fixed status offers \"Merge with\" it in its menu, and one click merges them")]
    public async Task MergeWithTheFixedLookAlike()
    {
        await using BunitContext ctx = NewContext();
        TwoProjectsWithLookAlikes(ctx);
        IRenderedComponent<BoardPage> board = ctx.Render<BoardPage>();
        board.Eventually(() => Assert.Contains("completed", Heads(board)));

        board.Eventually(() => HeadOf(board, "completed").Click());
        board.Eventually(() => Assert.NotEmpty(board.FindAll(".menu-item")));
        Assert.DoesNotContain(board.FindAll(".menu-item"), i => i.TextContent.Trim() == "Merge");   // nothing is selected
        board.Eventually(() => MenuItem(board, "Merge with Completed").Click());

        board.Eventually(() => Assert.DoesNotContain("completed", Heads(board)));
        StatusMerge merge = Assert.Single(ctx.Services.GetRequiredService<SettingsStore>().Board(repo.Root).Merges!);
        Assert.Equal(("Completed", 2), (merge.Name, merge.Statuses.Count));

        board.Eventually(() => HeadOf(board, "In Progress").Click());   // a column without look-alikes offers nothing of the kind
        board.Eventually(() => Assert.NotEmpty(board.FindAll(".menu-item")));
        Assert.DoesNotContain(board.FindAll(".menu-item"), i => i.TextContent.Contains("Merge"));
    }

    [Fact(DisplayName = "Dropping a card into a merged column uses the status its project has, and asks which when it has more than one")]
    public async Task DroppingIntoAMergedColumn()
    {
        await using BunitContext ctx = NewContext();
        TwoProjectsWithLookAlikes(ctx, todo: true);
        SettingsStore settings = ctx.Services.GetRequiredService<SettingsStore>();
        settings.SaveBoard(repo.Root, settings.Board(repo.Root) with { Merges = [new StatusMerge("Planned", ["Planned", "Todo"])] });
        Workspace ws = ctx.Services.GetRequiredService<Workspace>();
        IRenderedComponent<BoardPage> board = ctx.Render<BoardPage>();
        board.Eventually(() => Assert.DoesNotContain("Todo", Heads(board)));
        AngleSharp.Dom.IElement Planned(int project) => board.FindAll(".proj")[project].QuerySelectorAll("section.col").Single(c => c.GetAttribute("data-key") == "Planned");
        AngleSharp.Dom.IElement Health(int project) => board.FindAll(".proj")[project].QuerySelectorAll("article.card").First(c => c.TextContent.Contains("Health-check endpoint"));

        // the first project has Planned only: the card takes it, with no question
        Health(0).DragStart();
        Planned(0).Drop();
        board.Eventually(() => Assert.Equal("Planned", ws.In(repo.Root, () => WorkItemsApi.Get(123)!.Status)));
        Assert.Empty(overlays!.FindAll(".dialog"));

        // the second has Planned and Todo, both in the merge: it asks
        Health(1).DragStart();
        Planned(1).Drop();
        overlays.Eventually(() => Assert.Equal(["Planned", "Todo"], overlays.FindAll(".dialog .choice").Select(c => c.TextContent.Trim())));
        overlays.FireWhenReady(".dialog .choice", e => e.Click(), all => all.Single(x => x.TextContent.Trim() == "Todo"));
        board.Eventually(() => Assert.Equal("Todo", ws.In(repo2.Root, () => WorkItemsApi.Get(123)!.Status)));
        Assert.Equal("Planned", ws.In(repo.Root, () => WorkItemsApi.Get(123)!.Status));   // the other project's card stayed where it was dropped
    }

    private IDisposable HoldWrites(int id)
    {
        OpenSpecStore.FolderLock.WaitLimit.Value = TimeSpan.FromSeconds(10);   // the write waits for the holder; the test releases it long before
        return OpenSpecStore.FolderLock.Hold(repo.Root, WorkItemsApi.Get(id)!.Folder);
    }

    private static IElement CardOf(IRenderedComponent<BoardPage> board, string column, int id) =>
        Col(board, column).QuerySelectorAll("article.card").Single(c => c.GetAttribute("data-id") == id.ToString());

    [Fact(DisplayName = "A projected card shows the current time as Modified")]
    public async Task AProjectedCardShowsTheCurrentTimeAsModified()
    {
        FakeTimeProvider clock = new(new DateTimeOffset(2031, 3, 4, 5, 6, 0, TimeSpan.Zero));
        await using BunitContext ctx = NewContext(clock);
        IRenderedComponent<BoardPage> board = ctx.Render<BoardPage>();
        string now = Ui.Stamp(clock.GetLocalNow().DateTime);
        Assert.NotEqual(now, CardOf(board, "In Progress", 123).QuerySelector(".stamp")!.TextContent.Trim());
        await board.InvokeAsync(() => CardOf(board, "In Progress", 123).DragStart());
        IDisposable held = HoldWrites(123);
        try
        {
            await board.InvokeAsync(() => Col(board, "Blocked").Drop());
            await board.InvokeAsync(() => { });

            Assert.Equal("In Progress", WorkItemsApi.Get(123)!.Status);   // not written yet
            Assert.Equal(now, CardOf(board, "Blocked", 123).QuerySelector(".stamp")!.TextContent.Trim());   // and already stamped as just changed
        }
        finally { held.Dispose(); }
        board.Eventually(() => Assert.Equal("Blocked", WorkItemsApi.Get(123)!.Status));
    }

    [Fact(DisplayName = "A change from outside during a drop is loaded once after it")]
    public async Task AChangeFromOutsideDuringADropIsLoadedOnceAfterIt()
    {
        FakeTimeProvider time = new();   // the drop's writes tell the pages after their quiet time (#wi213): the test moves it by hand, so the outside write lands before the news
        await using BunitContext ctx = NewContext(time);
        Workspace workspace = ctx.Services.GetRequiredService<Workspace>();
        IRenderedComponent<BoardPage> board = ctx.Render<BoardPage>();
        int reads = board.Instance.Reads;

        await board.InvokeAsync(() => CardOf(board, "In Progress", 123).DragStart());
        await board.InvokeAsync(() => Col(board, "Blocked").Drop());
        board.Eventually(() => Assert.Equal("Blocked", WorkItemsApi.Get(123)!.Status));
        board.Eventually(() => Assert.Equal(1, board.Instance.Reads - reads));   // the drop's own read: it is over ...
        await board.InvokeAsync(() => { });                                       // ... to its last line
        Assert.True(WorkItemsApi.SetTitle(107, "Retitled from outside").Success);   // another writer, right after the drop's own write
        for (int passed = 0; passed < 800; passed += 50)
        {
            time.Advance(TimeSpan.FromMilliseconds(50));
            await board.InvokeAsync(() => { });
        }

        board.Eventually(() => Assert.Equal(2, board.Instance.Reads - reads));   // the drop's own read, and the one the change from outside calls for
        board.Eventually(() => Assert.Contains("Retitled from outside", Col(board, "Planned").TextContent));
        await board.InvokeAsync(() => { });
        Assert.Equal(2, board.Instance.Reads - reads);
    }

    [Fact(DisplayName = "A drop into Completed asks before it archives, shows nothing ahead of the answer, and a refusal moves nothing")]
    public async Task ADropIntoCompletedAsksAndARefusalMovesNothing()
    {
        await using BunitContext ctx = NewContext();
        IRenderedComponent<BoardPage> board = ctx.Render<BoardPage>();
        await board.InvokeAsync(() => CardOf(board, "In Progress", 123).DragStart());

        await board.InvokeAsync(() => Col(board, "Completed").Drop());
        overlays!.Eventually(() => Assert.Contains("Complete and archive?", overlays.Markup));
        Assert.NotNull(CardOf(board, "In Progress", 123));   // archiving is not shown before it is confirmed
        Assert.DoesNotContain(Col(board, "Completed").QuerySelectorAll("article.card"), c => c.GetAttribute("data-id") == "123");

        overlays.FireWhenReady(".dialog button", e => e.Click(), all => all.Single(x => x.TextContent.Trim() == "Cancel"));
        await board.InvokeAsync(() => { });

        Assert.NotNull(CardOf(board, "In Progress", 123));
        Assert.Equal("In Progress", WorkItemsApi.Get(123)!.Status);
        Assert.False(WorkItemsApi.Get(123)!.Archived);
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
        board.Eventually(() => Assert.Equal([NameOf(repo), NameOf(repo2)], board.FindAll(".proj-h h2").Select(h => h.TextContent.Trim())));

        board.FireWhenReady(".proj-h", e => e.Click(new MouseEventArgs { ShiftKey = true }), all => all[1]);   // anywhere in the heading row
        board.Eventually(() => Assert.Contains("selected", board.FindAll(".proj-h")[1].ClassName));
        await board.InvokeAsync(() => { });
        board.KeyDownWhenReady(".board-wrap", "ArrowUp");

        board.Eventually(() => Assert.Equal([NameOf(repo2), NameOf(repo)], board.FindAll(".proj-h h2").Select(h => h.TextContent.Trim())));
        Assert.Equal([repo2.Root, repo.Root], ctx.Services.GetRequiredService<SettingsStore>().Current.OpenRepos);
        board.KeyDownWhenReady(".board-wrap", "ArrowUp");   // already first: stays
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
        board.Eventually(() => Assert.Equal(2, board.FindAll("tr[data-group^='proj:']").Count));

        board.FireWhenReady("tr[data-group^='proj:']", e => e.Click(new MouseEventArgs { ShiftKey = true }), all => all[0]);   // anywhere in the group row
        board.Eventually(() => Assert.True(RowMarkProbe.GroupSelected(board, board.FindAll("tr[data-group^='proj:']")[0])));
        await board.InvokeAsync(() => { });
        board.KeyDownWhenReady(".tbl-wrap", "ArrowDown");

        board.Eventually(() => Assert.Contains(NameOf(repo2), board.FindAll("tr[data-group^='proj:']")[0].TextContent));
        Assert.Equal([repo2.Root, repo.Root], ctx.Services.GetRequiredService<SettingsStore>().Current.OpenRepos);
    }

    [Fact(DisplayName = "Selecting projects and selecting cards are one or the other, and a plain click on the board clears a project selection")]
    public async Task ProjectSelectionClears()
    {
        await using BunitContext ctx = NewContext();
        TwoProjects(ctx);
        IRenderedComponent<BoardPage> board = ctx.Render<BoardPage>();
        board.Eventually(() => Assert.Equal(2, board.FindAll(".proj").Count));
        bool ProjSelected() => board.FindAll(".proj-h.selected").Count > 0;

        board.FireWhenReady(".proj-h", e => e.Click(new MouseEventArgs { ShiftKey = true }), all => all[1]);
        board.Eventually(() => Assert.True(ProjSelected()));
        board.FireWhenReady(".proj article.card", e => e.Click(new MouseEventArgs { ShiftKey = true }), all => all[0]);   // a card: the project lets go
        board.Eventually(() => Assert.False(ProjSelected()));
        Assert.Single(board.FindAll("article.card.selected"));

        board.FireWhenReady(".proj-h", e => e.Click(new MouseEventArgs { ShiftKey = true }), all => all[1]);   // a project: the card lets go
        board.Eventually(() => Assert.True(ProjSelected()));
        Assert.Empty(board.FindAll("article.card.selected"));
        board.ClickWhenReady(".board-wrap");   // a plain click elsewhere
        board.Eventually(() => Assert.False(ProjSelected()));
    }

    [Fact(DisplayName = "Selection has one look everywhere: a solid line in the text colour round the item and a yellow halo outside it, never a fill")]
    public void OneSelectionLook()
    {
        string css = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "wwwroot", "css", "app.css"));
        Assert.Matches(@"\.card\.selected[^{]*\.proj-h\.selected[^{]*\{[^}]*outline: 3px solid var\(--ink\)[^}]*box-shadow: 0 0 0 7px var\(--sun\)", css);
        using BunitContext ctx = new();
        string rule = ctx.Render<RowMarks>(p => p.Add(m => m.Marks, [new RowMarks.RowMark("data-row", "1:0:5", RowMarks.Mark.Selected)])).Find("style").InnerHtml;   // a list row's ring: the same line and halo, inset, written by RowMarks
        Assert.Matches(@"outline:3px solid var\(--ink\);[^}]*box-shadow:inset 0 0 0 1px var\(--ink\),inset 0 0 0 8px var\(--sun\)", rule);
        Assert.DoesNotContain("background", rule);
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
        board.Eventually(() => Assert.NotEmpty(board.FindAll("tr[data-id]")));
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
        board.Eventually(() => Assert.NotEmpty(board.FindAll("tr[data-id]")));
        board.Eventually(() => board.FindAll("tr[data-id]").First(r => r.TextContent.Contains("#wi123")).QuerySelector("button.chev")!.Click());   // subtasks start folded
        board.Eventually(() => Assert.NotEmpty(board.FindAll(".subrow")));
        Assert.Single(board.FindAll(".subrow .agent-chip"));

        board.ClickWhenReady(".subrow .agent-chip");

        board.Eventually(() => Assert.Empty(board.FindAll(".subrow .agent-chip")));
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

        board.Eventually(() => Assert.Equal([NameOf(repo), NameOf(repo2)], board.FindAll(".proj-h h2").Select(h => h.TextContent.Trim())));
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
        board.Eventually(() => Assert.Equal(2, board.FindAll(".proj").Count));

        board.FireWhenReady(".proj .proj-h .chev", e => e.Click(), all => all[1]);
        board.Eventually(() => Assert.Empty(board.FindAll(".proj")[1].QuerySelectorAll("section.col")));
        board.FireWhenReady(".proj .proj-h .chev", e => e.Click(), all => all[1]);
        board.Eventually(() => Assert.NotEmpty(board.FindAll(".proj")[1].QuerySelectorAll("section.col")));

        board.FireWhenReady(".proj .proj-h h2 button", e => e.Click(), all => all[1]);
        board.FireWhenReady(".menu-item", e => e.Click(), all => all.Single(x => x.TextContent.Trim() == "Hide project"));
        board.Eventually(() => Assert.Single(board.FindAll(".proj")));
        Assert.Empty(board.FindAll(".proj-h"));   // alone again: no header
        nav.ClickWhenReady("button.nav-toggle");
        nav.Eventually(() => Assert.Contains(NameOf(repo2), nav.Find(".nav-hidden").TextContent));
        nav.ClickWhenReady($"button[aria-label='Show project {NameOf(repo2)}']");
        board.Eventually(() => Assert.Equal(2, board.FindAll(".proj").Count));
    }

    [Fact(DisplayName = "A card dropped into another project's column stays where it was, and a message says why")]
    public async Task NoDropAcrossProjects()
    {
        await using BunitContext ctx = NewContext();
        TwoProjects(ctx);
        IRenderedComponent<BoardPage> board = ctx.Render<BoardPage>();
        board.Eventually(() => Assert.Equal(2, board.FindAll(".proj").Count));

        board.FindAll(".proj")[0].QuerySelectorAll("article.card").First(c => c.TextContent.Contains("Health-check endpoint")).DragStart();
        board.FindAll(".proj")[1].QuerySelectorAll("section.col").Single(c => c.GetAttribute("data-key") == "Planned").Drop();

        overlays!.Eventually(() => Assert.Contains("stays in its repository", overlays.Find(".toast").TextContent));
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
        board.Eventually(() => Assert.Equal(2, board.FindAll(".proj").Count));
        AngleSharp.Dom.IElement card = board.FindAll(".proj")[1].QuerySelector("article.card")!;
        string id = card.GetAttribute("data-id")!;

        board.Eventually(() => card.Click());

        Assert.EndsWith($"/item/{id}", ctx.Services.GetRequiredService<NavigationManager>().Uri);
        Assert.Equal(repo2.Root, ctx.Services.GetRequiredService<Workspace>().Root);
    }

    [Fact(DisplayName = "In the list, a group's chevron collapses it and expands it again, and it stays collapsed after the data reloads")]
    public async Task CollapseListGroup()
    {
        await using BunitContext ctx = NewContext();
        IRenderedComponent<BoardPage> board = ctx.Render<BoardPage>();
        ShowList();
        board.Eventually(() => Assert.NotEmpty(GroupRows(board, "In Progress")));

        board.Eventually(() => Group(board, "In Progress").QuerySelector("button[aria-label='Expand group']")!.Click());
        board.Eventually(() => Assert.Empty(GroupRows(board, "In Progress")));
        Assert.NotEmpty(GroupRows(board, "Backlog"));   // only that group

        WorkItemsApi.SetPriority(123, "low");   // a write reloads the rows
        board.Eventually(() => Assert.Empty(GroupRows(board, "In Progress")));

        board.Eventually(() => Group(board, "In Progress").QuerySelector("button[aria-label='Expand group']")!.Click());
        board.Eventually(() => Assert.NotEmpty(GroupRows(board, "In Progress")));
    }

    [Fact(DisplayName = "In the list, a project's chevron collapses all of its rows, like on the board, and a group collapsed inside a project stays so, in that project only")]
    public async Task CollapseListProject()
    {
        await using BunitContext ctx = NewContext();
        TwoProjects(ctx);
        IRenderedComponent<BoardPage> board = ctx.Render<BoardPage>();
        ShowList();
        // every click finds its element and fires in one dispatcher step (a render between a Find and a Click re-issues the handler ids: UnknownEventHandlerIdException);
        // each step first lets the renderer finish what the click (or the settings change) queued, with no clock: a check that waits behind
        // a hundred grid renders on a busy machine never runs inside a wait's time limit (the failure said "Check count: 0")
        async Task Settled(Action check) { await board.InvokeAsync(() => { }); board.Eventually(check); }
        await Settled(() => Assert.Equal(2, board.FindAll("tr[data-id='123']").Count));

        board.Eventually(() => board.InvokeAsync(() => board.FindAll("tr[data-group^='proj:']")[0].QuerySelector("button[aria-label='Expand group']")!.Click()).GetAwaiter().GetResult());

        await Settled(() => Assert.Single(board.FindAll("tr[data-id='123']")));
        Assert.True(ctx.Services.GetRequiredService<SettingsStore>().ProjectCollapsed(repo.Root));   // the same setting as the board's project chevron

        // a group collapsed inside the other project stays collapsed when that project folds and unfolds, and only there
        AngleSharp.Dom.IElement Proj2() => board.FindAll("tr[data-group^='proj:']")[1];
        int Rows123() => board.FindAll("tr[data-id='123']").Count;
        board.Eventually(() => board.InvokeAsync(() => board.FindAll("tr[data-group='In Progress']").Last().QuerySelector("button[aria-label='Expand group']")!.Click()).GetAwaiter().GetResult());
        await Settled(() => Assert.Equal(0, Rows123()));
        board.Eventually(() => board.InvokeAsync(() => Proj2().QuerySelector("button[aria-label='Expand group']")!.Click()).GetAwaiter().GetResult());
        await Settled(() => Assert.True(ctx.Services.GetRequiredService<SettingsStore>().ProjectCollapsed(repo2.Root)));
        board.Eventually(() => board.InvokeAsync(() => Proj2().QuerySelector("button[aria-label='Expand group']")!.Click()).GetAwaiter().GetResult());
        await Settled(() => Assert.False(ctx.Services.GetRequiredService<SettingsStore>().ProjectCollapsed(repo2.Root)));
        Assert.Equal(0, Rows123());
        board.Eventually(() => board.InvokeAsync(() => board.FindAll("tr[data-group^='proj:']")[0].QuerySelector("button[aria-label='Expand group']")!.Click()).GetAwaiter().GetResult());
        await Settled(() => Assert.Equal(1, Rows123()));   // the first project's In Progress was never collapsed
    }

    [Fact(DisplayName = "With two repositories, the list groups rows by project first, then by column")]
    public async Task TwoProjectsInTheList()
    {
        await using BunitContext ctx = NewContext();
        TwoProjects(ctx);
        IRenderedComponent<BoardPage> board = ctx.Render<BoardPage>();
        ShowList();

        board.Eventually(() => Assert.Equal(2, board.FindAll("tr[data-group^='proj:']").Count));
        Assert.Contains(NameOf(repo), board.FindAll("tr[data-group^='proj:']")[0].TextContent);
        Assert.Contains(NameOf(repo2), board.FindAll("tr[data-group^='proj:']")[1].TextContent);
        Assert.Equal(2, board.FindAll("tr[data-id='123']").Count);
    }

    [Fact(DisplayName = "Merge asks which selected item is the main one, then merges the others into it")]
    public async Task MergeAsksForTheMainItem()
    {
        await using BunitContext ctx = NewContext();
        IRenderedComponent<BoardPage> board = ctx.Render<BoardPage>();
        for (int i = 0; i < 2; i++) board.FireWhenReady("article.card", e => e.Click(new MouseEventArgs { CtrlKey = true }), all => all[i]);   // re-found: each click re-renders

        board.ClickWhenReady(".actionbar button.primary");

        overlays!.Eventually(() => Assert.Equal(2, overlays.FindAll(".dialog .choice").Count));
        List<int> ids = overlays.FindAll(".dialog .choice").Select(c => int.Parse(System.Text.RegularExpressions.Regex.Match(c.TextContent, @"#wi(\d+)").Groups[1].Value)).ToList();
        Assert.Equal($"#wi{ids[1]} {WorkItemsApi.Get(ids[1])!.Title}", overlays.FindAll(".dialog .choice")[1].TextContent.Trim());
        string mainTitle = WorkItemsApi.Get(ids[1])!.Title;
        overlays.FireWhenReady(".dialog .choice", e => e.Click(), all => all[1]);

        overlays.Eventually(() => Assert.Contains("Merged into #wi", overlays.Find(".toast").TextContent));
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
        board.Eventually(() => Assert.Contains(board.FindAll("thead th"), h => h.TextContent.Contains("In status since")));
        List<string> heads = board.FindAll("thead th").Select(h => h.TextContent).ToList();
        Assert.Equal(heads.FindIndex(h => h.Contains("Due")) + 1, heads.FindIndex(h => h.Contains("In status since")));
        Assert.Contains(today.ToString("d MMM"), board.FindAll("tr[data-id='107']").First().TextContent);
        Assert.Contains(new DateOnly(2026, 9, 12).ToString("d MMM"), board.FindAll("tr[data-id='123']").First().TextContent);   // never moved: its creation date
    }

    // One of the side menu's Organise dropdowns (columns, rows or colour): open it, pick a value.
    private void PickField(string field, string value)
    {
        if (nav!.FindAll(".nav-view").Count == 0) nav.ClickWhenReady("button.nav-toggle");
        AngleSharp.Dom.IElement Field() => nav.FindAll(".field").Single(f => f.QuerySelector("label")!.TextContent.Trim() == field);   // "label" is a choice in several fields: stay in this one
        nav.Eventually(() => Field().QuerySelector(".dd")!.Click());
        nav.Eventually(() => Assert.Contains(Field().QuerySelectorAll(".rz-dropdown-item"), i => i.TextContent.Trim() == value));
        nav.Eventually(() => Field().QuerySelectorAll(".rz-dropdown-item").First(i => i.TextContent.Trim() == value).Click());
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

        board.Eventually(() => Assert.Equal(["High priority", "Lower priority"], board.FindAll(".swim-h span:first-child").Select(s => s.TextContent.Trim())));
        Assert.Equal(7, board.FindAll("section.col h3").Count);   // the column headers once, on top
        Assert.All(board.FindAll(".swim"), row => Assert.Equal(7, row.QuerySelectorAll("section.col[data-drop]").Length));   // a drop cell per column in every row, empty ones too
        Assert.All(board.FindAll(".swim .lane"), l => Assert.True(l.HasAttribute("data-drop")));
        Assert.Empty(board.FindAll(".lane-h"));
        AngleSharp.Dom.IElement Cell(string row, string column) => board.Find($".swim[data-swim={row}] section.col[data-cell='{column}']");
        Assert.Single(Cell("high", "Backlog").QuerySelectorAll("article.card"));
        Assert.Equal(2, Cell("low", "Backlog").QuerySelectorAll("article.card").Length);   // the fixture's own Backlog item and the new one

        Cell("low", "Backlog").QuerySelectorAll("article.card").First(c => c.GetAttribute("data-id") == other.ToString()).DragStart();
        Cell("high", "In Progress").QuerySelector(".lane")!.Drop();

        board.Eventually(() => Assert.Equal("In Progress", WorkItemsApi.Get(other)!.Status));
        board.Eventually(() => Assert.Equal("high", WorkItemsApi.Get(other)!.Priority));
        board.Eventually(() => Assert.True(HasCard(Cell("high", "In Progress"), other)));
        Assert.False(HasCard(Cell("low", "Backlog"), other));
    }

    [Fact(DisplayName = "The column menu no longer offers subgroups, and a column shows its cards in one lane whatever their priorities")]
    public async Task NoSubgroups()
    {
        await using BunitContext ctx = NewContext();
        Workspace workspace = ctx.Services.GetRequiredService<Workspace>();
        int urgent = workspace.CreateItem("Backlog").Id;
        Assert.True(WorkItemsApi.SetPriority(urgent, "high").Success);
        IRenderedComponent<BoardPage> board = ctx.Render<BoardPage>();

        board.ClickWhenReady("header.col-h", "Backlog");
        board.Eventually(() => Assert.Contains(board.FindAll(".menu-item"), i => i.TextContent.Contains("Hide column")));
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
        board.Eventually(() => Assert.Contains("swim:bug", ListOrder(board)));
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
        board.Eventually(() => Assert.Equal("3", Group(board, "Planned").QuerySelector(".count")!.TextContent.Trim()));
        Assert.All(new[] { "~", "bug", "feature" }, v => Assert.Equal("1", SwimIn(board, "Planned", v).QuerySelector(".count")!.TextContent.Trim()));

        // the bug from In Progress, dropped on Planned's feature group: it takes both the column and the row value
        board.Find("tr[data-id='123']").DragStart();
        await board.InvokeAsync(() => { });   // let the dispatcher drain, so the drop targets the current render tree
        SwimIn(board, "Planned", "feature").Drop();

        board.Eventually(() => Assert.Equal("Planned", WorkItemsApi.Get(123)!.Status));
        board.Eventually(() => Assert.Equal("feature", WorkItemsApi.Get(123)!.Label));
        board.Eventually(() => Assert.Equal("4", Group(board, "Planned").QuerySelector(".count")!.TextContent.Trim()));
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
        board.Eventually(() => Assert.Equal(2, board.FindAll("tr[data-group='swim:bug']").Count));

        board.Eventually(() => SwimIn(board, "Planned", "bug").QuerySelector("button[aria-label='Expand group']")!.Click());

        board.Eventually(() => Assert.Empty(board.FindAll($"tr[data-id='{bugInPlanned}']")));
        Assert.Single(board.FindAll("tr[data-id='123']"));   // the same label in In Progress stays open
        Assert.Equal(2, board.FindAll("tr[data-group='swim:bug']").Count);

        WorkItemsApi.SetPriority(123, "low");   // a write reloads the rows: the group stays shut
        board.Eventually(() => Assert.Empty(board.FindAll($"tr[data-id='{bugInPlanned}']")));
        Assert.Single(board.FindAll("tr[data-id='123']"));

        board.Eventually(() => SwimIn(board, "Planned", "bug").QuerySelector("button[aria-label='Expand group']")!.Click());
        board.Eventually(() => Assert.Single(board.FindAll($"tr[data-id='{bugInPlanned}']")));
    }

    [Fact(DisplayName = "The calendar view is a roadmap: items listed by group on the left, bars from start to due on the right, a group bar spanning the group's dates, a today line, and Today / previous / next / zoom controls")]
    public async Task CalendarIsARoadmap()
    {
        await using BunitContext ctx = NewContext();
        DateOnly today = DateOnly.FromDateTime(DateTime.Today);
        WorkItemsApi.SetStarted(123, today);
        WorkItemsApi.SetDue(123, today.AddDays(1));
        IRenderedComponent<BoardPage> board = ctx.Render<BoardPage>();

        nav.ClickWhenReady("button[aria-label=Layout]");
        nav.FireWhenReady(".menu-item", e => e.Click(), all => all.Single(x => x.TextContent.Trim() == "Calendar"));

        board.Eventually(() => Assert.NotEmpty(board.FindAll(".rm")));
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

        board.ClickWhenReady(".rm-next");
        board.Eventually(() => Assert.Contains(today.AddMonths(1).ToString("MMM yyyy"), Range()));
        board.ClickWhenReady(".rm-prev");
        board.Eventually(() => Assert.Contains(today.ToString("MMM yyyy"), Range()));
        board.ClickWhenReady(".rm-next");
        board.ClickWhenReady(".rm-today-btn");
        board.Eventually(() => Assert.Equal(today.ToString("MMM yyyy"), Range().Split(" – ")[0]));

        board.ClickWhenReady(".rm-zoom [data-zoom=quarter]");
        RoadmapWindow quarter = RoadmapModel.Window(today, "quarter");
        board.Eventually(() => Assert.Contains($"--col:{quarter.ColumnOf(today)}; --span:1;", board.Find(".rm-bar[data-id='123']").GetAttribute("style")));   // week columns now

        AngleSharp.Dom.IElement chevron = board.FindAll(".rm-lgroup").First(g => g.QuerySelector(".rm-row[data-id='123']") is not null).QuerySelector(".rm-chev")!;
        board.Eventually(() => chevron.Click());   // collapsing a group hides its rows and bars
        board.Eventually(() => Assert.Empty(board.FindAll(".rm-row[data-id='123']")));
        Assert.Empty(board.FindAll(".rm-bar[data-id='123']"));
        board.ClickWhenReady(".rm-chev[aria-expanded=false]");

        board.Eventually(() => Assert.NotEmpty(board.FindAll(".rm-bar[data-id='123']")));
        board.ClickWhenReady(".rm-bar[data-id='123']");
        Assert.EndsWith("/item/123", ctx.Services.GetRequiredService<NavigationManager>().Uri);
    }

    [Fact(DisplayName = "With swimlanes by priority the roadmap groups its items by priority instead of the board's columns")]
    public async Task RoadmapGroupsBySwimlane()
    {
        await using BunitContext ctx = NewContext();
        Assert.True(WorkItemsApi.SetPriority(123, "high").Success);
        IRenderedComponent<BoardPage> board = ctx.Render<BoardPage>();

        PickSwimlanes("priority");
        nav.ClickWhenReady("button[aria-label=Layout]");
        nav.FireWhenReady(".menu-item", e => e.Click(), all => all.Single(x => x.TextContent.Trim() == "Calendar"));

        board.Eventually(() => Assert.NotEmpty(board.FindAll(".rm-row[data-id='123']")));
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

        nav.ClickWhenReady("button.nav-toggle");
        Assert.Equal(["sort by", "columns", "rows", "colour"], nav.FindAll(".nav-view .organise .field label").Select(l => l.TextContent.Trim()));
        Assert.Contains("Organise", nav.Find(".nav-view .organise .nav-h").TextContent);
        Assert.DoesNotContain("swimlanes", nav.Find(".nav-view").TextContent);
        Assert.DoesNotContain("group by", nav.Find(".nav-view").TextContent);
        Assert.Equal(["priority", "status", "none", "status"], nav.FindAll(".organise .field .rz-dropdown-label").Select(l => l.TextContent.Trim()));   // today's board: columns by status, no rows, coloured by status

        PickField("rows", "feature");
        PickField("colour", "label");

        board.Eventually(() => Assert.Contains("Availability", board.FindAll(".swim-h span:first-child").Select(s => s.TextContent.Trim())));
        Assert.Contains("Banking", board.FindAll(".swim-h span:first-child").Select(s => s.TextContent.Trim()));
        Assert.Equal(board.FindAll(".swim-h").Count, board.FindAll(".swim").Count);
        Assert.Equal(7, board.FindAll("section.col h3").Count);   // the status columns, once, on top
        AngleSharp.Dom.IElement Band(int id) => board.FindAll("article.card").First(c => c.GetAttribute("data-id") == id.ToString()).QuerySelector(".band")!;
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
        bool Card(int id) => board.FindAll("article.card").Any(c => c.GetAttribute("data-id") == id.ToString());
        int shown = board.FindAll("article.card").Count;
        Assert.True(Card(107) && Card(123) && shown > 1);   // before any period

        nav.ClickWhenReady("button.nav-toggle");
        Assert.Contains("changed", nav.FindAll(".nav-view .field label").Select(l => l.TextContent.Trim()));
        Assert.Contains("any time", nav.Find("button[aria-label='changed']").TextContent);
        Assert.Empty(nav.FindAll("button[aria-label='Clear changed period']"));   // no × beside the field: Show all is in the picker
        Assert.Empty(nav.FindAll(".legend .lg.head"));

        // the day picker of the item page: first day, then last day; with nothing chosen yet it offers no Show all
        nav.ClickWhenReady("button[aria-label='changed']");
        nav.Eventually(() => Assert.NotEmpty(nav.FindAll(".range-pop")));
        Assert.Empty(nav.FindAll(".range-pop button.range-clear"));
        foreach (DateOnly day in new[] { first, last })
        {
            await nav.InvokeAsync(() => { });
            nav.Eventually(() => nav.ClickWhenReady($".range-pop button.rd[data-day='{day:yyyy-MM-dd}']"));
        }
        nav.Eventually(() => Assert.Equal((first, last), (settings.Board(repo.Root).ChangedFrom, settings.Board(repo.Root).ChangedTo)));   // remembered per board
        nav.Eventually(() => Assert.Contains(TaskManager.Helpers.Ui.Period(first, last), nav.Find("button[aria-label='changed']").TextContent));
        Assert.Equal("changed " + TaskManager.Helpers.Ui.Period(first, last), nav.Find(".legend .lg.head").TextContent.Trim());   // the legend's first line names it
        nav.Eventually(() => Assert.Equal($"1 of {shown} shown", nav.Find(".legend .lg.count").TextContent.Trim()));   // the second line counts what is shown of what the other filters leave
        Assert.Equal(["In Progress"], nav.FindAll(".legend .lg:not(.head)").Select(l => l.TextContent.Trim()));   // and keeps only the colours of the shown items

        // the board: 123 alone is left, with its colour
        board.Eventually(() => Assert.Single(board.FindAll("article.card")));
        Assert.True(Card(123) && !Card(107));
        Assert.Contains("var(--sun)", board.Find("article.card .band").GetAttribute("style"));
        // the list: one row
        settings.SaveBoard(repo.Root, settings.Board(repo.Root) with { View = "list" });
        board.Eventually(() => Assert.NotEmpty(board.FindAll("tr[data-id='123']")));
        Assert.Empty(board.FindAll("tr[data-id='107']"));
        // the calendar: the bar of 123, none for 107
        settings.SaveBoard(repo.Root, settings.Board(repo.Root) with { View = "calendar" });
        board.Eventually(() => Assert.NotEmpty(board.FindAll(".rm-bar[data-id='123']")));
        Assert.Empty(board.FindAll(".rm-bar[data-id='107'], .rm-row[data-id='107']"));

        // Show all in the picker puts everything back
        nav.ClickWhenReady("button[aria-label='changed']");
        nav.Eventually(() => Assert.Equal(["Show all"], nav.FindAll(".range-pop button.range-clear").Select(b => b.TextContent.Trim())));
        await nav.InvokeAsync(() => { });
        nav.ClickWhenReady(".range-pop button.range-clear");
        nav.Eventually(() => Assert.Equal((null, null), (settings.Board(repo.Root).ChangedFrom, settings.Board(repo.Root).ChangedTo)));
        nav.Eventually(() => Assert.Contains("any time", nav.Find("button[aria-label='changed']").TextContent));
        nav.Eventually(() => Assert.Empty(nav.FindAll(".range-pop")));   // and the popup closes
        Assert.Empty(nav.FindAll(".legend .lg.head"));
        board.Eventually(() => Assert.NotEmpty(board.FindAll(".rm-bar[data-id='107']")));
        settings.SaveBoard(repo.Root, settings.Board(repo.Root) with { View = "board" });
        board.Eventually(() => Assert.Equal(shown, board.FindAll("article.card").Count));
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
            Cell(from.Row, from.Column).QuerySelectorAll("article.card").First(c => c.GetAttribute("data-id") == id.ToString()).DragStart();
            Cell(to.Row, to.Column).QuerySelector(".lane")!.Drop();
        });

        // columns by status, rows by feature (a field with several values: the card leaves its old feature for the new one)
        PickField("rows", "feature");
        board.Eventually(() => Assert.True(HasCard(Cell("functional/banking", "Backlog"), pay)));
        await Drag(("functional/banking", "Backlog"), pay, ("non-functional/availability", "In Progress"));
        board.Eventually(() => Assert.Equal("In Progress", WorkItemsApi.Get(pay)!.Status));
        board.Eventually(() => Assert.Equal(["non-functional/availability"], WorkItemsApi.Get(pay)!.Features.Select(f => f.Path)));
        board.Eventually(() => Assert.True(HasCard(Cell("non-functional/availability", "In Progress"), pay)));

        // columns by priority, rows by label: two single-valued fields
        settings.SaveBoard(repo.Root, settings.Board(repo.Root) with { GroupBy = "priority", Swimlane = "label" });
        board.Eventually(() => Assert.True(HasCard(Cell(BoardModel.NoRow, "low"), pay)));
        await Drag((BoardModel.NoRow, "low"), pay, ("bug", "high"));
        board.Eventually(() => Assert.Equal("high", WorkItemsApi.Get(pay)!.Priority));
        board.Eventually(() => Assert.Equal("bug", WorkItemsApi.Get(pay)!.Label));
        Assert.Equal("In Progress", WorkItemsApi.Get(pay)!.Status);   // the status was not a dimension this time

        // a field a drop cannot set (due dates) stays read-only: the other dimension still applies
        settings.SaveBoard(repo.Root, settings.Board(repo.Root) with { GroupBy = "status", Swimlane = "due" });
        board.Eventually(() => Assert.True(HasCard(Cell("none", "In Progress"), pay)));
        await Drag(("none", "In Progress"), pay, ("overdue", "Blocked"));
        board.Eventually(() => Assert.Equal("Blocked", WorkItemsApi.Get(pay)!.Status));
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
        board.Eventually(() => Assert.Contains("swim:bug", board.FindAll("tr[data-group]").Select(g => g.GetAttribute("data-group"))));
        List<string> order = board.FindAll(".rz-data-grid tbody tr").Select(tr => tr.GetAttribute("data-group") ?? "#" + tr.GetAttribute("data-id")).ToList();
        string status = WorkItemsApi.Get(123)!.Status;
        Assert.True(order.IndexOf(status) < order.IndexOf("swim:bug") && order.IndexOf("swim:bug") < order.IndexOf("#123"), string.Join(",", order));
        Assert.Contains("var(--peach)", board.Find("tr[data-id='123']").GetAttribute("style"));   // bug
        Assert.Contains("var(--stone)", board.Find("tr[data-id='107']").GetAttribute("style"));   // no label

        // the roadmap: a row header with the columns as sub-headers, bars coloured by label
        settings.SaveBoard(repo.Root, settings.Board(repo.Root) with { View = "calendar" });
        board.Eventually(() => Assert.NotEmpty(board.FindAll(".rm-bar[data-id='123']")));
        Assert.Contains("bug", board.FindAll(".rm-group .rm-gname").Select(g => g.TextContent));
        AngleSharp.Dom.IElement bug = board.FindAll(".rm-lgroup").First(g => g.QuerySelector(".rm-gname")!.TextContent == "bug");
        Assert.Contains(bug.QuerySelectorAll(".rm-subgroup .rm-sname"), s => s.TextContent == status);
        Assert.NotNull(bug.QuerySelector(".rm-subgroup ~ .rm-row.deep[data-id='123']"));   // under its column's sub-header, a level in
        Assert.Contains("var(--peach)", board.Find(".rm-bar[data-id='123']").GetAttribute("style"));
        Assert.Contains("var(--peach)", board.Find(".rm-row[data-id='123']").GetAttribute("style"));   // the left dot

        // no rows: just the columns as groups, no sub-headers
        settings.SaveBoard(repo.Root, settings.Board(repo.Root) with { Swimlane = "none" });
        board.Eventually(() => Assert.Empty(board.FindAll(".rm-subgroup")));
        Assert.Contains(status, board.FindAll(".rm-group .rm-gname").Select(g => g.TextContent));
    }

    [Fact(DisplayName = "The side menu separates the organise block (sort first), the filters, and the display switches with dashed lines")]
    public async Task SideMenuSeparators()
    {
        await using BunitContext ctx = NewContext();
        ctx.Render<BoardPage>();
        nav.ClickWhenReady("button.nav-toggle");

        List<AngleSharp.Dom.IElement> parts = nav.Find(".nav-view").Children.ToList();
        List<AngleSharp.Dom.IElement> seps = parts.Where(p => p.ClassList.Contains("nav-sep")).ToList();
        Assert.Equal(2, seps.Count);
        Assert.All(seps, s => Assert.Equal("separator", s.GetAttribute("role")));
        Assert.True(parts[parts.IndexOf(seps[0]) - 1].ClassList.Contains("organise"));          // after the organise block (sort by, columns, rows, colour, legend)
        Assert.Contains("contributor", parts[parts.IndexOf(seps[0]) + 1].TextContent);          // before the filters
        Assert.True(parts[parts.IndexOf(seps[1]) - 1].ClassList.Contains("nav-filters"));       // after the filters block (contributor, period, changed, the Work items / Subtasks switch)
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
        board.Eventually(() => Assert.Equal(["~", "bug", "feature", "enhancement"], Rows()));

        // Shift-click selects (tinted); the up arrow moves the row past its neighbour and the order is remembered per row field
        board.Eventually(() => Head("feature").Click(new MouseEventArgs { ShiftKey = true }));
        board.Eventually(() => Assert.Contains("selected", Head("feature").ClassName));
        await board.InvokeAsync(() => { });
        board.KeyDownWhenReady(".board-wrap", "ArrowUp");
        board.Eventually(() => Assert.Equal(["~", "feature", "bug", "enhancement"], Rows()));
        Assert.Contains("selected", Head("feature").ClassName);   // still selected, so the next press moves it again
        Assert.Equal(["row:label:~", "row:label:feature", "row:label:bug", "row:label:enhancement"], settings.Board(repo.Root).Order!.Where(k => k.StartsWith("row:label:")));
        board.KeyDownWhenReady(".board-wrap", "ArrowDown");
        board.Eventually(() => Assert.Equal(["~", "bug", "feature", "enhancement"], Rows()));
        Assert.Contains(".swim-h.selected", File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "wwwroot", "js", "app.js")));   // arrows reach the page while a row is selected

        // a plain click lets go of the selection
        board.Eventually(() => Head("bug").Click());
        board.Eventually(() => Assert.Empty(board.FindAll(".swim-h.selected")));

        // dragging a header onto another moves it in front of that one
        Head("enhancement").DragStart();
        Head("bug").Drop();
        board.Eventually(() => Assert.Equal(["~", "enhancement", "bug", "feature"], Rows()));

        // the × hides the row (its cells too); the side menu lists it with an eye that brings it back in its place
        board.Eventually(() => Head("bug").QuerySelector("button.swim-hide")!.Click());
        board.Eventually(() => Assert.Equal(["~", "enhancement", "feature"], Rows()));
        Assert.Equal(Rows().Count, board.FindAll(".swim").Count);
        Assert.Contains("row:label:bug", settings.Board(repo.Root).Hidden!);
        nav!.Eventually(() => Assert.Equal(["bug"], nav.FindAll(".nav-hidden .grp-row .nm").Select(n => n.TextContent.Trim())));
        nav.ClickWhenReady(".nav-hidden button[aria-label='Show bug']");
        board.Eventually(() => Assert.Equal(["~", "enhancement", "bug", "feature"], Rows()));
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
        nav.ClickWhenReady("button.nav-toggle");
        Assert.Empty(nav.FindAll(".nav-worklog"));

        IRenderedComponent<Worklog> log = OpenWorklog(ctx);
        nav.Eventually(() => Assert.NotEmpty(nav.FindAll(".nav-worklog")));
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
        nav.FireWhenReady(".nav-worklog button.nav-pill", e => e.Click(), all => all[0]);   // Notes: with every kind on, a click shows only that kind
        log.Eventually(() => Assert.All(kinds(), k => Assert.Equal("note", k)));
        Assert.True(ui.WorklogFilter.Notes);
        Assert.False(ui.WorklogFilter.Events);
        nav.FireWhenReady(".nav-worklog button.nav-pill", e => e.Click(), all => all[0]);   // the only one on, off again: every kind is back
        Assert.Equal(new WorklogFilters(), ui.WorklogFilter);

        // who: a dropdown of the people in the feed
        nav.ClickWhenReady(".nav-worklog .field .dd");
        nav.Eventually(() => Assert.True(nav.FindAll(".rz-dropdown-item").Count > 1));
        Assert.Equal("everyone", nav.FindAll(".rz-dropdown-item")[0].TextContent.Trim());
        Assert.Equal(UiPeople(ctx).AsEnumerable(), nav.FindAll(".rz-dropdown-item").Skip(1).Select(i => i.TextContent.Trim()));   // each person seen in the feed

        // an action does what the day menu does for today
        int before = NotesApi.GetForWorkItem(123).Count;
        nav.ClickWhenReady(".nav-worklog button.nav-add");
        nav.FireWhenReady("button[role=menuitem]", e => e.Click(), all => all.Single(x => x.TextContent.Trim() == "Note"));
        log.Eventually(() => Assert.Equal(before + 1, NotesApi.GetForWorkItem(123).Count));
        log.Eventually(() => Assert.NotEmpty(log.FindAll(".wk-edit")));   // opened for writing, in today's group

        // the block goes with the worklog
        ((IDisposable)log.Instance).Dispose();   // the view goes away; the side menu stays
        Assert.Null(ui.WorklogOpen);
        nav.Eventually(() => Assert.Empty(nav.FindAll(".nav-worklog")));
    }

    [Fact(DisplayName = "In split view the Worklog block comes before the list's organise, sort and display blocks")]
    public async Task SplitViewPutsTheWorklogFirst()
    {
        await using BunitContext ctx = NewContext();
        SettingsStore settings = ctx.Services.GetRequiredService<SettingsStore>();
        settings.Update(s => s with { Split = true });
        ctx.Services.GetRequiredService<NavigationManager>().NavigateTo("item/123");
        nav.ClickWhenReady("button.nav-toggle");

        // the item is open beside the list, with no worklog: the menu holds the list's options as before
        nav.Eventually(() => Assert.NotEmpty(nav.FindAll(".organise")));
        Assert.Empty(nav.FindAll(".nav-worklog"));

        IRenderedComponent<Worklog> log = OpenWorklog(ctx);
        nav.Eventually(() => Assert.NotEmpty(nav.FindAll(".nav-worklog")));
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
        nav.Eventually(() => Assert.Empty(nav.FindAll(".organise")));
        Assert.NotEmpty(nav.FindAll(".nav-worklog"));
        ui.SetNarrowWindow(false);
        nav.Eventually(() => Assert.NotEmpty(nav.FindAll(".organise")));

        // the worklog closes: the menu is the list's alone
        ((IDisposable)log.Instance).Dispose();   // the view goes away; the side menu stays
        nav.Eventually(() => Assert.Empty(nav.FindAll(".nav-worklog")));
        Assert.NotEmpty(nav.FindAll(".organise"));
    }

    // ── worklog days ──

    [Fact(DisplayName = "The Worklog block lists the feed's days newest first with their entry counts, and clicking a day jumps the feed to it")]
    public async Task SideMenuListsTheWorklogDays()
    {
        await using BunitContext ctx = NewContext();
        ctx.Services.GetRequiredService<SettingsStore>().Update(s => s with { Split = false });
        ctx.Services.GetRequiredService<NavigationManager>().NavigateTo("item/123");
        nav.ClickWhenReady("button.nav-toggle");
        IRenderedComponent<Worklog> log = OpenWorklog(ctx);
        nav.Eventually(() => Assert.NotEmpty(nav.FindAll(".nav-worklog .nav-day")));

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
        nav.Eventually(() => feed[^1].QuerySelector(".wk-date")!.Click());   // fold it
        log.Eventually(() => Assert.Empty(log.Find($".wk-day[data-day='{day}']").QuerySelectorAll(".wk-e")));
        nav.Eventually(() => rows()[feed.Length - 1].Click());
        log.Eventually(() => Assert.NotEmpty(log.Find($".wk-day[data-day='{day}']").QuerySelectorAll(".wk-e")));
        log.Eventually(() => Assert.Equal($".wk-day[data-day='{day}']", ctx.JSInterop.Invocations["rtm.scrollToDay"].Single().Arguments[0]));
    }

    // ── decisions and edits ──

    [Fact(DisplayName = "The Worklog block shows Decisions and Edits filter pills and a Decision entry in the Add entry menu")]
    public async Task SideMenuShowsDecisionsAndEdits()
    {
        await using BunitContext ctx = NewContext();
        ctx.Services.GetRequiredService<SettingsStore>().Update(s => s with { Split = false });
        ctx.Services.GetRequiredService<NavigationManager>().NavigateTo("item/123");
        UiService ui = ctx.Services.GetRequiredService<UiService>();
        nav.ClickWhenReady("button.nav-toggle");
        IRenderedComponent<Worklog> log = OpenWorklog(ctx);
        nav.Eventually(() => Assert.NotEmpty(nav.FindAll(".nav-worklog")));
        IElement block = nav.Find(".nav-worklog");

        Assert.Equal(["Notes", "Decisions", "Bugs", "Events", "Links", "Subtasks", "Edits"], block.QuerySelectorAll("button.nav-pill").Select(l => l.TextContent.Trim()));

        nav.FireWhenReady(".nav-worklog button.nav-pill", e => e.Click(), all => all[1]);   // Decisions: every kind was on, so only this one now
        Assert.True(ui.WorklogFilter.Decisions);
        Assert.False(ui.WorklogFilter.Notes);
        nav.FireWhenReady(".nav-worklog button.nav-pill", e => e.Click(), all => all[6]);   // Edits joins it
        Assert.True(ui.WorklogFilter.Edits);
        Assert.False(ui.WorklogFilter.Events);
        nav.FireWhenReady(".nav-worklog button.nav-pill", e => e.Click(), all => all[2]);   // Bugs too
        Assert.True(ui.WorklogFilter.Bugs);
        nav.FireWhenReady(".nav-worklog button.nav-pill", e => e.Click(), all => all[1]);   // each click takes its kind out again; the last one brings every kind back
        nav.FireWhenReady(".nav-worklog button.nav-pill", e => e.Click(), all => all[6]);
        nav.FireWhenReady(".nav-worklog button.nav-pill", e => e.Click(), all => all[2]);
        Assert.Equal(new WorklogFilters(), ui.WorklogFilter);

        // the Decision entry does what the day menu's does for today
        nav.ClickWhenReady(".nav-worklog button.nav-add");
        nav.FireWhenReady("button[role=menuitem]", e => e.Click(), all => all.Single(x => x.TextContent.Trim() == "Decision"));
        log.Eventually(() => Assert.Single(NotesApi.GetForWorkItem(123), n => n.IsDecision));
        log.Eventually(() => Assert.NotEmpty(log.FindAll(".wk-edit.wk-decision")));
    }

    // ── analytics view ──

    private void ShowAnalytics()
    {
        nav.ClickWhenReady("button[aria-label=Layout]");
        nav.FireWhenReady(".menu-item", e => e.Click(), all => all.Single(x => x.TextContent.Trim() == "Analytics"));
    }

    [Fact(DisplayName = "The Analytics view shows the two default charts, each an SVG with its title and legend")]
    public async Task AnalyticsShowsTheDefaultCharts()
    {
        await using BunitContext ctx = NewContext();
        IRenderedComponent<BoardPage> board = ctx.Render<BoardPage>();
        ShowAnalytics();

        board.Eventually(() => Assert.Equal(2, board.FindAll(".an-chart").Count));
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
        board.Eventually(() => Assert.Equal(2, board.FindAll(".an-chart").Count));

        IElement Chart(int n) => board.FindAll(".an-chart")[n];
        void Pick(string field, string value)
        {
            IElement Field() => board.Find(".an-edit").QuerySelectorAll(".field").Single(f => f.QuerySelector("label")!.TextContent.Trim() == field);
            board.Eventually(() => Field().QuerySelector(".dd")!.Click());
            board.Eventually(() => Assert.Contains(Field().QuerySelectorAll(".rz-dropdown-item"), i => i.TextContent.Trim() == value));
            board.Eventually(() => Field().QuerySelectorAll(".rz-dropdown-item").First(i => i.TextContent.Trim() == value).Click());
        }

        board.Eventually(() => Chart(0).QuerySelector("button[aria-label='Edit chart']")!.Click());
        board.Eventually(() => Assert.NotNull(board.Find(".an-edit")));
        Assert.Equal("column", Chart(0).QuerySelector("svg.chart")!.GetAttribute("data-layout"));
        Pick("Layout", "Bar");
        board.Eventually(() => Assert.Equal("bar", Chart(0).QuerySelector("svg.chart")!.GetAttribute("data-layout")));
        Pick("X-axis", "Feature");
        Pick("Group by", "Status");
        board.Eventually(() => Assert.NotEmpty(Chart(0).QuerySelectorAll(".an-legend li")));
        Assert.Contains("feature", Chart(0).QuerySelector("svg.chart")!.GetAttribute("aria-label")!, StringComparison.OrdinalIgnoreCase);
        ChartSpec saved = settings.Board(repo.Root).Charts![0];
        Assert.Equal(("bar", "feature", "status"), (saved.Layout, saved.X, saved.Group));

        // another view of the same board remembers it
        IRenderedComponent<BoardPage> again = ctx.Render<BoardPage>();
        Assert.Equal("bar", again.FindAll(".an-chart")[0].QuerySelector("svg.chart")!.GetAttribute("data-layout"));

        board.ClickWhenReady(".an-add");
        board.Eventually(() => Assert.Equal(3, board.FindAll(".an-chart").Count));
        Assert.Equal(3, settings.Board(repo.Root).Charts!.Count);
        Assert.NotEmpty(board.FindAll(".an-edit"));   // the new chart opens in its editor
        board.ClickWhenReady(".an-edit button.an-del");
        board.Eventually(() => Assert.Equal(2, board.FindAll(".an-chart").Count));
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
        nav.ClickWhenReady("button.nav-toggle");
        int shown = board.FindAll("article.card").Count;
        Assert.True(shown > 1);

        settings.SaveBoard(repo.Root, settings.Board(repo.Root) with { ChangedFrom = first, ChangedTo = last });
        nav.Eventually(() => Assert.Equal($"1 of {shown} shown", nav.Find(".legend .lg.count").TextContent.Trim()));
        board.Eventually(() => Assert.Single(board.FindAll("article.card")));

        // a period that takes in every day since the start of the year: everything is in it, and the legend says so
        settings.SaveBoard(repo.Root, settings.Board(repo.Root) with { ChangedFrom = new DateOnly(2026, 1, 1), ChangedTo = today });
        nav.Eventually(() => Assert.Equal($"{shown} of {shown} shown", nav.Find(".legend .lg.count").TextContent.Trim()));
        board.Eventually(() => Assert.Equal(shown, board.FindAll("article.card").Count));
        Assert.Equal("changed " + TaskManager.Helpers.Ui.Period(new DateOnly(2026, 1, 1), today), nav.Find(".legend .lg.head").TextContent.Trim());   // the head line is unchanged
        settings.SaveBoard(repo.Root, settings.Board(repo.Root) with { ChangedFrom = null, ChangedTo = null });
        nav.Eventually(() => Assert.Empty(nav.FindAll(".legend .lg.count")));
    }

    // ── no agent filter ──

    [Fact(DisplayName = "The side menu has no agent work to review option, and a settings file that still has AgentOnly loads")]
    public async Task NoAgentFilter()
    {
        await using BunitContext ctx = NewContext();
        SettingsStore settings = ctx.Services.GetRequiredService<SettingsStore>();
        IRenderedComponent<BoardPage> board = ctx.Render<BoardPage>();
        nav.ClickWhenReady("button.nav-toggle");
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

        nav.ClickWhenReady("button.nav-toggle");
        nav.ClickWhenReady("button[aria-label='changed']");
        nav.Eventually(() => Assert.NotEmpty(nav.FindAll(".range-pop")));
        Assert.True(From().HasAttribute("disabled") && To().HasAttribute("disabled"));   // no range yet: nothing to time
        for (int n = 0; n < 2; n++)   // one day: its first click and its second
        {
            await nav.InvokeAsync(() => { });
            nav.Eventually(() => nav.ClickWhenReady($".range-pop button.rd[data-day='{day:yyyy-MM-dd}']"));
        }
        nav.Eventually(() => Assert.Equal((day, day), (Saved().ChangedFrom, Saved().ChangedTo)));
        board.Eventually(() => Assert.Single(board.FindAll("article.card")));   // the whole day: 123's event at 10:00 is in
        Assert.Equal($"{day:d MMM}", nav.Find("button[aria-label='changed']").TextContent.Trim());

        nav.ClickWhenReady("button[aria-label='changed']");
        nav.Eventually(() => Assert.False(From().HasAttribute("disabled") || To().HasAttribute("disabled")));   // a day is picked: the times open up
        await nav.InvokeAsync(() => { });
        nav.Eventually(() => From().Change("10:30"));   // after the event
        nav.Eventually(() => Assert.Equal(new TimeOnly(10, 30), Saved().ChangedFromTime));
        nav.Eventually(() => Assert.Equal($"{day:d MMM} from 10:30", nav.Find("button[aria-label='changed']").TextContent.Trim()));
        await nav.InvokeAsync(() => { });
        nav.Eventually(() => To().Change("18:00"));
        nav.Eventually(() => Assert.Equal((new TimeOnly(10, 30), new TimeOnly(18, 0)), (Saved().ChangedFromTime, Saved().ChangedToTime)));   // remembered per board
        nav.Eventually(() => Assert.Equal($"{day:d MMM} 10:30 – 18:00", nav.Find("button[aria-label='changed']").TextContent.Trim()));
        Assert.Equal($"changed {day:d MMM} 10:30 – 18:00", nav.Find(".legend .lg.head").TextContent.Trim());
        nav.Eventually(() => Assert.Equal($"0 of {shown} shown", nav.Find(".legend .lg.count").TextContent.Trim()));
        board.Eventually(() => Assert.Empty(board.FindAll("article.card")));   // the event is before the window

        await nav.InvokeAsync(() => { });
        nav.Eventually(() => From().Change("09:00"));   // before it: 123 is back
        nav.Eventually(() => Assert.Equal(new TimeOnly(9, 0), Saved().ChangedFromTime));
        board.Eventually(() => Assert.Single(board.FindAll("article.card")));
        nav.Eventually(() => Assert.Equal($"1 of {shown} shown", nav.Find(".legend .lg.count").TextContent.Trim()));

        await nav.InvokeAsync(() => { });
        nav.ClickWhenReady(".range-pop button.range-clear");   // Show all
        nav.Eventually(() => Assert.Equal((null, null, null, null), (Saved().ChangedFrom, Saved().ChangedTo, Saved().ChangedFromTime, Saved().ChangedToTime)));
        nav.Eventually(() => Assert.Contains("any time", nav.Find("button[aria-label='changed']").TextContent));
        Assert.Empty(nav.FindAll(".legend .lg.head"));
        board.Eventually(() => Assert.Equal(shown, board.FindAll("article.card").Count));
    }

    // ── menu patch ──

    [Fact(DisplayName = "The side menu has no checkboxes: filters and options are pills, filled when on and underlined when off")]
    public async Task MenuHasPillsNotCheckboxes()
    {
        await using BunitContext ctx = NewContext();
        IRenderedComponent<BoardPage> board = ctx.Render<BoardPage>();
        nav.ClickWhenReady("button.nav-toggle");

        Assert.Empty(nav.FindAll("input[type=checkbox]"));
        Assert.Empty(nav.FindAll("label.check"));
        IElement compact = nav.Find("button.nav-pill[data-opt=compact]");
        Assert.Equal("compact cards", compact.TextContent.Trim());
        Assert.Equal("false", compact.GetAttribute("aria-pressed"));
        Assert.DoesNotContain("is-sel", compact.ClassList);

        nav.Eventually(() => compact.Click());   // a real button: a click turns it on, and the board follows
        compact = nav.Find("button.nav-pill[data-opt=compact]");
        Assert.Equal("true", compact.GetAttribute("aria-pressed"));
        Assert.Contains("is-sel", compact.ClassList);
        board.Eventually(() => Assert.NotEmpty(board.FindAll("article.card.collapsed")));

        // the worklog's filters are pills too
        ctx.Services.GetRequiredService<SettingsStore>().Update(s => s with { Split = false });
        ctx.Services.GetRequiredService<NavigationManager>().NavigateTo("item/123");
        ctx.Services.GetRequiredService<UiService>().OpenWorklog(new WorklogActions(() => { }, () => { }, () => { }, () => { }, () => { }, () => { }));
        nav.Eventually(() => Assert.NotEmpty(nav.FindAll(".nav-worklog button.nav-pill")));
        Assert.Empty(nav.FindAll("input[type=checkbox]"));
        nav.FireWhenReady(".nav-worklog button.nav-pill", e => e.Click(), all => all[0]);   // every kind was on: only Notes stays filled, the rest are plain
        IElement notes = nav.FindAll(".nav-worklog button.nav-pill")[0];
        Assert.Equal("true", notes.GetAttribute("aria-pressed"));
        Assert.Contains("is-sel", notes.ClassList);
        Assert.Equal("false", nav.FindAll(".nav-worklog button.nav-pill")[1].GetAttribute("aria-pressed"));
        Assert.DoesNotContain("is-sel", nav.FindAll(".nav-worklog button.nav-pill")[1].ClassList);
    }

    [Fact(DisplayName = "Sort by is the first field under Organise, and show all hidden sits under the Hidden header")]
    public async Task SortFirstAndShowAllHiddenUnderHidden()
    {
        await using BunitContext ctx = NewContext();
        WorkItemsApi.Hide(107);
        ctx.Services.GetRequiredService<Workspace>().Notify();
        IRenderedComponent<BoardPage> board = ctx.Render<BoardPage>();
        nav.ClickWhenReady("button.nav-toggle");

        string[] fields = [.. nav.FindAll(".organise .field > label").Select(l => l.TextContent.Trim())];
        Assert.Equal(["sort by", "columns", "rows", "colour"], fields);

        // the option is under the Hidden header, named for what it does, and is not among the display switches
        IElement hidden = nav.Find(".nav-hidden");
        Assert.Equal("Hidden", hidden.QuerySelector(".nav-h")!.TextContent.Trim());
        IElement show = hidden.QuerySelector("button#f-hidden")!;
        Assert.Equal("show all hidden", show.TextContent.Trim());
        Assert.Single(nav.FindAll("button#f-hidden"));
        Assert.Equal(3, board.FindAll("article.card").Count);

        nav.Eventually(() => show.Click());
        board.Eventually(() => Assert.Equal(4, board.FindAll("article.card").Count));
        Assert.Null(nav.Find(".nav-hidden button#f-hidden").GetAttribute("aria-pressed"));   // an action, not a switch
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
        nav.ClickWhenReady("button.nav-toggle");

        void Is(string field, string look)
        {
            IElement dd = NavDropdown(field);
            Assert.Contains(look, dd.ClassList);
            Assert.DoesNotContain(look == "is-on" ? "is-off" : "is-on", dd.ClassList);
        }
        nav.Eventually(() => Is("groups", "is-off"));
        Is("subgroups", "is-on");
        Is("colour", "is-off");
        Is("contributor", "is-off");
        Is("period", "is-off");
        Is("sort by", "is-on");   // a sort order is always a choice: nothing in it is neutral

        settings.SaveBoard(repo.Root, settings.Board(repo.Root) with { ListGroupBy = "label", ColorBy = "priority", Period = "overdue", Who = "Alice" });
        nav.Eventually(() => Is("groups", "is-on"));
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
        nav.ClickWhenReady("button.nav-toggle");
        IElement Btn() => nav.Find(".field.changed .dates-btn");
        Assert.Equal("any time", Btn().TextContent.Trim());
        Assert.Contains("is-off", Btn().ClassList);
        Assert.DoesNotContain("is-on", Btn().ClassList);

        settings.SaveBoard(repo.Root, settings.Board(repo.Root) with { ChangedFrom = new DateOnly(2026, 11, 2), ChangedTo = new DateOnly(2026, 11, 8), ChangedFromTime = new TimeOnly(9, 30) });
        nav.Eventually(() => Assert.Contains("is-on", Btn().ClassList));
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
        board.Eventually(() => Assert.Equal(2, board.FindAll(".proj").Count));
        nav.ClickWhenReady("button.nav-toggle");
        IElement Switch() => nav.Find("button#f-hidden");
        Assert.Equal("show all hidden", Switch().TextContent.Trim());

        // a hidden column and a hidden project
        board.ClickWhenReady("header.col-h", "Blocked");
        board.FireWhenReady(".menu-item", e => e.Click(), all => all.Single(x => x.TextContent.Trim() == "Hide column"));
        settings.ToggleProjectHidden(repo2.Root);
        board.Eventually(() => Assert.Single(board.FindAll(".proj")));
        Assert.DoesNotContain("Blocked", Columns(board));
        Assert.Equal("show all hidden", Switch().TextContent.Trim());   // the label is the same in every state

        nav.Eventually(() => Switch().Click());
        board.Eventually(() => Assert.Equal(2, board.FindAll(".proj").Count));
        Assert.Contains("Blocked", Columns(board));
        Assert.Equal("show all hidden", Switch().TextContent.Trim());
        Assert.Empty(settings.Board(repo.Root).Hidden ?? []);   // nothing is kept in view by a switch: the list is empty

        // a hidden row
        settings.SaveBoard(repo.Root, settings.Board(repo.Root) with { Swimlane = "label", Hidden = [.. settings.Board(repo.Root).Hidden ?? [], "row:label:bug"] });
        List<string> Rows() => board.FindAll(".swim-h").Select(h => h.GetAttribute("data-swim")!).ToList();
        board.Eventually(() => Assert.DoesNotContain("bug", Rows()));
        nav.Eventually(() => Switch().Click());
        board.Eventually(() => Assert.Contains("bug", Rows()));

        // the list: a hidden grid column
        settings.SaveBoard(repo.Root, settings.Board(repo.Root) with { Swimlane = "none", View = "list", Hidden = [.. settings.Board(repo.Root).Hidden ?? [], BoardModel.ListColumnKey("Progress")] });
        List<string> Heads() => board.FindAll("thead th .th-l").Select(h => h.TextContent.Trim()).ToList();
        board.Eventually(() => Assert.DoesNotContain("Progress", Heads()));
        nav.Eventually(() => Switch().Click());
        board.Eventually(() => Assert.Contains("Progress", Heads()));
    }

    /// <summary>Two projects, each with its work item 107 "deleted" (hidden in the app), so show all hidden has an item of its own to bring back in each.</summary>
    private (Workspace Ws, SettingsStore Settings) HiddenSetup(BunitContext ctx)
    {
        TwoProjects(ctx);
        Workspace ws = ctx.Services.GetRequiredService<Workspace>();
        foreach (string root in new[] { repo.Root, repo2.Root }) Assert.True(ws.In(root, () => WorkItemsApi.Hide(107)).Item1);
        ws.Notify();
        return (ws, ctx.Services.GetRequiredService<SettingsStore>());
    }

    private static List<string> CardTitles(IRenderedComponent<BoardPage> board, string project) =>
        [.. board.FindAll($".proj[data-proj='{project}'] article.card .band").Select(b => b.TextContent.Trim()).Order()];

    [Fact(DisplayName = "After show all hidden every work item of a hidden project renders as a card and the column count equals the cards shown")]
    public async Task ShowAllHiddenRendersEveryCardAndCountsMatch()
    {
        await using BunitContext ctx = NewContext();
        (Workspace ws, SettingsStore settings) = HiddenSetup(ctx);
        settings.SaveBoard(repo.Root, settings.Board(repo.Root) with { Hidden = ["status:Planned", "status:Confirmed"] });   // a hidden column
        settings.ToggleProjectHidden(repo2.Root);                                                                          // a hidden project
        IRenderedComponent<BoardPage> board = ctx.Render<BoardPage>();
        nav.ClickWhenReady("button.nav-toggle");
        nav.ClickWhenReady("button#f-hidden");
        board.Eventually(() => Assert.Equal(2, board.FindAll(".proj").Count));

        foreach (TestRepo r in new[] { repo, repo2 })
        {
            List<string> every = ws.In(r.Root, () => WorkItemsApi.GetAll(includeHidden: true).Select(i => i.Title).Order().ToList());
            Assert.Contains(every, t => t.Length > 0);
            board.Eventually(() => Assert.Equal(every, CardTitles(board, NameOf(r))));   // the flagged item, the hidden column's and the hidden project's included
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

    [Fact(DisplayName = "A collapsed project stays collapsed after show all hidden is pressed")]
    public async Task CollapsedProjectStaysCollapsedWhileShowAllHiddenIsOn()
    {
        await using BunitContext ctx = NewContext();
        (Workspace ws, SettingsStore settings) = HiddenSetup(ctx);
        settings.ToggleProjectCollapsed(repo2.Root);
        IRenderedComponent<BoardPage> board = ctx.Render<BoardPage>();
        board.Eventually(() => Assert.Equal(2, board.FindAll(".proj").Count));
        Assert.Empty(CardTitles(board, NameOf(repo2)));   // folded: its heading only
        Assert.NotEmpty(CardTitles(board, NameOf(repo)));

        nav.ClickWhenReady("button.nav-toggle");
        nav.ClickWhenReady("button#f-hidden");
        board.Eventually(() => Assert.Equal(ws.In(repo.Root, () => WorkItemsApi.GetAll(includeHidden: true).Select(i => i.Title).Order().ToList()), CardTitles(board, NameOf(repo))));   // the open project got its flagged item back
        Assert.Empty(CardTitles(board, NameOf(repo2)));   // the folded one did not unfold
        Assert.Equal("false", board.Find($".proj[data-proj='{NameOf(repo2)}'] .proj-h .chev").GetAttribute("aria-expanded"));
        Assert.True(settings.ProjectCollapsed(repo2.Root));   // and the setting is untouched
    }

    [Fact(DisplayName = "In the list a collapsed project's rows stay folded after show all hidden is pressed")]
    public async Task CollapsedProjectKeepsItsRowsFoldedInTheListWhileShowAllHiddenIsOn()
    {
        await using BunitContext ctx = NewContext();
        (Workspace ws, SettingsStore settings) = HiddenSetup(ctx);
        settings.ToggleProjectCollapsed(repo2.Root);
        IRenderedComponent<BoardPage> board = ctx.Render<BoardPage>();
        ShowList();
        board.Eventually(() => Assert.Equal(2, board.FindAll("tr[data-group^='proj:']").Count));
        int others = board.FindAll("tr[data-id]").Count;

        nav.ClickWhenReady("button.nav-toggle");
        nav.ClickWhenReady("button#f-hidden");
        board.Eventually(() => Assert.True(board.FindAll("tr[data-id]").Count > others));   // the open project's flagged item is back
        Assert.Equal(ws.In(repo.Root, () => WorkItemsApi.GetAll(includeHidden: true).Count), board.FindAll("tr[data-id]").Count);   // the folded project's rows are not
        Assert.True(settings.ProjectCollapsed(repo2.Root));
    }

    [Fact(DisplayName = "A collapsed project's work items count in no column header, before and after show all hidden is pressed")]
    public async Task CollapsedProjectCountsInNoColumnHeader()
    {
        await using BunitContext ctx = NewContext();
        (Workspace ws, SettingsStore settings) = HiddenSetup(ctx);
        settings.ToggleProjectCollapsed(repo2.Root);
        IRenderedComponent<BoardPage> board = ctx.Render<BoardPage>();
        board.Eventually(() => Assert.Equal(2, board.FindAll(".proj").Count));

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

        nav.ClickWhenReady("button.nav-toggle");
        nav.ClickWhenReady("button#f-hidden");
        board.Eventually(() => Assert.Equal(ws.In(repo.Root, () => WorkItemsApi.GetAll(includeHidden: true).Count), board.FindAll(".proj article.card").Count));
        HeadersCountTheCardsDrawn(showAll: true);

        settings.ToggleProjectCollapsed(repo2.Root);   // unfolded: its items count again
        board.Eventually(() => Assert.Equal(ws.In(repo.Root, () => WorkItemsApi.GetAll(includeHidden: true).Count) + ws.In(repo2.Root, () => WorkItemsApi.GetAll(includeHidden: true).Count), board.FindAll(".proj article.card").Count));
        foreach (IElement head in board.FindAll(".head-row section.col"))
            Assert.Equal(board.FindAll($".proj section.col[data-key='{head.GetAttribute("data-key")}'] article.card").Count, int.Parse(head.QuerySelector(".count")!.TextContent));
    }

    [Fact(DisplayName = "A collapsed project's work items count in no side menu Hidden count")]
    public async Task CollapsedProjectCountsInNoSideMenuHiddenCount()
    {
        await using BunitContext ctx = NewContext();
        (_, SettingsStore settings) = HiddenSetup(ctx);
        settings.SaveBoard(repo.Root, settings.Board(repo.Root) with { Hidden = ["status:In Progress"] });
        IRenderedComponent<BoardPage> board = ctx.Render<BoardPage>();
        board.Eventually(() => Assert.Equal(2, board.FindAll(".proj").Count));
        nav.ClickWhenReady("button.nav-toggle");
        int Held() => int.Parse(nav.Find(".nav-hidden .grp-row .count").TextContent.Trim());
        nav.Eventually(() => Assert.True(Held() > 0));   // the open project's In Progress items

        settings.ToggleProjectCollapsed(repo.Root);
        nav.Eventually(() => Assert.Equal(0, Held()));   // folded: counted nowhere

        settings.ToggleProjectCollapsed(repo.Root);
        nav.Eventually(() => Assert.True(Held() > 0));
    }

    [Fact(DisplayName = "The collapsed menu has a show all hidden button that is the same one action")]
    public async Task CollapsedMenuShowAllHidden()
    {
        await using BunitContext ctx = NewContext();
        WorkItemsApi.Hide(107);
        ctx.Services.GetRequiredService<Workspace>().Notify();
        IRenderedComponent<BoardPage> board = ctx.Render<BoardPage>();
        IElement Btn() => nav!.Find("button.hidden-btn");
        Assert.Equal("Show all hidden", Btn().GetAttribute("aria-label"));
        Assert.Null(Btn().GetAttribute("aria-pressed"));   // an action, not a switch
        Assert.NotNull(Btn().QuerySelector("svg"));
        Assert.Equal(3, board.FindAll("article.card").Count);

        nav.Eventually(() => Btn().Click());
        board.Eventually(() => Assert.Equal(4, board.FindAll("article.card").Count));
        Assert.False(WorkItemsApi.Get(107)!.Hidden);

        nav.ClickWhenReady("button.nav-toggle");   // expanded, the same action is the pill under Hidden
        Assert.Empty(nav.FindAll("button.hidden-btn"));
        Assert.Single(nav.FindAll("button#f-hidden"));
    }

    [Fact(DisplayName = "The Worklog block has one Add entry button whose menu offers note, decision, bug, subtask and the three links")]
    public async Task WorklogHasOneAddEntryMenu()
    {
        await using BunitContext ctx = NewContext();
        ctx.Services.GetRequiredService<SettingsStore>().Update(s => s with { Split = false });
        ctx.Services.GetRequiredService<NavigationManager>().NavigateTo("item/123");
        UiService ui = ctx.Services.GetRequiredService<UiService>();
        nav.ClickWhenReady("button.nav-toggle");
        List<string> done = [];
        ui.OpenWorklog(new WorklogActions(() => done.Add("note"), () => done.Add("decision"), () => done.Add("document"), () => done.Add("test"), () => done.Add("commit"), () => done.Add("subtask"),
            CanLinkTest: () => false, AddBug: () => done.Add("bug")));
        nav.Eventually(() => Assert.NotEmpty(nav.FindAll(".nav-worklog")));

        Assert.Empty(nav.FindAll(".nav-worklog button.nav-act"));
        Assert.Single(nav.FindAll(".nav-worklog button.nav-add"));
        Assert.Empty(nav.FindAll("button[role=menuitem]"));   // closed until clicked
        nav.ClickWhenReady(".nav-worklog button.nav-add");
        IElement[] items = [.. nav.FindAll("button[role=menuitem]")];
        Assert.Equal(["Note", "Decision", "Bug", "Subtask", "Link document", "Link test", "Link commit"], items.Select(i => i.TextContent.Trim()));
        Assert.True(items[5].HasAttribute("disabled"));    // no test results file: the reason stays as the title
        Assert.False(items[6].HasAttribute("disabled"));

        nav.Eventually(() => items.Single(i => i.TextContent.Trim() == "Bug").Click());
        Assert.Equal(["bug"], done);
        nav.ClickWhenReady(".nav-worklog button.nav-add");
        nav.FireWhenReady("button[role=menuitem]", e => e.Click(), all => all.Single(x => x.TextContent.Trim() == "Subtask"));
        Assert.Equal(["bug", "subtask"], done);

        // no bug action wired: no Bug entry
        ui.CloseWorklog(ui.WorklogOpen!);
        ui.OpenWorklog(new WorklogActions(() => { }, () => { }, () => { }, () => { }, () => { }, () => { }));
        nav.Eventually(() => Assert.NotEmpty(nav.FindAll(".nav-worklog")));
        nav.ClickWhenReady(".nav-worklog button.nav-add");
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
        nav.ClickWhenReady("button.nav-toggle");
        Assert.All(first, q => Assert.NotEmpty(nav.FindAll(q)));   // the board has them

        settings.Update(s => s with { Split = false });
        location.NavigateTo("item/123");
        nav.Eventually(() => Assert.All(first, q => Assert.Empty(nav.FindAll(q))));
        Assert.NotEmpty(nav.FindAll("button.nav-toggle"));
        Assert.NotEmpty(nav.FindAll("button[aria-label=Settings]"));

        settings.Update(s => s with { Split = true });   // beside the list
        nav.Eventually(() => Assert.All(first, q => Assert.NotEmpty(nav.FindAll(q))));

        ui.SetNarrowWindow(true);   // too narrow for the split: the item fills the window again
        nav.Eventually(() => Assert.All(first, q => Assert.Empty(nav.FindAll(q))));
        ui.SetNarrowWindow(false);

        location.NavigateTo("");   // and back on the board
        nav.Eventually(() => Assert.All(first, q => Assert.NotEmpty(nav.FindAll(q))));
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
        nav.Eventually(() => toggle.Click());
        toggle = nav.Find("button.nav-toggle");
        Assert.Equal("Collapse menu", toggle.GetAttribute("title"));
        Assert.Equal("true", toggle.GetAttribute("aria-expanded"));
        Assert.Empty(nav.FindAll("nav.side > button.nav-toggle"));
        Assert.NotNull(toggle.ParentElement!.QuerySelector("button.tabs-btn"));
        Assert.Single(nav.FindAll(".nav-row > button.nav-toggle:last-child"));
        Assert.Single(nav.FindAll("nav.side > .nav-row:first-child"));   // that row is the menu's first
        nav.Eventually(() => toggle.Click());
        Assert.Single(nav.FindAll("nav.side > button.nav-toggle:first-child"));   // and back on top of the rail

        // an item filling the window: no first section, so the Worklog header's row has it
        nav.ClickWhenReady("button.nav-toggle");
        settings.Update(s => s with { Split = false });
        location.NavigateTo("item/123");
        ctx.Services.GetRequiredService<UiService>().OpenWorklog(new WorklogActions(() => { }, () => { }, () => { }, () => { }, () => { }, () => { }));
        nav.Eventually(() => Assert.NotEmpty(nav.FindAll(".nav-worklog .nav-h button.nav-toggle")));
        Assert.Single(nav.FindAll("button.nav-toggle"));
        Assert.Equal("Worklog", nav.Find(".nav-worklog .nav-h").TextContent.Trim());
        Assert.Single(nav.FindAll(".nav-worklog > .nav-h:first-child > button.nav-toggle"));
        nav.ClickWhenReady("button.nav-toggle");   // it still collapses
        nav.Eventually(() => Assert.Single(nav.FindAll("nav.side > button.nav-toggle:first-child")));
    }
    // ---- hook-badge chunk ----

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
        hook.WaitForExit();
        Assert.Equal(0, hook.ExitCode);
        return output;
    }

    [Fact(DisplayName = "The edit hook denies direct edits under openspec/changes unless RTM_ALLOW_DIRECT_EDIT is set")]
    public void EditHookDeniesDirectBoardEdits()
    {
        string inChange = """{"tool_name":"Edit","tool_input":{"file_path":"openspec/changes/some-item/proposal.md"}}""";
        string elsewhere = """{"tool_name":"Edit","tool_input":{"file_path":"TaskManager/Components/Pages/Board/BoardPage.razor"}}""";
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

    // ---- menu chunk ----

    private IElement Section(string name) => nav!.Find($"button.nav-sec-h[data-sec='{name}']");
    private IElement SectionBody(string name) => nav!.Find($"#sec-{name}-body");
    private static string Flat(IElement e) => string.Join(' ', e.TextContent.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    [Fact(DisplayName = "The menu's Organise, Filters and Hidden sections are collapsed by default and remember being opened")]
    public async Task MenuSectionsAreCollapsedByDefaultAndRemembered()
    {
        await using BunitContext ctx = NewContext(sectionsOpen: false);
        ctx.Render<BoardPage>();
        nav.ClickWhenReady("button.nav-toggle");

        foreach (string name in new[] { "organise", "filters", "hidden" })
        {
            Assert.Equal("false", Section(name).GetAttribute("aria-expanded"));
            Assert.True(SectionBody(name).HasAttribute("hidden"), name + " body starts folded");
            Assert.Equal("sec-" + name + "-body", Section(name).GetAttribute("aria-controls"));
        }
        Assert.Equal("Organise", Flat(Section("organise").QuerySelector(".nav-sec-t")!));
        Assert.Equal("Filters", Flat(Section("filters").QuerySelector(".nav-sec-t")!));

        nav.Eventually(() => Section("filters").Click());
        Assert.Equal("true", Section("filters").GetAttribute("aria-expanded"));
        Assert.False(SectionBody("filters").HasAttribute("hidden"));
        Assert.True(SectionBody("organise").HasAttribute("hidden"));   // the others stay folded
        Assert.Contains("contributor", SectionBody("filters").TextContent);

        // saved for the user (not per project), so a new menu, even in a new session, opens it again
        Assert.Equal(["filters"], ctx.Services.GetRequiredService<SettingsStore>().Current.OpenMenuSections);
        Assert.Equal(["filters"], new SettingsStore(settingsFile).Current.OpenMenuSections);
        IRenderedComponent<SideNav> second = ctx.Render<SideNav>();   // the menu's expanded state is shared (UiService.MenuExpanded), so it opens expanded
        Assert.Equal("true", second.Find("button.nav-sec-h[data-sec='filters']").GetAttribute("aria-expanded"));
        Assert.Equal("false", second.Find("button.nav-sec-h[data-sec='hidden']").GetAttribute("aria-expanded"));

        nav.Eventually(() => Section("filters").Click());   // and closing it is remembered too
        Assert.Equal("false", Section("filters").GetAttribute("aria-expanded"));
        Assert.Empty(new SettingsStore(settingsFile).Current.OpenMenuSections ?? []);
    }

    [Fact(DisplayName = "A collapsed section's header summarises a filter that is not neutral")]
    public async Task CollapsedSectionHeaderSummarisesActiveFilters()
    {
        await using BunitContext ctx = NewContext(sectionsOpen: false);
        SettingsStore settings = ctx.Services.GetRequiredService<SettingsStore>();
        ctx.Render<BoardPage>();
        nav.ClickWhenReady("button.nav-toggle");

        // neutral: nothing to say in the Filters and Hidden headers
        Assert.Empty(Section("filters").QuerySelectorAll(".nav-sec-sum"));
        Assert.Empty(Section("hidden").QuerySelectorAll(".nav-sec-sum"));
        Assert.Equal("Filters", Flat(Section("filters")));

        settings.SaveBoard(repo.Root, settings.Board(repo.Root) with { Who = "Claude", Period = "week", Unit = "subtasks" });
        nav.Eventually(() => Assert.Equal("Filters · contributor: Claude · period: this week · unit: Subtasks", Flat(Section("filters"))));
        Assert.True(SectionBody("filters").HasAttribute("hidden"));   // still folded: the header carries it
        Assert.Contains("contributor: Claude", Section("filters").GetAttribute("title"));

        settings.SaveBoard(repo.Root, settings.Board(repo.Root) with { Hidden = ["status:Blocked"] });
        nav.Eventually(() => Assert.Equal("Hidden · 1 hidden", Flat(Section("hidden"))));

        settings.SaveBoard(repo.Root, settings.Board(repo.Root) with { Hidden = null, Who = "all", Period = "all", Unit = "tasks" });
        nav.Eventually(() => Assert.Equal("Filters", Flat(Section("filters"))));
        Assert.Equal("Hidden", Flat(Section("hidden")));

        // Organise says how the board is arranged
        Assert.Contains("columns status", Flat(Section("organise")));
    }

    [Fact(DisplayName = "Shift-clicking days in the Worklog block filters the feed to those days and Show all days clears it")]
    public async Task ShiftClickingDaysFiltersTheFeed()
    {
        await using BunitContext ctx = NewContext();
        ctx.Services.GetRequiredService<SettingsStore>().Update(s => s with { Split = false });
        ctx.Services.GetRequiredService<NavigationManager>().NavigateTo("item/123");
        nav.ClickWhenReady("button.nav-toggle");
        IRenderedComponent<Worklog> log = OpenWorklog(ctx);
        nav.Eventually(() => Assert.NotEmpty(nav.FindAll(".nav-worklog .nav-day")));

        string[] feed = [.. log.FindAll(".wk-day[data-day]").Select(d => d.GetAttribute("data-day")!)];
        Assert.True(feed.Length >= 2);
        IElement[] rows() => [.. nav.FindAll(".nav-worklog button.nav-day")];
        string[] days() => [.. log.FindAll(".wk-day[data-day]").Select(d => d.GetAttribute("data-day")!)];
        string[] counts = [.. rows().Select(b => b.QuerySelector(".count")!.TextContent.Trim())];
        Assert.Empty(nav.FindAll(".nav-worklog .nav-days-all"));

        // a plain click still only jumps
        nav.Eventually(() => rows()[^1].Click());
        log.Eventually(() => Assert.NotEmpty(ctx.JSInterop.Invocations["rtm.scrollToDay"]));
        Assert.Equal(feed, days());
        Assert.Empty(nav.FindAll(".nav-worklog .nav-day.is-sel"));

        // Shift-click selects: the day turns into a pill and the feed shows only it
        int last = feed.Length - 1;
        nav.Eventually(() => rows()[last].Click(new MouseEventArgs { ShiftKey = true }));
        log.Eventually(() => Assert.Equal([feed[last]], days()));
        Assert.Single(nav.FindAll(".nav-worklog .nav-day.is-sel"));
        Assert.Contains("is-sel", rows()[last].ClassList);
        Assert.Equal(counts, rows().Select(b => b.QuerySelector(".count")!.TextContent.Trim()));   // the list keeps every day and its count
        Assert.Empty(log.FindAll(".wk-earlier"));

        // a second day joins; Shift-clicking a selected one takes it out again
        nav.Eventually(() => rows()[0].Click(new MouseEventArgs { ShiftKey = true }));
        log.Eventually(() => Assert.Equal([feed[0], feed[last]], days()));
        Assert.Equal(2, nav.FindAll(".nav-worklog .nav-day.is-sel").Count);
        nav.Eventually(() => rows()[0].Click(new MouseEventArgs { ShiftKey = true }));
        log.Eventually(() => Assert.Equal([feed[last]], days()));

        // Show all days clears it
        nav.Eventually(() => rows()[0].Click(new MouseEventArgs { ShiftKey = true }));
        log.Eventually(() => Assert.Equal(2, days().Length));
        nav.ClickWhenReady(".nav-worklog .nav-days-all");
        log.Eventually(() => Assert.Equal(feed, days()));
        Assert.Empty(nav.FindAll(".nav-worklog .nav-day.is-sel"));
        Assert.Empty(nav.FindAll(".nav-worklog .nav-days-all"));
    }

    [Fact(DisplayName = "The collapsed rail has a New work item button, and the expanded one is red while the view switcher is plain")]
    public async Task RailHasNewWorkItemAndViewSwitcherIsPlain()
    {
        await using BunitContext ctx = NewContext();
        ctx.Render<BoardPage>();

        // collapsed: a + right after the view button
        IElement Top(IElement e) { while (e.ParentElement!.TagName != "NAV") e = e.ParentElement!; return e; }
        IElement layout = nav.Find("button[aria-label='Layout']");
        IElement add = nav.Find("button[aria-label='New work item']");
        Assert.Contains("nav-new", add.ClassList);
        Assert.NotNull(add.QuerySelector("svg"));
        Assert.NotNull(layout.QuerySelector("svg"));   // the view's own icon
        Assert.Equal(Top(add).OuterHtml, Top(layout).NextElementSibling!.OuterHtml);

        // expanded: the view switcher is a plain button (not the yellow current-choice pill), named for the view; New work item is the red one
        nav.ClickWhenReady("button.nav-toggle");
        layout = nav.Find("button[aria-label='Layout']");
        Assert.DoesNotContain("is-sel", layout.ClassList);
        Assert.Contains("Board", Flat(layout));
        add = nav.Find("button[aria-label='New work item']");
        Assert.Contains("nav-new", add.ClassList);
        Assert.Contains("New work item", Flat(add));

        string css = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "wwwroot", "css", "app.css"));
        Assert.Matches(@"\.nav-btn\.nav-new\s*\{[^}]*background:\s*var\(--alert\)[^}]*color:\s*var\(--on-alert\)", css);   // filled red, not outlined (the geometry test checks the details)

        // the + does what the expanded button does: it creates the item and opens it
        nav.Eventually(() => add.Click());
        nav.Eventually(() => Assert.Contains("item/", ctx.Services.GetRequiredService<NavigationManager>().Uri));
    }

    // ---- list-dates chunk ----

    [Fact(DisplayName = "The list always has a Status column, whatever the grouping")]
    public async Task ListAlwaysHasAStatusColumn()
    {
        await using BunitContext ctx = NewContext();
        SettingsStore settings = ctx.Services.GetRequiredService<SettingsStore>();
        // a grid layout saved while the column was dropped by a grouping: Radzen would keep it hidden for good
        string savedHidden = """{"Columns":[{"UniqueID":"x","Property":"Status","Visible":false}],"Groups":[],"CurrentPage":0,"PageSize":10}""";
        settings.SaveBoard(repo.Root, settings.Board(repo.Root) with { View = "list", ListSettings = savedHidden });
        IRenderedComponent<BoardPage> board = ctx.Render<BoardPage>();
        List<string> Heads() => board.FindAll("thead th .th-l").Select(h => h.TextContent.Trim()).ToList();
        board.Eventually(() => Assert.Contains("Label", Heads()));
        Assert.Contains("Status", Heads());   // flat, groups none

        foreach (string by in new[] { "status", "label", "feature", "priority", "contributor" })
        {
            settings.SaveBoard(repo.Root, settings.Board(repo.Root) with { ListGroupBy = by });
            board.Eventually(() => Assert.NotEmpty(board.FindAll("tr[data-group]")));
            Assert.Contains("Status", Heads());   // grouped by status too: the column stays
        }
        settings.SaveBoard(repo.Root, settings.Board(repo.Root) with { ListGroupBy = "none", ListSwimlane = "status" });
        board.Eventually(() => Assert.NotEmpty(board.FindAll("tr[data-group^='swim:']")));
        Assert.Contains("Status", Heads());

        // only the user's own hide takes it away
        settings.SaveBoard(repo.Root, settings.Board(repo.Root) with { ListGroupBy = "status", ListSwimlane = "none", Hidden = [BoardModel.ListColumnKey("Status")] });
        board.Eventually(() => Assert.DoesNotContain("Status", Heads()));
    }

    [Fact(DisplayName = "The colour-by value tints the whole list row and the text stays readable")]
    public async Task ColourByTintsTheWholeListRow()
    {
        await using BunitContext ctx = NewContext();
        SettingsStore settings = ctx.Services.GetRequiredService<SettingsStore>();
        IRenderedComponent<BoardPage> board = ctx.Render<BoardPage>();
        ShowList();
        board.Eventually(() => Assert.NotEmpty(board.FindAll("tr[data-id]")));
        Assert.All(board.FindAll("tr[data-id]"), r => { Assert.NotNull(r.GetAttribute("data-tint")); Assert.Contains("--band:var(--", r.GetAttribute("style")); });

        settings.SaveBoard(repo.Root, settings.Board(repo.Root) with { ColorBy = "none" });
        board.Eventually(() => Assert.All(board.FindAll("tr[data-id]"), r => Assert.Null(r.GetAttribute("data-tint"))));   // colour none: no tint at all

        // css is not evaluated here: read the rules. The tint is a translucent wash over every cell of the row (the text colour is left alone,
        // so it reads on the light and the dark theme) and the old stripe down the first cell is gone
        string css = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "wwwroot", "css", "app.css")).Replace("\r\n", "\n");
        System.Text.RegularExpressions.Match rule = System.Text.RegularExpressions.Regex.Match(css, @"\.tbl-wrap tbody tr\[data-tint\] > td \{(?<body>[^}]*)\}");
        Assert.True(rule.Success, "no tint rule on the row's cells");
        Assert.Matches(@"background-image: linear-gradient\(color-mix\(in srgb, var\(--band\) \d+%, transparent\)", rule.Groups["body"].Value);
        Assert.DoesNotContain("color:", rule.Groups["body"].Value.Replace("background-color", "").Replace("color-mix", ""));
        Assert.DoesNotContain("inset 4px 0 0 var(--band", css);
        // the selected row's ring and the open row's wash stay the rows' own (outline and shadow on the tr, not overwritten by the cells' tint)
        Assert.Contains("outline-offset:-4px", ctx.Render<RowMarks>(p => p.Add(m => m.Marks, [new RowMarks.RowMark("data-row", "1:0:5", RowMarks.Mark.Selected)])).Find("style").InnerHtml);   // written by RowMarks (the rows carry no mark)
        Assert.DoesNotContain("outline", rule.Groups["body"].Value);
        Assert.DoesNotContain("box-shadow", rule.Groups["body"].Value);
    }

    [Fact(DisplayName = "Arrow keys move the selected list rows within their group and into the next group at the edge")]
    public async Task ArrowKeysMoveListRowsAcrossGroups()
    {
        await using BunitContext ctx = NewContext();
        Workspace workspace = ctx.Services.GetRequiredService<Workspace>();
        Assert.True(workspace.CreateItem("Planned").Ok);
        Assert.True(workspace.CreateItem("Planned").Ok);
        Assert.True(workspace.CreateItem("Confirmed").Ok);
        IRenderedComponent<BoardPage> board = ctx.Render<BoardPage>();
        ShowList();   // grouped by status: Backlog, Planned, Confirmed, ...
        board.Eventually(() => Assert.True(GroupRows(board, "Planned").Count >= 3 && GroupRows(board, "Confirmed").Count == 1));
        List<int> Ids(string group) => [.. GroupRows(board, group).Select(r => int.Parse(r.GetAttribute("data-id")!))];
        void Pick(int id) => board.FireWhenReady($"tr[data-id='{id}'] td.num", e => e.Click(new MouseEventArgs { ShiftKey = true }));
        async Task Press(string key) { await board.InvokeAsync(() => { }); board.KeyDownWhenReady(".tbl-wrap", key); }

        // in the middle of a group a key steps past the neighbour and stays in the group
        List<int> planned = Ids("Planned");
        Pick(planned[1]);
        await Press("ArrowUp");
        board.Eventually(() => Assert.Equal([planned[1], planned[0], .. planned.Skip(2)], Ids("Planned")));
        Pick(planned[1]);   // unselect

        // at the top edge Up puts the row at the end of the group above: the same as dropping it there
        int top = Ids("Planned")[0];
        List<int> backlog = Ids("Backlog");
        Pick(top);
        await Press("ArrowUp");
        board.Eventually(() => Assert.Equal("Backlog", WorkItemsApi.Get(top)!.Status));
        board.Eventually(() => Assert.Equal([.. backlog, top], Ids("Backlog")));
        Pick(top);

        // at the bottom edge Down puts the rows (several selected, kept in their order) at the start of the group below
        List<int> bottom = [.. Ids("Planned").TakeLast(2)];
        List<int> confirmed = Ids("Confirmed");
        foreach (int id in bottom) Pick(id);
        await Press("ArrowDown");
        board.Eventually(() => Assert.All(bottom, id => Assert.Equal("Confirmed", WorkItemsApi.Get(id)!.Status)));
        board.Eventually(() => Assert.Equal([.. bottom, .. confirmed], Ids("Confirmed")));

        // the first group has nothing above it: Up changes nothing
        foreach (int id in bottom) Pick(id);
        int first = Ids("Backlog")[0];
        Pick(first);
        await Press("ArrowUp");
        await board.InvokeAsync(() => { });
        Assert.Equal("Backlog", WorkItemsApi.Get(first)!.Status);
    }

    [Fact(DisplayName = "An expanded card shows its due date and the list shows updated, started and due apart")]
    public async Task ExpandedCardShowsDueAndListShowsThreeDates()
    {
        await using BunitContext ctx = NewContext();
        DateOnly today = DateOnly.FromDateTime(DateTime.Today);
        WorkItem item = WorkItemsApi.Get(123)! with { Due = today.AddDays(5), Archived = false };
        IRenderedComponent<WorkItemCard> card = ctx.Render<WorkItemCard>(p => p.Add(c => c.Item, item).Add(c => c.Options, new BoardOptions()));
        AngleSharp.Dom.IElement due = card.Find(".tags .tag.due");
        Assert.Equal("due " + today.AddDays(5).ToString("d MMM"), due.TextContent.Trim());
        Assert.DoesNotContain("overdue", due.ClassName);

        // past: the overdue peach, the colour the board gives an overdue item by due date
        IRenderedComponent<WorkItemCard> late = ctx.Render<WorkItemCard>(p => p.Add(c => c.Item, item with { Due = today.AddDays(-2) }).Add(c => c.Options, new BoardOptions()));
        Assert.Contains("overdue hot", late.Find(".tags .tag.due").ClassName);
        Assert.Equal("--peach", BoardModel.ColourOf(item with { Due = today.AddDays(-2) }, "due", today));
        Assert.Contains(".tag.hot { background: var(--peach)", File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "wwwroot", "css", "app.css")));

        // no date, no tag; a compact card shows no tags at all
        Assert.Empty(ctx.Render<WorkItemCard>(p => p.Add(c => c.Item, item with { Due = null }).Add(c => c.Options, new BoardOptions())).FindAll(".tag.due"));
        Assert.Empty(ctx.Render<WorkItemCard>(p => p.Add(c => c.Item, item).Add(c => c.Options, new BoardOptions(Compact: true))).FindAll(".tag.due"));

        // the list: Updated, Started and Due are three columns of their own, by default, in that order, beside In status since
        IRenderedComponent<BoardPage> board = ctx.Render<BoardPage>();
        OpenFreshList();
        List<string> Heads() => board.FindAll("thead th .th-l").Select(h => h.TextContent.Trim()).ToList();
        board.Eventually(() => Assert.Contains("Updated", Heads()));
        List<string> heads = Heads();
        Assert.Equal(["Updated", "Started", "Due"], heads.Where(h => h is "Updated" or "Started" or "Due"));
        Assert.Contains("In status since", heads);
    }

    // ---- examine chunk ----

    private static AngleSharp.Dom.IElement CardOf(IRenderedComponent<BoardPage> board, int id) => board.FindAll("article.card").First(c => c.GetAttribute("data-id") == id.ToString());
    private string NavUri(BunitContext ctx) => ctx.Services.GetRequiredService<NavigationManager>().Uri;
    private string Line(string name) => nav!.Find($".examine [data-line='{name}'] b").TextContent.Trim();

    [Fact(DisplayName = "With the menu expanded one click examines a card and shows its metadata and counts in the menu, the second click opens it")]
    public async Task OneClickExaminesTheSecondOpens()
    {
        await using BunitContext ctx = NewContext();
        int notesBefore = NotesApi.GetForWorkItem(123).Count, bugsBefore = NotesApi.GetForWorkItem(123).Count(n => n.IsBug);
        NotesApi.Create(123, "Tom", new DateTime(2026, 11, 1, 9, 0, 0), "bug");
        NotesApi.Create(123, "Tom", new DateTime(2026, 11, 1, 9, 5, 0), null);
        (_, _, int blocker) = WorkItemsApi.Create("Blocker");
        (_, _, int waiter) = WorkItemsApi.Create("Waiter");
        WorkItemsApi.SetDependencies(123, [blocker]);
        WorkItemsApi.SetDependencies(waiter, [123]);
        WorkItemsApi.SetDue(123, new DateOnly(2026, 12, 24));
        IRenderedComponent<BoardPage> board = ctx.Render<BoardPage>();
        nav.ClickWhenReady("button.nav-toggle");
        Assert.Empty(nav.FindAll(".examine"));

        board.Eventually(() => CardOf(board, 123).Click());

        board.Eventually(() => Assert.NotEmpty(nav.FindAll(".examine")));
        Assert.DoesNotContain("/item/", NavUri(ctx));   // examined, not opened
        string panel = nav.Find(".examine").TextContent;
        Assert.Contains("#wi123", panel);
        Assert.Contains("Health-check endpoint", panel);
        Assert.Contains("Security", panel);
        Assert.Contains("In Progress", panel);
        Assert.Contains("Artyom", panel);
        Assert.Contains("24 Dec", panel);   // due
        Assert.Equal((notesBefore + 2).ToString(), Line("notes"));
        Assert.Equal((bugsBefore + 1).ToString(), Line("bugs"));
        Assert.Equal("2", Line("subtasks"));   // the fixture has two of four subtasks open
        Assert.Equal("1", Line("waiting"));    // the item that waits on it
        Assert.Equal("1", Line("needs"));      // the item it waits on
        Assert.NotNull(CardOf(board, 123).GetAttribute("data-examined"));

        board.Eventually(() => CardOf(board, 123).Click());
        Assert.EndsWith("/item/123", NavUri(ctx));
        board.Eventually(() => Assert.Empty(nav.FindAll(".examine")));   // navigating away lets go
    }

    [Fact(DisplayName = "An examined item has the yellow mark, its dependents red and its dependencies blue, and only one item is examined at a time")]
    public async Task ExaminedItemAndItsNeighboursAreMarked()
    {
        await using BunitContext ctx = NewContext();
        (_, _, int blocker) = WorkItemsApi.Create("Blocker");
        (_, _, int waiter) = WorkItemsApi.Create("Waiter");
        WorkItemsApi.SetDependencies(123, [blocker]);
        WorkItemsApi.SetDependencies(waiter, [123]);
        IRenderedComponent<BoardPage> board = ctx.Render<BoardPage>();
        nav.ClickWhenReady("button.nav-toggle");

        board.Eventually(() => CardOf(board, 123).Click());
        board.Eventually(() => Assert.Contains("ex-on", CardOf(board, 123).ClassName));
        Assert.Contains("ex-waits", CardOf(board, waiter).ClassName);    // waits on the examined item: red
        Assert.Contains("ex-needs", CardOf(board, blocker).ClassName);   // the examined item waits on it: blue
        Assert.DoesNotContain("ex-", CardOf(board, 107).ClassName);

        board.Eventually(() => CardOf(board, 107).Click());   // another item: it is examined instead, nothing else is
        board.Eventually(() => Assert.Contains("ex-on", CardOf(board, 107).ClassName));
        Assert.Single(board.FindAll("article.card.ex-on"));
        Assert.Empty(board.FindAll("article.card.ex-waits"));
        Assert.Empty(board.FindAll("article.card.ex-needs"));
        Assert.DoesNotContain("/item/", NavUri(ctx));
        Assert.Contains("#wi107", nav.Find(".examine").TextContent);
    }

    [Fact(DisplayName = "Clicking outside any work item clears the examined state and the menu's blocks come back")]
    public async Task ClickingElsewhereLetsGo()
    {
        await using BunitContext ctx = NewContext();
        IRenderedComponent<BoardPage> board = ctx.Render<BoardPage>();
        nav.ClickWhenReady("button.nav-toggle");
        Assert.NotEmpty(nav.FindAll(".organise"));

        board.Eventually(() => CardOf(board, 123).Click());
        board.Eventually(() => Assert.NotEmpty(nav.FindAll(".examine")));
        Assert.True(nav.Find(".nav-opts").HasAttribute("hidden"));   // filtering, hiding and organising give way to the metadata: kept, out of sight

        board.ClickWhenReady(".board-wrap");
        board.Eventually(() => Assert.Empty(nav.FindAll(".examine")));
        Assert.False(nav.Find(".nav-opts").HasAttribute("hidden"));
        Assert.NotNull(nav.Find(".organise"));
        Assert.NotNull(nav.Find(".nav-hidden"));
        Assert.Empty(board.FindAll("article.card.ex-on"));

        board.Eventually(() => CardOf(board, 123).Click());   // examined again: the Done link in the menu lets go as well
        board.Eventually(() => Assert.NotEmpty(nav.FindAll(".examine")));
        nav.ClickWhenReady(".examine button.ex-done");
        board.Eventually(() => Assert.Empty(nav.FindAll(".examine")));
        Assert.DoesNotContain("/item/", NavUri(ctx));
    }

    [Fact(DisplayName = "With the menu collapsed one click opens the item as before")]
    public async Task CollapsedMenuOpensOnOneClick()
    {
        await using BunitContext ctx = NewContext();
        IRenderedComponent<BoardPage> board = ctx.Render<BoardPage>();

        board.Eventually(() => CardOf(board, 123).Click());

        Assert.EndsWith("/item/123", NavUri(ctx));
        Assert.Empty(nav!.FindAll(".examine"));
    }

    [Fact(DisplayName = "In the list a click on a row's title examines it with the menu expanded, a second click opens it, and Shift-click still selects")]
    public async Task ListRowIsExaminedToo()
    {
        await using BunitContext ctx = NewContext();
        IRenderedComponent<BoardPage> board = ctx.Render<BoardPage>();
        ShowList();
        nav.ClickWhenReady("button.nav-toggle");
        AngleSharp.Dom.IElement Row() => board.FindAll("tr[data-id]").First(r => r.GetAttribute("data-id") == "123");

        board.Eventually(() => Row().QuerySelector("td.num")!.Click(new MouseEventArgs { ShiftKey = true }));
        board.Eventually(() => Assert.True(RowMarkProbe.Selected(board, Row())));
        Assert.Empty(nav.FindAll(".examine"));
        board.Eventually(() => Row().QuerySelector("td.num")!.Click(new MouseEventArgs { ShiftKey = true }));

        board.Eventually(() => Row().QuerySelector(".row-title")!.Click());
        board.Eventually(() => Assert.True(RowMarkProbe.Examined(board, Row())));
        Assert.DoesNotContain("/item/", NavUri(ctx));
        Assert.Contains("#wi123", nav.Find(".examine").TextContent);

        board.Eventually(() => Row().QuerySelector(".row-title")!.Click());
        Assert.EndsWith("/item/123", NavUri(ctx));
    }

    // ---- list-header chunk ----

    [Fact(DisplayName = "The list's sticky column headers paint the strip band above them over the whole table width, so no row shows above the headers when the table is scrolled sideways")]
    public void ListHeaderBandIsCoveredOverTheWholeTableWidth()
    {
        // css is not evaluated here: read the rules. The strip (.hdr-path) above the headers is only as wide as the wrapper's visible part, so a
        // table wider than it, scrolled sideways, left the band open on the right and the rows scrolled up behind the headers showed in it
        string css = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "wwwroot", "css", "app.css")).Replace("\r\n", "\n");
        System.Text.RegularExpressions.Match strip = System.Text.RegularExpressions.Regex.Match(css, @"\.tbl-wrap \.hdr-path \{(?<body>[^}]*)\}");
        System.Text.RegularExpressions.Match th = System.Text.RegularExpressions.Regex.Match(css, @"\.tbl-wrap thead th \{(?<body>[^}]*)\}");
        Assert.True(strip.Success, "no rule for the path strip");
        Assert.True(th.Success, "no rule for the header cells");
        string band = System.Text.RegularExpressions.Regex.Match(strip.Groups["body"].Value, @"height: (\d+)px").Groups[1].Value;
        Assert.NotEmpty(band);
        // the header cells stick right under the strip, are opaque, and each paints the strip's height above itself in the same opaque colour
        Assert.Contains("position: sticky; top: " + band + "px;", th.Groups["body"].Value);
        Assert.Contains("background: var(--paper);", th.Groups["body"].Value);
        Assert.Contains("box-shadow: 0 -" + band + "px 0 var(--paper);", th.Groups["body"].Value);
        // the strip stays above the cells' shadows and the rows
        Assert.Contains("z-index: 3;", strip.Groups["body"].Value);
        Assert.Contains("z-index: 2;", th.Groups["body"].Value);
    }

    // ---- add-status chunk ----

    [Fact(DisplayName = "Grouped by status the board has a + status button after the last column, like + feature, and it adds the status to the project's list")]
    public async Task AddStatusColumn()
    {
        await using BunitContext ctx = NewContext();
        IRenderedComponent<BoardPage> board = ctx.Render<BoardPage>();
        board.Eventually(() => Assert.Equal(["Backlog", "Planned", "Confirmed", "In Progress", "Blocked", "In Review", "Completed"], Columns(board)));

        AngleSharp.Dom.IElement add = board.Find("button.add-col");   // the same button and place as + feature, after the last column
        Assert.Equal("+ status", add.TextContent.Trim());
        Assert.Equal("section", add.PreviousElementSibling!.LocalName);
        board.Eventually(() => add.Click());
        overlays!.Eventually(() => Assert.Contains("New status", overlays.Find(".dialog").TextContent));
        overlays.InputWhenReady(".dialog input", "Review ready");
        overlays.ClickWhenReady(".dialog button.primary");

        // the new column is there at once, empty, before Completed (which is archiving and stays last)
        board.Eventually(() => Assert.Equal(["Backlog", "Planned", "Confirmed", "In Progress", "Blocked", "In Review", "Review ready", "Completed"], Columns(board)));
        Assert.Empty(board.FindAll("section.col").Single(c => c.GetAttribute("data-key") == "Review ready").QuerySelectorAll("article.card"));
        Assert.Contains("Review ready", WorkItemsApi.GetStatuses());
        Assert.Contains("Review ready", repo.Read("openspec/config.yaml"));

        // grouped by feature the button is + feature again, not + status
        GroupByFeature();
        board.Eventually(() => Assert.Equal("+ feature", board.Find("button.add-col").TextContent.Trim()));
    }

    [Fact(DisplayName = "With several projects shown, + status asks which project")]
    public async Task AddStatusAsksWhichProject()
    {
        await using BunitContext ctx = NewContext();
        TwoProjects(ctx);
        Workspace ws = ctx.Services.GetRequiredService<Workspace>();
        IRenderedComponent<BoardPage> board = ctx.Render<BoardPage>();
        board.Eventually(() => Assert.NotEmpty(board.FindAll(".head-row")));   // the projects' columns agree: one shared header row
        Assert.Single(board.FindAll("button.add-col"));
        Assert.Equal("+ status", board.Find(".head-row > button.add-col").TextContent.Trim());

        board.ClickWhenReady(".head-row > button.add-col");
        overlays!.Eventually(() => Assert.Equal([NameOf(repo), NameOf(repo2), "All projects"], overlays.FindAll(".dialog .choice").Select(c => c.TextContent.Trim())));
        overlays.Eventually(() => overlays.FindAll(".dialog .choice").Single(c => c.TextContent.Trim() == NameOf(repo2)).Click());
        overlays.Eventually(() => Assert.Contains("New status", overlays.Find(".dialog").TextContent));
        overlays.InputWhenReady(".dialog input", "Design");
        overlays.ClickWhenReady(".dialog button.primary");

        board.Eventually(() => Assert.Contains("Design", Heads(board)));   // the union: the shared row has the new column
        Assert.Contains("Design", ws.In(repo2.Root, () => WorkItemsApi.GetStatuses()));
        Assert.DoesNotContain("Design", ws.In(repo.Root, () => WorkItemsApi.GetStatuses()));   // only the project picked
        Assert.Equal(1, board.FindAll(".proj")[0].QuerySelectorAll("section.col").Count(c => c.GetAttribute("data-key") == "Design"));   // the other shows it empty

        // "All projects": every project that lacks it gets it
        board.ClickWhenReady(".head-row > button.add-col");
        overlays.Eventually(() => Assert.Equal(3, overlays.FindAll(".dialog .choice").Count));
        overlays.FireWhenReady(".dialog .choice", e => e.Click(), all => all.Single(x => x.TextContent.Trim() == "All projects"));
        overlays.Eventually(() => Assert.Contains("New status", overlays.Find(".dialog").TextContent));
        overlays.InputWhenReady(".dialog input", "Design");   // repo2 has it already: only repo gets it
        overlays.ClickWhenReady(".dialog button.primary");

        board.Eventually(() => Assert.Contains("Design", ws.In(repo.Root, () => WorkItemsApi.GetStatuses())));
        Assert.Single(ws.In(repo2.Root, () => WorkItemsApi.GetStatuses()), "Design");
    }

    [Fact(DisplayName = "A status name that exists already is refused")]
    public async Task AddStatusRefusesAnExistingName()
    {
        await using BunitContext ctx = NewContext();
        IRenderedComponent<BoardPage> board = ctx.Render<BoardPage>();
        board.Eventually(() => Assert.NotEmpty(board.FindAll("button.add-col")));

        board.ClickWhenReady("button.add-col");
        overlays!.Eventually(() => overlays.InputWhenReady(".dialog input", "Planned"));
        overlays.ClickWhenReady(".dialog button.primary");

        overlays.Eventually(() => Assert.Contains("already", overlays.Find(".toast").TextContent));
        Assert.Equal(["Backlog", "Planned", "Confirmed", "In Progress", "Blocked", "In Review", "Completed"], Columns(board));
        Assert.Equal(WorkItemsApi.Statuses, WorkItemsApi.GetStatuses());
        Assert.DoesNotContain("statuses:", repo.Read("openspec/config.yaml"));   // nothing was written

        // spelling counts: "planned" is another status than "Planned" (the board can merge the two)
        board.ClickWhenReady("button.add-col");
        overlays.Eventually(() => overlays.InputWhenReady(".dialog input", "planned"));
        overlays.ClickWhenReady(".dialog button.primary");
        board.Eventually(() => Assert.Contains("planned", Columns(board)));
    }

    // ---- card-dates chunk ----

    private static readonly DateTime AgentWrote = new(2026, 10, 30, 9, 0, 0);

    private static FakeTimeProvider ClockAt(TimeSpan after) => new(new DateTimeOffset(AgentWrote + after, TimeSpan.Zero));

    private static bool HasCard(AngleSharp.Dom.IElement cell, int id) => cell.QuerySelectorAll("article.card").Any(c => c.GetAttribute("data-id") == id.ToString());

    [Fact(DisplayName = "A card shows no id pill and no agent badge")]
    public async Task CardShowsNoIdPillAndNoAgentBadge()
    {
        EditingWorkItemsTests.StampAgent(repo);   // Claude wrote #123 at 09:00 on 30 Oct, unreviewed
        await using BunitContext ctx = NewContext(ClockAt(TimeSpan.FromMinutes(3)));
        WorkItem item = WorkItemsApi.Get(123)! with { AgentBy = "Claude", AgentAt = AgentWrote, ReviewedAt = null };
        IRenderedComponent<WorkItemCard> card = ctx.Render<WorkItemCard>(p => p.Add(c => c.Item, item).Add(c => c.Options, new BoardOptions()));

        Assert.DoesNotContain("#wi", card.Markup);
        Assert.DoesNotContain("min ago", card.Markup);
        Assert.Empty(card.FindAll(".agent-badge"));
        Assert.Equal("123", card.Find("article.card").GetAttribute("data-id"));   // the id is still how the board finds the card

        // the list row leaves the badge out too: it squeezed the title down to a letter in a 1280 px window
        IRenderedComponent<BoardPage> board = ctx.Render<BoardPage>();
        ShowList();
        board.Eventually(() => Assert.Equal("Health-check endpoint", board.Find("tr[data-id='123'] .row-title").TextContent.Trim()));
        Assert.DoesNotContain("min ago", board.Find("tr[data-id='123']").TextContent);
        Assert.Empty(board.FindAll(".agent-badge"));
    }

    [Fact(DisplayName = "A card's dates are one period: 9–16 Nov, across months and years, from or due alone")]
    public async Task CardDatesAreOnePeriod()
    {
        FakeTimeProvider time = new(new DateTimeOffset(2026, 11, 16, 12, 0, 0, TimeSpan.Zero));
        await using BunitContext ctx = NewContext(time);
        WorkItem item = WorkItemsApi.Get(123)! with { Archived = false, Started = null, Due = null };
        IRenderedComponent<WorkItemCard> Card(WorkItem i, bool compact = false) => ctx.Render<WorkItemCard>(p => p.Add(c => c.Item, i).Add(c => c.Options, new BoardOptions(Compact: compact)));
        string? Pill(WorkItem i, bool compact = false) => Card(i, compact).FindAll(".tags .tag.dates").SingleOrDefault()?.TextContent.Trim();
        static DateOnly D(int y, int m, int d) => new(y, m, d);

        Assert.Equal("9–16 Nov", Pill(item with { Started = D(2026, 11, 9), Due = D(2026, 11, 16) }));
        Assert.Equal("28 Oct – 3 Nov", Pill(item with { Started = D(2026, 10, 28), Due = D(2026, 11, 3) }));
        Assert.Equal("9 Nov 25 – 3 Jan 26", Pill(item with { Started = D(2025, 11, 9), Due = D(2026, 1, 3) }));
        Assert.Equal("due 16 Nov", Pill(item with { Started = D(2026, 11, 16), Due = D(2026, 11, 16) }));   // one day: a bare "16 Nov" could be a start or a due day
        Assert.Equal("from 9 Nov", Pill(item with { Started = D(2026, 11, 9) }));
        Assert.Equal("due 16 Nov", Pill(item with { Due = D(2026, 11, 16) }));
        Assert.Null(Pill(item));
        Assert.Null(Pill(item with { Started = D(2026, 11, 9), Due = D(2026, 11, 16) }, compact: true));

        // overdue: the pill wears the due colour once the due day is past (the day itself is not), and not when archived or only started
        WorkItem week = item with { Started = D(2026, 11, 9), Due = D(2026, 11, 16) };
        Assert.DoesNotContain("overdue", Card(week).Find(".tags .tag.dates").ClassName);   // due today
        time.Advance(TimeSpan.FromDays(1));
        Assert.Contains("overdue hot", Card(week).Find(".tags .tag.dates").ClassName);
        Assert.Contains("overdue hot", Card(item with { Due = D(2026, 11, 16) }).Find(".tags .tag.dates").ClassName);
        Assert.DoesNotContain("overdue", Card(week with { Archived = true }).Find(".tags .tag.dates").ClassName);
        Assert.DoesNotContain("overdue", Card(item with { Started = D(2026, 11, 9) }).Find(".tags .tag.dates").ClassName);
    }

    [Fact(DisplayName = "The card's numbers say what they are: updated 15:34. 16 Nov 26, 11/12 subtasks, with long titles")]
    public async Task CardNumbersAreCaptioned()
    {
        await using BunitContext ctx = NewContext();
        List<Subtask> subs = [.. Enumerable.Range(1, 12).Select(n => new Subtask($"1.{n}", $"step {n}", n <= 11, "1. Tasks"))];
        WorkItem item = WorkItemsApi.Get(123)! with { Modified = new DateTime(2026, 11, 16, 15, 34, 0), Subtasks = subs };
        IRenderedComponent<WorkItemCard> card = ctx.Render<WorkItemCard>(p => p.Add(c => c.Item, item).Add(c => c.Options, new BoardOptions()));

        string meta = card.Find(".meta-row").TextContent;
        Assert.Contains("updated 15:34. 16 Nov 26", meta);
        Assert.Contains("11/12 subtasks", meta);
        Assert.Equal("Last changed 15:34, 16 Nov 2026", card.Find(".meta-row [title^='Last changed']").GetAttribute("title"));
        Assert.Equal("11 of 12 subtasks done", card.Find(".meta-row .subs").GetAttribute("title"));
        Assert.Single(card.FindAll(".meta-row"));   // still one meta row, no frame
    }

    [Fact(DisplayName = "The modified time reads 15:34. 16 Nov 26 on the card and in the list's Updated column")]
    public async Task ModifiedReadsTimeThenDay()
    {
        string folder = "openspec/changes/add-health-check/";
        repo.Write(folder + "events.log", "2026-11-16T15:34:00 | Artyom | priority | low -> high\n");   // the newest change: what Updated shows
        repo.Write(folder + ".openspec.yaml", repo.Read(folder + ".openspec.yaml").TrimEnd() + "\n  started: 2026-11-09\n  due: 2026-11-16\n");
        await using BunitContext ctx = NewContext();

        WorkItem item = WorkItemsApi.Get(123)!;
        Assert.Equal(new DateTime(2026, 11, 16, 15, 34, 0), item.Modified);
        IRenderedComponent<WorkItemCard> card = ctx.Render<WorkItemCard>(p => p.Add(c => c.Item, item).Add(c => c.Options, new BoardOptions()));
        Assert.StartsWith("updated 15:34. 16 Nov 26", card.Find(".meta-row").TextContent.Trim());
        Assert.StartsWith("updated 09:05. 3 Jan 26", ctx.Render<WorkItemCard>(p => p.Add(c => c.Item, item with { Modified = new DateTime(2026, 1, 3, 9, 5, 0) }).Add(c => c.Options, new BoardOptions())).Find(".meta-row").TextContent.Trim());   // zero-padded hour, plain day

        IRenderedComponent<BoardPage> board = ctx.Render<BoardPage>();
        ShowList();
        board.Eventually(() => Assert.NotEmpty(board.FindAll("tr[data-id='123']")));
        List<string> cells = board.Find("tr[data-id='123']").QuerySelectorAll("td").Select(c => c.TextContent.Trim()).ToList();
        int updated = cells.IndexOf("15:34. 16 Nov 26"), started = cells.IndexOf("9 Nov"), due = cells.IndexOf("16 Nov");
        Assert.True(updated >= 0 && started > updated && due > started, string.Join(" | ", cells));   // Updated, then Started and Due in their own columns, in the short form
    }

    // ---- menu2 chunk ----

    private IElement FeedPill(string name) => nav!.FindAll(".nav-worklog button.nav-pill").Single(b => b.TextContent.Trim() == name);
    private string[] FeedPillsOn() => [.. nav!.FindAll(".nav-worklog button.nav-pill").Where(b => b.GetAttribute("aria-pressed") == "true").Select(b => b.TextContent.Trim())];

    [Fact(DisplayName = "With every feed filter on, clicking one shows only that kind, and later clicks add kinds")]
    public async Task FeedFilterClickIsSoloThenAdditive()
    {
        await using BunitContext ctx = NewContext();
        ctx.Services.GetRequiredService<SettingsStore>().Update(s => s with { Split = false });
        ctx.Services.GetRequiredService<NavigationManager>().NavigateTo("item/123");
        UiService ui = ctx.Services.GetRequiredService<UiService>();
        nav.ClickWhenReady("button.nav-toggle");
        IRenderedComponent<Worklog> log = OpenWorklog(ctx);
        nav.Eventually(() => Assert.NotEmpty(nav.FindAll(".nav-worklog")));
        Assert.Equal(7, FeedPillsOn().Length);
        Assert.Empty(nav.FindAll(".nav-worklog .nav-feed-all"));   // nothing to restore while everything is on

        // every kind on: a click on one shows only that kind
        nav.Eventually(() => FeedPill("Notes").Click());
        Assert.Equal(["Notes"], FeedPillsOn());
        Assert.Equal(new WorklogFilters(Notes: true, Decisions: false, Events: false, Links: false, Subtasks: false, Edits: false, Bugs: false), ui.WorklogFilter);
        log.Eventually(() => Assert.All(log.FindAll(".wk-day .wk-e"), e => Assert.Equal("note", e.GetAttribute("data-kind"))));

        // not all on: a click adds the kind, a second click takes it out again
        nav.Eventually(() => FeedPill("Bugs").Click());
        Assert.Equal(["Notes", "Bugs"], FeedPillsOn());
        nav.Eventually(() => FeedPill("Notes").Click());
        Assert.Equal(["Bugs"], FeedPillsOn());

        // the last one off brings every kind back: never an empty feed
        nav.Eventually(() => FeedPill("Bugs").Click());
        Assert.Equal(7, FeedPillsOn().Length);
        Assert.Equal(new WorklogFilters(), ui.WorklogFilter);

        // the small "all" link restores everything at once, and keeps who
        nav.Eventually(() => FeedPill("Events").Click());
        nav.Eventually(() => FeedPill("Links").Click());
        Assert.Equal(["Events", "Links"], FeedPillsOn());
        nav.ClickWhenReady(".nav-worklog .nav-feed-all");
        Assert.Equal(new WorklogFilters(), ui.WorklogFilter);
        Assert.Empty(nav.FindAll(".nav-worklog .nav-feed-all"));
    }

    private static string SubtaskRowNumbers(IEnumerable<IElement> rows) => string.Join(",", rows.Select(r => r.GetAttribute("data-sub")));

    /// <summary>The board with the menu expanded and the fixture item #wi123 (two done and two open subtasks) examined.</summary>
    private BunitContext ExamineFixtureItem(out IRenderedComponent<BoardPage> board, out UiService ui)
    {
        BunitContext ctx = NewContext();
        IRenderedComponent<BoardPage> page = board = ctx.Render<BoardPage>();
        ui = ctx.Services.GetRequiredService<UiService>();
        nav.ClickWhenReady("button.nav-toggle");
        page.Eventually(() => CardOf(page, 123).Click());
        nav.Eventually(() => Assert.NotEmpty(nav.FindAll(".examine")));
        return ctx;
    }

    [Fact(DisplayName = "The examine panel lists the examined item's subtasks")]
    public async Task ExaminePanelListsSubtasks()
    {
        await using BunitContext ctx = ExamineFixtureItem(out _, out _);

        // the open ones are rows with their numbers, under their group; the done ones are folded behind a count
        Assert.Equal("1.3,2.1", SubtaskRowNumbers(nav!.FindAll(".examine .ex-sub")));
        Assert.Contains("Ping test in suite 543", nav.Find(".examine .ex-sub[data-sub='1.3']").TextContent);
        Assert.Equal("1.3", nav.Find(".examine .ex-sub[data-sub='1.3'] .ex-num").TextContent.Trim());
        Assert.Contains("Endpoint", nav.Find(".examine .ex-sub-group").TextContent);
        Assert.Equal("2 done", nav.Find(".examine .ex-sub-folded").TextContent.Trim());
        Assert.Empty(nav.FindAll(".examine .ex-sub[data-done]"));

        nav.ClickWhenReady(".examine .ex-sub-folded");
        Assert.Equal("1.1,1.2,1.3,2.1", SubtaskRowNumbers(nav.FindAll(".examine .ex-sub")));
        Assert.Equal(2, nav.FindAll(".examine .ex-sub[data-done]").Count);

        // the unit switch is no longer in Filters: the examine panel is where subtasks are looked at
        nav.ClickWhenReady(".examine button.ex-done");
        nav.Eventually(() => Assert.Empty(nav.FindAll(".examine")));
        nav.ClickWhenReady("button[data-sec=filters]");
        Assert.Empty(nav.FindAll("input[name=unit]"));
    }

    [Fact(DisplayName = "The board's subtask view stays reachable from the examine panel, and Filters offers the way back to work items while it is on")]
    public async Task SubtaskUnitReachableFromExaminePanel()
    {
        await using BunitContext ctx = ExamineFixtureItem(out IRenderedComponent<BoardPage> board, out _);
        SettingsStore settings = ctx.Services.GetRequiredService<SettingsStore>();

        nav.ClickWhenReady(".examine button.ex-all-subtasks");
        Assert.Equal("subtasks", settings.Board(repo.Root).Unit);
        board.Eventually(() => Assert.NotEmpty(board.FindAll("article.subcard")));

        nav.ClickWhenReady(".examine button.ex-done");
        nav.Eventually(() => Assert.Empty(nav.FindAll(".examine")));
        nav.Eventually(() => Assert.Equal("true", Section("filters").GetAttribute("aria-expanded")));   // the test menu starts with Filters open
        nav.ChangeWhenReady("input[name=unit][value=tasks]", "tasks");
        Assert.Equal("tasks", settings.Board(repo.Root).Unit);
    }

    [Fact(DisplayName = "Clicking a subtask in the examine panel asks whether to make it a work item of its own or one this item waits on, and does it")]
    public async Task ClickingAnExamineSubtaskAsksAndSplits()
    {
        await using BunitContext ctx = ExamineFixtureItem(out _, out UiService ui);
        int before = WorkItemsApi.GetAll(includeHidden: true).Count;

        // The click, found and sent in one dispatcher turn. The returned task ends when the panel's handler has, which is after the dialog is answered and the panel has
        // rendered again: awaiting it after the answer is what makes the next Find see the new render (a Find from before it holds a replaced handler).
        Task ClickSubtask(string number) => nav!.InvokeAsync(() => nav.Find($".examine .ex-sub[data-sub='{number}']").ClickAsync(new()));

        // a click asks; Cancel changes nothing
        Task asked = ClickSubtask("2.1");
        nav.Eventually(() => Assert.NotNull(ui.Dialog));
        Assert.Equal(["own", "dep"], ui.Dialog!.Choices!.Select(c => c.Value));
        Assert.Equal("Make it a work item of its own", ui.Dialog.Choices![0].Label);
        Assert.Equal("Make it a work item this one waits on", ui.Dialog.Choices[1].Label);
        Assert.Contains("Hide internals", ui.Dialog.Text);
        ui.Close(null);
        await asked;
        Assert.Equal(before, WorkItemsApi.GetAll(includeHidden: true).Count);
        Assert.Contains("2.1", SubtaskRowNumbers(nav.FindAll(".examine .ex-sub")));

        // "of its own": the subtask leaves this item and becomes one, logged on both
        asked = ClickSubtask("2.1");
        nav.Eventually(() => Assert.NotNull(ui.Dialog));
        ui.Close("own");
        await asked;
        nav.Eventually(() => Assert.DoesNotContain("2.1", SubtaskRowNumbers(nav.FindAll(".examine .ex-sub"))));
        Assert.Equal(before + 1, WorkItemsApi.GetAll(includeHidden: true).Count);
        WorkItem made = WorkItemsApi.GetAll(includeHidden: true).Single(w => w.Title == "Hide internals from anonymous callers");
        Assert.DoesNotContain(made.Id, WorkItemsApi.Get(123)!.DependsOn ?? []);
        Assert.Contains(WorkItemsApi.GetEvents(123), e => e.Kind == "subtask-moved");

        // "this one waits on": the new item is a dependency of the examined one
        asked = ClickSubtask("1.3");
        nav.Eventually(() => Assert.NotNull(ui.Dialog));
        ui.Close("dep");
        await asked;
        nav.Eventually(() => Assert.DoesNotContain("1.3", SubtaskRowNumbers(nav.FindAll(".examine .ex-sub"))));
        WorkItem examined = WorkItemsApi.Get(123)!;
        Assert.Single(examined.DependsOn!.Where(d => WorkItemsApi.Get(d)?.Title == "Ping test in suite 543"));
        Assert.Equal("Blocked", examined.Status);
    }

    [Fact(DisplayName = "Starting to drag a subtask in the examine panel asks the same question")]
    public async Task DraggingAnExamineSubtaskAsks()
    {
        await using BunitContext ctx = ExamineFixtureItem(out _, out UiService ui);

        Assert.Equal("true", nav!.Find(".examine .ex-sub[data-sub='2.1']").GetAttribute("draggable"));
        nav.Find(".examine .ex-sub[data-sub='2.1']").DragStart();
        nav.Eventually(() => Assert.NotNull(ui.Dialog?.Choices));
        ui.Close(null);
    }

    [Fact(DisplayName = "New work item is a filled red button and the menu's buttons share one edge and height")]
    public async Task NewWorkItemIsFilledRedAndMenuRowsAlign()
    {
        await using BunitContext ctx = NewContext();
        ctx.Render<BoardPage>();
        nav.ClickWhenReady("button.nav-toggle");
        string css = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "wwwroot", "css", "app.css")).Replace("\r\n", "\n");

        // filled, not outlined: the --alert fill, the text on it, a darker fill when pointed at; the same button on the collapsed rail
        string rule = System.Text.RegularExpressions.Regex.Matches(css, @"\.nav-btn\.nav-new \{(?<b>[^}]*)\}").Select(m => m.Groups["b"].Value).First();
        Assert.Contains("background: var(--alert)", rule);
        Assert.Contains("color: var(--on-alert)", rule);
        Assert.Contains("--on-alert:", css);
        Assert.Matches(@"\.nav-btn\.nav-new:hover \{[^}]*(brightness|color-mix)", css);

        // one geometry for every pressable row: view switcher, New work item, section headers, Settings and Add entry share height, padding and radius
        string shared = System.Text.RegularExpressions.Regex.Match(css, @"(?<sel>[^{}]*\.nav-sec-h[^{}]*) \{(?<b>[^}]*min-height: 32px[^}]*)\}").Value;
        Assert.NotEmpty(shared);
        Assert.Contains(".nav-btn", shared);
        Assert.Contains(".nav-add", shared);
        Assert.Contains("padding-inline:", shared);
        Assert.Contains("border-radius: 10px", shared);

        // in the markup every row leads with its icon (the section headers with the chevron), so the icons sit in one column
        Assert.All(nav.FindAll("nav.side .nav-btn, nav.side .nav-add, nav.side .nav-sec-h"), b => Assert.NotNull(b.QuerySelector(":scope > svg, :scope > .nav-sec-chev > svg")));
    }

    // ---- git-deltas chunk ----

    /// <summary>Makes the test repository a git repository with two commits (the fixture, then an edit of #wi107) and one uncommitted edit (#wi123). Call before NewContext.</summary>
    private (string First, string Second) MakeGitHistory()
    {
        TempGit.Init(repo.Root);
        TempGit.Commit(repo.Root, "2026-11-01T10:00:00+00:00", "Add the fixture");
        repo.Write("openspec/changes/export-remittances/proposal.md", repo.Read("openspec/changes/export-remittances/proposal.md") + "\nA second line.\n");
        TempGit.Commit(repo.Root, "2026-11-02T10:00:00+00:00", "Reword the export");
        repo.Write("openspec/changes/add-health-check/proposal.md", repo.Read("openspec/changes/add-health-check/proposal.md") + "\nNot committed yet.\n");
        string[] hashes = [.. TempGit.Run(repo.Root, null, "log", "--format=%h").Split('\n', StringSplitOptions.RemoveEmptyEntries)];
        return (hashes[1].Trim(), hashes[0].Trim());
    }

    private static List<string> CardIds(IRenderedComponent<BoardPage> board) => [.. board.FindAll("article.card").Select(c => c.GetAttribute("data-id")!).Order()];

    [Fact(DisplayName = "An item with uncommitted changes carries the marker")]
    public async Task UncommittedItemCarriesTheMarker()
    {
        MakeGitHistory();
        await using BunitContext ctx = NewContext();
        SettingsStore settings = ctx.Services.GetRequiredService<SettingsStore>();
        IRenderedComponent<BoardPage> board = ctx.Render<BoardPage>();
        board.Eventually(() => Assert.True(board.FindAll("article.card").Count > 2));

        // while no commit is chosen: a small dot on the card of the one item whose files are not committed, none on the others
        IElement dot = board.Find("article.card .dirty-dot");
        Assert.Equal("123", dot.Closest("article.card")!.GetAttribute("data-id"));
        Assert.Equal("Uncommitted changes", dot.GetAttribute("title"));
        Assert.Single(board.FindAll(".dirty-dot"));

        // choosing "uncommitted" filters instead of marking
        settings.SaveBoard(repo.Root, settings.Board(repo.Root) with { Commit = "uncommitted" });
        board.Eventually(() => Assert.Equal(["123"], CardIds(board)));
        Assert.Empty(board.FindAll(".dirty-dot"));
    }

    [Fact(DisplayName = "The Filters section offers any, uncommitted and the recent commits, and choosing one filters the board")]
    public async Task CommitSelectorFiltersTheBoard()
    {
        (string first, string second) = MakeGitHistory();
        await using BunitContext ctx = NewContext();
        SettingsStore settings = ctx.Services.GetRequiredService<SettingsStore>();
        IRenderedComponent<BoardPage> board = ctx.Render<BoardPage>();
        board.Eventually(() => Assert.True(board.FindAll("article.card").Count > 2));
        nav.ClickWhenReady("button.nav-toggle");

        Assert.Contains("is-off", NavDropdown("commit").ClassList);   // grey while it is any
        nav.Eventually(() => NavDropdown("commit").Click());
        IEnumerable<IElement> Items() => nav.FindAll(".nav-view .field").First(f => f.QuerySelector("label")!.TextContent.Trim() == "commit").QuerySelectorAll(".rz-dropdown-item");
        nav.Eventually(() => Assert.Equal(4, Items().Count()));
        List<string> options = [.. Items().Select(i => i.TextContent.Trim())];
        Assert.Equal(["any", "uncommitted"], options[..2]);
        Assert.StartsWith(second + " Reword the export", options[2]);   // newest first, each with its short hash and subject
        Assert.StartsWith(first + " Add the fixture", options[3]);

        nav.Eventually(() => Items().First(i => i.TextContent.Contains("Reword the export")).Click());
        board.Eventually(() => Assert.Equal(["107"], CardIds(board)));
        Assert.Equal(second, settings.Board(repo.Root).Commit);       // saved with the board's other options
        nav.Eventually(() => Assert.Contains("is-on", NavDropdown("commit").ClassList));
        Assert.Empty(board.FindAll(".dirty-dot"));

        nav.Eventually(() => Section("filters").Click());   // fold the section (the test menu starts with it open): its header says which commit
        Assert.True(SectionBody("filters").HasAttribute("hidden"));
        nav.Eventually(() => Assert.Equal("Filters · commit: " + second, Flat(Section("filters"))));

        settings.SaveBoard(repo.Root, settings.Board(repo.Root) with { Commit = "any" });
        board.Eventually(() => Assert.True(CardIds(board).Count > 2));
    }

    [Fact(DisplayName = "Every Organise and Filters row is a label column and a pill column, so all the pills share one left edge")]
    public async Task OptionRowsAreOneGridWithSharedLabelColumn()
    {
        await using BunitContext ctx = NewContext();
        ctx.Render<BoardPage>();
        nav.ClickWhenReady("button.nav-toggle");
        SettingsStore settings = ctx.Services.GetRequiredService<SettingsStore>();
        settings.SaveBoard(repo.Root, settings.Board(repo.Root) with { View = "list", ChangedFrom = new DateOnly(2026, 11, 9), ChangedTo = new DateOnly(2026, 11, 17) });
        nav.Eventually(() => Assert.NotEmpty(nav.FindAll(".nav-filters .field.changed")));

        // in the markup a row is its label and one control, nothing else
        IElement[] rows = [.. nav.FindAll(".organise .field, .nav-filters .field")];
        Assert.True(rows.Length >= 8, "sort by, groups, subgroups, colour, contributor, period, commit, changed");
        Assert.All(rows, r => { Assert.Equal(2, r.Children.Length); Assert.Equal("LABEL", r.Children[0].TagName); });

        // in the style one rule gives every row the same two columns, a label column of one width (the changed row and the commit row included)
        string css = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "wwwroot", "css", "app.css")).Replace("\r\n", "\n");
        string rule = System.Text.RegularExpressions.Regex.Matches(css, @"nav\.side \.nav-view \.field[^{}]*\{(?<b>[^}]*)\}").Select(m => m.Groups["b"].Value).First(b => b.Contains("display: grid"));
        Assert.Matches(@"grid-template-columns:\s*var\(--opt-label\)\s+minmax\(0,\s*1fr\)", rule);
        Assert.DoesNotContain("flex: 0 0 100%", css[css.IndexOf("nav.side .field.changed", StringComparison.Ordinal)..][..300]);   // the changed label is no longer stacked above its pill
        // a pill that is too long ends in an ellipsis inside itself, every pill alike (no rule only for the commit's)
        Assert.Matches(@"nav\.side \.field \.dd\.nav-dd[^{}]*\{[^}]*min-width: 0", css);
        Assert.Matches(@"nav\.side \.field \.dd \.rz-dropdown-label > \*[^{}]*\{[^}]*text-overflow: ellipsis", css);
    }

    [Fact(DisplayName = "Add entry is the same filled red button as New work item")]
    public async Task AddEntryIsTheRedButton()
    {
        await using BunitContext ctx = NewContext();
        ctx.Services.GetRequiredService<SettingsStore>().Update(s => s with { Split = false });
        ctx.Services.GetRequiredService<NavigationManager>().NavigateTo("item/123");
        nav.ClickWhenReady("button.nav-toggle");
        OpenWorklog(ctx);
        nav.Eventually(() => Assert.NotEmpty(nav.FindAll(".nav-worklog button.nav-add")));
        IElement add = nav.Find(".nav-worklog button.nav-add");
        Assert.Contains("nav-btn", add.ClassList);
        Assert.Contains("nav-new", add.ClassList);   // the class that carries the --alert fill, the text on it and the darker hover
        Assert.NotNull(add.QuerySelector(":scope > svg"));   // the + glyph stays
        string css = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "wwwroot", "css", "app.css")).Replace("\r\n", "\n");
        // no rule of its own paints it another colour
        Assert.DoesNotMatch(@"\.nav-add\s*\{[^}]*(background|color):", css);
        Assert.DoesNotMatch(@"\.nav-add:hover[^{}]*\{", css);
    }

    [Fact(DisplayName = "The commit list opens as a popup no wider than the menu, one row per commit, ending in an ellipsis")]
    public async Task CommitPopupIsMenuWide()
    {
        MakeGitHistory();
        await using BunitContext ctx = NewContext();
        ctx.Render<BoardPage>();
        nav.ClickWhenReady("button.nav-toggle");
        nav.Eventually(() => NavDropdown("commit").Click());
        IElement field() => nav.FindAll(".nav-view .field").First(f => f.QuerySelector("label")!.TextContent.Trim() == "commit");
        nav.Eventually(() => Assert.NotEmpty(field().QuerySelectorAll(".rz-dropdown-item")));

        // the popup's own width is set, the way a dropdown sets one (Dropdown's PopupStyle), and is not "as wide as its longest row"
        string style = field().QuerySelector(".rz-dropdown-panel .rz-dropdown-items-wrapper")!.GetAttribute("style") ?? "";
        Assert.Matches(@"max-width:\s*\d+px", style);
        Assert.Contains("min-width: 0", style);

        string css = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "wwwroot", "css", "app.css")).Replace("\r\n", "\n");
        Assert.Matches(@"\.rz-dropdown-item > span[^{}]*\{[^}]*text-overflow: ellipsis", css);   // a row ends in an ellipsis instead of widening the popup
        Assert.Matches(@"\.rz-dropdown-items-wrapper[^{}]*\{[^}]*max-height: min\(60vh, 400px\)", css);   // about 12 rows, then it scrolls
    }

    // ---- arrows chunk ----

    [Fact(DisplayName = "Arrow up in the list swaps the selected row within its subgroup, then crosses into the subgroup above, then into the group above; arrow down brings it back")]
    public async Task ArrowKeysMoveListRowsThroughSubgroups()
    {
        await using BunitContext ctx = NewContext();
        Workspace workspace = ctx.Services.GetRequiredService<Workspace>();
        int New(string status, string priority) { int id = workspace.CreateItem(status).Id; WorkItemsApi.SetPriority(id, priority); return id; }
        int high = New("Confirmed", "high"), lowA = New("Confirmed", "low"), lowB = New("Confirmed", "low");   // Confirmed is empty in the sample repository
        IRenderedComponent<BoardPage> board = ctx.Render<BoardPage>();
        ShowList();
        SettingsStore settings = ctx.Services.GetRequiredService<SettingsStore>();
        settings.SaveBoard(repo.Root, settings.Board(repo.Root) with { ListSwimlane = "priority", SortBy = "id" });   // groups by status, subgroups by priority, in a fixed order

        List<int> Seq() => [.. board.FindAll("tr[data-id]").Select(r => int.Parse(r.GetAttribute("data-id")!))];
        bool IsHigh(int id) => WorkItemsApi.Get(id)!.Priority == "high";
        List<int> Cell(string status, bool high) => [.. Seq().Where(id => WorkItemsApi.Get(id)!.Status == status && IsHigh(id) == high)];
        string Selected() => string.Join(",", board.FindAll("tr[data-id]").Where(r => RowMarkProbe.Selected(board, r)).Select(r => r.GetAttribute("data-id")));
        async Task Press(string key) { await board.InvokeAsync(() => { }); board.KeyDownWhenReady(".tbl-wrap", key); }
        board.Eventually(() => Assert.Equal([lowA, lowB], Cell("Confirmed", high: false)));
        Assert.Equal([high], Cell("Confirmed", high: true));
        board.FireWhenReady($"tr[data-id='{lowB}'] td.num", e => e.Click(new MouseEventArgs { ShiftKey = true }));
        board.Eventually(() => Assert.Equal($"{lowB}", Selected()));

        // inside its subgroup the row swaps with the one above it; the sort becomes my order, as a drag does
        await Press("ArrowUp");
        board.Eventually(() => Assert.Equal([lowB, lowA], Cell("Confirmed", high: false)));
        Assert.Equal("manual", settings.Board(repo.Root).SortBy);
        Assert.Equal($"{lowB}", Selected());

        // at the top of its subgroup the next press moves it to the end of the subgroup above: its priority changes
        await Press("ArrowUp");
        board.Eventually(() => Assert.Equal([high, lowB], Cell("Confirmed", high: true)));
        Assert.Equal([lowA], Cell("Confirmed", high: false));
        Assert.Equal($"{lowB}", Selected());
        await Press("ArrowUp");
        board.Eventually(() => Assert.Equal([lowB, high], Cell("Confirmed", high: true)));

        // at the top of the group's first subgroup the next press moves it to the end of the group above
        await Press("ArrowUp");
        board.Eventually(() => Assert.Equal("Planned", WorkItemsApi.Get(lowB)!.Status));
        board.Eventually(() => Assert.Equal(lowB, Seq().Where(id => WorkItemsApi.Get(id)!.Status == "Planned").Last()));
        board.Eventually(() =>   // the status and the priority are two writes in turn: the page shows the move at once, the second write lands a moment after the first
        {
            List<int> planned = [.. Seq().Where(id => WorkItemsApi.Get(id)!.Status == "Planned")];
            if (planned.Count > 1) Assert.Equal(IsHigh(planned[^2]), IsHigh(lowB));   // it entered that group's last subgroup
        });
        Assert.Equal($"{lowB}", Selected());

        // arrow down from the bottom edge enters the group below at the start of its first subgroup
        await Press("ArrowDown");
        board.Eventually(() => Assert.Equal("Confirmed", WorkItemsApi.Get(lowB)!.Status));
        board.Eventually(() => Assert.Equal([lowB, high], Cell("Confirmed", high: true)));
        Assert.Equal($"{lowB}", Selected());
    }

    [Fact(DisplayName = "Arrow keys on the board move a selected card one place per press and keep it selected, within its row's cell, then into the row above, and sideways into the next column's cell")]
    public async Task ArrowKeysOnTheBoardMoveOnePlacePerPress()
    {
        await using BunitContext ctx = NewContext();
        Workspace workspace = ctx.Services.GetRequiredService<Workspace>();
        int New(string priority) { int id = workspace.CreateItem("Confirmed").Id; WorkItemsApi.SetPriority(id, priority); return id; }
        int high = New("high"), lowA = New("low"), lowB = New("low");   // Confirmed is empty in the sample repository
        SettingsStore settings = ctx.Services.GetRequiredService<SettingsStore>();
        settings.SaveBoard(repo.Root, settings.Board(repo.Root) with { Swimlane = "priority", SortBy = "id" });
        IRenderedComponent<BoardPage> board = ctx.Render<BoardPage>();

        List<int> Cell(string row, string column) => [.. board.FindAll($".cat-cols.swim[data-swim='{row}'] section.col[data-cell='{column}'] article.card").Select(c => int.Parse(c.GetAttribute("data-id")!))];
        List<int> Selected() => [.. board.FindAll("article.card.selected").Select(c => int.Parse(c.GetAttribute("data-id")!))];
        async Task Press(string key) { await board.InvokeAsync(() => { }); board.KeyDownWhenReady(".board-wrap", key); }
        board.Eventually(() => Assert.Equal([lowA, lowB], Cell("low", "Confirmed")));
        board.FireWhenReady($"article.card[data-id='{lowB}']", e => e.Click(new MouseEventArgs { ShiftKey = true }));
        board.Eventually(() => Assert.Equal([lowB], Selected()));

        // one place per press, in the cell the card is in: the other row's cards do not count as neighbours
        await Press("ArrowUp");
        board.Eventually(() => Assert.Equal([lowB, lowA], Cell("low", "Confirmed")));
        Assert.Equal([lowB], Selected());
        // at the top of its cell it enters the row above, at the end of that cell
        await Press("ArrowUp");
        board.Eventually(() => Assert.Equal([high, lowB], Cell("high", "Confirmed")));
        Assert.Equal([lowA], Cell("low", "Confirmed"));
        Assert.Equal([lowB], Selected());
        await Press("ArrowUp");
        board.Eventually(() => Assert.Equal([lowB, high], Cell("high", "Confirmed")));
        // the first row has nothing above: the card stays
        await Press("ArrowUp");
        await board.InvokeAsync(() => { });
        Assert.Equal([lowB, high], Cell("high", "Confirmed"));
        Assert.Equal([lowB], Selected());

        // sideways it goes to the next column, in the same row, and back at the same height
        await Press("ArrowRight");
        board.Eventually(() => Assert.Equal([lowB], Cell("high", "In Progress").Where(id => id == lowB).ToList()));
        board.Eventually(() => Assert.Equal("In Progress", WorkItemsApi.Get(lowB)!.Status));   // the write follows the frame, on the thread pool
        Assert.Equal("high", WorkItemsApi.Get(lowB)!.Priority);
        await Press("ArrowLeft");
        board.Eventually(() => Assert.Equal([lowB, high], Cell("high", "Confirmed")));
        Assert.Equal([lowB], Selected());
    }

    [Fact(DisplayName = "After an arrow move app.js scrolls the moved card or row into view once the board has re-rendered, clear of the sticky header and the action bar")]
    public void ArrowMoveKeepsTheSelectionInView()
    {
        string js = AppJs();
        Assert.Contains("function _reveal(", js);
        Assert.Matches(@"_follow\s*=\s*\{[^}]*e\.key", js);   // the arrow key handler asks for it, with the direction
        Assert.Contains("_reveal(", js[js.IndexOf("new MutationObserver((records)")..]);   // and the re-render's observer does it
        string reveal = js[js.IndexOf("function _reveal(")..];
        reveal = reveal[..reveal.IndexOf("\n}")];
        Assert.Contains(".actionbar", reveal);
        Assert.Contains("thead th", reveal);   // the cells of the header stay stuck, the thead itself scrolls away
    }

    // ---- render cost of a click in the list (#wi179) ----

    /// <summary>The fixture plus <paramref name="extra"/> created items, the menu expanded through its toggle, the list shown and settled.</summary>
    private async Task<IRenderedComponent<BoardPage>> ListWithItems(BunitContext ctx, int extra)
    {
        for (int i = 0; i < extra; i++) WorkItemsApi.Create("Row " + i);
        IRenderedComponent<BoardPage> board = ctx.Render<BoardPage>();
        ShowList();
        nav.ClickWhenReady("button.nav-toggle");
        board.Eventually(() => Assert.True(board.FindAll("tr[data-id]").Count >= extra));
        await board.InvokeAsync(() => { });
        return board;
    }

    [Fact(DisplayName = "A background click with nothing selected does not render the list")]
    public async Task BackgroundClickWithNothingSelectedDoesNotRender()
    {
        await using BunitContext ctx = NewContext();
        IRenderedComponent<BoardPage> board = await ListWithItems(ctx, 20);
        int before = board.Instance.RowRenders;

        await board.InvokeAsync(() => board.ClickWhenReady(".tbl-wrap"));
        await board.InvokeAsync(() => { });

        Assert.Equal(0, board.Instance.RowRenders - before);

        // with an item examined the same click lets go of it, and that is a render
        await board.InvokeAsync(() => ctx.Services.GetRequiredService<UiService>().Examine((repo.Root, 123)));   // not by a click: bUnit does not bubble it to the wrapper, so the wrapper would not have reset itemClicked
        board.Eventually(() => Assert.True(RowMarkProbe.Examined(board, board.FindAll("tr[data-id]").First(r => r.GetAttribute("data-id") == "123"))));
        await board.InvokeAsync(() => board.ClickWhenReady(".tbl-wrap"));
        board.Eventually(() => Assert.False(RowMarkProbe.Examined(board, board.FindAll("tr[data-id]").First(r => r.GetAttribute("data-id") == "123"))));
    }

    // ── auto-hiding side menu ──

    private static string AppCss() => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "wwwroot", "css", "app.css")).Replace("\r\n", "\n");

    /// <summary>The rule of the css that brings the hidden menu back: from the edge zone's hover selector to the end of its block.</summary>
    private static string RevealRule()
    {
        string css = AppCss();
        int start = css.IndexOf(".side-edge:hover + nav.side", StringComparison.Ordinal);
        Assert.True(start > 0, "a rule that brings the hidden menu back");
        return css[start..css.IndexOf('}', start)];
    }

    [Fact(DisplayName = "Collapsed, the side menu is hidden and an edge zone is there to bring it back")]
    public async Task CollapsedSideMenuHasAnEdgeZone()
    {
        await using BunitContext ctx = NewContext();

        Assert.DoesNotContain("expanded", nav!.Find("nav.side").ClassName);
        IElement edge = Assert.Single(nav.FindAll(".side-edge"));
        Assert.Equal("true", edge.GetAttribute("aria-hidden"));   // no one reaches it by keyboard or screen reader: the rail's own buttons are the way in
        Assert.True(edge.NextElementSibling?.ClassList.Contains("side"), "the zone sits right before the menu, so the css can show the menu while the zone is hovered");

        string css = AppCss();
        Assert.Contains("nav.side:not(.expanded) { position: fixed;", css);   // out of the page's width: the page takes all of it
        Assert.Contains("translateX(-100%)", css[css.IndexOf("nav.side:not(.expanded) { position: fixed;", StringComparison.Ordinal)..][..400]);
        Assert.Matches(@"\.side-edge \{[^}]*position: fixed;[^}]*width: 8px;", css);
        Assert.Contains("#app:has(> nav.side:not(.expanded))", css);
    }

    [Fact(DisplayName = "Expanded, the side menu has no edge zone and is not hidden")]
    public async Task ExpandedSideMenuHasNoEdgeZone()
    {
        await using BunitContext ctx = NewContext();
        nav.ClickWhenReady("button.nav-toggle");

        Assert.Contains("expanded", nav.Find("nav.side").ClassName);
        Assert.Empty(nav.FindAll(".side-edge"));

        string css = AppCss();
        Assert.DoesNotContain("nav.side.expanded { position: fixed", css);   // only the collapsed menu leaves the page's layout
        Assert.DoesNotMatch(@"nav\.side\.expanded[^{}]*\{[^}]*translateX", css);
    }

    [Fact(DisplayName = "The side menu stays shown while one of its popups is open")]
    public async Task SideMenuStaysShownWhileAPopupIsOpen()
    {
        await using BunitContext ctx = NewContext();
        Assert.Empty(nav!.FindAll("nav.side .menu-backdrop"));

        nav.ClickWhenReady("nav.side .menu-activator");   // the repositories menu
        Assert.Single(nav.FindAll("nav.side .menu-backdrop"));   // an open popup is inside the menu's markup: the css keeps the menu while it holds one

        string rule = RevealRule();
        Assert.Contains("nav.side:not(.expanded):has(.menu-backdrop)", rule);
        Assert.Contains("nav.side:not(.expanded):has(:focus-visible)", rule);   // keyboard focus inside (a mouse click on a button must not pin it)
    }

    [Fact(DisplayName = "The hidden side menu slides in over the page and hides again after a short delay")]
    public void HiddenSideMenuSlidesInAndHidesAfterADelay()
    {
        string css = AppCss();
        string hidden = css[css.IndexOf("nav.side:not(.expanded) { position: fixed;", StringComparison.Ordinal)..];
        hidden = hidden[..hidden.IndexOf('}')];
        Assert.Contains("transform: translateX(-100%)", hidden);
        Assert.Matches(@"transition: transform 150ms [a-z-]+ 400ms", hidden);   // slides over 150 ms, and waits 400 ms before hiding

        string reveal = RevealRule();
        Assert.Contains(".side-edge:hover + nav.side:not(.expanded)", reveal);
        Assert.Contains("nav.side:not(.expanded):hover", reveal);
        Assert.Contains("*:not(nav.side:not(.expanded)) { transition: none !important; }", css);   // reduced motion drops the slide, not the wait: the global switch leaves this menu out
        Assert.Contains("transform: none", reveal);
        Assert.Contains("transition-delay: 0s", reveal);   // shown at once
        Assert.Contains("body.hints-on nav.side:not(.expanded)", reveal);   // the hint letters of the rail's buttons need it on screen

        Assert.Contains("hints-on", File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "wwwroot", "js", "app.js")));
    }
}
