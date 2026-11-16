using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Radzen;
using TaskManager.Components.Pages;
using TaskManager.Components.Layout;
using TaskManager.Components.Shared;
using TaskManager.AppState;
using TaskManager.BackgroundJobs;
using TaskManager.Helpers;

namespace TaskManager.Tests;

/// <summary>What the user arranges (hidden columns, the view, the grid's columns) is kept per project in the user's settings file, whatever pages they visit.</summary>
public sealed class KeepingMyArrangementsTests : IDisposable
{
    private readonly TestRepo repo = new();
    private readonly TestRepo repo2 = new();
    private readonly string settingsFile = Path.Combine(Path.GetTempPath(), "rtm-arr-" + Guid.NewGuid().ToString("N") + ".json");

    private IRenderedComponent<SideNav>? nav;

    private BunitContext NewContext()
    {
        BunitContext ctx = new();
        ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        ctx.Services.AddSingleton<UiService>();
        ctx.Services.AddRadzenComponents();
        ctx.Services.AddSingleton(new SettingsStore(settingsFile));
        ctx.Services.AddSingleton<Workspace>();
        Assert.True(ctx.Services.GetRequiredService<Workspace>().Open(repo.Root).Success);
        ctx.Render<Overlays>();
        nav = ctx.Render<SideNav>();
        return ctx;
    }

    private void Show(string view)
    {
        // Click returns before its handler while the renderer is busy (a render the tab switch queued): the empty InvokeAsync runs after
        // everything queued before it, so the menu is open before it is searched and the pick has landed before the test goes on. No clock.
        nav!.Find("button[aria-label=Layout]").Click();
        Settle();
        nav.FindAll(".menu-item").Single(i => i.TextContent.Trim() == view).Click();
        Settle();
    }

    private void Settle() => nav!.InvokeAsync(() => { }).GetAwaiter().GetResult();

    private static List<string> Columns(IRenderedComponent<BoardPage> board) => board.FindAll("section.col h3").Select(h => h.TextContent).ToList();

    [Fact(DisplayName = "Hidden columns stay hidden after adding a work item and visiting the list, the calendar and a work item page")]
    public async Task HiddenColumnsSurviveAWalkAround()
    {
        await using BunitContext ctx = NewContext();
        SettingsStore settings = ctx.Services.GetRequiredService<SettingsStore>();
        IRenderedComponent<BoardPage> board = ctx.Render<BoardPage>();
        board.FindAll("header.col-h").First(h => h.TextContent.Contains("Blocked")).Click();
        board.FindAll(".menu-item").Single(i => i.TextContent.Trim() == "Hide column").Click();
        Assert.DoesNotContain("Blocked", Columns(board));

        nav!.Find("button[aria-label='New work item']").Click();   // adds an item and opens its page, where the list sits beside it (a new page of the board)
        board.Dispose();
        IRenderedComponent<BoardPage> beside = ctx.Render<BoardPage>(p => p.Add(b => b.Embedded, true).Add(b => b.Current, 1));
        Show("List");
        beside.Dispose();
        IRenderedComponent<BoardPage> list = ctx.Render<BoardPage>();
        list.WaitForAssertion(() => Assert.NotEmpty(list.FindAll("tr[data-id]")));
        Show("Calendar");
        list.Dispose();
        IRenderedComponent<BoardPage> calendar = ctx.Render<BoardPage>();
        Show("Board");
        calendar.Dispose();
        IRenderedComponent<BoardPage> back = ctx.Render<BoardPage>();

        Assert.DoesNotContain("Blocked", Columns(back));
        Assert.Contains("status:Blocked", settings.Board(repo.Root).Hidden!);
        Assert.Equal(["status:Blocked"], new SettingsStore(settingsFile).Board(repo.Root).Hidden);
    }

    [Fact(DisplayName = "A hidden list column stays hidden after adding a work item and coming back to the list")]
    public async Task HiddenListColumnSurvives()
    {
        await using BunitContext ctx = NewContext();
        SettingsStore settings = ctx.Services.GetRequiredService<SettingsStore>();
        Show("List");
        IRenderedComponent<BoardPage> list = ctx.Render<BoardPage>();
        list.Find("button[aria-label='Hide Progress column']").Click();
        list.WaitForAssertion(() => Assert.DoesNotContain(list.FindAll("thead th"), h => h.TextContent.Contains("Progress")));

        nav!.Find("button[aria-label='New work item']").Click();
        list.Dispose();
        IRenderedComponent<BoardPage> beside = ctx.Render<BoardPage>(p => p.Add(b => b.Embedded, true).Add(b => b.Current, 1));
        beside.WaitForAssertion(() => Assert.NotEmpty(beside.FindAll("tr[data-id]")));
        Assert.DoesNotContain(beside.FindAll("thead th"), h => h.TextContent.Contains("Progress"));
        beside.Dispose();
        IRenderedComponent<BoardPage> back = ctx.Render<BoardPage>();
        back.WaitForAssertion(() => Assert.NotEmpty(back.FindAll("tr[data-id]")));

        Assert.DoesNotContain(back.FindAll("thead th"), h => h.TextContent.Contains("Progress"));
        Assert.Contains("col:Progress", settings.Board(repo.Root).Hidden!);
    }

