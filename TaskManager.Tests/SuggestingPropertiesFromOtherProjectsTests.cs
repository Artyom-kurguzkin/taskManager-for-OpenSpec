using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Radzen;
using TaskManager.Components.Pages;
using TaskManager.Components.Shared;
using TaskManager.AppState;
using TaskManager.BackgroundJobs;
using TaskManager.Helpers;
using Features;
using WorkItems;

namespace TaskManager.Tests;

/// <summary>
/// Adding a property (a status, a feature) to a project beside other open projects: besides typing a new name, the dialog offers the values those projects
/// already have and this one lacks. Two projects are open: both have Research and Exports; "theirs" also has Parked and Billing.
/// </summary>
public sealed class SuggestingPropertiesFromOtherProjectsTests : IDisposable
{
    private readonly TestRepo mine = new();
    private readonly TestRepo theirs = new();
    private readonly string settingsFile = Path.Combine(Path.GetTempPath(), "rtm-suggest-" + Guid.NewGuid().ToString("N") + ".json");
    private IRenderedComponent<Overlays>? overlays;

    public void Dispose()
    {
        mine.Dispose();
        theirs.Dispose();
        File.Delete(settingsFile);
    }

    private static string NameOf(TestRepo r) => Path.GetFileName(r.Root);

    private BunitContext NewContext(string groupBy)
    {
        BunitContext ctx = new();
        ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        ctx.Services.AddSingleton<UiService>();
        ctx.Services.AddRadzenComponents();
        SettingsStore settings = new(settingsFile);
        ctx.Services.AddSingleton(settings);
        ctx.Services.AddSingleton<Workspace>();
        Workspace ws = ctx.Services.GetRequiredService<Workspace>();
        Assert.True(ws.Open(mine.Root).Success);
        Assert.True(ws.OpenTab(theirs.Root).Success);
        Assert.True(ws.Switch(mine.Root).Success);

        Assert.True(ws.In(mine.Root, () => WorkItemsApi.AddStatus("Research", beforeCompleted: true)).Success);
        Assert.True(ws.In(theirs.Root, () => WorkItemsApi.AddStatus("Research", beforeCompleted: true)).Success);
        Assert.True(ws.In(theirs.Root, () => WorkItemsApi.AddStatus("Parked", beforeCompleted: true)).Success);
        Assert.True(ws.In(mine.Root, () => FeaturesApi.CreateFeature(FeaturesApi.Ungrouped, "Exports")).Success);
        Assert.True(ws.In(theirs.Root, () => FeaturesApi.CreateFeature(FeaturesApi.Ungrouped, "Exports")).Success);
        Assert.True(ws.In(theirs.Root, () => FeaturesApi.CreateFeature(FeaturesApi.Ungrouped, "Billing")).Success);

        foreach (TestRepo r in new[] { mine, theirs }) settings.SaveBoard(r.Root, settings.Board(r.Root) with { GroupBy = groupBy });
        overlays = ctx.Render<Overlays>();
        return ctx;
    }

    private IReadOnlyList<string> Choices() => [.. overlays!.FindAll(".dialog .choice").Select(c => c.TextContent.Trim())];

    /// <summary>The "+ feature" button of one project's own row.</summary>
    private static string AddButton(IRenderedComponent<BoardPage> board, string project)
    {
        string selector = $"section.proj[data-proj='{project}'] button.add-col";
        board.WaitForAssertion(() => Assert.NotEmpty(board.FindAll(selector)));
        return selector;
    }

    private void ChooseProject(string name)
    {
        overlays!.WaitForAssertion(() => Assert.Contains(name, Choices()));
        overlays.FireWhenReady(".dialog .choice", e => e.Click(), all => all.Single(c => c.TextContent.Trim() == name));
    }

    [Fact(DisplayName = "Adding a status to a project suggests the statuses other open projects have and it lacks")]
    public async Task StatusesOfOtherProjectsAreSuggested()
    {
        await using BunitContext ctx = NewContext("status");
        IRenderedComponent<BoardPage> board = ctx.Render<BoardPage>();
        board.WaitForAssertion(() => Assert.NotEmpty(board.FindAll("button.add-col")));

        board.ClickWhenReady(".head-row > button.add-col");
        overlays!.WaitForAssertion(() => Assert.Equal([NameOf(mine), NameOf(theirs), "All projects"], Choices()));
        ChooseProject(NameOf(mine));

        // "theirs" has Parked and "mine" lacks it; Research and the built-in ones are in both: only Parked is offered, beside typing a new name
        overlays.WaitForAssertion(() => Assert.Equal(["Parked", "New status…"], Choices()));
    }

