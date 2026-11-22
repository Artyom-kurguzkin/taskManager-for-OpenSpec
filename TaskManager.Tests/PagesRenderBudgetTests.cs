using AngleSharp.Dom;
using Bunit;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Notes;
using OpenSpecStore;
using Radzen;
using TaskManager.Components.Pages;
using TaskManager.Components;
using TaskManager.Components.Layout;
using TaskManager.Components.Shared;
using TaskManager.AppState;
using TaskManager.BackgroundJobs;
using TaskManager.Helpers;
using WorkItems;

namespace TaskManager.Tests;

/// <summary>
/// What a click or a keystroke costs on the item page (worklog, subtasks), the calendar and the analytics, by what the user does (#wi180):
/// counted with the components' own render counters and the pages' own work counters, never timed.
/// </summary>
public sealed class PagesRenderBudgetTests : IDisposable
{
    private readonly TestRepo repo = new();
    private readonly string settingsFile = Path.Combine(Path.GetTempPath(), "rtm-pg-" + Guid.NewGuid().ToString("N") + ".json");
    private readonly FakeTimeProvider time = new();   // the pages' 50 ms reload window moves only when a test moves it
    private IRenderedComponent<SideNav>? nav;

    public PagesRenderBudgetTests() => time.SetUtcNow(new DateTimeOffset(DateTime.SpecifyKind(DateTime.Today.AddHours(12), DateTimeKind.Unspecified), TimeSpan.Zero));   // the fake clock reads the same day as the notes and dates the tests write

    public void Dispose()
    {
        repo.Dispose();
        try { File.Delete(settingsFile); } catch (IOException) { }
    }

    private BunitContext NewContext(bool withNav = false)
    {
        BunitContext ctx = new();
        ctx.Services.AddSingleton<TimeProvider>(time);
        ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        ctx.Services.AddSingleton<UiService>();
        ctx.Services.AddRadzenComponents();
        ctx.Services.AddSingleton(new SettingsStore(settingsFile));
        ctx.Services.AddSingleton<Workspace>();
        OpenSpecStore.OpenSpecStoreModule.State.RealWatcher = false;   // the counts are read between steps: a late event from the disk would reload the page in the middle of one
        Assert.True(ctx.Services.GetRequiredService<Workspace>().Open(repo.Root).Success);
        ctx.Services.GetRequiredService<SettingsStore>().Update(s => s with { Name = "Tester" });
        ctx.Render<Overlays>();
        if (withNav) nav = ctx.Render<SideNav>();
        return ctx;
    }

    /// <summary>Item 123 (it has one older note of its own) with <paramref name="count"/> more notes of today, each with a title and a body (the worklog draws one menu per entry).</summary>
    private int[] Notes(int count)
    {
        DateTime today = DateTime.Today;
        int[] ids = new int[count];
        for (int i = 0; i < count; i++)
        {
            (bool _, string _, int id) = NotesApi.Create(123, "Tom", today.AddMinutes(i + 1));
            Assert.True(NotesApi.SetTitle(id, "Note " + i).Success);
            Assert.True(NotesApi.SaveBody(id, $"Some **words** {i}, and a link to #wi123.").Success);
            ids[i] = id;
        }
        return ids;
    }

    private static readonly MouseEventArgs Shift = new() { ShiftKey = true };

    private static void OpenEditor(IRenderedComponent<Worklog> log, int id)
    {
        log.FireWhenReady($".wk-note[data-note='{id}']", e => e.Click(Shift));
        log.WaitForAssertion(() => Assert.NotEmpty(log.FindAll(".menu-pop")));
        log.FireWhenReady(".menu-pop .menu-item", e => e.Click(), all => all.Single(i => i.TextContent.Trim() == "Edit"));
        log.WaitForAssertion(() => Assert.NotEmpty(log.FindAll(".wk-edit")));
    }

