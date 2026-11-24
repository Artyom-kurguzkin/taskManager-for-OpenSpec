using AngleSharp.Dom;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Radzen;
using TaskManager.AppState;
using TaskManager.Components.Layout;
using TaskManager.Components.Pages;

namespace TaskManager.Tests;

/// <summary>Alt+\ splits the window's active pane, Ctrl+W closes it; the window has one side menu that names the tab it acts on. The keys reach the layout
/// through its [JSInvokable] methods (wwwroot/js/app.js is the only caller), so the tests call those.</summary>
public sealed class SplittingPanesTests : IDisposable
{
    private readonly TestRepo repo = new();
    private readonly string settingsFile = Path.Combine(Path.GetTempPath(), "rtm-ui-" + Guid.NewGuid().ToString("N") + ".json");

    private BunitContext NewContext(MainWindowLauncher? launcher = null)
    {
        BunitContext ctx = new();
        ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        ctx.Services.AddSingleton<UiService>();
        ctx.Services.AddRadzenComponents();
        ctx.Services.AddSingleton(new SettingsStore(settingsFile));
        ctx.Services.AddSingleton<Workspace>();
        if (launcher is not null) ctx.Services.AddSingleton(launcher);
        Assert.True(ctx.Services.GetRequiredService<Workspace>().Open(repo.Root).Success);
        return ctx;
    }

    private static NavigationManager Nav(BunitContext ctx) => ctx.Services.GetRequiredService<NavigationManager>();
    private static Panes PanesOf(BunitContext ctx) => ctx.Services.GetRequiredService<UiService>().Panes;
    private static string Address(BunitContext ctx) => Nav(ctx).ToBaseRelativePath(Nav(ctx).Uri);

    /// <summary>The layout as the router fills it: the board, or the page the address names, as its body.</summary>
    private static IRenderedComponent<MainLayout> RenderLayout(BunitContext ctx, string path = "")
    {
        Nav(ctx).NavigateTo(path);
        return ctx.Render<MainLayout>(p => p.Add(l => l.Body, (RenderFragment)(b =>
        {
            if (path.StartsWith("item/"))
            {
                b.OpenComponent<WorkItemPage>(0);
                b.AddComponentParameter(1, "Id", int.Parse(path[5..]));
            }
            else b.OpenComponent<BoardPage>(0);
            b.CloseComponent();
        })));
    }

    private static Task Split(IRenderedComponent<MainLayout> layout) => layout.InvokeAsync(layout.Instance.SplitPane);
    private static Task Close(IRenderedComponent<MainLayout> layout) => layout.InvokeAsync(layout.Instance.ClosePane);

    private static IReadOnlyList<IElement> PaneElements(IRenderedComponent<MainLayout> layout) => layout.FindAll("main.panes > .pane");

    [Fact(DisplayName = "Alt+backslash splits the active pane into two showing the same page")]
    public async Task SplitsIntoTwo()
    {
        await using BunitContext ctx = NewContext();
        IRenderedComponent<MainLayout> layout = RenderLayout(ctx);
        layout.WaitForAssertion(() => Assert.NotEmpty(layout.FindAll("article.card")));

        await Split(layout);

        layout.WaitForAssertion(() =>
        {
            IReadOnlyList<IElement> panes = PaneElements(layout);
            Assert.Equal(2, panes.Count);
            Assert.All(panes, p => Assert.NotEmpty(p.QuerySelectorAll("article.card")));   // the same page, in both
        });
        Assert.Equal(2, PanesOf(ctx).All.Count);
        Assert.Equal(PanesOf(ctx).All[1], PanesOf(ctx).Active);   // the new pane takes over
        Assert.Contains("active", PaneElements(layout)[1].ClassList);
        Assert.DoesNotContain("active", PaneElements(layout)[0].ClassList);
    }

    [Fact(DisplayName = "A split never goes past four panes")]
    public async Task NeverPastFour()
    {
        await using BunitContext ctx = NewContext();
        IRenderedComponent<MainLayout> layout = RenderLayout(ctx);

        for (int i = 0; i < 6; i++) await Split(layout);

        layout.WaitForAssertion(() => Assert.Equal(4, PaneElements(layout).Count));
        Assert.Equal(4, PanesOf(ctx).All.Count);
        Assert.Equal(4, PanesOf(ctx).Number(PanesOf(ctx).Active));
    }

