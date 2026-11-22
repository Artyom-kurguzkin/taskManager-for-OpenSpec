using System.Globalization;
using AngleSharp.Dom;
using Bunit;
using GitHistory;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Notes;
using Notes.PublicDTOs;
using Radzen;
using TaskManager.Components.Pages;
using TaskManager.Components.Shared;
using TaskManager.AppState;
using TaskManager.BackgroundJobs;
using TaskManager.Helpers;
using WorkItems;
using WorkItems.PublicDTOs;

namespace TaskManager.Tests;

/// <summary>The worklog view of a work item: a feed by day of notes, logged events and commits, with Shift-click menus on the dates and the entries.</summary>
public sealed class UsingTheWorklogTests : IDisposable
{
    private readonly TestRepo repo = new();
    private readonly string settingsFile = Path.Combine(Path.GetTempPath(), "rtm-wk-" + Guid.NewGuid().ToString("N") + ".json");
    private IRenderedComponent<Overlays>? overlays;
    private static readonly MouseEventArgs Shift = new() { ShiftKey = true };

    public void Dispose()
    {
        repo.Dispose();
        try { File.Delete(settingsFile); } catch (IOException) { }
    }

    /// <summary>Completes when the workspace next says it changed: a saved note is written on the pool after the entry drew, and that is the write landing.</summary>
    private static Task Written(BunitContext ctx)
    {
        TaskCompletionSource written = new(TaskCreationOptions.RunContinuationsAsynchronously);
        ctx.Services.GetRequiredService<Workspace>().Changed += () => written.TrySetResult();
        return written.Task.WaitAsync(TimeSpan.FromSeconds(30));
    }

    private BunitContext NewContext(TimeProvider? time = null)
    {
        BunitContext ctx = new();
        if (time is not null) ctx.Services.AddSingleton(time);   // the pages' timers (reload coalescing, autosave) run on it: the test moves time by hand
        ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        ctx.Services.AddSingleton<UiService>();
        ctx.Services.AddRadzenComponents();
        ctx.Services.AddSingleton(new SettingsStore(settingsFile));
        ctx.Services.AddSingleton<Workspace>();
        Assert.True(ctx.Services.GetRequiredService<Workspace>().Open(repo.Root).Success);
        ctx.Services.GetRequiredService<SettingsStore>().Update(s => s with { Name = "Tester" });   // who the events are recorded as
        overlays = ctx.Render<Overlays>();
        return ctx;
    }

    private IRenderedComponent<Worklog> Log(BunitContext ctx, int id = 123, Action? told = null) =>
        ctx.Render<Worklog>(p => p.Add(c => c.Item, WorkItemsApi.Get(id)!).Add(c => c.Root, repo.Root)
            .Add(c => c.OnChanged, Microsoft.AspNetCore.Components.EventCallback.Factory.Create(this, () => told?.Invoke())));

    /// <summary>
    /// Lets <paramref name="ms"/> milliseconds of the pages' clock pass, one coalescing window (50 ms) at a time, and after each step lets the
    /// renderer finish what the timers queued: what real time would have done in that span, without waiting for it and in the same order every run.
    /// </summary>
    private static async Task Pass<T>(FakeTimeProvider time, IRenderedComponent<T> on, int ms) where T : Microsoft.AspNetCore.Components.IComponent
    {
        for (int passed = 0; passed < ms; passed += 50)
        {
            time.Advance(TimeSpan.FromMilliseconds(50));
            await on.InvokeAsync(() => { });
        }
    }

