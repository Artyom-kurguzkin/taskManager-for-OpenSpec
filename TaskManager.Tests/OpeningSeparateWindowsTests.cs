using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Radzen;
using TaskManager.Components.Pages;
using TaskManager.Components.Shared;
using TaskManager.AppState;
using TaskManager.BackgroundJobs;
using TaskManager.Helpers;
using WorkItems;

namespace TaskManager.Tests;

/// <summary>
/// "Separate window" opens a second window of this one process (not another process): the window has its own dialogs and toasts and its own address, and shares the
/// workspace, the settings and the sync with the main window. The host's part (one Photino window on the UI thread) sits behind <see cref="WindowFactory"/>.
/// </summary>
public sealed class OpeningSeparateWindowsTests : IDisposable
{
    private readonly TestRepo repo = new();
    private readonly string settingsFile = Path.Combine(Path.GetTempPath(), "rtm-ui-" + Guid.NewGuid().ToString("N") + ".json");
    private readonly List<WindowRequest> built = [];

    /// <summary>The main window: its own context with the app's shared workspace and settings.</summary>
    private BunitContext MainWindow(out IRenderedComponent<Overlays> overlays)
    {
        BunitContext ctx = new();
        ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        ctx.Services.AddSingleton(new UiService { ToastLifetime = Timeout.InfiniteTimeSpan });
        ctx.Services.AddRadzenComponents();
        ctx.Services.AddSingleton(new SettingsStore(settingsFile));
        ctx.Services.AddSingleton<Workspace>();
        Assert.True(ctx.Services.GetRequiredService<Workspace>().Open(repo.Root).Success);
        overlays = ctx.Render<Overlays>();
        return ctx;
    }

    private ItemWindows Windows(BunitContext main) =>
        new(main.Services.GetRequiredService<Workspace>(), built.Add, () => new UiService { ToastLifetime = Timeout.InfiniteTimeSpan });

    /// <summary>What the host does with a request: a window with its own provider, the app-wide services being the main window's own instances.</summary>
    private static BunitContext SeparateWindow(BunitContext main, WindowRequest request, out IRenderedComponent<Overlays> overlays)
    {
        BunitContext ctx = new();
        ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        ctx.Services.AddSingleton(request.Ui);
        ctx.Services.AddSingleton(request.Mode);
        ctx.Services.AddRadzenComponents();
        ctx.Services.AddSingleton(main.Services.GetRequiredService<SettingsStore>());
        ctx.Services.AddSingleton(main.Services.GetRequiredService<Workspace>());
        overlays = ctx.Render<Overlays>();
        return ctx;
    }

    public void Dispose()
    {
        repo.Dispose();
        File.Delete(settingsFile);
    }

    [Fact(DisplayName = "Opening an item in a separate window opens a window of this process on that item and project, with its own dialogs, and starts no other process")]
    public async Task OpensAWindowInThisProcess()
    {
        await using BunitContext main = MainWindow(out _);
        ItemWindows windows = Windows(main);

        (bool ok, string message) = windows.Open(repo.Root, 123);

        Assert.True(ok, message);
        WindowRequest request = Assert.Single(built);
        Assert.True(request.Mode.Detached);
        Assert.Equal("item/123", request.Mode.StartPath);
        Assert.Equal(repo.Root, request.Mode.Repo);
        Assert.Equal("#wi123 Health-check endpoint — Repo tasks", request.Title);
        Assert.NotSame(main.Services.GetRequiredService<UiService>(), request.Ui);   // its own dialogs and toasts
        Assert.NotSame(main.Services.GetService<AppMode>(), request.Mode);
    }

    [Fact(DisplayName = "A separate window for an item or a project that is not there is refused with a message and no window is built")]
    public async Task RefusesWhatIsNotThere()
    {
        await using BunitContext main = MainWindow(out _);
        ItemWindows windows = Windows(main);

        (bool ok, string message) = windows.Open(repo.Root, 9999);
        Assert.False(ok);
        Assert.Contains("#wi9999", message);

        (ok, message) = windows.Open(Path.Combine(Path.GetTempPath(), "rtm-not-open"), 123);
        Assert.False(ok);
        Assert.Contains("isn't open", message);

        Assert.Empty(built);
    }

    [Fact(DisplayName = "A window that cannot be built is refused with the reason, and does not keep receiving notices")]
    public async Task AWindowThatFailsToOpen()
    {
        await using BunitContext main = MainWindow(out _);
        List<UiService> offered = [];
        ItemWindows windows = new(main.Services.GetRequiredService<Workspace>(), r => { offered.Add(r.Ui); throw new InvalidOperationException("no display"); });

        (bool ok, string message) = windows.Open(repo.Root, 123);
        Assert.False(ok);
        Assert.Contains("no display", message);

        main.Services.GetRequiredService<Workspace>().Say("for the windows that are open");
        Assert.Empty(Assert.Single(offered).Toasts);
    }