    [Fact(DisplayName = "Ctrl+W closes the active pane and its neighbour becomes active")]
    public async Task CloseMakesTheNeighbourActive()
    {
        await using BunitContext ctx = NewContext();
        IRenderedComponent<MainLayout> layout = RenderLayout(ctx);
        await Split(layout);
        layout.WaitForAssertion(() => Assert.Equal(2, PaneElements(layout).Count));
        Pane first = PanesOf(ctx).All[0];

        await Close(layout);

        layout.WaitForAssertion(() => Assert.Empty(layout.FindAll("main.panes")));   // one pane again: the plain page
        Assert.Equal([first], PanesOf(ctx).All);
        Assert.Equal(first, PanesOf(ctx).Active);
        layout.WaitForAssertion(() => Assert.NotEmpty(layout.FindAll("main article.card")));
    }

    [Fact(DisplayName = "Ctrl+W with one pane closes nothing")]
    public async Task CloseWithOnePaneDoesNothing()
    {
        await using BunitContext ctx = NewContext();
        IRenderedComponent<MainLayout> layout = RenderLayout(ctx);
        layout.WaitForAssertion(() => Assert.NotEmpty(layout.FindAll("article.card")));

        await Close(layout);

        Assert.Single(PanesOf(ctx).All);
        Assert.NotEmpty(layout.FindAll("main article.card"));
    }

    [Fact(DisplayName = "Opening a work item from the list in a split opens it in the neighbouring pane")]
    public async Task ItemOpensInTheNeighbour()
    {
        await using BunitContext ctx = NewContext();
        IRenderedComponent<MainLayout> layout = RenderLayout(ctx);
        await Split(layout);
        layout.WaitForAssertion(() => Assert.All(PaneElements(layout), p => Assert.NotEmpty(p.QuerySelectorAll("article.card"))));
        await layout.InvokeAsync(() => PanesOf(ctx).Activate(PanesOf(ctx).All[0]));   // the list on the left is the one used

        layout.FireWhenReady("main.panes > .pane article.card", e => e.Click(), all => all[0]);

        layout.WaitForAssertion(() =>
        {
            IReadOnlyList<IElement> panes = PaneElements(layout);
            Assert.NotEmpty(panes[1].QuerySelectorAll("input.title-in"));   // the item, on the right
            Assert.Empty(panes[0].QuerySelectorAll("input.title-in"));
            Assert.NotEmpty(panes[0].QuerySelectorAll("article.card"));      // the list stays
        });
        Assert.StartsWith("item/", PanesOf(ctx).All[1].Path);
        Assert.Equal("", PanesOf(ctx).All[0].Path);
    }

    [Fact(DisplayName = "The window has one side menu and it names the tab it acts on")]
    public async Task OneMenuNamingTheTab()
    {
        await using BunitContext ctx = NewContext();
        IRenderedComponent<MainLayout> layout = RenderLayout(ctx);
        Assert.Single(layout.FindAll("nav.side"));
        Assert.Empty(layout.FindAll(".nav-tab"));   // one pane: nothing to name

        await Split(layout);
        await Split(layout);

        layout.WaitForAssertion(() => Assert.Equal(3, PaneElements(layout).Count));
        Assert.Single(layout.FindAll("nav.side"));   // never one per pane
        layout.WaitForAssertion(() => Assert.Equal("3", layout.Find("nav.side .nav-tab").TextContent.Trim()));   // the collapsed rail: just the number
        layout.ClickWhenReady("button.nav-toggle");
        layout.WaitForAssertion(() => Assert.Equal("Tab 3", layout.Find("nav.side .nav-tab").TextContent.Trim()));
    }

    [Fact(DisplayName = "The side menu follows the pane that was clicked last")]
    public async Task MenuFollowsTheClickedPane()
    {
        await using BunitContext ctx = NewContext();
        IRenderedComponent<MainLayout> layout = RenderLayout(ctx);
        await Split(layout);
        layout.WaitForAssertion(() => Assert.Equal(2, PaneElements(layout).Count));
        await layout.InvokeAsync(() => Nav(ctx).NavigateTo("settings"));   // the second pane goes to the settings
        layout.WaitForAssertion(() => Assert.Equal("settings", PanesOf(ctx).All[1].Path));

        layout.FireWhenReady("main.panes > .pane", e => e.PointerDown(), all => all[0]);   // a click on the first pane starts with its pointer-down

        layout.WaitForAssertion(() =>
        {
            Assert.Equal(PanesOf(ctx).All[0], PanesOf(ctx).Active);
            Assert.Equal("1", layout.Find("nav.side .nav-tab").TextContent.Trim());
            Assert.Equal("", Address(ctx));   // the window's address went back to the board
        });

        layout.FireWhenReady("main.panes > .pane", e => e.PointerDown(), all => all[1]);
        layout.WaitForAssertion(() =>
        {
            Assert.Equal("2", layout.Find("nav.side .nav-tab").TextContent.Trim());
            Assert.Equal("settings", Address(ctx));
        });
    }