    [Fact(DisplayName = "Restoring the scroll spot of a view adds no draw: the board draws as often with a saved spot as without one (#wi202)")]
    public async Task RestoringTheScrollSpotAddsNoDraw()
    {
        await using BunitContext ctx = NewContext();
        IRenderedComponent<BoardPage> first = ctx.Render<BoardPage>();
        await first.InvokeAsync(() => { });
        int plain = RenderCounts.Of<BoardPage>();
        await ctx.DisposeComponentsAsync();
        RenderCounts.Reset();

        ctx.Services.GetRequiredService<UiService>().ScrollSpots[(SettingsStore.Key(repo.Root), "board")] = (1200, 0);
        IRenderedComponent<BoardPage> back = ctx.Render<BoardPage>();
        back.WaitForAssertion(() => Assert.Single(ctx.JSInterop.Invocations["rtm.restoreScroll"]));
        await back.InvokeAsync(() => { });
        Assert.Equal(plain, RenderCounts.Of<BoardPage>());
    }

    [Fact(DisplayName = "Typing in a worklog note redraws only the editor")]
    public async Task TypingInAWorklogNoteRedrawsOnlyTheEditor()
    {
        await using BunitContext ctx = NewContext();
        int[] ids = Notes(3);   // three entries are as many menus as it takes to see one built again; each note is three writes
        IRenderedComponent<Worklog> log = ctx.Render<Worklog>(p => p.Add(c => c.Item, WorkItemsApi.Get(123)!).Add(c => c.Root, repo.Root));
        OpenEditor(log, ids[1]);
        await log.InvokeAsync(() => { });
        int menus = RenderCounts.Of<Menu>(), logs = RenderCounts.Of<Worklog>();

        log.InputWhenReady(".wk-edit input.wk-title-in", "A new title");
        log.InputWhenReady(".wk-edit textarea", "abc");   // one keystroke is as much as two: a draw per event would show here
        await log.InvokeAsync(() => { });

        Assert.Equal(0, RenderCounts.Of<Menu>() - menus);      // not one entry's menu was built again
        Assert.Equal(0, RenderCounts.Of<Worklog>() - logs);    // the keystrokes only store the text: nothing else drew

        // what was typed is what leaving the editor saves
        TaskCompletionSource written = new(TaskCreationOptions.RunContinuationsAsynchronously);
        ctx.Services.GetRequiredService<Workspace>().Changed += () => written.TrySetResult();
        log.Find(".wk-edit textarea").FocusOut();
        log.WaitForAssertion(() => Assert.Empty(log.FindAll(".wk-edit")));
        Assert.True(await ctx.Services.GetRequiredService<Workspace>().DrainAsync(TimeSpan.FromSeconds(30)));   // written on the pool, after the editor closed
        time.Advance(TimeSpan.FromMilliseconds(300));   // the writes' quiet time (#wi213), then the pages are told
        await written.Task.WaitAsync(TimeSpan.FromSeconds(30));
        Assert.Equal("abc", NotesApi.Get(ids[1])!.Body.Trim());
        Assert.Equal("A new title", NotesApi.Get(ids[1])!.Title);
    }

    [Fact(DisplayName = "Folding a day in the worklog builds no entry menu again", Skip = "not done: entry menus are already behind RenderBoundary and day menus per day, but a fold still draws 3 Menus (budget 2); the third source is not found")]
    public async Task FoldingADayBuildsNoEntryMenuAgain()
    {
        await using BunitContext ctx = NewContext();
        Notes(30);
        IRenderedComponent<Worklog> log = ctx.Render<Worklog>(p => p.Add(c => c.Item, WorkItemsApi.Get(123)!).Add(c => c.Root, repo.Root));
        string today = DateTime.Today.ToString("yyyy-MM-dd");
        int menus = RenderCounts.Of<Menu>();

        log.ClickWhenReady($".wk-day[data-day='{today}'] .wk-date");   // fold today
        log.WaitForAssertion(() => Assert.Single(log.FindAll(".wk-note")));   // the fixture's own note is of an older day
        int folded = RenderCounts.Of<Menu>();
        log.ClickWhenReady($".wk-day[data-day='{today}'] .wk-date");   // and open it again: the entries come back (new ones, as they were gone)
        log.WaitForAssertion(() => Assert.Equal(31, log.FindAll(".wk-note").Count));

        Assert.True(folded - menus <= 2, $"folding drew {folded - menus} menus");   // the day's own menu, not the 30 entries'
    }

