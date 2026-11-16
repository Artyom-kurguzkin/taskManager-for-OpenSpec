using AngleSharp.Dom;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Radzen;
using TaskManager.Components;
using TaskManager.Components.Layout;
using TaskManager.Components.Shared;
using TaskManager.AppState;
using TaskManager.BackgroundJobs;
using TaskManager.Helpers;

namespace TaskManager.Tests;

/// <summary>
/// What a click in the side menu costs, by what the user does (#wi190): the menu is drawn when what it shows changed, its option blocks are kept
/// alive while an item is examined, and a folded section is not drawn at all. Counted with the components' own render counters, never timed.
/// </summary>
public sealed class SideMenuRenderBudgetTests : IDisposable
{
    private readonly TestRepo repo = new();
    private readonly string settingsFile = Path.Combine(Path.GetTempPath(), "rtm-ui-" + Guid.NewGuid().ToString("N") + ".json");

    private IRenderedComponent<SideNav>? nav;

    /// <summary>A menu over the fixture repository with the given sections open (they are saved, so the menu starts with them open), expanded.</summary>
    private BunitContext NewContext(params string[] openSections)
    {
        BunitContext ctx = new();
        ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        ctx.Services.AddSingleton<UiService>();
        ctx.Services.AddRadzenComponents();
        SettingsStore settings = new(settingsFile);
        foreach (string section in openSections) settings.ToggleMenuSection(section);
        ctx.Services.AddSingleton(settings);
        ctx.Services.AddSingleton<Workspace>();
        OpenSpecStore.OpenSpecStoreModule.State.RealWatcher = false;   // the counts are read between steps: a late event from the disk would reload the page in the middle of one
        Assert.True(ctx.Services.GetRequiredService<Workspace>().Open(repo.Root).Success);
        ctx.Render<Overlays>();
        nav = ctx.Render<SideNav>();
        nav.Find("button.nav-toggle").Click();
        nav.WaitForAssertion(() => Assert.NotEmpty(nav.FindAll(".nav-view")));
        return ctx;
    }

    private static UiService Ui(BunitContext ctx) => ctx.Services.GetRequiredService<UiService>();

    private static int Menu() => RenderCounts.Of<SideNav>();
    private static int Options() => RenderCounts.Of<SideNavOptions>();
    private static int Dropdowns() => RenderCounts.Of<Dropdown>();

    [Fact(DisplayName = "A click in the side menu redraws it once and its dropdowns not at all")]
    public async Task AClickInTheMenuDrawsItOnceAndNoDropdowns()
    {
        await using BunitContext ctx = NewContext("organise", "filters", "hidden");
        Assert.Equal(7, nav!.FindAll(".rz-dropdown").Count);   // the board's seven: sort by, columns, rows, colour, contributor, period, commit
        await nav.InvokeAsync(() => { });
        int menu = Menu(), options = Options(), dropdowns = Dropdowns();

        nav.Find("nav.side").Click();   // a click on the menu itself, where nothing is: it lets go of an examined item, and there is none
        await nav.InvokeAsync(() => { });
        Assert.Equal(0, Menu() - menu);
        Assert.Equal(0, Options() - options);
        Assert.Equal(0, Dropdowns() - dropdowns);

        nav.Find("button.nav-pill[data-opt=compact]").Click();   // a switch of the board: saved, and the menu shows it
        nav.WaitForAssertion(() => Assert.Equal("true", nav.Find("button.nav-pill[data-opt=compact]").GetAttribute("aria-pressed")));
        await nav.InvokeAsync(() => { });
        Assert.InRange(Menu() - menu, 0, 1);
        Assert.Equal(1, Options() - options);   // the pill is what changed
        Assert.Equal(0, Dropdowns() - dropdowns);
    }

    [Fact(DisplayName = "Letting go of an examined item keeps the side menu's options as they were")]
    public async Task LettingGoKeepsTheOptions()
    {
        await using BunitContext ctx = NewContext("organise", "filters", "hidden");
        string markup = nav!.Find(".nav-opts").InnerHtml;
        Assert.False(nav.Find(".nav-opts").HasAttribute("hidden"));
        await nav.InvokeAsync(() => { });
        int dropdowns = Dropdowns(), options = Options();

        await nav.InvokeAsync(() => Ui(ctx).Examine((repo.Root, 123)));
        nav.WaitForAssertion(() => Assert.NotEmpty(nav.FindAll(".examine")));
        Assert.True(nav.Find(".nav-opts").HasAttribute("hidden"));   // kept, out of sight, not destroyed
        Assert.Equal(7, nav.FindAll(".nav-opts .rz-dropdown").Count);

        await nav.InvokeAsync(() => Ui(ctx).Examine(null));
        nav.WaitForAssertion(() => Assert.Empty(nav.FindAll(".examine")));
        await nav.InvokeAsync(() => { });

        Assert.False(nav.Find(".nav-opts").HasAttribute("hidden"));
        Assert.Equal(markup, nav.Find(".nav-opts").InnerHtml);
        Assert.Equal(0, Options() - options);
        Assert.Equal(0, Dropdowns() - dropdowns);
    }

    [Fact(DisplayName = "A theme change does not redraw the side menu's options")]
    public async Task AThemeChangeDoesNotRedrawTheOptions()
    {
        await using BunitContext ctx = NewContext("organise", "filters", "hidden");
        SettingsStore settings = ctx.Services.GetRequiredService<SettingsStore>();
        await nav!.InvokeAsync(() => { });
        int menu = Menu(), options = Options(), dropdowns = Dropdowns();

        await nav.InvokeAsync(() => settings.Update(s => s with { Theme = "dark" }));
        await nav.InvokeAsync(() => { });
        Assert.Equal("dark", settings.Current.Theme);

        Assert.Equal(0, Menu() - menu);
        Assert.Equal(0, Options() - options);
        Assert.Equal(0, Dropdowns() - dropdowns);
    }

    [Fact(DisplayName = "A folded section is not drawn until it is opened")]
    public async Task AFoldedSectionIsNotDrawnUntilOpened()
    {
        await using BunitContext ctx = NewContext();   // every section folded
        await nav!.InvokeAsync(() => { });
        Assert.Empty(nav.FindAll(".rz-dropdown"));
        Assert.Empty(nav.Find("#sec-organise-body").Children);
        Assert.True(nav.Find("#sec-organise-body").HasAttribute("hidden"));
        int dropdowns = Dropdowns();
        Assert.Equal(0, dropdowns);

        nav.Find("button.nav-sec-h[data-sec=filters]").Click();
        nav.WaitForAssertion(() => Assert.Equal(3, nav.FindAll("#sec-filters-body .rz-dropdown").Count));   // contributor, period, commit
        await nav.InvokeAsync(() => { });

        Assert.Equal(3, Dropdowns() - dropdowns);   // drawn when opened, once each
        Assert.Empty(nav.FindAll("#sec-organise-body .rz-dropdown"));   // the others are still not drawn
        Assert.Empty(nav.Find("#sec-hidden-body").Children);
    }

    public void Dispose()
    {
        repo.Dispose();
        try { File.Delete(settingsFile); } catch (IOException) { }
    }
}
