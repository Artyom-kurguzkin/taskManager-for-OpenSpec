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
