using Bunit;
using Microsoft.AspNetCore.Components.Web;
using OpenSpecStore;
using Radzen;
using TaskManager.Components.Pages;
using Microsoft.Extensions.DependencyInjection;
using TaskManager.Components.Layout;
using TaskManager.Components.Shared;
using TaskManager.AppState;
using TaskManager.BackgroundJobs;
using TaskManager.Helpers;

namespace TaskManager.Tests;

/// <summary>Opening and switching between several repositories as tabs in the side nav.</summary>
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

            IRenderedComponent<TaskManager.Components.Pages.SettingsPage> page = ctx.Render<TaskManager.Components.Pages.SettingsPage>().OnProject();
            void PointPlans(string path) { page.ChangeWhenReady("[data-folder='plans'] input", path); page.ClickWhenReady("[data-folder='plans'] .folder-point"); }
            PointPlans("local/files/plans");

            page.WaitForAssertion(() => Assert.Contains("local/files/plans", File.ReadAllText(guide)));
            Assert.DoesNotContain("DevResources/plans", File.ReadAllText(guide));
            Assert.Equal("local/files/plans", ctx.Services.GetRequiredService<SettingsStore>().PlansDir(repo1.Root));
            Assert.Contains("Updated", overlays!.Find(".toast").TextContent);   // the rewrite is announced like a version refresh

            PointPlans("");   // empty is the default folder, and the guide follows again
            page.WaitForAssertion(() => Assert.Contains("DevResources/plans", File.ReadAllText(guide)));
            PointPlans("other/files/plans");
            page.WaitForAssertion(() => Assert.Contains("other/files/plans", File.ReadAllText(guide)));
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

            nav.ClickWhenReady("button.tabs-btn");   // open the switcher
            nav.WaitForAssertion(() => Assert.NotNull(nav.Find($"[aria-label='Hide project {name2}']")));
            nav.ClickWhenReady($"[aria-label='Hide project {name2}']");
            Assert.True(settings.ProjectHidden(repo2.Root));
            Assert.Equal(Path.GetFullPath(repo1.Root).TrimEnd(Path.DirectorySeparatorChar), Path.GetFullPath(workspace.Root!).TrimEnd(Path.DirectorySeparatorChar), ignoreCase: true);   // no switch
            nav.WaitForAssertion(() => Assert.NotNull(nav.Find($"[aria-label='Show project {name2}']")));

            nav.ClickWhenReady($".menu-pop [aria-label='Show project {name2}']");
            Assert.False(settings.ProjectHidden(repo2.Root));

            nav.ClickWhenReady("button.nav-toggle");
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
            settings.SaveBoard(workspace.ViewRoot!, settings.Board(workspace.ViewRoot!) with { View = "list" });   // the list is a grid, grouped by project only once there are two
            settings.ToggleProjectHidden(repo2.Root);
            IRenderedComponent<BoardPage> board = ctx.Render<BoardPage>();
            string name2 = Path.GetFileName(repo2.Root);
            board.WaitForAssertion(() => Assert.NotEmpty(board.FindAll("tr[data-id]")));
            Assert.Empty(board.FindAll("tr[data-group^='proj:']"));

            nav.ClickWhenReady("button.tabs-btn");
            nav.WaitForAssertion(() => Assert.NotNull(nav.Find($".menu-pop [aria-label='Show project {name2}']")));
            nav.ClickWhenReady($".menu-pop [aria-label='Show project {name2}']");

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
            nav.ClickWhenReady("button.tabs-btn");

            nav.FireWhenReady(".menu-item", e => e.Click(), all => all.First(i => i.TextContent.Contains(Path.GetFileName(repo1.Root))));

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
            nav.ClickWhenReady("button.tabs-btn");
            Assert.Equal("Remove a project…", nav.FindAll(".menu-item").Last().TextContent.Trim());
            Assert.Empty(nav.FindAll(".menu-pop .chip.x"));   // no per-project close button

            nav.FireWhenReady(".menu-item", e => e.Click(), all => all.Last());
            overlays!.WaitForAssertion(() => Assert.Equal(2, overlays.FindAll(".dialog .choice").Count));
            overlays.FireWhenReady(".dialog .choice", e => e.Click(), all => all.Single(b => b.TextContent.Contains(Path.GetFileName(repo1.Root))));

            nav.WaitForAssertion(() => Assert.Equal([repo2.Root], workspace.OpenRepos));
            Assert.Equal(repo2.Root, workspace.Root);
            Assert.True(Directory.Exists(repo1.OpenSpec));
            nav.ClickWhenReady("button.tabs-btn");
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

            nav.ClickWhenReady("button[aria-label='New work item']");
            overlays!.WaitForAssertion(() => Assert.Equal(2, overlays.FindAll(".dialog .choice").Count));
            overlays.FireWhenReady(".dialog .choice", e => e.Click(), all => all.Single(b => b.TextContent.Contains(Path.GetFileName(repo1.Root))));

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
            nav.ClickWhenReady("button[aria-label='New work item']");
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
            IRenderedComponent<TaskManager.Components.Pages.SettingsPage> page = ctx.Render<TaskManager.Components.Pages.SettingsPage>().OnProject();

            async Task Point(int project, string kind, string path)
            {
                page.OnProject(project);
                page.ChangeWhenReady($"[data-project] [data-folder='{kind}'] input", path);
                page.WaitForAssertion(() => Assert.NotEmpty(page.FindAll($"[data-project] [data-folder='{kind}'] .folder-point")));   // typing re-renders the row: the button is drawn once the change has been handled
                page.ClickWhenReady($"[data-project] [data-folder='{kind}'] .folder-point");   // found and clicked in one dispatcher step
            }
            string Shown(int project, string kind) => page.OnProject(project).Find($"[data-project] [data-folder='{kind}'] input").GetAttribute("value")!;

            Assert.Equal([Path.GetFileName(repo1.Root), Path.GetFileName(repo2.Root)], page.Labels().Skip(1));   // one tab per project, after General
            for (int project = 0; project < 2; project++)
            {
                page.OnProject(project);
                Assert.Equal(Path.GetFileName(project == 0 ? repo1.Root : repo2.Root), page.Find("[data-project]").GetAttribute("data-project"));   // only this project's rows are drawn
                Assert.Equal("DevResources/plans", page.Find("[data-folder='plans'] input").GetAttribute("placeholder"));
                Assert.Equal("local/task-tracking", page.Find("[data-folder='images'] input").GetAttribute("placeholder"));
                Assert.Equal(["Plans", "Images"], page.FindAll("[data-project] [data-folder] th").Select(l => l.TextContent.Trim()));   // one line each, no merged option
            }

            await Point(1, "plans", "files/att/plans");
            await Point(1, "images", "files/att/images");
            Assert.Equal(("files/att/images", "local/task-tracking"), (settings.AttachmentsDir(repo2.Root), settings.AttachmentsDir(repo1.Root)));   // the other project keeps its own, old, folders
            Assert.Equal(("files/att/plans", "DevResources/plans"), (settings.PlansDir(repo2.Root), settings.PlansDir(repo1.Root)));
            await Point(0, "plans", "notes/plans");
            await Point(0, "images", "notes/images");

            Assert.Equal(("files/att/images", "notes/images"), (settings.AttachmentsDir(repo2.Root), settings.AttachmentsDir(repo1.Root)));
            Assert.Equal(("notes/plans", "files/att/plans"), (settings.PlansDir(repo1.Root), settings.PlansDir(repo2.Root)));
            Assert.Equal(("files/att/plans", "files/att/images"), (Shown(1, "plans"), Shown(1, "images")));

            // the guide of each project names its own folders; the other project's guide is not rewritten for it
            Assert.Contains("attachments=files/att/images", File.ReadLines(repo2.PathOf("openspec/REPO-TASKS.md")).First());
            Assert.Contains("`files/att/images/wi<item>/n<note>/`", repo2.Read("openspec/REPO-TASKS.md"));
            Assert.Contains("attachments=notes/images", File.ReadLines(repo1.PathOf("openspec/REPO-TASKS.md")).First());
            Assert.Contains("plans=notes/plans", File.ReadLines(repo1.PathOf("openspec/REPO-TASKS.md")).First());

            // a note's attachment goes to the folder of the project the note is in
            byte[] png = [1, 2, 3];
            Assert.True(workspace.In(repo2.Root, () => Notes.NotesApi.AttachBytes(123, "x.png", png)).Success);
            Assert.True(workspace.In(repo1.Root, () => Notes.NotesApi.AttachBytes(123, "x.png", png)).Success);
            Assert.True(File.Exists(repo2.PathOf("files/att/images/wi123/n123/x.png")));
            Assert.True(File.Exists(repo1.PathOf("notes/images/wi123/n123/x.png")));
            Assert.False(File.Exists(repo1.PathOf("files/att/images/wi123/n123/x.png")));

            await Point(1, "images", "");   // empty = the default folder; Point here leaves the file attached above where it is
            page.WaitForAssertion(() => Assert.Equal("local/task-tracking", settings.AttachmentsDir(repo2.Root)));
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

            // every open project: its tab, its full location (selectable text, title = the path), a Show in folder button
            string[] roots = [repo1.Root, repo2.Root];
            for (int project = 0; project < 2; project++)
            {
                page.OnProject(project);
                AngleSharp.Dom.IElement row = page.Find("[data-project]");
                Assert.Equal(roots[project], row.QuerySelector(".set-path")!.TextContent.Trim());
                Assert.Equal(row.GetAttribute("data-project"), row.QuerySelector("h3")!.TextContent.Trim());
                Assert.Equal(row.QuerySelector(".set-path")!.TextContent.Trim(), row.QuerySelector(".set-path")!.GetAttribute("title"));
                Assert.Equal("Show in folder", row.QuerySelector("button.show-folder")!.TextContent.Trim());
                Assert.NotNull(row.QuerySelector("input[aria-label='Plans folder']"));
                Assert.NotNull(row.QuerySelector("input[aria-label='Images folder']"));
            }

            page.OnProject(1).ClickWhenReady("[data-project] button.show-folder");
            page.OnProject(0).ClickWhenReady("[data-project] button.show-folder");
            Assert.Equal([repo2.Root, repo1.Root], shown);

            page.OnTab("general");
            // the top row adds the typed repository as a project and switches to it
            page.ChangeWhenReady("input#f-repo", repo3.Root);
            page.ClickWhenReady("button.open-repo");
            page.WaitForAssertion(() => Assert.Equal([repo1.Root, repo2.Root, repo3.Root], workspace.OpenRepos));
            Assert.Equal(repo3.Root, workspace.Root);
        }
    }

    // ── statuses are per project (#wi200) ──

    /// <summary>Two projects on the board, grouped by status: repo1 has a Research status, repo2 does not. Returns the names of both.</summary>
    private async Task<(BunitContext Ctx, Workspace Workspace, IRenderedComponent<BoardPage> Board, string With, string Without, int Card)> TwoProjectsOneWithResearch()
    {
        (BunitContext ctx, Workspace workspace, _) = NewContext();
        workspace.OpenTab(repo2.Root);
        workspace.Open(repo1.Root);
        Assert.True(workspace.In(repo1.Root, () => WorkItems.WorkItemsApi.AddStatus("Research", beforeCompleted: true)).Success);
        int card = workspace.In(repo2.Root, () => WorkItems.WorkItemsApi.GetAll().First(i => i.Status == "Backlog").Id);
        IRenderedComponent<BoardPage> board = ctx.Render<BoardPage>();
        await Task.CompletedTask;
        return (ctx, workspace, board, Path.GetFileName(repo1.Root), Path.GetFileName(repo2.Root), card);
    }

    private static AngleSharp.Dom.IElement Cell(IRenderedComponent<BoardPage> board, string project, string status) =>
        board.Find($".proj[data-proj='{project}'] section.col[data-key='{status}']");

    [Fact(DisplayName = "With two projects open a status only one has shows once in the header row, and the project without it has an unavailable cell there instead of a working column")]
    public async Task AStatusOfOneProjectIsUnavailableInTheOther()
    {
        (BunitContext ctx, _, IRenderedComponent<BoardPage> board, string with, string without, _) = await TwoProjectsOneWithResearch();
        await using (ctx)
        {
            Assert.Single(board.FindAll(".head-row section.col.head-only[data-key='Research']"));
            Assert.DoesNotContain("absent", Cell(board, with, "Research").ClassList);
            Assert.Contains("absent", Cell(board, without, "Research").ClassList);
            Assert.Empty(Cell(board, without, "Research").QuerySelectorAll("article.card"));
            Assert.DoesNotContain("absent", Cell(board, without, "Backlog").ClassList);
        }
    }

    [Fact(DisplayName = "Dropping a card onto a status its project lacks asks to add the status to that project; yes adds it to the project's config and moves the card")]
    public async Task DroppingOnAMissingStatusAsksAndAddsIt()
    {
        (BunitContext ctx, _, IRenderedComponent<BoardPage> board, _, string without, int card) = await TwoProjectsOneWithResearch();
        await using (ctx)
        {
            board.Find($".proj[data-proj='{without}'] article.card[data-id='{card}']").DragStart();
            Cell(board, without, "Research").Drop();

            overlays!.WaitForAssertion(() => Assert.Contains($"Add Research to {without}?", overlays.Find(".dialog").TextContent));
            Assert.DoesNotContain("Research", repo2.Read("openspec/config.yaml"));
            overlays.FireWhenReady(".dialog button", e => e.Click(), all => all.Single(b => b.TextContent.Trim() == "Add"));

            board.WaitForAssertion(() => Assert.Contains("Research", repo2.Read("openspec/config.yaml")));
            board.WaitForAssertion(() => Assert.Equal("Research", ctx.Services.GetRequiredService<Workspace>().In(repo2.Root, () => WorkItems.WorkItemsApi.Get(card)!.Status)));
            board.WaitForAssertion(() => Assert.DoesNotContain("absent", Cell(board, without, "Research").ClassList));
        }
    }

    [Fact(DisplayName = "Answering no to adding the missing status changes nothing: the config and the card stay as they were")]
    public async Task DecliningToAddTheStatusChangesNothing()
    {
        (BunitContext ctx, Workspace workspace, IRenderedComponent<BoardPage> board, _, string without, int card) = await TwoProjectsOneWithResearch();
        await using (ctx)
        {
            string before = repo2.Read("openspec/config.yaml");
            board.Find($".proj[data-proj='{without}'] article.card[data-id='{card}']").DragStart();
            Cell(board, without, "Research").Drop();

            overlays!.WaitForAssertion(() => Assert.Contains($"Add Research to {without}?", overlays.Find(".dialog").TextContent));
            overlays.FireWhenReady(".dialog button", e => e.Click(), all => all.Single(b => b.TextContent.Trim() == "Cancel"));

            overlays.WaitForAssertion(() => Assert.Empty(overlays.FindAll(".dialog")));
            Assert.Equal(before, repo2.Read("openspec/config.yaml"));
            Assert.Equal("Backlog", workspace.In(repo2.Root, () => WorkItems.WorkItemsApi.Get(card)!.Status));
            Assert.Contains("absent", Cell(board, without, "Research").ClassList);
        }
    }

    // ── the user's column order wins over the projects' lists (#wi207) ──

    private static List<string> HeadOrder(IRenderedComponent<BoardPage> board) => [.. board.FindAll(".head-row section.col.head-only").Select(c => c.GetAttribute("data-key")!)];
    private static List<string> CellOrder(IRenderedComponent<BoardPage> board, string project) => [.. board.FindAll($".proj[data-proj='{project}'] section.col").Select(c => c.GetAttribute("data-key")!)];
    private static List<string> SavedOrder(BunitContext ctx, Workspace workspace) =>
        [.. ctx.Services.GetRequiredService<SettingsStore>().Board(workspace.ViewRoot!).Order!.Where(k => k.StartsWith("status:")).Select(k => k["status:".Length..])];

    private static readonly string[] ResearchBeforeBlocked = ["Backlog", "Planned", "Confirmed", "In Progress", "Research", "Blocked", "In Review", "Completed"];

    [Fact(DisplayName = "With two projects open, dragging a status only one has in front of another column puts it there in the shared header, in the other project's unavailable cell and in the saved order, and it stays after a fresh render")]
    public async Task DraggingAStatusOfOneProjectInTheSharedHeader()
    {
        (BunitContext ctx, Workspace workspace, IRenderedComponent<BoardPage> board, string with, string without, _) = await TwoProjectsOneWithResearch();
        await using (ctx)
        {
            Assert.Equal(["Backlog", "Planned", "Confirmed", "In Progress", "Blocked", "In Review", "Research", "Completed"], HeadOrder(board));

            board.Find(".head-row section.col.head-only[data-key='Research'] header.col-h").DragStart();
            board.Find(".head-row section.col.head-only[data-key='Blocked']").Drop();

            board.WaitForAssertion(() => Assert.Equal(ResearchBeforeBlocked, HeadOrder(board)));
            Assert.Equal(ResearchBeforeBlocked, CellOrder(board, with));
            Assert.Equal(ResearchBeforeBlocked, CellOrder(board, without));   // the other project's unavailable cell keeps the header's place
            Assert.Contains("absent", Cell(board, without, "Research").ClassList);
            Assert.Equal(ResearchBeforeBlocked, SavedOrder(ctx, workspace));

            IRenderedComponent<BoardPage> fresh = ctx.Render<BoardPage>();   // the order is read back from the saved settings, not kept by the first page
            Assert.Equal(ResearchBeforeBlocked, HeadOrder(fresh));
            Assert.Equal(ResearchBeforeBlocked, CellOrder(fresh, without));
        }
    }

    [Fact(DisplayName = "With two projects open, Left and Right on a Shift-clicked status column of the shared header move it one place per press and save the order")]
    public async Task ArrowKeysMoveAColumnOfTheSharedHeader()
    {
        (BunitContext ctx, Workspace workspace, IRenderedComponent<BoardPage> board, _, string without, _) = await TwoProjectsOneWithResearch();
        await using (ctx)
        {
            board.FireWhenReady(".head-row section.col.head-only[data-key='Research']", e => e.Click(new MouseEventArgs { ShiftKey = true }));
            board.KeyDownWhenReady(".board-wrap", "ArrowLeft");
            board.WaitForAssertion(() => Assert.Equal(["Backlog", "Planned", "Confirmed", "In Progress", "Blocked", "Research", "In Review", "Completed"], HeadOrder(board)));

            board.KeyDownWhenReady(".board-wrap", "ArrowLeft");
            board.WaitForAssertion(() => Assert.Equal(ResearchBeforeBlocked, HeadOrder(board)));
            Assert.Equal(ResearchBeforeBlocked, CellOrder(board, without));
            Assert.Equal(ResearchBeforeBlocked, SavedOrder(ctx, workspace));

            board.KeyDownWhenReady(".board-wrap", "ArrowRight");
            board.WaitForAssertion(() => Assert.Equal(["Backlog", "Planned", "Confirmed", "In Progress", "Blocked", "Research", "In Review", "Completed"], HeadOrder(board)));
        }
    }

    [Fact(DisplayName = "With one project open, Left on a Shift-clicked status column moves it one place and Right moves it back, and a drop in front of another column puts it there")]
    public async Task ArrowKeysAndDragMoveAColumnOfOneProject()
    {
        (BunitContext ctx, Workspace workspace, _) = NewContext();
        await using (ctx)
        {
            Assert.True(workspace.In(repo1.Root, () => WorkItems.WorkItemsApi.AddStatus("Research", beforeCompleted: true)).Success);
            IRenderedComponent<BoardPage> board = ctx.Render<BoardPage>();
            List<string> Keys() => [.. board.FindAll("section.col").Select(c => c.GetAttribute("data-key")!)];

            board.FireWhenReady("section.col[data-key='Research'] header.col-h", e => e.Click(new MouseEventArgs { ShiftKey = true }));
            board.KeyDownWhenReady(".board-wrap", "ArrowLeft");
            board.WaitForAssertion(() => Assert.Equal(["Backlog", "Planned", "Confirmed", "In Progress", "Blocked", "Research", "In Review", "Completed"], Keys()));
            board.KeyDownWhenReady(".board-wrap", "ArrowRight");
            board.WaitForAssertion(() => Assert.Equal(["Backlog", "Planned", "Confirmed", "In Progress", "Blocked", "In Review", "Research", "Completed"], Keys()));

            board.Find("section.col[data-key='Research'] header.col-h").DragStart();
            board.Find("section.col[data-key='Blocked']").Drop();
            board.WaitForAssertion(() => Assert.Equal(ResearchBeforeBlocked, Keys()));
            Assert.Equal(ResearchBeforeBlocked, SavedOrder(ctx, workspace));
        }
    }

    [Fact(DisplayName = "The list shows a Research group for the project that has the status and none for the project that has not")]
    public async Task TheListHasNoGroupForAMissingStatus()
    {
        (BunitContext ctx, Workspace workspace, _, string with, string without, _) = await TwoProjectsOneWithResearch();
        await using (ctx)
        {
            SettingsStore settings = ctx.Services.GetRequiredService<SettingsStore>();
            settings.SaveBoard(workspace.ViewRoot!, settings.Board(workspace.ViewRoot!) with { View = "list", ListGroupBy = "status" });
            IRenderedComponent<BoardPage> list = ctx.Render<BoardPage>();

            list.WaitForAssertion(() => Assert.NotEmpty(list.FindAll("tr[data-id]")));
            List<string> researchRows = list.FindAll("tr[data-group]").Where(r => r.TextContent.Contains("Research")).Select(r => r.TextContent).ToList();
            Assert.Single(researchRows);   // one project's group, not both
            Assert.Equal(2, list.FindAll("tr[data-group^='proj:']").Count);
            Assert.NotEmpty(list.FindAll("tr[data-group^='proj:']").Where(r => r.TextContent.Contains(with)));
            Assert.NotEmpty(list.FindAll("tr[data-group^='proj:']").Where(r => r.TextContent.Contains(without)));
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
            settings.SaveBoard(workspace.ViewRoot!, settings.Board(workspace.ViewRoot!) with { View = "list", ListSettings = oneProject });

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
            board.FireWhenReady(".proj-h h2 button", e => e.Click(), all => all[1]);
            Assert.Equal(["Collapse", "Make it the active project", "Hide project"], Items());
            board.FireWhenReady(".menu-item", e => e.Click(), all => all.Single(i => i.TextContent.Trim() == "Collapse"));
            board.WaitForAssertion(() => Assert.Empty(board.FindAll(".proj")[1].QuerySelectorAll("section.col")));
            Assert.NotEmpty(board.FindAll(".proj")[0].QuerySelectorAll("section.col"));

            board.FireWhenReady(".proj-h h2 button", e => e.Click(), all => all[1]);
            Assert.Equal(["Expand", "Make it the active project", "Hide project"], Items());
            board.FireWhenReady(".menu-item", e => e.Click(), all => all.Single(i => i.TextContent.Trim() == "Expand"));
            board.WaitForAssertion(() => Assert.NotEmpty(board.FindAll(".proj")[1].QuerySelectorAll("section.col")));

            board.FireWhenReady(".proj-h h2 button", e => e.Click(), all => all[0]);
            Assert.Equal(["Collapse", "Hide project"], Items());   // the active project has nothing to switch to
            board.FireWhenReady(".menu-item", e => e.Click(), all => all.Single(i => i.TextContent.Trim() == "Hide project"));
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
            settings.SaveBoard(workspace.ViewRoot!, settings.Board(workspace.ViewRoot!) with { View = "list" });
            IRenderedComponent<BoardPage> board = ctx.Render<BoardPage>();
            board.WaitForAssertion(() => Assert.Equal(2, board.FindAll("tr[data-group^='proj:'] button.proj-name-btn").Count));
            int open = board.FindAll("tr[data-id]").Count;
            Assert.True(open > 0);

            board.FireWhenReady("tr[data-group^='proj:'] button.proj-name-btn", e => e.Click(new MouseEventArgs { ShiftKey = true }), all => all[1]);
            board.WaitForAssertion(() => Assert.Single(board.FindAll("tr[data-group^='proj:']").Where(g => RowMarkProbe.GroupSelected(board, g))));
            Assert.Empty(board.FindAll(".menu-pop"));

            board.FireWhenReady("tr[data-group^='proj:'] button.proj-name-btn", e => e.Click(), all => all[1]);
            board.WaitForAssertion(() => Assert.Equal(["Collapse", "Make it the active project", "Hide project"], board.FindAll(".menu-pop .menu-item").Select(i => i.TextContent.Trim())));   // Click returns before the handler under load
            board.FireWhenReady(".menu-item", e => e.Click(), all => all.Single(i => i.TextContent.Trim() == "Collapse"));
            board.WaitForAssertion(() => Assert.True(board.FindAll("tr[data-id]").Count < open));

            board.FireWhenReady("tr[data-group^='proj:'] button.proj-name-btn", e => e.Click(), all => all[1]);
            board.WaitForAssertion(() => Assert.Contains(board.FindAll(".menu-item"), i => i.TextContent.Trim() == "Expand"));
            board.FireWhenReady(".menu-item", e => e.Click(), all => all.Single(i => i.TextContent.Trim() == "Expand"));
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

            board.FireWhenReady(".proj-h h2 button", e => e.Click(new MouseEventArgs { ShiftKey = true }), all => all[1]);   // on the name itself
            board.WaitForAssertion(() => Assert.Contains("selected", board.FindAll(".proj-h")[1].ClassName));
            Assert.Empty(board.FindAll(".menu-pop"));

            board.FireWhenReady(".proj-h h2 button", e => e.Click(), all => all[1]);   // a plain click opens the menu and leaves the selection as it was
            board.WaitForAssertion(() => Assert.Single(board.FindAll(".menu-pop")));
            Assert.Contains("selected", board.FindAll(".proj-h")[1].ClassName);
            Assert.Equal(repo1.Root, workspace.Root);

            board.FireWhenReady(".menu-item", e => e.Click(), all => all.Single(i => i.TextContent.Trim() == "Make it the active project"));
            board.WaitForAssertion(() => Assert.Equal(repo2.Root, workspace.Root));
        }
    }

    // ── events.log is not ignored ──

    private static string Git(string dir, params string[] args)
    {
        System.Diagnostics.ProcessStartInfo psi = new("git") { WorkingDirectory = dir, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (string a in args) psi.ArgumentList.Add(a);
        using System.Diagnostics.Process p = System.Diagnostics.Process.Start(psi)!;
        string output = p.StandardOutput.ReadToEnd();
        p.WaitForExit();
        return output;
    }

    private static int Occurrences(string text, string line) => text.Replace("\r", "").Split('\n').Count(l => l.Trim() == line);

    [Fact(DisplayName = "Opening a repository whose .gitignore ignores *.log adds an exception for events.log once, and git then sees the events file")]
    public async Task EventsLogIsUnignored()
    {
        (BunitContext ctx, Workspace workspace, _) = NewContext();
        await using (ctx)
        {
            Git(repo2.Root, "init", "-q");
            repo2.Write(".gitignore", "bin/\r\n*.log");   // CRLF, and no newline at the end
            repo2.Write("openspec/changes/some-change/events.log", "2026-11-10T10:00:00 | me | created | x\n");
            workspace.TakeGuideNotice();

            Assert.True(workspace.OpenTab(repo2.Root).Success);

            string ignore = repo2.Read(".gitignore");
            Assert.Equal(1, Occurrences(ignore, "!openspec/**/events.log"));
            Assert.StartsWith("bin/\r\n*.log\r\n", ignore);   // the file's own line endings, the old text kept
            Assert.DoesNotContain("\n", ignore.Replace("\r\n", ""));
            Assert.EndsWith("\r\n", ignore);
            Assert.Contains("openspec/changes/some-change/events.log", Git(repo2.Root, "status", "--porcelain", "--untracked-files=all"));
            string? notice = workspace.TakeGuideNotice();
            Assert.Contains("events.log", notice);
            Assert.Contains("git add openspec", notice);

            workspace.Open(repo1.Root);
            workspace.OpenTab(repo2.Root);   // opened again: nothing more to add or say
            Assert.Equal(ignore, repo2.Read(".gitignore"));
            Assert.DoesNotContain("git add openspec", workspace.TakeGuideNotice() ?? "");
        }
    }

    [Fact(DisplayName = "A repository that does not ignore events.log, or is not a git repository, is left untouched")]
    public async Task EventsLogLeftAlone()
    {
        (BunitContext ctx, Workspace workspace, _) = NewContext();
        await using (ctx)
        {
            repo1.Write(".gitignore", "*.log\n");   // ignores them, but this is not a git repository
            workspace.Open(repo1.Root);
            Assert.Equal("*.log\n", repo1.Read(".gitignore"));

            Git(repo2.Root, "init", "-q");
            repo2.Write(".gitignore", "bin/\n");   // a repository that ignores nothing of ours
            workspace.OpenTab(repo2.Root);
            Assert.Equal("bin/\n", repo2.Read(".gitignore"));

            string bare = Directory.CreateTempSubdirectory("rtm-nogitignore").FullName;   // no .gitignore at all: none is created
            try
            {
                Git(bare, "init", "-q");
                Directory.CreateDirectory(Path.Combine(bare, "openspec", "changes"));
                workspace.OpenTab(bare);
                Assert.False(File.Exists(Path.Combine(bare, ".gitignore")));
            }
            finally { workspace.Open(repo1.Root); try { Directory.Delete(bare, true); } catch (IOException) { } }
        }
    }

    [Fact(DisplayName = "When events.log stays ignored after the exception the app says so and does not add the line again")]
    public async Task EventsLogStaysIgnored()
    {
        (BunitContext ctx, Workspace workspace, _) = NewContext();
        await using (ctx)
        {
            Git(repo2.Root, "init", "-q");
            repo2.Write(".gitignore", "/openspec/changes/\n");   // git cannot re-include a file under an ignored directory
            workspace.TakeGuideNotice();

            workspace.OpenTab(repo2.Root);

            Assert.Contains("still ignored", workspace.TakeGuideNotice());
            Assert.Equal(1, Occurrences(repo2.Read(".gitignore"), "!openspec/**/events.log"));

            workspace.Open(repo1.Root);
            workspace.OpenTab(repo2.Root);   // a later open: the line is the "already tried" marker

            Assert.Equal(1, Occurrences(repo2.Read(".gitignore"), "!openspec/**/events.log"));
            Assert.Contains("still ignored", workspace.TakeGuideNotice());
        }
    }

    // ── #wi19: opening another project keeps the arrangement ──

    /// <summary>Project one alone on the board with a Research status the user has dragged in front of Backlog; returns the context.</summary>
    private (BunitContext Ctx, Workspace Workspace) ResearchFirstInProjectOne(string status = "Research")
    {
        (BunitContext ctx, Workspace workspace, _) = NewContext();
        Assert.True(workspace.In(repo1.Root, () => WorkItems.WorkItemsApi.AddStatus(status, beforeCompleted: true)).Success);
        IRenderedComponent<BoardPage> board = ctx.Render<BoardPage>();
        board.Find($"section.col[data-key='{status}'] header.col-h").DragStart();
        board.Find("section.col[data-key='Backlog']").Drop();
        board.WaitForAssertion(() => Assert.Equal(status, board.FindAll("section.col").First().GetAttribute("data-key")));
        return (ctx, workspace);
    }

    [Fact(DisplayName = "Opening a second project keeps the first project's column arrangement: Research stays first")]
    public async Task OpeningASecondProjectKeepsTheArrangement()
    {
        (BunitContext ctx, Workspace workspace) = ResearchFirstInProjectOne();
        await using (ctx)
        {
            Assert.True(workspace.OpenTab(repo2.Root).Success);   // the import of another project

            Assert.Equal(repo1.Root, workspace.ViewRoot);
            IRenderedComponent<BoardPage> board = ctx.Render<BoardPage>();
            board.WaitForAssertion(() => Assert.Equal("Research", HeadOrder(board).First()));
            Assert.Equal(["Research", "Backlog", "Planned", "Confirmed", "In Progress", "Blocked", "In Review", "Completed"], HeadOrder(board));
        }
    }

    [Fact(DisplayName = "A status only the second project has appears where its own list puts it, without moving the user's arrangement")]
    public async Task AStatusOnlyTheSecondProjectHasKeepsTheArrangement()
    {
        (BunitContext ctx, Workspace workspace) = ResearchFirstInProjectOne("Review2");
        await using (ctx)
        {
            Assert.True(workspace.OpenTab(repo2.Root).Success);
            Assert.True(workspace.In(repo2.Root, () => WorkItems.WorkItemsApi.AddStatus("Research", beforeCompleted: true)).Success);

            IRenderedComponent<BoardPage> board = ctx.Render<BoardPage>();
            board.WaitForAssertion(() => Assert.Equal(repo1.Root, workspace.ViewRoot));
            List<string> head = HeadOrder(board);
            Assert.Equal(["Review2", "Backlog", "Planned", "Confirmed", "In Progress", "Blocked", "In Review"], head.Take(7));   // the arrangement is untouched
            Assert.Equal(["Research", "Completed"], head.Skip(7));   // the new status sits before Completed, where project two's list puts it
        }
    }

    [Fact(DisplayName = "Choosing the second project's tab still switches the view to its own options")]
    public async Task ChoosingATabSwitchesTheView()
    {
        (BunitContext ctx, Workspace workspace) = ResearchFirstInProjectOne();
        await using (ctx)
        {
            Assert.True(workspace.OpenTab(repo2.Root).Success);
            Assert.Equal(repo1.Root, workspace.ViewRoot);
            Assert.True(workspace.OpenTab(repo1.Root).Success);
            Assert.True(workspace.OpenTab(repo2.Root).Success);   // the tab of an open project: choosing it

            Assert.Equal(repo2.Root, workspace.ViewRoot);
            IRenderedComponent<BoardPage> board = ctx.Render<BoardPage>();
            board.WaitForAssertion(() => Assert.Equal("Backlog", HeadOrder(board).First()));   // project two has no arrangement of its own: not project one's
        }
    }
}
