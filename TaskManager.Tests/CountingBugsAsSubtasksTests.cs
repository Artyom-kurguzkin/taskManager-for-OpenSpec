using AngleSharp.Dom;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Notes;
using Notes.PublicDTOs;
using Radzen;
using TaskManager.AppState;
using TaskManager.Components.Layout;
using TaskManager.Components.Pages;
using TaskManager.Components.Shared;
using WorkItems;
using WorkItems.PublicDTOs;

namespace TaskManager.Tests;

/// <summary>
/// An unresolved bug is an open subtask of its work item, marked as a bug (the subtasks file is not written: the notes are the one place), and a resolved one is not.
/// The feed navigation in the side menu ends with the item's description, and has a Bugs entry counting the unresolved bugs.
/// </summary>
public sealed class CountingBugsAsSubtasksTests : IDisposable
{
    private readonly TestRepo repo = new();
    private readonly string settingsFile = Path.Combine(Path.GetTempPath(), "rtm-bugs-" + Guid.NewGuid().ToString("N") + ".json");

    public void Dispose()
    {
        repo.Dispose();
        try { File.Delete(settingsFile); } catch (IOException) { }
    }

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

    /// <summary>A bug report on item 123 whose first line says what is wrong.</summary>
    private static int Bug(string text, bool resolved = false)
    {
        (bool ok, _, int id) = NotesApi.Create(123, "Tester", DateTime.Now, NoteKinds.Bug);
        Assert.True(ok);
        Assert.True(NotesApi.SaveBody(id, text).Success);
        if (resolved) Assert.True(NotesApi.SetResolved(id, true).Success);
        return id;
    }

    private static IRenderedComponent<WorkItemPage> LinkedTasks(BunitContext ctx)
    {
        IRenderedComponent<WorkItemPage> page = ctx.Render<WorkItemPage>(p => p.Add(c => c.Id, 123));
        page.InvokeAsync(() => page.Find(".item-views input[value='linked']").ChangeAsync("linked")).GetAwaiter().GetResult();
        page.Eventually(() => Assert.NotEmpty(page.FindAll(".tile[data-tile='subtasks']")));
        return page;
    }

    private static string Count(IRenderedComponent<WorkItemPage> page) => page.Find(".tile[data-tile='subtasks'] .tile-h .count").TextContent.Trim();

    [Fact(DisplayName = "An unresolved bug counts as an open subtask and is listed with a bug mark")]
    public async Task UnresolvedBugCountsAsOpenSubtask()
    {
        await using BunitContext ctx = NewContext();
        WorkItem item = WorkItemsApi.Get(123)!;
        int done = item.Subtasks.Count(s => s.Done), total = item.Subtasks.Count;
        Bug("The health route answers 500 after a restart\nSeen twice on the staging box.");
        Bug("Build hash is empty in release builds");
        Bug("Old typo in the route name", resolved: true);

        IRenderedComponent<WorkItemPage> page = LinkedTasks(ctx);

        Assert.Equal($"{done} of {total + 2} done", Count(page));   // the two unresolved bugs are open subtasks, the resolved one is not counted
        IElement[] bugs = [.. page.FindAll(".tile[data-tile='subtasks'] .sub.bug")];
        Assert.Equal(2, bugs.Length);
        Assert.All(bugs, b => Assert.Equal("Bug", b.QuerySelector(".bug-mark")!.TextContent.Trim()));
        Assert.Contains(bugs, b => b.QuerySelector(".s-t")!.TextContent.Trim() == "The health route answers 500 after a restart");
        Assert.Empty(page.FindAll(".tile[data-tile='subtasks'] .sub:not(.bug) .bug-mark"));   // the plain subtasks carry no mark
        Assert.DoesNotContain("health route answers 500", repo.Read("openspec/changes/add-health-check/tasks.md"));   // computed from the notes, never written to the subtasks file
    }

