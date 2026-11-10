using Bunit;
using Microsoft.AspNetCore.Components.Web;
using OpenSpecStore;
using Radzen;
using TaskManager.Components.Pages;
using Microsoft.Extensions.DependencyInjection;
using TaskManager.Components.Layout;
using TaskManager.Components.Shared;
using TaskManager.Services;

namespace TaskManager.Tests;

/// <summary>Opening and switching between several repositories as tabs in the side nav.</summary>
[Collection(nameof(ModuleStateCollection))]
public sealed class SwitchingProjectsTests : IDisposable
{
    private readonly TestRepo repo1 = new();
    private readonly TestRepo repo2 = new();
    private readonly string settingsFile = Path.Combine(Path.GetTempPath(), "rtm-ui-" + Guid.NewGuid().ToString("N") + ".json");

    private IRenderedComponent<Overlays>? overlays;

    private (BunitContext Ctx, Workspace Workspace, IRenderedComponent<SideNav> Nav) NewContext(FolderOpener? opener = null)
    {
        BunitContext ctx = new();
        ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        ctx.Services.AddSingleton<UiService>();
        ctx.Services.AddRadzenComponents();   // the list view is a Radzen data grid
        ctx.Services.AddSingleton(new SettingsStore(settingsFile));
        ctx.Services.AddSingleton<Workspace>();
        if (opener is not null) ctx.Services.AddSingleton(opener);
        Workspace workspace = ctx.Services.GetRequiredService<Workspace>();
        Assert.True(workspace.Open(repo1.Root).Success);
        overlays = ctx.Render<Overlays>();
        IRenderedComponent<SideNav> nav = ctx.Render<SideNav>();
        return (ctx, workspace, nav);
    }

    [Fact(DisplayName = "Opening a project creates openspec/REPO-TASKS.md once (and has the UI say so), leaves a file of the user's alone, and refreshes an older copy of its own")]
    public async Task AgentGuideFile()
    {
        (BunitContext ctx, Workspace workspace, _) = NewContext();
        await using (ctx)
        {
            string guide = repo1.PathOf("openspec/REPO-TASKS.md");
            Assert.True(File.Exists(guide));
            string text = File.ReadAllText(guide);
            Assert.StartsWith("<!-- repo-tasks-guide v", text);
            Assert.Contains("agentBy", text);
            Assert.Contains("<!-- agent:", text);
            Assert.Contains("contributors: [artyom, Claude]", text);   // an agent that creates something is a contributor
            Assert.Contains("Add yourself when you create something", text);
            Assert.Contains("due:", text);   // start and due dates are part of the conventions
            foreach (string s in new[] { "agentBy", "notes/n", "commits:", "tests:", "validate", "## MODIFIED Requirements", "## Why" })
                Assert.Contains(s, text);   // author, change log, linked commits and tests, keeping OpenSpec valid
            Assert.Contains("Created openspec/REPO-TASKS.md", workspace.TakeGuideNotice());   // the UI says so once (a toast), not on every open
            Assert.Null(workspace.TakeGuideNotice());

            repo2.Write("openspec/REPO-TASKS.md", "my own notes\n");   // not ours: no marker
            workspace.OpenTab(repo2.Root);
            Assert.Equal("my own notes\n", repo2.Read("openspec/REPO-TASKS.md"));

            File.WriteAllText(guide, "<!-- repo-tasks-guide v0 -->\nold text\n");   // ours, but older
            workspace.Open(repo1.Root);
            Assert.Equal(text, File.ReadAllText(guide));
            Assert.Contains("Updated openspec/REPO-TASKS.md", workspace.TakeGuideNotice());
        }
    }

