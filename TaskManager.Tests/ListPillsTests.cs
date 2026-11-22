using AngleSharp.Dom;
using Bunit;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Radzen;
using Radzen.Blazor;
using TaskManager.Components.Pages;
using TaskManager.Components.Layout;
using TaskManager.Components.Shared;
using TaskManager.AppState;
using TaskManager.BackgroundJobs;
using TaskManager.Helpers;
using WorkItems;

namespace TaskManager.Tests;

/// <summary>
/// The list's status, label and priority cells are plain buttons; one shared picker opens over the cell that was clicked
/// (a full RadzenDropDown per cell made every render of the list cost thousands of component renders).
/// </summary>
public sealed class ListPillsTests : IDisposable
{
    private readonly TestRepo repo = new();
    private readonly string settingsFile = Path.Combine(Path.GetTempPath(), "rtm-ui-" + Guid.NewGuid().ToString("N") + ".json");

    private IRenderedComponent<Overlays>? overlays;

    private BunitContext NewContext()
    {
        BunitContext ctx = new();
        ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        ctx.Services.AddSingleton<UiService>();
        ctx.Services.AddRadzenComponents();
        ctx.Services.AddSingleton(new SettingsStore(settingsFile));
        ctx.Services.AddSingleton<Workspace>();
        OpenSpecStore.OpenSpecStoreModule.State.RealWatcher = false;   // the counts are read between steps: a late event from the disk would reload the page in the middle of one
        Assert.True(ctx.Services.GetRequiredService<Workspace>().Open(repo.Root).Success);
        overlays = ctx.Render<Overlays>();
        return ctx;
    }

    /// <summary>The list as it opens on a fresh repository, grouped like the board.</summary>
    private async Task<IRenderedComponent<BoardPage>> ListAsync(BunitContext ctx)
    {
        IRenderedComponent<BoardPage> board = ctx.Render<BoardPage>();
        IRenderedComponent<SideNav> nav = ctx.Render<SideNav>();
        nav.ClickWhenReady("button[aria-label=Layout]");
        nav.FireWhenReady(".menu-item", e => e.Click(), all => all.Single(i => i.TextContent.Trim() == "List"));
        SettingsStore settings = ctx.Services.GetRequiredService<SettingsStore>();
        BoardOptions o = settings.Board(repo.Root);
        settings.SaveBoard(repo.Root, o with { ListGroupBy = o.GroupBy, ListSwimlane = o.Swimlane });
        await board.InvokeAsync(() => { });
        board.WaitForAssertion(() => Assert.NotEmpty(board.FindAll("tr[data-id]")));
        return board;
    }

    private static IElement Row(IRenderedComponent<BoardPage> board, int id) => board.FindAll("tr[data-id]").First(r => r.TextContent.Contains($"#wi{id}"));

    private static IElement Pill(IRenderedComponent<BoardPage> board, int id, string kind) => Row(board, id).QuerySelector($"button[data-pill={kind}]")!;

    [Fact(DisplayName = "The list renders no pill editors until a pill is clicked")]
    public async Task NoEditorsUntilClicked()
    {
        await using BunitContext ctx = NewContext();
        IRenderedComponent<BoardPage> board = await ListAsync(ctx);

        Assert.Empty(board.FindComponents<RadzenDropDown<string>>());   // not one per cell, closed
        Assert.NotEmpty(board.FindAll("tr[data-id] button[data-pill=status]"));
        Assert.NotEmpty(board.FindAll("tr[data-id] button[data-pill=label]"));
        Assert.NotEmpty(board.FindAll("tr[data-id] button[data-pill=priority]"));
        Assert.Empty(board.FindAll(".menu-pop"));

        Act(board, () => Pill(board, 107, "status"), e => e.Click());
        board.WaitForAssertion(() => Assert.Single(board.FindAll(".menu-pop")));   // one picker, for the one cell
        Assert.Empty(board.FindComponents<RadzenDropDown<string>>());

        board.FireWhenReady(".menu-backdrop", e => e.Click(), all => all.Single());   // the open picker's backdrop covers the page: the next click lands on it first
        board.WaitForAssertion(() => Assert.Empty(board.FindAll(".menu-pop")));
        Act(board, () => Pill(board, 123, "priority"), e => e.Click());
        board.WaitForAssertion(() => Assert.Single(board.FindAll(".menu-pop")));   // never two at a time
    }

    [Fact(DisplayName = "A list pill looks like the read-only pill: its colour fill, its text, and a menu of the field's values")]
    public async Task PillLooksFilledAndListsValues()
    {
        await using BunitContext ctx = NewContext();
        IRenderedComponent<BoardPage> board = await ListAsync(ctx);

        IElement status = Pill(board, 107, "status");
        Assert.Contains("filled", status.ClassList);
        Assert.Contains("--fill:", status.GetAttribute("style"));
        Assert.Equal("Planned", status.TextContent.Trim());

        Act(board, () => status, e => e.Click());
        board.WaitForAssertion(() => Assert.Single(board.FindAll(".menu-pop")));
        string[] items = [.. board.FindAll(".menu-pop .menu-item").Select(i => i.TextContent.Trim())];
        Assert.Equal(["Backlog", "Planned", "Confirmed", "In Progress", "Blocked", "In Review", "Completed"], items);
        Assert.Equal(["Planned"], board.FindAll(".menu-pop .menu-item.cur").Select(i => i.TextContent.Trim()));   // the current value is marked
    }

