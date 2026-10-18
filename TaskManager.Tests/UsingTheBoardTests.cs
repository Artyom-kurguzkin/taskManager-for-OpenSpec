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

    private BunitContext NewContext()
    {
        BunitContext ctx = new();
        ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        ctx.Services.AddMudServices();
        ctx.Services.AddSingleton(new SettingsStore(settingsFile));
        ctx.Services.AddSingleton<Workspace>();
        Assert.True(ctx.Services.GetRequiredService<Workspace>().Open(repo.Root).Success);
        ctx.Render<MudBlazor.MudPopoverProvider>();
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
