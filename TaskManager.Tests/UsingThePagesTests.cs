using Radzen.Blazor;
using Radzen;
using AngleSharp.Dom;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Notes;
using TaskManager.Components.Layout;
using TaskManager.Components.Pages;
using TaskManager.Components.Shared;
using TaskManager.AppState;
using TaskManager.BackgroundJobs;
using TaskManager.Helpers;
using WorkItems;

namespace TaskManager.Tests;

public sealed class UsingThePagesTests : IDisposable
{
    private readonly TestRepo repo = new();
    private readonly string settingsFile = Path.Combine(Path.GetTempPath(), "rtm-ui-" + Guid.NewGuid().ToString("N") + ".json");
    private IRenderedComponent<Overlays>? overlays;

    private BunitContext NewContext(EditorLauncher? launcher = null, WindowLauncher? windows = null, AppMode? mode = null, TaskManager.Api.AgentStatus? server = null, TimeProvider? time = null)
    {
        BunitContext ctx = new();
        if (time is not null) ctx.Services.AddSingleton(time);   // the pages' timers (autosave, reload coalescing) run on it: the test moves time by hand
        if (server is not null) ctx.Services.AddSingleton(server);
        if (windows is not null) ctx.Services.AddSingleton(windows);
        if (mode is not null) ctx.Services.AddSingleton(mode);
        ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        ctx.Services.AddSingleton<UiService>();
        ctx.Services.AddRadzenComponents();   // Radzen components ask for its services (the markdown editor wants DialogService)
        ctx.Services.AddSingleton(new SettingsStore(settingsFile));
        ctx.Services.AddSingleton<Workspace>();
        if (launcher is not null) ctx.Services.AddSingleton(launcher);
        Assert.True(ctx.Services.GetRequiredService<Workspace>().Open(repo.Root).Success);
        overlays = ctx.Render<Overlays>();
        return ctx;
    }

    // ── Work item page ──

    // The page has two views under its header (Worklog, Linked tasks), switched with a segmented control of radio inputs named item-view.
    private static string CurrentView(IRenderedComponent<WorkItemPage> page) => page.Find(".item-views .seg-o.is-sel input").GetAttribute("value")!;

    private static void View(IRenderedComponent<WorkItemPage> page, string view)
    {
        page.InvokeAsync(() => page.Find($".item-views input[value='{view}']").ChangeAsync(view)).GetAwaiter().GetResult();   // find and trigger in one dispatcher step: a render in between would drop the handler
        page.WaitForAssertion(() => Assert.Equal(view, CurrentView(page)));
    }

    /// <summary>A subtask's text in the linked tasks main list (a folded done subtask is not there until its group's "N done" is opened).</summary>
    private static IElement SubText(IRenderedComponent<WorkItemPage> page, string text) =>
        page.FindAll(".tile[data-tile='subtasks'] .sub .s-t").Single(t => t.TextContent.Trim() == text);

    private static List<string> Tiles(IRenderedComponent<WorkItemPage> page) => page.FindAll(".tile").Select(t => t.GetAttribute("data-tile")!).ToList();

    [Fact(DisplayName = "The item page opens on the worklog, with Worklog and Linked tasks to switch between, the current one marked, and the description is the worklog's last entry")]
    public async Task TwoViews()
    {
        await using BunitContext ctx = NewContext();
        IRenderedComponent<WorkItemPage> page = ctx.Render<WorkItemPage>(p => p.Add(c => c.Id, 123));

        Assert.Equal(["Worklog", "Linked tasks"], page.FindAll(".item-views .seg-o").Select(l => l.TextContent.Trim()));
        Assert.Equal("worklog", CurrentView(page));
        Assert.Single(page.FindAll(".item-views .seg-o.is-sel"));
        Assert.NotEmpty(page.FindAll(".worklog"));
        Assert.Equal("wk-first", page.Find(".worklog").Children[^1].ClassName);   // the description, the last entry, after the days
        Assert.Contains("Expose a lightweight /health endpoint", page.Find(".wk-first .desc").TextContent);
        Assert.Empty(page.FindAll(".sub"));          // the subtasks are one view away
        Assert.Empty(page.FindAll(".bento"));        // the tile grid is gone
        Assert.Empty(page.FindAll("button.tile-h")); // and so is tile focus
        Assert.Empty(page.FindAll(".item-views input[value='description']"));   // no view of its own

        View(page, "linked");
        Assert.Equal(2, page.FindAll(".item-views .seg-o").Count);   // both stay, the marked one moves
        Assert.Single(page.FindAll(".item-views .seg-o.is-sel"));
        Assert.NotEmpty(page.FindAll(".view-linked"));
        Assert.Empty(page.FindAll(".worklog"));

        View(page, "worklog");
        Assert.NotEmpty(page.FindAll(".worklog"));
        Assert.Empty(page.FindAll(".view-linked"));
    }

    [Fact(DisplayName = "The linked tasks view lists what is attached to the item in the rail's Related list: the plan, documents and attachments of its notes, and images, each once")]
    public async Task RelatedList()
    {
        await using BunitContext ctx = NewContext();
        IRenderedComponent<WorkItemPage> empty = ctx.Render<WorkItemPage>(p => p.Add(c => c.Id, 107));
        View(empty, "linked");
        Assert.Contains("Nothing attached yet.", empty.Find(".related").TextContent);
        Assert.Empty(empty.FindAll(".related .rel"));

        repo.Write("DevResources/plans/health-2026-11-05.md", "# Health plan\n");
        repo.Write("docs/guide.md", "# Guide\n");
        repo.Write("docs/runbook.md", "# Runbook\n");
        repo.Write("local/task-tracking/wi123/shot.png", "png");
        repo.Write("local/task-tracking/n123/diagram.png", "png");
        Assert.True(WorkItemsApi.SetPlan(123, "DevResources/plans/health-2026-11-05.md").Success);
        Assert.True(WorkItemsApi.SetDescription(123, "Why\n\nSee [[guide]] (also [[nope]]) and the [runbook](docs/runbook.md).\n\n![shot](local/task-tracking/wi123/shot.png)\n\nThe [spec](https://example.com/spec).\n").Success);
        Assert.True(NotesApi.SaveBody(123, "# Ping test\n\nAgain [[guide]] and ![same](local/task-tracking/wi123/shot.png) and ![other](local/task-tracking/n123/diagram.png)\n").Success);
        Assert.True(NotesApi.AttachBytes(123, "report.pdf", [1, 2, 3]).Success);
        Assert.True(NotesApi.AttachLink(123, "https://example.com/spec").Success);   // the same address the description links to

        IRenderedComponent<WorkItemPage> page = ctx.Render<WorkItemPage>(p => p.Add(c => c.Id, 123));
        View(page, "linked");
        List<(string Kind, string Name)> rows = page.FindAll(".related .rel").Select(r => (r.QuerySelector(".k")!.TextContent.Trim(), r.QuerySelector(".rel-open")!.TextContent.Trim())).ToList();

        Assert.Equal(7, rows.Count);
        Assert.Equivalent(new[]
        {
            ("plan", "health-2026-11-05.md"), ("doc", "guide.md"), ("doc", "runbook.md"), ("image", "shot.png"),
            ("link", "https://example.com/spec"), ("image", "diagram.png"), ("file", "report.pdf"),
        }, rows);
        Assert.Equal(rows.Count, rows.Distinct().Count());
        Assert.DoesNotContain("Nothing attached yet.", page.Find(".related").TextContent);
        Assert.Equal(["Subtasks", "Waiting on", "Waiting on this", "Related"], page.FindAll(".tile-h h3").Select(h => h.TextContent.Trim()));   // Related is the last list of the rail
        Assert.Equal("7", page.Find(".ln-rail .related .tile-h .count").TextContent.Trim());

        // plans and documents open the app's document page, a link opens as notes open theirs
        Assert.Equal("doc/DevResources/plans/health-2026-11-05.md", page.FindAll(".related .rel-open").Single(a => a.TextContent.Trim() == "health-2026-11-05.md").GetAttribute("href"));
        Assert.Equal("doc/docs/guide.md", page.FindAll(".related .rel-open").Single(a => a.TextContent.Trim() == "guide.md").GetAttribute("href"));
        IElement link = page.FindAll(".related .rel-open").Single(a => a.TextContent.Trim() == "https://example.com/spec");
        Assert.Equal("https://example.com/spec", link.GetAttribute("href"));
        Assert.Equal("_blank", link.GetAttribute("target"));
    }

    [Fact(DisplayName = "A merged item's Linked tasks lists the subtasks of every source under their own group, and its worklog's Bugs filter finds the bug notes of every source")]
    public async Task MergedItemShowsEverySourcesTasksAndBugs()
    {
        await using BunitContext ctx = NewContext();
        Assert.True(WorkItemsApi.AddSubtask(107, "Source two task").Success);
        (bool _, string _, int bug) = NotesApi.Create(107, "Tom", DateTime.Now, "bug");
        Assert.True(NotesApi.SaveBody(bug, "# Rows jump\n\nA row jumps when sorted.\n").Success);
        (bool ok, string message, int id) = WorkItemsApi.Merge([123, 107]);
        Assert.True(ok, message);

        IRenderedComponent<WorkItemPage> page = ctx.Render<WorkItemPage>(p => p.Add(c => c.Id, id));
        View(page, "linked");
        string tile = page.Find("[data-tile='subtasks']").TextContent;
        Assert.Contains("Source two task", tile);
        Assert.Contains("merged " + DateTime.Now.ToString("yyyy-MM-dd"), tile);
        Assert.Contains(WorkItemsApi.Get(id)!.Subtasks.First(s => s.Text == "Source two task").Group.Split(' ', 2)[1].Split(" · ")[0], tile);

        View(page, "worklog");
        Assert.Equal(bug, WorkItemsApi.Get(id) is null ? 0 : NotesApi.GetForWorkItem(id).Single(n => n.IsBug).Id);
        Assert.Contains("Rows jump", page.Markup);
    }

    [Fact(DisplayName = "A merged item's feed says which source every note, bug and event came from, also with the Notes filter off, and Related lists the files under their source")]
    public async Task MergedItemNamesWhereEverythingCameFrom()
    {
        await using BunitContext ctx = NewContext();
        UiService ui = ctx.Services.GetRequiredService<UiService>();
        (bool _, string _, int first) = WorkItemsApi.Create("List moving", "Backlog", null, null);
        (bool _, string _, int second) = WorkItemsApi.Create("Swap groups", "Backlog", null, null);
        (bool _, string _, int own) = NotesApi.Create(first, "Tom", DateTime.Now, "bug");
        Assert.True(NotesApi.AttachBytes(own, "own.png", [1]).Success);
        Assert.True(WorkItemsApi.AddSubtask(second, "Drag the header").Success);
        (bool _, string _, int bug) = NotesApi.Create(second, "Tom", DateTime.Now, "bug");
        Assert.True(NotesApi.SetTitle(bug, "Rows jump").Success);
        Assert.True(NotesApi.AttachBytes(bug, "shot.png", [1, 2, 3]).Success);
        (bool ok, string message, int id) = WorkItemsApi.Merge([first, second], first);
        Assert.True(ok, message);

        IRenderedComponent<WorkItemPage> page = ctx.Render<WorkItemPage>(p => p.Add(c => c.Id, id));
        View(page, "worklog");
        string from = $"from #wi{second}";
        IElement Entry(string kind, string text) => page.FindAll($".wk-e[data-kind='{kind}']").Single(e => e.TextContent.Contains(text));
        Assert.Contains(from, Entry("bug", "Rows jump").TextContent);   // a bug note
        Assert.Contains(from, Entry("subtask-added", "Drag the header").TextContent);   // an event of the source
        Assert.Single(page.FindAll(".wk-e[data-kind='created']"), e => e.TextContent.Contains(from));   // the source's created line, not the main item's
        Assert.DoesNotContain($"from #wi{first}", page.Markup);
        ui.FilterWorklog(new WorklogFilters(Notes: false));
        page.WaitForAssertion(() => Assert.Contains(from, Entry("bug", "Rows jump").TextContent));   // the Bugs filter's view too

        View(page, "linked");
        Assert.Equal(["this item", from], page.FindAll(".rel-list .rel-from").Select(h => h.TextContent.Trim()));
        IElement[] rows = [.. page.FindAll(".rel-list > li")];
        Assert.Contains("own.png", rows[1].TextContent);
        Assert.Contains(from, rows[2].TextContent);
        Assert.Contains("shot.png", rows[3].TextContent);
    }

    [Fact(DisplayName = "Related stays one flat list when nothing in the item was merged in")]
    public async Task RelatedIsFlatWithoutMerge()
    {
        await using BunitContext ctx = NewContext();
        (bool _, string _, int note) = NotesApi.Create(123, "Tom", DateTime.Now, "bug");
        Assert.True(NotesApi.AttachBytes(note, "shot.png", [1]).Success);
        IRenderedComponent<WorkItemPage> page = ctx.Render<WorkItemPage>(p => p.Add(c => c.Id, 123));
        View(page, "linked");
        Assert.Empty(page.FindAll(".rel-list .rel-from"));
        Assert.Contains("shot.png", page.Find(".rel-list").TextContent);
    }

    [Fact(DisplayName = "When only one source of a merge had subtasks, Linked tasks still heads them with where and when they were merged in")]
    public async Task MergedSingleGroupKeepsItsHeader()
    {
        await using BunitContext ctx = NewContext();
        (bool _, string _, int first) = WorkItemsApi.Create("List moving", "Backlog", null, null);
        (bool _, string _, int second) = WorkItemsApi.Create("Swap groups", "Backlog", null, null);
        Assert.True(WorkItemsApi.AddSubtask(second, "Drag the header").Success);
        (bool ok, string message, int id) = WorkItemsApi.Merge([first, second]);
        Assert.True(ok, message);

        IRenderedComponent<WorkItemPage> page = ctx.Render<WorkItemPage>(p => p.Add(c => c.Id, id));
        View(page, "linked");

        Assert.Equal([$"#wi{second} Swap groups · merged {DateTime.Now:yyyy-MM-dd}"], page.FindAll(".sub-group .g-name").Select(n => n.TextContent.Trim()));
        Assert.Contains("Drag the header", page.Find("[data-tile='subtasks']").TextContent);
    }

    [Fact(DisplayName = "The linked tasks rail lists what the item waits on and what waits on it, each with its status pill, and says so when a list is empty")]
    public async Task LinkedTasksRail()
    {
        await using BunitContext ctx = NewContext();
        WorkItemsApi.AddDependency(123, 125);   // 123 waits on 125
        WorkItemsApi.AddDependency(107, 123);   // and 107 waits on 123
        IRenderedComponent<WorkItemPage> page = ctx.Render<WorkItemPage>(p => p.Add(c => c.Id, 123));

        View(page, "linked");

        Assert.Equal(["subtasks", "waiting", "blocks", "related"], Tiles(page));
        Assert.Equal(["waiting", "blocks", "related"], page.FindAll(".ln-rail > .tile").Select(t => t.GetAttribute("data-tile")!));   // the rail holds the three, the subtasks are the main list
        Assert.Empty(page.FindAll(".ln-rail [data-tile='subtasks']"));
        IElement waits = page.Find(".tile[data-tile='waiting'] .sub.dep.waits");
        Assert.Contains("#wi125", waits.TextContent);
        Assert.Equal(WorkItemsApi.Get(125)!.Status, waits.QuerySelector(".tag.lbl")!.TextContent.Trim());   // the status as a pastel pill
        Assert.Contains("--fill:var(--", waits.QuerySelector(".tag.lbl")!.GetAttribute("style"));
        Assert.Contains("+ wait on another item", page.Find(".tile[data-tile='waiting'] button[aria-label='Wait on another work item']").TextContent);
        IElement blocks = page.Find(".tile[data-tile='blocks'] .sub.dep.blocks");
        Assert.Contains("#wi107", blocks.TextContent);
        Assert.Equal(WorkItemsApi.Get(107)!.Status, blocks.QuerySelector(".tag.lbl")!.TextContent.Trim());
        Assert.Equal("1", page.Find(".tile[data-tile='blocks'] .tile-h .count").TextContent.Trim());
        Assert.Empty(page.FindAll(".tile[data-tile='subtasks'] .sub.dep"));   // the dependencies are in the rail, not among the subtasks

        // empty lists say so
        IRenderedComponent<WorkItemPage> alone = ctx.Render<WorkItemPage>(p => p.Add(c => c.Id, 125));
        View(alone, "linked");
        Assert.Equal(["subtasks", "waiting", "blocks", "related"], Tiles(alone));   // all of them, also when something is empty
        Assert.Contains("Nothing.", alone.Find(".tile[data-tile='waiting']").TextContent);
        IRenderedComponent<WorkItemPage> loner = ctx.Render<WorkItemPage>(p => p.Add(c => c.Id, 124));
        View(loner, "linked");
        Assert.Contains("Nothing.", loner.Find(".tile[data-tile='blocks']").TextContent);
    }