    private static string Iso(DateTime d) => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    private static string Text(IElement e) => string.Join(' ', e.TextContent.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    private static IElement DayOf(IRenderedComponent<Worklog> log, DateTime day) => log.Find($".wk-day[data-day='{Iso(day)}']");
    private static IElement Header(IRenderedComponent<Worklog> log, DateTime day) => DayOf(log, day).QuerySelector(".wk-date")!;
    private static IReadOnlyList<string> MenuItems(IRenderedComponent<Worklog> log) => log.FindAll(".menu-pop .menu-item").Select(Text).ToList();
    private static IElement Item(IRenderedComponent<Worklog> log, string text) => log.FindAll(".menu-pop .menu-item").Single(i => Text(i) == text);

    /// <summary>Shift-click on the element, then the menu it opens (waited for).</summary>
    private static void OpenMenu(IRenderedComponent<Worklog> log, IElement element)
    {
        element.Click(Shift);
        log.WaitForAssertion(() => Assert.NotEmpty(log.FindAll(".menu-pop")));
    }

    private static void Pick(IRenderedComponent<Worklog> log, string text)
    {
        Item(log, text).Click();
        log.WaitForAssertion(() => Assert.DoesNotContain(log.FindAll(".menu-pop .menu-item"), i => Text(i) == text));   // the menu closed (a picker may have opened)
    }


    /// <summary>An entry that can only be reported as a bug: its menu offers that and nothing else (closed again).</summary>
    private static void OnlyReportOn(IRenderedComponent<Worklog> log, IElement element)
    {
        OpenMenu(log, element);
        Assert.Equal(["Report a bug from this"], MenuItems(log));
        log.Find(".menu-backdrop").Click();
        log.WaitForAssertion(() => Assert.Empty(log.FindAll(".menu-pop")));
    }

    // ── git and test results, for the link cases ──

    private void Git(IEnumerable<string> args, DateTime? at = null)
    {
        System.Diagnostics.ProcessStartInfo psi = new("git") { WorkingDirectory = repo.Root, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (string a in args) psi.ArgumentList.Add(a);
        if (at is DateTime d) psi.Environment["GIT_AUTHOR_DATE"] = psi.Environment["GIT_COMMITTER_DATE"] = d.ToString("s", CultureInfo.InvariantCulture);
        using System.Diagnostics.Process p = System.Diagnostics.Process.Start(psi)!;
        p.StandardOutput.ReadToEnd();
        p.StandardError.ReadToEnd();
        p.WaitForExit();
    }

    private void GitInit(string message = "Fix uptime probe", DateTime? at = null)
    {
        foreach (string[] args in new string[][] { ["init", "-q"], ["config", "user.name", "Tester"], ["config", "user.email", "t@example.com"], ["config", "commit.gpgsign", "false"] }) Git(args);
        Commit(message, at);
    }

    private void Commit(string message, DateTime? at = null)
    {
        Git(["add", "-A"]);
        Git(["commit", "-q", "--allow-empty", "-m", message], at);
        GitHistoryApi.Configure(repo.Root, reset: true);   // what was learnt about the folder (is it git) starts over
    }

    private static string Head() => GitHistoryApi.Recent(1)[0].Hash;

    private void UseResults()
    {
        repo.Write("results/run.trx", ReadingLinkedTestRunsTests.Trx);
        ReadingLinkedTestRunsTests.UseResults(repo, "results/*.trx");
    }

    private static readonly TestLink Ping = new("UptimeTests", "Ping test", null), PageLoad = new("UptimeTests", "Page load test", null);

    // ── Tests ──

    [Fact(DisplayName = "The worklog groups notes and events by day, newest day first, each entry with the time it was created, and today is always there")]
    public async Task GroupsByDay()
    {
        await using BunitContext ctx = NewContext();
        DateTime today = DateTime.Today, yesterday = today.AddDays(-1);
        Assert.True(NotesApi.Create(123, "Tom", yesterday.AddHours(16).AddMinutes(30)).Item1);
        Assert.True(NotesApi.Create(123, "Ann", today.AddMinutes(1)).Item1);
        Assert.True(WorkItemsApi.SetStatus(123, "Planned").Success);

        IRenderedComponent<Worklog> log = Log(ctx);

        Assert.Equal([Iso(today), Iso(yesterday), "2026-10-02"], log.FindAll(".wk-day[data-day]").Select(d => d.GetAttribute("data-day")!));
        Assert.Equal($"Today · {today.ToString("d MMM", CultureInfo.InvariantCulture)}", Text(Header(log, today).QuerySelector(".wk-date-t")!));
        Assert.Contains($"{yesterday.ToString("d MMM yyyy", CultureInfo.InvariantCulture)}", Text(Header(log, yesterday)));
        Assert.Contains("2 Oct 2026", Text(Header(log, new DateTime(2026, 10, 2))));
        // newest first inside a day: the status change (now) above the note made a minute past midnight
        Assert.Equal(["status", "note"], DayOf(log, today).QuerySelectorAll(".wk-e").Select(e => e.GetAttribute("data-kind")!));
        Assert.Contains("status: In Progress → Planned", Text(DayOf(log, today).QuerySelector(".wk-e")!));
        IElement oldNote = DayOf(log, yesterday).QuerySelector(".wk-note")!;
        Assert.Equal("16:30", Text(oldNote.QuerySelector(".wk-time")!));
        Assert.Equal("Tom", Text(oldNote.QuerySelector(".wk-by")!));
        Assert.Equal("09:14", Text(DayOf(log, new DateTime(2026, 10, 2)).QuerySelector(".wk-time")!));
        Assert.Contains("Ping test", Text(DayOf(log, new DateTime(2026, 10, 2)).QuerySelector(".wk-note")!));
        Assert.Equal("Tester", Text(DayOf(log, today).QuerySelector(".wk-ev .wk-by")!));

        // an item with nothing logged still has today, with the way to add to it
        IRenderedComponent<Worklog> empty = Log(ctx, 107);
        IElement only = empty.Find(".wk-day");
        Assert.Equal(Iso(today), only.GetAttribute("data-day"));
        Assert.Contains("Nothing yet today. Shift-click the date to add an entry.", Text(only));
    }

    [Fact(DisplayName = "Clicking a day folds it and clicking it again unfolds it")]
    public async Task FoldsADay()
    {
        await using BunitContext ctx = NewContext();
        IRenderedComponent<Worklog> log = Log(ctx);
        DateTime day = new(2026, 10, 2);
        Assert.Equal("true", Header(log, day).GetAttribute("aria-expanded"));
        Assert.NotEmpty(DayOf(log, day).QuerySelectorAll(".wk-e"));

        Header(log, day).Click();
        log.WaitForAssertion(() => Assert.Equal("false", Header(log, day).GetAttribute("aria-expanded")));
        Assert.Empty(DayOf(log, day).QuerySelectorAll(".wk-e"));
        Assert.Empty(log.FindAll(".menu-pop"));   // a plain click only folds

        Header(log, day).Click();
        log.WaitForAssertion(() => Assert.Equal("true", Header(log, day).GetAttribute("aria-expanded")));
        Assert.NotEmpty(DayOf(log, day).QuerySelectorAll(".wk-e"));
    }

    [Fact(DisplayName = "Shift-clicking a day opens a menu to add a note or a decision, link a document, link a test, link a commit or add a subtask")]
    public async Task DateMenu()
    {
        await using BunitContext ctx = NewContext();
        IRenderedComponent<Worklog> log = Log(ctx);
        DateTime day = new(2026, 10, 2);

        OpenMenu(log, Header(log, day));

        Assert.Equal(["Add note", "Add decision", "Add bug", "Link document", "Link test", "Link commit", "Add subtask"], MenuItems(log));
        Assert.Equal("true", Header(log, day).GetAttribute("aria-expanded"));   // the Shift-click did not fold it
        // no results file and no git here: the two are switched off and say why
        Assert.Equal("No test results file configured", log.FindAll(".menu-pop .menu-item").Single(i => Text(i) == "Link test").GetAttribute("title"));
        Assert.True(log.FindAll(".menu-pop .menu-item").Single(i => Text(i) == "Link test").HasAttribute("disabled"));
        Assert.Equal("Not a git repository", log.FindAll(".menu-pop .menu-item").Single(i => Text(i) == "Link commit").GetAttribute("title"));
        Assert.False(Item(log, "Add note").HasAttribute("disabled"));

        // Shift+Enter on the focused date does the same
        log.Find(".menu-backdrop").Click();
        log.WaitForAssertion(() => Assert.Empty(log.FindAll(".menu-pop")));
        Header(log, day).KeyDown(new KeyboardEventArgs { Key = "Enter", ShiftKey = true });
        log.WaitForAssertion(() => Assert.Equal(7, MenuItems(log).Count));
    }

    [Fact(DisplayName = "Shift-clicking a note offers Edit, which opens it in place")]
    public async Task ShiftClickNoteEdits()
    {
        await using BunitContext ctx = NewContext();
        (bool _, string _, int id) = NotesApi.Create(123, "Tom", new DateTime(2026, 10, 3, 11, 0, 0));
        Assert.True(NotesApi.SetTitle(id, "Alpha").Success);
        Assert.True(NotesApi.SaveBody(id, "first words").Success);
        IRenderedComponent<Worklog> log = Log(ctx);
        IElement note() => DayOf(log, new DateTime(2026, 10, 3)).QuerySelector(".wk-note")!;

        note().Click();   // a plain click does nothing
        Assert.Empty(log.FindAll(".menu-pop"));
        Assert.Empty(log.FindAll(".wk-edit"));

        OpenMenu(log, note());
        Assert.Equal(["Edit", "Make it a decision", "Make it a bug", "Make it a subtask"], MenuItems(log));
        Pick(log, "Edit");

        log.WaitForAssertion(() => Assert.NotEmpty(log.FindAll(".wk-edit")));
        Assert.Equal("Alpha", log.Find(".wk-edit input.wk-title-in").GetAttribute("value"));
        Assert.Equal("first words", log.Find(".wk-edit textarea").GetAttribute("value")?.Trim());

        log.Find(".wk-edit input.wk-title-in").Input("   ");     // a blank title keeps the old one
        log.Find(".wk-edit textarea").Input("second words");
        Task written = Written(ctx);
        log.Find(".wk-edit textarea").FocusOut();

        log.WaitForAssertion(() => Assert.Empty(log.FindAll(".wk-edit")));
        await written;
        Assert.Equal("Alpha", NotesApi.Get(id)!.Title);
        Assert.Equal("second words", NotesApi.Get(id)!.Body.Trim());
        Assert.Contains("second words", Text(note()));
    }

    [Fact(DisplayName = "Leaving the note editor turns the note's unticked checkboxes into subtasks, and the feed shows them as added")]
    public async Task EditorPromotesCheckboxes()
    {
        await using BunitContext ctx = NewContext();
        (bool _, string _, int id) = NotesApi.Create(123, "Tom");
        IRenderedComponent<Worklog> log = Log(ctx);
        DateTime today = DateTime.Today;

        OpenMenu(log, DayOf(log, today).QuerySelector(".wk-note")!);
        Pick(log, "Edit");
        log.WaitForAssertion(() => Assert.NotEmpty(log.FindAll(".wk-edit")));
        log.Find(".wk-edit textarea").Input("- [ ] restrict permissions\n- [ ] write the docs\n- [x] already done");
        Task written = Written(ctx);
        log.Find(".wk-edit textarea").FocusOut();

        log.WaitForAssertion(() => Assert.Empty(log.FindAll(".wk-edit")));
        await written;
        Assert.Equal(["restrict permissions", "write the docs"], WorkItemsApi.Get(123)!.Subtasks.TakeLast(2).Select(s => s.Text));
        Assert.Contains("- [ ] restrict permissions (created subtask)", NotesApi.Get(id)!.Body);
        Assert.Contains("- [x] already done", NotesApi.Get(id)!.Body);
        log.WaitForAssertion(() =>
        {
            List<string> lines = DayOf(log, today).QuerySelectorAll(".wk-ev .wk-line").Select(Text).ToList();
            Assert.Contains("added subtask 2.2 restrict permissions", lines);
            Assert.Contains("added subtask 2.3 write the docs", lines);
        });
        Assert.Contains("restrict permissions (created subtask)", Text(DayOf(log, today).QuerySelector(".wk-note .wk-body")!));   // the checkbox line stays in the note
        overlays!.WaitForAssertion(() => Assert.Contains("Created 2 subtasks from the note", string.Join(' ', overlays.FindAll(".toast").Select(Text))));

        // a second time round (open and leave again) makes nothing new
        OpenMenu(log, DayOf(log, today).QuerySelector(".wk-note")!);
        Pick(log, "Edit");
        log.WaitForAssertion(() => Assert.NotEmpty(log.FindAll(".wk-edit")));
        log.Find(".wk-edit textarea").FocusOut();
        log.WaitForAssertion(() => Assert.Empty(log.FindAll(".wk-edit")));
        Assert.Equal(2, WorkItemsApi.GetEvents(123).Count(e => e.Kind == "subtask-added"));
    }

    [Fact(DisplayName = "Add subtask in a day's menu adds subtasks one after another from an input in the feed")]
    public async Task AddSubtaskFromTheMenu()
    {
        await using BunitContext ctx = NewContext();
        IRenderedComponent<Worklog> log = Log(ctx);
        DateTime today = DateTime.Today;
        IElement input() => DayOf(log, today).QuerySelector(".wk-add-subtask input")!;

        OpenMenu(log, Header(log, today));
        Pick(log, "Add subtask");
        log.WaitForAssertion(() => Assert.NotNull(DayOf(log, today).QuerySelector(".wk-add-subtask input")));

        input().Input("Restrict permissions");
        input().KeyDown(new KeyboardEventArgs { Key = "Enter" });
        log.WaitForAssertion(() => Assert.Equal("Restrict permissions", WorkItemsApi.Get(123)!.Subtasks[^1].Text));
        await log.InvokeAsync(() => { });
        log.WaitForAssertion(() => Assert.Equal("", input().GetAttribute("value") ?? ""));   // still open, empty, for the next one

        input().Input("Write the docs");
        input().KeyDown(new KeyboardEventArgs { Key = "Enter" });
        log.WaitForAssertion(() => Assert.Equal("Write the docs", WorkItemsApi.Get(123)!.Subtasks[^1].Text));
        await log.InvokeAsync(() => { });
        log.WaitForAssertion(() =>
        {
            List<string> lines = DayOf(log, today).QuerySelectorAll(".wk-ev .wk-line").Select(Text).ToList();
            Assert.Contains("added subtask 2.2 Restrict permissions", lines);
            Assert.Contains("added subtask 2.3 Write the docs", lines);
        });

        input().Input("   ");   // blank: nothing is added
        input().KeyDown(new KeyboardEventArgs { Key = "Enter" });
        await log.InvokeAsync(() => { });
        Assert.Equal(3, WorkItemsApi.Get(123)!.Subtasks.Count(s => s.Number.StartsWith("2.")));

        input().KeyDown(new KeyboardEventArgs { Key = "Escape" });
        log.WaitForAssertion(() => Assert.Empty(log.FindAll(".wk-add-subtask")));

        // leaving the input closes it as well
        OpenMenu(log, Header(log, today));
        Pick(log, "Add subtask");
        log.WaitForAssertion(() => Assert.NotNull(DayOf(log, today).QuerySelector(".wk-add-subtask input")));
        input().FocusOut();
        log.WaitForAssertion(() => Assert.Empty(log.FindAll(".wk-add-subtask")));
    }

    [Fact(DisplayName = "Linking a commit, a test or a document from the menu shows it in the feed as an event with who did it")]
    public async Task LinkingFromTheMenu()
    {
        await using BunitContext ctx = NewContext();
        GitInit();
        UseResults();
        string hash = Head();
        IRenderedComponent<Worklog> log = Log(ctx);
        DateTime today = DateTime.Today;

        OpenMenu(log, Header(log, today));
        Pick(log, "Link commit");
        log.WaitForAssertion(() => Assert.Contains(log.FindAll(".wk-pick .menu-item"), i => Text(i).Contains("Fix uptime probe")));
        log.FindAll(".wk-pick .menu-item").First(i => Text(i).Contains("Fix uptime probe")).Click();
        log.WaitForAssertion(() => Assert.Equal([hash], WorkItemsApi.Get(123)!.Commits!));
        log.WaitForAssertion(() => Assert.Empty(log.FindAll(".wk-pick")));
        log.WaitForAssertion(() =>   // the feed follows the file a render later
        {
            IElement commit = DayOf(log, today).QuerySelector(".wk-e[data-kind='commit-linked']")!;
            Assert.Contains($"linked a commit: {hash} Fix uptime probe", Text(commit));
            Assert.Equal("Tester", Text(commit.QuerySelector(".wk-by")!));
        });

        OpenMenu(log, Header(log, today));
        Pick(log, "Link test");
        log.WaitForAssertion(() => Assert.Contains(log.FindAll(".wk-pick .menu-item"), i => Text(i).Contains("Ping test")));
        log.FindAll(".wk-pick .menu-item").First(i => Text(i).Contains("Ping test")).Click();
        log.WaitForAssertion(() => Assert.Equal([Ping], WorkItemsApi.Get(123)!.Tests!));
        log.WaitForAssertion(() => Assert.Contains("linked a test: UptimeTests / Ping test", Text(DayOf(log, today).QuerySelector(".wk-e[data-kind='test-linked']")!)));

        OpenMenu(log, Header(log, today));
        Pick(log, "Link document");
        overlays!.WaitForAssertion(() => Assert.Contains("Link a document", overlays.Find(".dialog").TextContent));
        overlays.Find(".dialog input").Input("docs/runbook/uptime.md");
        overlays.Find(".dialog button.primary").Click();
        log.WaitForAssertion(() => Assert.Equal(["docs/runbook/uptime.md"], WorkItemsApi.Get(123)!.Docs!));
        log.WaitForAssertion(() => Assert.Equal("doc/docs/runbook/uptime.md", DayOf(log, today).QuerySelector(".wk-e[data-kind='doc-linked'] a")!.GetAttribute("href")));   // the path opens the document

        // a path outside the repository is refused with a toast and nothing is linked
        OpenMenu(log, Header(log, today));
        Pick(log, "Link document");
        overlays.WaitForAssertion(() => overlays.Find(".dialog input").Input("../outside.md"));
        overlays.Find(".dialog button.primary").Click();
        overlays.WaitForAssertion(() => Assert.NotEmpty(overlays.FindAll(".toast.warning")));
        Assert.Equal(["docs/runbook/uptime.md"], WorkItemsApi.Get(123)!.Docs!);
    }

    [Fact(DisplayName = "Shift-clicking a linked commit, test or document offers Unlink, and the feed then says it was unlinked")]
    public async Task UnlinkingFromTheFeed()
    {
        await using BunitContext ctx = NewContext();
        GitInit();
        UseResults();
        string hash = Head();
        Assert.True(WorkItemsApi.SetLinkedCommits(123, [hash]).Success);
        Assert.True(WorkItemsApi.SetLinkedTests(123, [Ping]).Success);
        Assert.True(WorkItemsApi.SetLinkedDocs(123, ["docs/runbook/uptime.md"]).Success);
        IRenderedComponent<Worklog> log = Log(ctx);
        DateTime today = DateTime.Today;

        foreach ((string linked, string unlinked) in new[] { ("commit-linked", "commit-unlinked"), ("test-linked", "test-unlinked"), ("doc-linked", "doc-unlinked") })
        {
            OpenMenu(log, DayOf(log, today).QuerySelector($".wk-e[data-kind='{linked}']")!);
            Assert.Equal(["Unlink", "Report a bug from this"], MenuItems(log));
            Pick(log, "Unlink");
            log.WaitForAssertion(() => Assert.NotNull(DayOf(log, today).QuerySelector($".wk-e[data-kind='{unlinked}']")));
            // the link event stays as history, but the link is gone and so is Unlink
            OnlyReportOn(log, DayOf(log, today).QuerySelector($".wk-e[data-kind='{linked}']")!);
        }

        Assert.Empty(WorkItemsApi.Get(123)!.Commits!);
        Assert.Empty(WorkItemsApi.Get(123)!.Tests!);
        Assert.Empty(WorkItemsApi.Get(123)!.Docs!);
        Assert.Contains("unlinked a commit", Text(DayOf(log, today).QuerySelector(".wk-e[data-kind='commit-unlinked']")!));
        Assert.Contains("unlinked a test: UptimeTests / Ping test", Text(DayOf(log, today).QuerySelector(".wk-e[data-kind='test-unlinked']")!));
        Assert.Contains("unlinked a document: docs/runbook/uptime.md", Text(DayOf(log, today).QuerySelector(".wk-e[data-kind='doc-unlinked']")!));
        // a status change offers only Report a bug from this
        Assert.True(WorkItemsApi.SetStatus(123, "Planned").Success);
        ctx.Services.GetRequiredService<Workspace>().Notify();
        log.WaitForAssertion(() => Assert.NotNull(DayOf(log, today).QuerySelector(".wk-e[data-kind='status']")));
        OnlyReportOn(log, DayOf(log, today).QuerySelector(".wk-e[data-kind='status']")!);
    }

    [Fact(DisplayName = "Links made before the log existed still show: commits at their commit time, tests and documents under Linked earlier")]
    public async Task LinksOlderThanTheLog()
    {
        await using BunitContext ctx = NewContext();
        GitInit("Old work", new DateTime(2026, 10, 5, 10, 0, 0));
        string old = Head();
        Commit("Touch #wi123 again", new DateTime(2026, 10, 6, 11, 30, 0));
        string mention = Head();
        Assert.True(WorkItemsApi.SetLinkedCommits(123, [old]).Success);
        Assert.True(WorkItemsApi.SetLinkedTests(123, [Ping]).Success);
        Assert.True(WorkItemsApi.SetLinkedDocs(123, ["docs/runbook/uptime.md"]).Success);
        UseResults();
        WorkItem item = WorkItemsApi.Get(123)!;
        File.Delete(Path.Combine(item.Path, "events.log"));   // as if the links were made before the log existed

        IRenderedComponent<Worklog> log = Log(ctx);

        IElement picked = DayOf(log, new DateTime(2026, 10, 5)).QuerySelector(".wk-e[data-kind='commit']")!;
        Assert.Contains($"commit {old} Old work", Text(picked));
        Assert.Equal("10:00", Text(picked.QuerySelector(".wk-time")!));
        IElement mentions = DayOf(log, new DateTime(2026, 10, 6)).QuerySelector(".wk-e[data-kind='mention']")!;
        Assert.Contains($"mentions #wi123 {mention} Touch #wi123 again", Text(mentions));
        Assert.Equal("11:30", Text(mentions.QuerySelector(".wk-time")!));

        IElement earlier = log.Find(".wk-earlier");
        Assert.Equal("Linked earlier", Text(earlier.QuerySelector(".wk-earlier-h")!));
        Assert.Empty(earlier.QuerySelectorAll(".wk-time"));
        Assert.Contains("UptimeTests / Ping test", Text(earlier.QuerySelector(".wk-e[data-kind='test']")!));
        Assert.Contains("docs/runbook/uptime.md", Text(earlier.QuerySelector(".wk-e[data-kind='doc']")!));

        // the old links can be undone from there; a commit that only mentions the item cannot
        OnlyReportOn(log, mentions);
        OpenMenu(log, earlier.QuerySelector(".wk-e[data-kind='test']")!);
        Assert.Equal(["Unlink", "Report a bug from this"], MenuItems(log));
        Pick(log, "Unlink");
        log.WaitForAssertion(() => Assert.Empty(WorkItemsApi.Get(123)!.Tests!));
        log.WaitForAssertion(() => Assert.Null(log.Find(".wk-earlier").QuerySelector(".wk-e[data-kind='test']")));
    }

    [Fact(DisplayName = "A linked test shows its latest result in the feed")]
    public async Task LinkedTestResult()
    {
        await using BunitContext ctx = NewContext();
        UseResults();
        Assert.True(WorkItemsApi.SetLinkedTests(123, [Ping, PageLoad, new TestLink("UptimeTests", "Gone test", null)]).Success);
        IRenderedComponent<Worklog> log = Log(ctx);

        IElement dot(string test) => log.FindAll(".wk-e[data-kind='test-linked']").Single(e => Text(e).Contains(test)).QuerySelector(".wk-dot")!;

        Assert.Contains("passed", dot("Ping test").ClassList);
        Assert.Equal("passed", dot("Ping test").GetAttribute("aria-label"));
        Assert.Contains("failed", dot("Page load test").ClassList);
        Assert.Contains("none", dot("Gone test").ClassList);
        Assert.Equal("not in the results", dot("Gone test").GetAttribute("aria-label"));

        // unlinked, a test has no dot (only its history line)
        Assert.True(WorkItemsApi.SetLinkedTests(123, [PageLoad]).Success);
        ctx.Services.GetRequiredService<Workspace>().Notify();   // the page learns of changes made outside it
        log.WaitForAssertion(() => Assert.Empty(log.FindAll(".wk-e[data-kind='test-linked']").Where(e => Text(e).Contains("Ping test")).SelectMany(e => e.QuerySelectorAll(".wk-dot"))));
    }

    [Fact(DisplayName = "A status change and a ticked subtask appear in the feed as events")]
    public async Task StatusAndSubtaskEvents()
    {
        await using BunitContext ctx = NewContext();
        Assert.True(WorkItemsApi.SetStatus(123, "Planned").Success);
        Assert.True(WorkItemsApi.SetSubtaskDone(123, "2.1", true).Success);
        Assert.True(WorkItemsApi.SetSubtaskDone(123, "2.1", false).Success);
        Assert.True(WorkItemsApi.AddSubtask(123, "Write docs").Success);
        Assert.True(WorkItemsApi.AddDependency(123, 107).Success);
        Assert.True(WorkItemsApi.RemoveDependency(123, 107).Success);
        IRenderedComponent<Worklog> log = Log(ctx);

        List<string> lines = DayOf(log, DateTime.Today).QuerySelectorAll(".wk-ev .wk-line").Select(Text).ToList();   // newest first

        Assert.Contains("status: In Progress → Planned", lines);
        Assert.Contains("reopened 2.1 Hide internals from anonymous callers", lines);
        Assert.Contains("added subtask 2.2 Write docs", lines);
        Assert.Contains(lines, l => l.StartsWith("ticked 2.1 Hide internals"));
        Assert.Contains(lines, l => l.StartsWith("waits on #wi107 · "));
        Assert.Contains(lines, l => l.StartsWith("no longer waits on #wi107"));
        Assert.Equal("item/107", DayOf(log, DateTime.Today).QuerySelector(".wk-e[data-kind='dependency-added'] a")!.GetAttribute("href"));
        Assert.True(lines.FindIndex(l => l.StartsWith("no longer waits on #wi107")) < lines.FindIndex(l => l.StartsWith("waits on #wi107")));   // newest above oldest
    }

    [Fact(DisplayName = "An agent's note carries the agent chip until it is reviewed, and a note merged in says which item it came from")]
    public async Task AgentChipAndMergedNote()
    {
        await using BunitContext ctx = NewContext();
        (bool _, string _, int id) = NotesApi.Create(123, "Claude");
        Assert.True(NotesApi.StampAgent(id, "Claude").Success);
        IRenderedComponent<Worklog> log = Log(ctx);
        IElement note() => log.FindAll(".wk-note").Single(e => e.GetAttribute("data-kind") == "note" && Text(e).Contains("New note"));

        Assert.Single(note().QuerySelectorAll(".agent-chip"));
        Assert.Empty(log.FindAll(".wk-note").Where(e => !Text(e).Contains("New note")).SelectMany(e => e.QuerySelectorAll(".agent-chip")));

        note().QuerySelector(".agent-chip")!.Click();

        log.WaitForAssertion(() => Assert.Empty(note().QuerySelectorAll(".agent-chip")));
        Assert.False(NotesApi.Get(id)!.AgentUnreviewed);

        // 107 is merged into 123: its description becomes a note that says where it came from
        (bool ok, string message, int merged) = WorkItemsApi.Merge([123, 107], 123);
        Assert.True(ok, message);
        IRenderedComponent<Worklog> after = Log(ctx, merged);
        Note from = NotesApi.GetForWorkItem(merged).Single(n => n.MergedFrom == 107);
        IElement entry = after.FindAll(".wk-note").Single(e => Text(e).Contains(from.Title));
        Assert.Contains("from #wi107", Text(entry));
        Assert.DoesNotContain("from #wi", Text(after.FindAll(".wk-note").First(e => Text(e).Contains("Ping test"))));
    }

    [Fact(DisplayName = "Without git Link commit is switched off and says why, and a merged item's feed shows the commits that mention the items it was made from")]
    public async Task NoGitAndMergedCommits()
    {
        await using BunitContext ctx = NewContext();
        IRenderedComponent<Worklog> plain = Log(ctx);
        OpenMenu(plain, Header(plain, DateTime.Today));
        IElement linkCommit = plain.FindAll(".menu-pop .menu-item").Single(i => Text(i) == "Link commit");
        Assert.True(linkCommit.HasAttribute("disabled"));
        Assert.Equal("Not a git repository", linkCommit.GetAttribute("title"));
        Assert.Throws<Bunit.MissingEventHandlerException>(() => linkCommit.Click());   // a disabled item has no handler: nothing opens
        Assert.Empty(plain.FindAll(".wk-pick"));

        GitInit("Start on #wi123", new DateTime(2026, 10, 7, 9, 0, 0));
        Commit("Probe for #wi107 too", new DateTime(2026, 10, 8, 14, 45, 0));
        (bool ok, string message, int merged) = WorkItemsApi.Merge([123, 107], 123);
        Assert.True(ok, message);

        IRenderedComponent<Worklog> log = Log(ctx, merged);

        Assert.Contains("mentions #wi123 ", Text(DayOf(log, new DateTime(2026, 10, 7)).QuerySelector(".wk-e[data-kind='mention']")!));
        IElement fromSource = DayOf(log, new DateTime(2026, 10, 8)).QuerySelector(".wk-e[data-kind='mention']")!;
        Assert.Contains("mentions #wi107 ", Text(fromSource));
        Assert.Contains("Probe for #wi107 too", Text(fromSource));
        Assert.Equal("14:45", Text(fromSource.QuerySelector(".wk-time")!));
    }

    /// <summary>The item page open on its worklog: the page and the worklog it hosts.</summary>
    private (IRenderedComponent<WorkItemPage> Page, IRenderedComponent<Worklog> Log) OpenWorklogView(BunitContext ctx)
    {
        IRenderedComponent<WorkItemPage> page = ctx.Render<WorkItemPage>(p => p.Add(c => c.Id, 123));
        page.Find(".item-views input[value='worklog']").Change("worklog");
        page.WaitForAssertion(() => Assert.NotEmpty(page.FindAll(".worklog")));
        return (page, page.FindComponent<Worklog>());
    }

    [Fact(DisplayName = "One write from the worklog reloads the page once, not once per Changed event")]
    public async Task OneWriteReloadsOnce()
    {
        FakeTimeProvider time = new(new DateTimeOffset(DateTime.SpecifyKind(DateTime.Today.AddHours(12), DateTimeKind.Unspecified), TimeSpan.Zero));   // the page's "today" is the clock's: the real date, held still
        await using BunitContext ctx = NewContext(time);
        (IRenderedComponent<WorkItemPage> page, IRenderedComponent<Worklog> log) = OpenWorklogView(ctx);
        DateTime today = DateTime.Today;
        IElement input() => DayOf(log, today).QuerySelector(".wk-add-subtask input")!;
        OpenMenu(log, Header(log, today));
        Pick(log, "Add subtask");
        log.WaitForAssertion(() => Assert.NotNull(input()));
        (int pageLoads, int logLoads, int pageRenders) = (page.Instance.Loads, log.Instance.Loads, page.RenderCount);

        input().Input("Restrict permissions");
        input().KeyDown(new KeyboardEventArgs { Key = "Enter" });
        log.WaitForAssertion(() => Assert.Contains("added subtask 2.2 Restrict permissions", DayOf(log, today).QuerySelectorAll(".wk-ev .wk-line").Select(Text)));
        await Pass(time, page, 300);   // anything queued behind the write on the pages' clock has had its time

        Assert.Equal(pageLoads + 1, page.Instance.Loads);
        Assert.Equal(logLoads + 1, log.Instance.Loads);
        Assert.True(page.RenderCount <= pageRenders + 2, $"the page rendered {page.RenderCount - pageRenders} times for one write");
    }

    [Fact(DisplayName = "Several Changed events in one frame reload a page once more, not once each")]
    public async Task ChangedEventsAreCoalesced()
    {
        FakeTimeProvider time = new();
        await using BunitContext ctx = NewContext(time);
        IRenderedComponent<WorkItemPage> page = ctx.Render<WorkItemPage>(p => p.Add(c => c.Id, 123));
        int before = page.Instance.Loads;

        Workspace workspace = ctx.Services.GetRequiredService<Workspace>();
        for (int i = 0; i < 10; i++) workspace.Notify();   // a write and the file watcher's echo, and more
        await Pass(time, page, 400);

        Assert.InRange(page.Instance.Loads - before, 1, 2);   // the first at once, one more for what came after it
    }

    [Fact(DisplayName = "A worklog of a folder that is not a git repository reads again without starting a git process")]
    public async Task NoGitProcessWithoutARepository()
    {
        FakeTimeProvider time = new();
        await using BunitContext ctx = NewContext(time);
        IRenderedComponent<Worklog> log = Log(ctx);
        Assert.False(GitHistory.GitHistoryApi.IsRepository());   // the sample repo has no real .git (only an empty objects folder)
        int before = GitHistory.GitHistoryModule.Processes;
        int loads = log.Instance.Loads;

        ctx.Services.GetRequiredService<Workspace>().Notify();
        await Pass(time, log, 300);

        Assert.True(log.Instance.Loads > loads);
        Assert.Equal(before, GitHistory.GitHistoryModule.Processes);   // a process start costs 70 ms and more on a busy machine, per read
    }

    [Fact(DisplayName = "The description is the last entry of the worklog, at the bottom as the item's first post, has no hover effect, and Shift-click Edit changes it in place")]
    public async Task DescriptionIsTheLastEntry()
    {
        await using BunitContext ctx = NewContext();
        Assert.True(WorkItemsApi.SetDescription(123, "The **first** words").Success);
        IRenderedComponent<Worklog> log = Log(ctx);

        IElement first = log.Find(".worklog").Children[^1];   // after every day, "Linked earlier" included
        Assert.Contains("wk-first", first.ClassName);
        Assert.All(log.FindAll(".wk-day"), day => Assert.True(day.CompareDocumentPosition(first).HasFlag(AngleSharp.Dom.DocumentPositions.Following)));
        Assert.DoesNotContain("hov", log.Find(".wk-first article").ClassList);   // no hover fill or outline
        Assert.Contains("Description", Text(first.QuerySelector(".wk-label")!));
        Assert.Equal("first", Text(first.QuerySelector(".wk-body strong")!));
        Assert.Equal(Iso(DateTime.Today), log.Find(".worklog").Children[0].GetAttribute("data-day"));

        IElement entry() => log.Find(".wk-first .wk-e");
        entry().Click();   // a plain click does nothing
        Assert.Empty(log.FindAll(".menu-pop"));
        OpenMenu(log, entry());
        Assert.Equal(["Edit"], MenuItems(log));
        Pick(log, "Edit");

        log.WaitForAssertion(() => Assert.NotEmpty(log.FindAll(".wk-edit")));
        Assert.Equal("The **first** words", log.Find(".wk-edit textarea").GetAttribute("value")?.Trim());
        log.Find(".wk-edit textarea").Input("Second words");
        log.Find(".wk-edit textarea").FocusOut();

        log.WaitForAssertion(() => Assert.Empty(log.FindAll(".wk-edit")));
        Assert.Equal("Second words", WorkItemsApi.GetDescription(123).Trim());
        Assert.Contains("Second words", Text(log.Find(".wk-first .wk-body")));

        // nothing written yet: the way to add it is said, muted
        Assert.True(WorkItemsApi.SetDescription(123, "").Success);
        IRenderedComponent<Worklog> bare = Log(ctx);
        Assert.Equal("Shift-click to add a description", Text(bare.Find(".wk-first .muted")));
    }

    [Fact(DisplayName = "The worklog filters hide notes, events, links or subtask events, and who narrows the feed to one person")]
    public async Task FiltersAndWho()
    {
        await using BunitContext ctx = NewContext();
        UiService ui = ctx.Services.GetRequiredService<UiService>();
        Assert.True(NotesApi.Create(123, "Zed", DateTime.Now).Item1);
        Assert.True(WorkItemsApi.SetStatus(123, "Planned").Success);
        Assert.True(WorkItemsApi.AddSubtask(123, "Write docs").Success);
        Assert.True(WorkItemsApi.SetLinkedDocs(123, ["docs/runbook/uptime.md"]).Success);
        IRenderedComponent<Worklog> log = Log(ctx);
        List<string> kinds() => DayOf(log, DateTime.Today).QuerySelectorAll(".wk-e").Select(e => e.GetAttribute("data-kind")!).ToList();

        Assert.NotNull(ui.WorklogOpen);   // the side menu is told a worklog is open
        Assert.Contains("Zed", ui.WorklogPeople);
        Assert.Contains("Tester", ui.WorklogPeople);
        Assert.Equal(["doc-linked", "note", "status", "subtask-added"], kinds().Order());

        ui.FilterWorklog(new WorklogFilters(Events: false));
        log.WaitForAssertion(() => Assert.DoesNotContain("status", kinds()));
        Assert.Contains("note", kinds());
        Assert.Contains("doc-linked", kinds());

        ui.FilterWorklog(new WorklogFilters(Notes: false));
        log.WaitForAssertion(() => Assert.DoesNotContain("note", kinds()));
        Assert.Contains("status", kinds());

        ui.FilterWorklog(new WorklogFilters(Links: false));
        log.WaitForAssertion(() => Assert.DoesNotContain("doc-linked", kinds()));
        Assert.Contains("subtask-added", kinds());

        ui.FilterWorklog(new WorklogFilters(Subtasks: false));
        log.WaitForAssertion(() => Assert.DoesNotContain("subtask-added", kinds()));
        Assert.Contains("doc-linked", kinds());

        ui.FilterWorklog(new WorklogFilters(Who: "Zed"));
        log.WaitForAssertion(() => Assert.Equal(["note"], kinds()));
        Assert.All(log.FindAll(".wk-day .wk-by"), by => Assert.Equal("Zed", Text(by)));
        Assert.Empty(log.FindAll(".wk-day[data-day='2026-10-02']"));   // a day with nothing left is left out (today stays)

        ui.FilterWorklog(new WorklogFilters());
        log.WaitForAssertion(() => Assert.Contains("status", kinds()));
        Assert.NotEmpty(log.FindAll(".wk-day[data-day='2026-10-02']"));
    }

    // ── days published ──

    /// <summary>The days of the published list: Bugs and Description are entries of their own (see the feed navigation tests).</summary>
    private static IReadOnlyList<WorklogDay> Days(UiService ui) => [.. ui.WorklogDays.Where(d => d.Special is null)];

    [Fact(DisplayName = "The worklog publishes its days to the side menu and updates them when the filters change")]
    public async Task PublishesItsDays()
    {
        await using BunitContext ctx = NewContext();
        UiService ui = ctx.Services.GetRequiredService<UiService>();
        DateTime today = DateTime.Today, oct2 = new(2026, 10, 2);
        Assert.True(NotesApi.Create(123, "Zed", today.AddMinutes(1)).Item1);
        IRenderedComponent<Worklog> log = Log(ctx);

        // newest first, today always, labelled as the feed labels them, with the entries each shows
        Assert.Equal([today, oct2], Days(ui).Select(d => d.Date));
        Assert.Equal($"Today · {today.ToString("d MMM", CultureInfo.InvariantCulture)}", Days(ui)[0].Label);
        Assert.Equal("2 Oct 2026", Days(ui)[1].Label);
        Assert.Equal(DayOf(log, today).QuerySelectorAll(".wk-e").Length, Days(ui)[0].Count);
        Assert.Equal(DayOf(log, oct2).QuerySelectorAll(".wk-e").Length, Days(ui)[1].Count);

        // a filter that leaves a day nothing to show takes it off the list (today stays), and counts follow
        ui.FilterWorklog(new WorklogFilters(Who: "Zed"));
        log.WaitForAssertion(() => Assert.Equal([today], Days(ui).Select(d => d.Date)));
        Assert.Equal(1, Days(ui)[0].Count);

        ui.FilterWorklog(new WorklogFilters());
        log.WaitForAssertion(() => Assert.Equal([today, oct2], Days(ui).Select(d => d.Date)));

        // the jump action unfolds a folded day
        Header(log, oct2).Click();
        log.WaitForAssertion(() => Assert.Empty(DayOf(log, oct2).QuerySelectorAll(".wk-e")));
        ui.WorklogOpen!.JumpTo!(oct2);
        log.WaitForAssertion(() => Assert.NotEmpty(DayOf(log, oct2).QuerySelectorAll(".wk-e")));

        ((IDisposable)log.Instance).Dispose();
        Assert.Empty(ui.WorklogDays);   // the worklog closes: the list goes
    }

    // ── unlink a test ──

    [Fact(DisplayName = "An Unlink menu stays on the entry it was opened on when the feed changes under it: a new entry above does not move the menu to another test, and Unlink removes the test that was clicked")]
    public async Task UnlinkMenuStaysOnItsEntry()
    {
        await using BunitContext ctx = NewContext();
        UseResults();
        Assert.True(WorkItemsApi.SetLinkedTests(123, [Ping]).Success);
        Assert.True(WorkItemsApi.SetLinkedTests(123, [Ping, PageLoad]).Success);   // the newer link sits above the older one
        IRenderedComponent<Worklog> log = Log(ctx);
        DateTime today = DateTime.Today;
        IElement Linked(string test) => DayOf(log, today).QuerySelectorAll(".wk-e[data-kind='test-linked']").Single(e => Text(e).Contains(test));

        OpenMenu(log, Linked("Ping test"));
        Assert.Equal(["Unlink", "Report a bug from this"], MenuItems(log));

        // something else writes to the item (another agent, a status change): its entry lands above the two, and the feed draws again
        Assert.True(WorkItemsApi.SetStatus(123, "Planned").Success);
        ctx.Services.GetRequiredService<Workspace>().Notify();
        log.WaitForAssertion(() => Assert.NotNull(DayOf(log, today).QuerySelector(".wk-e[data-kind='status']")));

        // the menu is still open, and it still belongs to the Ping entry: it sits in the Ping entry's own wrapper, not the Page load one
        IElement open = log.Find(".menu-pop").Closest(".menu-wrap")!;
        Assert.Contains("Ping test", Text(open));
        Assert.DoesNotContain("Page load test", Text(open));

        Pick(log, "Unlink");
        log.WaitForAssertion(() => Assert.NotNull(DayOf(log, today).QuerySelector(".wk-e[data-kind='test-unlinked']")));
        Assert.Equal(["UptimeTests / Page load test"], WorkItemsApi.Get(123)!.Tests!.Select(t => $"{t.Suite} / {t.Test}"));
        Assert.Contains("unlinked a test: UptimeTests / Ping test", Text(DayOf(log, today).QuerySelector(".wk-e[data-kind='test-unlinked']")!));
    }

    // ── decisions and edits ──

    [Fact(DisplayName = "A decision note is emphasised in the feed with a DECISION label and the accent mark, and Add decision from the day menu creates one and opens it in place")]
    public async Task DecisionNotesAreEmphasised()
    {
        await using BunitContext ctx = NewContext();
        (bool _, string _, int made) = NotesApi.Create(123, "Zed", DateTime.Now, NoteKinds.Decision);
        Assert.True(NotesApi.SetTitle(made, "Use note kinds").Success);
        Assert.True(NotesApi.SaveBody(made, "One place, filterable.").Success);
        Assert.True(NotesApi.Create(123, "Zed", DateTime.Now.AddMinutes(-1)).Item1);
        (bool _, string _, int untitled) = NotesApi.Create(123, "Zed", DateTime.Now.AddMinutes(-2), NoteKinds.Decision);
        Assert.True(NotesApi.SaveBody(untitled, "Default title stays unseen.").Success);
        IRenderedComponent<Worklog> log = Log(ctx);
        DateTime today = DateTime.Today;

        // a decision that keeps the default title shows the tag and the text, not "DECISION Decision"
        IElement bare = DayOf(log, today).QuerySelectorAll(".wk-decision").Single(e => Text(e).Contains("Default title stays unseen."));
        Assert.Equal("Decision", Text(bare.QuerySelector(".wk-tag")!));
        Assert.Empty(bare.QuerySelectorAll(".wk-title"));
        string bareText = Text(bare.QuerySelector(".wk-main")!);
        Assert.Equal(bareText.IndexOf("Decision", StringComparison.Ordinal), bareText.LastIndexOf("Decision", StringComparison.Ordinal));   // the word once: the tag

        IElement decision = DayOf(log, today).QuerySelectorAll(".wk-note").Single(e => Text(e).Contains("Use note kinds"));
        Assert.Contains("wk-decision", decision.ClassList);
        Assert.Equal("Decision", Text(decision.QuerySelector(".wk-tag")!));
        Assert.NotNull(decision.QuerySelector(".wk-mark"));
        Assert.Contains("One place, filterable.", Text(decision));
        IElement plain = DayOf(log, today).QuerySelectorAll(".wk-note").Single(e => Text(e).Contains("New note"));
        Assert.DoesNotContain("wk-decision", plain.ClassList);
        Assert.Empty(plain.QuerySelectorAll(".wk-tag"));

        // Add decision, from the day menu, right after Add note
        DateTime day = new(2026, 10, 2);
        OpenMenu(log, Header(log, day));
        Assert.Equal(["Add note", "Add decision"], MenuItems(log).Take(2));
        Pick(log, "Add decision");

        log.WaitForAssertion(() => Assert.NotNull(DayOf(log, day).QuerySelector(".wk-edit")));
        Note created = NotesApi.GetForWorkItem(123).Single(n => n.Title == "Decision" && n.Id != untitled);
        Assert.True(created.IsDecision);
        Assert.Equal(day, created.Created.Date);
        Assert.Equal("Tester", created.By);
        Assert.Contains("wk-decision", log.Find(".wk-edit").ClassList);
        Assert.Equal("What was decided, and why. Two lines.", log.Find(".wk-edit textarea").GetAttribute("placeholder"));

        log.Find(".wk-edit input.wk-title-in").Input("Keep kinds in frontmatter");
        log.Find(".wk-edit textarea").Input("One place. Why: filterable.");
        log.Find(".wk-edit").FocusOut();
        log.WaitForAssertion(() => Assert.Empty(log.FindAll(".wk-edit")));
        Assert.True(NotesApi.Get(created.Id)!.IsDecision);   // saving keeps the kind
        log.WaitForAssertion(() => Assert.Contains("wk-decision", DayOf(log, day).QuerySelectorAll(".wk-note").Single(e => Text(e).Contains("Keep kinds in frontmatter")).ClassList));
    }

    [Fact(DisplayName = "The Decisions and Edits filters hide decision notes and housekeeping events (title, priority, label, dates, feature, contributors, description, attachments) on their own, and Notes no longer hides decisions")]
    public async Task DecisionAndEditFilters()
    {
        await using BunitContext ctx = NewContext();
        UiService ui = ctx.Services.GetRequiredService<UiService>();
        Assert.True(NotesApi.Create(123, "Zed", DateTime.Now, NoteKinds.Decision).Item1);
        Assert.True(NotesApi.Create(123, "Zed", DateTime.Now).Item1);
        Assert.True(WorkItemsApi.SetStatus(123, "Planned").Success);
        Assert.True(WorkItemsApi.SetLinkedDocs(123, ["docs/runbook/uptime.md"]).Success);
        string at = DateTime.Now.AddMinutes(-5).ToString("s", CultureInfo.InvariantCulture);
        string[] edits = ["title | Old -> New", "priority | Medium -> High", "label | bug added", "dates | 2026-10-01 -> 2026-10-09", "feature | none -> Uptime",
            "contributor-added | Zed", "contributor-removed | Zed", "description-edited | description.md", "attachment-added | n7: shot.png", "custom | something else"];
        string events = Path.Combine(repo.OpenSpec, "changes", "add-health-check", "events.log");
        File.AppendAllText(events, string.Concat(edits.Select(e => $"{at} | Tester | {e}\n")) + $"{at} | Tester | plan-linked | DevResources/plans/p.md\n{at} | Tester | merged-from | #wi5 Old probe\n");
        IRenderedComponent<Worklog> log = Log(ctx);
        List<string> kinds() => DayOf(log, DateTime.Today).QuerySelectorAll(".wk-e").Select(e => e.GetAttribute("data-kind")!).ToList();
        string[] housekeeping = [.. edits.Select(e => e.Split(" | ")[0])];

        Assert.Contains("decision", kinds());
        Assert.All(housekeeping, k => Assert.Contains(k, kinds()));
        Assert.Contains("attached shot.png to #n7", Text(log.Find(".wk-e[data-kind='attachment-added'] .wk-line")));
        Assert.Contains("edited the description", Text(log.Find(".wk-e[data-kind='description-edited'] .wk-line")));
        Assert.Contains("title: Old → New", Text(log.Find(".wk-e[data-kind='title'] .wk-line")));
        Assert.Contains("merged #wi5 Old probe into this item", Text(log.Find(".wk-e[data-kind='merged-from'] .wk-line")));

        ui.FilterWorklog(new WorklogFilters(Decisions: false));
        log.WaitForAssertion(() => Assert.DoesNotContain("decision", kinds()));
        Assert.Contains("note", kinds());   // the other notes stay
        Assert.Contains("status", kinds());
        Assert.All(housekeeping, k => Assert.Contains(k, kinds()));

        ui.FilterWorklog(new WorklogFilters(Notes: false));
        log.WaitForAssertion(() => Assert.DoesNotContain("note", kinds()));
        Assert.Contains("decision", kinds());   // Notes does not hide decisions

        ui.FilterWorklog(new WorklogFilters(Edits: false));
        log.WaitForAssertion(() => Assert.DoesNotContain("title", kinds()));
        Assert.All(housekeeping, k => Assert.DoesNotContain(k, kinds()));   // an unknown kind counts as an edit too
        Assert.Contains("decision", kinds());
        Assert.Contains("note", kinds());
        Assert.Contains("status", kinds());
        Assert.Contains("doc-linked", kinds());
        Assert.Contains("plan-linked", kinds());   // a plan link is a link, not an edit
        Assert.Contains("merged-from", kinds());   // a merge is an event, with created and status

        ui.FilterWorklog(new WorklogFilters(Events: false));
        log.WaitForAssertion(() => Assert.DoesNotContain("merged-from", kinds()));
        Assert.DoesNotContain("status", kinds());
        Assert.Contains("title", kinds());

        ui.FilterWorklog(new WorklogFilters(Links: false));
        log.WaitForAssertion(() => Assert.DoesNotContain("plan-linked", kinds()));
        Assert.DoesNotContain("doc-linked", kinds());
        Assert.Contains("title", kinds());

        // the days in the side menu follow the filters
        ui.FilterWorklog(new WorklogFilters(Notes: false, Decisions: false, Events: false, Links: false, Subtasks: false, Edits: false));
        log.WaitForAssertion(() => Assert.Empty(kinds()));
        Assert.Equal(0, ui.WorklogDays[0].Count);
    }

    // ── convert entries ──

    [Fact(DisplayName = "A note's menu offers Make it a decision, a bug and a subtask, and a decision's offers Make it a note, a bug and a subtask, under Edit")]
    public async Task ConvertMenuOfNotesAndDecisions()
    {
        await using BunitContext ctx = NewContext();
        (bool _, string _, int plain) = NotesApi.Create(123, "Tom", DateTime.Now.AddMinutes(-3));
        Assert.True(NotesApi.SetTitle(plain, "Plain one").Success);
        (bool _, string _, int decision) = NotesApi.Create(123, "Tom", DateTime.Now.AddMinutes(-2), NoteKinds.Decision);
        Assert.True(NotesApi.SetTitle(decision, "Decided one").Success);
        Assert.True(WorkItemsApi.AddSubtask(123, "Open subtask").Success);
        IRenderedComponent<Worklog> log = Log(ctx);
        DateTime today = DateTime.Today;
        IElement entry(string text) => DayOf(log, today).QuerySelectorAll(".wk-e").Single(e => Text(e).Contains(text));

        OpenMenu(log, entry("Plain one"));
        Assert.Equal(["Edit", "Make it a decision", "Make it a bug", "Make it a subtask"], MenuItems(log));
        Pick(log, "Edit");   // Edit stays first, and still opens the note
        log.WaitForAssertion(() => Assert.NotEmpty(log.FindAll(".wk-edit")));
        log.Find(".wk-edit").FocusOut();
        log.WaitForAssertion(() => Assert.Empty(log.FindAll(".wk-edit")));

        OpenMenu(log, entry("Decided one"));
        Assert.Equal(["Edit", "Make it a note", "Make it a bug", "Make it a subtask"], MenuItems(log));
        Pick(log, "Make it a note");
        log.WaitForAssertion(() => Assert.False(NotesApi.Get(decision)!.IsDecision));

        // the subtask added above, still open: Make it a note / decision, nothing else
        OpenMenu(log, entry("added subtask 2.2 Open subtask"));
        Assert.Equal(["Make it a note", "Make it a decision", "Make it a bug"], MenuItems(log));
        Pick(log, "Make it a note");
        log.WaitForAssertion(() => Assert.Contains(NotesApi.GetForWorkItem(123), n => n.Title == "Open subtask"));
    }

    [Fact(DisplayName = "A subtask that is ticked offers only Report a bug from this in the feed")]
    public async Task TickedSubtaskHasNoConvertMenu()
    {
        await using BunitContext ctx = NewContext();
        Assert.True(WorkItemsApi.AddSubtask(123, "Finished subtask").Success);
        Assert.True(WorkItemsApi.SetSubtaskDone(123, "2.2", true).Success);
        IRenderedComponent<Worklog> log = Log(ctx);

        OpenMenu(log, DayOf(log, DateTime.Today).QuerySelectorAll(".wk-e[data-kind='subtask-added']").Single());
        Assert.Equal(["Report a bug from this"], MenuItems(log));
    }

    [Fact(DisplayName = "Making a note a subtask adds the subtask from its title, keeps the note and logs the conversion")]
    public async Task NoteBecomesSubtask()
    {
        await using BunitContext ctx = NewContext();
        (bool _, string _, int id) = NotesApi.Create(123, "Tom", DateTime.Now.AddMinutes(-1));
        Assert.True(NotesApi.SetTitle(id, "Restrict permissions").Success);
        Assert.True(NotesApi.SaveBody(id, "details nobody keeps").Success);
        IRenderedComponent<Worklog> log = Log(ctx);
        DateTime today = DateTime.Today;

        OpenMenu(log, DayOf(log, today).QuerySelectorAll(".wk-note").Single(e => Text(e).Contains("Restrict permissions")));
        Pick(log, "Make it a subtask");

        Assert.Equal("Restrict permissions", NotesApi.Get(id)!.Title);
        Assert.Equal($"Restrict permissions #n{id}", WorkItemsApi.Get(123)!.Subtasks[^1].Text);
        string number = WorkItemsApi.Get(123)!.Subtasks[^1].Number;
        Assert.Contains(WorkItemsApi.GetEvents(123), e => e.Kind == "converted" && e.Text == $"note #n{id} -> subtask {number}");
        log.WaitForAssertion(() => Assert.Contains($"promoted note #n{id} to subtask {number}", Text(DayOf(log, today).QuerySelector(".wk-e[data-kind='converted']")!)));
        Assert.Contains(DayOf(log, today).QuerySelectorAll(".wk-note"), e => Text(e).Contains("Restrict permissions") && Text(e).Contains("details nobody keeps"));   // the note is still in the feed
    }

    [Fact(DisplayName = "Making a subtask a decision creates the decision from its text, keeps the subtask and logs the conversion")]
    public async Task SubtaskBecomesDecision()
    {
        await using BunitContext ctx = NewContext();
        Assert.True(WorkItemsApi.AddSubtask(123, "Use note kinds").Success);
        IRenderedComponent<Worklog> log = Log(ctx);
        DateTime today = DateTime.Today;

        OpenMenu(log, DayOf(log, today).QuerySelector(".wk-e[data-kind='subtask-added']")!);
        Pick(log, "Make it a decision");

        log.WaitForAssertion(() => Assert.Contains(WorkItemsApi.Get(123)!.Subtasks, s => s.Text == "Use note kinds"));
        Note made = NotesApi.GetForWorkItem(123).Single(n => n.Title == "Use note kinds");
        Assert.True(made.IsDecision);
        Assert.Equal("Tester", made.By);
        Assert.Equal(today, made.Created.Date);
        Assert.Contains(WorkItemsApi.GetEvents(123), e => e.Kind == "converted" && e.Text == $"subtask 2.2 -> decision #n{made.Id}");
        log.WaitForAssertion(() => Assert.Contains($"converted subtask 2.2 to a decision #n{made.Id}", Text(DayOf(log, today).QuerySelector(".wk-e[data-kind='converted']")!)));
        Assert.Contains("wk-decision", DayOf(log, today).QuerySelectorAll(".wk-note").Single(e => Text(e).Contains("Use note kinds")).ClassList);
    }

    // ── description editor ──

    [Fact(DisplayName = "The description editor fills the rest of the window below its header")]
    public async Task DescriptionEditorFillsTheWindow()
    {
        await using BunitContext ctx = NewContext();
        IRenderedComponent<Worklog> log = Log(ctx);

        OpenMenu(log, log.Find(".wk-first .wk-e"));
        Pick(log, "Edit");

        log.WaitForAssertion(() => Assert.NotEmpty(log.FindAll(".wk-first.filling textarea.desc-in")));   // the section takes the window's height, the box takes what the label leaves
        log.WaitForAssertion(() => Assert.Contains(ctx.JSInterop.Invocations, i => i.Identifier == "rtm.fillEditor"));   // scrolled to the top of the page and sized to the scroll area

        string css = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "wwwroot", "css", "app.css")).Replace("\r\n", "\n");
        Assert.Matches(@"\.wk-first\.filling\s*\{[^}]*display:\s*flex[^}]*flex-direction:\s*column", css);
        Assert.Matches(@"\.wk-first\.filling textarea\.desc-in\s*\{[^}]*flex:\s*1", css);
    }

