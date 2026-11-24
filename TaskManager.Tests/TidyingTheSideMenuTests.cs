using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Notes;
using Notes.PublicDTOs;
using Radzen;
using TaskManager.AppState;
using TaskManager.Components.Layout;
using TaskManager.Components.Pages;
using TaskManager.Components.Shared;
using TaskManager.Helpers;
using WorkItems;

namespace TaskManager.Tests;

/// <summary>
/// The side menu, tidied (#wi215, #wi219, #wi220): the collapsed Add entry icon sits like the other rail icons, the worklog's days list has no second Bugs button,
/// Open current log is a Settings action, saved arrangements open from an icon on the Organise header and the colour legend from an icon by the colour dropdown.
/// </summary>
public sealed class TidyingTheSideMenuTests : IDisposable
{
    private readonly TestRepo repo = new();
    private readonly string settingsFile = Path.Combine(Path.GetTempPath(), "rtm-tidy-" + Guid.NewGuid().ToString("N") + ".json");

    public void Dispose()
    {
        repo.Dispose();
        try { File.Delete(settingsFile); } catch (IOException) { }
    }

    private BunitContext NewContext(FolderOpener? opener = null)
    {
        BunitContext ctx = new();
        ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        ctx.Services.AddSingleton<UiService>();
        ctx.Services.AddRadzenComponents();
        ctx.Services.AddSingleton(new SettingsStore(settingsFile));
        ctx.Services.AddSingleton<Workspace>();
        if (opener is not null) ctx.Services.AddSingleton(opener);
        Assert.True(ctx.Services.GetRequiredService<Workspace>().Open(repo.Root).Success);
        ctx.Render<Overlays>();
        return ctx;
    }