    [Fact(DisplayName = "The agent guide tells agents the workflow and where plans go; changing the plans folder in Settings rewrites the guide at once")]
    public async Task GuideFollowsPlansFolder()
    {
        (BunitContext ctx, Workspace workspace, _) = NewContext();
        await using (ctx)
        {
            string guide = repo1.PathOf("openspec/REPO-TASKS.md");
            string text = File.ReadAllText(guide);
            foreach (string s in new[] { "DevResources/plans", "x-rtm.plan", "worktree", "cheaper", "Research", "uncommitted", "local/task-tracking" }) Assert.Contains(s, text);
            workspace.TakeGuideNotice();

            IRenderedComponent<TaskManager.Components.Pages.SettingsPage> page = ctx.Render<TaskManager.Components.Pages.SettingsPage>();
            page.Find("input[aria-label='Plans folder']").Change("local/plans");

            page.WaitForAssertion(() => Assert.Contains("local/plans", File.ReadAllText(guide)));
            Assert.DoesNotContain("DevResources/plans", File.ReadAllText(guide));
            Assert.Equal("local/plans", ctx.Services.GetRequiredService<SettingsStore>().PlansDir(repo1.Root));
            Assert.Contains("Updated", overlays!.Find(".toast").TextContent);   // the rewrite is announced like a version refresh

            page.Find("input[aria-label='Plans folder']").Change("");   // back to the default, and the guide follows again
            page.WaitForAssertion(() => Assert.Contains("DevResources/plans", File.ReadAllText(guide)));
            page.Find("input[aria-label='Plans folder']").Change("other/plans");
            page.WaitForAssertion(() => Assert.Contains("other/plans", File.ReadAllText(guide)));
        }
    }

    [Fact(DisplayName = "The project switcher has an eye per project: clicking it hides the project from the board without switching, again shows it; the view options have no second project list")]
    public async Task ProjectFilterInSideMenu()
    {
        (BunitContext ctx, Workspace workspace, IRenderedComponent<SideNav> nav) = NewContext();
        await using (ctx)
        {
            workspace.OpenTab(repo2.Root);
            workspace.Open(repo1.Root);
            SettingsStore settings = ctx.Services.GetRequiredService<SettingsStore>();
            string name2 = Path.GetFileName(repo2.Root);

            nav.Find("button.tabs-btn").Click();   // open the switcher
            nav.WaitForAssertion(() => Assert.NotNull(nav.Find($"[aria-label='Hide project {name2}']")));
            nav.Find($"[aria-label='Hide project {name2}']").Click();
            Assert.True(settings.ProjectHidden(repo2.Root));
            Assert.Equal(Path.GetFullPath(repo1.Root).TrimEnd(Path.DirectorySeparatorChar), Path.GetFullPath(workspace.Root!).TrimEnd(Path.DirectorySeparatorChar), ignoreCase: true);   // no switch
            nav.WaitForAssertion(() => Assert.NotNull(nav.Find($"[aria-label='Show project {name2}']")));

            nav.Find($".menu-pop [aria-label='Show project {name2}']").Click();
            Assert.False(settings.ProjectHidden(repo2.Root));

            nav.Find("button.nav-toggle").Click();
            Assert.Empty(nav.FindAll(".nav-projects"));
        }
    }

    [Fact(DisplayName = "Showing a hidden project from the switcher re-renders the switcher and the board at once, also for a project backed by git")]
    public async Task ShowingHiddenProjectRerenders()
    {
        (BunitContext ctx, Workspace workspace, IRenderedComponent<SideNav> nav) = NewContext();
        await using (ctx)
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("git") { WorkingDirectory = repo2.Root, ArgumentList = { "init", "-q" }, CreateNoWindow = true })!.WaitForExit();
            workspace.OpenTab(repo2.Root);
            workspace.Open(repo1.Root);
            SettingsStore settings = ctx.Services.GetRequiredService<SettingsStore>();
            settings.SaveBoard(repo1.Root, settings.Board(repo1.Root) with { View = "list" });   // the list is a grid, grouped by project only once there are two
            settings.ToggleProjectHidden(repo2.Root);
            IRenderedComponent<BoardPage> board = ctx.Render<BoardPage>();
            string name2 = Path.GetFileName(repo2.Root);
            board.WaitForAssertion(() => Assert.NotEmpty(board.FindAll("tr[data-id]")));
            Assert.Empty(board.FindAll("tr[data-group^='proj:']"));