    [Fact(DisplayName = "Typing in a subtask redraws the item page once")]
    public async Task TypingInASubtaskRedrawsTheItemPageOnce()
    {
        await using BunitContext ctx = NewContext();
        IRenderedComponent<WorkItemPage> page = ctx.Render<WorkItemPage>(p => p.Add(c => c.Id, 123));
        page.InvokeAsync(() => page.Find(".item-views input[value='linked']").ChangeAsync("linked")).GetAwaiter().GetResult();
        page.WaitForAssertion(() => Assert.NotEmpty(page.FindAll(".tile[data-tile='subtasks']")));
        page.FireWhenReady(".tile[data-tile='subtasks'] .sub .s-t", e => e.Click(), all => all.Single(t => t.TextContent.Trim() == "Hide internals from anonymous callers"));
        IElement box = page.Find(".sub input.sub-edit");
        await page.InvokeAsync(() => { });
        int pages = RenderCounts.Of<WorkItemPage>();

        Act(page, () => box, e => e.KeyDown("x"));          // a keystroke is a keydown and an input
        Act(page, () => box, e => e.Input("Hide internals from anonymous callers x"));

        Assert.True(RenderCounts.Of<WorkItemPage>() - pages <= 1, $"a keystroke drew the page {RenderCounts.Of<WorkItemPage>() - pages} times");   // was twice

        Act(page, () => box, e => e.KeyDown("Enter"));      // saving still shows the saved text
        Assert.Contains("- [ ] 2.1 Hide internals from anonymous callers x", repo.Read("openspec/changes/add-health-check/tasks.md"));
        page.WaitForAssertion(() => Assert.Empty(page.FindAll(".sub input.sub-edit")));
    }

    [Fact(DisplayName = "Ticking a subtask does not render unchanged notes again")]
    public async Task TickingASubtaskDoesNotRenderUnchangedNotesAgain()
    {
        await using BunitContext ctx = NewContext();
        int[] ids = Notes(6);
        IRenderedComponent<WorkItemPage> page = ctx.Render<WorkItemPage>(p => p.Add(c => c.Id, 123));
        page.WaitForAssertion(() => Assert.Equal(7, page.FindAll(".wk-note").Count));
        int rendered = page.Instance.NoteHtml.Renders;
        Assert.True(rendered >= 6);   // every note was converted once

        page.InvokeAsync(() => page.Find(".item-views input[value='linked']").ChangeAsync("linked")).GetAwaiter().GetResult();
        page.WaitForAssertion(() => Assert.NotEmpty(page.FindAll(".tile[data-tile='subtasks']")));
        page.ChangeWhenReady("#sub-2-1", true);                       // saved (on the thread pool, after the page drew), the page and the feed read again
        page.WaitForAssertion(() => Assert.Contains("- [x] 2.1", repo.Read("openspec/changes/add-health-check/tasks.md")));
        page.InvokeAsync(() => page.Find(".item-views input[value='worklog']").ChangeAsync("worklog")).GetAwaiter().GetResult();
        page.WaitForAssertion(() => Assert.Equal(7, page.FindAll(".wk-note").Count));

        Assert.Equal(rendered, page.Instance.NoteHtml.Renders);   // not one note's markdown was converted again
        Assert.Equal(6, page.FindAll(".wk-note .wk-body").Count(b => b.TextContent.Contains("words")));

        // a note that changed is converted again, and only that one
        Assert.True(NotesApi.SaveBody(ids[0], "Changed **text**").Success);
        ctx.Services.GetRequiredService<Workspace>().Notify();
        page.WaitForAssertion(() => Assert.Contains("Changed", page.Markup));
        Assert.Equal(rendered + 1, page.Instance.NoteHtml.Renders);
    }

