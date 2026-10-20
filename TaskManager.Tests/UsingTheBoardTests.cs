using Bunit;
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
        Assert.Contains("Availability", card.Find(".card-features").TextContent);
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

        Assert.Equal(["Backlog", "Planned", "Confirmed", "In Progress", "Blocked", "Completed"], board.FindAll(".col-h h3").Select(h => h.TextContent.Trim()));
        Assert.Equal(4, board.FindAll("article.card").Count);
        Assert.Equal(["High priority", "Lower priority"], board.FindAll(".lane-h span:first-child").Select(s => s.TextContent.Trim()));
    }

    [Fact(DisplayName = "Switching to List shows a table row per work item, and clicking a header twice reverses its sort")]
    public async Task ListView()
    {
        await using BunitContext ctx = NewContext();
        IRenderedComponent<BoardPage> board = ctx.Render<BoardPage>();
        ShowList();

        Assert.Equal(4, board.FindAll("tr.lrow").Count);
        board.FindAll("button.th-sort").Single(b => b.TextContent.StartsWith("Title")).Click();
        Assert.Equal("Title ↑", board.Find("button.th-sort.on").TextContent.Trim());
        board.FindAll("button.th-sort").Single(b => b.TextContent.StartsWith("Title")).Click();
        Assert.Equal("Title ↓", board.Find("button.th-sort.on").TextContent.Trim());
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
        board.FindAll("tr.lrow").First(r => r.TextContent.Contains("#wi123")).QuerySelector("button.chev")!.Click();

        Assert.Equal(["1.1", "1.2", "1.3", "2.1"], board.FindAll("tr.subrow td.num").Select(td => td.TextContent.Trim()));
    }

    [Fact(DisplayName = "Completed rows in the list are read-only and Completed has no '+ Add item'")]
    public async Task CompletedRowsReadOnly()
    {
        await using BunitContext ctx = NewContext();
        IRenderedComponent<BoardPage> board = ctx.Render<BoardPage>();
        ShowList();

        AngleSharp.Dom.IElement row = board.FindAll("tr.lrow").Single(r => r.TextContent.Contains("#wi124"));
        Assert.Empty(row.QuerySelectorAll("button.dd"));
        Assert.Contains("Completed", row.TextContent);
        AngleSharp.Dom.IElement completed = board.FindAll("tbody").Single(b => b.QuerySelector(".grp")!.TextContent.Contains("Completed"));
        Assert.Empty(completed.QuerySelectorAll("tr.addrow"));
    }

    [Fact(DisplayName = "The expand-all chevron in the list header shows every subtask row")]
    public async Task ExpandAllFromViewMenu()
    {
        await using BunitContext ctx = NewContext();
        IRenderedComponent<BoardPage> board = ctx.Render<BoardPage>();
        ShowList();
        Assert.Empty(board.FindAll("tr.subrow"));

        board.FindAll("button").Single(b => b.TextContent.Trim() == "Expand all rows").Click();

        Assert.Equal(4 + 2 + 1 + 1, board.FindAll("tr.subrow").Count);
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

        Assert.Contains("hidden-item", board.FindAll("tr.lrow").Single(r => r.TextContent.Contains("#wi107")).ClassName);
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
        foreach (AngleSharp.Dom.IElement pick in board.FindAll("article.card input.pick").Take(2).ToList()) pick.Change(true);

        board.Find(".actionbar button.primary").Click();

        overlays!.WaitForAssertion(() => Assert.Contains("Merged into #wi", overlays.Find(".toast").TextContent));
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

        Assert.NotEmpty(board.FindAll("tr.lrow"));
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
        nav.Find("button[aria-label='Show Blocked']").Click();
        Assert.Contains("Blocked", Columns(board));
        Assert.Empty(nav.FindAll(".nav-hidden"));
    }

    [Fact(DisplayName = "Dragging the Backlog header onto Completed moves Backlog next to Completed")]
    public async Task DragColumn()
    {
        await using BunitContext ctx = NewContext();
        IRenderedComponent<BoardPage> board = ctx.Render<BoardPage>();

        board.FindAll("header.col-h").First(h => h.TextContent.Contains("Backlog")).DragStart();
        board.FindAll("section.col").Single(c => c.QuerySelector("h3")!.TextContent == "Completed").Drop();

        Assert.Equal(["Planned", "Confirmed", "In Progress", "Blocked", "Completed", "Backlog"], Columns(board));
    }

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
