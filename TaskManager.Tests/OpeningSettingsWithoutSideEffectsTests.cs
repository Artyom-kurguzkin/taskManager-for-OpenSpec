using BoardSync;
using BoardSync.PublicDTOs;
using Bunit;
using GitHistory;
using Microsoft.Extensions.DependencyInjection;
using OpenSpecStore;
using Radzen;
using TaskManager.Components.Pages;
using TaskManager.Components.Shared;
using TaskManager.AppState;
using TaskManager.BackgroundJobs;
using TaskManager.Helpers;

namespace TaskManager.Tests;

/// <summary>
/// Looking at Settings changes nothing: the page draws the sync line and the storage fields from what was worked out when something last changed (a setting saved, a sync pass ended),
/// not from the disk on every redraw. Writes are detected by comparing every file of the repository and of the app-data folder (path, size, write time) before and after; the
/// app-data files are first dated to 2001, so a rewrite that lands in the same clock tick as the original still shows. A file system watcher would report asynchronously and a test could not
/// say "nothing came" without waiting; a before/after comparison is deterministic. Redraws are forced through the section's own component, with no waiting anywhere: the page is
/// flushed with an empty InvokeAsync, which runs after everything the sync service queued before it.
/// </summary>
public sealed class OpeningSettingsWithoutSideEffectsTests : IDisposable
{
    private readonly string temp = Directory.CreateTempSubdirectory("rtm-settings-open-").FullName;
    private readonly TestRepo repo = new();
    private readonly UiService ui = new() { ToastLifetime = Timeout.InfiniteTimeSpan };
    private readonly BoardStorageSettings settings;
    private readonly BoardSyncService sync;
    private readonly List<IDisposable> disposables = [];
    private Func<SyncOptions, Task<SyncReport>>? engine;

    private string Data => Path.Combine(temp, "data");
    private string Storage => Path.Combine(temp, "storage");

    public OpeningSettingsWithoutSideEffectsTests()
    {
        OpenSpecStoreApi.Open(repo.Root, keepOthers: true);
        settings = new BoardStorageSettings(Data, StorageProviders.Builtin);
        SyncSetup timing = new(TimeSpan.FromMinutes(2), o => engine is not null ? engine(o) : BoardSyncApi.Sync(o)) { RealTimers = false, Jitter = () => 0 };
        sync = new BoardSyncService(settings, ui, timing, new SettingsStore(Path.Combine(temp, "app.json")));
        sync.InProject = (root, work) => { OpenSpecStoreApi.Use(root); GitHistoryApi.Configure(root); work(); };
        sync.Attach(repo.Root);
        Assert.True(settings.Connect(repo.Root, "folder").Success);
        Assert.True(settings.SetValue(repo.Root, "path", Storage).Success);
        sync.LastPass(repo.Root).GetAwaiter().GetResult();   // the pass the connection started has ended: nothing runs in the background from here on
    }