    /// <summary>Holds the folder of item 123 so its writes wait on the pool: what a page shows meanwhile is what it drew before the write.</summary>
    private static IDisposable HoldItemFolder(string root)
    {
        FolderLock.WaitLimit.Value = TimeSpan.FromSeconds(10);   // the write waits for the holder; the test releases it long before
        return FolderLock.Hold(root, "add-health-check");
    }

    private const string Tasks = "openspec/changes/add-health-check/tasks.md", Yaml = "openspec/changes/add-health-check/.openspec.yaml";

    [Fact(DisplayName = "Ticking a subtask renders the item page once before the write")]
    public async Task TickingASubtaskRendersTheItemPageOnceBeforeTheWrite()
    {
        await using BunitContext ctx = NewContext();
        IRenderedComponent<WorkItemPage> page = ctx.Render<WorkItemPage>(p => p.Add(c => c.Id, 123));
        page.InvokeAsync(() => page.Find(".item-views input[value='linked']").ChangeAsync("linked")).GetAwaiter().GetResult();
        page.WaitForAssertion(() => Assert.NotEmpty(page.FindAll("#sub-2-1")));
        Assert.Contains("2 of 4 done", page.Find(".tile[data-tile='subtasks'] .tile-h").TextContent);
        await page.InvokeAsync(() => { });
        int loads = page.Instance.Loads, drawn = RenderCounts.Of<WorkItemPage>();
        using IDisposable held = HoldItemFolder(repo.Root);

        Task tick = page.InvokeAsync(() => page.Find("#sub-2-1").ChangeAsync(true));
        await page.InvokeAsync(() => { });   // the dispatcher's next turn: the write is waiting on the pool

        Assert.False(tick.IsCompleted);
        Assert.Contains("3 of 4 done", page.Find(".tile[data-tile='subtasks'] .tile-h").TextContent);   // the count shows the tick ...
        Assert.Empty(page.FindAll("#sub-2-1"));                                                         // ... the subtask moved to the done ones
        Assert.Equal(1, RenderCounts.Of<WorkItemPage>() - drawn);
        Assert.Equal(loads, page.Instance.Loads);                                                        // nothing was read: it is a projection
        Assert.Contains("- [ ] 2.1", repo.Read(Tasks));                                                  // and nothing was written yet

        held.Dispose();
        await tick;
        Assert.Contains("- [x] 2.1", repo.Read(Tasks));
        await ctx.Services.GetRequiredService<Workspace>().Resolved;
        time.Advance(TimeSpan.FromMilliseconds(100));
        Assert.Equal(loads, page.Instance.Loads);                                                        // its own echo did not read the page again
    }

    [Fact(DisplayName = "A status pill on the item page renders once before the write")]
    public async Task AStatusPillOnTheItemPageRendersOnceBeforeTheWrite()
    {
        await using BunitContext ctx = NewContext();
        IRenderedComponent<WorkItemPage> page = ctx.Render<WorkItemPage>(p => p.Add(c => c.Id, 123));
        Assert.Equal("in In Progress since", page.Find(".moved-field label").TextContent.Trim());
        await page.InvokeAsync(() => { });
        int loads = page.Instance.Loads, drawn = RenderCounts.Of<WorkItemPage>();
        using IDisposable held = HoldItemFolder(repo.Root);

        page.ClickWhenReady(".status-field .dd");
        Task pick = page.InvokeAsync(() => page.FindAll(".status-field .rz-dropdown-item").First(i => i.TextContent.Trim() == "In Review").ClickAsync(new MouseEventArgs()));
        await page.InvokeAsync(() => { });

        Assert.False(pick.IsCompleted);
        Assert.Equal("in In Review since", page.Find(".moved-field label").TextContent.Trim());   // the page already says so
        Assert.Equal(loads, page.Instance.Loads);
        Assert.Contains("status: In Progress", repo.Read(Yaml));                                  // nothing was written yet

        held.Dispose();
        await pick;
        Assert.Contains("status: In Review", repo.Read(Yaml));
        await ctx.Services.GetRequiredService<Workspace>().Resolved;
        time.Advance(TimeSpan.FromMilliseconds(100));
        Assert.Equal(loads, page.Instance.Loads);
    }

