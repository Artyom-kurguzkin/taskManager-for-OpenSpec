using System.Text;
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
using WorkItems;
using WorkItems.PublicDTOs;

namespace TaskManager.Tests;

/// <summary>
/// What the sync did on its own is said on the item it concerns (a `sync` event on its events.log); an attachment that is missing is put back unless the item's log says it was removed
/// on purpose; Settings has no "Allow and sync" and shows why a sync stopped; a committed version with other line endings than the working file is still a usable merge base.
/// Two computers are two temporary repositories, two app-data folders and two services over one Folder storage, the way <see cref="SyncingWithConnectedStorageTests"/> builds them.
/// </summary>
public sealed class ReportingWhatTheSyncDidTests : IDisposable
{
    private const string Item = "openspec/changes/add-health-check";
    private const string Pic = "wi123/n123/shot.png";
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0xFF, 0xFE, 0x00, 0xC3, 0x28, 0x80, 0x81, 0x00, 0x42];

    private readonly string temp = Directory.CreateTempSubdirectory("rtm-reporting-").FullName;
    private readonly List<IDisposable> disposables = [];
    private string Storage => Path.Combine(temp, "storage");

    public void Dispose()
    {
        foreach (IDisposable d in disposables) d.Dispose();
        try { Directory.Delete(temp, true); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }

    private sealed class Computer : IDisposable
    {
        public readonly TestRepo Repo = new();
        public readonly string Data;
        public readonly UiService Ui = new() { ToastLifetime = Timeout.InfiniteTimeSpan };
        public readonly SettingsStore App;
        public readonly BoardStorageSettings Settings;
        public readonly BoardSyncService Sync;
        public Func<SyncOptions, Task<SyncReport>>? Engine;

        public Computer(string temp, string name)
        {
            Data = Path.Combine(temp, "data-" + name);
            App = new SettingsStore(Path.Combine(temp, "app-" + name + ".json"));
            OpenSpecStoreApi.Open(Repo.Root, keepOthers: true);
            Settings = new(Data, StorageProviders.Builtin);
            SyncSetup timing = new(TimeSpan.FromMinutes(2), o => Engine is not null ? Engine(o) : BoardSyncApi.Sync(o), null) { RealTimers = false, Jitter = () => 0 };
            Sync = new(Settings, Ui, timing, App);
            Settings.Changed += () => Sync.LastPass(Repo.Root).GetAwaiter().GetResult();
            Sync.InProject = In;
            Sync.Attach(Repo.Root);
        }

        public string Root => Repo.Root;

        public static void In(string root, Action work)
        {
            OpenSpecStoreApi.Use(root);
            GitHistoryApi.Configure(root);
            work();
        }

        public T Do<T>(Func<T> work)
        {
            T result = default!;
            In(Root, () => result = work());
            return result;
        }

        public void ConnectFolder(string folder)
        {
            Assert.True(Settings.Connect(Root, "folder").Success);
            Assert.True(Settings.SetValue(Root, "path", folder).Success);
        }

        public string AttachDir => Path.GetFullPath(Path.Combine(Root, App.AttachmentsDir(Root)));
        public string AttachmentFile(string relative) => Path.Combine(AttachDir, relative);

        public void WriteAttachment(string relative, byte[] bytes)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(AttachmentFile(relative))!);
            File.WriteAllBytes(AttachmentFile(relative), bytes);
        }

        public void Dispose()
        {
            Sync.Dispose();
            Repo.Dispose();
        }
    }

    private Computer NewComputer(string name)
    {
        Computer c = new(temp, name);
        disposables.Add(c);
        return c;
    }

    private async Task<(Computer A, Computer B)> TwoConnected()
    {
        Computer a = NewComputer("a"), b = NewComputer("b");
        a.ConnectFolder(Storage);
        await a.Sync.LastPass(a.Root);
        b.ConnectFolder(Storage);
        await b.Sync.LastPass(b.Root);
        return (a, b);
    }

    private static SyncReport Report(params SyncNote[] notes) => new(SyncOutcome.Synced, null, [], [], [], [], [], [], []) { Notes = notes };

    // ── the sync says what it did, on the item ──

    [Fact(DisplayName = "The sync writes what it put back on the item")]
    public async Task TheSyncWritesWhatItPutBackOnTheItem()
    {
        Computer a = NewComputer("a");
        a.ConnectFolder(Storage);
        await a.Sync.LastPass(a.Root);
        const string said = "put back proposal.md: it was missing on this computer and nothing says it was removed on purpose";
        a.Engine = _ => Task.FromResult(Report(new SyncNote(123, "add-health-check", "put-back-file", said)));

        await a.Sync.SyncNow(a.Root);

        string log = a.Repo.Read(Item + "/events.log");
        string line = log.Split('\n').Single(l => l.Contains(" | sync | "));
        Assert.EndsWith(" | sync | " + said, line.TrimEnd('\r'));
        Assert.Matches(@"^\d{4}-\d\d-\d\dT\d\d:\d\d:\d\d[+-]\d\d:\d\d \| ", line);          // the time carries its offset
        WorkItemEvent logged = a.Do(() => WorkItemsApi.GetEvents(123).Single(e => e.Kind == "sync"));
        Assert.Equal(said, logged.Text);
        Assert.Equal("events", WorkItemsApi.EventGroup("sync"));
    }

    [Fact(DisplayName = "The computer that archived an item does not log that it followed the archive")]
    public async Task TheComputerThatArchivedDoesNotLogThatItFollowedTheArchive()
    {
        (Computer a, Computer b) = await TwoConnected();
        List<SyncNote> onA = [], onB = [];
        a.Engine = async o => { SyncReport r = await BoardSyncApi.Sync(o); onA.AddRange(r.Notes); return r; };
        b.Engine = async o => { SyncReport r = await BoardSyncApi.Sync(o); onB.AddRange(r.Notes); return r; };

        // A archives the item: the folder moves whole and its log says where.
        string changes = Path.Combine(a.Root, "openspec", "changes");
        Directory.CreateDirectory(Path.Combine(changes, "archive"));
        Directory.Move(Path.Combine(changes, "add-health-check"), Path.Combine(changes, "archive", "2026-11-20-add-health-check"));
        await a.Sync.SyncNow(a.Root);
        await b.Sync.SyncNow(b.Root);
        await a.Sync.SyncNow(a.Root);

        Assert.Contains(onB, n => n.Kind == "followed-archive");                  // B did follow it
        Assert.DoesNotContain(onA, n => n.Kind is "followed-archive" or "followed-rename" or "followed-merge");   // A made the move: its storage lagged, nothing was followed here
    }

    /// <summary>What a fresh computer connected to the storage ends up with: the storage's copy of an item's events.log.</summary>
    private async Task<string> StorageCopy()
    {
        Computer c = NewComputer("c" + disposables.Count);
        c.ConnectFolder(Storage);
        await c.Sync.LastPass(c.Root);
        await c.Sync.SyncNow(c.Root);
        return c.Repo.Read(Item + "/events.log");
    }

    [Fact(DisplayName = "A put-back folder's sync event is in the local log after the next sync")]
    public async Task APutBackFoldersSyncEventIsInTheLocalLogAfterTheNextSync()
    {
        // The watcher is off so the order is fixed: the app started after the hand's deletion and read the board without the folder, and the sync's download is not yet seen by the store (it is, a moment later, in the app).
        OpenSpecStoreModule.State.RealWatcher = false;
        (Computer a, Computer b) = await TwoConnected();
        await a.Sync.SyncNow(a.Root);
        await b.Sync.SyncNow(b.Root);
        Assert.True(File.Exists(Path.Combine(b.Root, Item, "proposal.md")));

        Directory.Delete(Path.Combine(b.Root, Item), true);                          // by hand, nothing says it moved
        b.Do(() => { OpenSpecStoreApi.Refresh(); return WorkItemsApi.GetAll(includeHidden: true).Count; });   // the board as the app read it at start: without the item
        await b.Sync.SyncNow(b.Root);
        Assert.True(File.Exists(Path.Combine(b.Root, Item, "proposal.md")));         // back
        await b.Sync.SyncNow(b.Root);
        await b.Sync.SyncNow(b.Root);

        const string said = " | sync | put back: the folder was missing on this computer and nothing says it moved";
        string local = b.Repo.Read(Item + "/events.log");
        Assert.Single(local.Split((char)10), l => l.Contains(said));
        Assert.Contains(said, await StorageCopy());
        await a.Sync.SyncNow(a.Root);
        Assert.Equal(local, a.Repo.Read(Item + "/events.log"));
    }

    [Fact(DisplayName = "A put-back file's sync event survives the next sync")]
    public async Task APutBackFilesSyncEventSurvivesTheNextSync()
    {
        (Computer a, Computer b) = await TwoConnected();
        await a.Sync.SyncNow(a.Root);
        await b.Sync.SyncNow(b.Root);

        File.Delete(Path.Combine(b.Root, Item, "proposal.md"));                      // nothing says it was removed on purpose
        await b.Sync.SyncNow(b.Root);
        Assert.True(File.Exists(Path.Combine(b.Root, Item, "proposal.md")));
        await b.Sync.SyncNow(b.Root);
        await b.Sync.SyncNow(b.Root);

        const string said = " | sync | put back proposal.md: it was missing on this computer and nothing says it was removed on purpose";
        string local = b.Repo.Read(Item + "/events.log");
        Assert.Single(local.Split((char)10), l => l.Contains(said));
        Assert.Contains(said, await StorageCopy());
    }

    [Fact(DisplayName = "Sync events go to the synced project, not the open one")]
    public async Task SyncEventsGoToTheSyncedProjectNotTheOpenOne()
    {
        (Computer a, Computer b) = await TwoConnected();
        await a.Sync.SyncNow(a.Root);
        await b.Sync.SyncNow(b.Root);
        // One window with both projects open as tabs, the way the app runs them: B is synced while A is the project in front.
        Workspace workspace = new(b.App, b.Ui, b.Sync);
        Assert.True(workspace.OpenTab(b.Root).Success);
        Assert.True(workspace.OpenTab(a.Root).Success);
        Assert.Equal(Path.GetFullPath(a.Root), Path.GetFullPath(workspace.Root!));
        string aLog = Path.Combine(a.Root, Item, "events.log");
        string? aBefore = File.Exists(aLog) ? File.ReadAllText(aLog) : null;

        File.Delete(Path.Combine(b.Root, Item, "proposal.md"));
        await b.Sync.SyncNow(b.Root);

        const string said = " | sync | put back proposal.md: it was missing on this computer and nothing says it was removed on purpose";
        Assert.Contains(said, b.Repo.Read(Item + "/events.log"));
        Assert.Equal(aBefore, File.Exists(aLog) ? File.ReadAllText(aLog) : null);                    // the project in front is not the one that was synced
    }

    // ── attachments ──

    [Fact(DisplayName = "A missing attachment is put back")]
    public async Task AMissingAttachmentIsPutBack()
    {
        (Computer a, Computer b) = await TwoConnected();
        a.WriteAttachment(Pic, Png);
        await a.Sync.SyncNow(a.Root);
        await b.Sync.SyncNow(b.Root);
        Assert.Equal(Png, File.ReadAllBytes(b.AttachmentFile(Pic)));

        File.Delete(a.AttachmentFile(Pic));                                       // a checkout or a hand took it away; the item's log says nothing
        await a.Sync.SyncNow(a.Root);

        Assert.Equal(Png, File.ReadAllBytes(a.AttachmentFile(Pic)));              // back from the storage
        await b.Sync.SyncNow(b.Root);
        Assert.Equal(Png, File.ReadAllBytes(b.AttachmentFile(Pic)));              // and the other computer never lost it
    }

    [Fact(DisplayName = "A removed attachment stays removed")]
    public async Task ARemovedAttachmentStaysRemoved()
    {
        (Computer a, Computer b) = await TwoConnected();
        a.WriteAttachment(Pic, Png);
        await a.Sync.SyncNow(a.Root);
        await b.Sync.SyncNow(b.Root);
        Assert.True(File.Exists(b.AttachmentFile(Pic)));

        a.Do(() => WorkItemsApi.LogEvent(123, "attachment-removed", "n123: shot.png"));      // what removing it from the note writes
        File.Delete(a.AttachmentFile(Pic));
        await a.Sync.SyncNow(a.Root);
        Assert.False(File.Exists(a.AttachmentFile(Pic)));                          // not put back: the log says why it is gone

        await b.Sync.SyncNow(b.Root);                                              // the log came with the board; the removal follows
        Assert.False(File.Exists(b.AttachmentFile(Pic)));
    }

    // ── Settings ──

    [Fact(DisplayName = "Settings offers no Allow and sync")]
    public async Task SettingsOffersNoAllowAndSync()
    {
        Computer a = NewComputer("a");
        a.ConnectFolder(Storage);
        await a.Sync.LastPass(a.Root);
        const string other = "This storage holds the board of another project. Nothing was changed. Check the bucket and prefix in Settings.";
        a.Engine = _ => Task.FromResult(new SyncReport(SyncOutcome.Blocked, other, [], [], [], [], [], [], [], blocked: SyncBlock.OtherProject));
        await a.Sync.SyncNow(a.Root);

        await using BunitContext ctx = new();
        ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        ctx.Services.AddSingleton(a.Ui);
        ctx.Services.AddRadzenComponents();
        ctx.Services.AddSingleton(new SettingsStore(Path.Combine(temp, "settings-page.json")));
        ctx.Services.AddSingleton(a.Settings);
        ctx.Services.AddSingleton(a.Sync);
        ctx.Services.AddSingleton<Workspace>();
        Assert.True(ctx.Services.GetRequiredService<Workspace>().Open(a.Root).Success);
        a.Engine = _ => Task.FromResult(new SyncReport(SyncOutcome.Blocked, other, [], [], [], [], [], [], [], blocked: SyncBlock.OtherProject));
        await a.Sync.LastPass(a.Root);
        await a.Sync.SyncNow(a.Root);
        IRenderedComponent<SettingsPage> page = ctx.Render<SettingsPage>().OnProject();

        AngleSharp.Dom.IElement section = page.Find("[data-storage]");
        Assert.Equal("Paused: " + other, section.QuerySelector(".bs-state")!.TextContent.Trim());     // the reason is shown
        Assert.Null(section.QuerySelector("button.bs-allow"));
        Assert.DoesNotContain("Allow and sync", page.Markup);
        Assert.DoesNotContain("deletion", section.TextContent, StringComparison.OrdinalIgnoreCase);   // nothing about deletions to allow
    }

    // ── line endings of the committed base ──

    [Fact(DisplayName = "A committed base with other line endings still merges")]
    public async Task ACommittedBaseWithOtherLineEndingsStillMerges()
    {
        const string seed = "# Tasks\n\n## 1. Tasks\n\n- [ ] 1.1 write the test\n- [ ] 1.2 write the code\n";
        const string path = "add-health-check/tasks.md";
        DateTime now = new(2026, 11, 12, 8, 0, 0, DateTimeKind.Utc);
        string Root(string id) => Path.Combine(temp, id, "changes");
        void Write(string id, string text)
        {
            string file = Path.Combine(Root(id), path);
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.WriteAllText(file, text, new UTF8Encoding(false));
            File.SetLastWriteTimeUtc(file, now);
        }
        Task<SyncReport> Sync(string id, Func<string, string?>? committed = null) =>
            BoardSyncApi.Sync(new SyncOptions(Root(id), Path.Combine(temp, id, "state"), BoardSyncApi.FolderStorage(Storage), id, () => now, CommittedContent: committed));

        Write("A", seed);
        await Sync("A");
        now = now.AddMinutes(5);
        Write("A", seed.Replace("- [ ] 1.1", "- [x] 1.1"));
        await Sync("A");

        // B has never synced: its working file has Windows line endings, git hands back the committed text with Unix ones.
        now = now.AddMinutes(5);
        Write("B", seed.Replace("- [ ] 1.2", "- [x] 1.2").Replace("\n", "\r\n"));
        SyncReport report = await Sync("B", p => p == path ? seed : null);

        Assert.Equal(SyncOutcome.Synced, report.Outcome);
        string merged = File.ReadAllText(Path.Combine(Root("B"), path));
        Assert.Contains("- [x] 1.1", merged);
        Assert.Contains("- [x] 1.2", merged);
        Assert.Empty(report.Conflicts);
    }
}
