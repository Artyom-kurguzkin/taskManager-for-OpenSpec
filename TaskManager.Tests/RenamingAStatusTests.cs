using Bunit;
using Microsoft.Extensions.DependencyInjection;
using OpenSpecStore;
using Radzen;
using TaskManager.Components.Pages;
using TaskManager.Components.Shared;
using TaskManager.AppState;
using WorkItems;

namespace TaskManager.Tests;

/// <summary>
/// #wi229: a status that is not Backlog, In Progress or Completed is renamed from where it is seen. #wi226 / #wi230: the list's group row opens the same menu as the board's column
/// header (a click on its name), so every action of a column is there for a group too.
/// </summary>
public sealed class RenamingAStatusTests : IDisposable
{
    private const string Config = "openspec/config.yaml";
    private readonly TestRepo repo = new();
    private readonly string settingsFile = Path.Combine(Path.GetTempPath(), "rtm-rename-" + Guid.NewGuid().ToString("N") + ".json");

    public void Dispose()
    {
        repo.Dispose();
        File.Delete(settingsFile);
    }

    [Fact(DisplayName = "Renaming a status moves every item that has it")]
    public void RenamingMovesTheItems()
    {
        OpenSpecStoreApi.Open(repo.Root);
        Assert.True(WorkItemsApi.AddStatus("Review ready").Success);
        Assert.True(WorkItemsApi.SetStatus(107, "Review ready").Success);
        Assert.True(WorkItemsApi.SetStatus(125, "Review ready").Success);
        string moved = WorkItemsApi.Get(107)!.Moved?.ToString() ?? "";

        (bool ok, string message) = WorkItemsApi.RenameStatus("Review ready", "  Needs review ");

        Assert.True(ok, message);
        Assert.Equal("Needs review", WorkItemsApi.Get(107)!.Status);
        Assert.Equal("Needs review", WorkItemsApi.Get(125)!.Status);
        Assert.Equal(2, WorkItemsApi.CountWithStatus("Needs review"));
        Assert.Equal(0, WorkItemsApi.CountWithStatus("Review ready"));
        Assert.Contains("Needs review", WorkItemsApi.GetStatuses());
        Assert.DoesNotContain("Review ready", WorkItemsApi.GetStatuses());
        Assert.Equal(moved, WorkItemsApi.Get(107)!.Moved?.ToString() ?? "");   // a rename is not a move: "in status since" stays
    }

    [Fact(DisplayName = "Renaming a status keeps the project's status list order")]
    public void RenamingKeepsTheListOrder()
    {
        OpenSpecStoreApi.Open(repo.Root);

        Assert.True(WorkItemsApi.RenameStatus("Planned", "Scoped").Success);

        Assert.Equal(["Backlog", "Scoped", "Confirmed", "In Progress", "Blocked", "In Review", "Completed"], WorkItemsApi.GetStatuses());
        Assert.Equal("Scoped", WorkItemsApi.Get(107)!.Status);   // it was Planned
        Assert.Contains("Scoped", repo.Read(Config));
    }

    [Fact(DisplayName = "Renaming a status carries hidden and merged board columns")]
    public void RenamingCarriesTheBoardOptions()
    {
        BoardOptions o = new()
        {
            GroupBy = "status",
            Hidden = ["status:Planned", "status:Blocked", "feature:Planned"],
            Order = ["status:Blocked", "status:Planned", "item:7"],
            Merges = [new("Ready", ["Planned", "Confirmed"]), new("Planned", ["Blocked"])],
        };

        BoardOptions renamed = BoardModel.RenameStatus(o, "Planned", "Scoped");

        Assert.Equal(["status:Scoped", "status:Blocked", "feature:Planned"], renamed.Hidden);   // only the status key is the status
        Assert.Equal(["status:Blocked", "status:Scoped", "item:7"], renamed.Order);
        Assert.Equal(["Scoped", "Confirmed"], renamed.Merges![0].Statuses);
        Assert.Equal("Ready", renamed.Merges[0].Name);
        Assert.Equal(["Blocked"], renamed.Merges[1].Statuses);
        Assert.Equal("Planned", renamed.Merges[1].Name);   // a merge's own name is what the user typed: it stays
    }

    [Fact(DisplayName = "A core status cannot be renamed")]
    public void ACoreStatusStays()
    {
        OpenSpecStoreApi.Open(repo.Root);

        foreach (string core in new[] { "Backlog", "In Progress", "Completed", "backlog", "in progress", "COMPLETED" })
        {
            (bool ok, string message) = WorkItemsApi.RenameStatus(core, "Something else");
            Assert.False(ok);
            Assert.Contains("stays", message);
        }
        Assert.Equal(["Backlog", "Planned", "Confirmed", "In Progress", "Blocked", "In Review", "Completed"], WorkItemsApi.GetStatuses());
        Assert.False(WorkItemsApi.RenameStatus("Nowhere", "Anywhere").Success);   // a status the project does not have
        Assert.False(BoardModel.CanRenameStatus(new BoardOptions(), ["Backlog", "Planned", "In Progress", "Completed"], "Backlog"));
        Assert.True(BoardModel.CanRenameStatus(new BoardOptions(), ["Backlog", "Planned", "In Progress", "Completed"], "Planned"));
    }