    [Fact(DisplayName = "Saving a note renders the worklog once before the write and reads the note once")]
    public async Task SavingANoteRendersTheWorklogOnceBeforeTheWriteAndReadsTheNoteOnce()
    {
        await using BunitContext ctx = NewContext();
        int[] ids = Notes(3);
        IRenderedComponent<Worklog> log = ctx.Render<Worklog>(p => p.Add(c => c.Item, WorkItemsApi.Get(123)!).Add(c => c.Root, repo.Root));
        OpenEditor(log, ids[1]);
        log.InputWhenReady(".wk-edit input.wk-title-in", "A brand new title");
        log.InputWhenReady(".wk-edit textarea", "Brand new words");
        await log.InvokeAsync(() => { });
        int loads = log.Instance.Loads, drawn = RenderCounts.Of<Worklog>();
        string file = $"openspec/changes/add-health-check/notes/n{ids[1]}.md";
        using IDisposable held = HoldItemFolder(repo.Root);
        Workspace workspace = ctx.Services.GetRequiredService<Workspace>();
        int reads = NotesModule.FolderReads;
        TaskCompletionSource written = new(TaskCreationOptions.RunContinuationsAsynchronously);
        workspace.Changed += () => written.TrySetResult();

        Task save = log.InvokeAsync(() => log.Find(".wk-edit textarea").FocusOutAsync(new FocusEventArgs()));
        await log.InvokeAsync(() => { });

        Assert.Empty(log.FindAll(".wk-edit"));
        Assert.Contains("Brand new words", log.Find($".wk-note[data-note='{ids[1]}'] .wk-body").TextContent);   // the entry shows what was typed ...
        Assert.Contains("A brand new title", log.Find($".wk-note[data-note='{ids[1]}'] .wk-title").TextContent);
        Assert.Equal(1, RenderCounts.Of<Worklog>() - drawn);
        Assert.Equal(loads, log.Instance.Loads);
        Assert.DoesNotContain("Brand new words", repo.Read(file));                                              // ... before anything was written

        held.Dispose();
        await save;
        Assert.True(await workspace.DrainAsync(TimeSpan.FromSeconds(30)));   // written on the pool, after the entry drew
        time.Advance(TimeSpan.FromMilliseconds(300));   // the writes' quiet time (#wi213), then the pages are told
        await written.Task.WaitAsync(TimeSpan.FromSeconds(30));
        Assert.Contains("Brand new words", repo.Read(file));
        Assert.Contains("A brand new title", repo.Read(file));
        await workspace.Resolved;
        time.Advance(TimeSpan.FromMilliseconds(100));
        Assert.Equal(loads, log.Instance.Loads);   // its own echo did not read the worklog again
        Assert.Equal(1, NotesModule.FolderReads - reads);   // the notes folder was read once for the whole save (was once before each step, and again by the duplicate scan)
    }

    /// <summary>Item 123 with dates, the calendar view shown, the side menu expanded (a click then examines).</summary>
    private async Task<IRenderedComponent<BoardPage>> Calendar(BunitContext ctx)
    {
        DateOnly today = DateOnly.FromDateTime(DateTime.Today);
        WorkItemsApi.SetStarted(123, today);
        WorkItemsApi.SetDue(123, today.AddDays(1));
        IRenderedComponent<BoardPage> board = ctx.Render<BoardPage>();
        nav!.ClickWhenReady("button[aria-label=Layout]");
        nav.FireWhenReady(".menu-item", e => e.Click(), all => all.Single(i => i.TextContent.Trim() == "Calendar"));
        nav.ClickWhenReady("button.nav-toggle");
        board.WaitForAssertion(() => Assert.NotEmpty(board.FindAll(".rm-bar[data-id='123']")));
        await board.InvokeAsync(() => { });
        return board;
    }

