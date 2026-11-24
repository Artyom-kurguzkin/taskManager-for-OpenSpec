using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Radzen;
using TaskManager.AppState;
using TaskManager.Components.Layout;

namespace TaskManager.Tests;

/// <summary>A work item's worklog adds entries from the side menu; collapsed, the rail offers the same menu behind a plus icon.</summary>
public sealed class AddingEntriesFromTheCollapsedMenuTests : IDisposable
{
    private readonly TestRepo repo = new();
    private readonly string settingsFile = Path.Combine(Path.GetTempPath(), "rtm-ui-" + Guid.NewGuid().ToString("N") + ".json");

    private (BunitContext ctx, IRenderedComponent<SideNav> nav, UiService ui) NewItemPage()
    {
        BunitContext ctx = new();
        ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        ctx.Services.AddSingleton<UiService>();
        ctx.Services.AddRadzenComponents();
        ctx.Services.AddSingleton(new SettingsStore(settingsFile));
        ctx.Services.AddSingleton<Workspace>();
        Assert.True(ctx.Services.GetRequiredService<Workspace>().Open(repo.Root).Success);
        ctx.Services.GetRequiredService<NavigationManager>().NavigateTo("item/123");
        IRenderedComponent<SideNav> nav = ctx.Render<SideNav>();
        return (ctx, nav, ctx.Services.GetRequiredService<UiService>());
    }

    [Fact(DisplayName = "With the menu collapsed, a work item's rail shows an Add entry icon")]
    public async Task CollapsedRailShowsAnAddEntryIcon()
    {
        (BunitContext ctx, IRenderedComponent<SideNav> nav, UiService ui) = NewItemPage();
        await using BunitContext _ = ctx;
        Assert.Empty(nav.FindAll("nav.side button[aria-label='Add entry']"));   // no worklog open: nothing to add to

        ui.OpenWorklog(new WorklogActions(() => { }, () => { }, () => { }, () => { }, () => { }, () => { }));
        nav.Eventually(() => Assert.Single(nav.FindAll("nav.side:not(.expanded) button[aria-label='Add entry']")));
        AngleSharp.Dom.IElement add = nav.Find("button[aria-label='Add entry']");
        Assert.Equal("Add entry", add.GetAttribute("title"));
        Assert.NotNull(add.QuerySelector(":scope > svg"));
        Assert.Equal("", add.TextContent.Trim());   // an icon only

        ui.CloseWorklog(ui.WorklogOpen!);
        nav.Eventually(() => Assert.Empty(nav.FindAll("button[aria-label='Add entry']")));
    }

    [Fact(DisplayName = "The collapsed Add entry icon opens the same entries as the expanded menu")]
    public async Task CollapsedIconOpensTheSameEntries()
    {
        (BunitContext ctx, IRenderedComponent<SideNav> nav, UiService ui) = NewItemPage();
        await using BunitContext _ = ctx;
        List<string> done = [];
        ui.OpenWorklog(new WorklogActions(() => done.Add("note"), () => done.Add("decision"), () => done.Add("document"), () => done.Add("test"), () => done.Add("commit"), () => done.Add("subtask"),
            CanLinkTest: () => false, AddBug: () => done.Add("bug")));
        nav.Eventually(() => Assert.NotEmpty(nav.FindAll("button[aria-label='Add entry']")));

        nav.ClickWhenReady("button[aria-label='Add entry']");
        string[] collapsed = [.. nav.FindAll("button[role=menuitem]").Select(i => i.TextContent.Trim())];
        Assert.Equal(["Note", "Decision", "Bug", "Subtask", "Link document", "Link test", "Link commit"], collapsed);
        Assert.True(nav.FindAll("button[role=menuitem]")[5].HasAttribute("disabled"));   // no test results file, as expanded

        nav.FireWhenReady("button[role=menuitem]", e => e.Click(), all => all.Single(x => x.TextContent.Trim() == "Decision"));
        Assert.Equal(["decision"], done);

        await nav.InvokeAsync(() => { });
        nav.ClickWhenReady("button.nav-toggle");   // expanded: the block's own Add entry button, same entries
        nav.Eventually(() => Assert.NotEmpty(nav.FindAll(".nav-worklog button.nav-add")));
        Assert.Empty(nav.FindAll("nav.side:not(.expanded) button[aria-label='Add entry']"));
        nav.ClickWhenReady(".nav-worklog button.nav-add");
        Assert.Equal(collapsed, nav.FindAll("button[role=menuitem]").Select(i => i.TextContent.Trim()));
    }

    public void Dispose()
    {
        repo.Dispose();
        try { File.Delete(settingsFile); } catch { /* temp */ }
    }
}