    [Fact(DisplayName = "Subtasks are ticked, edited, added and selected in the linked tasks view as they were in the subtasks tile")]
    public async Task SubtasksInTheLinkedView()
    {
        FakeTimeProvider time = new();   // the workspace's quiet time (a tick tells the pages 300 ms after its write) and the page's reload window run on it: they end when the test says
        await using BunitContext ctx = NewContext(time: time);
        Workspace workspace = ctx.Services.GetRequiredService<Workspace>();
        IRenderedComponent<WorkItemPage> page = ctx.Render<WorkItemPage>(p => p.Add(c => c.Id, 123));
        View(page, "linked");

        // Every write tells the page (the tick's after its quiet time) and the page draws again, which gives the add line's input a new handler: a draw that landed between a Find and its event
        // was the old "no event handler with ID" failure. A step is settled when the write has landed, the news is told and the draw is done; the next Find sees the final tree.
        async Task Settled()
        {
            Assert.True(await workspace.DrainAsync(TimeSpan.FromSeconds(30)));   // the write has landed and its news is armed
            time.Advance(TimeSpan.FromSeconds(1));   // the quiet time ends: the pages are told, and the reload window that follows ends
            await workspace.Resolved;   // the scan the news started
            await page.InvokeAsync(() => { });   // the draw the news asked for has run
        }

        await page.InvokeAsync(() => page.Find("#sub-1-3").ChangeAsync(true));   // tick; find and trigger in one dispatcher step
        page.WaitForAssertion(() => Assert.Contains("- [x] 1.3", repo.Read("openspec/changes/add-health-check/tasks.md")));
        page.WaitForAssertion(() => Assert.Empty(page.FindAll("#sub-1-3")));   // done: folded away
        await Settled();

        await page.InvokeAsync(() => SubText(page, "Hide internals from anonymous callers").ClickAsync());   // edit in place
        page.WaitForAssertion(() => Assert.Equal("Hide internals from anonymous callers", page.Find(".sub input.sub-edit").GetAttribute("value")));
        await page.InvokeAsync(() => page.Find(".sub input.sub-edit").InputAsync("Hide internals from everyone"));
        await page.InvokeAsync(() => page.Find(".sub input.sub-edit").KeyDownAsync(Bunit.Key.Enter));
        page.WaitForAssertion(() => Assert.Contains("2.1 Hide internals from everyone", repo.Read("openspec/changes/add-health-check/tasks.md")));
        Assert.Empty(page.FindAll(".sub input.sub-edit"));
        await Settled();

        await page.InvokeAsync(() => page.Find("input[aria-label='New subtask']").ChangeAsync("A fresh step"));   // add
        await page.InvokeAsync(() => page.Find("form.add-line").SubmitAsync());
        page.WaitForAssertion(() => Assert.Contains(WorkItemsApi.Get(123)!.Subtasks, s => s.Text == "A fresh step"));
        await Settled();
        page.WaitForAssertion(() => Assert.Equal(2, page.FindAll(".tile[data-tile='subtasks'] .sub:not(.done)").Count));   // the open ones: 2.1 and the new one

        Assert.Empty(page.FindAll(".actionbar"));   // select: Shift-click
        await page.InvokeAsync(() => page.FindAll(".sub .s-t")[0].ClickAsync(new Microsoft.AspNetCore.Components.Web.MouseEventArgs { ShiftKey = true }));
        page.WaitForAssertion(() => Assert.Equal("Make it a work item", page.Find(".actionbar button.primary").TextContent.Trim()));
        Assert.NotEmpty(page.FindAll(".actionbar button.dep"));   // "Make it a dependency"
        await page.InvokeAsync(() => page.FindAll(".sub .s-t")[1].ClickAsync(new Microsoft.AspNetCore.Components.Web.MouseEventArgs { CtrlKey = true }));
        page.WaitForAssertion(() => Assert.Equal(2, page.FindAll(".sub.selected").Count));
        Assert.Equal("Move into a separate work item", page.Find(".actionbar button.primary").TextContent.Trim());
    }

    [Fact(DisplayName = "The main list counts every subtask in its progress line and lists the open ones; the done ones are folded")]
    public async Task SubtasksMainListCounts()
    {
        await using BunitContext ctx = NewContext();
        for (int i = WorkItemsApi.Get(123)!.Subtasks.Count; i < 25; i++) Assert.True(WorkItemsApi.AddSubtask(123, $"Extra step {i}").Success);
        IRenderedComponent<WorkItemPage> page = ctx.Render<WorkItemPage>(p => p.Add(c => c.Id, 123));

        View(page, "linked");

        Assert.Equal(23, page.FindAll(".tile[data-tile='subtasks'] .sub:not(.done)").Count);   // 25, 2 of them done
        Assert.Empty(page.FindAll(".tile[data-tile='subtasks'] .sub.done"));
        Assert.Contains("2 of 25 done", page.Find(".tile[data-tile='subtasks'] .tile-h").TextContent);
        Assert.Contains("width:8%", page.Find(".tile[data-tile='subtasks'] .ln-bar > div").GetAttribute("style")!.Replace(" ", ""));
    }

    [Fact(DisplayName = "The worklog view shows the item's notes in the feed and tells the page when one is added")]
    public async Task WorklogEntries()
    {
        await using BunitContext ctx = NewContext();
        (bool _, string _, int id) = NotesApi.Create(123, "Tom");
        Assert.True(NotesApi.SaveBody(id, "# Second entry\n\nSome **bold** words\n").Success);
        IRenderedComponent<WorkItemPage> page = ctx.Render<WorkItemPage>(p => p.Add(c => c.Id, 123));

        View(page, "worklog");
        List<IElement> entries = page.FindAll(".worklog .wk-note").ToList();

        Assert.Equal(2, entries.Count);   // newest first
        Assert.Contains("Second entry", entries[0].TextContent);
        Assert.Contains("Tom", entries[0].TextContent);
        Assert.Equal("bold", entries[0].QuerySelector("strong")!.TextContent);   // the body is rendered markdown
        Assert.Contains("Ping test", entries[1].TextContent);

        // the component alone: Add note in a date's menu creates a note and tells its page
        int told = 0;
        IRenderedComponent<Worklog> log = ctx.Render<Worklog>(p => p
            .Add(c => c.Item, WorkItemsApi.Get(123)!).Add(c => c.Root, repo.Root)
            .Add(c => c.OnChanged, Microsoft.AspNetCore.Components.EventCallback.Factory.Create(this, (Action)(() => told++))));
        log.FireWhenReady(".wk-day.today .wk-date", e => e.Click(new Microsoft.AspNetCore.Components.Web.MouseEventArgs { ShiftKey = true }));
        log.WaitForAssertion(() => Assert.NotEmpty(log.FindAll(".menu-pop")));
        log.FireWhenReady(".menu-pop .menu-item", e => e.Click(), all => all.Single(i => i.TextContent.Trim() == "Add note"));

        log.WaitForAssertion(() => Assert.Equal(1, told));
        log.WaitForAssertion(() => Assert.NotEmpty(log.FindAll(".wk-edit")));
    }

    [Fact(DisplayName = "In a narrow window the linked tasks rail drops below the main list and nothing is minimised")]
    public async Task NarrowStacks()
    {
        await using BunitContext ctx = NewContext();
        ctx.JSInterop.Setup<bool>("rtm.isNarrow").SetResult(true);
        IRenderedComponent<WorkItemPage> page = ctx.Render<WorkItemPage>(p => p.Add(c => c.Id, 123));

        Assert.NotEmpty(page.FindAll(".wk-first .desc"));   // the worklog is open at once, as on the desktop
        View(page, "linked");

        page.WaitForAssertion(() => Assert.Single(page.FindAll(".view-linked.stack")));
        Assert.Equal(["subtasks", "waiting", "blocks", "related"], Tiles(page));
        Assert.Equal(2, page.FindAll(".tile[data-tile='subtasks'] .sub:not(.done)").Count);   // the open subtasks are listed, not folded away
        Assert.Equal(["ln-main", "ln-rail"], page.Find(".view-linked").Children.Select(c => c.ClassList.First(x => x.StartsWith("ln-"))));   // the rail follows the main list, so it is below it once stacked
        Assert.Empty(page.FindAll(".tile-h [aria-expanded], button.tile-h"));
    }

    [Fact(DisplayName = "A work item has one feature, picked in one dropdown: another pick replaces it, \"no feature\" leaves it unassigned, and its spec stays")]
    public async Task OneFeatureDropdown()
    {
        await using BunitContext ctx = NewContext();
        IRenderedComponent<WorkItemPage> page = ctx.Render<WorkItemPage>(p => p.Add(c => c.Id, 123));
        Assert.Empty(page.FindAll(".head.kvs .chip.spec"));
        Assert.Contains("Security", page.Find(".head.kvs [aria-label='feature'] .rz-dropdown-label").TextContent);   // picked by hand, so it wins over its spec

        page.FireWhenReady("[aria-label='feature'] .rz-dropdown-item", e => e.Click(), all => all.Single(i => i.TextContent.Trim() == "Banking"));
        page.WaitForAssertion(() => Assert.Equal(["functional/banking"], WorkItemsApi.Get(123)!.Features.Select(f => f.Path)));

        page.FireWhenReady("[aria-label='feature'] .rz-dropdown-item", e => e.Click(), all => all.Single(i => i.TextContent.Trim() == "no feature"));
        page.WaitForAssertion(() => Assert.Empty(WorkItemsApi.Get(123)!.Features));
        Assert.True(File.Exists(repo.PathOf("openspec/changes/add-health-check/specs/non-functional/availability/spec.md")));
    }


    [Fact(DisplayName = "One dates control on the work item page picks a range: the first day clicked is the start, the second the due date; Clear removes both")]
    public async Task StartAndDueDates()
    {
        await using BunitContext ctx = NewContext();
        IRenderedComponent<WorkItemPage> page = ctx.Render<WorkItemPage>(p => p.Add(c => c.Id, 123));
        Assert.Empty(page.FindAll("input[type='date']"));   // no native pickers any more
        Assert.Contains("no dates", page.Find("button.dates-btn").TextContent);

        // A click returns before the handler has run when the dispatcher is busy, and a save reaches the disk before the page has rendered it:
        // so each step waits for the render it needs, and lets the dispatcher drain before the next event.
        async Task OpenPopup()
        {
            page.ClickWhenReady("button.dates-btn");
            page.WaitForAssertion(() => Assert.NotEmpty(page.FindAll(".range-pop")));
            await page.InvokeAsync(() => { });
        }
        async Task PickDay(DateOnly day)
        {
            page.ClickWhenReady($".range-pop button.rd[data-day='{day:yyyy-MM-dd}']");
            await page.InvokeAsync(() => { });
        }

        await OpenPopup();
        DateOnly first = new(DateTime.Today.Year, DateTime.Today.Month, 1);
        await PickDay(first);
        page.WaitForAssertion(() => Assert.Contains("pick the end", page.Find(".range-pop .range-f").TextContent));
        Assert.Null(WorkItemsApi.Get(123)!.Started);   // nothing saved until the range is complete
        await PickDay(first.AddDays(6));
        page.WaitForAssertion(() => Assert.Equal((first, first.AddDays(6)), (WorkItemsApi.Get(123)!.Started, WorkItemsApi.Get(123)!.Due)));
        page.WaitForAssertion(() => Assert.Contains(TaskManager.Helpers.Ui.Period(first, first.AddDays(6)), page.Find("button.dates-btn").TextContent));
        page.WaitForAssertion(() => Assert.Empty(page.FindAll(".range-pop")));   // closed once the range is complete
        await page.InvokeAsync(() => { });

        await OpenPopup();   // picking backwards swaps them
        await PickDay(first.AddDays(9));
        page.WaitForAssertion(() => Assert.Contains("pick the end", page.Find(".range-pop .range-f").TextContent));
        await PickDay(first.AddDays(2));
        page.WaitForAssertion(() => Assert.Equal((first.AddDays(2), first.AddDays(9)), (WorkItemsApi.Get(123)!.Started, WorkItemsApi.Get(123)!.Due)));
        page.WaitForAssertion(() => Assert.Empty(page.FindAll(".range-pop")));
        await page.InvokeAsync(() => { });

        await OpenPopup();
        page.ClickWhenReady(".range-pop button.range-clear");
        page.WaitForAssertion(() => Assert.Null(WorkItemsApi.Get(123)!.Due));
        Assert.Null(WorkItemsApi.Get(123)!.Started);
    }

    [Fact(DisplayName = "The date range picker offers its clear button only when a ClearLabel is given and a range is set; without a ClearLabel it keeps the plain Clear")]
    public async Task DateRangeClearLabel()
    {
        await using BunitContext ctx = NewContext();
        DateOnly first = new(2026, 11, 2), last = new(2026, 11, 8);
        (DateOnly? Start, DateOnly? End)? cleared = null;
        IRenderedComponent<TaskManager.Components.Shared.DateRange> Render(DateOnly? start, DateOnly? end, string? label) => ctx.Render<TaskManager.Components.Shared.DateRange>(p => p
            .Add(c => c.Start, start).Add(c => c.End, end).Add(c => c.ClearLabel, label)
            .Add(c => c.Changed, r => { cleared = r; }));
        async Task<IReadOnlyList<string>> Buttons(IRenderedComponent<TaskManager.Components.Shared.DateRange> range)
        {
            range.ClickWhenReady("button.dates-btn");
            range.WaitForAssertion(() => Assert.NotEmpty(range.FindAll(".range-pop")));
            await range.InvokeAsync(() => { });
            return [.. range.FindAll(".range-pop button.range-clear").Select(b => b.TextContent.Trim())];
        }

        Assert.Empty(await Buttons(Render(null, null, "Show all")));   // nothing chosen: nothing to show all of
        Assert.Equal(["Clear"], await Buttons(Render(null, null, null)));   // no label: the plain button as before
        IRenderedComponent<TaskManager.Components.Shared.DateRange> set = Render(first, last, "Show all");
        Assert.Equal(["Show all"], await Buttons(set));

        set.ClickWhenReady(".range-pop button.range-clear");
        set.WaitForAssertion(() => Assert.Equal((null, null), cleared));   // both ends cleared
        set.WaitForAssertion(() => Assert.Empty(set.FindAll(".range-pop")));   // and the popup closes
    }

