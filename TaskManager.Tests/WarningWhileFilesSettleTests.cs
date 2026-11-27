using BoardSync;
using BoardSync.PublicDTOs;
using Bunit;
using GitHistory;
using Microsoft.Extensions.DependencyInjection;
using OpenSpecStore;
using Radzen;
using TaskManager.AppState;
using TaskManager.BackgroundJobs;
using TaskManager.Components.Pages;
using TaskManager.Helpers;

namespace TaskManager.Tests;

/// <summary>
/// Files written in the last ten seconds are left for the next pass (the settle window). Until now the state line said "Nothing new" and closing the app took what was settled and left
/// the rest. Now the line says how many files wait and in which folder, "Sync now" says the same, and closing the app while files settle asks first: "Wait and sync" or "Close anyway".
/// The engine is a fake that leaves a file alone until the injected clock is ten seconds past its write; the wait is injected too, so no test sleeps. The close dialog's decision lives in
/// <see cref="CloseGuard"/>, which the tests drive; the window's close button only calls it.
/// </summary>
public sealed class WarningWhileFilesSettleTests : IDisposable
{
    private const string Folder = "add-health-check";
    private static readonly string[] Written = [Folder + "/proposal.md", Folder + "/tasks.md"];

    private readonly string temp = Directory.CreateTempSubdirectory("rtm-settle-warn-").FullName;
    private readonly TestRepo repo = new();
    private readonly UiService ui = new() { ToastLifetime = Timeout.InfiniteTimeSpan };
    private readonly List<IDisposable> disposables = [];
    private readonly List<string> uploaded = [];
    private readonly List<TimeSpan> waits = [];
    private DateTime now = new(2026, 11, 27, 10, 0, 0, DateTimeKind.Utc);
    private DateTime? writtenAt;   // null: nothing was written yet
    private BoardStorageSettings settings = null!;
    private BoardSyncService sync = null!;

    private string Storage => Path.Combine(temp, "storage");

