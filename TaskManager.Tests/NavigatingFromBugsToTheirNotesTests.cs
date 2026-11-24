using AngleSharp.Dom;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Notes;
using Notes.PublicDTOs;
using Radzen;
using TaskManager.AppState;
using TaskManager.Components.Layout;
using TaskManager.Components.Pages;
using TaskManager.Components.Shared;
using WorkItems;

namespace TaskManager.Tests;

/// <summary>
/// A bug is a note: from the bug list (the unresolved bugs among Linked tasks' subtasks) a click opens the worklog at that note, its day unfolded and the entry marked.
/// This holds for a bug written as one and for a note that was converted to a bug later.
/// </summary>
public sealed class NavigatingFromBugsToTheirNotesTests : IDisposable
{
    private readonly TestRepo repo = new();
    private readonly string settingsFile = Path.Combine(Path.GetTempPath(), "rtm-bugnav-" + Guid.NewGuid().ToString("N") + ".json");

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

    private static string CurrentView(IRenderedComponent<WorkItemPage> page) => page.Find(".item-views .seg-o.is-sel input").GetAttribute("value")!;

    private static void View(IRenderedComponent<WorkItemPage> page, string view)
    {
        page.InvokeAsync(() => page.Find($".item-views input[value='{view}']").ChangeAsync(view)).GetAwaiter().GetResult();
        page.WaitForAssertion(() => Assert.Equal(view, CurrentView(page)));
    }

    private static IElement BugText(IRenderedComponent<WorkItemPage> page, int note) => page.Find($".tile[data-tile='subtasks'] .sub.bug[data-bug='{note}'] .s-t");

    private static int NewNote(string text, DateTime created, string? kind = null)
    {
        (bool ok, _, int id) = NotesApi.Create(123, "Tester", created, kind);
        Assert.True(ok);
        Assert.True(NotesApi.SaveBody(id, text).Success);
        return id;
    }

    private static IElement BugLink(IRenderedComponent<WorkItemPage> page, int note) => page.Find($".tile[data-tile='subtasks'] .sub.bug[data-bug='{note}'] button.sub-from");

    private static void OpensTheWorklogAt(BunitContext ctx, IRenderedComponent<WorkItemPage> page, int note, bool byLink = false)
    {
        page.InvokeAsync(() => (byLink ? BugLink(page, note) : BugText(page, note)).Click()).GetAwaiter().GetResult();
        page.WaitForAssertion(() => Assert.Equal("worklog", CurrentView(page)));
        page.WaitForAssertion(() => Assert.Contains("flash", page.Find($".wk-e[data-note='{note}']").ClassList));
        page.WaitForAssertion(() => Assert.Equal($".wk-e[data-note='{note}']", ctx.JSInterop.Invocations["rtm.scrollToDay"].Last().Arguments[0]));
        Assert.Single(page.FindAll(".wk-e.flash"));
    }

    [Fact(DisplayName = "Clicking a bug in the bug list opens the worklog at that bug's note")]
    public async Task ClickingABugOpensItsNote()
    {
        await using BunitContext ctx = NewContext();
        NewNote("Ping route times out under load", DateTime.Now.AddDays(-9));
        int bug = NewNote("The health route answers 500 after a restart\nSeen twice on the staging box.", DateTime.Now.AddDays(-4), NoteKinds.Bug);
        NewNote("Build hash is empty in release builds", DateTime.Now.AddDays(-1), NoteKinds.Bug);
        IRenderedComponent<WorkItemPage> page = ctx.Render<WorkItemPage>(p => p.Add(c => c.Id, 123));
        View(page, "linked");

        OpensTheWorklogAt(ctx, page, bug);
    }

    [Fact(DisplayName = "A note converted to a bug can be reached from the bug list")]
    public async Task ConvertedNoteCanBeReachedFromTheBugList()
    {
        await using BunitContext ctx = NewContext();
        int old = NewNote("Retry count is read from the wrong key", DateTime.Now.AddDays(-12));
        NewNote("Unrelated note from last week", DateTime.Now.AddDays(-7));
        int resolved = NewNote("Old typo in the route name", DateTime.Now.AddDays(-6), NoteKinds.Bug);
        Assert.True(NotesApi.SetResolved(resolved, true).Success);
        IRenderedComponent<WorkItemPage> page = ctx.Render<WorkItemPage>(p => p.Add(c => c.Id, 123));
        View(page, "linked");
        Assert.Empty(page.FindAll(".tile[data-tile='subtasks'] .sub.bug"));

        // the note becomes a bug while the page is open: it joins the list, and its text opens the note
        Assert.True(NotesApi.ConvertKind(old, NoteKinds.Bug).Success);
        ctx.Services.GetRequiredService<Workspace>().Notify();
        page.Eventually(() => Assert.Single(page.FindAll(".tile[data-tile='subtasks'] .sub.bug")));
        Assert.Empty(page.FindAll($".sub.bug[data-bug='{resolved}']"));

        OpensTheWorklogAt(ctx, page, old);
    }

    [Fact(DisplayName = "A bug row names its note as a link, like a subtask made from a note, and the click opens the worklog at that note")]
    public async Task BugRowLinksToItsNote()
    {
        await using BunitContext ctx = NewContext();
        int promoted = NewNote("Retry count is read from the wrong key", DateTime.Now.AddDays(-5));
        int bug = NewNote("Health route answers 500 after a restart", DateTime.Now.AddDays(-2), NoteKinds.Bug);
        Assert.True(NotesApi.ConvertToSubtask(promoted).Success);   // a subtask made from a note keeps its "from #nN" link beside the bug
        IRenderedComponent<WorkItemPage> page = ctx.Render<WorkItemPage>(p => p.Add(c => c.Id, 123));
        View(page, "linked");

        Assert.Equal($"from #n{promoted}", page.Find($".tile[data-tile='subtasks'] button.sub-from:not(.mono)").TextContent.Trim());
        Assert.Equal($"#n{bug}", BugLink(page, bug).TextContent.Trim());   // the number the row ends in is the way to the note
        Assert.Empty(page.FindAll($".sub.bug[data-bug='{bug}'] span.mono"));   // one place for it, not a label and a link

        OpensTheWorklogAt(ctx, page, bug, byLink: true);
    }
}