    [Fact(DisplayName = "A work item's plan lives in the Related list, not in the header: picked there from the plans folder, it opens rendered in the app and its × unlinks it")]
    public async Task PlanOnThePage()
    {
        await using BunitContext ctx = NewContext();
        repo.Write("DevResources/plans/health-2026-11-05.md", "# Health plan\n\nStep one.\n");
        repo.Write("DevResources/plans/other-2026-11-04.md", "# Other\n");
        IRenderedComponent<WorkItemPage> page = ctx.Render<WorkItemPage>(p => p.Add(c => c.Id, 123));
        View(page, "linked");
        Assert.Empty(page.FindAll(".plan-field, a.plan-link, button.plan-x"));   // the header has no plan: it would say it twice
        Assert.Empty(page.FindAll("input[aria-label='plan']"));
        Assert.Empty(page.FindAll(".related li.rel[data-kind='plan']"));

        page.ClickWhenReady(".related [aria-label='Link a plan'] button, .related button[aria-label='Link a plan']");
        page.WaitForAssertion(() => Assert.Equal(2, page.FindAll(".menu-pop .pick-item").Count));
        page.FireWhenReady(".menu-pop .pick-item", e => e.Click(), all => all.First(i => i.TextContent.Contains("health-2026-11-05")));
        page.WaitForAssertion(() => Assert.Equal("DevResources/plans/health-2026-11-05.md", WorkItemsApi.Get(123)!.Plan));
        AngleSharp.Dom.IElement link = page.Find(".related li.rel[data-kind='plan'] a.rel-open");
        Assert.Equal("doc/DevResources/plans/health-2026-11-05.md", link.GetAttribute("href"));
        Assert.Equal("health-2026-11-05.md", link.TextContent.Trim());
        Assert.Empty(page.FindAll(".related [aria-label='Link a plan']"));   // one plan per item

        IRenderedComponent<DocPage> doc = ctx.Render<DocPage>(p => p.Add(c => c.Path, WorkItemsApi.Get(123)!.Plan!));
        Assert.Equal("Health plan", doc.Find("h1").TextContent);

        await page.InvokeAsync(() => { });
        page.ClickWhenReady(".related li.rel[data-kind='plan'] button.rel-x");
        page.WaitForAssertion(() => Assert.Null(WorkItemsApi.Get(123)!.Plan));
        page.WaitForAssertion(() => Assert.Empty(page.FindAll(".related li.rel[data-kind='plan']")));
    }

    [Fact(DisplayName = "Linked documents show in the Related list, where × unlinks one; what the description merely mentions has no ×")]
    public async Task LinkedDocsInRelated()
    {
        await using BunitContext ctx = NewContext();
        repo.Write("docs/guide.md", "# Guide\n");
        repo.Write("docs/runbook.md", "# Runbook\n");
        Assert.True(WorkItemsApi.SetLinkedDocs(123, ["docs/guide.md"]).Success);
        Assert.True(WorkItemsApi.SetDescription(123, "## Why\n\nSee [the runbook](docs/runbook.md).\n").Success);
        IRenderedComponent<WorkItemPage> page = ctx.Render<WorkItemPage>(p => p.Add(c => c.Id, 123));
        View(page, "linked");

        string[] Docs() => [.. page.FindAll(".related li.rel[data-kind='doc'] .rel-open").Select(a => a.TextContent.Trim())];
        Assert.Equal(["guide.md", "runbook.md"], Docs()[..2]);   // the stored link first, then what the description names (a note of the fixture names one more)
        Assert.Single(page.FindAll(".related li.rel button.rel-x"));   // only the stored link can be unlinked

        page.ClickWhenReady(".related li.rel button.rel-x");
        page.WaitForAssertion(() => Assert.Empty(WorkItemsApi.Get(123)!.Docs ?? []));
        page.WaitForAssertion(() => Assert.DoesNotContain("guide.md", Docs()));
        Assert.Contains("runbook.md", Docs());
    }

