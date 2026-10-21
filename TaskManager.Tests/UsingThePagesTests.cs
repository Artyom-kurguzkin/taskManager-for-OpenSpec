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

    private BunitContext NewContext()
    {
        BunitContext ctx = new();
        ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        ctx.Services.AddSingleton<UiService>();
        ctx.Services.AddSingleton(new SettingsStore(settingsFile));
        ctx.Services.AddSingleton<Workspace>();
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
        Assert.Contains("openspec/changes/add-health-check", page.Markup.Replace('\\', '/'));
    }

    [Fact(DisplayName = "Commit authors show as contributors without a remove button; people assigned in the app can be removed")]
    public async Task GitContributors()
    {
        await using BunitContext ctx = NewContext();
        try
        {
            WorkItemsApi.ConfigureContributors(() => ("t1", folder => folder == "add-health-check" ? ["Alice"] : []));
            IRenderedComponent<WorkItemPage> page = ctx.Render<WorkItemPage>(p => p.Add(c => c.Id, 123));
            List<AngleSharp.Dom.IElement> chips = page.FindAll(".kv:has(label) .chip").Where(c => c.ParentElement!.TextContent.Contains("contributors")).ToList();

            Assert.Equal(["Alice", "Artyom", "Tom"], chips.Select(c => c.TextContent.Trim()));
            Assert.DoesNotContain("x", chips[0].ClassList);   // Alice has commits: not removable
            Assert.Contains("x", chips[1].ClassList);

            chips[1].Click();   // unassign Artyom
            Assert.Equal(["Tom"], WorkItemsApi.Get(123)!.Assigned);
        }
        finally { WorkItemsApi.ConfigureContributors(null); }
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

    [Fact(DisplayName = "Deleting from the work item page hides it, and Restore brings it back")]
    public async Task HideFromPage()
    {
        await using BunitContext ctx = NewContext();
        IRenderedComponent<WorkItemPage> page = ctx.Render<WorkItemPage>(p => p.Add(c => c.Id, 107));

        page.Find("button.hide-item").Click();
        Assert.True(WorkItemsApi.Get(107)!.Hidden);
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
        Assert.Contains("click to edit", page.Markup);
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
        Assert.Contains($"#wi{newId}", page.Find(".warning.waiting-on").TextContent);

        IRenderedComponent<WorkItemPage> dependency = ctx.Render<WorkItemPage>(p => p.Add(c => c.Id, newId));
        Assert.Contains("#wi123", dependency.Find(".blocking").TextContent);
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