    public void Dispose()
    {
        foreach (IDisposable d in disposables) d.Dispose();
        sync.Dispose();
        repo.Dispose();
        try { Directory.Delete(temp, true); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }

    private BunitContext PageContext()
    {
        BunitContext ctx = new();
        disposables.Add(ctx);
        ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        ctx.Services.AddSingleton(ui);
        ctx.Services.AddRadzenComponents();
        ctx.Services.AddSingleton(new SettingsStore(Path.Combine(temp, "settings-" + Guid.NewGuid().ToString("N") + ".json")));
        ctx.Services.AddSingleton(settings);
        ctx.Services.AddSingleton(sync);
        ctx.Services.AddSingleton<Workspace>();
        Assert.True(ctx.Services.GetRequiredService<Workspace>().Open(repo.Root).Success);   // attaches the project: one pass
        sync.LastPass(repo.Root).GetAwaiter().GetResult();
        return ctx;
    }

    private static AngleSharp.Dom.IElement Section(IRenderedComponent<SettingsPage> page) => page.Find("[data-storage]");

    private static string Line(IRenderedComponent<SettingsPage> page) => Section(page).QuerySelector("p.bs-state")!.TextContent.Trim();

    /// <summary>Draws the storage section again, as often as asked, the way any event of the page would.</summary>
    private static void Redraw(IRenderedComponent<SettingsPage> page, int times)
    {
        IRenderedComponent<BoardStorageSection> section = page.FindComponent<BoardStorageSection>();
        for (int i = 0; i < times; i++) section.Render();
    }

    private static Task Flush(IRenderedComponent<SettingsPage> page) => page.InvokeAsync(() => { });

    private static Task Change(IRenderedComponent<SettingsPage> page, string selector, string value)
    {
        page.ChangeWhenReady("[data-storage] " + selector, value);
        return Task.CompletedTask;
    }

    /// <summary>Every file of the repository and of the app-data folder with its size and write time.</summary>
    private List<string> Files() =>
        [.. new[] { repo.Root, Data }.Where(Directory.Exists)
            .SelectMany(d => Directory.EnumerateFiles(d, "*", SearchOption.AllDirectories))
            .Order(StringComparer.Ordinal)
            .Select(f => $"{f} | {new System.IO.FileInfo(f).Length} | {File.GetLastWriteTimeUtc(f).Ticks}")];

    private void DateTheAppData()
    {
        foreach (string f in Directory.EnumerateFiles(Data, "*", SearchOption.AllDirectories)) File.SetLastWriteTimeUtc(f, new DateTime(2001, 1, 1, 0, 0, 0, DateTimeKind.Utc));
    }

    [Fact(DisplayName = "Opening Settings and redrawing it three times writes no file")]
    public async Task OpeningAndRedrawingWritesNothing()
    {
        Directory.CreateDirectory(Path.Combine(repo.Root, ".git"));                       // a git repository: the status looks at the board's files for markers
        repo.Write("openspec/changes/add-health-check/notes/outside.md", "A note a pull brought in; the sync has not seen it yet.\n");   // the status line walks the board: a changed list is what used to make it save its cache
        DateTheAppData();
        await using BunitContext ctx = PageContext();
        DateTheAppData();
        List<string> before = Files();

        IRenderedComponent<SettingsPage> page = ctx.Render<SettingsPage>().OnProject();
        Assert.NotNull(Section(page).QuerySelector(".bs-sync"));                          // the storage section is there
        Redraw(page, 3);
        await Flush(page);

        Assert.Equal(before, Files());
    }

    [Fact(DisplayName = "Settings shows the sync state after a sync pass without reading it again on every redraw")]
    public async Task TheLastWorkedOutStateIsDrawn()
    {
        await using BunitContext ctx = PageContext();
        IRenderedComponent<SettingsPage> page = ctx.Render<SettingsPage>().OnProject();
        Assert.Equal(sync.Status(repo.Root).Line, Line(page));

        int statuses = sync.StatusRuns, configReads = BoardStorageSettings.ConfigReads(repo.Root);
        Redraw(page, 3);
        await Flush(page);
        Assert.Equal(statuses, sync.StatusRuns);                                        // no walk of the board, no git probe, no state file read
        Assert.Equal(configReads, BoardStorageSettings.ConfigReads(repo.Root));          // config.yaml was not read for the toggle, the fields or the placeholders
        Assert.Equal(sync.Status(repo.Root).Line, Line(page));

        engine = _ => Task.FromResult(new SyncReport(SyncOutcome.Failed, "offline", [], [], [], [], [], [], []));
        await sync.SyncNow(repo.Root);                                                   // a pass ended: that is a change, and the line follows
        await Flush(page);
        Assert.Equal("Failed: offline", Line(page));
    }

    [Fact(DisplayName = "A storage-settings change updates the sync line and the fields")]
    public async Task AChangeOfTheSettingsRefreshesTheView()
    {
        await using BunitContext ctx = PageContext();
        IRenderedComponent<SettingsPage> page = ctx.Render<SettingsPage>().OnProject();
        string other = Path.Combine(temp, "other-storage");
        Assert.Equal(Storage, Section(page).QuerySelector("input[data-field='path']")!.GetAttribute("value"));

        await Change(page, "input[data-field='path']", other);
        await sync.LastPass(repo.Root);
        await Flush(page);
        page.Eventually(() => Assert.Equal(other, Section(page).QuerySelector("input[data-field='path']")!.GetAttribute("value")));

        await Change(page, "input[data-field='path']", "");                              // an address that is gone: the project cannot sync
        await sync.LastPass(repo.Root);
        await Flush(page);
        page.Eventually(() => Assert.StartsWith("Fill in", Line(page)));
        page.Eventually(() => Assert.Equal(sync.Status(repo.Root).Line, Line(page)));
        page.Eventually(() => Assert.Equal("", Section(page).QuerySelector("input[data-field='path']")!.GetAttribute("value") ?? ""));
    }
}