    [Fact(DisplayName = "A status cannot be renamed to a name already in use")]
    public void ANameInUseIsRefused()
    {
        OpenSpecStoreApi.Open(repo.Root);

        Assert.False(WorkItemsApi.RenameStatus("Planned", "Confirmed").Success);   // another status of the project
        Assert.False(WorkItemsApi.RenameStatus("Planned", "Backlog").Success);   // a core one
        Assert.False(WorkItemsApi.RenameStatus("Planned", "Planned").Success);   // itself
        Assert.False(WorkItemsApi.RenameStatus("Planned", "   ").Success);   // nothing typed
        Assert.False(WorkItemsApi.RenameStatus("Planned", new string('x', 41)).Success);   // as long as AddStatus allows
        Assert.Equal("Planned", WorkItemsApi.Get(107)!.Status);
        Assert.Contains("Planned", WorkItemsApi.GetStatuses());
        Assert.True(WorkItemsApi.RenameStatus("Planned", "planned").Success);   // spelling counts, as for a new status
    }

    private IRenderedComponent<Overlays>? overlays;

    private BunitContext NewContext()
    {
        BunitContext ctx = new();
        ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        ctx.Services.AddSingleton<UiService>();
        ctx.Services.AddRadzenComponents();
        ctx.Services.AddSingleton(new SettingsStore(settingsFile));
        ctx.Services.AddSingleton<Workspace>();
        Assert.True(ctx.Services.GetRequiredService<Workspace>().Open(repo.Root).Success);
        overlays = ctx.Render<Overlays>();
        return ctx;
    }

    private static List<string> MenuItems(IRenderedComponent<BoardPage> board) => board.FindAll(".menu-item").Select(i => i.TextContent.Trim()).ToList();

    [Fact(DisplayName = "The list group menu offers the same actions as the board column menu")]
    public async Task TheListGroupMenuIsTheColumnMenu()
    {
        await using BunitContext ctx = NewContext();
        Assert.True(WorkItemsApi.AddStatus("Review ready").Success);
        Assert.True(WorkItemsApi.SetStatus(107, "Review ready").Success);
        IRenderedComponent<BoardPage> board = ctx.Render<BoardPage>();
        board.Eventually(() => Assert.Contains("Review ready", board.FindAll("section.col h3").Select(h => h.TextContent)));

        board.Eventually(() => board.FindAll("header.col-h").First(h => h.QuerySelector("h3")!.TextContent == "Review ready").Click());
        board.Eventually(() => Assert.Contains("Rename status…", MenuItems(board)));
        List<string> column = MenuItems(board);
        board.ClickWhenReady(".menu-backdrop");

        SettingsStore settings = ctx.Services.GetRequiredService<SettingsStore>();
        settings.SaveBoard(repo.Root, settings.Board(repo.Root) with { View = "list", ListGroupBy = "status", ListSwimlane = "none" });
        board.Eventually(() => Assert.NotEmpty(board.FindAll("tr[data-group='Review ready']")));
        Assert.Empty(board.FindAll("tr[data-group] button.gh-menu"));   // no "…" button: the group's name is the way in

        board.ClickWhenReady("tr[data-group='Review ready'] button.gname-btn");
        board.Eventually(() => Assert.Contains("Rename status…", MenuItems(board)));
        List<string> group = MenuItems(board);

        Assert.Equal(column.Select(i => i.Replace("column", "group")), group);   // the same actions, in the same order; only the noun differs
        Assert.Contains("Hide group", group);
        Assert.Contains("Delete status…", group);
        board.ClickWhenReady(".menu-backdrop");

        board.ClickWhenReady("tr[data-group='Backlog'] button.gname-btn");   // a core status: no rename, no delete, but the rest
        board.Eventually(() => Assert.Contains("Hide group", MenuItems(board)));
        Assert.DoesNotContain(MenuItems(board), i => i.Contains("Rename status") || i.Contains("Delete status"));
        board.ClickWhenReady(".menu-backdrop");

        board.ClickWhenReady("tr[data-group='Review ready'] button.gname-btn");   // Hide group takes it off the list, as Hide column does the board
        board.FireWhenReady(".menu-item", e => e.Click(), all => all.Single(i => i.TextContent.Trim() == "Hide group"));
        board.Eventually(() => Assert.Empty(board.FindAll("tr[data-group='Review ready']")));
        Assert.Contains("status:Review ready", settings.Board(repo.Root).Hidden!);
    }

    [Fact(DisplayName = "Rename status… in a column's menu asks for the new name, prefilled, and the column keeps its place and its hidden state")]
    public async Task RenameFromTheBoardColumn()
    {
        await using BunitContext ctx = NewContext();
        IRenderedComponent<BoardPage> board = ctx.Render<BoardPage>();
        board.Eventually(() => Assert.Contains("Planned", board.FindAll("section.col h3").Select(h => h.TextContent)));
        List<string> before = board.FindAll("section.col h3").Select(h => h.TextContent).ToList();

        board.Eventually(() => board.FindAll("header.col-h").First(h => h.QuerySelector("h3")!.TextContent == "Planned").Click());
        board.FireWhenReady(".menu-item", e => e.Click(), all => all.Single(i => i.TextContent.Trim() == "Rename status…"));
        overlays!.Eventually(() => Assert.Equal("Planned", overlays.Find(".dialog input").GetAttribute("value")));
        overlays.InputWhenReady(".dialog input", "Scoped");
        overlays.FireWhenReady(".dialog button", e => e.Click(), all => all.Single(x => x.TextContent.Trim() == "Rename"));

        board.Eventually(() => Assert.Equal(before.Select(c => c == "Planned" ? "Scoped" : c), board.FindAll("section.col h3").Select(h => h.TextContent)));
        Assert.Equal("Scoped", WorkItemsApi.Get(107)!.Status);   // it was Planned
        Assert.Contains("Scoped", repo.Read(Config));
    }
}