    private void HideColumn(IRenderedComponent<BoardPage> board, string column)
    {
        board.FindAll("header.col-h").First(h => h.TextContent.Contains(column)).Click();
        board.FindAll(".menu-item").Single(i => i.TextContent.Trim() == "Hide column").Click();
    }

    [Fact(DisplayName = "Opening or adding a work item in another project leaves the board's arrangement as it is, instead of showing that project's (empty) hidden list")]
    public async Task OtherProjectsItemKeepsTheBoardsArrangement()
    {
        await using BunitContext ctx = NewContext();
        Workspace workspace = ctx.Services.GetRequiredService<Workspace>();
        SettingsStore settings = ctx.Services.GetRequiredService<SettingsStore>();
        Assert.True(workspace.OpenTab(repo2.Root).Success);
        Assert.True(workspace.OpenTab(repo.Root).Success);
        IRenderedComponent<BoardPage> board = ctx.Render<BoardPage>();
        Assert.Contains("Blocked", Columns(board));
        HideColumn(board, "Blocked");

        Assert.True(workspace.CreateItem("Planned", project: repo2.Root).Ok);   // a new item in the other project, whose page then opens: that project becomes the active one
        Assert.True(workspace.Open(repo2.Root).Success);

        board.WaitForAssertion(() => Assert.DoesNotContain("Blocked", Columns(board)));
        Assert.Contains("status:Blocked", settings.Board(repo.Root).Hidden!);
        Assert.Null(settings.Board(repo2.Root).Hidden);
    }

    [Fact(DisplayName = "Switching to a project's tab shows the arrangement saved for that project, and switching back restores the first one's")]
    public async Task EachProjectHasItsOwnArrangement()
    {
        await using BunitContext ctx = NewContext();
        Workspace workspace = ctx.Services.GetRequiredService<Workspace>();
        SettingsStore settings = ctx.Services.GetRequiredService<SettingsStore>();
        Assert.True(workspace.OpenTab(repo2.Root).Success);
        Assert.True(workspace.OpenTab(repo.Root).Success);
        IRenderedComponent<BoardPage> board = ctx.Render<BoardPage>();
        HideColumn(board, "Blocked");

        Assert.True(workspace.OpenTab(repo2.Root).Success);
        board.WaitForAssertion(() => Assert.Contains("Blocked", Columns(board)));
        HideColumn(board, "Backlog");
        Assert.Equal(["status:Backlog"], settings.Board(repo2.Root).Hidden);

        Assert.True(workspace.OpenTab(repo.Root).Success);
        board.WaitForAssertion(() => Assert.Contains("Backlog", Columns(board)));
        Assert.DoesNotContain("Blocked", Columns(board));
        Assert.Equal(["status:Blocked"], settings.Board(repo.Root).Hidden);
    }

    [Fact(DisplayName = "Making a project the active one from its name's menu shows that project's arrangement and leaves the tabs in their order")]
    public async Task MakingAProjectActiveSwitchesTheArrangementNotTheTabOrder()
    {
        await using BunitContext ctx = NewContext();
        Workspace workspace = ctx.Services.GetRequiredService<Workspace>();
        Assert.True(workspace.OpenTab(repo2.Root).Success);
        Assert.True(workspace.OpenTab(repo.Root).Success);
        IRenderedComponent<BoardPage> board = ctx.Render<BoardPage>();
        HideColumn(board, "Blocked");
        List<string> tabs = [.. workspace.OpenRepos];

        Assert.True(workspace.Switch(repo2.Root).Success);

        board.WaitForAssertion(() => Assert.Contains("Blocked", Columns(board)));
        Assert.Equal(tabs, workspace.OpenRepos);
        Assert.Equal(Path.GetFullPath(repo2.Root), Path.GetFullPath(workspace.ViewRoot!));
    }

    [Fact(DisplayName = "Column order and widths are kept per project")]
    public async Task GridLayoutIsPerProject()
    {
        await using BunitContext ctx = NewContext();
        Workspace workspace = ctx.Services.GetRequiredService<Workspace>();
        Assert.True(workspace.OpenTab(repo2.Root).Success);
        Assert.True(workspace.OpenTab(repo.Root).Success);
        Show("List");
        IRenderedComponent<BoardPage> list = ctx.Render<BoardPage>();
        list.WaitForAssertion(() => Assert.NotEmpty(list.FindAll("tr[data-id]")));
        Save(list, "300px");

        Assert.True(workspace.OpenTab(repo2.Root).Success);
        Show("List");   // the other project has its own view, a board until it is switched
        list.WaitForAssertion(() => Assert.NotEmpty(list.FindAll("tr[data-id]")));
        Save(list, "180px");

        SettingsStore reopened = new(settingsFile);
        Assert.Contains("300px", reopened.Board(repo.Root).ListSettings);
        Assert.DoesNotContain("180px", reopened.Board(repo.Root).ListSettings);
        Assert.Contains("180px", reopened.Board(repo2.Root).ListSettings);

        static void Save(IRenderedComponent<BoardPage> page, string width) =>
            page.InvokeAsync(() => typeof(BoardPage).GetMethod("SaveGridSettings", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
                .Invoke(page.Instance, [new DataGridSettings { Columns = [new DataGridColumnSettings { Property = "Title", Width = width }] }])).GetAwaiter().GetResult();
    }

    public void Dispose()
    {
        repo2.Dispose();
        repo.Dispose();
        File.Delete(settingsFile);
    }
}