    [Fact(DisplayName = "Picking a status from a list pill sets it")]
    public async Task PickingStatusSetsIt()
    {
        await using BunitContext ctx = NewContext();
        IRenderedComponent<BoardPage> board = await ListAsync(ctx);
        Assert.Equal("Planned", WorkItemsApi.Get(107)!.Status);

        Act(board, () => Pill(board, 107, "status"), e => e.Click());
        board.FireWhenReady(".menu-pop .menu-item", e => e.Click(), all => all.Single(i => i.TextContent.Trim() == "Blocked"));

        board.WaitForAssertion(() => Assert.Equal("Blocked", WorkItemsApi.Get(107)!.Status));
        board.WaitForAssertion(() => Assert.Empty(board.FindAll(".menu-pop")));   // picking closes it
    }

    [Fact(DisplayName = "Picking a label or a priority from a list pill sets it")]
    public async Task PickingLabelAndPriority()
    {
        await using BunitContext ctx = NewContext();
        IRenderedComponent<BoardPage> board = await ListAsync(ctx);

        Act(board, () => Pill(board, 107, "label"), e => e.Click());
        board.FireWhenReady(".menu-pop .menu-item", e => e.Click(), all => all.Single(i => i.TextContent.Trim() == "bug"));
        board.WaitForAssertion(() => Assert.Equal("bug", WorkItemsApi.Get(107)!.Label));

        Act(board, () => Pill(board, 107, "priority"), e => e.Click());
        board.FireWhenReady(".menu-pop .menu-item", e => e.Click(), all => all.Single(i => i.TextContent.Trim() == "high"));
        board.WaitForAssertion(() => Assert.Equal("high", WorkItemsApi.Get(107)!.Priority));
    }

    [Fact(DisplayName = "Picking Completed from a list pill still asks first")]
    public async Task CompletedAsksFirst()
    {
        await using BunitContext ctx = NewContext();
        IRenderedComponent<BoardPage> board = await ListAsync(ctx);

        Act(board, () => Pill(board, 107, "status"), e => e.Click());
        board.FireWhenReady(".menu-pop .menu-item", e => e.Click(), all => all.Single(i => i.TextContent.Trim() == "Completed"));

        overlays!.WaitForAssertion(() => Assert.Contains("Complete and archive?", overlays.Find(".dialog").TextContent));
        Assert.Equal("Planned", WorkItemsApi.Get(107)!.Status);   // nothing happens before the answer
        overlays.FireWhenReady(".dialog button", e => e.Click(), all => all.Single(b => b.TextContent.Trim() == "Cancel"));

        overlays.WaitForAssertion(() => Assert.Empty(overlays.FindAll(".dialog")));
        Assert.Equal("Planned", WorkItemsApi.Get(107)!.Status);
    }

    [Fact(DisplayName = "A list pill opens its picker from the keyboard")]
    public async Task OpensFromKeyboard()
    {
        await using BunitContext ctx = NewContext();
        IRenderedComponent<BoardPage> board = await ListAsync(ctx);

        Assert.Equal("BUTTON", Pill(board, 107, "status").TagName);   // focusable, so Tab reaches it
        Act(board, () => Pill(board, 107, "status"), e => e.KeyDown(new KeyboardEventArgs { Key = "Enter" }));
        board.WaitForAssertion(() => Assert.Single(board.FindAll(".menu-pop")));
        board.FireWhenReady(".menu-backdrop", e => e.Click(), all => all.Single());   // outside click (Esc clicks it, see app.js)
        board.WaitForAssertion(() => Assert.Empty(board.FindAll(".menu-pop")));

        Act(board, () => Pill(board, 107, "priority"), e => e.KeyDown(new KeyboardEventArgs { Key = " " }));
        board.WaitForAssertion(() => Assert.Single(board.FindAll(".menu-pop")));
    }

    [Fact(DisplayName = "A Shift-click on a list pill selects the row and does not open the picker")]
    public async Task ShiftClickDoesNotOpen()
    {
        await using BunitContext ctx = NewContext();
        IRenderedComponent<BoardPage> board = await ListAsync(ctx);

        Act(board, () => Pill(board, 107, "status"), e => e.Click(new MouseEventArgs { ShiftKey = true }));
        await board.InvokeAsync(() => { });
        Assert.Empty(board.FindAll(".menu-pop"));
    }

    [Fact(DisplayName = "An archived item's status in the list is a read-only pill, its other pills open pickers")]
    public async Task ArchivedStatusReadOnly()
    {
        await using BunitContext ctx = NewContext();
        IRenderedComponent<BoardPage> board = await ListAsync(ctx);

        SettingsStore settings = ctx.Services.GetRequiredService<SettingsStore>();
        settings.SaveBoard(repo.Root, settings.Board(repo.Root) with { ListGroupBy = "label" });   // the archived item shows among the others
        board.WaitForAssertion(() => Assert.Contains(board.FindAll("tr[data-id]"), r => r.TextContent.Contains("#wi124")));
        IElement row = Row(board, 124);   // the archived item of the fixture
        Assert.Null(row.QuerySelector("button[data-pill=status]"));
        Assert.NotNull(row.QuerySelector("span.box.cell.filled"));
        Assert.NotNull(row.QuerySelector("button[data-pill=priority]"));
    }

    public void Dispose()
    {
        repo.Dispose();
        File.Delete(settingsFile);
    }
    private static void Act<T>(Bunit.IRenderedComponent<T> cut, Func<AngleSharp.Dom.IElement?> find, Action<AngleSharp.Dom.IElement> fire) where T : Microsoft.AspNetCore.Components.IComponent
    {
        cut.Eventually(() => Xunit.Assert.NotNull(find()));   // rendered and present, or the test fails
        cut.InvokeAsync(() => fire(find()!)).GetAwaiter().GetResult();
    }
}