    private static string Css() => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "wwwroot", "css", "app.css")).Replace("\r\n", "\n");

    /// <summary>The board's side menu, expanded, with the Organise section open.</summary>
    private static async Task<IRenderedComponent<SideNav>> OpenedMenu(BunitContext ctx)
    {
        IRenderedComponent<SideNav> nav = ctx.Render<SideNav>();
        nav.ClickWhenReady("button.nav-toggle");
        nav.WaitForAssertion(() => Assert.NotEmpty(nav.FindAll("button.nav-sec-h[data-sec=organise]")));
        if (nav.Find("button.nav-sec-h[data-sec=organise]").GetAttribute("aria-expanded") != "true") nav.ClickWhenReady("button.nav-sec-h[data-sec=organise]");
        await nav.InvokeAsync(() => { });
        nav.WaitForAssertion(() => Assert.NotEmpty(nav.FindAll("#sec-organise-body .field")));
        return nav;
    }

    [Fact(DisplayName = "The collapsed Add entry icon has the same size as the other rail icons")]
    public async Task TheCollapsedAddIconIsRailSized()
    {
        await using BunitContext ctx = NewContext();
        ctx.Services.GetRequiredService<NavigationManager>().NavigateTo("item/123");
        IRenderedComponent<SideNav> nav = ctx.Render<SideNav>();
        ctx.Services.GetRequiredService<UiService>().OpenWorklog(new WorklogActions(() => { }, () => { }, () => { }, () => { }, () => { }, () => { }));
        nav.Eventually(() => Assert.Single(nav.FindAll("nav.side:not(.expanded) button.nav-add-icon")));

        // the icon is a rail button like the others (the red New work item one); what widened it was the menu's wrap taking the expanded rows' negative margins on the 44px rail too
        Assert.Contains("nav-btn", nav.Find("button.nav-add-icon").ClassList);
        string css = Css();
        string[] widening = [.. css.Split('\n').Where(l => l.Contains("margin-inline: -6px") && l.Contains(".nav-add-menu"))];
        Assert.NotEmpty(widening);
        Assert.All(widening, l => Assert.Contains("nav.side.expanded .nav-add-menu", l));
        Assert.DoesNotContain("nav.side .nav-sec-h, nav.side .nav-add-menu", css);
        Assert.Contains("nav.side:not(.expanded) .nav-add-menu { align-self: stretch; }", css);   // the rail's width, not shrunk to the button: the same box as the other icons
    }

    [Fact(DisplayName = "The worklog's days list has no Bugs row; the Bugs pill filters bugs")]
    public async Task TheDaysListHasNoBugsRow()
    {
        await using BunitContext ctx = NewContext();
        ctx.Services.GetRequiredService<NavigationManager>().NavigateTo("item/123");
        UiService ui = ctx.Services.GetRequiredService<UiService>();
        (bool ok, _, int id) = NotesApi.Create(123, "Tester", DateTime.Now, NoteKinds.Bug);
        Assert.True(ok);
        Assert.True(NotesApi.SaveBody(id, "Health route answers 500 after a restart").Success);
        IRenderedComponent<SideNav> nav = ctx.Render<SideNav>();
        nav.ClickWhenReady("button.nav-toggle");
        ctx.Render<TaskManager.Components.Pages.Worklog>(p => p.Add(c => c.Item, WorkItemsApi.Get(123)!).Add(c => c.Root, repo.Root));
        nav.Eventually(() => Assert.NotEmpty(nav.FindAll(".nav-worklog button.nav-day")));

        // an unresolved bug adds no row; the days end with Description, as before
        string[] rows = [.. nav.FindAll(".nav-worklog button.nav-day").Select(r => r.FirstChild!.TextContent.Trim())];
        Assert.DoesNotContain("Bugs", rows);
        Assert.Equal("Description", rows[^1]);
        Assert.Empty(nav.FindAll(".nav-worklog button.nav-day-bugs"));

        // the pill is the way to the bugs
        Assert.Contains(nav.FindAll(".nav-worklog .nav-pills button").Select(b => b.TextContent.Trim()), t => t.StartsWith("Bugs"));
        nav.ClickWhenReady(".nav-worklog .nav-pills button:contains('Bugs')");
        nav.Eventually(() => Assert.True(ui.WorklogFilter.Bugs && !ui.WorklogFilter.AllKinds));
    }

    [Fact(DisplayName = "Open current log is on the Settings page, not in the side menu")]
    public async Task OpenCurrentLogIsInSettings()
    {
        string logs = Path.Combine(Path.GetTempPath(), "rtm-tidy-logs-" + Guid.NewGuid().ToString("N")[..8]);
        try
        {
            AppLog.Use(AppLog.Create(logs, console: false, LogLevel.Information, TimeProvider.System, pid: 77));
            string file = AppLog.CurrentFile!;
            List<string> opened = [];
            await using BunitContext ctx = NewContext(path => { opened.Add(path); return (true, ""); });

            IRenderedComponent<SideNav> nav = ctx.Render<SideNav>();
            nav.ClickWhenReady("button.nav-toggle");
            Assert.Empty(nav.FindAll("button.nav-log"));
            Assert.Empty(nav.FindAll("button[aria-label='Open current log']"));

            IRenderedComponent<TaskManager.Components.Pages.SettingsPage> page = ctx.Render<TaskManager.Components.Pages.SettingsPage>();
            page.Find("[data-setting='logs-folder'] button.open-current-log").Click();
            page.WaitForAssertion(() => Assert.Equal([file], opened));
        }
        finally { try { Directory.Delete(logs, recursive: true); } catch (IOException) { } }
    }

    [Fact(DisplayName = "Saved arrangements open from an icon on the Organise header")]
    public async Task ArrangementsOpenFromTheOrganiseHeader()
    {
        await using BunitContext ctx = NewContext();
        SettingsStore settings = ctx.Services.GetRequiredService<SettingsStore>();
        settings.SaveBoard(repo.Root, settings.Board(repo.Root) with { GroupBy = "priority" });
        settings.SaveArrangement(repo.Root, "Focus");
        IRenderedComponent<SideNav> nav = await OpenedMenu(ctx);

        Assert.Empty(nav.FindAll("button.nav-sec-h[data-sec=arrangements]"));   // no section of its own any more
        Assert.Empty(nav.FindAll(".nav-arr"));
        AngleSharp.Dom.IElement icon = nav.Find(".organise > .nav-sec-row > .menu-wrap button[aria-label='Saved arrangements']");
        Assert.NotNull(icon.QuerySelector(":scope > svg"));
        Assert.Equal("", icon.TextContent.Trim());
        Assert.Empty(nav.FindAll(".arr-row"));   // closed until clicked

        nav.ClickWhenReady("button[aria-label='Saved arrangements']");
        nav.WaitForAssertion(() => Assert.Equal(["Focus"], nav.FindAll(".menu-pop .arr-row .nm").Select(n => n.TextContent.Trim())));
        Assert.NotEmpty(nav.FindAll(".menu-pop input.arr-name"));

        // open, save as and delete work from there
        settings.SaveBoard(repo.Root, new BoardOptions());
        nav.ClickWhenReady("button[aria-label='Open arrangement Focus']");
        await nav.InvokeAsync(() => { });
        nav.WaitForAssertion(() => Assert.Equal("priority", settings.Board(repo.Root).GroupBy));
        nav.WaitForAssertion(() => Assert.Empty(nav.FindAll(".arr-row")));   // opening one closes the list
        nav.ClickWhenReady("button[aria-label='Saved arrangements']");
        nav.Find("input.arr-name").Input("Weekly");
        nav.ClickWhenReady("button.arr-save");
        await nav.InvokeAsync(() => { });
        nav.WaitForAssertion(() => Assert.Equal(["Focus", "Weekly"], settings.ArrangementNames(repo.Root)));
        nav.ClickWhenReady("button[aria-label='Delete arrangement Focus']");
        await nav.InvokeAsync(() => { });
        nav.WaitForAssertion(() => Assert.Equal(["Weekly"], settings.ArrangementNames(repo.Root)));
    }

    [Fact(DisplayName = "The colour legend is hidden until its icon next to the colour dropdown is clicked")]
    public async Task TheLegendIsBehindAnIcon()
    {
        await using BunitContext ctx = NewContext();
        SettingsStore settings = ctx.Services.GetRequiredService<SettingsStore>();
        settings.SaveBoard(repo.Root, settings.Board(repo.Root) with { ColorBy = "status" });
        IRenderedComponent<SideNav> nav = await OpenedMenu(ctx);

        AngleSharp.Dom.IElement icon = nav.Find("#sec-organise-body .field.colour button[aria-label='Colour legend']");
        Assert.Equal("false", icon.GetAttribute("aria-pressed"));
        Assert.Empty(nav.FindAll(".legend"));

        nav.ClickWhenReady("button[aria-label='Colour legend']");
        nav.WaitForAssertion(() => Assert.NotEmpty(nav.FindAll(".legend .lg")));
        Assert.Equal("true", nav.Find("button[aria-label='Colour legend']").GetAttribute("aria-pressed"));

        nav.ClickWhenReady("button[aria-label='Colour legend']");
        nav.WaitForAssertion(() => Assert.Empty(nav.FindAll(".legend")));

        // with no colouring there is nothing to explain: no icon
        settings.SaveBoard(repo.Root, settings.Board(repo.Root) with { ColorBy = "none" });
        nav.WaitForAssertion(() => Assert.Empty(nav.FindAll("button[aria-label='Colour legend']")));
    }
}