    [Fact(DisplayName = "The item page's views have no frames: the description, the Related list, the linked-tasks main list, rail and rows, and the worklog's notes sit straight on the page")]
    public void ViewsHaveNoFrames()
    {
        string css = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "wwwroot", "css", "app.css")).Replace("\r\n", "\n");
        string Rule(string selector) => System.Text.RegularExpressions.Regex.Matches(css, @"\n\s*" + System.Text.RegularExpressions.Regex.Escape(selector) + @"\s*\{([^}]*)\}").Last().Groups[1].Value;   // the last one wins the cascade

        string tile = Rule(".view-grid .tile");
        Assert.Matches(@"border:\s*0\b", tile);
        Assert.Matches(@"background:\s*transparent", tile);
        Assert.Matches(@"padding:\s*0\b", tile);
        string heading = Rule(".view-grid .tile-h h3");
        Assert.Matches(@"border:\s*0\b", heading);
        Assert.Matches(@"background:\s*transparent", heading);
        Assert.Matches(@"--stroke:\s*transparent", Rule(".view-grid .sub"));         // subtask and dependency rows: no box, a line between them
        Assert.Matches(@"--stroke:\s*transparent", Rule(".view-grid .sub-group"));
        Assert.Matches(@"border:\s*0\b", Rule(".view-grid .sub-fold"));              // the "N done" button is plain text
        Assert.Matches(@"--stroke:\s*transparent", Rule(".wk-note"));                // a note in the feed is not a card
        Assert.Matches(@"box-shadow:\s*none", Rule(".wk-edit"));
    }

    [Fact(DisplayName = "Opening a work item shows its header and description, its subtasks under Linked tasks, and its notes in the Worklog")]
    public async Task WorkItemPage()
    {
        await using BunitContext ctx = NewContext();
        IRenderedComponent<WorkItemPage> page = ctx.Render<WorkItemPage>(p => p.Add(c => c.Id, 123));

        Assert.Equal("Health-check endpoint", page.Find("input.title-in").GetAttribute("value"));
        Assert.Contains("In Progress", page.Find(".status-field").TextContent);
        Assert.Contains("Expose a lightweight /health endpoint", page.Find(".wk-first .desc").TextContent);
        View(page, "linked");
        Assert.Contains("2 of 4 done", page.Find(".tile[data-tile='subtasks'] .tile-h").TextContent);
        Assert.Equal(2, page.FindAll(".sub").Count);   // the two open ones; the done ones are folded
        View(page, "worklog");
        Assert.Contains("Ping test", page.Find(".worklog").TextContent);
        Assert.DoesNotContain(page.FindAll("h3"), h => h.TextContent.Trim() == "Notes");   // no separate notes panel
    }

    [Fact(DisplayName = "Every contributor can be removed, including one who has commits on the change")]
    public async Task GitContributors()
    {
        await using BunitContext ctx = NewContext();
        try
        {
            WorkItemsApi.ConfigureContributors(() => ("t1", folder => folder == "plain-change" ? ["Alice"] : []));
            IRenderedComponent<WorkItemPage> page = ctx.Render<WorkItemPage>(p => p.Add(c => c.Id, 125));
            AngleSharp.Dom.IElement alice = page.FindAll(".contributors .chip").Single();

            Assert.Equal("Alice", alice.TextContent.Trim());
            Assert.Contains("x", alice.ClassList);

            Act(page, () => alice, e => e.Click());
            page.WaitForAssertion(() => Assert.Empty(WorkItemsApi.Get(125)!.Contributors));
        }
        finally { WorkItemsApi.ConfigureContributors(null); }
    }

    [Fact(DisplayName = "+ add on contributors accepts a name typed in, not only people already on other items")]
    public async Task AddNewPerson()
    {
        await using BunitContext ctx = NewContext();
        IRenderedComponent<WorkItemPage> page = ctx.Render<WorkItemPage>(p => p.Add(c => c.Id, 107));

        page.ClickWhenReady("button[aria-label='Add a contributor']");
        page.InputWhenReady(".contributors .menu-filter", "Zed");
        page.FireWhenReady(".contributors .menu-item", e => e.Click(), all => all.Single(i => i.TextContent.Contains("Zed")));

        page.WaitForAssertion(() => Assert.Equal(["Tom", "Zed"], WorkItemsApi.Get(107)!.Contributors));
    }

    [Fact(DisplayName = "The Linked tasks view links a note-made subtask back to its note, and the click opens the worklog at that note")]
    public async Task LinkedTasksLinksBackToTheNote()
    {
        await using BunitContext ctx = NewContext();
        (bool ok, string message, string number) = NotesApi.ConvertToSubtask(123);
        Assert.True(ok, message);
        IRenderedComponent<WorkItemPage> page = ctx.Render<WorkItemPage>(p => p.Add(c => c.Id, 123));
        View(page, "linked");

        // the row shows the subtask's text without the reference, and "from #n123" in its place
        IElement row = SubText(page, "Ping test").ParentElement!;
        Assert.Equal(number, row.QuerySelector(".mono:last-child")!.TextContent.Trim());
        IElement from = row.QuerySelector("button.sub-from")!;
        Assert.Equal("from #n123", from.TextContent.Trim());
        Assert.DoesNotContain("#n123", SubText(page, "Ping test").TextContent);

        Act(page, () => from, e => e.Click());

        page.WaitForAssertion(() => Assert.Equal("worklog", CurrentView(page)));
        page.WaitForAssertion(() => Assert.Contains("flash", page.Find(".wk-e[data-note='123']").ClassList));   // the entry is marked, by a class: the fade is CSS
        page.WaitForAssertion(() => Assert.Equal(".wk-e[data-note='123']", ctx.JSInterop.Invocations["rtm.scrollToDay"].Single().Arguments[0]));
        Assert.Single(page.FindAll(".wk-e.flash"));

        // a reference to a note the item does not have is plain text: there is nothing to open
        Assert.True(WorkItemsApi.AddSubtask(123, "Orphan #n9999").Success);
        ctx.Services.GetRequiredService<Workspace>().Notify();   // the page reads again
        View(page, "linked");
        page.WaitForAssertion(() => Assert.Empty(SubText(page, "Orphan #n9999").ParentElement!.QuerySelectorAll("button.sub-from")));
    }

    [Fact(DisplayName = "A converted entry in the worklog links its subtask, and the click opens Linked tasks at that subtask")]
    public async Task WorklogLinksBackToTheSubtask()
    {
        await using BunitContext ctx = NewContext();
        (bool ok, string message, int bug) = NotesApi.ConvertSubtask(123, "1.3", Notes.PublicDTOs.NoteKinds.Bug, "Tom");
        Assert.True(ok, message);
        IRenderedComponent<WorkItemPage> page = ctx.Render<WorkItemPage>(p => p.Add(c => c.Id, 123));

        IElement link = page.FindAll(".wk-e[data-kind='converted'] button.wk-jump").Single();
        Assert.Equal("subtask 1.3", link.TextContent.Trim());
        Act(page, () => link, e => e.Click());

        page.WaitForAssertion(() => Assert.Equal("linked", CurrentView(page)));
        page.WaitForAssertion(() => Assert.Contains("flash", page.Find(".sub[data-sub='1.3']").ClassList));
        page.WaitForAssertion(() => Assert.Equal(".sub[data-sub='1.3']", ctx.JSInterop.Invocations["rtm.scrollToDay"].Single().Arguments[0]));
        Assert.Single(page.FindAll(".sub.flash"));
        Assert.True(NotesApi.Get(bug)!.IsBug);
    }

    [Fact(DisplayName = "Ticking a subtask on the work item page saves it to tasks.md")]
    public async Task TickOnPage()
    {
        await using BunitContext ctx = NewContext();
        IRenderedComponent<WorkItemPage> page = ctx.Render<WorkItemPage>(p => p.Add(c => c.Id, 123));
        View(page, "linked");
        page.ChangeWhenReady("#sub-2-1", true);
        page.WaitForAssertion(() => Assert.Contains("- [x] 2.1", repo.Read("openspec/changes/add-health-check/tasks.md")));   // the write runs on the thread pool, after the page drew
    }

    [Fact(DisplayName = "Clicking a subtask's text edits it in place; Enter saves it to tasks.md")]
    public async Task EditSubtaskText()
    {
        await using BunitContext ctx = NewContext();
        IRenderedComponent<WorkItemPage> page = ctx.Render<WorkItemPage>(p => p.Add(c => c.Id, 123));
        View(page, "linked");

        Act(page, () => SubText(page, "Hide internals from anonymous callers"), e => e.Click());
        AngleSharp.Dom.IElement box = page.WaitForElement(".sub input.sub-edit");
        Assert.Equal("Hide internals from anonymous callers", box.GetAttribute("value"));
        Act(page, () => box, e => e.Input("Hide internals from everyone"));
        Act(page, () => box, e => e.KeyDown("Enter"));

        page.WaitForAssertion(() =>
        {
            Assert.Contains("- [ ] 2.1 Hide internals from everyone", repo.Read("openspec/changes/add-health-check/tasks.md"));
            Assert.Empty(page.FindAll(".sub input.sub-edit"));
            Assert.Empty(page.FindAll(".actionbar"));   // a plain click edits; it does not select
        });
    }

    [Fact(DisplayName = "Esc while editing a subtask leaves it as it was")]
    public async Task EditSubtaskCancel()
    {
        await using BunitContext ctx = NewContext();
        IRenderedComponent<WorkItemPage> page = ctx.Render<WorkItemPage>(p => p.Add(c => c.Id, 123));
        View(page, "linked");
        string before = repo.Read("openspec/changes/add-health-check/tasks.md");

        Act(page, () => SubText(page, "Ping test in suite 543"), e => e.Click());
        page.InputWhenReady(".sub input.sub-edit", "Something else");
        page.KeyDownWhenReady(".sub input.sub-edit", "Escape");

        page.WaitForAssertion(() => Assert.Empty(page.FindAll(".sub input.sub-edit")));
        Assert.Equal(before, repo.Read("openspec/changes/add-health-check/tasks.md"));
    }
    [Fact(DisplayName = "Selecting two subtasks offers moving them into a separate work item")]
    public async Task SplitFromPage()
    {
        await using BunitContext ctx = NewContext();
        IRenderedComponent<WorkItemPage> page = ctx.Render<WorkItemPage>(p => p.Add(c => c.Id, 123));
        View(page, "linked");
        Act(page, () => SubText(page, "Ping test in suite 543"), e => e.Click(new Microsoft.AspNetCore.Components.Web.MouseEventArgs { ShiftKey = true }));
        Act(page, () => SubText(page, "Hide internals from anonymous callers"), e => e.Click(new Microsoft.AspNetCore.Components.Web.MouseEventArgs { ShiftKey = true }));

        page.ClickWhenReady(".actionbar button.primary");

        page.WaitForAssertion(() =>
        {
            Assert.Equal(2, WorkItemsApi.Get(123)!.Subtasks.Count);
            Assert.Contains(WorkItemsApi.GetAll(), w => w.Title == "Split from #wi123");
        });
    }

    [Fact(DisplayName = "A hidden item's page offers Restore; a visible item's page does not offer hiding")]
    public async Task HideFromPage()
    {
        await using BunitContext ctx = NewContext();
        IRenderedComponent<WorkItemPage> visible = ctx.Render<WorkItemPage>(p => p.Add(c => c.Id, 107));
        Assert.Empty(visible.FindAll("button.hide-item"));   // hiding only happens from the board's selection bar

        WorkItemsApi.Hide(107);
        IRenderedComponent<WorkItemPage> page = ctx.Render<WorkItemPage>(p => p.Add(c => c.Id, 107));
        page.ClickWhenReady("button.hide-item");
        page.WaitForAssertion(() => Assert.False(WorkItemsApi.Get(107)!.Hidden));
    }

    [Fact(DisplayName = "Add note in the worklog's date menu creates a note and opens it in an editor in the feed")]
    public async Task AddNoteOpensIt()
    {
        await using BunitContext ctx = NewContext();
        IRenderedComponent<WorkItemPage> page = ctx.Render<WorkItemPage>(p => p.Add(c => c.Id, 107));

        View(page, "worklog");
        page.FireWhenReady(".worklog .wk-day.today .wk-date", e => e.Click(new Microsoft.AspNetCore.Components.Web.MouseEventArgs { ShiftKey = true }));
        page.WaitForAssertion(() => Assert.NotEmpty(page.FindAll(".menu-pop")));
        page.FireWhenReady(".menu-pop .menu-item", e => e.Click(), all => all.Single(i => i.TextContent.Trim() == "Add note"));

        page.WaitForAssertion(() => Assert.NotEmpty(page.FindAll(".worklog .wk-edit")));
        Assert.Equal(124, NotesApi.GetForWorkItem(107).Single().Id);
        Assert.EndsWith("/", new Uri(ctx.Services.GetRequiredService<Microsoft.AspNetCore.Components.NavigationManager>().Uri).AbsolutePath);   // it did not navigate away
    }

    [Fact(DisplayName = "The worklog lists the newest day and note first, with its time and author")]
    public async Task ChangeLogNewestFirst()
    {
        await using BunitContext ctx = NewContext();
        (bool _, string _, int id) = NotesApi.Create(123, "Tom");
        IRenderedComponent<WorkItemPage> page = ctx.Render<WorkItemPage>(p => p.Add(c => c.Id, 123));

        View(page, "worklog");
        List<AngleSharp.Dom.IElement> entries = page.FindAll(".worklog .wk-note").ToList();

        Assert.Equal(2, entries.Count);
        Assert.Contains("Tom", entries[0].TextContent);
        Assert.Equal(DateTime.Today.ToString("yyyy-MM-dd"), entries[0].Closest(".wk-day")!.GetAttribute("data-day"));
        Assert.Contains("Ping test", entries[1].TextContent);
        Assert.Equal("09:14", entries[1].QuerySelector(".wk-time")!.TextContent.Trim());
    }

    [Fact(DisplayName = "The description shows formatted text in the worklog until you Shift-click it and choose Edit")]
    public async Task DescriptionRendered()
    {
        await using BunitContext ctx = NewContext();
        IRenderedComponent<WorkItemPage> page = ctx.Render<WorkItemPage>(p => p.Add(c => c.Id, 123));

        Assert.Equal("Why", page.Find(".desc h2").TextContent.Trim());
        Assert.DoesNotContain("## Why", page.Find(".desc").TextContent);
    }

    [Fact(DisplayName = "A completed (archived) work item has editable metadata but its status stays locked")]
    public async Task ArchivedReadOnly()
    {
        await using BunitContext ctx = NewContext();
        IRenderedComponent<WorkItemPage> page = ctx.Render<WorkItemPage>(p => p.Add(c => c.Id, 124));

        // Status stays read-only
        Assert.Contains("Completed", page.Find(".status-field").TextContent);
        Assert.Empty(page.FindAll(".status-field .dd"));
        // Other fields are editable: priority, label, feature and contributor dropdowns
        Assert.NotEmpty(page.FindAll(".dd"));
        Assert.NotEmpty(page.FindAll(".wk-first article"));   // the description entry still takes Shift-click Edit
    }

    [Fact(DisplayName = "On the work item page the agent is a contributor chip, lilac while unreviewed and the click that reviews it; the title row has no chip; subtasks and notes keep theirs")]
    public async Task AgentChipsOnThePage()
    {
        EditingWorkItemsTests.StampAgent(repo);
        repo.Write("openspec/changes/add-health-check/tasks.md", repo.Read("openspec/changes/add-health-check/tasks.md").TrimEnd() + "\n- [ ] 2.2 Agent drafted this <!-- agent: Claude -->\n");
        string note = "openspec/changes/add-health-check/notes/n123.md";
        repo.Write(note, new System.Text.RegularExpressions.Regex("\n---\n").Replace(repo.Read(note), "\nagentBy: Claude\nagentAt: 2026-10-30T09:00:00\n---\n", 1));
        await using BunitContext ctx = NewContext();
        IRenderedComponent<WorkItemPage> page = ctx.Render<WorkItemPage>(p => p.Add(c => c.Id, 123));

        Assert.Empty(page.FindAll(".head .agent-chip"));   // the title row carries no chip
        IElement chip = page.Find(".contributors .agent-contrib.unreviewed");
        Assert.Equal("Claude", chip.TextContent.Trim());
        Assert.Contains("Claude", chip.GetAttribute("title"));
        View(page, "linked");
        page.ClickWhenReady(".sub .agent-chip");
        page.WaitForAssertion(() => Assert.Empty(page.FindAll(".sub .agent-chip")));
        await page.InvokeAsync(() => { });
        View(page, "worklog");
        page.ClickWhenReady(".worklog .agent-chip");
        page.WaitForAssertion(() => Assert.Empty(page.FindAll(".worklog .agent-chip")));
        page.ClickWhenReady(".contributors .agent-contrib");
        page.WaitForAssertion(() => Assert.Empty(page.FindAll(".agent-contrib.unreviewed")));
        Assert.Empty(page.FindAll(".agent-chip"));
        Assert.Equal("Claude", page.Find(".contributors .agent-contrib").TextContent.Trim());   // stays, as a plain chip
        Assert.False(WorkItemsApi.Get(123)!.AgentUnreviewed);
    }

    [Fact(DisplayName = "An agent already listed among the contributors is not shown twice")]
    public async Task AgentNotShownTwice()
    {
        EditingWorkItemsTests.StampAgent(repo);
        string yaml = "openspec/changes/add-health-check/.openspec.yaml";
        repo.Write(yaml, repo.Read(yaml).Replace("contributors: [Artyom, Tom]", "contributors: [Claude, Tom]"));
        await using BunitContext ctx = NewContext();
        IRenderedComponent<WorkItemPage> page = ctx.Render<WorkItemPage>(p => p.Add(c => c.Id, 123));

        List<IElement> chips = page.FindAll(".contributors .chip").Where(c => c.TextContent.Trim() == "Claude").ToList();
        Assert.Single(chips);
        Assert.Contains("agent-contrib", chips[0].ClassList);
    }

    [Fact(DisplayName = "Saving anything on the work item page shows a Saved toast")]
    public async Task SavedToast()
    {
        await using BunitContext ctx = NewContext();
        IRenderedComponent<WorkItemPage> page = ctx.Render<WorkItemPage>(p => p.Add(c => c.Id, 123));

        page.ChangeWhenReady("input.title-in", "Health-check endpoint v2");

        overlays!.WaitForAssertion(() => Assert.Equal("Saved", overlays.Find(".toast.success").TextContent.Trim()));
        Assert.Equal("Health-check endpoint v2", WorkItemsApi.Get(123)!.Title);
    }

    // The note reads like a work item's description: rendered text; a click turns it into a plain text box, leaving it saves and renders again.
    private static Task Type(IRenderedComponent<NotePage> page, string text)
    {
        if (page.FindAll("textarea.desc-in").Count == 0) page.ClickWhenReady(".note-view");
        page.InputWhenReady("textarea.desc-in", text);
        return Task.CompletedTask;
    }

    [Fact(DisplayName = "The note page edits in place: the title is an input, clicking the note opens the editor, focusing out saves and shows the rendered note; a pasted image becomes an attachment")]
    public async Task NoteEditsInPlace()
    {
        await using BunitContext ctx = NewContext();
        IRenderedComponent<NotePage> page = ctx.Render<NotePage>(p => p.Add(c => c.Id, 123));

        page.ChangeWhenReady("input.note-title", "Decided on X");
        page.WaitForAssertion(() => Assert.Equal("Decided on X", NotesApi.Get(123)!.Title));

        Assert.Empty(page.FindAll("button.edit-note"));
        Assert.Empty(page.FindAll("button.done-note"));
        Assert.Empty(page.FindAll("textarea.desc-in"));

        page.ClickWhenReady(".note-view");
        page.WaitForAssertion(() => Assert.NotEmpty(page.FindAll("textarea.desc-in")));
        await Type(page, "# Decided on X\n\nchanged in test");
        page.BlurWhenReady("textarea.desc-in");

        page.WaitForAssertion(() =>
        {
            Assert.Empty(page.FindAll("textarea.desc-in"));
            Assert.Contains("changed in test", page.Find(".note-view").TextContent);
            Assert.Contains("changed in test", NotesApi.Get(123)!.Body);
            Assert.Contains("changed in test", repo.Read("openspec/changes/add-health-check/notes/n123.md"));
        });

        string png = Convert.ToBase64String([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
        await page.InvokeAsync(() => page.Instance.PastedImage("pasted-x.png", png));
        const string entry = "local/task-tracking/wi123/n123/pasted-x.png";
        Assert.EndsWith(entry, NotesApi.Get(123)!.Attachments[^1]);
        Assert.True(File.Exists(repo.PathOf(entry)));
    }

    [Fact(DisplayName = "Typing in the editor keeps every keystroke: the app binds the text, it never pushes an older copy back")]
    public async Task EditorKeepsTyping()
    {
        await using BunitContext ctx = NewContext();
        IRenderedComponent<NotePage> page = ctx.Render<NotePage>(p => p.Add(c => c.Id, 123));
        page.ClickWhenReady(".note-view");
        string loaded = page.WaitForElement("textarea.desc-in").GetAttribute("value") ?? "";

        await Type(page, loaded + "abc");
        await Type(page, loaded + "abcdef");

        Assert.Equal(loaded + "abcdef", page.Find("textarea.desc-in").GetAttribute("value"));
    }

    [Fact(DisplayName = "A note's own autosave doesn't reload the page under your cursor, and says Saved once it is on disk")]
    public async Task AutosaveDoesNotReload()
    {
        FakeTimeProvider time = new();
        await using BunitContext ctx = NewContext(time: time);
        int changes = 0;
        ctx.Services.GetRequiredService<Workspace>().Changed += () => changes++;
        IRenderedComponent<NotePage> page = ctx.Render<NotePage>(p => p.Add(c => c.Id, 123));

        await Type(page, "# Ping test\n\nfirst");
        time.Advance(TimeSpan.FromSeconds(1));   // typing stopped long enough: the autosave runs
        overlays!.WaitForAssertion(() => Assert.Contains("Saved", overlays.Find(".toast.success").TextContent));
        time.Advance(TimeSpan.FromSeconds(1));   // and anything the save scheduled on the page's clock has had its time
        await page.InvokeAsync(() => { });

        Assert.Equal(0, changes);
        Assert.Contains("first", repo.Read("openspec/changes/add-health-check/notes/n123.md"));
    }

    [Fact(DisplayName = "Edit in <editor> opens the note's file in the already-configured external editor")]
    public async Task EditInEditorConfigured()
    {
        string? launchedEditor = null, launchedPath = null;
        await using BunitContext ctx = NewContext((editor, path) => { launchedEditor = editor; launchedPath = path; return (true, ""); });
        ctx.Services.GetRequiredService<SettingsStore>().Update(s => s with { Editor = "code" });
        IRenderedComponent<NotePage> page = ctx.Render<NotePage>(p => p.Add(c => c.Id, 123));

        Assert.Equal("Edit in code", page.Find(".edit-ext").TextContent.Trim());
        page.ClickWhenReady(".edit-ext");

        page.WaitForAssertion(() =>
        {
            Assert.Equal("code", launchedEditor);
            Assert.Contains("n123.md", launchedPath);
        });
    }

    [Fact(DisplayName = "Edit in editor asks which editor to use the first time, then remembers it in settings")]
    public async Task EditInEditorAsksFirstTime()
    {
        string? launchedEditor = null;
        await using BunitContext ctx = NewContext((editor, path) => { launchedEditor = editor; return (true, ""); });
        IRenderedComponent<NotePage> page = ctx.Render<NotePage>(p => p.Add(c => c.Id, 123));

        Assert.Equal("Edit in…", page.Find(".edit-ext").TextContent.Trim());
        page.ClickWhenReady(".edit-ext");

        overlays!.WaitForAssertion(() => Assert.Contains("editor", overlays.Find(".dialog").TextContent, StringComparison.OrdinalIgnoreCase));
        overlays.InputWhenReady(".dialog input", "notepad++");
        overlays.ClickWhenReady(".dialog button.primary");

        overlays.WaitForAssertion(() => Assert.Equal("notepad++", launchedEditor));
        overlays.WaitForAssertion(() => Assert.Equal("notepad++", ctx.Services.GetRequiredService<SettingsStore>().Current.Editor));
    }

    [Fact(DisplayName = "Choosing Completed asks before archiving, and Cancel leaves the item as it was")]
    public async Task CompleteAsksFirst()
    {
        await using BunitContext ctx = NewContext();
        IRenderedComponent<WorkItemPage> page = ctx.Render<WorkItemPage>(p => p.Add(c => c.Id, 107));

        page.ClickWhenReady(".status-field .dd");
        page.FireWhenReady(".status-field .rz-dropdown-item", e => e.Click(), all => all.First(i => i.TextContent.Trim() == "Completed"));

        overlays!.WaitForAssertion(() => Assert.Contains("Complete and archive?", overlays.Find(".dialog").TextContent));
        overlays.FireWhenReady(".dialog button", e => e.Click(), all => all.Single(b => b.TextContent.Trim() == "Cancel"));

        overlays.WaitForAssertion(() => Assert.Empty(overlays.FindAll(".dialog")));
        Assert.Equal("Planned", WorkItemsApi.Get(107)!.Status);
    }

    [Fact(DisplayName = "Selecting one subtask offers to make it a work item of its own")]
    public async Task SplitOneSubtask()
    {
        await using BunitContext ctx = NewContext();
        IRenderedComponent<WorkItemPage> page = ctx.Render<WorkItemPage>(p => p.Add(c => c.Id, 123));

        View(page, "linked");
        Act(page, () => SubText(page, "Ping test in suite 543"), e => e.Click(new Microsoft.AspNetCore.Components.Web.MouseEventArgs { ShiftKey = true }));
        Assert.Equal("Make it a work item", page.Find(".actionbar button.primary").TextContent.Trim());
        page.ClickWhenReady(".actionbar button.primary");

        page.WaitForAssertion(() =>
        {
            Assert.Equal(3, WorkItemsApi.Get(123)!.Subtasks.Count);
            Assert.Contains(WorkItemsApi.GetAll(), w => w.Subtasks is [{ Number: "1.1", Text: "Ping test in suite 543" }]);
        });
    }

    [Fact(DisplayName = "Selecting a subtask also offers to make it a dependency, which blocks the parent and links the two pages")]
    public async Task SplitAsDependency()
    {
        await using BunitContext ctx = NewContext();
        IRenderedComponent<WorkItemPage> page = ctx.Render<WorkItemPage>(p => p.Add(c => c.Id, 123));

        View(page, "linked");
        Act(page, () => SubText(page, "Ping test in suite 543"), e => e.Click(new Microsoft.AspNetCore.Components.Web.MouseEventArgs { ShiftKey = true }));
        page.ClickWhenReady(".actionbar button.dep");

        int newId = 0;
        page.WaitForAssertion(() =>
        {
            Assert.Equal("Blocked", WorkItemsApi.Get(123)!.Status);
            newId = WorkItemsApi.GetAll().Single(w => w.Subtasks is [{ Number: "1.1", Text: "Ping test in suite 543" }]).Id;
            Assert.Contains($"#wi{newId}", page.Find(".sub.dep.waits.blocking-me").TextContent);
        });

        IRenderedComponent<WorkItemPage> dependency = ctx.Render<WorkItemPage>(p => p.Add(c => c.Id, newId));
        View(dependency, "linked");
        Assert.Contains("#wi123", dependency.Find(".sub.dep.blocks").TextContent);
    }

    [Theory(DisplayName = "Typing another item's id in Add a subtask makes it a dependency instead of a subtask")]
    [InlineData("#wi123")]
    [InlineData("wi123")]
    [InlineData("123")]
    public async Task DependencyFromSubtaskBox(string typed)
    {
        await using BunitContext ctx = NewContext();
        IRenderedComponent<WorkItemPage> page = ctx.Render<WorkItemPage>(p => p.Add(c => c.Id, 107));

        View(page, "linked");
        page.ChangeWhenReady("input[aria-label='New subtask']", typed);
        page.SubmitWhenReady("form.add-line");

        page.WaitForAssertion(() =>
        {
            Assert.Equal([123], WorkItemsApi.Get(107)!.DependsOn);
            Assert.Equal(2, WorkItemsApi.Get(107)!.Subtasks.Count);
            Assert.Contains("#wi123", page.Find(".sub.dep.waits.blocking-me").TextContent);
        });
    }

    [Fact(DisplayName = "The rail's Waiting on list has a picker of existing work items to wait on; choosing one makes it a dependency")]
    public async Task PickDependency()
    {
        await using BunitContext ctx = NewContext();
        IRenderedComponent<WorkItemPage> page = ctx.Render<WorkItemPage>(p => p.Add(c => c.Id, 107));
        View(page, "linked");

        page.ClickWhenReady("button[aria-label='Wait on another work item']");
        page.InputWhenReady(".menu-filter", "health");
        page.FireWhenReady(".menu-item", e => e.Click(), all => all.Single());

        page.WaitForAssertion(() =>
        {
            Assert.Equal([123], WorkItemsApi.Get(107)!.DependsOn);
            Assert.Equal("Blocked", WorkItemsApi.Get(107)!.Status);
            Assert.Contains("#wi123", page.Find(".sub.dep.waits").TextContent);
        });
    }

    [Fact(DisplayName = "The dependency picker leaves out the item itself, ones it already waits on and ones that wait on it")]
    public async Task DependencyPickerChoices()
    {
        await using BunitContext ctx = NewContext();
        WorkItemsApi.AddDependency(107, 123);   // 107 waits on 123
        IRenderedComponent<WorkItemPage> page = ctx.Render<WorkItemPage>(p => p.Add(c => c.Id, 123));
        View(page, "linked");

        page.ClickWhenReady("button[aria-label='Wait on another work item']");

        page.WaitForAssertion(() =>
        {
            string choices = string.Join(" | ", page.FindAll(".menu-item").Select(i => i.TextContent));
            Assert.DoesNotContain("#wi123", choices);   // itself
            Assert.DoesNotContain("#wi107", choices);   // it already waits on 123: that would be a loop
            Assert.Contains("#wi125", choices);
        });
    }

    [Fact(DisplayName = "Dependencies are listed in the rail's Waiting on list: a done one struck through, and × removes it")]
    public async Task DependenciesUnderSubtasks()
    {
        await using BunitContext ctx = NewContext();
        WorkItemsApi.AddDependency(107, 124);   // archived, so done
        IRenderedComponent<WorkItemPage> page = ctx.Render<WorkItemPage>(p => p.Add(c => c.Id, 107));

        View(page, "linked");
        AngleSharp.Dom.IElement row = page.Find(".tile[data-tile='waiting'] .sub.dep.waits");
        Assert.Contains("#wi124", row.TextContent);
        Assert.Contains("done", row.ClassList);

        Act(page, () => row.QuerySelector("button[aria-label='Remove dependency']")!, e => e.Click());

        page.WaitForAssertion(() =>
        {
            Assert.Empty(WorkItemsApi.Get(107)!.DependsOn!);
            Assert.Empty(page.FindAll(".sub.dep"));
        });
    }

    // Makes the temp repo a git repository with one commit, before the workspace opens it. (Unused on this page since the
    // commits and tests lists left it for the Worklog; kept for the tests that bring them back there.)
    private void GitInit(string message = "Fix uptime probe")
    {
        foreach (string[] args in new[]
        {
            new[] { "init", "-q" }, ["config", "user.name", "Tester"], ["config", "user.email", "t@example.com"], ["config", "commit.gpgsign", "false"],
            ["add", "-A"], ["commit", "-q", "-m", message],
        })
        {
            System.Diagnostics.ProcessStartInfo psi = new("git") { WorkingDirectory = repo.Root, RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (string a in args) psi.ArgumentList.Add(a);
            using System.Diagnostics.Process p = System.Diagnostics.Process.Start(psi)!;
            p.WaitForExit();
        }
    }

    // ── Note page ──

    [Fact(DisplayName = "Typing a [[doc]] link in a note adds it to the linked list straight away")]
    public async Task NoteLinksLive()
    {
        await using BunitContext ctx = NewContext();
        IRenderedComponent<NotePage> page = ctx.Render<NotePage>(p => p.Add(c => c.Id, 123));

        await Type(page, "# Ping test\n\nSee [[runbook/uptime]] and #wi999");

        page.WaitForAssertion(() =>
        {
            Assert.Contains("runbook/uptime", page.Find(".links").TextContent);
            Assert.Contains("unresolved", page.Find(".links").TextContent);
        });
    }

    [Fact(DisplayName = "A note is saved automatically shortly after typing stops")]
    public async Task NoteAutosave()
    {
        FakeTimeProvider time = new();
        await using BunitContext ctx = NewContext(time: time);
        IRenderedComponent<NotePage> page = ctx.Render<NotePage>(p => p.Add(c => c.Id, 123));
        await Type(page, "# Ping test\n\nAutosaved text.\n");
        Assert.DoesNotContain("Autosaved text.", repo.Read("openspec/changes/add-health-check/notes/n123.md"));   // not while typing

        time.Advance(TimeSpan.FromSeconds(1));   // typing stopped: the autosave's pause ends
        overlays!.WaitForAssertion(() => Assert.Contains("Saved", overlays.Find(".toast.success").TextContent));

        Assert.Contains("Autosaved text.", repo.Read("openspec/changes/add-health-check/notes/n123.md"));
        Assert.Equal("# Ping test\n\nAutosaved text.\n", NotesApi.Get(123)!.Body);
    }

    // ── Settings ──

    [Fact(DisplayName = "Choosing Dark in Settings saves the theme")]
    public async Task ThemeSetting()
    {
        await using BunitContext ctx = NewContext();
        IRenderedComponent<SettingsPage> page = ctx.Render<SettingsPage>();
        page.ChangeWhenReady("input[name=theme][value=dark]", "dark");
        page.WaitForAssertion(() => Assert.Equal("dark", ctx.Services.GetRequiredService<SettingsStore>().Current.Theme));
    }

    // ── Settings tabs (#wi160): General, then one tab per open project; every setting a row of a two-column table ──

    [Fact(DisplayName = "Settings has a General tab and one tab per open project, opens on General, and General holds what is not per project as table rows")]
    public async Task SettingsTabs_GeneralAndOnePerProject()
    {
        using TestRepo other = new();
        await using BunitContext ctx = NewContext();
        Assert.True(ctx.Services.GetRequiredService<Workspace>().OpenTab(other.Root).Success);
        IRenderedComponent<SettingsPage> page = ctx.Render<SettingsPage>();

        Assert.Equal(["General", Path.GetFileName(repo.Root), Path.GetFileName(other.Root)], page.Labels());
        Assert.Equal("general", page.Chosen());
        Assert.Equal(["general", repo.Root, other.Root], page.Radios().Select(r => r.GetAttribute("value")));
        Assert.Equal(["Your name", "Theme", "Keyboard hints", "External editor", "Open another repository", "User settings", "Logs folder", "Rebuild index", "Agents over MCP"],
            page.FindAll("table.set-table > tbody > tr > th").Select(h => h.TextContent.Trim()));   // label left, control right
        Assert.All(page.FindAll("table.set-table > tbody > tr"), r => Assert.Equal(2, r.Children.Length));
        Assert.Empty(page.FindAll("[data-project], .set-row"));   // nothing of a project, and no card per setting
        Assert.Single(page.FindAll(".settings-panel > p.muted"));   // one short line of help under the tab title
    }

    [Fact(DisplayName = "Choosing a project's tab shows that project's rows and hides General's and the other project's; the chosen tab is marked")]
    public async Task SettingsTabs_ChoosingAProject()
    {
        using TestRepo other = new();
        await using BunitContext ctx = NewContext();
        Assert.True(ctx.Services.GetRequiredService<Workspace>().OpenTab(other.Root).Success);
        IRenderedComponent<SettingsPage> page = ctx.Render<SettingsPage>();

        page.OnProject(1);

        Assert.Equal(other.Root, page.Chosen());
        Assert.Equal(Path.GetFileName(other.Root), page.Find("[data-project]").GetAttribute("data-project"));
        Assert.Single(page.FindAll("[data-project]"));
        Assert.Equal(["Location", "Plans", "Images", "Statuses"], page.FindAll("[data-project] table.set-table > tbody > tr > th").Select(h => h.TextContent.Trim()));   // no board storage is registered in this test
        Assert.Contains(other.Root, page.Find("[data-project] .set-path").TextContent);
        Assert.DoesNotContain("Theme", page.Markup);   // General's rows are not drawn
        Assert.Empty(page.FindAll("input#f-hints"));

        page.OnProject(0);
        Assert.Equal(Path.GetFileName(repo.Root), page.Find("[data-project]").GetAttribute("data-project"));
        Assert.Contains(repo.Root, page.Find("[data-project] .set-path").TextContent);

        page.OnTab("general");
        Assert.Contains("Keyboard hints", page.Markup);
        Assert.Empty(page.FindAll("[data-project]"));
    }

    [Fact(DisplayName = "The chosen Settings tab is remembered in the window: it is there when the user comes back, and General again when its project was closed")]
    public async Task SettingsTabs_RememberedInTheWindow()
    {
        using TestRepo other = new();
        await using BunitContext ctx = NewContext();
        Workspace workspace = ctx.Services.GetRequiredService<Workspace>();
        Assert.True(workspace.OpenTab(other.Root).Success);
        IRenderedComponent<SettingsPage> page = ctx.Render<SettingsPage>();
        page.OnProject(1);
        Assert.Equal(other.Root, ctx.Services.GetRequiredService<UiService>().SettingsTab);

        LeaveTo(ctx, "item/123");
        await ctx.DisposeComponentsAsync();   // the page is gone
        page = ctx.Render<SettingsPage>();
        Assert.Equal(other.Root, page.Chosen());   // back on the project's tab, not on General
        Assert.Equal(Path.GetFileName(other.Root), page.Find("[data-project]").GetAttribute("data-project"));

        workspace.CloseTab(other.Root);
        await ctx.DisposeComponentsAsync();
        page = ctx.Render<SettingsPage>();
        Assert.Equal("general", page.Chosen());   // the project is no longer open: General
        Assert.Equal(["General", Path.GetFileName(repo.Root)], page.Labels());
    }

    [Fact(DisplayName = "The settings page shows the .mcp.json snippet with the running app's path")]
    public async Task SettingsShowsMcpSnippet()
    {
        await using BunitContext ctx = NewContext();
        IRenderedComponent<SettingsPage> page = ctx.Render<SettingsPage>();

        string snippet = page.Find("tr.mcp pre.box").TextContent;
        Assert.Contains("--mcp", snippet);
        Assert.Contains("RTM_AGENT", snippet);
        Assert.Contains((Environment.ProcessPath ?? "").Replace('\\', '/'), snippet);
        Assert.Contains("claude mcp add repo-tasks", page.Find("tr.mcp").TextContent);
        Assert.Empty(page.FindAll(".mcp-status"));   // no status registered (the desktop host does that): no status line
    }

    [Fact(DisplayName = "The settings page says whether this app serves agents, with its port, or that another Repo Tasks window does")]
    public async Task SettingsShowsServerStatus()
    {
        TaskManager.Api.AgentEndpointStatus state = new(true, 7431, false);
        await using BunitContext ctx = NewContext(server: () => state);
        IRenderedComponent<SettingsPage> page = ctx.Render<SettingsPage>();

        Assert.Contains("Serving agents · port 7431", page.Find(".mcp-status").TextContent);
        Assert.Contains("on", page.Find(".mcp-status .chip").ClassList);
        Assert.Empty(page.FindAll(".mcp-status button"));   // nothing to start or stop: the app serves while it is open

        state = new(false, 7431, true);   // the status is read when the page opens
        Assert.Contains("Another Repo Tasks window serves agents", ctx.Render<SettingsPage>().Find(".mcp-status").TextContent);

        state = new(false, 7431, false, "Address already in use");
        IRenderedComponent<SettingsPage> broken = ctx.Render<SettingsPage>();
        Assert.Contains("Not serving agents", broken.Find(".mcp-status").TextContent);
        Assert.Contains("Address already in use", broken.Find("tr.mcp").TextContent);
    }

    [Fact(DisplayName = "Keyboard hints (F) are off until switched on in Settings; the switch is pushed to the page script")]
    public async Task HintsSetting()
    {
        await using BunitContext ctx = NewContext();
        Assert.False(ctx.Services.GetRequiredService<SettingsStore>().Current.Hints);
        IRenderedComponent<SettingsPage> page = ctx.Render<SettingsPage>();

        page.ChangeWhenReady("input#f-hints", true);

        page.WaitForAssertion(() => Assert.True(ctx.Services.GetRequiredService<SettingsStore>().Current.Hints));
        string js = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "wwwroot", "js", "app.js"));
        Assert.Contains("setHints(", js);           // the layout tells the script whether F does anything
        Assert.Contains("_hints.enabled", js);      // and F is ignored while it is off
    }

    [Fact(DisplayName = "Opening a folder without openspec/ in Settings sets it up and says so; a path that does not exist shows the error")]
    public async Task OpenWrongFolder()
    {
        await using BunitContext ctx = NewContext();
        IRenderedComponent<SettingsPage> page = ctx.Render<SettingsPage>();
        string empty = Directory.CreateTempSubdirectory("rtm-empty").FullName;
        try
        {
            page.ChangeWhenReady("input#f-repo", empty);
            page.ClickWhenReady("button.open-repo");

            page.WaitForAssertion(() => Assert.Contains("Set up openspec/", overlays!.Find(".toast").TextContent));
            Assert.True(File.Exists(Path.Combine(empty, "openspec", "config.yaml")));

            page.ChangeWhenReady("input#f-repo", Path.Combine(empty, "nope"));
            page.ClickWhenReady("button.open-repo");
            page.WaitForAssertion(() => Assert.Contains("nope", page.Find(".warning").TextContent));
        }
        finally { ctx.Services.GetRequiredService<Workspace>().Open(repo.Root); Directory.Delete(empty, true); }
    }

    // ── Doc page ──

    [Fact(DisplayName = "A plan's tables render as tables and its long lines wrap inside the window, on the doc page and anywhere markdown is shown")]
    public async Task DocPageRendersTables()
    {
        await using BunitContext ctx = NewContext();
        repo.Write("DevResources/plans/table-plan-2026-11-10.md",
            "# Plan\n\n## Chunks\n\n| # | Chunk | Owner |\n|---|-------|-------|\n| A | One thing | sonnet, worktree, CDP 9341 |\n| B | Another | main session |\n\nSee https://example.com/a-long-address for more.\n");
        IRenderedComponent<DocPage> page = ctx.Render<DocPage>(p => p.Add(c => c.Path, "DevResources/plans/table-plan-2026-11-10.md"));

        page.WaitForAssertion(() => Assert.Single(page.FindAll(".desc table")));
        Assert.Equal(["#", "Chunk", "Owner"], page.FindAll(".desc table th").Select(h => h.TextContent.Trim()));
        Assert.Equal(2, page.FindAll(".desc table tbody tr").Count);
        Assert.DoesNotContain("|---", page.Find(".desc").TextContent);
        Assert.Equal("https://example.com/a-long-address", page.Find(".desc a[href^='https://example.com']").GetAttribute("href"));   // a bare address is a link

        // a table scrolls on its own and long words break, so nothing pushes the page wider than the window
        string css = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "wwwroot", "css", "app.css")).Replace("\r\n", "\n");
        string Rule(string selector) => System.Text.RegularExpressions.Regex.Matches(css, @"\n\s*" + System.Text.RegularExpressions.Regex.Escape(selector) + @"\s*\{([^}]*)\}").Last().Groups[1].Value;
        Assert.Matches(@"overflow-wrap:\s*anywhere", Rule(".desc"));
        Assert.Matches(@"min-width:\s*0", Rule(".desc"));
        Assert.Matches(@"overflow-x:\s*auto", Rule(".desc table"));
    }

    [Fact(DisplayName = "An image a document links to shows on the doc page: a file beside the document or a path from the repository root, an .svg too")]
    public async Task DocPageShowsLinkedImages()
    {
        await using BunitContext ctx = NewContext();
        repo.Write("DevResources/plans/loop.excalidraw.svg", "<svg xmlns=\"http://www.w3.org/2000/svg\"/>");
        repo.Write("docs/shot.svg", "<svg xmlns=\"http://www.w3.org/2000/svg\"/>");
        repo.Write("DevResources/plans/pic-plan.md", "# Plan\n\n![loop](loop.excalidraw.svg)\n\n![shot](docs/shot.svg)\n\n![gone](missing.svg)\n");
        IRenderedComponent<DocPage> page = ctx.Render<DocPage>(p => p.Add(c => c.Path, "DevResources/plans/pic-plan.md"));

        page.WaitForAssertion(() => Assert.Equal(2, page.FindAll(".desc img").Count));   // only the files of the repository are images
        Assert.StartsWith("data:image/svg+xml;base64,", page.Find(".desc img[alt=loop]").GetAttribute("src"));   // beside the document
        Assert.StartsWith("data:image/svg+xml;base64,", page.Find(".desc img[alt=shot]").GetAttribute("src"));   // from the repository root
        Assert.Equal("gone", page.Find(".desc a[href='missing.svg']").TextContent);                                  // a missing file is a link with its text, never an image to fetch
    }

    [Fact(DisplayName = "A markdown file under the repository opens on the doc page, rendered, with its path shown; a missing file or a path outside the repository shows not found")]
    public async Task DocPageRendersMarkdown()
    {
        await using BunitContext ctx = NewContext();
        repo.Write("DevResources/plans/demo-plan-2026-11-05.md", "# Demo plan\n\n- **Status:** draft\n\nSome *text*.\n");

        IRenderedComponent<DocPage> page = ctx.Render<DocPage>(p => p.Add(c => c.Path, "DevResources/plans/demo-plan-2026-11-05.md"));
        Assert.Equal("Demo plan", page.Find("h1").TextContent);
        Assert.Contains("<em>text</em>", page.Markup);
        Assert.Contains("DevResources/plans/demo-plan-2026-11-05.md", page.Find(".head .mono").TextContent);

        IRenderedComponent<DocPage> missing = ctx.Render<DocPage>(p => p.Add(c => c.Path, "DevResources/plans/nope.md"));
        Assert.Contains("not found", missing.Find("h2").TextContent);
        IRenderedComponent<DocPage> outside = ctx.Render<DocPage>(p => p.Add(c => c.Path, "../outside.md"));
        Assert.Contains("not found", outside.Find("h2").TextContent);
    }

    [Fact(DisplayName = "The note page lists attachments: an image inline, a file by name, a link as a link, each removable; a link can be added from the page")]
    public async Task NoteAttachments()
    {
        await using BunitContext ctx = NewContext();
        string src = Directory.CreateTempSubdirectory("rtm-att").FullName;
        string png = Path.Combine(src, "pic.png"), txt = Path.Combine(src, "log.txt");
        File.WriteAllBytes(png, [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
        File.WriteAllText(txt, "hello");
        Assert.True(NotesApi.AttachFile(123, png).Success);
        Assert.True(NotesApi.AttachFile(123, txt).Success);
        Assert.True(NotesApi.AttachLink(123, "https://example.com/spec").Success);
        IRenderedComponent<NotePage> page = ctx.Render<NotePage>(p => p.Add(c => c.Id, 123));

        Assert.StartsWith("data:image/png;base64,", page.Find("img.att-img").GetAttribute("src"));
        Assert.Equal("log.txt", page.Find("button.att-file").TextContent.Trim());
        Assert.Equal("https://example.com/spec", page.Find("a.att-link").GetAttribute("href"));

        page.InputWhenReady("input.att-url", "https://example.com/more");
        page.FireWhenReady(".attachments button", e => e.Click(), all => all.First(b => b.TextContent.Trim() == "Add link"));
        page.WaitForAssertion(() => Assert.Contains("https://example.com/more", NotesApi.Get(123)!.Attachments));

        page.WaitForAssertion(() => Assert.Equal(4, page.FindAll("button.att-x").Count));
        page.FireWhenReady("button.att-x", e => e.Click(), all => all[0]);
        page.WaitForAssertion(() => Assert.Equal(3, NotesApi.Get(123)!.Attachments.Count));
        Directory.Delete(src, true);
    }

    [Fact(DisplayName = "A note is edited like a work item description: click the text for a plain text box, leave it and the note is saved and shown rendered")]
    public async Task NoteEditsLikeDescription()
    {
        await using BunitContext ctx = NewContext();
        IRenderedComponent<NotePage> page = ctx.Render<NotePage>(p => p.Add(c => c.Id, 123));
        Assert.Empty(page.FindAll(".rz-markdown-editor"));
        Assert.Contains("desc", page.Find(".note-view").ClassList);

        page.ClickWhenReady(".note-view");
        page.WaitForAssertion(() =>
        {
            Assert.Empty(page.FindAll(".note-view"));
            Assert.Empty(page.FindAll(".rz-markdown-editor"));
            Assert.Equal("Note", page.Find("textarea.desc-in").GetAttribute("aria-label"));
        });
        page.InputWhenReady("textarea.desc-in", "# Ping test\n\nplain **typed** text");
        page.BlurWhenReady("textarea.desc-in");

        page.WaitForAssertion(() =>
        {
            Assert.Empty(page.FindAll("textarea.desc-in"));
            Assert.Contains("<strong>typed</strong>", page.Find(".note-view").InnerHtml);
            Assert.Contains("plain **typed** text", repo.Read("openspec/changes/add-health-check/notes/n123.md"));
        });
    }

    [Fact(DisplayName = "An open work item sits beside the list: the layout renders the list on the left with the open item's row marked, the item on the right, and the split toggle hides the list and is remembered")]
    public async Task SplitBesideTheList()
    {
        await using BunitContext ctx = NewContext();
        Microsoft.AspNetCore.Components.NavigationManager nav = ctx.Services.GetRequiredService<Microsoft.AspNetCore.Components.NavigationManager>();
        SettingsStore settings = ctx.Services.GetRequiredService<SettingsStore>();
        Assert.True(settings.Current.Split);   // on unless switched off
        Assert.Equal("board", settings.Board(repo.Root).View);   // the board is not in list view, the embedded list is one anyway

        nav.NavigateTo("item/123");
        IRenderedComponent<MainLayout> layout = ctx.Render<MainLayout>(p => p.Add(l => l.Body, (Microsoft.AspNetCore.Components.RenderFragment)(b =>
        {
            b.OpenComponent<TaskManager.Components.Pages.WorkItemPage>(0);
            b.AddComponentParameter(1, "Id", 123);
            b.CloseComponent();
        })));

        layout.WaitForAssertion(() => Assert.Equal(4, layout.FindAll("main.split .split-list tr[data-id]").Count));   // the grid groups its rows after the first render
        string Open() => string.Join(",", layout.FindAll(".split-list tr[data-id]").Where(r => RowMarkProbe.Current(layout, r)).Select(r => r.GetAttribute("data-id")));   // the row RowMarks marks
        Assert.Equal("123", Open());
        Assert.Equal("Health-check endpoint", layout.Find(".split-item input.title-in").GetAttribute("value"));

        // the marked row follows the address
        nav.NavigateTo("item/107");
        layout.WaitForAssertion(() => Assert.Equal("107", Open()));

        // the split menu's "Item only" hides the list, and the choice outlives the app
        PickSplit(layout, "Item only");
        layout.WaitForAssertion(() =>   // the click returns before its handler and the draw it causes when the renderer is busy
        {
            Assert.Empty(layout.FindAll(".split-list"));
            Assert.Empty(layout.FindAll("main.split"));
            Assert.NotEmpty(layout.FindAll("main input.title-in"));   // the item is still there, alone
            Assert.False(settings.Current.Split);
            Assert.False(new SettingsStore(settingsFile).Current.Split);
        });

        PickSplit(layout, "Beside the list");
        layout.WaitForAssertion(() =>
        {
            Assert.NotEmpty(layout.FindAll(".split-list tr[data-id]"));
            Assert.True(new SettingsStore(settingsFile).Current.Split);
        });

        // away from an item (the board itself) there is no split
        nav.NavigateTo("/");
        layout.WaitForAssertion(() => Assert.Empty(layout.FindAll(".split-list")));
    }

    [Fact(DisplayName = "The note editor fills its tile and the worklog's description editor sits in the feed, both with no border of their own while editing")]
    public async Task EditorsFillTheirTile()
    {
        await using BunitContext ctx = NewContext();
        IRenderedComponent<WorkItemPage> item = ctx.Render<WorkItemPage>(p => p.Add(c => c.Id, 123));
        item.FireWhenReady(".wk-first .wk-e", e => e.Click(new Microsoft.AspNetCore.Components.Web.MouseEventArgs { ShiftKey = true }));
        item.FireWhenReady(".menu-pop .menu-item", e => e.Click(), all => all.Single(i => i.TextContent.Trim() == "Edit"));
        item.WaitForAssertion(() => Assert.NotEmpty(item.FindAll(".wk-first textarea.desc-in")));
        IRenderedComponent<NotePage> note = ctx.Render<NotePage>(p => p.Add(c => c.Id, 123));
        note.ClickWhenReady(".note-view");
        note.WaitForElement(".tile[data-tile='note'] > textarea.desc-in");

        foreach (var box in new[] { item.Find(".wk-first textarea.desc-in"), note.Find(".tile[data-tile='note'] > textarea.desc-in") })
        {
            Assert.Null(box.GetAttribute("rows"));   // no fixed row count: the tile sizes it
            Assert.Null(box.GetAttribute("style"));
        }

        string css = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "wwwroot", "css", "app.css")).Replace("\r\n", "\n");
        string rule = System.Text.RegularExpressions.Regex.Matches(css, @"\n\s*textarea\.desc-in\s*\{([^}]*)\}").Last().Groups[1].Value;   // the last one wins the cascade
        Assert.Matches(@"flex:\s*1 1 auto", rule);          // grows into what the tile has left
        Assert.Matches(@"min-height:\s*12rem", rule);
        Assert.Matches(@"resize:\s*none", rule);
        Assert.Matches(@"border:\s*0\b", rule);              // the tile is the frame
        Assert.Matches(@"outline:\s*none", rule);
        Assert.Matches(@"background:\s*transparent", rule);
        Assert.DoesNotMatch(@"[^-]height:\s*\d", rule);      // no fixed height
        Assert.Matches(@"\.note-grid \.tile\[data-tile=""note""\][^{]*\{[^}]*min-height", css);   // the note tile is tall enough to fill, even beside short tiles
    }

    [Fact(DisplayName = "In the main list each task group is a header with its progress; groups with open subtasks come first in file order, fully done groups after them")]
    public async Task MergedGroups()
    {
        await using BunitContext ctx = NewContext();
        WorkItemsApi.SetSubtaskDone(107, "1.2", true);   // 107's only group is then done: it goes last
        (bool ok, string message, int id) = WorkItemsApi.Merge([107, 123], 107);
        Assert.True(ok, message);
        IRenderedComponent<WorkItemPage> page = ctx.Render<WorkItemPage>(p => p.Add(c => c.Id, id));

        View(page, "linked");
        List<IElement> groups = page.FindAll(".tile[data-tile='subtasks'] .sub-group").ToList();
        Assert.Equal(3, groups.Count);
        Assert.Contains("#wi123 Health-check endpoint · Endpoint", groups[0].TextContent);
        Assert.Contains("2 / 3", groups[0].TextContent);   // done / total
        Assert.Contains("#wi123 Health-check endpoint · Hardening", groups[1].TextContent);
        Assert.Contains("0 / 1", groups[1].TextContent);
        Assert.Contains("Monthly remittance export", groups[2].TextContent);
        Assert.Contains("2 / 2", groups[2].TextContent);
        Assert.DoesNotContain("Export job", page.Find(".tile[data-tile='subtasks']").TextContent);   // folded
        Assert.Contains("Ping test in suite 543", page.Find(".tile[data-tile='subtasks']").TextContent);
        Assert.Contains("4 of 6 done", page.Find(".tile[data-tile='subtasks'] .tile-h").TextContent);

        page.ChangeWhenReady("input[aria-label='New subtask']", "Last one");   // adding still works, into the last group of the file
        page.SubmitWhenReady(".tile[data-tile='subtasks'] form.add-line");
        page.WaitForAssertion(() => Assert.Contains("Hardening", WorkItemsApi.Get(id)!.Subtasks.Single(s => s.Text == "Last one").Group));
    }

    // ── Scroll memory (#wi202): a view comes back where the user left it, per repository, kept in the window's own state ──

    private static object[] LastRestore(BunitContext ctx) => ctx.JSInterop.Invocations["rtm.restoreScroll"].Last().Arguments.Select(a => a!).ToArray();

    private static void LeaveTo(BunitContext ctx, string path) =>
        ctx.Services.GetRequiredService<Microsoft.AspNetCore.Components.NavigationManager>().NavigateTo(path);

    [Fact(DisplayName = "The board comes back at the scroll spot the user left it at, and the same for the settings page")]
    public async Task ViewsComeBackWhereTheyWereLeft()
    {
        await using BunitContext ctx = NewContext();
        UiService ui = ctx.Services.GetRequiredService<UiService>();
        ctx.JSInterop.Setup<double[]>("rtm.scrollSpot").SetResult([1200, 30]);

        IRenderedComponent<BoardPage> board = ctx.Render<BoardPage>();
        Assert.Empty(ctx.JSInterop.Invocations.Where(i => i.Identifier == "rtm.restoreScroll"));   // nothing was left yet: the first visit starts at the top
        LeaveTo(ctx, "item/123");
        board.WaitForAssertion(() => Assert.Equal((1200d, 30d), ui.ScrollSpots[(SettingsStore.Key(repo.Root), "board")]));

        await ctx.DisposeComponentsAsync();   // the page is gone, the layout shows the item
        ctx.Render<BoardPage>();
        ctx.Render<TaskManager.Components.Pages.SettingsPage>();   // another view has its own spot: nothing was saved for it, so it is not moved
        Assert.Equal([1200d, 30d], LastRestore(ctx));
        Assert.Single(ctx.JSInterop.Invocations["rtm.restoreScroll"]);

        LeaveTo(ctx, "settings");
        await ctx.DisposeComponentsAsync();
        Assert.True(ui.ScrollSpots.ContainsKey((SettingsStore.Key(repo.Root), "settings")));
        ctx.Render<TaskManager.Components.Pages.SettingsPage>();
        Assert.Equal(2, ctx.JSInterop.Invocations["rtm.restoreScroll"].Count);
    }

    [Fact(DisplayName = "Each repository keeps its own scroll spot: switching to one restores where that one was left")]
    public async Task EachRepositoryKeepsItsOwnSpot()
    {
        using TestRepo other = new();
        await using BunitContext ctx = NewContext();
        UiService ui = ctx.Services.GetRequiredService<UiService>();
        Workspace workspace = ctx.Services.GetRequiredService<Workspace>();
        ui.ScrollSpots[(SettingsStore.Key(repo.Root), "board")] = (1200, 0);
        ui.ScrollSpots[(SettingsStore.Key(other.Root), "board")] = (300, 0);

        IRenderedComponent<BoardPage> board = ctx.Render<BoardPage>();
        board.WaitForAssertion(() => Assert.Equal([1200d, 0d], LastRestore(ctx)));
        Assert.True(workspace.Open(other.Root).Success);
        board.WaitForAssertion(() => Assert.Equal([300d, 0d], LastRestore(ctx)));
        Assert.True(workspace.Open(repo.Root).Success);
        board.WaitForAssertion(() => { Assert.Equal(3, ctx.JSInterop.Invocations["rtm.restoreScroll"].Count); Assert.Equal([1200d, 0d], LastRestore(ctx)); });
    }

    public void Dispose()
    {
        repo.Dispose();
        File.Delete(settingsFile);
    }

    [Fact(DisplayName = "The work item page shows 'in <status> since' with the date the item last moved to that status")]
    public async Task InStatusSince()
    {
        await using BunitContext ctx = NewContext();
        IRenderedComponent<WorkItemPage> page = ctx.Render<WorkItemPage>(p => p.Add(c => c.Id, 123));

        Assert.Equal("in In Progress since", page.Find(".moved-field label").TextContent);
        Assert.Equal(new DateOnly(2026, 9, 12).ToString("d MMM"), page.Find(".moved-field .chip").TextContent.Trim());   // no x-rtm.moved: the creation date

        DateOnly today = DateOnly.FromDateTime(DateTime.Today);
        Assert.True(WorkItemsApi.SetStatus(123, "In Review").Success);
        page = ctx.Render<WorkItemPage>(p => p.Add(c => c.Id, 123));
        Assert.Equal("in In Review since", page.Find(".moved-field label").TextContent);
        Assert.Equal(today.ToString("d MMM"), page.Find(".moved-field .chip").TextContent.Trim());
    }

    // ── Split menu and the separate window ──

    private static IRenderedComponent<MainLayout> RenderItemLayout(BunitContext ctx, int id) =>
        ctx.Render<MainLayout>(p => p.Add(l => l.Body, (Microsoft.AspNetCore.Components.RenderFragment)(b =>
        {
            b.OpenComponent<WorkItemPage>(0);
            b.AddComponentParameter(1, "Id", id);
            b.CloseComponent();
        })));

    private static List<string> SplitChoices(IRenderedComponent<MainLayout> layout) =>
        layout.FindAll(".menu-pop .menu-item").Select(i => i.TextContent.Trim()).ToList();

    /// <summary>Opens the split menu and picks one entry by its text.</summary>
    private static void PickSplit(IRenderedComponent<MainLayout> layout, string choice)
    {
        layout.InvokeAsync(() => { }).GetAwaiter().GetResult();   // let what is queued on the renderer run first: an event dispatched into a busy renderer returns before it is handled
        layout.ClickWhenReady("button.split-btn");
        layout.FireWhenReady(".menu-pop .menu-item", e => e.Click(), all => all.Single(i => i.TextContent.Trim() == choice));   // the menu is drawn after the click, not with it
    }

    [Fact(DisplayName = "The split button is a menu with Beside the list, Item only and Separate window; Separate window asks the launcher for a window on this item and the main window goes back to the board")]
    public async Task SplitMenuWithSeparateWindow()
    {
        List<(string Root, int Id)> launched = [];
        await using BunitContext ctx = NewContext(windows: (root, id) => { launched.Add((root, id)); return (true, ""); });
        Microsoft.AspNetCore.Components.NavigationManager nav = ctx.Services.GetRequiredService<Microsoft.AspNetCore.Components.NavigationManager>();
        SettingsStore settings = ctx.Services.GetRequiredService<SettingsStore>();
        nav.NavigateTo("item/123");
        IRenderedComponent<MainLayout> layout = RenderItemLayout(ctx, 123);
        layout.WaitForAssertion(() => Assert.NotEmpty(layout.FindAll("main.split .split-list tr[data-id]")));

        // the three choices; the current one is marked (beside the list is on until switched off)
        layout.ClickWhenReady("button.split-btn");
        layout.WaitForAssertion(() =>
        {
            Assert.Equal(["Beside the list", "Item only", "Separate window"], SplitChoices(layout));
            Assert.Equal(["Beside the list"], layout.FindAll(".menu-pop .menu-item.cur").Select(i => i.TextContent.Trim()));
            Assert.Contains("wide-only", layout.FindAll(".menu-pop .menu-item")[0].ClassList);   // the CSS drops it in a narrow window; the other two stay
        });

        layout.FireWhenReady(".menu-pop .menu-item", e => e.Click(), all => all[1]);   // Item only
        layout.WaitForAssertion(() =>
        {
            Assert.False(settings.Current.Split);
            Assert.Empty(layout.FindAll("main.split"));
        });
        layout.ClickWhenReady("button.split-btn");
        layout.WaitForAssertion(() => Assert.Equal(["Item only"], layout.FindAll(".menu-pop .menu-item.cur").Select(i => i.TextContent.Trim())));
        layout.FireWhenReady(".menu-pop .menu-item", e => e.Click(), all => all[0]);   // Beside the list
        layout.WaitForAssertion(() =>
        {
            Assert.True(settings.Current.Split);
            Assert.NotEmpty(layout.FindAll("main.split .split-list tr[data-id]"));
        });
        Assert.Empty(launched);

        // Separate window: the launcher gets the repository and the item, the split choice is left alone, the main window goes to the board
        PickSplit(layout, "Separate window");
        Assert.Equal([(repo.Root, 123)], launched);
        Assert.True(settings.Current.Split);
        layout.WaitForAssertion(() => Assert.Equal("", nav.ToBaseRelativePath(nav.Uri)));
        layout.WaitForAssertion(() => Assert.Empty(layout.FindAll("main.split")));
    }

    [Fact(DisplayName = "A separate window shows only the work item page, without the side menu, the split menu or the back button")]
    public async Task DetachedWindow()
    {
        AppMode mode = new() { Detached = true };
        await using BunitContext ctx = NewContext(mode: mode);
        ctx.Services.GetRequiredService<Microsoft.AspNetCore.Components.NavigationManager>().NavigateTo("item/123");

        IRenderedComponent<MainLayout> layout = RenderItemLayout(ctx, 123);

        Assert.Equal("Health-check endpoint", layout.Find("main input.title-in").GetAttribute("value"));
        Assert.Empty(layout.FindAll("nav.side"));
        Assert.Empty(layout.FindAll("main.split"));
        Assert.Empty(layout.FindAll(".split-list"));
        Assert.Empty(layout.FindAll("button.split-btn"));
        Assert.Empty(layout.FindAll(".sheet-close"));
    }

    [Fact(DisplayName = "The item page's feature list ends with New feature…, which creates a feature and sets it on the item")]
    public async Task NewFeatureFromTheItemPage()
    {
        await using BunitContext ctx = NewContext();
        IRenderedComponent<WorkItemPage> page = ctx.Render<WorkItemPage>(p => p.Add(c => c.Id, 123));
        Assert.Equal("+ New feature…", page.FindAll("[aria-label='feature'] .rz-dropdown-item").Last().TextContent.Trim());

        // cancelling leaves the item as it was, and the dropdown still shows its feature
        page.FireWhenReady("[aria-label='feature'] .rz-dropdown-item", e => e.Click(), all => all.Last());
        overlays!.WaitForAssertion(() => Assert.Contains("New feature", overlays.Find(".dialog").TextContent));
        overlays.FireWhenReady(".dialog button", e => e.Click(), all => all.Single(b => b.TextContent.Trim() == "Cancel"));
        overlays.WaitForAssertion(() => Assert.Empty(overlays.FindAll(".dialog")));
        Assert.Equal(["non-functional/security"], WorkItemsApi.Get(123)!.Features.Select(f => f.Path));
        page.WaitForAssertion(() => Assert.Contains("Security", page.Find(".head.kvs [aria-label='feature'] .rz-dropdown-label").TextContent));

        page.FireWhenReady("[aria-label='feature'] .rz-dropdown-item", e => e.Click(), all => all.Last());
        overlays.InputWhenReady(".dialog input", "Exports");
        overlays.FireWhenReady(".dialog button", e => e.Click(), all => all.Single(b => b.TextContent.Trim() == "Create"));

        page.WaitForAssertion(() => Assert.EndsWith("exports", WorkItemsApi.Get(123)!.Features.Single().Path));
        page.WaitForAssertion(() => Assert.Contains("Exports", page.Find(".head.kvs [aria-label='feature'] .rz-dropdown-label").TextContent));
        Assert.Contains("exports", File.ReadAllText(repo.PathOf("openspec/config.yaml")));   // listed there until OpenSpec creates its spec
    }
    [Fact(DisplayName = "Pasting an image into the worklog's description editor inserts a link to the saved file at the caret, and the description entry renders the image")]
    public async Task DescriptionImagePaste()
    {
        await using BunitContext ctx = NewContext();
        IRenderedComponent<WorkItemPage> page = ctx.Render<WorkItemPage>(p => p.Add(c => c.Id, 123));
        IRenderedComponent<Worklog> log = page.FindComponent<Worklog>();
        void Edit()
        {
            page.FireWhenReady(".wk-first .wk-e", e => e.Click(new Microsoft.AspNetCore.Components.Web.MouseEventArgs { ShiftKey = true }));
            page.FireWhenReady(".menu-pop .menu-item", e => e.Click(), all => all.Single(i => i.TextContent.Trim() == "Edit"));
            page.WaitForAssertion(() => Assert.NotEmpty(page.FindAll(".wk-first textarea.desc-in")));
        }
        Edit();
        Assert.NotEmpty(page.FindAll("textarea.desc-in[data-paste-img]"));   // the paste script listens to this box only

        string png = Convert.ToBase64String([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
        const string link = "![](local/task-tracking/wi123/pasted-x.png)";
        await page.InvokeAsync(() => log.Instance.PastedDescriptionImage("pasted-x.png", png, "line one\nline two", 8));   // caret at the end of line one

        Assert.True(File.Exists(repo.PathOf("local/task-tracking/wi123/pasted-x.png")));
        page.WaitForAssertion(() => Assert.Equal($"line one\n{link}\nline two", page.Find("textarea.desc-in").GetAttribute("value")));   // on its own line, at the caret
        Assert.Contains(link, WorkItemsApi.GetDescription(123));   // saved at once: the file is not left unreferenced
        Assert.Empty(NotesApi.GetForWorkItem(123).SelectMany(n => n.Attachments).Where(a => a.Contains("pasted-x")));   // no note

        page.Find(".wk-first .wk-edit textarea").FocusOut();

        page.WaitForAssertion(() => Assert.Empty(page.FindAll("textarea.desc-in")));
        string src = page.Find(".wk-first .desc img").GetAttribute("src")!;
        Assert.StartsWith("data:image/png;base64," + png[..8], src);   // the WebView can load it
        Assert.Contains("line two", page.Find(".wk-first .desc").TextContent);

        Edit();
        await page.InvokeAsync(() => log.Instance.PastedDescriptionImage("pasted-x.png", png, "", 0));   // the same name again: never overwritten
        Assert.True(File.Exists(repo.PathOf("local/task-tracking/wi123/pasted-x-2.png")));
    }

    [Fact(DisplayName = "A pasted description image goes to the repository the work item lives in, not to whichever project is active")]
    public void DescriptionImageRoot()
    {
        string a = Path.Combine(Path.GetTempPath(), "repo-a"), b = Path.Combine(Path.GetTempPath(), "repo-b");
        string item = Path.Combine(b, "openspec", "changes", "export");

        Assert.Equal(b, TaskManager.Components.Pages.WorkItemPage.RootOf(item, [a, b], active: a));   // the item's own repository wins over the active one
        Assert.Equal(a, TaskManager.Components.Pages.WorkItemPage.RootOf(Path.Combine(Path.GetTempPath(), "elsewhere", "x"), [a, b], active: a));   // unknown: the active one
        Assert.Equal(b, TaskManager.Components.Pages.WorkItemPage.RootOf(Path.Combine(b + "-other", "openspec", "changes", "x"), [b], active: b));   // a sibling folder with a longer name is not inside b
    }

    [Fact(DisplayName = "The calendar, the work item page and the note page have no outer frame, like the list")]
    public async Task NoOuterFrame()
    {
        string css = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "wwwroot", "css", "app.css")).Replace("\r\n", "\n");
        string Rule(string selector) => System.Text.RegularExpressions.Regex.Matches(css, @"\n\s*" + System.Text.RegularExpressions.Regex.Escape(selector) + @"\s*\{([^}]*)\}").Last().Groups[1].Value;   // the last one wins the cascade

        // the item, note, doc and settings pages: the sheet loses the [data-sk] outline, radius, fill and shadow
        string sheet = Rule(@".sheet[data-sk=""soft""]");
        Assert.Matches(@"border:\s*0\b", sheet);
        Assert.Matches(@"border-radius:\s*0\b", sheet);
        Assert.Matches(@"box-shadow:\s*none", sheet);
        Assert.Matches(@"background:\s*transparent", sheet);

        // the calendar: neither the roadmap nor its scroll pane is framed, and the extra padding goes
        string rm = Rule(".rm");
        Assert.Matches(@"border:\s*0\b", rm);
        Assert.Matches(@"box-shadow:\s*none", rm);
        Assert.DoesNotMatch(@"padding:\s*12px 16px", rm);
        string scroll = Rule(".rm-scroll");
        Assert.Matches(@"border:\s*0\b", scroll);
        Assert.Matches(@"border-radius:\s*0\b", scroll);
        Assert.Matches(@"background:\s*transparent", scroll);

        // the markup still carries the class the rule is for
        await using BunitContext ctx = NewContext();
        Assert.Contains("sheet", ctx.Render<WorkItemPage>(p => p.Add(c => c.Id, 123)).Find("div[data-sk=soft]").ClassList);
        Assert.Contains("sheet", ctx.Render<NotePage>(p => p.Add(c => c.Id, 123)).Find("div[data-sk=soft]").ClassList);
    }

    [Fact(DisplayName = "In a window too narrow for the split screen the list beside an open item is not rendered, and it comes back when the window widens")]
    public async Task NarrowWindowSkipsTheList()
    {
        await using BunitContext ctx = NewContext();
        ctx.JSInterop.Setup<bool>("rtm.watchNarrow", _ => true).SetResult(true);   // the window is under the split breakpoint
        ctx.Services.GetRequiredService<Microsoft.AspNetCore.Components.NavigationManager>().NavigateTo("item/123");
        IRenderedComponent<MainLayout> layout = RenderItemLayout(ctx, 123);

        layout.WaitForAssertion(() => Assert.Empty(layout.FindAll(".split-list")));
        Assert.NotEmpty(layout.FindAll(".split-item .sheet"));   // the item is there; only the list the CSS would hide is not built

        await layout.InvokeAsync(() => layout.Instance.NarrowChanged(false));   // the window widens
        layout.WaitForAssertion(() => Assert.NotEmpty(layout.FindAll("main.split .split-list tr[data-id]")));
        Assert.NotEmpty(layout.FindAll(".split-item .sheet"));
    }

    // ── Loading once ──

    [Fact(DisplayName = "Opening a work item loads its data once, and a parent re-render does not make the worklog load again")]
    public async Task OpeningLoadsOnce()
    {
        await using BunitContext ctx = NewContext();
        IRenderedComponent<WorkItemPage> page = ctx.Render<WorkItemPage>(p => p.Add(c => c.Id, 123));
        Assert.Equal(1, page.Instance.Loads);

        View(page, "worklog");
        IRenderedComponent<Worklog> log = page.FindComponent<Worklog>();
        Assert.Equal(1, page.Instance.Loads);
        Assert.Equal(1, log.Instance.Loads);

        // the page rendering again (its own state, or the parameters it already had) is not a reason to read again, here or in the worklog
        page.Render();
        page.Render(p => p.Add(c => c.Id, 123));
        Assert.Equal(1, page.Instance.Loads);
        Assert.Equal(1, log.Instance.Loads);

        // another item is a new navigation: it loads again
        page.Render(p => p.Add(c => c.Id, 107));
        Assert.Equal(2, page.Instance.Loads);
    }

    [Fact(DisplayName = "Switching the item page's view does not re-read the repository")]
    public async Task SwitchingViewsDoesNotReload()
    {
        await using BunitContext ctx = NewContext();
        IRenderedComponent<WorkItemPage> page = ctx.Render<WorkItemPage>(p => p.Add(c => c.Id, 123));
        int before = page.Instance.Loads;

        View(page, "linked");
        View(page, "worklog");
        View(page, "linked");
        View(page, "worklog");

        Assert.Equal(before, page.Instance.Loads);
    }

    // ── linked tasks: main list and rail ──

    [Fact(DisplayName = "The linked tasks view shows the subtasks as the main list with their progress, open first, done ones folded per group, and the rail beside it with waiting on, waiting on this and related")]
    public async Task LinkedMainListAndRail()
    {
        await using BunitContext ctx = NewContext();
        WorkItemsApi.SetSubtaskDone(107, "1.2", true);
        (bool ok, string message, int id) = WorkItemsApi.Merge([107, 123], 107);
        Assert.True(ok, message);
        Assert.True(WorkItemsApi.AddDependency(id, 124).Success);   // waits on 124
        Assert.True(WorkItemsApi.AddDependency(125, id).Success);   // 125 waits on it
        IRenderedComponent<WorkItemPage> page = ctx.Render<WorkItemPage>(p => p.Add(c => c.Id, id));

        View(page, "linked");

        // two thirds main list, one third rail, side by side
        Assert.Equal(["ln-main", "ln-rail"], page.Find(".view-linked").Children.Select(c => c.ClassList.First(x => x.StartsWith("ln-"))));
        IElement main = page.Find(".ln-main");
        Assert.Equal("subtasks", main.GetAttribute("data-tile"));
        Assert.Equal("Subtasks", main.QuerySelector(".tile-h h3")!.TextContent.Trim());
        Assert.Equal("4 of 6 done", main.QuerySelector(".tile-h .count")!.TextContent.Trim());
        Assert.Contains("width:67%", main.QuerySelector(".ln-bar > div")!.GetAttribute("style")!.Replace(" ", ""));   // the progress bar, 4 of 6

        // groups: the ones with open subtasks first (file order), the done one last; each with its open rows (the number at the end), then "N done"
        List<IElement> groups = main.QuerySelectorAll(".sub-grp").ToList();
        Assert.Equal(3, groups.Count);
        Assert.Equal(["Ping test in suite 543"], groups[0].QuerySelectorAll(".sub .s-t").Select(t => t.TextContent.Trim()));
        Assert.Equal("2.3", groups[0].QuerySelector(".sub .mono")!.TextContent.Trim());
        Assert.Equal("▸ 2 done", groups[0].QuerySelector("button.sub-fold")!.TextContent.Trim());
        Assert.Equal(["Hide internals from anonymous callers"], groups[1].QuerySelectorAll(".sub .s-t").Select(t => t.TextContent.Trim()));
        Assert.Empty(groups[1].QuerySelectorAll("button.sub-fold"));   // nothing done in it: no fold
        Assert.Empty(groups[2].QuerySelectorAll(".sub"));              // all done: only the fold
        Assert.Equal("▸ 2 done", groups[2].QuerySelector("button.sub-fold")!.TextContent.Trim());
        Assert.Equal("false", groups[2].QuerySelector("button.sub-fold")!.GetAttribute("aria-expanded"));
        Assert.Empty(main.QuerySelectorAll(".sub.done"));

        // the add line closes the list
        Assert.Equal("Add a subtask, or #wi12 to wait on it", main.QuerySelector("form.add-line input")!.GetAttribute("placeholder"));
        Assert.Same(main.QuerySelector("form.add-line"), main.Children[^1]);

        // the rail: waiting on (with its picker), waiting on this, related
        IElement rail = page.Find(".ln-rail");
        Assert.Equal(["Waiting on", "Waiting on this", "Related"], rail.QuerySelectorAll(".tile-h h3").Select(h => h.TextContent.Trim()));
        Assert.Contains("#wi124", rail.QuerySelector("[data-tile='waiting'] .sub.dep.waits")!.TextContent);
        Assert.NotNull(rail.QuerySelector("[data-tile='waiting'] button[aria-label='Remove dependency']"));
        Assert.NotNull(rail.QuerySelector("[data-tile='waiting'] button[aria-label='Wait on another work item']"));
        Assert.Contains("#wi125", rail.QuerySelector("[data-tile='blocks'] .sub.dep.blocks")!.TextContent);
        Assert.Equal("1", rail.QuerySelector("[data-tile='blocks'] .tile-h .count")!.TextContent.Trim());
        Assert.NotNull(rail.QuerySelector("[data-tile='related']"));
    }

    [Fact(DisplayName = "A folded group's done subtasks open in place from its N done button")]
    public async Task FoldedGroupOpensInPlace()
    {
        await using BunitContext ctx = NewContext();
        IRenderedComponent<WorkItemPage> page = ctx.Render<WorkItemPage>(p => p.Add(c => c.Id, 123));
        View(page, "linked");
        IElement Fold() => page.Find(".sub-grp button.sub-fold");
        string[] Texts() => [.. page.FindAll(".tile[data-tile='subtasks'] .sub .s-t").Select(t => t.TextContent.Trim())];

        Assert.Equal("▸ 2 done", Fold().TextContent.Trim());
        Assert.DoesNotContain("Add /health route", Texts());

        Act(page, () => Fold(), e => e.Click());

        page.WaitForAssertion(() =>
        {
            Assert.Equal("▾ 2 done", Fold().TextContent.Trim());
            Assert.Equal("true", Fold().GetAttribute("aria-expanded"));
            Assert.Equal(["Ping test in suite 543", "Add /health route", "Return build hash", "Hide internals from anonymous callers"], Texts());   // in place: the done ones follow the open ones of their group
            Assert.Equal(["Add /health route", "Return build hash"], page.FindAll(".sub.done .s-t").Select(t => t.TextContent.Trim()));   // muted
            Assert.True(page.Find("#sub-1-1").HasAttribute("checked"));
        });

        // a done subtask is reopened from there, and the fold counts one less
        page.ChangeWhenReady("#sub-1-1", false);
        page.WaitForAssertion(() => Assert.Contains("- [ ] 1.1", repo.Read("openspec/changes/add-health-check/tasks.md")));
        page.WaitForAssertion(() => Assert.Equal("▾ 1 done", Fold().TextContent.Trim()));

        Act(page, () => Fold(), e => e.Click());
        page.WaitForAssertion(() =>
        {
            Assert.Equal("▸ 1 done", Fold().TextContent.Trim());
            Assert.DoesNotContain("Return build hash", Texts());
        });
    }

    // ── changed period with times ──

    [Fact(DisplayName = "The date range picker shows time inputs only in its with-time mode")]
    public async Task DateRangeTimesOnlyInWithTimeMode()
    {
        await using BunitContext ctx = NewContext();
        DateOnly day = new(2026, 11, 11);
        (TimeOnly? From, TimeOnly? To)? times = null;
        IRenderedComponent<TaskManager.Components.Shared.DateRange> Render(bool withTime, DateOnly? start, TimeOnly? startTime = null, TimeOnly? endTime = null) => ctx.Render<TaskManager.Components.Shared.DateRange>(p => p
            .Add(c => c.Start, start).Add(c => c.End, start).Add(c => c.WithTime, withTime)
            .Add(c => c.StartTime, startTime).Add(c => c.EndTime, endTime)
            .Add(c => c.TimesChanged, t => { times = t; }));
        async Task<IRenderedComponent<TaskManager.Components.Shared.DateRange>> Open(IRenderedComponent<TaskManager.Components.Shared.DateRange> range)
        {
            range.ClickWhenReady("button.dates-btn");
            range.WaitForAssertion(() => Assert.NotEmpty(range.FindAll(".range-pop")));
            await range.InvokeAsync(() => { });
            return range;
        }

        Assert.Empty((await Open(Render(false, day))).FindAll("input[type='time']"));   // off by default: the item page's dates are as before
        IRenderedComponent<TaskManager.Components.Shared.DateRange> none = await Open(Render(true, null));
        Assert.Equal(2, none.FindAll("input[type='time']").Count);
        Assert.All(none.FindAll("input[type='time']"), i => Assert.True(i.HasAttribute("disabled")));   // nothing picked yet

        IRenderedComponent<TaskManager.Components.Shared.DateRange> set = await Open(Render(true, day, new TimeOnly(9, 0), new TimeOnly(12, 30)));
        Assert.All(set.FindAll("input[type='time']"), i => Assert.False(i.HasAttribute("disabled")));
        Assert.Equal(["09:00", "12:30"], set.FindAll("input[type='time']").Select(i => i.GetAttribute("value")));
        Assert.Equal("11 Nov 09:00 – 12:30", set.Find("button.dates-btn").TextContent.Trim());   // a single day, then its two times
        set.ChangeWhenReady("input[aria-label='to time']", "13:15");
        set.WaitForAssertion(() => Assert.Equal((new TimeOnly(9, 0), new TimeOnly(13, 15)), times));
        set.ChangeWhenReady("input[aria-label='from time']", "");
        set.WaitForAssertion(() => Assert.Equal((null, new TimeOnly(12, 30)), times));   // an emptied input clears that time (the other end is the parent's value, which this test never updates)
    }

    // ---- git-deltas chunk ----

    [Fact(DisplayName = "The item page and the examine panel show the last contributing commit")]
    public async Task ItemShowsItsLastCommit()
    {
        TempGit.Init(repo.Root);
        TempGit.Commit(repo.Root, "2026-11-01T10:00:00+00:00", "Add the fixture");
        repo.Write("openspec/changes/export-remittances/proposal.md", repo.Read("openspec/changes/export-remittances/proposal.md") + "\nA second line.\n");
        TempGit.Commit(repo.Root, "2026-11-02T10:00:00+00:00", "Reword the export");
        string second = TempGit.Run(repo.Root, null, "log", "-1", "--format=%h").Trim(), first = TempGit.Run(repo.Root, null, "log", "--format=%h", "--skip=1", "-1").Trim();
        await using BunitContext ctx = NewContext();
        List<Action> gitRefreshes = [];
        TaskManager.AppState.Caches.S.Git.Background = work => { gitRefreshes.Add(work); return Task.CompletedTask; };   // git's answer about the work tree arrives after the frame: the test delivers it

        // #wi107 was last touched by the second commit, #wi123 by the first
        IRenderedComponent<WorkItemPage> page = ctx.Render<WorkItemPage>(p => p.Add(c => c.Id, 107));
        Assert.Equal(second, page.Find(".kv.last-commit .chip").TextContent.Trim());
        Assert.Equal("Reword the export", page.Find(".kv.last-commit .chip").GetAttribute("title"));
        IRenderedComponent<WorkItemPage> other = ctx.Render<WorkItemPage>(p => p.Add(c => c.Id, 123));
        Assert.Equal(first, other.Find(".kv.last-commit .chip").TextContent.Trim());

        // files saved since are not in a commit yet: the line says so
        repo.Write("openspec/changes/add-health-check/proposal.md", repo.Read("openspec/changes/add-health-check/proposal.md") + "\nNot committed yet.\n");
        OpenSpecStore.OpenSpecStoreApi.Refresh();   // an outside edit, noticed by the board's file watcher: the version the uncommitted answer is kept under moves
        IRenderedComponent<WorkItemPage> dirty = ctx.Render<WorkItemPage>(p => p.Add(c => c.Id, 123));
        Assert.Equal(first, dirty.Find(".kv.last-commit .chip").TextContent.Trim());   // the answer known so far comes at once ...
        for (int turns = 0; gitRefreshes.Count > 0; turns++)   // ... and git's new one reloads the page, which may queue the next one: take them one at a time until the queue is empty
        {
            Assert.True(turns < 10, "a refresh keeps queueing another although nothing changed");
            Action work = gitRefreshes[0];
            gitRefreshes.RemoveAt(0);
            work();
        }
        dirty.WaitForAssertion(() => Assert.Equal("uncommitted", dirty.Find(".kv.last-commit .chip").TextContent.Trim()));

        // the examine panel has the one line
        UiService popups = ctx.Services.GetRequiredService<UiService>();
        IRenderedComponent<ExaminePanel> panel = ctx.Render<ExaminePanel>();
        popups.Examine((repo.Root, 107));
        panel.WaitForAssertion(() => Assert.Equal($"{second} · Reword the export", panel.Find("[data-line='last commit'] b").TextContent.Trim()));
        popups.Examine((repo.Root, 123));
        panel.WaitForAssertion(() => Assert.Equal("uncommitted changes", panel.Find("[data-line='last commit'] b").TextContent.Trim()));
    }

    // ── #wi208: the table editor left the app ──

    [Fact(DisplayName = "The note editor is a plain textarea with no table helper attached")]
    public async Task NoteEditorIsAPlainTextarea()
    {
        await using BunitContext ctx = NewContext();
        IRenderedComponent<NotePage> page = ctx.Render<NotePage>(p => p.Add(c => c.Id, 123));

        page.ClickWhenReady(".note-view");
        page.WaitForAssertion(() => Assert.NotEmpty(page.FindAll("textarea.desc-in")));
        await page.InvokeAsync(() => { });   // the after-render work of the editor has run

        Assert.DoesNotContain(ctx.JSInterop.Invocations, i => i.Identifier == "rtm.markdownEditor");
    }

    [Fact(DisplayName = "index.html loads no markdown table scripts")]
    public void IndexLoadsNoMarkdownTableScripts()
    {
        string html = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "wwwroot", "index.html"));
        Assert.DoesNotContain("md-table.js", html);
        Assert.DoesNotContain("md-editor.js", html);
    }
    private static void Act<T>(Bunit.IRenderedComponent<T> cut, Func<AngleSharp.Dom.IElement?> find, Action<AngleSharp.Dom.IElement> fire) where T : Microsoft.AspNetCore.Components.IComponent
    {
        cut.Eventually(() => Xunit.Assert.NotNull(find()));   // rendered and present, or the test fails
        cut.InvokeAsync(() => fire(find()!)).GetAwaiter().GetResult();
    }
}
