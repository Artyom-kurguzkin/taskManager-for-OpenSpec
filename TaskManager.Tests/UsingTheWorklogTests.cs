using System.Globalization;
using AngleSharp.Dom;
using Bunit;
using GitHistory;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Notes;
using Notes.PublicDTOs;
using Radzen;
using TaskManager.Components.Pages;
using TaskManager.Components.Shared;
using TaskManager.Services;
using WorkItems;
using WorkItems.PublicDTOs;

namespace TaskManager.Tests;

/// <summary>The worklog view of a work item: a feed by day of notes, logged events and commits, with Shift-click menus on the dates and the entries.</summary>
[Collection(nameof(ModuleStateCollection))]
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

    private BunitContext NewContext()
    {
        BunitContext ctx = new();
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

    /// <summary>An entry without a menu: a Shift-click on it does nothing (it has no click handler at all).</summary>
    private static void NoMenuOn(IRenderedComponent<Worklog> log, IElement element)
    {
        try { element.Click(Shift); } catch (Bunit.MissingEventHandlerException) { }
        Assert.Empty(log.FindAll(".menu-pop"));
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

        Assert.Equal(["Add note", "Add decision", "Link document", "Link test", "Link commit", "Add subtask"], MenuItems(log));
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
        log.WaitForAssertion(() => Assert.Equal(6, MenuItems(log).Count));
    }

    [Fact(DisplayName = "Add note opens an editor in the feed; leaving it saves the title and the text, and a note added under an older day is dated that day")]
    public async Task AddNoteEditsInPlace()
    {
        await using BunitContext ctx = NewContext();
        int told = 0;
        IRenderedComponent<Worklog> log = Log(ctx, 123, () => told++);
        DateTime day = new(2026, 10, 2);

        OpenMenu(log, Header(log, day));
        Pick(log, "Add note");

        log.WaitForAssertion(() => Assert.NotNull(DayOf(log, day).QuerySelector(".wk-edit")));
        Note created = NotesApi.GetForWorkItem(123).Single(n => n.Title == "New note");
        Assert.Equal(day, created.Created.Date);
        Assert.True(Math.Abs((created.Created.TimeOfDay - DateTime.Now.TimeOfDay).TotalMinutes) < 5 || Math.Abs((created.Created.TimeOfDay - DateTime.Now.TimeOfDay).TotalMinutes) > 1435);
        Assert.Equal("Tester", created.By);
        Assert.Equal("New note", log.Find(".wk-edit input.wk-title-in").GetAttribute("value"));
        Assert.True(told >= 1);

        log.Find(".wk-edit input.wk-title-in").Input("Plan the probe");
        log.Find(".wk-edit textarea").Input("Use **GET** on /health");
        log.Find(".wk-edit").FocusOut();

        log.WaitForAssertion(() => Assert.Empty(log.FindAll(".wk-edit")));
        Note saved = NotesApi.Get(created.Id)!;
        Assert.Equal("Plan the probe", saved.Title);
        Assert.Equal("Use **GET** on /health", saved.Body.Trim());
        Assert.Equal(day, saved.Created.Date);
        // the feed follows the file a render later: wait for the entry, then read it
        log.WaitForAssertion(() => Assert.Equal("GET", DayOf(log, day).QuerySelectorAll(".wk-note").Single(e => Text(e).Contains("Plan the probe")).QuerySelector("strong")!.TextContent));

        // a note added under today is dated now
        OpenMenu(log, Header(log, DateTime.Today));
        Pick(log, "Add note");
        log.WaitForAssertion(() => Assert.NotNull(DayOf(log, DateTime.Today).QuerySelector(".wk-edit")));
        Assert.Equal(DateTime.Today, NotesApi.GetForWorkItem(123).Single(n => n.Id != created.Id && n.Title == "New note").Created.Date);
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
        Assert.Equal(["Edit"], MenuItems(log));
        Pick(log, "Edit");

        log.WaitForAssertion(() => Assert.NotEmpty(log.FindAll(".wk-edit")));
        Assert.Equal("Alpha", log.Find(".wk-edit input.wk-title-in").GetAttribute("value"));
        Assert.Equal("first words", log.Find(".wk-edit textarea").GetAttribute("value")?.Trim());

        log.Find(".wk-edit input.wk-title-in").Input("   ");     // a blank title keeps the old one
        log.Find(".wk-edit textarea").Input("second words");
        log.Find(".wk-edit textarea").FocusOut();

        log.WaitForAssertion(() => Assert.Empty(log.FindAll(".wk-edit")));
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
        log.Find(".wk-edit textarea").FocusOut();

        log.WaitForAssertion(() => Assert.Empty(log.FindAll(".wk-edit")));
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
            Assert.Equal(["Unlink"], MenuItems(log));
            Pick(log, "Unlink");
            log.WaitForAssertion(() => Assert.NotNull(DayOf(log, today).QuerySelector($".wk-e[data-kind='{unlinked}']")));
            // the link event stays as history, but the link is gone and so is its menu
            NoMenuOn(log, DayOf(log, today).QuerySelector($".wk-e[data-kind='{linked}']")!);
        }

        Assert.Empty(WorkItemsApi.Get(123)!.Commits!);
        Assert.Empty(WorkItemsApi.Get(123)!.Tests!);
        Assert.Empty(WorkItemsApi.Get(123)!.Docs!);
        Assert.Contains("unlinked a commit", Text(DayOf(log, today).QuerySelector(".wk-e[data-kind='commit-unlinked']")!));
        Assert.Contains("unlinked a test: UptimeTests / Ping test", Text(DayOf(log, today).QuerySelector(".wk-e[data-kind='test-unlinked']")!));
        Assert.Contains("unlinked a document: docs/runbook/uptime.md", Text(DayOf(log, today).QuerySelector(".wk-e[data-kind='doc-unlinked']")!));
        // a status change has no menu
        Assert.True(WorkItemsApi.SetStatus(123, "Planned").Success);
        ctx.Services.GetRequiredService<Workspace>().Notify();
        log.WaitForAssertion(() => Assert.NotNull(DayOf(log, today).QuerySelector(".wk-e[data-kind='status']")));
        NoMenuOn(log, DayOf(log, today).QuerySelector(".wk-e[data-kind='status']")!);
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
        NoMenuOn(log, mentions);
        OpenMenu(log, earlier.QuerySelector(".wk-e[data-kind='test']")!);
        Assert.Equal(["Unlink"], MenuItems(log));
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
        await using BunitContext ctx = NewContext();
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
        await Task.Delay(300);   // anything queued behind the write has had its time

        Assert.Equal(pageLoads + 1, page.Instance.Loads);
        Assert.Equal(logLoads + 1, log.Instance.Loads);
        Assert.True(page.RenderCount <= pageRenders + 2, $"the page rendered {page.RenderCount - pageRenders} times for one write");
    }

    [Fact(DisplayName = "Several Changed events in one frame reload a page once more, not once each")]
    public async Task ChangedEventsAreCoalesced()
    {
        await using BunitContext ctx = NewContext();
        IRenderedComponent<WorkItemPage> page = ctx.Render<WorkItemPage>(p => p.Add(c => c.Id, 123));
        int before = page.Instance.Loads;

        Workspace workspace = ctx.Services.GetRequiredService<Workspace>();
        for (int i = 0; i < 10; i++) workspace.Notify();   // a write and the file watcher's echo, and more
        await Task.Delay(400);

        Assert.InRange(page.Instance.Loads - before, 1, 2);   // the first at once, one more for what came after it
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

    [Fact(DisplayName = "The worklog publishes its days to the side menu and updates them when the filters change")]
    public async Task PublishesItsDays()
    {
        await using BunitContext ctx = NewContext();
        UiService ui = ctx.Services.GetRequiredService<UiService>();
        DateTime today = DateTime.Today, oct2 = new(2026, 10, 2);
        Assert.True(NotesApi.Create(123, "Zed", today.AddMinutes(1)).Item1);
        IRenderedComponent<Worklog> log = Log(ctx);

        // newest first, today always, labelled as the feed labels them, with the entries each shows
        Assert.Equal([today, oct2], ui.WorklogDays.Select(d => d.Date));
        Assert.Equal($"Today · {today.ToString("d MMM", CultureInfo.InvariantCulture)}", ui.WorklogDays[0].Label);
        Assert.Equal("2 Oct 2026", ui.WorklogDays[1].Label);
        Assert.Equal(DayOf(log, today).QuerySelectorAll(".wk-e").Length, ui.WorklogDays[0].Count);
        Assert.Equal(DayOf(log, oct2).QuerySelectorAll(".wk-e").Length, ui.WorklogDays[1].Count);

        // a filter that leaves a day nothing to show takes it off the list (today stays), and counts follow
        ui.FilterWorklog(new WorklogFilters(Who: "Zed"));
        log.WaitForAssertion(() => Assert.Equal([today], ui.WorklogDays.Select(d => d.Date)));
        Assert.Equal(1, ui.WorklogDays[0].Count);

        ui.FilterWorklog(new WorklogFilters());
        log.WaitForAssertion(() => Assert.Equal([today, oct2], ui.WorklogDays.Select(d => d.Date)));

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
        Assert.Equal(["Unlink"], MenuItems(log));

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
}
