using Radzen.Blazor;
using Radzen;
using AngleSharp.Dom;
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
        ctx.Services.AddRadzenComponents();   // Radzen components ask for its services (the markdown editor wants DialogService)
        ctx.Services.AddSingleton(new SettingsStore(settingsFile));
        ctx.Services.AddSingleton<Workspace>();
        if (launcher is not null) ctx.Services.AddSingleton(launcher);
        Assert.True(ctx.Services.GetRequiredService<Workspace>().Open(repo.Root).Success);
        overlays = ctx.Render<Overlays>();
        return ctx;
    }

    // ── Work item page ──

    // The page is a bento of equal tiles (description, subtasks, blocks, commits, tests, change log). One is focused:
    // large and fully editable. The others are compact previews; clicking one focuses it.
    private static void Focus(IRenderedComponent<WorkItemPage> page, string tile) => page.Find($".tile[data-tile='{tile}'] .tile-h").Click();

    private static List<string> Tiles(IRenderedComponent<WorkItemPage> page) => page.FindAll(".tile").Select(t => t.GetAttribute("data-tile")!).ToList();

    [Fact(DisplayName = "The work item page is a bento of tiles with the description focused; 'blocks' only shows when something waits on it")]
    public async Task BentoTiles()
    {
        await using BunitContext ctx = NewContext();
        IRenderedComponent<WorkItemPage> page = ctx.Render<WorkItemPage>(p => p.Add(c => c.Id, 123));

        Assert.Equal(["description", "subtasks", "commits", "tests", "log"], Tiles(page));
        Assert.Equal("description", page.Find(".tile.focus").GetAttribute("data-tile"));
        Assert.Single(page.FindAll(".tile.focus .desc.edit"));

        WorkItemsApi.AddDependency(107, 123);
        IRenderedComponent<WorkItemPage> blocking = ctx.Render<WorkItemPage>(p => p.Add(c => c.Id, 123));
        Assert.Contains("blocks", Tiles(blocking));
        Assert.Contains("#wi107", blocking.Find(".tile[data-tile='blocks']").TextContent);
    }

    [Fact(DisplayName = "Clicking a compact tile focuses it; the description becomes a compact preview")]
    public async Task FocusTile()
    {
        await using BunitContext ctx = NewContext();
        IRenderedComponent<WorkItemPage> page = ctx.Render<WorkItemPage>(p => p.Add(c => c.Id, 123));
        Assert.Empty(page.FindAll(".sub"));   // compact: previews only

        Focus(page, "subtasks");

        Assert.Equal("subtasks", page.Find(".tile.focus").GetAttribute("data-tile"));
        Assert.Equal(4, page.FindAll(".tile.focus .sub").Count);
        Assert.Empty(page.FindAll(".desc.edit"));
        Assert.Contains("Expose a lightweight /health endpoint", page.Find(".tile[data-tile='description'] .peek-desc").TextContent);
    }

    [Fact(DisplayName = "In a narrow window the page opens with every tile minimised; tapping one expands it, tapping its header minimises it")]
    public async Task NarrowStartsMinimised()
    {
        await using BunitContext ctx = NewContext();
        ctx.JSInterop.Setup<bool>("rtm.isNarrow").SetResult(true);
        IRenderedComponent<WorkItemPage> page = ctx.Render<WorkItemPage>(p => p.Add(c => c.Id, 123));

        page.WaitForAssertion(() => Assert.Empty(page.FindAll(".tile.focus")));
        Assert.Contains("Expose a lightweight /health endpoint", page.Find(".tile[data-tile='description'] .peek-desc").TextContent);

        Focus(page, "subtasks");
        Assert.Equal("subtasks", page.Find(".tile.focus").GetAttribute("data-tile"));

        page.Find(".tile.focus .tile-h").Click();
        Assert.Empty(page.FindAll(".tile.focus"));
    }

    [Fact(DisplayName = "On the desktop, the focused tile's header does not minimise it: one tile is always open")]
    public async Task DesktopKeepsOneOpen()
    {
        await using BunitContext ctx = NewContext();
        IRenderedComponent<WorkItemPage> page = ctx.Render<WorkItemPage>(p => p.Add(c => c.Id, 123));

        page.Find(".tile.focus .tile-h").Click();

        Assert.Equal("description", page.Find(".tile.focus").GetAttribute("data-tile"));
    }

    [Fact(DisplayName = "A compact tile shows only its last few entries and a count; focused, it lists them all")]
    public async Task CompactShowsFew()
    {
        await using BunitContext ctx = NewContext();
        for (int i = WorkItemsApi.Get(123)!.Subtasks.Count; i < 25; i++) Assert.True(WorkItemsApi.AddSubtask(123, $"Extra step {i}").Success);
        IRenderedComponent<WorkItemPage> page = ctx.Render<WorkItemPage>(p => p.Add(c => c.Id, 123));

        Assert.Equal(3, page.FindAll(".tile[data-tile='subtasks'] .peek").Count);
        Assert.Contains("/25", page.Find(".tile[data-tile='subtasks'] .tile-h").TextContent);

        Focus(page, "subtasks");
        Assert.Equal(25, page.FindAll(".tile.focus .sub").Count);
    }

    [Fact(DisplayName = "A work item has one feature, picked in one dropdown: another pick replaces it, \"no feature\" leaves it unassigned, and its spec stays")]
    public async Task OneFeatureDropdown()
    {
        await using BunitContext ctx = NewContext();
        IRenderedComponent<WorkItemPage> page = ctx.Render<WorkItemPage>(p => p.Add(c => c.Id, 123));
        Assert.Empty(page.FindAll(".head.kvs .chip.spec"));
        Assert.Contains("Security", page.Find(".head.kvs [aria-label='feature'] .rz-dropdown-label").TextContent);   // picked by hand, so it wins over its spec

        page.FindAll("[aria-label='feature'] .rz-dropdown-item").Single(i => i.TextContent.Trim() == "Banking").Click();
        page.WaitForAssertion(() => Assert.Equal(["functional/banking"], WorkItemsApi.Get(123)!.Features.Select(f => f.Path)));

        page.FindAll("[aria-label='feature'] .rz-dropdown-item").Single(i => i.TextContent.Trim() == "no feature").Click();
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

        page.Find("button.dates-btn").Click();
        DateOnly first = new(DateTime.Today.Year, DateTime.Today.Month, 1);
        page.Find($".range-pop button.rd[data-day='{first:yyyy-MM-dd}']").Click();
        Assert.Null(WorkItemsApi.Get(123)!.Started);   // nothing saved until the range is complete
        page.Find($".range-pop button.rd[data-day='{first.AddDays(6):yyyy-MM-dd}']").Click();
        page.WaitForAssertion(() => Assert.Equal((first, first.AddDays(6)), (WorkItemsApi.Get(123)!.Started, WorkItemsApi.Get(123)!.Due)));
        page.WaitForAssertion(() => Assert.Contains($"{first:d MMM} – {first.AddDays(6):d MMM}", page.Find("button.dates-btn").TextContent));
        Assert.Empty(page.FindAll(".range-pop"));   // closed once the range is complete

        page.Find("button.dates-btn").Click();   // picking backwards swaps them
        page.Find($".range-pop button.rd[data-day='{first.AddDays(9):yyyy-MM-dd}']").Click();
        page.Find($".range-pop button.rd[data-day='{first.AddDays(2):yyyy-MM-dd}']").Click();
        page.WaitForAssertion(() => Assert.Equal((first.AddDays(2), first.AddDays(9)), (WorkItemsApi.Get(123)!.Started, WorkItemsApi.Get(123)!.Due)));

        page.Find("button.dates-btn").Click();
        page.Find(".range-pop button.range-clear").Click();
        page.WaitForAssertion(() => Assert.Null(WorkItemsApi.Get(123)!.Due));
        Assert.Null(WorkItemsApi.Get(123)!.Started);
    }

    [Fact(DisplayName = "A work item's plan is picked from the plans folder: the chip opens it rendered in the app, its × unlinks it; there is no path box")]
    public async Task PlanOnThePage()
    {
        await using BunitContext ctx = NewContext();
        repo.Write("DevResources/plans/health-2026-11-05.md", "# Health plan\n\nStep one.\n");
        repo.Write("DevResources/plans/other-2026-11-04.md", "# Other\n");
        IRenderedComponent<WorkItemPage> page = ctx.Render<WorkItemPage>(p => p.Add(c => c.Id, 123));
        Assert.Empty(page.FindAll("a.plan-link"));
        Assert.Empty(page.FindAll("input[aria-label='plan']"));

        page.Find("[aria-label='Link a plan'] button, button[aria-label='Link a plan']").Click();
        page.WaitForAssertion(() => Assert.Equal(2, page.FindAll(".menu-pop .pick-item").Count));
        page.FindAll(".menu-pop .pick-item").First(i => i.TextContent.Contains("health-2026-11-05")).Click();
        page.WaitForAssertion(() => Assert.Equal("DevResources/plans/health-2026-11-05.md", WorkItemsApi.Get(123)!.Plan));
        AngleSharp.Dom.IElement link = page.Find("a.plan-link");
        Assert.Equal("doc/DevResources/plans/health-2026-11-05.md", link.GetAttribute("href"));
        Assert.Equal("health-2026-11-05.md", link.TextContent.Trim());

        IRenderedComponent<DocPage> doc = ctx.Render<DocPage>(p => p.Add(c => c.Path, WorkItemsApi.Get(123)!.Plan!));
        Assert.Equal("Health plan", doc.Find("h1").TextContent);

        page.Find("button.plan-x").Click();
        page.WaitForAssertion(() => Assert.Null(WorkItemsApi.Get(123)!.Plan));
        Assert.Empty(page.FindAll("a.plan-link"));
    }

    [Fact(DisplayName = "Opening a work item shows its header, description, subtasks, and its notes as the change log")]
    public async Task WorkItemPage()
    {
        await using BunitContext ctx = NewContext();
        IRenderedComponent<WorkItemPage> page = ctx.Render<WorkItemPage>(p => p.Add(c => c.Id, 123));

        Assert.Equal("Health-check endpoint", page.Find("input.title-in").GetAttribute("value"));
        Assert.Contains("In Progress", page.Find(".status-field").TextContent);
        Assert.Contains("Expose a lightweight /health endpoint", page.Find(".desc").TextContent);
        Assert.Contains("Ping test", page.Find(".tile[data-tile='log']").TextContent);
        Focus(page, "subtasks");
        Assert.Equal(4, page.FindAll(".sub").Count);
        Focus(page, "log");
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
        Focus(page, "subtasks");
        page.Find("#sub-2-1").Change(true);
        Assert.Contains("- [x] 2.1", repo.Read("openspec/changes/add-health-check/tasks.md"));
    }

    [Fact(DisplayName = "Clicking a subtask's text edits it in place; Enter saves it to tasks.md")]
    public async Task EditSubtaskText()
    {
        await using BunitContext ctx = NewContext();
        IRenderedComponent<WorkItemPage> page = ctx.Render<WorkItemPage>(p => p.Add(c => c.Id, 123));
        Focus(page, "subtasks");

        page.FindAll(".sub .s-t")[3].Click();
        AngleSharp.Dom.IElement box = page.Find(".sub input.sub-edit");
        Assert.Equal("Hide internals from anonymous callers", box.GetAttribute("value"));
        box.Input("Hide internals from everyone");
        box.KeyDown("Enter");

        Assert.Contains("- [ ] 2.1 Hide internals from everyone", repo.Read("openspec/changes/add-health-check/tasks.md"));
        Assert.Empty(page.FindAll(".sub input.sub-edit"));
        Assert.Empty(page.FindAll(".actionbar"));   // a plain click edits; it does not select
    }

    [Fact(DisplayName = "Esc while editing a subtask leaves it as it was")]
    public async Task EditSubtaskCancel()
    {
        await using BunitContext ctx = NewContext();
        IRenderedComponent<WorkItemPage> page = ctx.Render<WorkItemPage>(p => p.Add(c => c.Id, 123));
        Focus(page, "subtasks");
        string before = repo.Read("openspec/changes/add-health-check/tasks.md");

        page.FindAll(".sub .s-t")[0].Click();
        page.Find(".sub input.sub-edit").Input("Something else");
        page.Find(".sub input.sub-edit").KeyDown("Escape");

        Assert.Empty(page.FindAll(".sub input.sub-edit"));
        Assert.Equal(before, repo.Read("openspec/changes/add-health-check/tasks.md"));
    }
    [Fact(DisplayName = "Selecting two subtasks offers moving them into a separate work item")]
    public async Task SplitFromPage()
    {
        await using BunitContext ctx = NewContext();
        IRenderedComponent<WorkItemPage> page = ctx.Render<WorkItemPage>(p => p.Add(c => c.Id, 123));
        Focus(page, "subtasks");
        page.FindAll(".sub .s-t")[2].Click(new Microsoft.AspNetCore.Components.Web.MouseEventArgs { ShiftKey = true });
        page.FindAll(".sub .s-t")[3].Click(new Microsoft.AspNetCore.Components.Web.MouseEventArgs { ShiftKey = true });

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

        Focus(page, "log");
        page.FindAll("button").Single(b => b.TextContent.Contains("Add entry")).Click();

        Assert.EndsWith("/note/124", ctx.Services.GetRequiredService<Microsoft.AspNetCore.Components.NavigationManager>().Uri);
    }

    [Fact(DisplayName = "The change log lists the newest note first, with its date and author")]
    public async Task ChangeLogNewestFirst()
    {
        await using BunitContext ctx = NewContext();
        (bool _, string _, int id) = NotesApi.Create(123, "Tom");
        IRenderedComponent<WorkItemPage> page = ctx.Render<WorkItemPage>(p => p.Add(c => c.Id, 123));

        Focus(page, "log");
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
        Assert.Empty(page.FindAll(".status-field .dd"));
        // Other fields are editable: priority, label, feature and contributor dropdowns
        Assert.NotEmpty(page.FindAll(".dd"));
        Assert.NotEmpty(page.FindAll(".desc.edit"));
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
        Focus(page, "subtasks");
        page.Find(".sub .agent-chip").Click();
        page.WaitForAssertion(() => Assert.Empty(page.FindAll(".sub .agent-chip")));
        Focus(page, "log");
        page.Find(".change-log .agent-chip").Click();
        page.WaitForAssertion(() => Assert.Empty(page.FindAll(".change-log .agent-chip")));
        page.Find(".contributors .agent-contrib").Click();
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

        page.Find("input.title-in").Change("Health-check endpoint v2");

        overlays!.WaitForAssertion(() => Assert.Equal("Saved", overlays.Find(".toast.success").TextContent.Trim()));
        Assert.Equal("Health-check endpoint v2", WorkItemsApi.Get(123)!.Title);
    }

    // The note opens as a rendered preview; Edit swaps in the markdown editor (Radzen's), Done goes back. Radzen's panes are
    // driven by its script, so a test types by raising the editor's ValueChanged, which is what typing does.
    private static Task Type(IRenderedComponent<NotePage> page, string text)
    {
        if (page.FindAll(".rz-markdown-editor").Count == 0) page.Find(".note-view").Click();
        RadzenMarkdownEditor editor = page.FindComponent<RadzenMarkdownEditor>().Instance;
        return page.InvokeAsync(() => editor.ValueChanged.InvokeAsync(text));
    }

    [Fact(DisplayName = "The note page edits in place: the title is an input, clicking the note opens the editor, focusing out saves and shows the rendered note; a pasted image becomes an attachment")]
    public async Task NoteEditsInPlace()
    {
        await using BunitContext ctx = NewContext();
        IRenderedComponent<NotePage> page = ctx.Render<NotePage>(p => p.Add(c => c.Id, 123));

        page.Find("input.note-title").Change("Decided on X");
        Assert.Equal("Decided on X", NotesApi.Get(123)!.Title);

        Assert.Empty(page.FindAll("button.edit-note"));
        Assert.Empty(page.FindAll("button.done-note"));
        Assert.Equal("Click to edit", page.Find(".note-view").GetAttribute("title"));
        Assert.Empty(page.FindAll(".rz-markdown-editor"));

        page.Find(".note-view").Click();
        Assert.NotEmpty(page.FindAll(".rz-markdown-editor"));
        await Type(page, "# Decided on X\n\nchanged in test");
        page.Find(".note-editor").FocusOut();

        Assert.Empty(page.FindAll(".rz-markdown-editor"));
        Assert.Contains("changed in test", page.Find(".note-view").TextContent);
        Assert.Contains("changed in test", NotesApi.Get(123)!.Body);
        Assert.Contains("changed in test", repo.Read("openspec/changes/add-health-check/notes/n123.md"));

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
        page.Find(".note-view").Click();
        string loaded = page.FindComponent<RadzenMarkdownEditor>().Instance.Value;

        await Type(page, loaded + "abc");
        await Type(page, loaded + "abcdef");

        Assert.Equal(loaded + "abcdef", page.FindComponent<RadzenMarkdownEditor>().Instance.Value);
    }

    [Fact(DisplayName = "A note's own autosave doesn't reload the page under your cursor, and says Saved once it is on disk")]
    public async Task AutosaveDoesNotReload()
    {
        await using BunitContext ctx = NewContext();
        int changes = 0;
        ctx.Services.GetRequiredService<Workspace>().Changed += () => changes++;
        IRenderedComponent<NotePage> page = ctx.Render<NotePage>(p => p.Add(c => c.Id, 123));

        await Type(page, "# Ping test\n\nfirst");
        await Task.Delay(1200);   // autosave has run

        Assert.Equal(0, changes);
        Assert.Contains("first", repo.Read("openspec/changes/add-health-check/notes/n123.md"));
        overlays!.WaitForAssertion(() => Assert.Contains("Saved", overlays.Find(".toast.success").TextContent));
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

        page.Find(".status-field .dd").Click();
        page.FindAll(".status-field .rz-dropdown-item").First(i => i.TextContent.Trim() == "Completed").Click();

        overlays!.WaitForAssertion(() => Assert.Contains("Complete and archive?", overlays.Find(".dialog").TextContent));
        overlays.FindAll(".dialog button").Single(b => b.TextContent.Trim() == "Cancel").Click();

        overlays.WaitForAssertion(() => Assert.Empty(overlays.FindAll(".dialog")));
        Assert.Equal("Planned", WorkItemsApi.Get(107)!.Status);
    }

    [Fact(DisplayName = "Without git, Linked commits says the folder is not a git repository")]
    public async Task NoGitHistory()
    {
        await using BunitContext ctx = NewContext();
        IRenderedComponent<WorkItemPage> page = ctx.Render<WorkItemPage>(p => p.Add(c => c.Id, 123));
        Focus(page, "commits");
        Assert.Contains("Not a git repository", page.Find(".linked-commits").TextContent);
    }

    [Fact(DisplayName = "Selecting one subtask offers to make it a work item of its own")]
    public async Task SplitOneSubtask()
    {
        await using BunitContext ctx = NewContext();
        IRenderedComponent<WorkItemPage> page = ctx.Render<WorkItemPage>(p => p.Add(c => c.Id, 123));

        Focus(page, "subtasks");
        page.FindAll(".sub .s-t")[2].Click(new Microsoft.AspNetCore.Components.Web.MouseEventArgs { ShiftKey = true });
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

        Focus(page, "subtasks");
        page.FindAll(".sub .s-t")[2].Click(new Microsoft.AspNetCore.Components.Web.MouseEventArgs { ShiftKey = true });
        page.Find(".actionbar button.dep").Click();

        Assert.Equal("Blocked", WorkItemsApi.Get(123)!.Status);
        int newId = WorkItemsApi.GetAll().Single(w => w.Subtasks is [{ Number: "1.1", Text: "Ping test in suite 543" }]).Id;
        Assert.Contains($"#wi{newId}", page.Find(".sub.dep.waits.blocking-me").TextContent);

        IRenderedComponent<WorkItemPage> dependency = ctx.Render<WorkItemPage>(p => p.Add(c => c.Id, newId));
        Focus(dependency, "blocks");
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

        Focus(page, "subtasks");
        page.Find("input[aria-label='New subtask']").Change(typed);
        page.Find("form.add-line").Submit();

        Assert.Equal([123], WorkItemsApi.Get(107)!.DependsOn);
        Assert.Equal(2, WorkItemsApi.Get(107)!.Subtasks.Count);
        Assert.Contains("#wi123", page.Find(".sub.dep.waits.blocking-me").TextContent);
    }

    [Fact(DisplayName = "The Subtasks tile has a picker of existing work items to wait on; choosing one makes it a dependency")]
    public async Task PickDependency()
    {
        await using BunitContext ctx = NewContext();
        IRenderedComponent<WorkItemPage> page = ctx.Render<WorkItemPage>(p => p.Add(c => c.Id, 107));
        Focus(page, "subtasks");

        page.Find("button[aria-label='Wait on another work item']").Click();
        page.Find(".menu-filter").Input("health");
        page.FindAll(".menu-item").Single().Click();

        Assert.Equal([123], WorkItemsApi.Get(107)!.DependsOn);
        Assert.Equal("Blocked", WorkItemsApi.Get(107)!.Status);
        Assert.Contains("#wi123", page.Find(".sub.dep.waits").TextContent);
    }

    [Fact(DisplayName = "The dependency picker leaves out the item itself, ones it already waits on and ones that wait on it")]
    public async Task DependencyPickerChoices()
    {
        await using BunitContext ctx = NewContext();
        WorkItemsApi.AddDependency(107, 123);   // 107 waits on 123
        IRenderedComponent<WorkItemPage> page = ctx.Render<WorkItemPage>(p => p.Add(c => c.Id, 123));
        Focus(page, "subtasks");

        page.Find("button[aria-label='Wait on another work item']").Click();
        string choices = string.Join(" | ", page.FindAll(".menu-item").Select(i => i.TextContent));

        Assert.DoesNotContain("#wi123", choices);   // itself
        Assert.DoesNotContain("#wi107", choices);   // it already waits on 123: that would be a loop
        Assert.Contains("#wi125", choices);
    }

    [Fact(DisplayName = "Dependencies are listed under Subtasks: a done one struck through, and × removes it")]
    public async Task DependenciesUnderSubtasks()
    {
        await using BunitContext ctx = NewContext();
        WorkItemsApi.AddDependency(107, 124);   // archived, so done
        IRenderedComponent<WorkItemPage> page = ctx.Render<WorkItemPage>(p => p.Add(c => c.Id, 107));

        Focus(page, "subtasks");
        AngleSharp.Dom.IElement row = page.Find(".tile .sub.dep.waits");
        Assert.Contains("#wi124", row.TextContent);
        Assert.Contains("done", row.ClassList);

        row.QuerySelector("button[aria-label='Remove dependency']")!.Click();

        Assert.Empty(WorkItemsApi.Get(107)!.DependsOn!);
        Assert.Empty(page.FindAll(".sub.dep"));
    }

    [Fact(DisplayName = "A merged item lists the commits that mention the numbers of the items it was merged from")]
    public async Task MergedItemKeepsCommits()
    {
        GitInit("Speed up the export #wi107");
        await using BunitContext ctx = NewContext();
        (bool ok, string message, int id) = WorkItemsApi.Merge([107, 123]);
        Assert.True(ok, message);
        IRenderedComponent<WorkItemPage> page = ctx.Render<WorkItemPage>(p => p.Add(c => c.Id, id));

        Focus(page, "commits");

        page.WaitForAssertion(() => Assert.Contains("Speed up the export #wi107", page.Find(".linked-commits").TextContent), TimeSpan.FromSeconds(5));   // a merged item's Load runs several git logs, so the click's render can lag on a busy machine
    }

    [Fact(DisplayName = "+ link commit lists recent commits; the picked one is linked and can be removed")]
    public async Task LinkCommit()
    {
        GitInit();
        await using BunitContext ctx = NewContext();
        IRenderedComponent<WorkItemPage> page = ctx.Render<WorkItemPage>(p => p.Add(c => c.Id, 123));

        Focus(page, "commits");
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

        Focus(page, "tests");
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
        await using BunitContext ctx = NewContext();
        IRenderedComponent<NotePage> page = ctx.Render<NotePage>(p => p.Add(c => c.Id, 123));
        await Type(page, "# Ping test\n\nAutosaved text.\n");

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

    [Fact(DisplayName = "Keyboard hints (F) are off until switched on in Settings; the switch is pushed to the page script")]
    public async Task HintsSetting()
    {
        await using BunitContext ctx = NewContext();
        Assert.False(ctx.Services.GetRequiredService<SettingsStore>().Current.Hints);
        IRenderedComponent<SettingsPage> page = ctx.Render<SettingsPage>();

        page.Find("input#f-hints").Change(true);

        Assert.True(ctx.Services.GetRequiredService<SettingsStore>().Current.Hints);
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
            page.Find("input#f-repo").Change(empty);
            page.Find("button.open-repo").Click();

            page.WaitForAssertion(() => Assert.Contains("Set up openspec/", overlays!.Find(".toast").TextContent));
            Assert.True(File.Exists(Path.Combine(empty, "openspec", "config.yaml")));

            page.Find("input#f-repo").Change(Path.Combine(empty, "nope"));
            page.Find("button.open-repo").Click();
            page.WaitForAssertion(() => Assert.Contains("nope", page.Find(".warning").TextContent));
        }
        finally { ctx.Services.GetRequiredService<Workspace>().Open(repo.Root); Directory.Delete(empty, true); }
    }

    // ── Doc page ──

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

        page.Find("input.att-url").Input("https://example.com/more");
        page.FindAll(".attachments button").First(b => b.TextContent.Trim() == "Add link").Click();
        Assert.Contains("https://example.com/more", NotesApi.Get(123)!.Attachments);

        page.WaitForAssertion(() => Assert.Equal(4, page.FindAll("button.att-x").Count));
        page.FindAll("button.att-x")[0].Click();
        Assert.Equal(3, NotesApi.Get(123)!.Attachments.Count);
        Directory.Delete(src, true);
    }

    public void Dispose()
    {
        repo.Dispose();
        File.Delete(settingsFile);
    }
}