    [Fact(DisplayName = "A toast shows only in the window that caused it")]
    public async Task ToastOnlyInItsOwnWindow()
    {
        await using BunitContext main = MainWindow(out IRenderedComponent<Overlays> mainOverlays);
        Assert.True(Windows(main).Open(repo.Root, 123).Success);
        await using BunitContext other = SeparateWindow(main, built[0], out IRenderedComponent<Overlays> otherOverlays);
        IRenderedComponent<WorkItemPage> mainPage = main.Render<WorkItemPage>(p => p.Add(c => c.Id, 123));
        IRenderedComponent<WorkItemPage> otherPage = other.Render<WorkItemPage>(p => p.Add(c => c.Id, 123));

        otherPage.Find("input.title-in").Change("Health-check endpoint v2");

        otherOverlays.WaitForAssertion(() => Assert.Equal("Saved", otherOverlays.Find(".toast.success").TextContent.Trim()));
        mainPage.WaitForAssertion(() => Assert.Equal("Health-check endpoint v2", mainPage.Find("input.title-in").GetAttribute("value")));   // the main window has the change by now
        Assert.Empty(mainOverlays.FindAll(".toast"));
    }

    [Fact(DisplayName = "A notice from the workspace reaches every open window, and a closed window gets no more")]
    public async Task NoticeReachesEveryWindow()
    {
        await using BunitContext main = MainWindow(out IRenderedComponent<Overlays> mainOverlays);
        Workspace workspace = main.Services.GetRequiredService<Workspace>();
        Assert.True(Windows(main).Open(repo.Root, 123).Success);
        await using BunitContext other = SeparateWindow(main, built[0], out IRenderedComponent<Overlays> otherOverlays);

        EditingWorkItemsTests.Duplicate(repo, "second-check");   // a pull brought a second change numbered #wi123
        workspace.Notify();

        mainOverlays.WaitForAssertion(() => Assert.Contains("#wi123 was used twice", mainOverlays.Find(".toast.warning").TextContent));
        otherOverlays.WaitForAssertion(() => Assert.Contains("#wi123 was used twice", otherOverlays.Find(".toast.warning").TextContent));

        built[0].Closed();   // the window is closed
        UiService closed = other.Services.GetRequiredService<UiService>();
        int before = closed.Toasts.Count;
        workspace.Say("Another notice");

        Assert.Equal(before, closed.Toasts.Count);
        Assert.Contains(main.Services.GetRequiredService<UiService>().Toasts, t => t.Text == "Another notice");
    }

    [Fact(DisplayName = "Both windows see an edit made in the other")]
    public async Task BothWindowsSeeEachOthersEdits()
    {
        await using BunitContext main = MainWindow(out _);
        Assert.True(Windows(main).Open(repo.Root, 123).Success);
        await using BunitContext other = SeparateWindow(main, built[0], out _);
        IRenderedComponent<WorkItemPage> mainPage = main.Render<WorkItemPage>(p => p.Add(c => c.Id, 123));
        IRenderedComponent<WorkItemPage> otherPage = other.Render<WorkItemPage>(p => p.Add(c => c.Id, 123));

        otherPage.Find("input.title-in").Change("Edited in the separate window");
        mainPage.WaitForAssertion(() => Assert.Equal("Edited in the separate window", mainPage.Find("input.title-in").GetAttribute("value")));

        mainPage.Find("input.title-in").Change("Edited in the main window");
        otherPage.WaitForAssertion(() => Assert.Equal("Edited in the main window", otherPage.Find("input.title-in").GetAttribute("value")));
    }

    [Fact(DisplayName = "A separate window keeps to its own project when the main window switches to another one: it reads it and saves to it")]
    public async Task KeepsToItsProject()
    {
        using TestRepo second = new();
        await using BunitContext main = MainWindow(out _);
        Workspace workspace = main.Services.GetRequiredService<Workspace>();
        Assert.True(Windows(main).Open(repo.Root, 123).Success);
        await using BunitContext other = SeparateWindow(main, built[0], out _);
        Assert.True(workspace.OpenTab(second.Root).Success);   // the main window goes to the other project, which also has a #wi123
        workspace.In(second.Root, () => WorkItemsApi.SetTitle(123, "The other project's item"));
        Assert.Equal(second.Root, workspace.Root);

        IRenderedComponent<WorkItemPage> page = other.Render<WorkItemPage>(p => p.Add(c => c.Id, 123));
        Assert.Equal("Health-check endpoint", page.Find("input.title-in").GetAttribute("value"));

        page.Find("input.title-in").Change("Renamed in the separate window");
        page.WaitForAssertion(() => Assert.Equal("Renamed in the separate window", page.Find("input.title-in").GetAttribute("value")));
        Assert.Equal("Renamed in the separate window", workspace.In(repo.Root, () => WorkItemsApi.Get(123)!.Title));
        Assert.Equal("The other project's item", workspace.In(second.Root, () => WorkItemsApi.Get(123)!.Title));
    }
}