    [Fact(DisplayName = "The description editor shows only the user's text, without empty Why and What Changes sections, and saving keeps the item valid")]
    public async Task DescriptionEditorShowsOnlyTheUsersText()
    {
        await using BunitContext ctx = NewContext();
        repo.Write("openspec/changes/add-health-check/proposal.md", "# Proposal\n\nThe user's own words.\n\n## Why\n\n\n\n## What Changes\n\n- \n");
        IRenderedComponent<Worklog> log = Log(ctx);

        string shown = Text(log.Find(".wk-first .wk-body"));
        Assert.Contains("The user's own words.", shown);
        Assert.DoesNotContain("Why", shown);
        Assert.DoesNotContain("What Changes", shown);

        OpenMenu(log, log.Find(".wk-first .wk-e"));
        Pick(log, "Edit");
        log.WaitForAssertion(() => Assert.NotEmpty(log.FindAll(".wk-edit textarea")));
        Assert.Equal("The user's own words.", log.Find(".wk-edit textarea").GetAttribute("value")!.Trim());

        log.Find(".wk-edit textarea").Input("Second words");
        log.Find(".wk-edit textarea").FocusOut();
        log.WaitForAssertion(() => Assert.Empty(log.FindAll(".wk-edit")));

        string proposal = repo.Read("openspec/changes/add-health-check/proposal.md").Replace("\r\n", "\n");
        Assert.Equal("# Proposal\n\nSecond words", proposal.TrimEnd('\n'));   // the user's text only: OpenSpec's validator (1.14.3, --strict) needs no Why or What Changes header
        Assert.Null(WorkItemsApi.Get(123)!.Warning);
    }