    [Fact(DisplayName = "Navigating through the window's address lands in the active pane")]
    public async Task NavigationLandsInTheActivePane()
    {
        await using BunitContext ctx = NewContext();
        IRenderedComponent<MainLayout> layout = RenderLayout(ctx);
        await Split(layout);
        layout.WaitForAssertion(() => Assert.Equal(2, PaneElements(layout).Count));

        await layout.InvokeAsync(() => Nav(ctx).NavigateTo("item/123"));

        layout.WaitForAssertion(() =>
        {
            Assert.Equal("item/123", PanesOf(ctx).All[1].Path);
            Assert.Equal("", PanesOf(ctx).All[0].Path);
            Assert.NotEmpty(PaneElements(layout)[1].QuerySelectorAll("input.title-in"));
            Assert.NotEmpty(PaneElements(layout)[0].QuerySelectorAll("article.card"));
        });
    }

    [Fact(DisplayName = "One pane looks the same as before the split existed")]
    public async Task OnePaneIsThePlainPage()
    {
        await using BunitContext ctx = NewContext();
        IRenderedComponent<MainLayout> layout = RenderLayout(ctx, "item/123");

        layout.WaitForAssertion(() => Assert.NotEmpty(layout.FindAll("main > .sheet input.title-in, main input.title-in")));
        Assert.Empty(layout.FindAll("main.panes"));
        Assert.Empty(layout.FindAll(".pane"));
        Assert.Empty(layout.FindAll(".nav-tab"));
        Assert.Single(layout.FindAll("nav.side"));
        Assert.Equal("Health-check endpoint", layout.Find("main input.title-in").GetAttribute("value"));
    }

    [Fact(DisplayName = "The work item page no longer offers beside the list or item only")]
    public async Task NoBesideTheList()
    {
        await using BunitContext ctx = NewContext(path => (true, ""));
        IRenderedComponent<MainLayout> layout = RenderLayout(ctx, "item/123");
        layout.WaitForAssertion(() => Assert.NotEmpty(layout.FindAll("input.title-in")));

        Assert.DoesNotContain("Beside the list", layout.Markup);
        Assert.DoesNotContain("Item only", layout.Markup);
        Assert.Empty(layout.FindAll("main.split"));
        Assert.Empty(layout.FindAll(".split-list"));
        Assert.Single(layout.FindAll("button.split-btn"));   // one button now: a separate window, not a menu
        Assert.Empty(layout.FindAll("button.split-btn[aria-haspopup]"));
    }

    [Fact(DisplayName = "Alt+Win+backslash opens a full window on the active pane's page")]
    public async Task OpenWindowUsesTheLauncher()
    {
        List<string> opened = [];
        await using BunitContext ctx = NewContext(path => { opened.Add(path); return (true, ""); });
        IRenderedComponent<MainLayout> layout = RenderLayout(ctx, "item/123");

        await layout.InvokeAsync(layout.Instance.OpenWindow);

        Assert.Equal(["item/123"], opened);
    }

    [Fact(DisplayName = "Alt+Win+backslash says so when no window can be opened")]
    public async Task OpenWindowFailureIsToasted()
    {
        await using BunitContext ctx = NewContext();   // no launcher: a host that cannot open one
        IRenderedComponent<MainLayout> layout = RenderLayout(ctx);
        UiService ui = ctx.Services.GetRequiredService<UiService>();

        await layout.InvokeAsync(layout.Instance.OpenWindow);

        Assert.Single(ui.Toasts, t => t.Kind == ToastKind.Warning);
    }

    [Fact(DisplayName = "An item opened from a pane goes to its right neighbour, or the left one from the last pane")]
    public void ItemTargets()
    {
        Panes panes = new();
        Pane first = panes.All[0];
        Assert.Equal(first, panes.ItemTarget(first));   // alone: itself
        Pane second = panes.Split()!;
        Pane third = panes.Split()!;
        Assert.Equal(second, panes.ItemTarget(first));
        Assert.Equal(third, panes.ItemTarget(second));
        Assert.Equal(second, panes.ItemTarget(third));
        Assert.Equal([1, 2, 3], panes.All.Select(panes.Number));
        Assert.True(panes.Close(third));
        Assert.Equal(second, panes.Active);
        Assert.True(panes.Close(second));
        Assert.False(panes.Close(first));   // the last one stays
    }

