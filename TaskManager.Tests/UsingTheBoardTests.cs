using Bunit;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor.Services;
using TaskManager.Board;
using TaskManager.Components.Pages;
using TaskManager.Services;
using WorkItems;

namespace TaskManager.Tests;

/// <summary>
/// Renders the real pages against a temp copy of the fixture repo. Each test owns its bUnit context and disposes
/// it asynchronously (MudBlazor's services are IAsyncDisposable only).
/// </summary>
[Collection(nameof(ModuleStateCollection))]
public sealed class UsingTheBoardTests : IDisposable
{
    private readonly TestRepo repo = new();
    private readonly string settingsFile = Path.Combine(Path.GetTempPath(), "rtm-ui-" + Guid.NewGuid().ToString("N") + ".json");

    private IRenderedComponent<MudBlazor.MudPopoverProvider>? popovers;

    private BunitContext NewContext()
    {
        BunitContext ctx = new();
        ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        ctx.Services.AddMudServices();
        ctx.Services.AddSingleton(new SettingsStore(settingsFile));
        ctx.Services.AddSingleton<Workspace>();
        Assert.True(ctx.Services.GetRequiredService<Workspace>().Open(repo.Root).Success);
        popovers = ctx.Render<MudBlazor.MudPopoverProvider>();
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
        board.Find("input[name=view][value=list]").Change("list");

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

        board.Find("input#f-hidden").Change(true);

        Assert.Equal(4, board.FindAll("article.card").Count);
        Assert.Single(board.FindAll("article.card.hidden-item"));
    }

    [Fact(DisplayName = "Opening the group-by dropdown and picking feature regroups the board")]
    public async Task DropdownOpensAndPicks()
    {
        await using BunitContext ctx = NewContext();
        IRenderedComponent<BoardPage> board = ctx.Render<BoardPage>();

        board.FindAll(".field").Single(f => f.TextContent.Contains("group by")).QuerySelector("button.dd")!.Click();
        popovers!.WaitForAssertion(() => Assert.Contains(popovers.FindAll(".mud-menu-item"), i => i.TextContent.Trim() == "feature"));
        popovers.FindAll(".mud-menu-item").First(i => i.TextContent.Trim() == "feature").Click();

        board.WaitForAssertion(() => Assert.NotEmpty(board.FindAll(".cat-tabs")));
    }

    [Fact(DisplayName = "Every board column and sub-category lane is a drop zone the drag script can accept drops on")]
    public async Task DropZonesMarked()
    {
        await using BunitContext ctx = NewContext();
        IRenderedComponent<BoardPage> board = ctx.Render<BoardPage>();

        Assert.All(board.FindAll("section.col"), c => Assert.True(c.HasAttribute("data-drop")));
        Assert.All(board.FindAll(".lane"), l => Assert.True(l.HasAttribute("data-drop")));
        Assert.Contains("data-drop", File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "wwwroot", "js", "app.js")));
    }

    [Fact(DisplayName = "The 'All groups' tab shows no misleading count")]
    public async Task AllGroupsHasNoCount()
    {
        await using BunitContext ctx = NewContext();
        IRenderedComponent<BoardPage> board = ctx.Render<BoardPage>();
        board.FindAll(".field").Single(f => f.TextContent.Contains("group by")).QuerySelector("button.dd")!.Click();
        popovers!.WaitForAssertion(() => Assert.NotEmpty(popovers.FindAll(".mud-menu-item")));
        popovers.FindAll(".mud-menu-item").First(i => i.TextContent.Trim() == "feature").Click();

        board.WaitForAssertion(() => Assert.Empty(board.Find("label.seg-o:has(input[value=all])").QuerySelectorAll(".count")));
    }

    [Fact(DisplayName = "An expanded list row numbers its subtasks 1.1, 1.2 …")]
    public async Task ListSubtaskNumbers()
    {
        await using BunitContext ctx = NewContext();
        IRenderedComponent<BoardPage> board = ctx.Render<BoardPage>();
        board.Find("input[name=view][value=list]").Change("list");
        board.FindAll("tr.lrow").First(r => r.TextContent.Contains("#wi123")).QuerySelector("button.chev")!.Click();

        Assert.Equal(["1.1", "1.2", "1.3", "2.1"], board.FindAll("tr.subrow td.num").Select(td => td.TextContent.Trim()));
    }

    [Fact(DisplayName = "Completed rows in the list are read-only and Completed has no '+ Add item'")]
    public async Task CompletedRowsReadOnly()
    {
        await using BunitContext ctx = NewContext();
        IRenderedComponent<BoardPage> board = ctx.Render<BoardPage>();
        board.Find("input[name=view][value=list]").Change("list");

        AngleSharp.Dom.IElement row = board.FindAll("tr.lrow").Single(r => r.TextContent.Contains("#wi124"));
        Assert.Empty(row.QuerySelectorAll("button.dd"));
        Assert.Contains("Completed", row.TextContent);
        AngleSharp.Dom.IElement completed = board.FindAll("tbody").Single(b => b.QuerySelector(".grp")!.TextContent.Contains("Completed"));
        Assert.Empty(completed.QuerySelectorAll("tr.addrow"));
    }

    [Fact(DisplayName = "A hidden work item's row is marked as hidden in the list")]
    public async Task HiddenRowMarked()
    {
        await using BunitContext ctx = NewContext();
        WorkItemsApi.Hide(107);
        IRenderedComponent<BoardPage> board = ctx.Render<BoardPage>();
        board.Find("input[name=view][value=list]").Change("list");
        board.Find("input#f-hidden").Change(true);

        Assert.Contains("hidden-item", board.FindAll("tr.lrow").Single(r => r.TextContent.Contains("#wi107")).ClassName);
    }

    [Fact(DisplayName = "Clicking a column header opens its menu")]
    public async Task ColumnMenuOpens()
    {
        await using BunitContext ctx = NewContext();
        IRenderedComponent<BoardPage> board = ctx.Render<BoardPage>();

        board.FindAll("header.col-h").First(h => h.TextContent.Contains("Blocked")).Click();

        popovers!.WaitForAssertion(() => Assert.Contains(popovers.FindAll(".mud-menu-item"), i => i.TextContent.Contains("Add sub-category")));
    }

    [Fact(DisplayName = "Showing subtasks puts one card per tasks.md checkbox on the board")]
    public async Task SubtaskCards()
    {
        await using BunitContext ctx = NewContext();
        IRenderedComponent<BoardPage> board = ctx.Render<BoardPage>();
        board.Find("input[name=unit][value=subtasks]").Change("subtasks");
        Assert.Equal(4 + 2 + 1 + 1, board.FindAll("article.subcard").Count);
    }

    public void Dispose()
    {
        repo.Dispose();
        File.Delete(settingsFile);
    }
}
