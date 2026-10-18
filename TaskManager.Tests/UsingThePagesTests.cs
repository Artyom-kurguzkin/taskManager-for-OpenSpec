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

    [Fact(DisplayName = "Opening a work item shows its header, description, subtasks and notes")]
    public async Task WorkItemPage()
    {
        await using BunitContext ctx = NewContext();
        IRenderedComponent<WorkItemPage> page = ctx.Render<WorkItemPage>(p => p.Add(c => c.Id, 123));

        Assert.Equal("Health-check endpoint", page.Find("input.title-in").GetAttribute("value"));
        Assert.Contains("In Progress", page.Find(".status-field").TextContent);
        Assert.Contains("Expose a lightweight /health endpoint", page.Find(".desc").TextContent);
        Assert.Equal(4, page.FindAll(".sub").Count);
        Assert.Contains("Ping test", page.Find(".notes-list").TextContent);
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

    [Fact(DisplayName = "Adding a note from a work item opens the new note")]
    public async Task AddNoteOpensIt()
    {
        await using BunitContext ctx = NewContext();
        IRenderedComponent<WorkItemPage> page = ctx.Render<WorkItemPage>(p => p.Add(c => c.Id, 107));

        page.FindAll("button").Single(b => b.TextContent.Contains("Add note")).Click();

        Assert.EndsWith("/note/124", ctx.Services.GetRequiredService<Microsoft.AspNetCore.Components.NavigationManager>().Uri);
    }

    [Fact(DisplayName = "The description shows formatted text until you click to edit it")]
    public async Task DescriptionRendered()
    {
        await using BunitContext ctx = NewContext();
        IRenderedComponent<WorkItemPage> page = ctx.Render<WorkItemPage>(p => p.Add(c => c.Id, 123));

        Assert.Equal("Why", page.Find(".desc h2").TextContent.Trim());
        Assert.DoesNotContain("## Why", page.Find(".desc").TextContent);
    }

    [Fact(DisplayName = "A completed (archived) work item is read-only on its page")]
    public async Task ArchivedReadOnly()
    {
        await using BunitContext ctx = NewContext();
        IRenderedComponent<WorkItemPage> page = ctx.Render<WorkItemPage>(p => p.Add(c => c.Id, 124));

        Assert.Empty(page.FindAll(".chip.x"));
        Assert.Empty(page.FindAll("button.dd"));            // no status/priority/feature/contributor pickers
        Assert.Empty(page.FindAll("form.add-line"));
        Assert.Contains("Completed", page.Find(".status-field").TextContent);
        Assert.DoesNotContain("click to edit", page.Markup);
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

    [Fact(DisplayName = "Without git, the change log says the folder is not a git repository")]
    public async Task NoGitHistory()
    {
        await using BunitContext ctx = NewContext();
        IRenderedComponent<WorkItemPage> page = ctx.Render<WorkItemPage>(p => p.Add(c => c.Id, 123));
        Assert.Contains("Not a git repository", page.Find(".changelog").TextContent);
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