    /// <summary>Two panes, the side menu opened; the left pane shows <paramref name="left"/> and the right one <paramref name="right"/> ("" is the board). The left one is active.</summary>
    private static async Task<IRenderedComponent<MainLayout>> SplitWith(BunitContext ctx, string left, string right)
    {
        IRenderedComponent<MainLayout> layout = RenderLayout(ctx);
        layout.ClickWhenReady("button.nav-toggle");
        await Split(layout);
        layout.WaitForAssertion(() => Assert.Equal(2, PaneElements(layout).Count));
        Panes panes = PanesOf(ctx);
        await layout.InvokeAsync(() => { panes.Navigate(panes.All[0], left); panes.Navigate(panes.All[1], right); panes.Activate(panes.All[0]); });
        layout.WaitForAssertion(() =>
        {
            Assert.Equal(left.StartsWith("item/") ? 1 : 0, PaneElements(layout)[0].QuerySelectorAll("input.title-in").Length);
            Assert.Equal(right.StartsWith("item/") ? 1 : 0, PaneElements(layout)[1].QuerySelectorAll("input.title-in").Length);
        });
        return layout;
    }

    [Fact(DisplayName = "With the board pane active the side menu shows no worklog of an item in another pane")]
    public async Task NoWorklogOfAnotherPane()
    {
        await using BunitContext ctx = NewContext();
        IRenderedComponent<MainLayout> layout = await SplitWith(ctx, "", "item/123");

        layout.WaitForAssertion(() =>
        {
            Assert.Equal("1", PanesOf(ctx).Number(PanesOf(ctx).Active).ToString());
            Assert.Empty(layout.FindAll("nav.side .nav-worklog"));
            Assert.True(ctx.Services.GetRequiredService<UiService>().WorklogOpen is null);
        });
    }

    [Fact(DisplayName = "Activating the item's pane brings its worklog block back to the side menu")]
    public async Task ActivatingTheItemPaneBringsTheWorklogBack()
    {
        await using BunitContext ctx = NewContext();
        IRenderedComponent<MainLayout> layout = await SplitWith(ctx, "", "item/123");
        Assert.Empty(layout.FindAll("nav.side .nav-worklog"));

        await layout.InvokeAsync(() => PanesOf(ctx).Activate(PanesOf(ctx).All[1]));

        layout.WaitForAssertion(() =>
        {
            Assert.NotEmpty(layout.FindAll("nav.side .nav-worklog"));
            Assert.True(ctx.Services.GetRequiredService<UiService>().WorklogOpen is not null);
        });

        await layout.InvokeAsync(() => PanesOf(ctx).Activate(PanesOf(ctx).All[0]));   // and goes again with the board
        layout.WaitForAssertion(() => Assert.Empty(layout.FindAll("nav.side .nav-worklog")));
    }

    [Fact(DisplayName = "Two item panes each keep their own worklog in the side menu")]
    public async Task TwoItemPanesEachKeepTheirWorklog()
    {
        await using BunitContext ctx = NewContext();
        UiService ui = ctx.Services.GetRequiredService<UiService>();
        IRenderedComponent<MainLayout> layout = await SplitWith(ctx, "item/123", "item/107");
        Panes panes = PanesOf(ctx);

        WorklogActions? left = null, right = null;
        layout.WaitForAssertion(() => Assert.True((left = ui.WorklogOpen) is not null));
        await layout.InvokeAsync(() => panes.Activate(panes.All[1]));
        layout.WaitForAssertion(() => Assert.True((right = ui.WorklogOpen) is not null));
        Assert.False(ReferenceEquals(left, right));   // each pane's item speaks for the menu while its pane is active

        await layout.InvokeAsync(() => ui.ClickWorklogKind("Decisions"));   // a filter picked while the right pane is active belongs to it
        Assert.False(ui.WorklogFilter.Notes);
        await layout.InvokeAsync(() => panes.Activate(panes.All[0]));
        layout.WaitForAssertion(() =>
        {
            Assert.True(ReferenceEquals(left, ui.WorklogOpen));
            Assert.Equal(new WorklogFilters(), ui.WorklogFilter);
            Assert.NotEmpty(layout.FindAll("nav.side .nav-worklog"));
        });
        await layout.InvokeAsync(() => panes.Activate(panes.All[1]));
        layout.WaitForAssertion(() =>
        {
            Assert.True(ReferenceEquals(right, ui.WorklogOpen));
            Assert.False(ui.WorklogFilter.Notes);
        });
    }

    public void Dispose()
    {
        repo.Dispose();
        File.Delete(settingsFile);
    }
}
