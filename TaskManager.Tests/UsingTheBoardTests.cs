using WorkItems.PublicDTOs;
using Radzen;
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
    private readonly TestRepo repo2 = new();   // a second project, opened as a tab by TwoProjects
    private readonly string settingsFile = Path.Combine(Path.GetTempPath(), "rtm-ui-" + Guid.NewGuid().ToString("N") + ".json");

    private IRenderedComponent<Overlays>? overlays;
    private IRenderedComponent<SideNav>? nav;

    private BunitContext NewContext()
    {
        BunitContext ctx = new();
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
            settings.SaveBoard(repo.Root, settings.Board(repo.Root) with { GroupBy = by });
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
        await board.InvokeAsync(() => { });   // let the dispatcher drain, so the drop targets the current render tree
        GroupRows(board, "Planned")[0].Drop();
        board.WaitForAssertion(() => Assert.Equal([before[2], before[0], before[1]], RowsIn(board, "Planned")), TimeSpan.FromSeconds(5));   // the grid reloads after a drop; slow under a full-suite load

        GroupRows(board, "Planned")[0].DragStart();
        await board.InvokeAsync(() => { });
        Group(board, "In Progress").Drop();   // the group row stands for the group's end
        board.WaitForAssertion(() => Assert.Equal([before[0], before[1]], RowsIn(board, "Planned")), TimeSpan.FromSeconds(5));
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
        nav.FindAll(".field").Single(f => f.TextContent.Contains("group by")).QuerySelector(".dd")!.Click();
        nav.WaitForAssertion(() => Assert.Contains(nav.FindAll(".rz-dropdown-item"), i => i.TextContent.Trim() == "feature"));
        nav.FindAll(".rz-dropdown-item").First(i => i.TextContent.Trim() == "feature").Click();

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
        settings.SaveBoard(repo.Root, settings.Board(repo.Root) with { GroupBy = "label" });
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

    [Fact(DisplayName = "The calendar view shows the month in weeks from Monday, each dated item as a bar from its start to its due date, items without dates in a side list, and a bar opens its item")]
    public async Task CalendarView()
    {
        await using BunitContext ctx = NewContext();
        DateOnly today = DateOnly.FromDateTime(DateTime.Today);
        WorkItemsApi.SetStarted(123, today);
        WorkItemsApi.SetDue(123, today.AddDays(1));
        IRenderedComponent<BoardPage> board = ctx.Render<BoardPage>();

        nav!.Find("button[aria-label=Layout]").Click();
        nav.FindAll(".menu-item").Single(i => i.TextContent.Trim() == "Calendar").Click();

        board.WaitForAssertion(() => Assert.NotEmpty(board.FindAll(".cal")));
        Assert.Contains(DateTime.Today.ToString("MMMM yyyy"), board.Find(".cal-h").TextContent);
        Assert.Equal(["Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun"], board.FindAll(".cal-dow").Select(d => d.TextContent.Trim()));
        Assert.Single(board.FindAll(".cal-day.today"));
        AngleSharp.Dom.IElement bar = board.FindAll(".cal-bar[data-id='123']").First();
        Assert.Contains("Health-check endpoint", bar.TextContent);
        Assert.Contains(WorkItemsApi.Get(107)!.Title, board.Find(".cal-unscheduled").TextContent);   // no dates
        Assert.DoesNotContain("Health-check", board.Find(".cal-unscheduled").TextContent);

        board.Find(".cal-next").Click();
        board.WaitForAssertion(() => Assert.Contains(DateTime.Today.AddMonths(1).ToString("MMMM yyyy"), board.Find(".cal-h").TextContent));
        board.Find(".cal-today").Click();
        board.WaitForAssertion(() => Assert.Contains(DateTime.Today.ToString("MMMM yyyy"), board.Find(".cal-h").TextContent));

        board.FindAll(".cal-bar[data-id='123']").First().Click();
        Assert.EndsWith("/item/123", ctx.Services.GetRequiredService<NavigationManager>().Uri);
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
        nav.FindAll(".field").Single(f => f.TextContent.Contains("group by")).QuerySelector(".dd")!.Click();
        nav.FindAll(".rz-dropdown-item").First(i => i.TextContent.Trim() == "due date").Click();
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

    [Fact(DisplayName = "A column shows priority subgroups when its cards differ in priority, and dropping a card on a subgroup sets its priority")]
    public async Task PrioritySubgroups()
    {
        await using BunitContext ctx = NewContext();
        Workspace workspace = ctx.Services.GetRequiredService<Workspace>();
        int urgent = workspace.CreateItem("Backlog").Id, other = workspace.CreateItem("Backlog").Id;
        Assert.True(WorkItemsApi.SetPriority(urgent, "high").Success);
        IRenderedComponent<BoardPage> board = ctx.Render<BoardPage>();

        Assert.Equal(["High priority", "Lower priority"], Col(board, "Backlog").QuerySelectorAll(".lane-h.prio span:first-child").Select(s => s.TextContent.Trim()));
        Assert.Empty(Col(board, "Planned").QuerySelectorAll(".lane-h.prio"));   // one priority there: no heading

        AngleSharp.Dom.IElement HighLane() => Col(board, "Backlog").QuerySelector(".lane-h.prio[data-prio=high] + .lane")!;
        Assert.Single(HighLane().QuerySelectorAll("article.card"));
        Col(board, "Backlog").QuerySelectorAll(".lane-h.prio[data-prio=low] + .lane article.card").First(c => c.TextContent.Contains($"#wi{other}")).DragStart();
        HighLane().Drop();

        board.WaitForAssertion(() => Assert.Equal("high", WorkItemsApi.Get(other)!.Priority));
        board.WaitForAssertion(() => Assert.Equal(2, HighLane().QuerySelectorAll("article.card").Length));   // the fixture's own Backlog item stays in the low lane
        Assert.Equal(3, CardsIn(board, "Backlog").Count);
    }

    // The layout dropdown in the side menu switches the board to the list.
    private void ShowList()
    {
        nav!.Find("button[aria-label=Layout]").Click();
        nav.FindAll(".menu-item").Single(i => i.TextContent.Trim() == "List").Click();
    }

    private void GroupByFeature()
    {
        nav!.Find("button.nav-toggle").Click();
        nav.FindAll(".field").Single(f => f.TextContent.Contains("group by")).QuerySelector(".dd")!.Click();
        nav.FindAll(".rz-dropdown-item").First(i => i.TextContent.Trim() == "feature").Click();
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
        Assert.True(ws.Open(repo.Root).Success);
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

    [Fact(DisplayName = "Agent-added work shows an agent chip on its card; clicking it marks the item and its agent subtasks reviewed without opening it")]
    public async Task AgentChipOnCard()
    {
        EditingWorkItemsTests.StampAgent(repo);
        repo.Write("openspec/changes/add-health-check/tasks.md", repo.Read("openspec/changes/add-health-check/tasks.md").TrimEnd() + "\n- [ ] 2.2 Agent drafted this <!-- agent: Claude -->\n");
        await using BunitContext ctx = NewContext();
        IRenderedComponent<BoardPage> board = ctx.Render<BoardPage>();
        Assert.Single(board.FindAll("article.card .agent-chip"));

        board.FindAll("article.card").Single(c => c.TextContent.Contains("Health-check endpoint")).QuerySelector(".agent-chip")!.Click();

        board.WaitForAssertion(() => Assert.Empty(board.FindAll("article.card .agent-chip")));
        Assert.DoesNotContain("/item/", ctx.Services.GetRequiredService<NavigationManager>().Uri);
        Assert.Contains("reviewedAt", repo.Read("openspec/changes/add-health-check/.openspec.yaml"));
        Assert.DoesNotContain("<!-- agent", repo.Read("openspec/changes/add-health-check/tasks.md"));
    }

    [Fact(DisplayName = "In the list the agent chip sits on the row and on each agent subtask; a subtask's chip clears only that subtask")]
    public async Task AgentChipsInList()
    {
        EditingWorkItemsTests.StampAgent(repo);
        repo.Write("openspec/changes/add-health-check/tasks.md", repo.Read("openspec/changes/add-health-check/tasks.md").TrimEnd() + "\n- [ ] 2.2 Agent drafted this <!-- agent: Claude -->\n");
        await using BunitContext ctx = NewContext();
        IRenderedComponent<BoardPage> board = ctx.Render<BoardPage>();
        ShowList();
        board.WaitForAssertion(() => Assert.Single(board.FindAll("tr[data-id] .tcell .agent-chip")));
        board.FindAll("tr[data-id]").First(r => r.TextContent.Contains("#wi123")).QuerySelector("button.chev")!.Click();   // subtasks start folded
        board.WaitForAssertion(() => Assert.NotEmpty(board.FindAll(".subrow")));
        Assert.Single(board.FindAll(".subrow .agent-chip"));

        board.Find(".subrow .agent-chip").Click();

        board.WaitForAssertion(() => Assert.Empty(board.FindAll(".subrow .agent-chip")));
        Assert.Single(board.FindAll("tr[data-id] .tcell .agent-chip"));   // the item itself is still flagged
        Assert.DoesNotContain("<!-- agent", repo.Read("openspec/changes/add-health-check/tasks.md"));
    }

    [Fact(DisplayName = "The side menu can show only work with unreviewed agent changes")]
    public async Task AgentOnlyFilter()
    {
        EditingWorkItemsTests.StampAgent(repo);
        await using BunitContext ctx = NewContext();
        IRenderedComponent<BoardPage> board = ctx.Render<BoardPage>();
        Assert.True(board.FindAll("article.card").Count > 1);
        nav!.Find("button.nav-toggle").Click();

        nav.Find("input#f-agent").Change(true);

        board.WaitForAssertion(() => Assert.Equal(["Health-check endpoint"], board.FindAll("article.card .band").Select(b => b.TextContent.Trim())));
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

    [Fact(DisplayName = "A project collapses from its chevron and hides from its menu; the side menu lists a hidden project with an eye to bring it back")]
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

        board.FindAll(".proj .proj-h button[aria-label='Project options']")[1].Click();
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
}
