using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Radzen;
using TaskManager.Components.Pages;
using TaskManager.Components.Layout;
using TaskManager.Components.Shared;
using TaskManager.AppState;
using TaskManager.Helpers;

namespace TaskManager.Tests;

/// <summary>A project's arrangement (view, grouping, hidden columns ...) can be saved under a name, several times, and opened again from the side menu; they live in the user's settings file next to the live arrangement.</summary>
public sealed class SavingNamedArrangementsTests : IDisposable
{
    private readonly TestRepo repo = new();
    private readonly TestRepo repo2 = new();
    private readonly string settingsFile = Path.Combine(Path.GetTempPath(), "rtm-named-" + Guid.NewGuid().ToString("N") + ".json");

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
        return ctx;
    }

    private static readonly string[] Focus = ["status:Blocked", "col:Progress"];

    [Fact(DisplayName = "An arrangement saved under a name can be opened later and brings back its hidden columns, grouping and view")]
    public void ASavedArrangementComesBack()
    {
        SettingsStore store = new(settingsFile);
        store.SaveBoard(repo.Root, new BoardOptions(Hidden: Focus, GroupBy: "priority", View: "list", Compact: true));
        store.SaveArrangement(repo.Root, "Focus");

        store.SaveBoard(repo.Root, new BoardOptions());   // the user shows everything again
        Assert.Empty(store.Board(repo.Root).Hidden ?? []);

        Assert.True(store.OpenArrangement(repo.Root, "Focus"));
        BoardOptions o = store.Board(repo.Root);
        Assert.Equal((Focus, "priority", "list", true), (o.Hidden, o.GroupBy, o.View, o.Compact));
        Assert.Equal(["Focus"], store.ArrangementNames(repo.Root));
    }

    [Fact(DisplayName = "Several arrangements can be kept per project, listed by name, and another project does not see them")]
    public void SeveralArrangementsPerProject()
    {
        SettingsStore store = new(settingsFile);
        store.SaveBoard(repo.Root, new BoardOptions(View: "list"));
        store.SaveArrangement(repo.Root, "Weekly list");
        store.SaveBoard(repo.Root, new BoardOptions(GroupBy: "feature"));
        store.SaveArrangement(repo.Root, "By feature");

        Assert.Equal(["By feature", "Weekly list"], store.ArrangementNames(repo.Root));
        Assert.Empty(store.ArrangementNames(repo2.Root));
        Assert.False(store.OpenArrangement(repo2.Root, "Weekly list"));
        Assert.True(store.OpenArrangement(repo.Root, "Weekly list"));
        Assert.Equal(("list", "status"), (store.Board(repo.Root).View, store.Board(repo.Root).GroupBy));
        Assert.True(store.OpenArrangement(repo.Root, "By feature"));
        Assert.Equal(("board", "feature"), (store.Board(repo.Root).View, store.Board(repo.Root).GroupBy));
    }

    [Fact(DisplayName = "Saving under an existing name updates that arrangement")]
    public void SavingUnderTheSameNameUpdates()
    {
        SettingsStore store = new(settingsFile);
        store.SaveBoard(repo.Root, new BoardOptions(GroupBy: "priority"));
        store.SaveArrangement(repo.Root, "Mine");
        store.SaveBoard(repo.Root, new BoardOptions(GroupBy: "label", Compact: true));
        store.SaveArrangement(repo.Root, " mine ");   // the same name: spaces and case do not make a second one

        Assert.Equal(["Mine"], store.ArrangementNames(repo.Root));
        store.SaveBoard(repo.Root, new BoardOptions());
        store.OpenArrangement(repo.Root, "Mine");
        Assert.Equal(("label", true), (store.Board(repo.Root).GroupBy, store.Board(repo.Root).Compact));
    }

    [Fact(DisplayName = "An empty name saves nothing")]
    public void AnEmptyNameSavesNothing()
    {
        SettingsStore store = new(settingsFile);
        store.SaveArrangement(repo.Root, "   ");
        Assert.Empty(store.ArrangementNames(repo.Root));
        Assert.Null(store.Current.Arrangements);
    }

    [Fact(DisplayName = "A deleted arrangement is gone after a restart")]
    public void ADeletedArrangementStaysGone()
    {
        SettingsStore store = new(settingsFile);
        store.SaveBoard(repo.Root, new BoardOptions(Hidden: Focus));
        store.SaveArrangement(repo.Root, "Focus");
        store.SaveArrangement(repo.Root, "Spare");
        Assert.Equal(["Focus", "Spare"], new SettingsStore(settingsFile).ArrangementNames(repo.Root));   // kept across a restart

        store.DeleteArrangement(repo.Root, "Focus");

        SettingsStore restarted = new(settingsFile);
        Assert.Equal(["Spare"], restarted.ArrangementNames(repo.Root));
        Assert.False(restarted.OpenArrangement(repo.Root, "Focus"));
        Assert.Equal(Focus, restarted.Board(repo.Root).Hidden);   // the arrangement on screen is not touched by deleting a saved one
    }

    [Fact(DisplayName = "A settings file without saved arrangements still loads")]
    public void AnOldFileStillLoads()
    {
        File.WriteAllText(settingsFile, "{ \"Theme\": \"dark\", \"Boards\": { }, \"OpenMenuSections\": [\"organise\"] }");

        SettingsStore store = new(settingsFile);

        Assert.Equal("dark", store.Current.Theme);
        Assert.Null(store.Current.Arrangements);
        Assert.Empty(store.ArrangementNames(repo.Root));
        store.SaveBoard(repo.Root, new BoardOptions(View: "list"));
        store.SaveArrangement(repo.Root, "First");
        Assert.Equal(["First"], new SettingsStore(settingsFile).ArrangementNames(repo.Root));
    }

    [Fact(DisplayName = "The side menu saves the arrangement under the typed name, lists it, opens it on a click and deletes it")]
    public async Task TheSideMenuSavesOpensAndDeletes()
    {
        await using BunitContext ctx = NewContext();
        SettingsStore settings = ctx.Services.GetRequiredService<SettingsStore>();
        IRenderedComponent<SideNav> nav = ctx.Render<SideNav>();
        settings.SaveBoard(repo.Root, settings.Board(repo.Root) with { Hidden = Focus, GroupBy = "priority" });
        if (nav.FindAll(".nav-view").Count == 0) nav.ClickWhenReady("button.nav-toggle");   // the menu is folded to its rail until opened
        nav.ClickWhenReady("button.nav-sec-h[data-sec=arrangements]");
        await nav.InvokeAsync(() => { });
        nav.WaitForAssertion(() => Assert.NotEmpty(nav.FindAll("input.arr-name")));
        Assert.Empty(nav.FindAll(".arr-row"));

        nav.Find("input.arr-name").Input("Focus");
        nav.ClickWhenReady("button.arr-save");
        await nav.InvokeAsync(() => { });
        nav.WaitForAssertion(() => Assert.Equal(["Focus"], nav.FindAll(".arr-row .nm").Select(n => n.TextContent.Trim())));
        Assert.Equal(["Focus"], settings.ArrangementNames(repo.Root));
        nav.WaitForAssertion(() => Assert.Equal("", nav.Find("input.arr-name").GetAttribute("value") ?? ""));   // the name box is empty for the next one

        settings.SaveBoard(repo.Root, new BoardOptions());
        nav.ClickWhenReady("button[aria-label='Open arrangement Focus']");
        await nav.InvokeAsync(() => { });
        nav.WaitForAssertion(() => Assert.Equal((Focus, "priority"), (settings.Board(repo.Root).Hidden, settings.Board(repo.Root).GroupBy)));

        nav.ClickWhenReady("button[aria-label='Delete arrangement Focus']");
        await nav.InvokeAsync(() => { });
        nav.WaitForAssertion(() => Assert.Empty(nav.FindAll(".arr-row")));
        Assert.Empty(new SettingsStore(settingsFile).ArrangementNames(repo.Root));
    }

    public void Dispose()
    {
        repo.Dispose();
        repo2.Dispose();
        try { File.Delete(settingsFile); } catch (IOException) { }
    }
}