    [Fact(DisplayName = "A resolved bug no longer counts as an open subtask")]
    public async Task ResolvedBugNoLongerCounts()
    {
        await using BunitContext ctx = NewContext();
        WorkItem item = WorkItemsApi.Get(123)!;
        int done = item.Subtasks.Count(s => s.Done), total = item.Subtasks.Count;
        int id = Bug("Ping route times out under load");
        IRenderedComponent<WorkItemPage> page = LinkedTasks(ctx);
        Assert.Equal($"{done} of {total + 1} done", Count(page));

        // ticking the bug's box resolves it: it drops out of the subtasks and the count
        page.InvokeAsync(() => page.Find(".tile[data-tile='subtasks'] .sub.bug input[type=checkbox]").ChangeAsync(true)).GetAwaiter().GetResult();
        page.Eventually(() =>
        {
            Assert.Equal($"{done} of {total} done", Count(page));
            Assert.Empty(page.FindAll(".tile[data-tile='subtasks'] .sub.bug"));
        });
        Note note = NotesApi.Get(id)!;
        Assert.NotNull(note.ResolvedAt);
        Assert.False(note.IsOpenBug);
        Assert.Contains("resolvedAt:", File.ReadAllText(note.Path));
    }

    [Fact(DisplayName = "The board card counts an unresolved bug among its subtasks")]
    public async Task CardCountsOpenBugs()
    {
        await using BunitContext ctx = NewContext();
        WorkItem item = WorkItemsApi.Get(123)!;
        int done = item.Subtasks.Count(s => s.Done), total = item.Subtasks.Count;
        Bug("Health route answers 500 after a restart");

        IRenderedComponent<BoardPage> board = ctx.Render<BoardPage>();
        board.Eventually(() => Assert.NotEmpty(board.FindAll("article.card")));
        IElement card = board.FindAll("article.card").First(c => c.TextContent.Contains(item.Title));

        Assert.Equal($"{done} of {total + 1} subtasks done", card.QuerySelector(".subs")!.GetAttribute("title"));
    }

    private IRenderedComponent<SideNav> Nav(BunitContext ctx)
    {
        ctx.Services.GetRequiredService<SettingsStore>().Update(s => s with { Split = false });
        ctx.Services.GetRequiredService<NavigationManager>().NavigateTo("item/123");
        IRenderedComponent<SideNav> nav = ctx.Render<SideNav>();
        nav.ClickWhenReady("button.nav-toggle");
        return nav;
    }

    [Fact(DisplayName = "The feed navigation ends with Description, which scrolls the feed to the description")]
    public async Task FeedNavigationEndsWithDescription()
    {
        await using BunitContext ctx = NewContext();
        IRenderedComponent<SideNav> nav = Nav(ctx);
        ctx.Render<Worklog>(p => p.Add(c => c.Item, WorkItemsApi.Get(123)!).Add(c => c.Root, repo.Root));
        nav.Eventually(() => Assert.NotEmpty(nav.FindAll(".nav-worklog button.nav-day")));

        IElement[] rows = [.. nav.FindAll(".nav-worklog button.nav-day")];
        Assert.Equal("Description", rows[^1].FirstChild!.TextContent.Trim());
        Assert.DoesNotContain(rows[..^1], r => r.FirstChild!.TextContent.Trim() == "Description");   // one entry, and the last

        nav.InvokeAsync(() => nav.FindAll(".nav-worklog button.nav-day")[^1].Click()).GetAwaiter().GetResult();
        nav.Eventually(() => Assert.Equal(".wk-first", ctx.JSInterop.Invocations["rtm.scrollToDay"].Last().Arguments[0]));
    }

    [Fact(DisplayName = "The feed navigation has a Bugs entry counting the unresolved bugs, which shows the bug notes")]
    public async Task FeedNavigationHasBugsEntry()
    {
        await using BunitContext ctx = NewContext();
        Bug("Health route answers 500 after a restart");
        Bug("Build hash is empty in release builds");
        Bug("Old typo in the route name", resolved: true);
        IRenderedComponent<SideNav> nav = Nav(ctx);
        IRenderedComponent<Worklog> log = ctx.Render<Worklog>(p => p.Add(c => c.Item, WorkItemsApi.Get(123)!).Add(c => c.Root, repo.Root));
        nav.Eventually(() => Assert.NotEmpty(nav.FindAll(".nav-worklog button.nav-day")));

        IElement entry() => nav.FindAll(".nav-worklog button.nav-day").Single(r => r.FirstChild!.TextContent.Trim() == "Bugs");
        Assert.Equal("2", entry().QuerySelector(".count")!.TextContent.Trim());

        nav.InvokeAsync(() => entry().Click()).GetAwaiter().GetResult();
        log.Eventually(() =>
        {
            string[] kinds = [.. log.FindAll(".wk-day .wk-e").Select(e => e.GetAttribute("data-kind")!).Distinct()];
            Assert.Equal(["bug"], kinds);
        });
    }
}
