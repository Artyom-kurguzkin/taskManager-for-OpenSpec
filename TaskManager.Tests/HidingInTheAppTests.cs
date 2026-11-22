using System.Security.Cryptography;
using AngleSharp.Dom;
using Bunit;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Notes;
using Radzen;
using TaskManager.AppState;
using TaskManager.Components.Layout;
using TaskManager.Components.Pages;
using TaskManager.Components.Shared;
using WorkItems;

namespace TaskManager.Tests;

/// <summary>
/// Hiding is the app's (#wi199, #wi201): a column, a project or a work item is hidden in this computer's settings, per repository, and nothing under openspec/ is written for it.
/// What an agent's resolution (cancelled, duplicate, superseded) hid stays hidden as before and is listed under the same Hidden section by its resolution.
/// </summary>
public sealed class HidingInTheAppTests : IDisposable
{
    private readonly TestRepo repo = new();
    private readonly TestRepo repo2 = new();
    private readonly string settingsFile = Path.Combine(Path.GetTempPath(), "rtm-hide-" + Guid.NewGuid().ToString("N") + ".json");
    private IRenderedComponent<SideNav>? nav;

    private BunitContext NewContext()
    {
        BunitContext ctx = new();
        ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        ctx.Services.AddSingleton<UiService>();
        ctx.Services.AddRadzenComponents();
        SettingsStore settings = new(settingsFile);
        foreach (string section in new[] { "organise", "filters", "hidden" }) settings.ToggleMenuSection(section);
        ctx.Services.AddSingleton(settings);
        ctx.Services.AddSingleton<Workspace>();
        Assert.True(ctx.Services.GetRequiredService<Workspace>().Open(repo.Root).Success);
        ctx.Render<Overlays>();
        nav = ctx.Render<SideNav>();
        nav.ClickWhenReady("button.nav-toggle");   // the expanded side menu holds the Hidden section
        return ctx;
    }

    public void Dispose()
    {
        repo.Dispose();
        repo2.Dispose();
        File.Delete(settingsFile);
    }

    private static List<string> Columns(IRenderedComponent<BoardPage> board) => board.FindAll("section.col h3").Select(h => h.TextContent).ToList();
    private static IElement Col(IRenderedComponent<BoardPage> board, string name) => board.FindAll("section.col").Single(c => c.QuerySelector("h3")!.TextContent == name);
    private static string NameOf(TestRepo r) => Path.GetFileName(r.Root);

    private void TwoProjects(BunitContext ctx)
    {
        Workspace ws = ctx.Services.GetRequiredService<Workspace>();
        Assert.True(ws.OpenTab(repo2.Root).Success);
        Assert.True(ws.Switch(repo.Root).Success);
    }

    private static void HideColumn(IRenderedComponent<BoardPage> board, string column)
    {
        board.FireWhenReady("header.col-h", e => e.Click(), all => all.First(h => h.TextContent.Contains(column)));
        board.WaitForAssertion(() => Assert.Contains(board.FindAll(".menu-item"), i => i.TextContent.Trim() == "Hide column"));
        board.FireWhenReady(".menu-item", e => e.Click(), all => all.Single(i => i.TextContent.Trim() == "Hide column"));
    }

    private static void HideProject(IRenderedComponent<BoardPage> board, int at)
    {
        board.FireWhenReady(".proj .proj-h h2 button", e => e.Click(), all => all[at]);
        board.WaitForAssertion(() => Assert.Contains(board.FindAll(".menu-item"), i => i.TextContent.Trim() == "Hide project"));
        board.FireWhenReady(".menu-item", e => e.Click(), all => all.Single(i => i.TextContent.Trim() == "Hide project"));
    }

