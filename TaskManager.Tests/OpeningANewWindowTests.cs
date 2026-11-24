using Bunit;
using Microsoft.Extensions.DependencyInjection;
using TaskManager.AppState;
using WorkItems;

namespace TaskManager.Tests;

/// <summary>
/// "New window" (Win+Alt+\) opens one more full window of this one process: its own side menu, pages and dialogs, on the address the opener was on. It is not the
/// detached one-item window. The host's part (one Photino window on the UI thread) sits behind <see cref="WindowFactory"/>, shared with the separate-window path.
/// </summary>
public sealed class OpeningANewWindowTests : IDisposable
{
    private readonly TestRepo repo = new();
    private readonly string settingsFile = Path.Combine(Path.GetTempPath(), "rtm-ui-" + Guid.NewGuid().ToString("N") + ".json");
    private readonly List<WindowRequest> built = [];

    private BunitContext MainWindow()
    {
        BunitContext ctx = new();
        ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        ctx.Services.AddSingleton(new UiService { ToastLifetime = Timeout.InfiniteTimeSpan });
        ctx.Services.AddSingleton(new SettingsStore(settingsFile));
        ctx.Services.AddSingleton<Workspace>();
        Assert.True(ctx.Services.GetRequiredService<Workspace>().Open(repo.Root).Success);
        return ctx;
    }

    private ItemWindows Windows(BunitContext main) =>
        new(main.Services.GetRequiredService<Workspace>(), built.Add, () => new UiService { ToastLifetime = Timeout.InfiniteTimeSpan });

    public void Dispose()
    {
        repo.Dispose();
        File.Delete(settingsFile);
    }

    [Fact(DisplayName = "A new window opens the full app on the same address with its own dialogs")]
    public async Task OpensTheFullAppOnTheSameAddress()
    {
        await using BunitContext main = MainWindow();

        (bool ok, string message) = Windows(main).OpenFull("item/123");

        Assert.True(ok, message);
        WindowRequest request = Assert.Single(built);
        Assert.Equal("item/123", request.Mode.StartPath);
        Assert.Equal("Repo tasks", request.Title);
        Assert.NotSame(main.Services.GetRequiredService<UiService>(), request.Ui);   // its own dialogs and toasts
        Assert.NotSame(main.Services.GetService<AppMode>(), request.Mode);
    }

    [Fact(DisplayName = "A new window on the board opens at the board address")]
    public async Task OpensOnTheBoard()
    {
        await using BunitContext main = MainWindow();

        Assert.True(Windows(main).OpenFull("").Success);

        Assert.Equal("", Assert.Single(built).Mode.StartPath);
    }

    [Fact(DisplayName = "A new window is not a detached one-item window")]
    public async Task IsNotDetached()
    {
        await using BunitContext main = MainWindow();

        Assert.True(Windows(main).OpenFull("item/123").Success);

        AppMode mode = Assert.Single(built).Mode;
        Assert.False(mode.Detached);   // the layout draws the side menu and the list
        Assert.Null(mode.Repo);        // it follows the shared active project, like the main window
    }

    [Fact(DisplayName = "A failing window factory comes back as a message, not a crash")]
    public async Task AFailingFactoryIsAMessage()
    {
        await using BunitContext main = MainWindow();
        List<UiService> offered = [];
        ItemWindows windows = new(main.Services.GetRequiredService<Workspace>(), r => { offered.Add(r.Ui); throw new InvalidOperationException("no display"); });

        (bool ok, string message) = windows.OpenFull("");

        Assert.False(ok);
        Assert.Contains("no display", message);
        main.Services.GetRequiredService<Workspace>().Say("for the windows that are open");
        Assert.Empty(Assert.Single(offered).Toasts);   // the failed window is not kept attached
    }

    [Fact(DisplayName = "A notice from the workspace reaches a new window until it is closed")]
    public async Task NoticesReachTheNewWindow()
    {
        await using BunitContext main = MainWindow();
        Workspace workspace = main.Services.GetRequiredService<Workspace>();
        Assert.True(Windows(main).OpenFull("").Success);
        UiService other = built[0].Ui;

        workspace.Say("Hello");
        Assert.Contains(other.Toasts, t => t.Text == "Hello");

        built[0].Closed();
        int before = other.Toasts.Count;
        workspace.Say("Again");
        Assert.Equal(before, other.Toasts.Count);
    }

    [Fact(DisplayName = "Opening a new window starts no second agent endpoint")]
    public void StartsNoSecondEndpoint()
    {
        // The windows class has no way to start one: it takes only the workspace and the window factory, and the endpoint is started once in Program.
        // The real-app check counts the process's listening ports before and after.
        string[] ctor = typeof(ItemWindows).GetConstructors().Single().GetParameters().Select(p => p.ParameterType.Name).ToArray();
        Assert.DoesNotContain(ctor, n => n.Contains("Agent", StringComparison.Ordinal));
    }
}
