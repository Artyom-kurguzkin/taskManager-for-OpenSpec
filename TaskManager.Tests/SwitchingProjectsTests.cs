using Bunit;
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

    private (BunitContext Ctx, Workspace Workspace, IRenderedComponent<SideNav> Nav) NewContext()
    {
        BunitContext ctx = new();
        ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        ctx.Services.AddSingleton<UiService>();
        ctx.Services.AddSingleton(new SettingsStore(settingsFile));
        ctx.Services.AddSingleton<Workspace>();
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
            Assert.Contains("due:", text);   // start and due dates are part of the conventions
            Assert.Contains("REPO-TASKS.md", workspace.TakeGuideNotice());   // the UI says so once (a toast), not on every open
            Assert.Null(workspace.TakeGuideNotice());

            repo2.Write("openspec/REPO-TASKS.md", "my own notes\n");   // not ours: no marker
            workspace.OpenTab(repo2.Root);
            Assert.Equal("my own notes\n", repo2.Read("openspec/REPO-TASKS.md"));

            File.WriteAllText(guide, "<!-- repo-tasks-guide v0 -->\nold text\n");   // ours, but older
            workspace.Open(repo1.Root);
            Assert.Equal(text, File.ReadAllText(guide));
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

    [Fact(DisplayName = "Remove a project…, the last entry of the repository menu, asks which project to remove and closes that tab (files stay)")]
    public async Task RemoveProjectFromMenu()
    {
        (BunitContext ctx, Workspace workspace, IRenderedComponent<SideNav> nav) = NewContext();
        await using (ctx)
        {
            workspace.OpenTab(repo2.Root);
            nav.Find("button.tabs-btn").Click();
            Assert.Equal("Remove a project…", nav.FindAll(".menu-item").Last().TextContent.Trim());

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
}