    private static Dictionary<string, string> Fingerprint(string dir) =>
        Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories).ToDictionary(f => Path.GetRelativePath(dir, f), f => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(f))));

    // ── #wi199: the hide takes effect whatever was done before ──

    [Fact(DisplayName = "Hiding a column and a project takes effect after cards were moved with the arrow keys and a status was changed")]
    public async Task HidingAfterArrowMovesAndStatusChanges()
    {
        await using BunitContext ctx = NewContext();
        Workspace workspace = ctx.Services.GetRequiredService<Workspace>();
        SettingsStore settings = ctx.Services.GetRequiredService<SettingsStore>();
        Assert.True(workspace.CreateItem("Planned").Ok);
        Assert.True(workspace.CreateItem("Planned").Ok);
        IRenderedComponent<BoardPage> board = ctx.Render<BoardPage>();

        board.FireWhenReady("section.col article.card", e => e.Click(new MouseEventArgs { ShiftKey = true }), all => Col(board, "Planned").QuerySelectorAll("article.card")[0]);
        board.KeyDownWhenReady(".board-wrap", "ArrowDown");   // an arrow move, saved after its pause
        Assert.True(workspace.In(repo.Root, () => WorkItemsApi.SetStatus(107, "In Review")).Success);   // a status change from outside
        workspace.Notify();
        HideColumn(board, "Blocked");

        board.WaitForAssertion(() => Assert.DoesNotContain("Blocked", Columns(board)));
        Assert.Contains("status:Blocked", settings.Board(repo.Root).Hidden!);

        Assert.True(workspace.In(repo.Root, () => WorkItemsApi.SetStatus(107, "Blocked")).Success);
        workspace.Notify();
        HideColumn(board, "Backlog");
        board.WaitForAssertion(() => Assert.DoesNotContain("Backlog", Columns(board)));
        Assert.DoesNotContain("Blocked", Columns(board));

        TwoProjects(ctx);   // a second project opens after all that
        board.WaitForAssertion(() => Assert.Equal(2, board.FindAll(".proj").Count));
        HideProject(board, 1);
        board.WaitForAssertion(() => Assert.Single(board.FindAll(".proj")));
        nav!.ClickWhenReady($"button[aria-label='Show project {NameOf(repo2)}']");
        board.WaitForAssertion(() => Assert.Equal(2, board.FindAll(".proj").Count));
        nav.ClickWhenReady("button[aria-label='Show Backlog']");
        board.WaitForAssertion(() => Assert.Contains("Backlog", Columns(board)));
    }

    // ── #wi12: "show all hidden" is one action ──

    [Fact(DisplayName = "Pressing 'show all hidden' once brings back every hidden item, column and project, clears the Hidden list and the legacy flags, resets the filters, and draws nothing dashed")]
    public async Task ShowAllHiddenRestoresEverythingInOnePress()
    {
        await using BunitContext ctx = NewContext();
        TwoProjects(ctx);
        SettingsStore settings = ctx.Services.GetRequiredService<SettingsStore>();
        IRenderedComponent<BoardPage> board = ctx.Render<BoardPage>();
        board.WaitForAssertion(() => Assert.Equal(2, board.FindAll(".proj").Count));
        settings.SetItemsHidden(repo.Root, [123], true);                  // an item
        settings.SetItemsHidden(repo2.Root, [107], true);                 // an item of the other project
        Assert.True(WorkItemsApi.Hide(125).Success);                     // a flag an older version wrote
        ctx.Services.GetRequiredService<Workspace>().Notify();
        HideColumn(board, "Blocked");                                    // a column
        HideProject(board, 1);                                           // a project
        settings.SaveBoard(repo.Root, settings.Board(repo.Root) with { Who = "Tom", Period = "week", Commit = "uncommitted", ChangedFrom = new DateOnly(2000, 1, 1), ChangedTo = new DateOnly(2000, 1, 2), ChangedFromTime = new TimeOnly(9, 30) });
        board.WaitForAssertion(() => Assert.Empty(board.FindAll("article.card")));
        nav!.WaitForAssertion(() => Assert.False(nav.Find("button#f-hidden").HasAttribute("disabled")));
        int changed = 0;
        settings.Changed += () => changed++;

        nav.ClickWhenReady("button#f-hidden");

        nav.WaitForAssertion(() => Assert.Equal(1, Volatile.Read(ref changed)));   // Click returns before the handler has run when the dispatcher is busy
        board.WaitForAssertion(() => Assert.Equal(2, board.FindAll(".proj").Count));
        Assert.Contains("Blocked", Columns(board));
        Assert.Contains(board.FindAll("article.card"), c => c.TextContent.Contains("Health-check endpoint"));
        Assert.Contains(board.FindAll("article.card"), c => c.TextContent.Contains(WorkItemsApi.Get(125)!.Title));
        Assert.Empty(board.FindAll(".is-hidden, .hidden-item, [data-hidden]"));
        Assert.Null(settings.Current.HiddenItems);
        Assert.Null(settings.Current.HiddenProjects);
        BoardOptions o = settings.Board(repo.Root);
        BoardOptions defaults = new();
        Assert.Empty(o.Hidden ?? []);
        Assert.Equal((defaults.Who, defaults.Period, defaults.Commit, defaults.ChangedFrom, defaults.ChangedTo, defaults.ChangedFromTime, defaults.ChangedToTime), (o.Who, o.Period, o.Commit, o.ChangedFrom, o.ChangedTo, o.ChangedFromTime, o.ChangedToTime));
        Assert.False(WorkItemsApi.Get(125)!.Hidden);                    // the legacy flag is cleared in the item's file
        Assert.DoesNotContain("hidden", File.ReadAllText(Path.Combine(repo.Root, WorkItemsApi.Get(125)!.Path, ".openspec.yaml")));
        nav.WaitForAssertion(() => Assert.Empty(nav.FindAll(".nav-hidden .grp-row")));   // the Hidden list is empty
        Assert.True(nav.Find("button#f-hidden").HasAttribute("disabled"));               // and there is nothing left to press for
        Assert.Equal(1, Volatile.Read(ref changed));                       // one change, not one per piece: still one after everything settled
    }

    [Fact(DisplayName = "The collapsed menu's show all hidden button does the same one action")]
    public async Task TheRailButtonRestoresToo()
    {
        await using BunitContext ctx = NewContext();
        SettingsStore settings = ctx.Services.GetRequiredService<SettingsStore>();
        settings.SetItemsHidden(repo.Root, [123], true);
        IRenderedComponent<BoardPage> board = ctx.Render<BoardPage>();
        nav!.ClickWhenReady("button.nav-toggle");   // folded again: the rail
        Assert.DoesNotContain(board.FindAll("article.card"), c => c.TextContent.Contains("Health-check endpoint"));

        nav.ClickWhenReady("button.hidden-btn");

        board.WaitForAssertion(() => Assert.Contains(board.FindAll("article.card"), c => c.TextContent.Contains("Health-check endpoint")));
        Assert.Null(settings.Current.HiddenItems);
    }

    // ── #wi201: an item is hidden in the app, never in the repository ──

    private static IElement CardOf(IRenderedComponent<BoardPage> board, string title) => board.FindAll("article.card").First(c => c.TextContent.Contains(title));

    [Fact(DisplayName = "Delete (hide) hides the selected work items in the app's settings and writes nothing under openspec; the Hidden section lists them and an eye restores them")]
    public async Task HidingAnItemWritesNothingToTheRepository()
    {
        await using BunitContext ctx = NewContext();
        SettingsStore settings = ctx.Services.GetRequiredService<SettingsStore>();
        IRenderedComponent<BoardPage> board = ctx.Render<BoardPage>();
        Dictionary<string, string> before = Fingerprint(repo.OpenSpec);
        int cards = board.FindAll("article.card").Count;

        board.FireWhenReady("article.card", e => e.Click(new MouseEventArgs { ShiftKey = true }), all => CardOf(board, "Health-check endpoint"));
        board.ClickWhenReady(".actionbar button.hide-sel");

        board.WaitForAssertion(() => Assert.Equal(cards - 1, board.FindAll("article.card").Count));
        Assert.DoesNotContain(board.FindAll("article.card"), c => c.TextContent.Contains("Health-check endpoint"));
        Assert.True(settings.ItemHidden(repo.Root, 123));
        Assert.False(WorkItemsApi.Get(123)!.Hidden);   // the item itself says nothing
        Assert.Equal(before, Fingerprint(repo.OpenSpec));   // not one byte under openspec/ changed
        Assert.Contains("#wi123 Health-check endpoint", nav!.Find(".nav-hidden").TextContent);

        nav.ClickWhenReady("button[aria-label='Show work item #wi123']");

        board.WaitForAssertion(() => Assert.Equal(cards, board.FindAll("article.card").Count));
        Assert.False(settings.ItemHidden(repo.Root, 123));
        Assert.DoesNotContain("#wi123", nav.Find(".nav-hidden").TextContent);
        Assert.Equal(before, Fingerprint(repo.OpenSpec));
    }

    [Fact(DisplayName = "A work item an agent's resolution hid stays hidden and is listed under Hidden by its resolution, also after 'show all hidden' is pressed")]
    public async Task AResolutionStillHides()
    {
        await using BunitContext ctx = NewContext();
        Assert.True(NotesApi.Resolve(107, WorkItemsApi.ParseResolution("cancelled", 107), "No longer needed.", "Claude").Hidden);
        ctx.Services.GetRequiredService<Workspace>().Notify();
        IRenderedComponent<BoardPage> board = ctx.Render<BoardPage>();

        Assert.DoesNotContain(board.FindAll("article.card"), c => c.TextContent.Contains(WorkItemsApi.Get(107)!.Title));
        nav!.WaitForAssertion(() => Assert.Contains("#wi107", nav.Find(".nav-hidden").TextContent));
        string hidden = nav.Find(".nav-hidden").TextContent;
        Assert.Contains("Cancelled", hidden);
        Assert.Empty(nav.FindAll("button[aria-label='Show work item #wi107']"));   // its way back is the item's own page

        nav.WaitForAssertion(() => Assert.True(nav.Find("button#f-hidden").HasAttribute("disabled")));   // nothing the press could bring back: a resolution comes back from the item's own page
        nav.ClickWhenReady("button#f-hidden");
        Assert.DoesNotContain(board.FindAll("article.card"), c => c.TextContent.Contains(WorkItemsApi.Get(107)!.Title));
        Assert.True(WorkItemsApi.Get(107)!.Hidden);
        Assert.Contains("Cancelled", nav.Find(".nav-hidden").TextContent);
    }

    [Fact(DisplayName = "The page of an item hidden in the app says it is hidden in this app and where to restore it, with no word about openspec; one hidden by a resolution says which")]
    public async Task TheItemPageSaysWhyItIsHidden()
    {
        await using BunitContext ctx = NewContext();
        SettingsStore settings = ctx.Services.GetRequiredService<SettingsStore>();
        settings.SetItemsHidden(repo.Root, [123], true);
        Assert.True(NotesApi.Resolve(107, WorkItemsApi.ParseResolution("cancelled", 107), "No longer needed.", "Claude").Hidden);

        string app = ctx.Render<WorkItemPage>(p => p.Add(c => c.Id, 123)).Find("p.warning").TextContent;
        Assert.Equal("Hidden in this app. Restore it from the side menu's Hidden section.", app.Trim());

        string resolved = ctx.Render<WorkItemPage>(p => p.Add(c => c.Id, 107)).Find("p.warning").TextContent;
        Assert.Contains("Cancelled", resolved);
        Assert.DoesNotContain("openspec", resolved, StringComparison.OrdinalIgnoreCase);

        Assert.Empty(ctx.Render<WorkItemPage>(p => p.Add(c => c.Id, 125)).FindAll("p.warning").Where(w => w.TextContent.Contains("Hidden")));
    }

    // ── #wi201: an x-rtm.hidden from before the app hid items, with no resolution note, counts as hidden in the app ──

    [Fact(DisplayName = "A work item hidden by an old x-rtm.hidden flag with no resolution is listed under Hidden with an eye, and its page shows the app-hidden banner")]
    public async Task ALegacyFlagIsListedAsAppHidden()
    {
        await using BunitContext ctx = NewContext();
        Assert.True(WorkItemsApi.Hide(123).Success);   // the flag an older version wrote
        ctx.Services.GetRequiredService<Workspace>().Notify();

        nav!.WaitForAssertion(() => Assert.Contains("#wi123 Health-check endpoint", nav.Find(".nav-hidden").TextContent));
        Assert.Empty(nav.FindAll(".nav-hidden .resolution"));   // no resolution label
        nav.Find("button[aria-label='Show work item #wi123']");   // the eye
        string banner = ctx.Render<WorkItemPage>(p => p.Add(c => c.Id, 123)).Find("p.warning").TextContent;
        Assert.Equal("Hidden in this app. Restore it from the side menu's Hidden section.", banner.Trim());
    }

    [Fact(DisplayName = "The eye of a legacy-flagged item clears x-rtm.hidden in its file, leaves nothing in the settings, and the item is back on the board; Restore on its page does the same")]
    public async Task RestoringALegacyFlagClearsIt()
    {
        await using BunitContext ctx = NewContext();
        SettingsStore settings = ctx.Services.GetRequiredService<SettingsStore>();
        Assert.True(WorkItemsApi.Hide(123).Success);
        Assert.True(WorkItemsApi.Hide(107).Success);
        ctx.Services.GetRequiredService<Workspace>().Notify();
        IRenderedComponent<BoardPage> board = ctx.Render<BoardPage>();
        Assert.DoesNotContain(board.FindAll("article.card"), c => c.TextContent.Contains("Health-check endpoint"));

        nav!.WaitForAssertion(() => nav.Find("button[aria-label='Show work item #wi123']"));
        nav.ClickWhenReady("button[aria-label='Show work item #wi123']");

        board.WaitForAssertion(() => Assert.Contains(board.FindAll("article.card"), c => c.TextContent.Contains("Health-check endpoint")));
        Assert.False(WorkItemsApi.Get(123)!.Hidden);
        string yaml = File.ReadAllText(System.IO.Path.Combine(repo.Root, WorkItemsApi.Get(123)!.Path, ".openspec.yaml"));
        Assert.DoesNotContain("hidden", yaml);
        Assert.False(settings.ItemHidden(repo.Root, 123));
        Assert.Empty(settings.HiddenItemsOf(repo.Root));
        nav.WaitForAssertion(() => Assert.DoesNotContain("#wi123", nav.Find(".nav-hidden").TextContent));

        IRenderedComponent<WorkItemPage> page = ctx.Render<WorkItemPage>(p => p.Add(c => c.Id, 107));   // the page's Restore button
        page.ClickWhenReady("button.hide-item");
        page.WaitForAssertion(() => Assert.False(WorkItemsApi.Get(107)!.Hidden));
        Assert.Empty(settings.HiddenItemsOf(repo.Root));
    }

    [Fact(DisplayName = "An item with a resolution note keeps the resolution behaviour: labelled, no eye, the page names the resolution")]
    public async Task AResolutionIsNotALegacyFlag()
    {
        await using BunitContext ctx = NewContext();
        Assert.True(NotesApi.Resolve(107, WorkItemsApi.ParseResolution("cancelled", 107), "No longer needed.", "Claude").Hidden);
        Assert.True(WorkItemsApi.Hide(123).Success);
        ctx.Services.GetRequiredService<Workspace>().Notify();

        nav!.WaitForAssertion(() => Assert.Contains("#wi123", nav.Find(".nav-hidden").TextContent));
        Assert.Empty(nav.FindAll("button[aria-label='Show work item #wi107']"));
        Assert.Contains("Cancelled", nav.Find(".nav-hidden").TextContent);
        string page = ctx.Render<WorkItemPage>(p => p.Add(c => c.Id, 107)).Find("p.warning").TextContent;
        Assert.Contains("Cancelled", page);
        Assert.DoesNotContain("Hidden in this app", page);
    }
}