            nav.Find("button.tabs-btn").Click();
            nav.WaitForAssertion(() => Assert.NotNull(nav.Find($".menu-pop [aria-label='Show project {name2}']")));
            nav.Find($".menu-pop [aria-label='Show project {name2}']").Click();

            nav.WaitForAssertion(() => Assert.NotNull(nav.Find($".menu-pop [aria-label='Hide project {name2}']")));
            board.WaitForAssertion(() => Assert.Equal(2, board.FindAll("tr[data-group^='proj:']").Count));
        }
    }

    [Fact(DisplayName = "At start the app refreshes an outdated guide in every open project, not only the active one, and says which")]
    public async Task GuideRefreshedInEveryTab()
    {
        (BunitContext ctx, Workspace workspace, _) = NewContext();
        await using (ctx)
        {
            workspace.OpenTab(repo2.Root);
            workspace.TakeGuideNotice();
            string guide2 = repo2.PathOf("openspec/REPO-TASKS.md");
            File.WriteAllText(guide2, "<!-- repo-tasks-guide v1 -->\nold\n");
            OpenSpecStoreApi.Close();   // as at app exit; the tab stays in the settings

            Assert.True(workspace.Open(repo1.Root).Success);   // repo2 comes back as a tab

            Assert.StartsWith("<!-- repo-tasks-guide v", File.ReadAllText(guide2));
            Assert.DoesNotContain("v1 -->", File.ReadAllText(guide2));
            string? notice = workspace.TakeGuideNotice();
            Assert.NotNull(notice);
            Assert.Contains("Updated", notice);
            Assert.Contains(Path.GetFileName(repo2.Root), notice);
        }
    }

    [Fact(DisplayName = "A freshly opened repository is the only tab")]
    public async Task OneTabByDefault()
    {
        (BunitContext ctx, Workspace workspace, _) = NewContext();
        await using (ctx) { Assert.Equal([repo1.Root], workspace.OpenRepos); }
    }

    [Fact(DisplayName = "Opening another repository as a tab adds it and switches to it")]
    public async Task OpenTabSwitches()
    {
        (BunitContext ctx, Workspace workspace, _) = NewContext();
        await using (ctx)
        {
            (bool ok, string message) = workspace.OpenTab(repo2.Root);

            Assert.True(ok, message);
            Assert.Equal([repo1.Root, repo2.Root], workspace.OpenRepos);
            Assert.Equal(repo2.Root, workspace.Root);
        }
    }

    [Fact(DisplayName = "In(repo) reads another open repository without changing the active one")]
    public async Task ReadAnotherTab()
    {
        (BunitContext ctx, Workspace workspace, _) = NewContext();
        await using (ctx)
        {
            repo1.Write("openspec/changes/export-remittances/.openspec.yaml", "schema: spec-driven\nx-rtm:\n  id: 107\n  title: First repo item\n");
            workspace.OpenTab(repo2.Root);
            Assert.Equal(repo2.Root, workspace.Root);

            string title = workspace.In(repo1.Root, () => WorkItems.WorkItemsApi.Get(107)!.Title);

            Assert.Equal("First repo item", title);
            Assert.Equal(repo2.Root, workspace.Root);
            Assert.Equal("Monthly remittance export", WorkItems.WorkItemsApi.Get(107)!.Title);
            Assert.Equal([repo1.Root, repo2.Root], workspace.Projects);
        }
    }

    [Fact(DisplayName = "Switching tabs in the side nav changes the active repository")]
    public async Task SwitchTabFromNav()
    {
        (BunitContext ctx, Workspace workspace, IRenderedComponent<SideNav> nav) = NewContext();
        await using (ctx)
        {
            workspace.OpenTab(repo2.Root);
            nav.Find("button.tabs-btn").Click();

            nav.FindAll(".menu-item").First(i => i.TextContent.Contains(Path.GetFileName(repo1.Root))).Click();

            Assert.Equal(repo1.Root, workspace.Root);
        }
    }

    [Fact(DisplayName = "Remove a project…, the last entry of the repository menu and the only way to remove one (no close button on each project), asks which project to remove and closes that tab (files stay)")]
    public async Task RemoveProjectFromMenu()
    {
        (BunitContext ctx, Workspace workspace, IRenderedComponent<SideNav> nav) = NewContext();
        await using (ctx)
        {
            workspace.OpenTab(repo2.Root);
            nav.Find("button.tabs-btn").Click();
            Assert.Equal("Remove a project…", nav.FindAll(".menu-item").Last().TextContent.Trim());
            Assert.Empty(nav.FindAll(".menu-pop .chip.x"));   // no per-project close button

            nav.FindAll(".menu-item").Last().Click();
            overlays!.WaitForAssertion(() => Assert.Equal(2, overlays.FindAll(".dialog .choice").Count));
            overlays.FindAll(".dialog .choice").Single(b => b.TextContent.Contains(Path.GetFileName(repo1.Root))).Click();

            nav.WaitForAssertion(() => Assert.Equal([repo2.Root], workspace.OpenRepos));
            Assert.Equal(repo2.Root, workspace.Root);
            Assert.True(Directory.Exists(repo1.OpenSpec));
            nav.Find("button.tabs-btn").Click();
            Assert.DoesNotContain(nav.FindAll(".menu-item"), i => i.TextContent.Contains("Remove a project"));   // the last one can't be removed
        }
    }

    [Fact(DisplayName = "New work item asks which project it goes to when several are open; the item is created there and that project becomes active")]
    public async Task NewItemAsksForProject()
    {
        (BunitContext ctx, Workspace workspace, IRenderedComponent<SideNav> nav) = NewContext();
        await using (ctx)
        {
            workspace.OpenTab(repo2.Root);   // repo2 active
            int before = workspace.In(repo1.Root, () => WorkItems.WorkItemsApi.GetAll(includeHidden: true).Count);

            nav.Find("button[aria-label='New work item']").Click();
            overlays!.WaitForAssertion(() => Assert.Equal(2, overlays.FindAll(".dialog .choice").Count));
            overlays.FindAll(".dialog .choice").Single(b => b.TextContent.Contains(Path.GetFileName(repo1.Root))).Click();

            nav.WaitForAssertion(() => Assert.Equal(repo1.Root, workspace.Root));
            Assert.Equal(before + 1, WorkItems.WorkItemsApi.GetAll(includeHidden: true).Count);
        }
    }

    [Fact(DisplayName = "A refresh from the file watcher never throws while folders are moved or removed under it: the app shows a warning and carries on")]
    public async Task RefreshSurvivesMovingFolders()
    {
        (BunitContext ctx, Workspace workspace, _) = NewContext();
        await using (ctx)
        {
            string changes = Path.Combine(repo1.Root, "openspec", "changes");
            string archive = Path.Combine(changes, "archive");
            // another process moves whole folders in and out, as a merge or an archive does, and removes the archive itself
            using CancellationTokenSource stop = new(TimeSpan.FromSeconds(3));
            Task churn = Task.Run(() =>
            {
                for (int n = 0; !stop.IsCancellationRequested; n++)
                    try
                    {
                        string dir = Path.Combine(archive, $"2026-11-10-moving-{n % 3}");
                        Directory.CreateDirectory(dir);
                        File.WriteAllText(Path.Combine(dir, ".openspec.yaml"), $"schema: spec-driven\ncreated: 2026-11-10\nx-rtm:\n  id: 123\n  title: Moving {n}\n");   // a duplicate number: Notify renumbers it
                        File.WriteAllText(Path.Combine(dir, "proposal.md"), "# Moving\n");
                        if (n % 5 == 0) Directory.Delete(archive, recursive: true); else Directory.Delete(dir, recursive: true);
                    }
                    catch (IOException) { } catch (UnauthorizedAccessException) { }
            });
            List<Exception> thrown = [];
            while (!stop.IsCancellationRequested)
                try { workspace.Notify(); } catch (Exception e) { thrown.Add(e); }
            await churn;

            Assert.Empty(thrown.Select(e => e.GetType().Name + ": " + e.Message).Distinct());
            workspace.Notify();
            Assert.Contains(WorkItems.WorkItemsApi.GetAll(), w => w.Id == 107);   // the board reads as usual afterwards
        }
    }

    [Fact(DisplayName = "What the user changes in the app is recorded in the worklog under their name")]
    public async Task AppChangesAreLoggedUnderTheUser()
    {
        (BunitContext ctx, Workspace workspace, _) = NewContext();
        await using (ctx)
        {
            ctx.Services.GetRequiredService<SettingsStore>().Update(s => s with { Name = "Dana" });
            Assert.Equal("Dana", workspace.Me);

            Assert.True(WorkItems.WorkItemsApi.SetStatus(107, "In Progress").Success);

            WorkItems.PublicDTOs.WorkItemEvent last = WorkItems.WorkItemsApi.GetEvents(107)[^1];
            Assert.Equal(("status", "Dana"), (last.Kind, last.By));
        }
    }

    [Fact(DisplayName = "With one project open, New work item creates it straight away, without asking")]
    public async Task NewItemOneProjectDoesNotAsk()
    {
        (BunitContext ctx, Workspace workspace, IRenderedComponent<SideNav> nav) = NewContext();
        await using (ctx)
        {
            int before = WorkItems.WorkItemsApi.GetAll(includeHidden: true).Count;
            nav.Find("button[aria-label='New work item']").Click();
            nav.WaitForAssertion(() => Assert.Equal(before + 1, WorkItems.WorkItemsApi.GetAll(includeHidden: true).Count));
            Assert.Empty(overlays!.FindAll(".dialog"));
        }
    }

    [Fact(DisplayName = "Closing a tab that isn't the only one falls back to another open tab")]
    public async Task CloseTabFallsBack()
    {
        (BunitContext ctx, Workspace workspace, _) = NewContext();
        await using (ctx)
        {
            workspace.OpenTab(repo2.Root);

            workspace.CloseTab(repo2.Root);

            Assert.Equal([repo1.Root], workspace.OpenRepos);
            Assert.Equal(repo1.Root, workspace.Root);
        }
    }

    [Fact(DisplayName = "The last remaining tab can't be closed")]
    public async Task CannotCloseLastTab()
    {
        (BunitContext ctx, Workspace workspace, _) = NewContext();
        await using (ctx)
        {
            workspace.CloseTab(repo1.Root);

            Assert.Equal([repo1.Root], workspace.OpenRepos);
        }
    }

    public void Dispose()
    {
        repo1.Dispose();
        repo2.Dispose();
    }

    [Fact(DisplayName = "Settings lists every open project with its own plans folder and attachments folder, and each is saved for that project only")]
    public async Task SettingsFoldersPerProject()
    {
        (BunitContext ctx, Workspace workspace, _) = NewContext();
        await using (ctx)
        {
            Assert.True(workspace.OpenTab(repo2.Root).Success);
            SettingsStore settings = ctx.Services.GetRequiredService<SettingsStore>();
            IRenderedComponent<TaskManager.Components.Pages.SettingsPage> page = ctx.Render<TaskManager.Components.Pages.SettingsPage>();

            IReadOnlyList<AngleSharp.Dom.IElement> rows = page.FindAll("section[data-project]");
            Assert.Equal([Path.GetFileName(repo1.Root), Path.GetFileName(repo2.Root)], rows.Select(r => r.GetAttribute("data-project")));
            foreach (AngleSharp.Dom.IElement row in rows)
            {
                Assert.Equal("DevResources/plans", row.QuerySelector("input[aria-label='Plans folder']")!.GetAttribute("placeholder"));
                Assert.Equal("local/task-tracking", row.QuerySelector("input[aria-label='Attachments folder']")!.GetAttribute("placeholder"));
            }

            page.FindAll("section[data-project]")[1].QuerySelector("input[aria-label='Attachments folder']")!.Change("files/att");
            page.FindAll("section[data-project]")[0].QuerySelector("input[aria-label='Plans folder']")!.Change("notes/plans");

            Assert.Equal(("files/att", "local/task-tracking"), (settings.AttachmentsDir(repo2.Root), settings.AttachmentsDir(repo1.Root)));
            Assert.Equal(("notes/plans", "DevResources/plans"), (settings.PlansDir(repo1.Root), settings.PlansDir(repo2.Root)));
            Assert.Equal("files/att", page.FindAll("section[data-project]")[1].QuerySelector("input[aria-label='Attachments folder']")!.GetAttribute("value"));

            // the guide of each project names its own folders; the other project's guide is not rewritten for it
            Assert.Contains("attachments=files/att", File.ReadLines(repo2.PathOf("openspec/REPO-TASKS.md")).First());
            Assert.Contains("`files/att/wi<item>/n<note>/`", repo2.Read("openspec/REPO-TASKS.md"));
            Assert.Contains("attachments=local/task-tracking", File.ReadLines(repo1.PathOf("openspec/REPO-TASKS.md")).First());
            Assert.Contains("plans=notes/plans", File.ReadLines(repo1.PathOf("openspec/REPO-TASKS.md")).First());

            // a note's attachment goes to the folder of the project the note is in
            byte[] png = [1, 2, 3];
            Assert.True(workspace.In(repo2.Root, () => Notes.NotesApi.AttachBytes(123, "x.png", png)).Success);
            Assert.True(workspace.In(repo1.Root, () => Notes.NotesApi.AttachBytes(123, "x.png", png)).Success);
            Assert.True(File.Exists(repo2.PathOf("files/att/wi123/n123/x.png")));
            Assert.True(File.Exists(repo1.PathOf("local/task-tracking/wi123/n123/x.png")));
            Assert.False(File.Exists(repo1.PathOf("files/att/wi123/n123/x.png")));

            page.FindAll("section[data-project]")[1].QuerySelector("input[aria-label='Attachments folder']")!.Change("");   // empty = the default again
            Assert.Equal("local/task-tracking", settings.AttachmentsDir(repo2.Root));
            Assert.Contains("attachments=local/task-tracking", File.ReadLines(repo2.PathOf("openspec/REPO-TASKS.md")).First());
        }
    }

    [Fact(DisplayName = "Settings shows every open project's location under its name, each with a Show in folder button, and the top row opens another repository")]
    public async Task SettingsShowsEachProjectLocation()
    {
        using TestRepo repo3 = new();
        List<string> shown = [];
        (BunitContext ctx, Workspace workspace, _) = NewContext(path => { shown.Add(path); return (true, ""); });
        await using (ctx)
        {
            Assert.True(workspace.OpenTab(repo2.Root).Success);
            IRenderedComponent<SettingsPage> page = ctx.Render<SettingsPage>();

            // the top row is for opening another repository: empty, and it no longer shows the active project's path
            AngleSharp.Dom.IElement box = page.Find("input#f-repo");
            Assert.Equal("", box.GetAttribute("value") ?? "");
            Assert.Equal("Path to a repository…", box.GetAttribute("placeholder"));
            Assert.Contains("Open another repository", page.Markup);
            Assert.DoesNotContain("Folders of", page.Markup);

            // every open project: its name, its full location (selectable text, title = the path), a Show in folder button
            IReadOnlyList<AngleSharp.Dom.IElement> rows = page.FindAll("section[data-project]");
            Assert.Equal([repo1.Root, repo2.Root], rows.Select(r => r.QuerySelector(".set-path")!.TextContent.Trim()));
            foreach (AngleSharp.Dom.IElement row in rows)
            {
                Assert.Equal(row.GetAttribute("data-project"), row.QuerySelector("h3")!.TextContent.Trim());
                Assert.Equal(row.QuerySelector(".set-path")!.TextContent.Trim(), row.QuerySelector(".set-path")!.GetAttribute("title"));
                Assert.Equal("Show in folder", row.QuerySelector("button.show-folder")!.TextContent.Trim());
                Assert.NotNull(row.QuerySelector("input[aria-label='Plans folder']"));
                Assert.NotNull(row.QuerySelector("input[aria-label='Attachments folder']"));
            }

            page.FindAll("section[data-project]")[1].QuerySelector("button.show-folder")!.Click();
            page.FindAll("section[data-project]")[0].QuerySelector("button.show-folder")!.Click();
            Assert.Equal([repo2.Root, repo1.Root], shown);

            // the top row adds the typed repository as a project and switches to it
            page.Find("input#f-repo").Change(repo3.Root);
            page.Find("button.open-repo").Click();
            page.WaitForAssertion(() => Assert.Equal([repo1.Root, repo2.Root, repo3.Root], workspace.OpenRepos));
            Assert.Equal(repo3.Root, workspace.Root);
        }
    }

    // ── changed picker: the reported case ──

    [Fact(DisplayName = "Opening the list with two projects open still groups by project when the saved list settings were made with one project, so the page stays alive and the side menu's pickers keep working")]
    public async Task ListWithSettingsFromOneProjectGroupsByProjectOnceThereAreTwo()
    {
        (BunitContext ctx, Workspace workspace, IRenderedComponent<SideNav> nav) = NewContext();
        await using (ctx)
        {
            workspace.OpenTab(repo2.Root);
            workspace.Open(repo1.Root);
            SettingsStore settings = ctx.Services.GetRequiredService<SettingsStore>();
            // what the grid saved while one project was open: grouped by column only
            string oneProject = """{"Columns":[],"Groups":[{"Property":"Group","SortOrder":null,"Title":"Column","FormatString":"","ShowFooter":null}],"CurrentPage":0,"PageSize":10}""";
            settings.SaveBoard(repo1.Root, settings.Board(repo1.Root) with { View = "list", ListSettings = oneProject });

            IRenderedComponent<BoardPage> board = ctx.Render<BoardPage>();   // throws while it draws a project row from a column group

            board.WaitForAssertion(() => Assert.Equal(2, board.FindAll("tr[data-group^='proj:']").Count));
            Assert.NotEmpty(board.FindAll("tr[data-id]"));
        }
    }

    // ── project name menu ──

    [Fact(DisplayName = "Clicking a project's name opens a menu to collapse or expand it and to hide it, and the header shows no active label or options icon")]
    public async Task ProjectNameOpensMenu()
    {
        (BunitContext ctx, Workspace workspace, _) = NewContext();
        await using (ctx)
        {
            workspace.OpenTab(repo2.Root);
            workspace.Open(repo1.Root);
            IRenderedComponent<BoardPage> board = ctx.Render<BoardPage>();
            board.WaitForAssertion(() => Assert.Equal(2, board.FindAll(".proj-h").Count));

            Assert.DoesNotContain("active", board.FindAll(".proj-h")[0].TextContent);
            Assert.Empty(board.FindAll(".proj-h button[aria-label='Project options'], .proj-h .muted"));
            Assert.Equal(["button", "h2"], board.FindAll(".proj-h")[0].Children.Select(c => c.LocalName));   // the chevron and the name; no options icon
            IReadOnlyList<AngleSharp.Dom.IElement> names = board.FindAll(".proj-h h2 button");
            Assert.Equal(2, names.Count);   // the name is a real button, the menu's activator
            Assert.Equal("menu", names[1].GetAttribute("aria-haspopup"));

            IEnumerable<string> Items() => board.FindAll(".menu-pop .menu-item").Select(i => i.TextContent.Trim());
            names[1].Click();
            Assert.Equal(["Collapse", "Make it the active project", "Hide project"], Items());
            board.FindAll(".menu-item").Single(i => i.TextContent.Trim() == "Collapse").Click();
            board.WaitForAssertion(() => Assert.Empty(board.FindAll(".proj")[1].QuerySelectorAll("section.col")));
            Assert.NotEmpty(board.FindAll(".proj")[0].QuerySelectorAll("section.col"));

            board.FindAll(".proj-h h2 button")[1].Click();
            Assert.Equal(["Expand", "Make it the active project", "Hide project"], Items());
            board.FindAll(".menu-item").Single(i => i.TextContent.Trim() == "Expand").Click();
            board.WaitForAssertion(() => Assert.NotEmpty(board.FindAll(".proj")[1].QuerySelectorAll("section.col")));

            board.FindAll(".proj-h h2 button")[0].Click();
            Assert.Equal(["Collapse", "Hide project"], Items());   // the active project has nothing to switch to
            board.FindAll(".menu-item").Single(i => i.TextContent.Trim() == "Hide project").Click();
            board.WaitForAssertion(() => Assert.Single(board.FindAll(".proj")));
        }
    }

    [Fact(DisplayName = "In the list, a project row's name opens the same menu: Collapse closes its rows, Expand opens them again, and a Shift-click on the name only selects the row")]
    public async Task ListProjectRowNameOpensMenu()
    {
        (BunitContext ctx, Workspace workspace, _) = NewContext();
        await using (ctx)
        {
            workspace.OpenTab(repo2.Root);
            workspace.Open(repo1.Root);
            SettingsStore settings = ctx.Services.GetRequiredService<SettingsStore>();
            settings.SaveBoard(repo1.Root, settings.Board(repo1.Root) with { View = "list" });
            IRenderedComponent<BoardPage> board = ctx.Render<BoardPage>();
            board.WaitForAssertion(() => Assert.Equal(2, board.FindAll("tr[data-group^='proj:'] button.proj-name-btn").Count));
            int open = board.FindAll("tr[data-id]").Count;
            Assert.True(open > 0);

            board.FindAll("tr[data-group^='proj:'] button.proj-name-btn")[1].Click(new MouseEventArgs { ShiftKey = true });
            board.WaitForAssertion(() => Assert.Single(board.FindAll("tr[data-selected]")));
            Assert.Empty(board.FindAll(".menu-pop"));

            board.FindAll("tr[data-group^='proj:'] button.proj-name-btn")[1].Click();
            Assert.Equal(["Collapse", "Make it the active project", "Hide project"], board.FindAll(".menu-pop .menu-item").Select(i => i.TextContent.Trim()));
            board.FindAll(".menu-item").Single(i => i.TextContent.Trim() == "Collapse").Click();
            board.WaitForAssertion(() => Assert.True(board.FindAll("tr[data-id]").Count < open));

            board.FindAll("tr[data-group^='proj:'] button.proj-name-btn")[1].Click();
            board.FindAll(".menu-item").Single(i => i.TextContent.Trim() == "Expand").Click();
            board.WaitForAssertion(() => Assert.Equal(open, board.FindAll("tr[data-id]").Count));
        }
    }

    [Fact(DisplayName = "Shift-clicking a project header still selects the project without opening the menu, and a project that is not the active one can be made active from its name's menu")]
    public async Task ShiftClickSelectsAndMenuMakesActive()
    {
        (BunitContext ctx, Workspace workspace, _) = NewContext();
        await using (ctx)
        {
            workspace.OpenTab(repo2.Root);
            workspace.Open(repo1.Root);
            IRenderedComponent<BoardPage> board = ctx.Render<BoardPage>();
            board.WaitForAssertion(() => Assert.Equal(2, board.FindAll(".proj-h").Count));

            board.FindAll(".proj-h h2 button")[1].Click(new MouseEventArgs { ShiftKey = true });   // on the name itself
            board.WaitForAssertion(() => Assert.Contains("selected", board.FindAll(".proj-h")[1].ClassName));
            Assert.Empty(board.FindAll(".menu-pop"));

            board.FindAll(".proj-h h2 button")[1].Click();   // a plain click opens the menu and leaves the selection as it was
            Assert.Single(board.FindAll(".menu-pop"));
            Assert.Contains("selected", board.FindAll(".proj-h")[1].ClassName);
            Assert.Equal(repo1.Root, workspace.Root);

            board.FindAll(".menu-item").Single(i => i.TextContent.Trim() == "Make it the active project").Click();
            board.WaitForAssertion(() => Assert.Equal(repo2.Root, workspace.Root));
        }
    }
}
