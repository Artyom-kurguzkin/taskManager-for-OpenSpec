using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Notes;
using TaskManager.Components.Pages;
using TaskManager.Components.Shared;
using TaskManager.Services;
using WorkItems;

namespace TaskManager.Tests;

[Collection(nameof(ModuleStateCollection))]
public sealed class UsingThePagesTests : IDisposable
{
    private readonly TestRepo repo = new();
    private readonly string settingsFile = Path.Combine(Path.GetTempPath(), "rtm-ui-" + Guid.NewGuid().ToString("N") + ".json");
    private IRenderedComponent<Overlays>? overlays;

    private BunitContext NewContext(EditorLauncher? launcher = null)
    {
        BunitContext ctx = new();
        ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        ctx.Services.AddSingleton<UiService>();
        ctx.Services.AddSingleton(new SettingsStore(settingsFile));
        ctx.Services.AddSingleton<Workspace>();
        if (launcher is not null) ctx.Services.AddSingleton(launcher);
        Assert.True(ctx.Services.GetRequiredService<Workspace>().Open(repo.Root).Success);
        overlays = ctx.Render<Overlays>();
        return ctx;
    }

    // ── Work item page ──

    [Fact(DisplayName = "Opening a work item shows its header, description, subtasks, and its notes as the change log")]
    public async Task WorkItemPage()
    {
        await using BunitContext ctx = NewContext();
        IRenderedComponent<WorkItemPage> page = ctx.Render<WorkItemPage>(p => p.Add(c => c.Id, 123));

        Assert.Equal("Health-check endpoint", page.Find("input.title-in").GetAttribute("value"));
        Assert.Contains("In Progress", page.Find(".status-field").TextContent);
        Assert.Contains("Expose a lightweight /health endpoint", page.Find(".desc").TextContent);
        Assert.Equal(4, page.FindAll(".sub").Count);
        Assert.Contains("Ping test", page.Find(".change-log").TextContent);
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

            alice.Click();
            Assert.Empty(WorkItemsApi.Get(125)!.Contributors);
        }
        finally { WorkItemsApi.ConfigureContributors(null); }
    }

    [Fact(DisplayName = "+ add on contributors accepts a name typed in, not only people already on other items")]
    public async Task AddNewPerson()
    {
        await using BunitContext ctx = NewContext();
        IRenderedComponent<WorkItemPage> page = ctx.Render<WorkItemPage>(p => p.Add(c => c.Id, 107));

        page.Find("button[aria-label='Add a contributor']").Click();
        page.Find(".contributors .menu-filter").Input("Zed");
        page.FindAll(".contributors .menu-item").Single(i => i.TextContent.Contains("Zed")).Click();

        Assert.Equal(["Tom", "Zed"], WorkItemsApi.Get(107)!.Contributors);
    }

    [Fact(DisplayName = "Ticking a subtask on the work item page saves it to tasks.md")]
    public async Task TickOnPage()
    {
        await using BunitContext ctx = NewContext();
        IRenderedComponent<WorkItemPage> page = ctx.Render<WorkItemPage>(p => p.Add(c => c.Id, 123));
        page.Find("#sub-2-1").Change(true);
        Assert.Contains("- [x] 2.1", repo.Read("openspec/changes/add-health-check/tasks.md"));
    }

    [Fact(DisplayName = "Selecting two subtasks offers moving them into a separate work item")]
    public async Task SplitFromPage()
    {
        await using BunitContext ctx = NewContext();
        IRenderedComponent<WorkItemPage> page = ctx.Render<WorkItemPage>(p => p.Add(c => c.Id, 123));
        page.FindAll(".sub .s-t")[2].Click();
        page.FindAll(".sub .s-t")[3].Click();

        page.Find(".actionbar button.primary").Click();

        Assert.Equal(2, WorkItemsApi.Get(123)!.Subtasks.Count);
        Assert.Contains(WorkItemsApi.GetAll(), w => w.Title == "Split from #wi123");
    }

    [Fact(DisplayName = "A hidden item's page offers Restore; a visible item's page does not offer hiding")]
    public async Task HideFromPage()
    {
        await using BunitContext ctx = NewContext();
        IRenderedComponent<WorkItemPage> visible = ctx.Render<WorkItemPage>(p => p.Add(c => c.Id, 107));
        Assert.Empty(visible.FindAll("button.hide-item"));   // hiding only happens from the board's selection bar

        WorkItemsApi.Hide(107);
        IRenderedComponent<WorkItemPage> page = ctx.Render<WorkItemPage>(p => p.Add(c => c.Id, 107));
        page.Find("button.hide-item").Click();
        Assert.False(WorkItemsApi.Get(107)!.Hidden);
    }

    [Fact(DisplayName = "+ Add entry in the change log creates a note and opens it")]
    public async Task AddNoteOpensIt()
    {
        await using BunitContext ctx = NewContext();
        IRenderedComponent<WorkItemPage> page = ctx.Render<WorkItemPage>(p => p.Add(c => c.Id, 107));

        page.FindAll("button").Single(b => b.TextContent.Contains("Add entry")).Click();

        Assert.EndsWith("/note/124", ctx.Services.GetRequiredService<Microsoft.AspNetCore.Components.NavigationManager>().Uri);
    }

    [Fact(DisplayName = "The change log lists the newest note first, with its date and author")]
    public async Task ChangeLogNewestFirst()
    {
        await using BunitContext ctx = NewContext();
        (bool _, string _, int id) = NotesApi.Create(123, "Tom");
        IRenderedComponent<WorkItemPage> page = ctx.Render<WorkItemPage>(p => p.Add(c => c.Id, 123));

        List<AngleSharp.Dom.IElement> entries = page.FindAll(".change-log .log-e").ToList();

        Assert.Equal(2, entries.Count);
        Assert.Contains($"#n{id}", entries[0].TextContent);
        Assert.Contains("Tom", entries[0].TextContent);
        Assert.Contains("#n123", entries[1].TextContent);
    }

    [Fact(DisplayName = "The description shows formatted text until you click to edit it")]
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
        Assert.Empty(page.FindAll(".status-field button.dd"));
        // Other fields are editable: priority, label, feature and contributor dropdowns
        Assert.NotEmpty(page.FindAll("button.dd"));
        Assert.NotEmpty(page.FindAll(".desc.edit"));
    }

    [Fact(DisplayName = "Typing never re-writes the editor's text, so fast typing can't be overwritten")]
    public async Task EditorIsNotRewrittenWhileTyping()
    {
        await using BunitContext ctx = NewContext();
        IRenderedComponent<NotePage> page = ctx.Render<NotePage>(p => p.Add(c => c.Id, 123));
        string loaded = page.Find("textarea.md").GetAttribute("value") ?? page.Find("textarea.md").TextContent;

        page.Find("textarea.md").Input(loaded + "abc");
        page.Find("textarea.md").Input(loaded + "abcdef");

        string now = page.Find("textarea.md").GetAttribute("value") ?? page.Find("textarea.md").TextContent;
        Assert.Equal(loaded, now);   // the DOM keeps what the user typed; the app doesn't push its copy back
    }

    [Fact(DisplayName = "A note's own autosave doesn't reload the page under your cursor")]
    public async Task AutosaveDoesNotReload()
    {
        await using BunitContext ctx = NewContext();
        int changes = 0;
        ctx.Services.GetRequiredService<Workspace>().Changed += () => changes++;
        IRenderedComponent<NotePage> page = ctx.Render<NotePage>(p => p.Add(c => c.Id, 123));

        page.Find("textarea.md").Input("# Ping test\n\nfirst");
        await Task.Delay(1200);   // autosave has run

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
        page.Find(".edit-ext").Click();

        Assert.Equal("code", launchedEditor);
        Assert.Contains("n123.md", launchedPath);
    }

    [Fact(DisplayName = "Edit in editor asks which editor to use the first time, then remembers it in settings")]
    public async Task EditInEditorAsksFirstTime()
    {
        string? launchedEditor = null;
        await using BunitContext ctx = NewContext((editor, path) => { launchedEditor = editor; return (true, ""); });
        IRenderedComponent<NotePage> page = ctx.Render<NotePage>(p => p.Add(c => c.Id, 123));

        Assert.Equal("Edit in…", page.Find(".edit-ext").TextContent.Trim());
        page.Find(".edit-ext").Click();

        overlays!.WaitForAssertion(() => Assert.Contains("editor", overlays.Find(".dialog").TextContent, StringComparison.OrdinalIgnoreCase));
        overlays.Find(".dialog input").Input("notepad++");
        overlays.Find(".dialog button.primary").Click();

        overlays.WaitForAssertion(() => Assert.Equal("notepad++", launchedEditor));
        Assert.Equal("notepad++", ctx.Services.GetRequiredService<SettingsStore>().Current.Editor);
    }

    [Fact(DisplayName = "Choosing Completed asks before archiving, and Cancel leaves the item as it was")]
    public async Task CompleteAsksFirst()
    {
        await using BunitContext ctx = NewContext();
        IRenderedComponent<WorkItemPage> page = ctx.Render<WorkItemPage>(p => p.Add(c => c.Id, 107));

        page.Find(".status-field button.dd").Click();
        page.FindAll(".menu-item").First(i => i.TextContent.Trim() == "Completed").Click();

        overlays!.WaitForAssertion(() => Assert.Contains("Complete and archive?", overlays.Find(".dialog").TextContent));
        Assert.Empty(page.FindAll(".menu-item"));   // the menu closed before the dialog opened
        overlays.FindAll(".dialog button").Single(b => b.TextContent.Trim() == "Cancel").Click();

        overlays.WaitForAssertion(() => Assert.Empty(overlays.FindAll(".dialog")));
        Assert.Equal("Planned", WorkItemsApi.Get(107)!.Status);
    }

    [Fact(DisplayName = "Without git, Linked commits says the folder is not a git repository")]
    public async Task NoGitHistory()
    {
        await using BunitContext ctx = NewContext();
        IRenderedComponent<WorkItemPage> page = ctx.Render<WorkItemPage>(p => p.Add(c => c.Id, 123));
        Assert.Contains("Not a git repository", page.Find(".linked-commits").TextContent);
    }

    [Fact(DisplayName = "Selecting one subtask offers to make it a work item of its own")]
    public async Task SplitOneSubtask()
    {
        await using BunitContext ctx = NewContext();
        IRenderedComponent<WorkItemPage> page = ctx.Render<WorkItemPage>(p => p.Add(c => c.Id, 123));

        page.FindAll(".sub .s-t")[2].Click();
        Assert.Equal("Make it a work item", page.Find(".actionbar button.primary").TextContent.Trim());
        page.Find(".actionbar button.primary").Click();

        Assert.Equal(3, WorkItemsApi.Get(123)!.Subtasks.Count);
        Assert.Contains(WorkItemsApi.GetAll(), w => w.Subtasks is [{ Number: "1.1", Text: "Ping test in suite 543" }]);
    }

    [Fact(DisplayName = "Selecting a subtask also offers to make it a dependency, which blocks the parent and links the two pages")]
    public async Task SplitAsDependency()
    {
        await using BunitContext ctx = NewContext();
        IRenderedComponent<WorkItemPage> page = ctx.Render<WorkItemPage>(p => p.Add(c => c.Id, 123));

        page.FindAll(".sub .s-t")[2].Click();
        page.Find(".actionbar button.dep").Click();

        Assert.Equal("Blocked", WorkItemsApi.Get(123)!.Status);
        int newId = WorkItemsApi.GetAll().Single(w => w.Subtasks is [{ Number: "1.1", Text: "Ping test in suite 543" }]).Id;
        Assert.Contains($"#wi{newId}", page.Find(".sub.dep.waits.blocking-me").TextContent);

        IRenderedComponent<WorkItemPage> dependency = ctx.Render<WorkItemPage>(p => p.Add(c => c.Id, newId));
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

        page.Find("input[aria-label='New subtask']").Change(typed);
        page.Find("form.add-line").Submit();

        Assert.Equal([123], WorkItemsApi.Get(107)!.DependsOn);
        Assert.Equal(2, WorkItemsApi.Get(107)!.Subtasks.Count);
        Assert.Contains("#wi123", page.Find(".sub.dep.waits.blocking-me").TextContent);
    }

    [Fact(DisplayName = "Dependencies are listed under Subtasks: a done one struck through, and × removes it")]
    public async Task DependenciesUnderSubtasks()
    {
        await using BunitContext ctx = NewContext();
        WorkItemsApi.AddDependency(107, 124);   // archived, so done
        IRenderedComponent<WorkItemPage> page = ctx.Render<WorkItemPage>(p => p.Add(c => c.Id, 107));

        AngleSharp.Dom.IElement row = page.Find(".panel .sub.dep.waits");
        Assert.Contains("#wi124", row.TextContent);
        Assert.Contains("done", row.ClassList);

        row.QuerySelector("button[aria-label='Remove dependency']")!.Click();

        Assert.Empty(WorkItemsApi.Get(107)!.DependsOn!);
        Assert.Empty(page.FindAll(".sub.dep"));
    }

    [Fact(DisplayName = "+ link commit lists recent commits; the picked one is linked and can be removed")]
    public async Task LinkCommit()
    {
        GitInit();
        await using BunitContext ctx = NewContext();
        IRenderedComponent<WorkItemPage> page = ctx.Render<WorkItemPage>(p => p.Add(c => c.Id, 123));

        page.Find("button[aria-label='Link a commit']").Click();
        page.FindAll(".menu-item").Single(i => i.TextContent.Contains("Fix uptime probe")).Click();

        Assert.Single(WorkItemsApi.Get(123)!.Commits!);
        AngleSharp.Dom.IElement linked = page.FindAll(".linked-commits .commit").Single(c => c.TextContent.Contains("Fix uptime probe"));
        linked.QuerySelector("button[aria-label='Unlink commit']")!.Click();
        Assert.Empty(WorkItemsApi.Get(123)!.Commits ?? []);
    }

    [Fact(DisplayName = "+ link test lists the tests in the newest results file; a linked test shows its result and can get a note")]
    public async Task LinkTest()
    {
        repo.Write("results/run.trx", ReadingLinkedTestRunsTests.Trx);
        await using BunitContext ctx = NewContext();
        ReadingLinkedTestRunsTests.UseResults(repo, "results/*.trx");
        IRenderedComponent<WorkItemPage> page = ctx.Render<WorkItemPage>(p => p.Add(c => c.Id, 123));

        page.Find("button[aria-label='Link a test']").Click();
        Assert.Equal(2, page.FindAll(".menu-item").Count);
        page.Find(".menu-filter").Input("ping");   // long lists: type to narrow
        page.FindAll(".menu-item").Single().Click();

        AngleSharp.Dom.IElement row = page.FindAll(".linked-tests .test").Single(t => t.TextContent.Contains("Ping test"));
        Assert.Contains("passed", row.ClassName);
        row.QuerySelector("button[aria-label='Add a note for this test']")!.Click();
        Assert.NotNull(WorkItemsApi.Get(123)!.Tests!.Single().Note);
        Assert.Matches("/note/[0-9]+$", ctx.Services.GetRequiredService<Microsoft.AspNetCore.Components.NavigationManager>().Uri);
    }

    // Makes the temp repo a git repository with one commit, before the workspace opens it.
    private void GitInit()
    {
        foreach (string[] args in new[]
        {
            new[] { "init", "-q" }, ["config", "user.name", "Tester"], ["config", "user.email", "t@example.com"], ["config", "commit.gpgsign", "false"],
            ["add", "-A"], ["commit", "-q", "-m", "Fix uptime probe"],
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

        page.Find("textarea.md").Input("# Ping test\n\nSee [[runbook/uptime]] and #wi999");

        page.WaitForAssertion(() =>
        {
            Assert.Contains("runbook/uptime", page.Find(".links").TextContent);
            Assert.Contains("unresolved", page.Find(".links").TextContent);
        });
    }

    [Fact(DisplayName = "A note is saved automatically shortly after typing stops")]
    public async Task NoteAutosave()
    {
        await using BunitContext ctx = NewContext();
        IRenderedComponent<NotePage> page = ctx.Render<NotePage>(p => p.Add(c => c.Id, 123));
        page.Find("textarea.md").Input("# Ping test\n\nAutosaved text.\n");

        await Task.Delay(1500);

        Assert.Contains("Autosaved text.", repo.Read("openspec/changes/add-health-check/notes/n123.md"));
        Assert.Equal("# Ping test\n\nAutosaved text.\n", NotesApi.Get(123)!.Body);
    }

    // ── Settings ──

    [Fact(DisplayName = "Choosing Dark in Settings saves the theme")]
    public async Task ThemeSetting()
    {
        await using BunitContext ctx = NewContext();
        IRenderedComponent<SettingsPage> page = ctx.Render<SettingsPage>();
        page.Find("input[name=theme][value=dark]").Change("dark");
        Assert.Equal("dark", ctx.Services.GetRequiredService<SettingsStore>().Current.Theme);
    }

    [Fact(DisplayName = "Opening a folder without openspec/ in Settings shows how to fix it")]
    public async Task OpenWrongFolder()
    {
        await using BunitContext ctx = NewContext();
        IRenderedComponent<SettingsPage> page = ctx.Render<SettingsPage>();
        string empty = Directory.CreateTempSubdirectory("rtm-empty").FullName;

        page.Find("input#f-repo").Change(empty);
        page.Find("button.open-repo").Click();

        Assert.Contains("No openspec/ folder here", page.Markup);
    }

    public void Dispose()
    {
        repo.Dispose();
        File.Delete(settingsFile);
    }
}