    public void Dispose()
    {
        foreach (IDisposable d in disposables) d.Dispose();
        sync?.Dispose();
        repo.Dispose();
        try { Directory.Delete(temp, true); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }

    /// <summary>A pass leaves the board's files alone until the clock is a settle window past their write, then takes them.</summary>
    private Task<SyncReport> Engine(SyncOptions o)
    {
        if (writtenAt is not { } at) return Task.FromResult(new SyncReport(SyncOutcome.NothingToDo, null, [], [], [], [], [], [], []));
        List<string> left = [.. now - at < o.SettleWindow ? Written : []];
        List<string> take = [.. Written.Except(left).Except(uploaded)];
        uploaded.AddRange(take);
        return Task.FromResult(new SyncReport(take.Count > 0 ? SyncOutcome.Synced : SyncOutcome.NothingToDo, null, take, [], [], [], [], [], [], settling: left));
    }

    private void Start()
    {
        OpenSpecStoreApi.Open(repo.Root, keepOthers: true);
        settings = new BoardStorageSettings(Path.Combine(temp, "data"), StorageProviders.Builtin);
        SyncSetup timing = new(TimeSpan.FromMinutes(2), Engine, () => now)
        {
            RealTimers = false,
            Jitter = () => 0,
            SettleWindow = TimeSpan.FromSeconds(10),
            Wait = (span, _) => { waits.Add(span); now += span; return Task.CompletedTask; },   // the clock moves, nothing sleeps
        };
        sync = new BoardSyncService(settings, ui, timing, new SettingsStore(Path.Combine(temp, "app.json")));
        sync.InProject = (root, work) => { OpenSpecStoreApi.Use(root); GitHistoryApi.Configure(root); work(); };
        sync.Attach(repo.Root);
        Assert.True(settings.Connect(repo.Root, "folder").Success);
        Assert.True(settings.SetValue(repo.Root, "path", Storage).Success);
        sync.LastPass(repo.Root).GetAwaiter().GetResult();
    }

    /// <summary>Two files of one change folder were written a second ago and a sync runs now.</summary>
    private async Task WrittenJustNow()
    {
        Start();
        writtenAt = now.AddSeconds(-1);
        await sync.SyncNow(repo.Root);
    }

    [Fact(DisplayName = "The state line names the files still settling")]
    public async Task TheStateLineNamesTheFilesStillSettling()
    {
        await WrittenJustNow();

        Assert.Equal("Waiting for 2 files to settle (folder add-health-check)", sync.Status(repo.Root).Line);   // not "Nothing new", not "Waiting to sync at"
        SettlingFolder settling = Assert.Single(sync.Settling());
        Assert.Equal(Folder, settling.Folder);
        Assert.Equal(Written, settling.Files);

        now = now.AddSeconds(10);                                       // the window passes and the pass that follows takes them
        await sync.QuietElapsed(repo.Root);
        Assert.StartsWith("Synced at", sync.Status(repo.Root).Line);
        Assert.Empty(sync.Settling());
    }

    [Fact(DisplayName = "Closing with files still settling warns and names them")]
    public async Task ClosingWithFilesStillSettlingWarnsAndNamesThem()
    {
        await WrittenJustNow();
        CloseGuard guard = new(sync, ui);

        Task<bool> closing = guard.MayClose();
        DialogRequest d = Assert.IsType<DialogRequest>(ui.Dialog);
        Assert.Equal("Files are still settling", d.Title);
        Assert.Contains("add-health-check/proposal.md", d.Text);
        Assert.Contains("add-health-check/tasks.md", d.Text);
        Assert.Contains("folder add-health-check", d.Text);
        Assert.Equal(["Wait and sync", "Close anyway"], d.Choices!.Select(c => c.Label));
        Assert.False(closing.IsCompleted);                              // the window stays open until the user answers

        ui.Close(CloseGuard.CloseAnyway);
        Assert.True(await closing);
        Assert.Empty(waits);                                            // nothing was waited for
        Assert.Empty(uploaded);

        Task<bool> again = guard.MayClose();                            // the same question, answered by closing the dialog: the window stays
        ui.Close(null);
        Assert.False(await again);

        now = now.AddSeconds(10);
        await sync.QuietElapsed(repo.Root);                             // the files settled: no question at all
        Assert.True(await guard.MayClose());
        Assert.Null(ui.Dialog);
    }

    [Fact(DisplayName = "Wait and sync closes after the files settle")]
    public async Task WaitAndSyncClosesAfterTheFilesSettle()
    {
        await WrittenJustNow();
        CloseGuard guard = new(sync, ui);

        Task<bool> closing = guard.MayClose();
        ui.Close(CloseGuard.WaitAndSync);
        Assert.True(await closing);

        Assert.Equal(Written, uploaded.Order());                         // both files went before the window closed
        Assert.Empty(sync.Settling());
        Assert.Equal([TimeSpan.FromSeconds(10)], waits);                 // one settle window of the injected clock, no real sleep
        Assert.Null(ui.Dialog);
    }

    [Fact(DisplayName = "Closing right after a merge warns even though no sync pass ran yet")]
    public async Task ClosingRightAfterAMergeWarnsEvenThoughNoSyncPassRanYet()
    {
        Start();                                                        // the first pass found nothing: it left nothing settling
        int runs = sync.Runs(repo.Root);
        string changes = Path.Combine(repo.Root, "openspec", "changes", Folder);
        Directory.CreateDirectory(changes);
        foreach (string file in Written)                                // a merge in the app wrote these a second ago; the quiet wait before a pass is minutes
        {
            string path = Path.Combine(repo.Root, "openspec", "changes", file.Replace('/', Path.DirectorySeparatorChar));
            File.WriteAllText(path, "merged");
            File.SetLastWriteTimeUtc(path, now.AddSeconds(-1));
        }
        CloseGuard guard = new(sync, ui);

        Assert.Equal(runs, sync.Runs(repo.Root));                       // no pass ran
        Assert.Equal("Waiting for 2 files to settle (folder add-health-check)", sync.Status(repo.Root).Line);
        SettlingFolder settling = Assert.Single(sync.Settling());
        Assert.Equal(Folder, settling.Folder);
        Assert.Equal(Written, settling.Files);

        Task<bool> closing = guard.MayClose();
        DialogRequest d = Assert.IsType<DialogRequest>(ui.Dialog);
        Assert.Equal("Files are still settling", d.Title);
        Assert.Contains("add-health-check/proposal.md", d.Text);
        ui.Close(CloseGuard.CloseAnyway);
        Assert.True(await closing);

        now = now.AddSeconds(10);                                       // the window passes with no pass in between: nothing is asked
        Assert.Empty(sync.Settling());
        Assert.True(await guard.MayClose());
        Assert.Null(ui.Dialog);
    }

    [Fact(DisplayName = "Sync now with files still settling says so")]
    public async Task SyncNowWithFilesStillSettlingSaysSo()
    {
        Start();
        BunitContext ctx = new();
        disposables.Add(ctx);
        ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        ctx.Services.AddSingleton(ui);
        ctx.Services.AddRadzenComponents();
        ctx.Services.AddSingleton(new SettingsStore(Path.Combine(temp, "settings.json")));
        ctx.Services.AddSingleton(settings);
        ctx.Services.AddSingleton(sync);
        ctx.Services.AddSingleton<Workspace>();
        Assert.True(ctx.Services.GetRequiredService<Workspace>().Open(repo.Root).Success);
        sync.LastPass(repo.Root).GetAwaiter().GetResult();
        IRenderedComponent<SettingsPage> page = ctx.Render<SettingsPage>().OnProject();
        Assert.DoesNotContain("settle", page.Find("[data-storage] p.bs-state").TextContent);

        writtenAt = now.AddSeconds(-1);
        page.Find("[data-storage] .bs-sync").Click();
        await sync.LastPass(repo.Root);
        page.WaitForAssertion(() => Assert.Equal("Waiting for 2 files to settle (folder add-health-check)", page.Find("[data-storage] p.bs-state").TextContent.Trim()));
    }

    [Fact(DisplayName = "A file the last pass reported stops counting once the window has passed")]
    public async Task AFileTheLastPassReportedStopsCountingOnceTheWindowHasPassed()
    {
        await WrittenJustNow();
        Assert.NotEmpty(sync.Settling());
        Assert.StartsWith("Waiting for 2 files to settle", sync.Status(repo.Root).Line);

        now = now.AddSeconds(60);                                       // far past the ten seconds, and no pass has run since: the next one is minutes away

        Assert.Empty(sync.Settling());                                  // the report of the last pass is old news; nothing was written inside the window
        Assert.DoesNotContain("Waiting for", sync.Status(repo.Root).Line);
        CloseGuard guard = new(sync, ui);
        Assert.True(await guard.MayClose());
        Assert.Null(ui.Dialog);
    }

    [Fact(DisplayName = "A file the sync downloaded does not count as settling")]
    public async Task AFileTheSyncDownloadedDoesNotCountAsSettling()
    {
        using TestRepo repoB = new();
        OpenSpecStoreApi.Open(repoB.Root, keepOthers: true);
        BoardSyncService Service(TestRepo r, string name)
        {
            BoardStorageSettings s = new(Path.Combine(temp, "data-" + name), StorageProviders.Builtin);
            SyncSetup timing = new(TimeSpan.FromMinutes(2), o => BoardSyncApi.Sync(o), null) { RealTimers = false, Jitter = () => 0, SettleWindow = TimeSpan.FromSeconds(10) };
            BoardSyncService svc = new(s, ui, timing, new SettingsStore(Path.Combine(temp, "app-" + name + ".json")))
            {
                InProject = (root, work) => { OpenSpecStoreApi.Use(root); GitHistoryApi.Configure(root); work(); },
            };
            disposables.Add(svc);
            svc.Attach(r.Root);
            Assert.True(s.Connect(r.Root, "folder").Success);
            Assert.True(s.SetValue(r.Root, "path", Storage).Success);
            svc.LastPass(r.Root).GetAwaiter().GetResult();
            return svc;
        }
        BoardSyncService a = Service(repo, "a"), b = Service(repoB, "b");

        string file = Path.Combine("openspec", "changes", Folder, "proposal.md");
        string onB = Path.Combine(repoB.Root, file);
        Directory.CreateDirectory(Path.GetDirectoryName(onB)!);
        File.WriteAllText(onB, "written on the other computer");
        File.SetLastWriteTimeUtc(onB, DateTime.UtcNow.AddHours(-1));    // long settled there
        await b.SyncNow(repoB.Root);
        await a.SyncNow(repo.Root);                                     // A downloads it: the sync writes the file now, so its time is now

        Assert.Equal("written on the other computer", File.ReadAllText(Path.Combine(repo.Root, file)));
        Assert.Empty(a.Settling());                                     // the sync's own write is not a change waiting for the window
        Assert.DoesNotContain("Waiting for", a.Status(repo.Root).Line);
    }
}