    [Fact(DisplayName = "Picking a suggested status adds it to the project and to no other")]
    public async Task PickingASuggestedStatusAddsIt()
    {
        await using BunitContext ctx = NewContext("status");
        Workspace ws = ctx.Services.GetRequiredService<Workspace>();
        IRenderedComponent<BoardPage> board = ctx.Render<BoardPage>();
        board.WaitForAssertion(() => Assert.NotEmpty(board.FindAll("button.add-col")));

        board.ClickWhenReady(".head-row > button.add-col");
        ChooseProject(NameOf(mine));
        overlays!.WaitForAssertion(() => Assert.Contains("Parked", Choices()));
        overlays.FireWhenReady(".dialog .choice", e => e.Click(), all => all.Single(c => c.TextContent.Trim() == "Parked"));

        board.WaitForAssertion(() => Assert.Contains("Parked", ws.In(mine.Root, () => WorkItemsApi.GetStatuses())));
        Assert.Equal(["Research", "Parked", "Completed"], ws.In(mine.Root, () => WorkItemsApi.GetStatuses()).TakeLast(3));   // before Completed
        Assert.Single(ws.In(theirs.Root, () => WorkItemsApi.GetStatuses()), "Parked");   // theirs is untouched
    }

    [Fact(DisplayName = "The suggestions of a status still leave the way to type a new name")]
    public async Task TypingANewNameStillWorks()
    {
        await using BunitContext ctx = NewContext("status");
        Workspace ws = ctx.Services.GetRequiredService<Workspace>();
        IRenderedComponent<BoardPage> board = ctx.Render<BoardPage>();
        board.WaitForAssertion(() => Assert.NotEmpty(board.FindAll("button.add-col")));

        board.ClickWhenReady(".head-row > button.add-col");
        ChooseProject(NameOf(mine));
        overlays!.WaitForAssertion(() => Assert.Contains("New status…", Choices()));
        overlays.FireWhenReady(".dialog .choice", e => e.Click(), all => all.Single(c => c.TextContent.Trim() == "New status…"));
        overlays.WaitForAssertion(() => Assert.NotEmpty(overlays.FindAll(".dialog input")));
        overlays.InputWhenReady(".dialog input", "Design");
        overlays.ClickWhenReady(".dialog button.primary");

        board.WaitForAssertion(() => Assert.Contains("Design", ws.In(mine.Root, () => WorkItemsApi.GetStatuses())));
        Assert.DoesNotContain("Design", ws.In(theirs.Root, () => WorkItemsApi.GetStatuses()));
    }

    [Fact(DisplayName = "Adding a feature suggests the features of other open projects")]
    public async Task FeaturesOfOtherProjectsAreSuggested()
    {
        await using BunitContext ctx = NewContext("feature");
        IRenderedComponent<BoardPage> board = ctx.Render<BoardPage>();

        board.ClickWhenReady(AddButton(board, NameOf(mine)));

        // "theirs" has Billing and "mine" lacks it; Exports is in both
        overlays!.WaitForAssertion(() => Assert.Equal(["Billing", "New feature…"], Choices()));
    }

    [Fact(DisplayName = "Picking a suggested feature adds it to the project")]
    public async Task PickingASuggestedFeatureAddsIt()
    {
        await using BunitContext ctx = NewContext("feature");
        Workspace ws = ctx.Services.GetRequiredService<Workspace>();
        IRenderedComponent<BoardPage> board = ctx.Render<BoardPage>();

        board.ClickWhenReady(AddButton(board, NameOf(mine)));
        overlays!.WaitForAssertion(() => Assert.Contains("Billing", Choices()));
        overlays.FireWhenReady(".dialog .choice", e => e.Click(), all => all.Single(c => c.TextContent.Trim() == "Billing"));

        board.WaitForAssertion(() => Assert.Contains(ws.In(mine.Root, () => FeaturesApi.GetAll()), f => f.Name == "Billing"));
    }

    [Fact(DisplayName = "With no other open project, adding a status asks for a name straight away")]
    public async Task NoSuggestionsWithOneProject()
    {
        await using BunitContext ctx = NewContext("status");
        Workspace ws = ctx.Services.GetRequiredService<Workspace>();
        ws.CloseTab(theirs.Root);
        IRenderedComponent<BoardPage> board = ctx.Render<BoardPage>();
        board.WaitForAssertion(() => Assert.NotEmpty(board.FindAll("button.add-col")));

        board.ClickWhenReady("button.add-col");
        overlays!.WaitForAssertion(() => Assert.Contains("New status", overlays.Find(".dialog").TextContent));
        Assert.Empty(Choices());
    }
}