    [Fact(DisplayName = "A click inside the calendar does not let go of the examined item")]
    public async Task AClickInsideTheCalendarDoesNotLetGoOfTheExaminedItem()
    {
        await using BunitContext ctx = NewContext(withNav: true);
        UiService ui = ctx.Services.GetRequiredService<UiService>();
        IRenderedComponent<BoardPage> board = await Calendar(ctx);
        int pages = RenderCounts.Of<BoardPage>(), roadmaps = RenderCounts.Of<Roadmap>();

        board.ClickWhenReady(".rm-bar[data-id='123']");   // examines it
        await board.InvokeAsync(() => { });
        Assert.Equal((repo.Root, 123), ui.Examined);
        Assert.Equal(1, RenderCounts.Of<BoardPage>() - pages);   // was twice
        Assert.Equal(1, RenderCounts.Of<Roadmap>() - roadmaps);  // was twice

        // the controls of the calendar are not the background: the examined item stays, and the board is not drawn for them
        pages = RenderCounts.Of<BoardPage>();
        board.ClickWhenReady(".rm-zoom [data-zoom=quarter]");
        board.ClickWhenReady(".rm-next");
        board.FireWhenReady(".rm-chev", e => e.Click(), all => all.First());
        await board.InvokeAsync(() => { });
        Assert.Equal((repo.Root, 123), ui.Examined);
        Assert.Equal(0, RenderCounts.Of<BoardPage>() - pages);
        Assert.Contains("quarter", board.Find(".rm-zoom .on").GetAttribute("data-zoom"));   // and they did what they are for
        Assert.NotEmpty(board.FindAll(".rm-chev.shut"));

        // the empty background does let go
        board.ClickWhenReady(".rm-head");
        await board.InvokeAsync(() => { });
        Assert.Null(ui.Examined);
    }

    [Fact(DisplayName = "Analytics does not rebuild its charts when nothing they show changed")]
    public async Task AnalyticsDoesNotRebuildItsChartsWhenNothingTheyShowChanged()
    {
        await using BunitContext ctx = NewContext();
        IRenderedComponent<Analytics> analytics = ctx.Render<Analytics>(p => p
            .Add(c => c.Items, WorkItemsApi.GetAll()).Add(c => c.Features, []).Add(c => c.Events, id => WorkItemsApi.GetEvents(id))
            .Add(c => c.Charts, AnalyticsModel.Charts(new BoardOptions())));
        int built = analytics.Instance.Builds, charts = RenderCounts.Of<Chart>(), shown = RenderCounts.Of<Analytics>();
        Assert.True(built > 0);

        // the board drew again: the same items in a new list, the same charts in a new list
        analytics.Render(p => p
            .Add(c => c.Items, [.. WorkItemsApi.GetAll()]).Add(c => c.Features, []).Add(c => c.Events, id => WorkItemsApi.GetEvents(id))
            .Add(c => c.Charts, [.. AnalyticsModel.Charts(new BoardOptions())]));
        await analytics.InvokeAsync(() => { });
        Assert.Equal(built, analytics.Instance.Builds);                 // no chart was worked out again
        Assert.Equal(charts, RenderCounts.Of<Chart>());                 // none was drawn again
        Assert.Equal(shown, RenderCounts.Of<Analytics>());

        // an item changed: they are
        Assert.True(WorkItemsApi.SetPriority(123, WorkItemsApi.Get(123)!.Priority == "high" ? "low" : "high").Success);
        analytics.Render(p => p
            .Add(c => c.Items, WorkItemsApi.GetAll()).Add(c => c.Features, []).Add(c => c.Events, id => WorkItemsApi.GetEvents(id))
            .Add(c => c.Charts, AnalyticsModel.Charts(new BoardOptions())));
        await analytics.InvokeAsync(() => { });
        Assert.True(analytics.Instance.Builds > built);
    }
    private static void Act<T>(Bunit.IRenderedComponent<T> cut, Func<AngleSharp.Dom.IElement?> find, Action<AngleSharp.Dom.IElement> fire) where T : Microsoft.AspNetCore.Components.IComponent
    {
        cut.Eventually(() => Xunit.Assert.NotNull(find()));   // rendered and present, or the test fails
        cut.InvokeAsync(() => fire(find()!)).GetAwaiter().GetResult();
    }
}