    [Fact(DisplayName = "The app's scrollbar is defined once, in the base layer, from colour tokens")]
    public void ScrollbarIsDefinedOnce()
    {
        string css = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "wwwroot", "css", "app.css")).Replace("\r\n", "\n");
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(css, @"::-webkit-scrollbar-thumb\s*\{"));
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(css, @"scrollbar-color:"));
        int layer = css.IndexOf("@layer base {", StringComparison.Ordinal), end = css.IndexOf("\n}\n", layer, StringComparison.Ordinal);
        string baseLayer = css[layer..end];
        Assert.Matches(@"::-webkit-scrollbar-thumb\s*\{", baseLayer);
        Assert.Matches(@"scrollbar-color:[^;]*var\(--", baseLayer);
        Assert.Contains("scrollbar-width: thin", baseLayer);
        Assert.Contains("the app's scrollbar", baseLayer);
    }

// ── textarea identity ──

    /// <summary>
    /// The element reference id of the textarea on screen. bUnit writes it into the markup (blazor:elementReference) only in the render that created the
    /// element: Blazor's diff gives a render that keeps the element no id (RenderTreeDiffBuilder, case ElementReferenceCapture), so the attribute reads ""
    /// from then on, whichever render came last. Reading it when the test happens to look is therefore a race with the reloads; this remembers the id
    /// the creating render wrote and forgets it when the textarea is gone.
    /// </summary>
    private static Func<string?> TextareaOnScreen(IRenderedComponent<Worklog> log)
    {
        string? shown = null;
        log.OnMarkupUpdated += (_, _) =>
        {
            System.Text.RegularExpressions.Match m = System.Text.RegularExpressions.Regex.Match(log.Markup, @"<textarea\b[^>]*\bblazor:elementReference=""([^""]*)""");
            if (!m.Success) Volatile.Write(ref shown, null);
            else if (m.Groups[1].Value.Length > 0) Volatile.Write(ref shown, m.Groups[1].Value);
        };
        return () => Volatile.Read(ref shown);
    }

    // ── editor survives feed updates ──

    [Fact(DisplayName = "An open note editor keeps its textarea, text and focus when a new entry arrives above it, and does not close")]
    public async Task OpenEditorSurvivesANewEntryAbove()
    {
        await using BunitContext ctx = NewContext();
        UiService ui = ctx.Services.GetRequiredService<UiService>();
        IRenderedComponent<Worklog> log = Log(ctx);
        Func<string?> onScreen = TextareaOnScreen(log);
        await log.InvokeAsync(() => ui.WorklogOpen!.AddNote());
        log.WaitForAssertion(() => Assert.NotNull(onScreen()));
        string? textarea = onScreen();
        log.Find(".wk-edit textarea").Input("typed while the feed changes");

        // an entry arriving above the editor: the file watcher's echo of a write, another agent's event
        File.AppendAllText(Path.Combine(repo.OpenSpec, "changes", "add-health-check", "events.log"), $"{DateTime.Now.AddMinutes(1):s} | Someone | custom | arrived above\n");
        ctx.Services.GetRequiredService<Workspace>().Notify();
        log.WaitForAssertion(() => Assert.Contains("arrived above", string.Concat(log.FindAll(".wk-e[data-kind='custom']").Select(Text))));
        await log.InvokeAsync(() => { });

        // Blazor kept the textarea (it was not created again, which would have lost focus and the caret and, a moment later, closed the editor with its
        // focusout), the text is still in it, and the editor is a plain textarea: no table helper is attached (#wi208)
        Assert.Equal(textarea, onScreen());
        Assert.DoesNotContain(ctx.JSInterop.Invocations, i => i.Identifier == "rtm.markdownEditor");
        Assert.Equal("typed while the feed changes", log.Find(".wk-edit textarea").GetAttribute("value"));
        Assert.Single(log.FindAll(".wk-edit"));
    }

    // ── note editor grows ──

    [Fact(DisplayName = "The note editor grows with its text instead of scrolling in a small box, and asks for the auto-grow helper when it opens")]
    public async Task NoteEditorGrowsWithItsText()
    {
        await using BunitContext ctx = NewContext();
        UiService ui = ctx.Services.GetRequiredService<UiService>();
        IRenderedComponent<Worklog> log = Log(ctx);
        Func<string?> onScreen = TextareaOnScreen(log);
        await log.InvokeAsync(() => ui.WorklogOpen!.AddNote());
        log.WaitForAssertion(() => Assert.NotEmpty(log.FindAll(".wk-edit textarea.desc-in")));

        await log.InvokeAsync(() => { });   // the after-render work (the helper's attach) is done
        Assert.Contains(ctx.JSInterop.Invocations, i => i.Identifier == "rtm.autoGrow" && ((Microsoft.AspNetCore.Components.ElementReference)i.Arguments[0]!).Id == onScreen());
        Assert.Empty(log.FindAll(".wk-edit [aria-label='Expand editor']"));   // short: no expand button until the text is taller than one screen

        string css = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "wwwroot", "css", "app.css")).Replace("\r\n", "\n");
        Assert.Matches(@"\.wk-edit textarea\.desc-in\s*\{[^}]*resize:\s*none", css);   // no drag handle, no small fixed box
        string js = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "wwwroot", "js", "app.js"));
        Assert.Contains("rtm.autoGrow = ", js);
    }

    [Fact(DisplayName = "A note taller than the window gets an expand button that fills the window, and it collapses again")]
    public async Task NoteTallerThanTheWindowCanExpand()
    {
        await using BunitContext ctx = NewContext();
        UiService ui = ctx.Services.GetRequiredService<UiService>();
        IRenderedComponent<Worklog> log = Log(ctx);
        await log.InvokeAsync(() => ui.WorklogOpen!.AddNote());
        log.WaitForAssertion(() => Assert.NotEmpty(log.FindAll(".wk-edit textarea.desc-in")));

        await log.InvokeAsync(() => log.Instance.NoteEditorCapped(true));   // the helper: the text is taller than the window
        log.WaitForAssertion(() => Assert.NotEmpty(log.FindAll(".wk-edit button[aria-label='Expand editor']")));
        Assert.Empty(log.FindAll(".wk-edit.filling"));

        int fills() => ctx.JSInterop.Invocations.Count(i => i.Identifier == "rtm.fillEditor");
        int before = fills();
        log.Find(".wk-edit button[aria-label='Expand editor']").Click();
        log.WaitForAssertion(() => Assert.NotEmpty(log.FindAll(".wk-edit.filling")));
        log.WaitForAssertion(() => Assert.True(fills() > before));   // sized to the scroll area, as the description editor is
        Assert.Empty(log.FindAll(".wk-edit button[aria-label='Expand editor']"));

        log.Find(".wk-edit button[aria-label='Collapse editor']").Click();
        log.WaitForAssertion(() => Assert.Empty(log.FindAll(".wk-edit.filling")));
        Assert.NotEmpty(log.FindAll(".wk-edit button[aria-label='Expand editor']"));   // still taller than the window: capped, with the button
    }

    // ── nothing is lost; bugs ──

    [Fact(DisplayName = "After promoting a note the feed shows the note and the promoted line, and the note no longer offers Make it a subtask")]
    public async Task PromotedNoteStaysInTheFeed()
    {
        await using BunitContext ctx = NewContext();
        (bool _, string _, int id) = NotesApi.Create(123, "Tom", DateTime.Now.AddMinutes(-1));
        Assert.True(NotesApi.SetTitle(id, "Restrict permissions").Success);
        Assert.True(NotesApi.SaveBody(id, "details nobody should lose").Success);
        IRenderedComponent<Worklog> log = Log(ctx);
        DateTime today = DateTime.Today;
        IElement note() => DayOf(log, today).QuerySelectorAll(".wk-note").Single(e => Text(e).Contains("Restrict permissions"));

        OpenMenu(log, note());
        Pick(log, "Make it a subtask");

        string number = WorkItemsApi.Get(123)!.Subtasks[^1].Number;
        log.WaitForAssertion(() => Assert.Contains($"promoted note #n{id} to subtask {number}", Text(DayOf(log, today).QuerySelector(".wk-e[data-kind='converted']")!)));
        Assert.Contains("details nobody should lose", Text(note()));
        Assert.Equal("Restrict permissions", NotesApi.Get(id)!.Title);
        Assert.Equal(1, WorkItemsApi.GetEvents(123).Count(e => e.Kind == "converted"));

        OpenMenu(log, note());
        Assert.Equal(["Edit", "Make it a decision", "Make it a bug"], MenuItems(log));   // no second subtask from the same note
    }

    [Fact(DisplayName = "A bug report shows with its Bug tag, is hidden by the Bugs filter and not by Notes, and Add bug from the day menu opens one in place")]
    public async Task BugReports()
    {
        await using BunitContext ctx = NewContext();
        UiService ui = ctx.Services.GetRequiredService<UiService>();
        (bool _, string _, int made) = NotesApi.Create(123, "Zed", DateTime.Now, NoteKinds.Bug);
        Assert.True(NotesApi.SetTitle(made, "Save loses the title").Success);
        Assert.True(NotesApi.SaveBody(made, "Open a note, type, close.").Success);
        Assert.True(NotesApi.Create(123, "Zed", DateTime.Now.AddMinutes(-1)).Item1);
        IRenderedComponent<Worklog> log = Log(ctx);
        DateTime today = DateTime.Today;
        List<string> kinds() => DayOf(log, today).QuerySelectorAll(".wk-e").Select(e => e.GetAttribute("data-kind")!).ToList();

        IElement bug = DayOf(log, today).QuerySelectorAll(".wk-note").Single(e => Text(e).Contains("Save loses the title"));
        Assert.Contains("wk-bug", bug.ClassList);
        Assert.DoesNotContain("wk-decision", bug.ClassList);
        Assert.Equal("Bug", Text(bug.QuerySelector(".wk-tag")!));
        Assert.Contains("Open a note, type, close.", Text(bug));
        Assert.Contains("bug", kinds());

        ui.FilterWorklog(new WorklogFilters(Notes: false));
        log.WaitForAssertion(() => Assert.DoesNotContain("note", kinds()));
        Assert.Contains("bug", kinds());   // Notes does not hide bugs

        ui.FilterWorklog(new WorklogFilters(Bugs: false));
        log.WaitForAssertion(() => Assert.DoesNotContain("bug", kinds()));
        Assert.Contains("note", kinds());
        ui.FilterWorklog(new WorklogFilters());
        log.WaitForAssertion(() => Assert.Contains("bug", kinds()));

        // Add bug, from the day menu, right after Add decision
        DateTime day = new(2026, 10, 2);
        OpenMenu(log, Header(log, day));
        Assert.Equal(["Add note", "Add decision", "Add bug"], MenuItems(log).Take(3));
        Pick(log, "Add bug");

        log.WaitForAssertion(() => Assert.NotNull(DayOf(log, day).QuerySelector(".wk-edit")));
        Note created = NotesApi.GetForWorkItem(123).Single(n => n.IsBug && n.Id != made);
        Assert.Equal(day, created.Created.Date);
        Assert.Equal("Tester", created.By);
        Assert.Contains("wk-bug", log.Find(".wk-edit").ClassList);
        Assert.Equal("What is wrong, and how to see it.", log.Find(".wk-edit textarea").GetAttribute("placeholder"));

        log.Find(".wk-edit input.wk-title-in").Input("Menu closes too early");
        log.Find(".wk-edit textarea").Input("Shift-click, move the mouse.");
        log.Find(".wk-edit").FocusOut();
        log.WaitForAssertion(() => Assert.Empty(log.FindAll(".wk-edit")));
        Assert.True(NotesApi.Get(created.Id)!.IsBug);   // saving keeps the kind
    }

    [Fact(DisplayName = "Every entry can become a bug: notes, decisions and subtasks convert, and an event offers Report a bug from this")]
    public async Task EveryEntryCanBecomeABug()
    {
        await using BunitContext ctx = NewContext();
        (bool _, string _, int plain) = NotesApi.Create(123, "Tom", DateTime.Now.AddMinutes(-4));
        Assert.True(NotesApi.SetTitle(plain, "Plain one").Success);
        (bool _, string _, int decision) = NotesApi.Create(123, "Tom", DateTime.Now.AddMinutes(-3), NoteKinds.Decision);
        Assert.True(NotesApi.SetTitle(decision, "Decided one").Success);
        (bool _, string _, int bug) = NotesApi.Create(123, "Tom", DateTime.Now.AddMinutes(-2), NoteKinds.Bug);
        Assert.True(NotesApi.SetTitle(bug, "Broken one").Success);
        Assert.True(WorkItemsApi.AddSubtask(123, "Open subtask").Success);
        Assert.True(WorkItemsApi.SetStatus(123, "Planned").Success);
        IRenderedComponent<Worklog> log = Log(ctx);
        DateTime today = DateTime.Today;
        IElement entry(string text) => DayOf(log, today).QuerySelectorAll(".wk-e").Single(e => Text(e).Contains(text));

        OpenMenu(log, entry("Broken one"));
        Assert.Equal(["Edit", "Make it a note", "Make it a decision", "Mark it resolved", "Make it a subtask"], MenuItems(log));
        Pick(log, "Make it a decision");
        log.WaitForAssertion(() => Assert.True(NotesApi.Get(bug)!.IsDecision));

        OpenMenu(log, entry("Plain one"));
        Pick(log, "Make it a bug");
        log.WaitForAssertion(() => Assert.True(NotesApi.Get(plain)!.IsBug));
        Assert.Contains(WorkItemsApi.GetEvents(123), e => e.Kind == "converted" && e.Text == $"note #n{plain} -> bug");

        OpenMenu(log, entry("Decided one"));
        Pick(log, "Make it a bug");
        log.WaitForAssertion(() => Assert.True(NotesApi.Get(decision)!.IsBug));

        OpenMenu(log, entry("added subtask 2.2 Open subtask"));
        Pick(log, "Make it a bug");
        log.WaitForAssertion(() => Assert.Contains(NotesApi.GetForWorkItem(123), n => n.IsBug && n.Title == "Open subtask"));
        Assert.Contains(WorkItemsApi.Get(123)!.Subtasks, s => s.Text == "Open subtask");   // the subtask stays

        // an event: a bug note titled with what the feed says; the event stays and the new bug opens in place
        IElement status = DayOf(log, today).QuerySelector(".wk-e[data-kind='status']")!;
        string said = Text(status.QuerySelector(".wk-line")!);
        OpenMenu(log, status);
        Assert.Equal(["Report a bug from this"], MenuItems(log));
        Pick(log, "Report a bug from this");

        log.WaitForAssertion(() => Assert.NotEmpty(log.FindAll(".wk-edit")));
        Note reported = NotesApi.GetForWorkItem(123).Single(n => n.IsBug && n.Title == said);
        Assert.Contains("Planned", reported.Title);
        Assert.Contains("wk-bug", log.Find(".wk-edit").ClassList);
        Assert.NotNull(DayOf(log, today).QuerySelector(".wk-e[data-kind='status']"));
    }

    // ── #wi212: cheap feed events ──

    [Fact(DisplayName = "A started, tests green and done event each show their kind as a label in the worklog, and a decision event is emphasised")]
    public void AgentEventsShowTheirKind()
    {
        using BunitContext ctx = NewContext();
        Assert.True(WorkItemsApi.LogEvent(123, "started", "Reading the plan.").Success);
        Assert.True(WorkItemsApi.LogEvent(123, "tests-green", "12 passed\n0 failed").Success);
        Assert.True(WorkItemsApi.LogEvent(123, "decision", "Kinds are labels, not new groups.").Success);
        Assert.True(WorkItemsApi.LogEvent(123, "done", "Chunk finished.").Success);
        Assert.True(WorkItemsApi.SetStatus(123, "Planned").Success);

        IRenderedComponent<Worklog> log = Log(ctx);

        IElement entry(string kind) => DayOf(log, DateTime.Today).QuerySelector($".wk-e[data-kind='{kind}']")!;
        Assert.Equal("started", Text(entry("started").QuerySelector(".wk-kind")!));
        Assert.Equal("tests green", Text(entry("tests-green").QuerySelector(".wk-kind")!));
        Assert.Equal("done", Text(entry("done").QuerySelector(".wk-kind")!));
        Assert.Contains("Reading the plan.", Text(entry("started")));
        Assert.Contains("12 passed\n0 failed", entry("tests-green").QuerySelector(".wk-etext")!.TextContent);   // the breaks of a multi-line text are kept
        IElement decision = entry("decision");
        Assert.Contains("wk-decision", decision.ClassList);
        Assert.Equal("Decision", Text(decision.QuerySelector(".wk-tag")!));
        Assert.Contains("Kinds are labels", Text(decision));
        Assert.DoesNotContain("wk-decision", entry("done").ClassList);
        Assert.Null(entry("status").QuerySelector(".wk-kind"));   // other kinds are unchanged
    }

    // ── #wi213: notes and feed per item ──

    [Fact(DisplayName = "The feed shows the new note without re-reading the other items' notes")]
    public async Task TheFeedShowsTheNewNoteWithoutReReadingOthers()
    {
        for (int i = 0; i < 25; i++)   // a board with many items, each with a note of its own
        {
            string dir = $"openspec/changes/bulk-{i:D3}";
            repo.Write(dir + "/.openspec.yaml", $"schema: spec-driven\ncreated: 2026-09-22\nx-rtm:\n  id: {200 + i}\n  title: Bulk item {i}\n  status: Planned\n  priority: low\n");
            repo.Write(dir + "/tasks.md", "# Tasks\n\n## 1. Work\n\n- [ ] 1.1 First\n");
            repo.Write(dir + $"/notes/n{1000 + i}.md", $"---\nid: {1000 + i}\nby: Test\ncreated: 2026-10-02T09:14:00\n---\n# Note {i}\n\nBody {i}.\n");
        }
        await using BunitContext ctx = NewContext();
        TaskManager.AppState.Caches.S.Items.Clock = () => DateTime.UtcNow.AddMinutes(10);   // reads happen long after the files were written: none is "racy"
        IRenderedComponent<Worklog> log = Log(ctx);
        TaskManager.AppState.NotesCache.All();
        List<string> opened = [], listed = [];
        OpenSpecStore.OpenSpecStoreModule.State.ReadLog = opened;
        TaskManager.AppState.NotesCache.ListLog = listed;
        DateTime today = DateTime.Today;

        OpenMenu(log, Header(log, today));
        Task written = Written(ctx);
        Pick(log, "Add note");
        log.WaitForAssertion(() => Assert.NotEmpty(log.FindAll(".wk-edit")));
        log.Find(".wk-edit input.wk-title-in").Input("Brand new");
        log.Find(".wk-edit textarea").Input("Seen at once.");
        log.Find(".wk-edit textarea").FocusOut();
        log.WaitForAssertion(() => Assert.Empty(log.FindAll(".wk-edit")));
        await written;

        log.WaitForAssertion(() => Assert.Contains("Brand new", Text(DayOf(log, today))), WaitThen.Bound);
        string changes = Path.Combine(repo.Root, "openspec", "changes") + Path.DirectorySeparatorChar;
        string mine = Path.Combine(changes, "add-health-check") + Path.DirectorySeparatorChar;
        lock (opened) Assert.All(opened.Where(p => p.StartsWith(changes, StringComparison.OrdinalIgnoreCase) && p.Contains(Path.DirectorySeparatorChar + "notes" + Path.DirectorySeparatorChar)), p => Assert.StartsWith(mine, p, StringComparison.OrdinalIgnoreCase));   // no note of another change was opened (the board's own headers are the items cache's business)
        lock (listed) Assert.All(listed, p => Assert.StartsWith(mine, p, StringComparison.OrdinalIgnoreCase));   // and no other change's notes folder was looked at
    }
}
