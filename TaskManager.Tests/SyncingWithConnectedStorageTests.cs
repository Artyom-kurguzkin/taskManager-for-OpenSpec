using BoardSync;
using BoardSync.PublicDTOs;
using Bunit;
using GitHistory;
using Microsoft.Extensions.DependencyInjection;
using Notes;
using Notes.PublicDTOs;
using OpenSpecStore;
using Radzen;
using TaskManager.Components.Pages;
using TaskManager.Services;
using WorkItems;
using WorkItems.PublicDTOs;

namespace TaskManager.Tests;

/// <summary>
/// A project can keep its board in a connected storage as well as in its folder: the settings choose it per project, a sync service runs the
/// engine in the background (when the project opens, two quiet minutes after the board changes, when the app closes and on request) and says what happened.
/// "Two computers" are two temporary repositories, two app-data folders and two services over one Folder storage; the services are plain
/// classes, so two of them live in one process (the module state is shared, and each service enters its project the way the workspace does).
/// </summary>
[Collection(nameof(ModuleStateCollection))]
public sealed class SyncingWithConnectedStorageTests : IDisposable
{
    private const string Item = "openspec/changes/add-health-check";
    private const string Yaml = Item + "/.openspec.yaml";
    private const string Proposal = Item + "/proposal.md";

    private readonly string temp = Directory.CreateTempSubdirectory("rtm-connected-").FullName;
    private readonly List<IDisposable> disposables = [];
    private string Storage => Path.Combine(temp, "storage");

    public void Dispose()
    {
        foreach (IDisposable d in disposables) d.Dispose();
        try { Directory.Delete(temp, true); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }

    // ── a computer: a repository, an app-data folder, the settings and the service ──

    private sealed class Computer : IDisposable
    {
        public readonly TestRepo Repo = new();
        public readonly string Data;
        public readonly UiService Ui = new();
        /// <summary>This computer's app settings: where the project's attachments folder is read from.</summary>
        public readonly SettingsStore App;
        public BoardStorageSettings Settings { get; private set; }
        public BoardSyncService Sync { get; private set; }
        public readonly List<SyncOptions> Seen = [];
        public Func<SyncOptions, Task<SyncReport>>? Engine;
        private readonly IReadOnlyList<StorageProvider>? providers;
        private readonly Func<DateTime>? clock;

        public Computer(string temp, string name, IReadOnlyList<StorageProvider>? providers = null, Func<DateTime>? clock = null)
        {
            Data = Path.Combine(temp, "data-" + name);
            App = new SettingsStore(Path.Combine(temp, "app-" + name + ".json"));
            this.providers = providers;
            this.clock = clock;
            OpenSpecStoreApi.Open(Repo.Root, keepOthers: true);
            (Settings, Sync) = Start();
        }

        private (BoardStorageSettings, BoardSyncService) Start()
        {
            BoardStorageSettings settings = new(Data, providers ?? StorageProviders.Builtin);
            BoardSyncService sync = new(settings, Ui, new SyncSetup(
                TimeSpan.FromMinutes(2),
                async o => { Seen.Add(o); return Engine is null ? await BoardSyncApi.Sync(o) : await Engine(o); },
                clock), App);
            sync.InProject = In;
            sync.Attach(Repo.Root);
            return (settings, sync);
        }

        /// <summary>Closes the app and opens it again over the same repository and app-data folder: new settings, new service, nothing in memory.</summary>
        public void Restart()
        {
            Sync.Dispose();
            (Settings, Sync) = Start();
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

        public void Do(Action work) => In(Root, work);

        public void ConnectFolder(string folder)
        {
            Assert.True(Settings.Connect(Root, "folder").Success);
            Assert.True(Settings.SetValue(Root, "path", folder).Success);
        }

        public string Read(string relative) => Repo.Read(relative);
        public IReadOnlyList<string> Toasts => Ui.Toasts.Select(t => t.Text).ToList();
        public string Line => Sync.Status(Root).Line;
        /// <summary>The pass the service started last (the one a connect, an open or a click caused): awaiting it is how a test waits for a sync.</summary>
        public Task Pass() => Sync.LastPass(Root);

        /// <summary>The attachments folder in effect (the default one in these tests).</summary>
        public string AttachDir => Path.GetFullPath(Path.Combine(Root, App.AttachmentsDir(Root)));

        public void WriteAttachment(string relative, byte[] bytes)
        {
            string file = Path.Combine(AttachDir, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.WriteAllBytes(file, bytes);
        }

        public byte[] ReadAttachment(string relative) => File.ReadAllBytes(Path.Combine(AttachDir, relative));
        public bool HasAttachment(string relative) => File.Exists(Path.Combine(AttachDir, relative));
        public IEnumerable<SyncOptions> AttachmentRuns => Seen.Where(o => o.WholeFilesOnly);

        public void Dispose()
        {
            Sync.Dispose();
            Repo.Dispose();
        }
    }

    private Computer NewComputer(string name, IReadOnlyList<StorageProvider>? providers = null, Func<DateTime>? clock = null)
    {
        Computer c = new(temp, name, providers, clock);
        disposables.Add(c);
        return c;
    }

    private async Task<(Computer A, Computer B)> TwoConnected(Func<DateTime>? clock = null)
    {
        Computer a = NewComputer("a", clock: clock), b = NewComputer("b", clock: clock);
        a.ConnectFolder(Storage);
        await a.Pass();
        b.ConnectFolder(Storage);
        await b.Pass();
        return (a, b);
    }

    // "This change is later than that one" without waiting for a clock to move: the engine's clock reads a day ahead of the test's start, and a change is
    // written at "hour n" after the start. A later hour is a later change, whatever the real time is (a clock never goes below the one it replaces).
    private readonly DateTime start = DateTime.UtcNow;
    private DateTime ADayLater() => start.AddDays(1);
    private void WrittenAt(Computer c, string relative, int hour) => File.SetLastWriteTimeUtc(Path.Combine(c.Root, relative), start.AddHours(hour));

    private static IEnumerable<WorkItemEvent> Events(int id, string kind) => WorkItemsApi.GetEvents(id).Where(e => e.Kind == kind);

    // ── the page ──

    private BunitContext PageContext(Computer c, IReadOnlyList<StorageProvider>? providers = null, FolderOpener? opener = null)
    {
        string settingsFile = Path.Combine(temp, "settings-" + Guid.NewGuid().ToString("N") + ".json");
        BunitContext ctx = new();
        disposables.Add(ctx);
        ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        ctx.Services.AddSingleton(c.Ui);
        ctx.Services.AddRadzenComponents();
        ctx.Services.AddSingleton(new SettingsStore(settingsFile));
        ctx.Services.AddSingleton(c.Settings);
        ctx.Services.AddSingleton(c.Sync);
        if (opener is not null) ctx.Services.AddSingleton(opener);
        ctx.Services.AddSingleton<Workspace>();
        Assert.True(ctx.Services.GetRequiredService<Workspace>().Open(c.Root).Success);
        return ctx;
    }

    private static AngleSharp.Dom.IElement Section(IRenderedComponent<SettingsPage> page) => page.Find("section[data-storage]");

    /// <summary>Changes an input inside the storage section on the renderer's thread, so a re-render between finding it and changing it cannot leave the event without a handler.</summary>
    private static Task Change(IRenderedComponent<SettingsPage> page, string selector, string value) =>
        page.InvokeAsync(() => Section(page).QuerySelector(selector)!.Change(value));

    // ── settings ──

    [Fact(DisplayName = "A project is set to this folder only by default and nothing syncs")]
    public async Task ThisFolderOnlyIsTheDefault()
    {
        Computer c = NewComputer("a");
        await using BunitContext ctx = PageContext(c);
        IRenderedComponent<SettingsPage> page = ctx.Render<SettingsPage>();

        Assert.Null(c.Settings.Config(c.Root));
        Assert.Equal("This folder only", Section(page).QuerySelector(".seg-o.is-sel")!.TextContent.Trim());
        Assert.Empty(Section(page).QuerySelectorAll("[data-field]"));

        c.Sync.BoardChanged();
        await c.Sync.QuietElapsed(c.Root);
        await c.Sync.SyncOnExit();

        Assert.False(c.Sync.IsActive(c.Root));
        Assert.Null(c.Sync.QuietDue(c.Root));
        Assert.Equal(0, c.Sync.Runs(c.Root));
        Assert.False(Directory.Exists(c.Data));                          // no state, device id or credentials were made
        Assert.DoesNotContain("boardStorage", c.Read("openspec/config.yaml"));
    }

    [Fact(DisplayName = "Choosing connected storage with a folder saves the provider and path in the project's settings in the repository")]
    public async Task ConnectedFolderIsSavedInTheRepository()
    {
        Computer c = NewComputer("a");
        await using BunitContext ctx = PageContext(c);
        IRenderedComponent<SettingsPage> page = ctx.Render<SettingsPage>();

        await Change(page, "input[value='connected']", "connected");
        page.WaitForAssertion(() => Assert.NotNull(Section(page).QuerySelector("input[value='folder']")));
        await Change(page, "input[value='folder']", "folder");
        page.WaitForAssertion(() => Assert.NotNull(Section(page).QuerySelector("input[data-field='path']")));
        await Change(page, "input[data-field='path']", Storage);

        page.WaitForAssertion(() =>
        {
            string config = c.Read("openspec/config.yaml");
            Assert.Contains("boardStorage:", config);
            Assert.Contains("provider: folder", config);
            Assert.Contains("path:", config);
            Assert.Contains("In Progress: [High priority, Lower priority]", config);   // the other settings in the block stay
        });
        Assert.Equal("folder", c.Settings.Config(c.Root)!.Provider);
        Assert.Equal(Storage, c.Settings.Config(c.Root)!.Values["path"]);

        await Change(page, "input[value='this']", "this");                 // and back
        page.WaitForAssertion(() => Assert.DoesNotContain("boardStorage", c.Read("openspec/config.yaml")));
        Assert.Null(c.Settings.Config(c.Root));
    }

    [Fact(DisplayName = "The S3 secret is kept on this computer only: it is not in the repository and is never shown back")]
    public async Task SecretStaysOnThisComputer()
    {
        const string secret = "TOP-SECRET-1234-abcd";
        const string keyId = "AKIA-KEY-ID-9876";
        Computer c = NewComputer("a");
        c.Settings.Connect(c.Root, "s3");
        c.Settings.SetValue(c.Root, "endpoint", "https://acct.r2.cloudflarestorage.com");
        c.Settings.SetValue(c.Root, "bucket", "board");
        c.Settings.SetValue(c.Root, "accessKey", keyId);
        c.Settings.SetValue(c.Root, "secretKey", secret);

        foreach (string file in Directory.EnumerateFiles(c.Root, "*", SearchOption.AllDirectories))
        {
            string text = File.ReadAllText(file);
            Assert.DoesNotContain(secret, text);
            Assert.DoesNotContain(keyId, text);
        }
        Assert.Contains("endpoint:", c.Read("openspec/config.yaml"));      // the address is in the repository
        Assert.Contains("bucket: board", c.Read("openspec/config.yaml"));
        foreach (string file in Directory.EnumerateFiles(c.Data, "*", SearchOption.AllDirectories))
            if (OperatingSystem.IsWindows()) Assert.DoesNotContain(secret, File.ReadAllText(file));   // protected for the current user, not plain text

        TaskManager.Services.BoardStorageSettings again = new(c.Data, StorageProviders.Builtin);       // a restart
        Assert.Equal(secret, again.ComputerValue(c.Root, "secretKey"));
        Assert.True(again.IsSet(c.Root, "secretKey"));

        await using BunitContext ctx = PageContext(c);
        IRenderedComponent<SettingsPage> page = ctx.Render<SettingsPage>();
        Assert.DoesNotContain(secret, page.Markup);
        AngleSharp.Dom.IElement input = Section(page).QuerySelector("input[data-field='secretKey']")!;
        Assert.Equal("password", input.GetAttribute("type"));
        Assert.True(string.IsNullOrEmpty(input.GetAttribute("value")));
        Assert.Contains("saved", Section(page).TextContent + input.GetAttribute("placeholder"), StringComparison.OrdinalIgnoreCase);

        c.Settings.SetValue(c.Root, "secretKey", "");
        Assert.False(c.Settings.IsSet(c.Root, "secretKey"));
    }

    [Fact(DisplayName = "A project connected from the repository settings waits for the key on this computer")]
    public async Task SecondComputerOnlyAsksForTheKey()
    {
        Computer c = NewComputer("a");
        c.Repo.Write("openspec/config.yaml", c.Read("openspec/config.yaml") + "  boardStorage:\n    provider: s3\n    endpoint: https://acct.r2.cloudflarestorage.com\n    bucket: board\n");   // what git brought
        Assert.True(c.Settings.Approve(c.Root).Success);                           // the repository only proposes it: this computer has allowed it
        c.Sync.BoardChanged();
        await c.Sync.Attach(c.Root);

        Assert.Equal("s3", c.Settings.Config(c.Root)!.Provider);
        Assert.False(c.Settings.IsSet(c.Root, "secretKey"));
        Assert.Contains("this computer", c.Line, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, c.Sync.Runs(c.Root));                                   // nothing to sync with yet
        await using BunitContext ctx = PageContext(c);
        IRenderedComponent<SettingsPage> page = ctx.Render<SettingsPage>();
        Assert.Equal("https://acct.r2.cloudflarestorage.com", Section(page).QuerySelector("input[data-field='endpoint']")!.GetAttribute("value"));
    }

    [Fact(DisplayName = "Test connection says whether the storage answered")]
    public async Task TestConnectionSaysIfTheStorageAnswered()
    {
        Computer c = NewComputer("a");
        c.ConnectFolder(Storage);
        await c.Pass();
        await using BunitContext ctx = PageContext(c);
        IRenderedComponent<SettingsPage> page = ctx.Render<SettingsPage>();

        page.Find("section[data-storage] button.bs-test").Click();
        page.WaitForAssertion(() => Assert.Contains("Connected", Section(page).QuerySelector(".bs-test-result")!.TextContent));

        string notAFolder = Path.Combine(temp, "a-file");
        File.WriteAllText(notAFolder, "x");
        c.Settings.SetValue(c.Root, "path", Path.Combine(notAFolder, "inside"));
        page.WaitForAssertion(() => Assert.Null(Section(page).QuerySelector(".bs-test-result")));   // an old answer is not shown for new settings
        await ctx.Renderer.Dispatcher.InvokeAsync(() => { });
        page.Find("section[data-storage] button.bs-test").Click();
        page.WaitForAssertion(() =>
        {
            string answer = Section(page).QuerySelector(".bs-test-result")!.TextContent;
            Assert.DoesNotContain("Connected", answer);
            Assert.Contains("Could not", answer);
        });
    }

    [Fact(DisplayName = "A new provider is one descriptor: the settings render its fields without new page code")]
    public async Task ANewProviderIsOneDescriptor()
    {
        StorageProvider memory = new("vault", "Vault (test)", "A provider added by a descriptor alone.",
            [new StorageField("site", "Site address", "https://vault.example"), new StorageField("token", "Token", Secret: true, OnThisComputer: true), new StorageField("area", "Area", Optional: true)],
            values => BoardSyncApi.FolderStorage(Path.Combine(temp, "vault-" + values["site"].Replace(':', '_').Replace('/', '_'))));
        Computer c = NewComputer("a", [.. StorageProviders.Builtin, memory]);
        await using BunitContext ctx = PageContext(c);
        IRenderedComponent<SettingsPage> page = ctx.Render<SettingsPage>();

        await Change(page, "input[value='connected']", "connected");
        page.WaitForAssertion(() => Assert.NotNull(Section(page).QuerySelector("input[value='vault']")));
        await Change(page, "input[value='vault']", "vault");
        page.WaitForAssertion(() => Assert.Equal(["site", "token", "area"], Section(page).QuerySelectorAll("[data-field]").Select(e => e.GetAttribute("data-field")).ToList()));
        Assert.Equal("password", Section(page).QuerySelector("input[data-field='token']")!.GetAttribute("type"));
        Assert.Contains("Site address", Section(page).TextContent);

        await Change(page, "input[data-field='site']", "https://vault.example");
        await Change(page, "input[data-field='token']", "t0ken-value");
        page.WaitForAssertion(() => Assert.Contains("site: https://vault.example", c.Read("openspec/config.yaml")));
        Assert.DoesNotContain("t0ken-value", c.Read("openspec/config.yaml"));
        Assert.Equal("t0ken-value", c.Settings.ComputerValue(c.Root, "token"));
        await c.Pass();
        Assert.True(c.Sync.Runs(c.Root) > 0);   // the descriptor built a storage and the project synced with it
    }

    // ── syncing ──

    [Fact(DisplayName = "Two computers on the same storage: a work item changed on one shows up on the other after its sync")]
    public async Task ChangeReachesTheOtherComputer()
    {
        (Computer a, Computer b) = await TwoConnected();

        Assert.True(a.Do(() => WorkItemsApi.SetTitle(123, "Health endpoint for the uptime monitor")).Success);
        await a.Sync.SyncNow(a.Root);
        Assert.Contains("Health endpoint for the uptime monitor", a.Read(Yaml));
        Assert.DoesNotContain("Health endpoint for the uptime monitor", b.Read(Yaml));
        Assert.StartsWith("Synced", a.Line);

        await b.Sync.SyncNow(b.Root);
        Assert.Contains("Health endpoint for the uptime monitor", b.Read(Yaml));
        b.Do(OpenSpecStoreApi.Refresh);
        Assert.Equal("Health endpoint for the uptime monitor", b.Do(() => WorkItemsApi.Get(123)!.Title));
        Assert.StartsWith("Synced", b.Line);
        Assert.Contains("received", b.Line);
        Assert.Contains(b.Toasts, t => t.StartsWith("Received") && t.Contains("from the storage"));
    }

    // ── when it syncs: a project opens, two quiet minutes after a change, the app closes, on request. Never by itself. ──

    private DateTime now = DateTime.UtcNow;
    private void Tick(int minutes) => now = now.AddMinutes(minutes);

    private static void ChangeTitle(Computer c, string title)
    {
        Assert.True(c.Do(() => WorkItemsApi.SetTitle(123, title)).Success);
        c.Sync.BoardChanged();                                         // what the workspace does for every change
    }

    [Fact(DisplayName = "A change starts a two-minute wait, and another change restarts it")]
    public async Task AChangeStartsTheWaitAndAnotherRestartsIt()
    {
        Computer a = NewComputer("a", clock: () => now);
        a.ConnectFolder(Storage);
        await a.Pass();
        int runs = a.Sync.Runs(a.Root);
        Assert.Null(a.Sync.QuietDue(a.Root));                          // nothing pending after a sync

        ChangeTitle(a, "First");
        Assert.Equal(now.AddMinutes(2), a.Sync.QuietDue(a.Root));
        Assert.StartsWith("Waiting to sync at " + now.AddMinutes(2).ToLocalTime().ToString("HH:mm"), a.Line);

        Tick(1);
        ChangeTitle(a, "Second");
        Assert.Equal(now.AddMinutes(2), a.Sync.QuietDue(a.Root));      // a minute later, the wait ends a minute later too
        Assert.Equal(runs, a.Sync.Runs(a.Root));                       // and nothing has synced yet
    }

    [Fact(DisplayName = "When the wait ends the board syncs once")]
    public async Task WhenTheWaitEndsTheBoardSyncsOnce()
    {
        Computer a = NewComputer("a");
        a.ConnectFolder(Storage);
        await a.Pass();
        int runs = a.Sync.Runs(a.Root);

        for (int i = 1; i <= 5; i++) ChangeTitle(a, "Title " + i);     // a burst
        Assert.Equal(runs, a.Sync.Runs(a.Root));

        await a.Sync.QuietElapsed(a.Root);                             // what the timer calls

        Assert.Equal(runs + 1, a.Sync.Runs(a.Root));
        Assert.Null(a.Sync.QuietDue(a.Root));
        Assert.Contains(Directory.EnumerateFiles(Storage, "*", SearchOption.AllDirectories), f => File.ReadAllText(f).Contains("title: Title 5"));   // the last of the burst was sent
    }

    [Fact(DisplayName = "Opening a connected project syncs it once")]
    public async Task OpeningAConnectedProjectSyncsItOnce()
    {
        Computer a = NewComputer("a");
        a.ConnectFolder(Storage);
        await a.Pass();
        ChangeTitle(a, "Written before the app closed");
        a.Sync.Dispose();                                              // the app closes without syncing...

        a.Restart();                                                   // ...and opens again: the project is opened
        await a.Pass();

        Assert.Equal(1, a.Sync.Runs(a.Root));
        Assert.Contains(Directory.EnumerateFiles(Storage, "*", SearchOption.AllDirectories), f => File.ReadAllText(f).Contains("title: Written before the app closed"));
    }

    [Fact(DisplayName = "Closing the app syncs what has not been synced yet, and never holds the app for more than the limit")]
    public async Task ClosingTheAppSyncsWhatIsPendingWithinTheLimit()
    {
        Computer a = NewComputer("a");
        a.ConnectFolder(Storage);
        await a.Pass();
        int runs = a.Sync.Runs(a.Root);
        await a.Sync.SyncOnExit();
        Assert.Equal(runs, a.Sync.Runs(a.Root));                       // nothing pending: nothing syncs

        ChangeTitle(a, "Changed just before closing");
        await a.Sync.SyncOnExit();
        Assert.Equal(runs + 1, a.Sync.Runs(a.Root));
        Assert.Null(a.Sync.QuietDue(a.Root));
        Assert.Contains(Directory.EnumerateFiles(Storage, "*", SearchOption.AllDirectories), f => File.ReadAllText(f).Contains("title: Changed just before closing"));

        // a storage that never answers: the limit ends the wait (here a token that has already ended stands for the 15 seconds) and the app closes
        TaskCompletionSource<SyncReport> never = new(TaskCreationOptions.RunContinuationsAsynchronously);
        a.Engine = _ => never.Task;
        ChangeTitle(a, "Changed again");
        using CancellationTokenSource limit = new();
        limit.Cancel();
        await a.Sync.SyncOnExit(limit.Token);
        Assert.False(a.Pass().IsCompleted);                            // the sync is still hanging; closing did not wait for it

        never.SetResult(Offline());
        await a.Pass();
    }

    [Fact(DisplayName = "A change that arrives while a sync is running causes exactly one more sync after it")]
    public async Task AChangeDuringASyncCausesExactlyOneMoreSync()
    {
        Computer a = NewComputer("a");
        a.ConnectFolder(Storage);
        await a.Pass();
        TaskCompletionSource entered = new(TaskCreationOptions.RunContinuationsAsynchronously), gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        int calls = 0;
        a.Engine = async _ => { Interlocked.Increment(ref calls); entered.TrySetResult(); await gate.Task; return Report(SyncOutcome.NothingToDo); };

        Task first = a.Sync.SyncNow(a.Root);
        await entered.Task;                                            // the engine is inside the first sync
        ChangeTitle(a, "Changed during the sync");
        Task[] during = [a.Sync.QuietElapsed(a.Root), a.Sync.QuietElapsed(a.Root), a.Sync.QuietElapsed(a.Root)];   // however many triggers arrive meanwhile
        Assert.Equal(1, calls);                                        // none of them started a second sync beside the first

        gate.SetResult();
        Task[] all = [first, .. during];
        await Task.WhenAll(all);
        await a.Pass();

        Assert.Equal(2, calls);                                        // the first, and one more after it
    }

    [Fact(DisplayName = "Nothing syncs on a timer by itself: with no change, no start and no exit there is no sync")]
    public async Task NothingSyncsOnATimerByItself()
    {
        Computer a = NewComputer("a", clock: () => now);
        a.ConnectFolder(Storage);
        await a.Pass();
        int calls = a.Seen.Count;

        Tick(24 * 60);                                                 // a day goes by with the app open

        Assert.Null(a.Sync.QuietDue(a.Root));                          // no wait was started, so no timer is running
        Assert.Equal(calls, a.Seen.Count);
        await a.Sync.SyncOnExit();                                     // nothing is pending at exit either
        Assert.Equal(calls, a.Seen.Count);
    }

    [Fact(DisplayName = "A field changed on both computers is logged as a sync conflict event on the work item, with what was dropped")]
    public async Task FieldConflictBecomesAnEvent()
    {
        (Computer a, Computer b) = await TwoConnected(ADayLater);

        a.Do(() => WorkItemsApi.SetStatus(123, "Blocked"));
        WrittenAt(a, Yaml, 1);
        await a.Sync.SyncNow(a.Root);
        b.Do(() => WorkItemsApi.SetStatus(123, "In Review"));
        WrittenAt(b, Yaml, 2);                        // the other change is the later one
        await b.Sync.SyncNow(b.Root);

        WorkItemEvent conflict = b.Do(() => Events(123, "sync-conflict").Single(e => e.Text.StartsWith("status")));
        Assert.Contains("In Review", conflict.Text);
        Assert.Contains("Blocked", conflict.Text);
        Assert.Contains("dropped", conflict.Text);
        Assert.Contains(b.Toasts, t => t.Contains("conflict"));
        Assert.Contains("status: In Review", b.Read(Yaml));
        Assert.NotNull(b.Sync.QuietDue(b.Root));                                      // the event is the app's own write: a wait for its sync has started
        await b.Sync.QuietElapsed(b.Root);                                            // and it goes up too
        Assert.Equal(0, b.Sync.Status(b.Root).Unsent);
        await a.Sync.SyncNow(a.Root);
        Assert.Contains("sync-conflict", a.Read(Item + "/events.log"));
    }

    [Fact(DisplayName = "Text lost in a sync conflict is kept as a note on the work item")]
    public async Task TextConflictBecomesANote()
    {
        (Computer a, Computer b) = await TwoConnected(ADayLater);

        a.Do(() => WorkItemsApi.SetDescription(123, "Alpha: poll the endpoint from the monitor every minute."));
        WrittenAt(a, Proposal, 1);
        await a.Sync.SyncNow(a.Root);
        b.Do(() => WorkItemsApi.SetDescription(123, "Beta: the monitor asks the endpoint twice a minute."));
        WrittenAt(b, Proposal, 2);
        await b.Sync.SyncNow(b.Root);

        b.Do(OpenSpecStoreApi.Refresh);
        Note note = b.Do(() => NotesApi.GetForWorkItem(123).Single(n => n.Title.StartsWith("Sync conflict")));
        string kept = b.Read(Proposal);
        string lost = kept.Contains("Beta") ? "Alpha: poll the endpoint from the monitor every minute." : "Beta: the monitor asks the endpoint twice a minute.";
        Assert.DoesNotContain(lost, kept);
        Assert.Contains(lost, note.Body);                       // the dropped text in full
        Assert.Equal("Sync", note.By);
        Assert.NotNull(note.AgentBy);                          // marked like an agent's note, so it stands out for review
        Assert.Contains(b.Toasts, t => t.Contains("conflict"));
    }

    [Fact(DisplayName = "A conflict on a work item that is gone is kept in a file next to the sync state and says so")]
    public async Task ConflictWithoutItemIsKeptInAFile()
    {
        Computer a = NewComputer("a");
        a.ConnectFolder(Storage);
        await a.Pass();
        a.Engine = _ => Task.FromResult(new SyncReport(SyncOutcome.Synced, null, [], [], [], [], [],
            [new SyncConflict("vanished-change/proposal.md", "edited on this computer, deleted on another", "deleted", "The text that would be lost.")], []));

        await a.Sync.SyncNow(a.Root);

        string[] kept = Directory.EnumerateFiles(a.Data, "*", SearchOption.AllDirectories).Where(f => File.ReadAllText(f).Contains("The text that would be lost.")).ToArray();
        Assert.Single(kept);
        Assert.Contains(a.Toasts, t => t.Contains("conflict") && t.Contains(Path.GetDirectoryName(kept[0])!));
    }

    [Fact(DisplayName = "A blocked sync shows why and syncs once it is allowed")]
    public async Task BlockedSyncIsAllowedByTheUser()
    {
        (Computer a, Computer b) = await TwoConnected();
        foreach (string folder in new[] { "export-remittances", "plain-change", "archive" }) Directory.Delete(Path.Combine(a.Root, "openspec/changes", folder), true);
        File.Delete(Path.Combine(a.Root, Item, "tasks.md"));
        File.Delete(Path.Combine(a.Root, Item, "proposal.md"));
        File.Delete(Path.Combine(a.Root, Item, "notes", "n123.md"));
        File.Delete(Path.Combine(a.Root, Item, ".openspec.yaml"));

        await a.Sync.SyncNow(a.Root);

        Assert.StartsWith("Paused:", a.Line);
        Assert.Contains("delete", a.Line);
        Assert.True(a.Sync.Status(a.Root).CanAllow);
        Assert.Contains(a.Toasts, t => t.Contains("paused", StringComparison.OrdinalIgnoreCase) || t.Contains("blocked", StringComparison.OrdinalIgnoreCase));
        Assert.True(File.Exists(Path.Combine(b.Root, Yaml)));                 // nothing reached the storage

        await a.Sync.SyncNow(a.Root, allowMassDelete: true);

        Assert.StartsWith("Synced", a.Line);
        Assert.False(a.Sync.Status(a.Root).CanAllow);
        await b.Sync.SyncNow(b.Root);                                         // the other computer's brake holds the same way
        Assert.StartsWith("Paused:", b.Line);
        Assert.True(File.Exists(Path.Combine(b.Root, Yaml)));
        await b.Sync.SyncNow(b.Root, allowMassDelete: true);
        Assert.False(File.Exists(Path.Combine(b.Root, Yaml)));
    }

    [Fact(DisplayName = "A failing storage shows the reason in the state line and does not toast on every attempt")]
    public async Task FailingStorageToastsOnce()
    {
        Computer a = NewComputer("a");
        string notAFolder = Path.Combine(temp, "a-file");
        File.WriteAllText(notAFolder, "x");
        a.ConnectFolder(Path.Combine(notAFolder, "inside"));
        await a.Pass();

        await a.Sync.SyncNow(a.Root);
        await a.Sync.SyncNow(a.Root);
        await a.Sync.SyncNow(a.Root);

        Assert.StartsWith("Failed:", a.Line);
        Assert.Single(a.Toasts, t => t.Contains("Sync failed", StringComparison.OrdinalIgnoreCase));   // one for the first, none for the repeats

        a.Settings.SetValue(a.Root, "path", Storage);                        // fixed: the next failure is news again
        await a.Pass();
        await a.Sync.SyncNow(a.Root);
        Assert.DoesNotContain("Failed", a.Line);                             // the pass the new address caused had already synced: this one found nothing new
        Directory.Delete(Storage, true);
        File.WriteAllText(Storage, "now a file");
        await a.Sync.SyncNow(a.Root);
        Assert.Equal(2, a.Toasts.Count(t => t.Contains("Sync failed", StringComparison.OrdinalIgnoreCase)));
    }

    [Fact(DisplayName = "Paths the app wrote are passed to the sync as app edits and cleared after it succeeds")]
    public async Task AppEditsArePassedAndCleared()
    {
        Computer a = NewComputer("a");
        a.ConnectFolder(Storage);
        await a.Pass();
        a.Do(() => WorkItemsApi.SetTitle(123, "Written by the app"));
        File.WriteAllText(Path.Combine(a.Root, "openspec/changes/plain-change/proposal.md"), "# Plain\n\nEdited by git or an editor.\n");   // an outside edit

        a.Engine = o => Task.FromResult(new SyncReport(SyncOutcome.Failed, "offline", [], [], [], [], [], [], []));
        await a.Sync.SyncNow(a.Root);
        Assert.Contains("add-health-check/.openspec.yaml", a.Seen[^1].EditedInApp!);
        Assert.DoesNotContain("plain-change/proposal.md", a.Seen[^1].EditedInApp!);
        Assert.Equal(Path.Combine(a.Root, "openspec", "changes"), a.Seen[^1].LocalRoot);
        Assert.False(string.IsNullOrEmpty(a.Seen[^1].DeviceId));
        Assert.DoesNotContain('/', a.Seen[^1].DeviceId);

        a.Engine = null;                                                   // the failed sync cleared nothing
        await a.Sync.SyncNow(a.Root);
        Assert.Contains("add-health-check/.openspec.yaml", a.Seen[^1].EditedInApp!);
        Assert.Contains("add-health-check/events.log", a.Seen[^1].EditedInApp!);   // the worklog line the app appended for the title

        await a.Sync.SyncNow(a.Root);                                      // the successful one did
        Assert.Empty(a.Seen[^1].EditedInApp!);
    }

    [Fact(DisplayName = "Paths the board server wrote in its own process are passed as app edits too")]
    public async Task BoardServerWritesAreAppEdits()
    {
        Computer a = NewComputer("a");
        a.ConnectFolder(Storage);
        await a.Pass();
        string full = Path.Combine(a.Root, "openspec/changes/plain-change/proposal.md");
        File.WriteAllText(full, "# Plain\n\nWritten by an agent through the board server.\n");
        File.AppendAllText(Path.Combine(a.Data, "server-writes.log"), full + Environment.NewLine + Path.Combine(temp, "elsewhere.md") + Environment.NewLine);

        await a.Sync.SyncNow(a.Root);

        Assert.Contains("plain-change/proposal.md", a.Seen[^1].EditedInApp!);
        Assert.Single(a.Seen[^1].EditedInApp!, p => p.Contains("proposal.md"));
        Assert.False(File.Exists(Path.Combine(a.Data, "server-writes.log")));   // read once and cleared
    }

    [Fact(DisplayName = "The board server records the paths it writes so the desktop app can tell them from git's")]
    public void BoardServerRecordsItsWrites()
    {
        Computer a = NewComputer("a");
        long cursor = OpenSpecStoreApi.ChangesSince(long.MaxValue).Cursor;
        string file = a.Repo.PathOf(Proposal);
        OpenSpecStoreApi.ReadText(file);
        Assert.True(OpenSpecStoreApi.WriteText(file, "# Written by a tool call\n").Success);

        long next = ServerWrites.Flush(a.Data, cursor);

        Assert.True(next > cursor);
        Assert.Equal([Path.GetFullPath(file)], File.ReadAllLines(Path.Combine(a.Data, ServerWrites.FileName)));
        Assert.Equal(next, ServerWrites.Flush(a.Data, next));          // nothing new: nothing appended
        Assert.Single(File.ReadAllLines(Path.Combine(a.Data, ServerWrites.FileName)));
    }

    [Fact(DisplayName = "The git folder is passed to the sync, whether it is a directory or a worktree's file")]
    public async Task GitFolderIsPassedWhateverItIs()
    {
        Computer a = NewComputer("a");
        Directory.CreateDirectory(Path.Combine(a.Root, ".git"));
        a.ConnectFolder(Storage);
        await a.Pass();
        Assert.Equal(Path.Combine(a.Root, ".git"), a.Seen[^1].GitFolder);

        Computer b = NewComputer("b");
        File.WriteAllText(Path.Combine(b.Root, ".git"), "gitdir: C:/somewhere/.git/worktrees/b");
        b.ConnectFolder(Storage);
        await b.Pass();
        Assert.Equal(Path.Combine(b.Root, ".git"), b.Seen[^1].GitFolder);

        Computer c = NewComputer("c");
        c.ConnectFolder(Storage);
        await c.Pass();
        Assert.Null(c.Seen[^1].GitFolder);
    }

    [Fact(DisplayName = "The first connection that replaced local files names the backup folder in a toast")]
    public async Task BackupIsToldOnce()
    {
        Computer a = NewComputer("a");
        a.ConnectFolder(Storage);
        await a.Pass();
        Computer b = NewComputer("b");
        File.WriteAllText(Path.Combine(b.Root, Proposal), "# Different\n\nA proposal that only exists on this computer.\n");

        b.ConnectFolder(Storage);
        await b.Pass();

        string backup = Assert.Single(b.Toasts, t => t.Contains("backup", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(b.Data, backup);
        Assert.True(Directory.Exists(backup[(backup.IndexOf("backup folder ", StringComparison.Ordinal) + "backup folder ".Length)..]));   // it names the folder the copies are in
        Assert.Contains("A proposal that only exists on this computer.", Directory.EnumerateFiles(b.Data, "*", SearchOption.AllDirectories).Select(File.ReadAllText).First(t => t.Contains("only exists on this computer")));
    }

    [Fact(DisplayName = "Choosing this folder only again stops the sync and leaves the storage as it was")]
    public async Task DisconnectingStopsTheSync()
    {
        Computer a = NewComputer("a");
        a.ConnectFolder(Storage);
        await a.Pass();
        Assert.True(a.Sync.IsActive(a.Root));
        ChangeTitle(a, "Changed while connected");                       // a wait is pending

        Assert.True(a.Settings.Disconnect(a.Root).Success);
        await a.Pass();
        int runs = a.Sync.Runs(a.Root);
        await a.Sync.QuietElapsed(a.Root);                               // neither the wait, a trigger nor the exit syncs it any more
        await a.Sync.SyncOnExit();

        Assert.False(a.Sync.IsActive(a.Root));
        Assert.Equal(runs, a.Sync.Runs(a.Root));
        Assert.True(File.Exists(Path.Combine(Storage, "manifest.json")));
    }


    // ── why a sync is blocked, and what the user is told ──

    private static SyncReport Blocked(SyncBlock why, string message) => new(SyncOutcome.Blocked, message, [], [], [], [], [], [], [], blocked: why);
    private static SyncReport Offline() => new(SyncOutcome.Failed, "offline", [], [], [], [], [], [], []);
    private static void BeginMerge(Computer c)
    {
        Directory.CreateDirectory(Path.Combine(c.Root, ".git"));
        File.WriteAllText(Path.Combine(c.Root, ".git", "MERGE_HEAD"), "0123456789abcdef0123456789abcdef01234567\n");
    }
    private static void EndMerge(Computer c) => File.Delete(Path.Combine(c.Root, ".git", "MERGE_HEAD"));

    [Fact(DisplayName = "Allow and sync is offered for the deletion brake only, never for a git pause")]
    public async Task AllowIsOfferedForTheBrakeOnly()
    {
        Computer a = NewComputer("a");
        a.ConnectFolder(Storage);
        await a.Pass();
        await using BunitContext ctx = PageContext(a);
        IRenderedComponent<SettingsPage> page = ctx.Render<SettingsPage>();

        a.Engine = _ => Task.FromResult(Blocked(SyncBlock.MassDelete, "A message that does not say what the old host looked for."));   // the reason decides, not the wording
        await a.Sync.SyncNow(a.Root);
        Assert.True(a.Sync.Status(a.Root).CanAllow);
        page.WaitForAssertion(() => Assert.NotNull(Section(page).QuerySelector("button.bs-allow")));

        a.Engine = _ => Task.FromResult(Blocked(SyncBlock.GitBusy, "Sync is paused while a git merge is in progress. If you want to allow it, finish the merge."));   // even a wording that once looked like the brake's
        await a.Sync.SyncNow(a.Root);
        Assert.False(a.Sync.Status(a.Root).CanAllow);
        Assert.StartsWith("Paused: Sync is paused while a git merge", a.Line);
        page.WaitForAssertion(() =>
        {
            Assert.Null(Section(page).QuerySelector("button.bs-allow"));
            Assert.StartsWith("Paused:", Section(page).QuerySelector(".bs-state")!.TextContent.Trim());
        });

        a.Engine = _ => Task.FromResult(Blocked(SyncBlock.None, "allow it"));                         // a report that gives no reason offers nothing
        await a.Sync.SyncNow(a.Root);
        Assert.False(a.Sync.Status(a.Root).CanAllow);

        a.Engine = null;                                                                             // and with the real engine: a real git pause and a real brake
        BeginMerge(a);
        await a.Sync.SyncNow(a.Root);
        Assert.False(a.Sync.Status(a.Root).CanAllow);
        EndMerge(a);
    }

    [Fact(DisplayName = "The state line shows a git pause without waiting for the next sync")]
    public async Task TheStateLineShowsAGitPauseAtOnce()
    {
        Computer a = NewComputer("a");
        a.ConnectFolder(Storage);
        await a.Pass();
        await a.Sync.SyncNow(a.Root);
        Assert.StartsWith("Nothing new", a.Line);

        int runs = a.Sync.Runs(a.Root);
        BeginMerge(a);
        Assert.StartsWith("Paused: Sync is paused while a git merge is in progress", a.Line);       // asked straight away: no sync ran in between
        Assert.Equal(runs, a.Sync.Runs(a.Root));

        await using BunitContext ctx = PageContext(a);
        IRenderedComponent<SettingsPage> page = ctx.Render<SettingsPage>();                           // Settings opened in the middle of the merge

        string line = Section(page).QuerySelector(".bs-state")!.TextContent.Trim();
        Assert.StartsWith("Paused: Sync is paused while a git merge is in progress", line);
        Assert.Contains("bad", Section(page).QuerySelector(".bs-state")!.ClassList);
        Assert.Null(Section(page).QuerySelector("button.bs-allow"));
        Assert.False(a.Sync.Status(a.Root).CanAllow);

        EndMerge(a);
        Assert.False(a.Sync.Status(a.Root).CanAllow);
        await a.Sync.SyncNow(a.Root);
        Assert.DoesNotContain("Paused", a.Line);                                                      // the next sync takes the line out of the pause

        a.Repo.Write(Proposal, "# Add health check\n\n<<<<<<< HEAD\nours\n=======\ntheirs\n>>>>>>> main\n");      // a pull that left markers
        Assert.StartsWith("Paused: Sync is paused: add-health-check/proposal.md still has merge conflict markers", a.Line);
    }

    [Fact(DisplayName = "A git pause toasts once, not on every attempt")]
    public async Task AGitPauseToastsOnce()
    {
        Computer a = NewComputer("a");
        a.ConnectFolder(Storage);
        await a.Pass();

        BeginMerge(a);
        await a.Sync.SyncNow(a.Root);
        await a.Sync.SyncNow(a.Root);
        await a.Sync.SyncNow(a.Root);
        Assert.Single(a.Toasts, t => t.Contains("paused", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(a.Toasts, t => t.Contains("See Settings"));                           // nothing to do there: git has to finish

        a.Repo.Write(Proposal, "# Add health check\n\n<<<<<<< HEAD\nours\n=======\ntheirs\n>>>>>>> main\n");   // the pause changes its words (and one more file): still the same pause
        await a.Sync.SyncNow(a.Root);
        Assert.Single(a.Toasts, t => t.Contains("paused", StringComparison.OrdinalIgnoreCase));

        EndMerge(a);
        a.Repo.Write(Proposal, "# Add health check\n\nResolved.\n");
        await a.Sync.SyncNow(a.Root);
        Assert.StartsWith("Synced", a.Line);                                                          // it went on

        BeginMerge(a);                                                                                // a new pause is news again
        await a.Sync.SyncNow(a.Root);
        Assert.Equal(2, a.Toasts.Count(t => t.Contains("paused", StringComparison.OrdinalIgnoreCase)));
    }

    // ── what the sync tells the user about stale files and the trash ──

    [Fact(DisplayName = "Board files replaced because they were older than the synced version are announced once")]
    public async Task StaleFilesAreAnnouncedOnce()
    {
        (Computer a, Computer b) = await TwoConnected();
        string old = b.Read(Yaml);
        a.Do(() => WorkItemsApi.SetTitle(123, "A newer title"));
        await a.Sync.SyncNow(a.Root);
        await b.Sync.SyncNow(b.Root);
        Assert.Contains("A newer title", b.Read(Yaml));

        b.Repo.Write(Yaml, old);                                          // a checkout brings the old file back: git wrote it, the app did not
        SyncReport report = (await b.Sync.SyncNow(b.Root))!;

        Assert.Equal([Yaml.Replace("openspec/changes/", "")], report.Stale);
        Assert.Contains("A newer title", b.Read(Yaml));
        Assert.Equal(["1 board file was older than the synced version and was replaced by it"], b.Toasts.Where(t => t.Contains("older than the synced version")));
        await b.Sync.SyncNow(b.Root);
        Assert.Single(b.Toasts, t => t.Contains("older than the synced version"));                 // not again for the quiet syncs after it

        b.Engine = _ => Task.FromResult(new SyncReport(SyncOutcome.Synced, null, [], ["a/x.md", "b/y.md", "c/z.md"], [], [], [], [], [], ["a/x.md", "b/y.md", "c/z.md"]));
        await b.Sync.SyncNow(b.Root);
        Assert.Contains("3 board files were older than the synced version and were replaced by it", b.Toasts);
    }

    [Fact(DisplayName = "Files the sync removed are announced with the trash, and the trash folder can be opened")]
    public async Task TrashIsAnnouncedAndCanBeOpened()
    {
        (Computer a, Computer b) = await TwoConnected();
        string? opened = null;
        await using BunitContext ctx = PageContext(b, opener: p => { opened = p; return (true, ""); });
        IRenderedComponent<SettingsPage> page = ctx.Render<SettingsPage>();
        Assert.Null(Section(page).QuerySelector(".bs-open-trash"));                                   // nothing was removed yet: no folder, no link

        File.Delete(Path.Combine(a.Root, Item, "notes", "n123.md"));
        await a.Sync.SyncNow(a.Root);
        SyncReport report = (await b.Sync.SyncNow(b.Root))!;

        Assert.Single(report.Trashed);
        Assert.Equal(["1 file the sync removed is kept in the trash for 30 days"], b.Toasts.Where(t => t.Contains("kept in the trash")));
        string trash = Path.Combine(b.Settings.StateFolder(b.Root), "trash");
        Assert.Equal(trash, b.Sync.Status(b.Root).TrashFolder);
        Assert.StartsWith(trash, report.TrashFolder);
        Assert.True(File.Exists(report.Trashed[0]));

        page.WaitForAssertion(() => Assert.Equal("Open trash folder", Section(page).QuerySelector(".bs-open-trash")!.TextContent.Trim()));
        await page.InvokeAsync(() => Section(page).QuerySelector(".bs-open-trash")!.Click());
        page.WaitForAssertion(() => Assert.Equal(trash, opened));

        b.Engine = _ => Task.FromResult(new SyncReport(SyncOutcome.Synced, null, [], [], [], ["a", "b", "c"], [], [], [], trashed: ["x", "y", "z"]));
        await b.Sync.SyncNow(b.Root);
        Assert.Contains("3 files the sync removed are kept in the trash for 30 days", b.Toasts);

        Assert.Null(a.Sync.Status(a.Root).TrashFolder);                                              // the computer that deleted it has none
    }

    [Fact(DisplayName = "The backup folder in the first-sync toast is the one the engine reports")]
    public async Task TheBackupFolderComesFromTheReport()
    {
        Computer a = NewComputer("a");
        a.ConnectFolder(Storage);
        await a.Pass();
        a.Engine = _ => Task.FromResult(new SyncReport(SyncOutcome.Synced, null, [], [], [], [], [], [], [Path.Combine(temp, "x", "deep", "er", "file.md")], backupFolder: Path.Combine(temp, "x")));

        await a.Sync.SyncNow(a.Root);

        Assert.Single(a.Toasts, t => t.EndsWith("backup folder " + Path.Combine(temp, "x")));
    }

    // ── whose change was dropped ──

    [Fact(DisplayName = "The conflict event and the conflict note name whose change was dropped")]
    public async Task EventAndNoteNameWhoseChangeWasDropped()
    {
        (Computer a, Computer b) = await TwoConnected(ADayLater);

        // The other computer's change is later and already in the storage: this computer's is dropped.
        a.Do(() => WorkItemsApi.SetStatus(123, "Blocked"));
        WrittenAt(a, Yaml, 1);
        b.Do(() => WorkItemsApi.SetStatus(123, "In Review"));
        WrittenAt(b, Yaml, 2);
        await b.Sync.SyncNow(b.Root);
        await a.Sync.SyncNow(a.Root);
        Assert.Contains("kept \"In Review\", dropped \"Blocked\" (this computer's change)", a.Do(() => Events(123, "sync-conflict").Single(e => e.Text.StartsWith("status"))).Text);

        // This computer's change is the later one: the other computer's is dropped.
        await a.Sync.SyncNow(a.Root);
        await b.Sync.SyncNow(b.Root);
        a.Do(() => WorkItemsApi.SetStatus(123, "Confirmed"));
        WrittenAt(a, Yaml, 3);
        await a.Sync.SyncNow(a.Root);
        b.Do(() => WorkItemsApi.SetStatus(123, "Planned"));
        WrittenAt(b, Yaml, 4);
        await b.Sync.SyncNow(b.Root);
        Assert.Contains("kept \"Planned\", dropped \"Confirmed\" (another computer's change)", b.Do(() => Events(123, "sync-conflict").Single(e => e.Text.Contains("dropped \"Confirmed\""))).Text);

        // The text of the lost paragraph is a note whose first line says the same.
        await a.Sync.SyncNow(a.Root);
        await b.Sync.SyncNow(b.Root);
        a.Do(() => WorkItemsApi.SetDescription(123, "Alpha: poll the endpoint from the monitor every minute."));
        WrittenAt(a, Proposal, 5);
        b.Do(() => WorkItemsApi.SetDescription(123, "Beta: the monitor asks the endpoint twice a minute."));
        WrittenAt(b, Proposal, 6);
        await b.Sync.SyncNow(b.Root);
        await a.Sync.SyncNow(a.Root);
        a.Do(OpenSpecStoreApi.Refresh);
        Note here = a.Do(() => NotesApi.GetForWorkItem(123).Single(n => n.Title.StartsWith("Sync conflict")));
        Assert.Contains("this computer's change", here.Body.Split('\n')[0]);
        Assert.Contains("Alpha: poll the endpoint", here.Body);

        await a.Sync.SyncNow(a.Root);
        await b.Sync.SyncNow(b.Root);
        a.Do(() => WorkItemsApi.SetDescription(123, "Delta: the monitor always asks."));
        WrittenAt(a, Proposal, 7);
        await a.Sync.SyncNow(a.Root);
        b.Do(() => WorkItemsApi.SetDescription(123, "Gamma: the monitor never asks."));
        WrittenAt(b, Proposal, 8);
        await b.Sync.SyncNow(b.Root);
        b.Do(OpenSpecStoreApi.Refresh);
        Note there = b.Do(() => NotesApi.GetForWorkItem(123).Single(n => n.Title.StartsWith("Sync conflict") && n.Body.Contains("Delta")));
        Assert.Contains("another computer's change", there.Body.Split('\n')[0]);
    }

    [Fact(DisplayName = "A conflict file kept outside the board says whose change was dropped")]
    public async Task ConflictFileNamesTheSide()
    {
        Computer a = NewComputer("a");
        a.ConnectFolder(Storage);
        await a.Pass();
        a.Engine = _ => Task.FromResult(new SyncReport(SyncOutcome.Synced, null, [], [], [], [], [],
            [new SyncConflict("vanished-change/proposal.md", "edited on this computer, deleted on another", "deleted", "Lost text.", DroppedHere: true)], []));

        await a.Sync.SyncNow(a.Root);

        Assert.Contains("dropped: this computer's change", Directory.EnumerateFiles(a.Data, "*", SearchOption.AllDirectories).Select(File.ReadAllText).Single(t => t.Contains("Lost text.")));
    }

    // ── app edits survive a restart ──

    private static string EditedFile(Computer c) => Path.Combine(c.Settings.ProjectFolder(c.Root), "edited-in-app.txt");
    private static string ServerLog(Computer c) => Path.Combine(c.Data, ServerWrites.FileName);
    private const string PlainProposal = "plain-change/proposal.md";

    /// <summary>The remembered edits as a reader sees them; a read that meets the atomic replace just then sees none.</summary>
    private static string[] Edited(Computer c)
    {
        try { return File.Exists(EditedFile(c)) ? File.ReadAllLines(EditedFile(c)) : []; }
        catch (IOException) { return []; }
    }

    [Fact(DisplayName = "Paths the app wrote are still app edits after the app restarts")]
    public async Task AppEditsSurviveARestart()
    {
        Computer a = NewComputer("a");
        a.ConnectFolder(Storage);
        await a.Pass();
        a.Do(() => WorkItemsApi.SetTitle(123, "Written before the restart"));
        File.WriteAllText(Path.Combine(a.Root, "openspec/changes", PlainProposal), "# Plain\n\nEdited by git or an editor.\n");   // an outside edit
        a.Engine = _ => Task.FromResult(Offline());
        await a.Sync.SyncNow(a.Root);                                                                 // nothing succeeded, so nothing is cleared
        Assert.True(File.Exists(EditedFile(a)));                                                     // written when the set was gathered, not later by a timer
        Assert.Contains("add-health-check/.openspec.yaml", Edited(a));
        Assert.DoesNotContain(PlainProposal, Edited(a));
        Assert.StartsWith(a.Data, EditedFile(a));                                                    // in the project's app-data folder, not in the repository

        a.Restart();                                                                                 // the app is closed and opened again: nothing in memory
        await a.Pass();
        Assert.Contains("add-health-check/.openspec.yaml", a.Seen[^1].EditedInApp!);
        Assert.Contains("add-health-check/events.log", a.Seen[^1].EditedInApp!);
        Assert.DoesNotContain(PlainProposal, a.Seen[^1].EditedInApp!);                              // the outside edit is still an outside edit

        a.Engine = null;
        await a.Sync.SyncNow(a.Root);                                                                // a sync that succeeds takes them
        Assert.Contains("add-health-check/.openspec.yaml", a.Seen[^1].EditedInApp!);
        Assert.False(File.Exists(EditedFile(a)));                                                    // cleared with the set in memory
        a.Restart();
        await a.Pass();
        Assert.Empty(a.Seen[^1].EditedInApp!);                                                       // cleared exactly as the set in memory is
    }

    [Fact(DisplayName = "An app write is remembered when the board changes, even before any sync has seen it")]
    public async Task AnAppWriteIsRememberedBeforeTheNextSync()
    {
        Computer a = NewComputer("a");                                                               // no sync runs in this test: nothing triggers one
        a.ConnectFolder(Storage);
        await a.Pass();
        a.Engine = _ => Task.FromResult(Offline());
        int runs = a.Sync.Runs(a.Root);

        a.Do(() => WorkItemsApi.SetTitle(123, "Written just before the app died"));
        a.Sync.BoardChanged();                                                                       // what the workspace does for every change
        Assert.True(File.Exists(EditedFile(a)));
        Assert.Equal(runs, a.Sync.Runs(a.Root));
        Assert.Contains("add-health-check/.openspec.yaml", Edited(a));

        for (int i = 0; i < 20; i++) { a.Do(() => WorkItemsApi.SetTitle(123, "Title " + i)); a.Sync.BoardChanged(); }   // a burst: written, but not once per change
        Assert.Contains("add-health-check/events.log", Edited(a));
        Assert.Empty(Directory.EnumerateFiles(Path.GetDirectoryName(EditedFile(a))!, "edited-in-app.txt.tmp"));   // atomic: never a half-written file left
    }

    [Fact(DisplayName = "Choosing this folder only again forgets the remembered app edits")]
    public async Task DisconnectingForgetsTheAppEdits()
    {
        Computer a = NewComputer("a");
        a.ConnectFolder(Storage);
        await a.Pass();
        a.Do(() => WorkItemsApi.SetTitle(123, "Written while connected"));
        a.Engine = _ => Task.FromResult(Offline());
        await a.Sync.SyncNow(a.Root);
        Assert.True(File.Exists(EditedFile(a)));

        Assert.True(a.Settings.Disconnect(a.Root).Success);
        await a.Pass();
        Assert.False(File.Exists(EditedFile(a)));

        a.ConnectFolder(Storage);
        await a.Pass();
        Assert.Empty(a.Seen[^1].EditedInApp!);
    }

    [Fact(DisplayName = "A remembered-edits file that is damaged or names paths outside the board is read as far as it is safe")]
    public async Task ADamagedEditsFileIsReadSafely()
    {
        Computer a = NewComputer("a");
        a.ConnectFolder(Storage);
        await a.Pass();
        a.Sync.Dispose();
        File.WriteAllText(EditedFile(a), "\n..\\..\\evil.md\n" + Path.Combine(temp, "absolute.md") + "\nadd-health-check\\proposal.md\n   \n");
        a.Engine = _ => Task.FromResult(Offline());

        a.Restart();
        await a.Pass();

        Assert.Equal(["add-health-check/proposal.md"], a.Seen[^1].EditedInApp!.ToArray());
    }

    [Fact(DisplayName = "Paths the board server wrote while the app was closed are app edits on the next start")]
    public async Task ServerWritesWhileTheAppWasClosedAreAppEdits()
    {
        Computer a = NewComputer("a");
        a.ConnectFolder(Storage);
        await a.Pass();
        a.Sync.Dispose();                                                                            // the app is closed; the board server goes on
        string full = Path.Combine(a.Root, "openspec/changes", PlainProposal);
        File.WriteAllText(full, "# Plain\n\nWritten by an agent through the board server.\n");
        File.AppendAllText(ServerLog(a), full + Environment.NewLine + full + Environment.NewLine);   // logged at every flush
        a.Engine = _ => Task.FromResult(Offline());

        a.Restart();
        await a.Pass();

        Assert.Single(a.Seen[^1].EditedInApp!, p => p == PlainProposal);
        Assert.False(File.Exists(ServerLog(a)));                                                     // read, and it is in the project's file now

        a.Restart();                                                                                 // the log is gone, the path is not
        await a.Pass();
        Assert.Contains(PlainProposal, a.Seen[^1].EditedInApp!);
    }

    [Fact(DisplayName = "A batch of server writes that a crash left half read is read on the next start")]
    public async Task ACrashedReadLosesNoServerWrites()
    {
        Computer a = NewComputer("a");
        a.ConnectFolder(Storage);
        await a.Pass();
        a.Sync.Dispose();
        string full = Path.Combine(a.Root, "openspec/changes", PlainProposal);
        File.WriteAllText(full, "# Plain\n\nWritten by an agent through the board server.\n");
        File.WriteAllText(ServerLog(a) + ".deadbeef.reading", full + Environment.NewLine);          // taken by a run that died before it used it
        a.Engine = _ => Task.FromResult(Offline());

        a.Restart();
        await a.Pass();

        Assert.Contains(PlainProposal, a.Seen[^1].EditedInApp!);
        Assert.Empty(Directory.EnumerateFiles(a.Data, "*.reading"));
    }

    [Fact(DisplayName = "Server writes of one project are not taken by another, and wait for it if it is not syncing yet")]
    public async Task ServerWritesStayWithTheirProject()
    {
        Computer a = NewComputer("a");
        Computer other = NewComputer("other");
        a.ConnectFolder(Storage);
        await a.Pass();
        a.Engine = _ => Task.FromResult(Offline());
        string mine = Path.Combine(a.Root, "openspec/changes", PlainProposal);
        string theirs = Path.Combine(other.Root, "openspec/changes", PlainProposal);
        File.WriteAllText(mine, "# Plain\n\nMine.\n");
        File.WriteAllText(theirs, "# Plain\n\nTheirs.\n");
        File.WriteAllLines(ServerLog(a), [mine, theirs, Path.Combine(temp, "gone.md")]);

        await a.Sync.SyncNow(a.Root);                                                                // the other project is not connected here yet

        Assert.Contains(PlainProposal, a.Seen[^1].EditedInApp!);
        Assert.Equal([Path.GetFullPath(theirs)], File.ReadAllLines(ServerLog(a)));                                     // its path waits; a path of a file that is gone does not

        a.Settings.Connect(other.Root, "folder");
        a.Settings.SetValue(other.Root, "path", Path.Combine(temp, "storage-other"));
        await a.Sync.Attach(other.Root);
        Assert.Contains(PlainProposal, a.Seen.Last(o => o.LocalRoot.StartsWith(other.Root)).EditedInApp!);
        Assert.False(File.Exists(ServerLog(a)));
    }

    [Fact(DisplayName = "A server log at its size cap is cut down to its distinct paths before it stops growing")]
    public void TheServerLogIsCompactedAtItsCap()
    {
        Computer a = NewComputer("a");
        string old = a.Repo.PathOf(Proposal);
        Directory.CreateDirectory(a.Data);
        File.WriteAllLines(ServerLog(a), Enumerable.Repeat(Path.GetFullPath(old), 30_000));         // the same path at every flush: over 1 MB
        Assert.True(new System.IO.FileInfo(ServerLog(a)).Length > 1_000_000);

        long cursor = OpenSpecStoreApi.ChangesSince(long.MaxValue).Cursor;
        string changed = a.Repo.PathOf(Yaml);
        OpenSpecStoreApi.ReadText(changed);
        Assert.True(OpenSpecStoreApi.WriteText(changed, a.Read(Yaml) + "# touched\n").Success);
        ServerWrites.Flush(a.Data, cursor);

        Assert.Equal([Path.GetFullPath(old), Path.GetFullPath(changed)], File.ReadAllLines(ServerLog(a)));   // nothing was lost, and the new path fits

        File.WriteAllLines(ServerLog(a), Enumerable.Range(0, 40_000).Select(i => Path.Combine(temp, $"distinct-{i}-padding-padding.md")));   // distinct paths that alone are over the cap
        long size = new System.IO.FileInfo(ServerLog(a)).Length;
        Assert.True(size > 1_000_000);
        OpenSpecStoreApi.ReadText(changed);
        Assert.True(OpenSpecStoreApi.WriteText(changed, a.Read(Yaml) + "# touched again\n").Success);
        ServerWrites.Flush(a.Data, cursor);

        Assert.Equal(size, new System.IO.FileInfo(ServerLog(a)).Length);                                      // it stops growing; what is not recorded looks like an outside edit
    }

    // ── git ──

    [Fact(DisplayName = "Connecting a storage adds the event log union-merge line to .gitattributes once")]
    public async Task ConnectingAddsTheUnionMergeLineOnce()
    {
        const string Line = "openspec/**/events.log merge=union";
        Computer a = NewComputer("a");
        Directory.CreateDirectory(Path.Combine(a.Root, ".git"));
        a.Repo.Write(".gitattributes", "*.png binary\n");

        a.ConnectFolder(Storage);
        await a.Pass();
        a.Sync.Attach(a.Root);                          // the next open
        a.Settings.SetValue(a.Root, "path", Storage);
        await a.Pass();

        string text = a.Read(".gitattributes").Replace("\r", "");
        Assert.StartsWith("*.png binary\n", text);
        Assert.Equal(1, text.Split('\n').Count(l => l.Trim() == Line));
        Assert.Contains("# Repo Tasks", text);
        Assert.Single(a.Toasts, t => t.Contains(".gitattributes"));

        Computer b = NewComputer("b");                  // not a git repository: nothing is created
        b.ConnectFolder(Storage);
        await b.Pass();
        Assert.False(File.Exists(Path.Combine(b.Root, ".gitattributes")));

        Computer c = NewComputer("c");                  // no file yet: it is created
        Directory.CreateDirectory(Path.Combine(c.Root, ".git"));
        c.ConnectFolder(Storage);
        await c.Pass();
        Assert.Equal(1, c.Read(".gitattributes").Replace("\r", "").Split('\n').Count(l => l.Trim() == Line));
    }
    // ── the settings files tolerate a reader, and the engine's newer report lists reach the page ──

    private StorageProvider Vault => new("vault", "Vault", "A test storage whose only setting is a key kept on this computer.",
        [new("token", "Token", Secret: true, OnThisComputer: true)], _ => BoardSyncApi.FolderStorage(Storage));

    [Fact(DisplayName = "A settings file that stays locked ends the save with a message, not an exception")]
    public async Task ALockedSettingsFileEndsTheSaveWithAMessage()
    {
        if (!OperatingSystem.IsWindows()) return;   // a share mode of None is only enforced there
        Computer a = NewComputer("a", [Vault]);
        Assert.True(a.Settings.Connect(a.Root, "vault").Success);
        Assert.True(a.Settings.SetValue(a.Root, "token", "first").Success);
        await a.Pass();
        string file = Directory.EnumerateFiles(a.Data, "credentials.json", SearchOption.AllDirectories).Single();

        using (FileStream hold = new(file, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            (bool ok, string message) = a.Settings.SetValue(a.Root, "token", "second");
            Assert.False(ok);
            Assert.NotEqual("", message);
            Assert.NotNull(a.Sync.Status(a.Root).Line);                   // the app still answers
            _ = a.Settings.Problem(a.Root);
            _ = a.Settings.Open(a.Root);
            _ = await a.Sync.SyncNow(a.Root);                             // and a sync run does not throw
        }

        Assert.Equal("first", a.Settings.ComputerValue(a.Root, "token"));   // the failed save changed nothing
        Assert.True(a.Settings.SetValue(a.Root, "token", "third").Success);
        Assert.Equal("third", a.Settings.ComputerValue(a.Root, "token"));
    }

    [Fact(DisplayName = "Files a download replaced are not announced as removed, and the trash link shows")]
    public async Task ReplacedFilesAreNotAnnouncedAsRemoved()
    {
        Computer a = NewComputer("a");
        a.ConnectFolder(Storage);
        await a.Pass();
        string trash = Path.Combine(a.Settings.StateFolder(a.Root), "trash");
        Directory.CreateDirectory(Path.Combine(trash, "20260101-000000"));
        string copy = Path.Combine(trash, "20260101-000000", "notes", "n1.md");
        int before = a.Toasts.Count;

        a.Engine = _ => Task.FromResult(new SyncReport(SyncOutcome.Synced, null, [], ["notes/n1.md"], [], [], [], [], [], replaced: [copy]));
        SyncReport report = (await a.Sync.SyncNow(a.Root))!;

        Assert.Equal([copy], report.Replaced);
        Assert.Equal(["Received 1 change from the storage"], a.Toasts.Skip(before));                  // not a word about the trash or removals
        Assert.Equal(trash, a.Sync.Status(a.Root).TrashFolder);
        await using BunitContext ctx = PageContext(a, opener: _ => (true, ""));
        IRenderedComponent<SettingsPage> page = ctx.Render<SettingsPage>();
        Assert.Equal("Open trash folder", Section(page).QuerySelector(".bs-open-trash")!.TextContent.Trim());
    }

    [Fact(DisplayName = "Links the sync skipped are announced once and shown in the state line")]
    public async Task SkippedLinksAreAnnouncedOnce()
    {
        Computer a = NewComputer("a");
        a.ConnectFolder(Storage);
        await a.Pass();
        int before = a.Toasts.Count;
        await using BunitContext ctx = PageContext(a);
        IRenderedComponent<SettingsPage> page = ctx.Render<SettingsPage>();
        Assert.Null(Section(page).QuerySelector(".bs-skipped"));

        a.Engine = _ => Task.FromResult(new SyncReport(SyncOutcome.NothingToDo, null, [], [], [], [], [], [], [], skippedLinks: ["add-health-check/shortcut", "other/link"]));
        for (int i = 0; i < 3; i++) await a.Sync.SyncNow(a.Root);                                   // the same links at every look: one toast

        Assert.Equal(["2 links inside the board were skipped: the sync never follows links. add-health-check/shortcut…"], a.Toasts.Skip(before).Where(t => t.Contains("skipped")));
        page.Render();
        Assert.Contains("2 links inside the board were skipped", Section(page).QuerySelector(".bs-skipped")!.TextContent);

        a.Engine = _ => Task.FromResult(new SyncReport(SyncOutcome.NothingToDo, null, [], [], [], [], [], [], [], skippedLinks: ["add-health-check/shortcut", "other/link", "third"]));
        await a.Sync.SyncNow(a.Root);                                                                // another set: told again
        Assert.Equal(2, a.Toasts.Skip(before).Count(t => t.Contains("skipped")));

        a.Engine = _ => Task.FromResult(new SyncReport(SyncOutcome.NothingToDo, null, [], [], [], [], [], [], []));
        await a.Sync.SyncNow(a.Root);                                                                // the last sync skipped none: the line goes
        page.Render();
        Assert.Null(Section(page).QuerySelector(".bs-skipped"));
    }

    [Fact(DisplayName = "The git pause in the state line ignores conflict markers in files the sync itself wrote")]
    public async Task TheGitPauseIgnoresMarkersInFilesTheSyncWrote()
    {
        (Computer a, Computer b) = await TwoConnected();
        const string Marked = "<<<<<<< ours\nthis\n=======\nthat\n>>>>>>> theirs\n";
        b.Repo.Write(Item + "/marked.md", Marked);
        await b.Sync.SyncNow(b.Root);                                                                // no git folder here: uploaded as it is
        Directory.CreateDirectory(Path.Combine(a.Root, ".git"));
        await a.Sync.SyncNow(a.Root);                                                                // the sync writes the file, markers and all
        Assert.Equal(Marked, a.Read(Item + "/marked.md").Replace("\r", ""));

        string local = Path.Combine(a.Root, "openspec", "changes");
        Assert.Contains("merge conflict markers", BoardSyncApi.GitPause(local, Path.Combine(a.Root, ".git")));   // what a stateless look would say
        Assert.DoesNotContain("Paused", a.Line);                                                     // the status agrees with the sync

        a.Repo.Write(Item + "/marked.md", Marked + "edited by hand\n");                              // a change of the user's own is looked at again
        Assert.StartsWith("Paused: Sync is paused: ", a.Line);
    }

    // ═══ the attachments folder syncs beside the board ═══

    /// <summary>Not valid UTF-8, with the PNG signature: a picture as the app writes it.</summary>
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0xFF, 0xFE, 0x00, 0xC3, 0x28, 0x80, 0x81, 0x00, 0x42];
    private const string Pic = "wi1/n1/pic.png";
    private string StoredManifest => Path.Combine(Storage, "attachments", "manifest.json");

    [Fact(DisplayName = "A picture attached on one computer shows up in the other computer's attachments folder after its sync")]
    public async Task AttachedPictureArrivesOnTheOtherComputer()
    {
        (Computer a, Computer b) = await TwoConnected();
        a.WriteAttachment(Pic, Png);

        await a.Sync.SyncNow(a.Root);
        Assert.True(File.Exists(StoredManifest));
        Assert.False(b.HasAttachment(Pic));
        await b.Sync.SyncNow(b.Root);

        Assert.Equal(Png, b.ReadAttachment(Pic));
        SyncOptions run = a.AttachmentRuns.First();
        Assert.Equal(Path.Combine(a.Settings.ProjectFolder(a.Root), "state-attachments"), run.StateFolder);   // its own state, never the board's
        Assert.Equal(a.AttachDir, run.LocalRoot);
        Assert.Null(run.EditedInApp);
        Assert.Null(run.GitFolder);
        Assert.NotEqual(a.Settings.StateFolder(a.Root), run.StateFolder);
    }

    [Fact(DisplayName = "Attachments are not synced before the storage is approved on this computer")]
    public async Task AttachmentsWaitForTheApproval()
    {
        Computer c = NewComputer("a");
        c.Repo.Write("openspec/config.yaml", c.Read("openspec/config.yaml") + "  boardStorage:\n    provider: folder\n    path: '" + Storage.Replace("'", "''") + "'\n");   // what git brought: only proposed
        c.WriteAttachment(Pic, Png);
        await c.Sync.Attach(c.Root);
        c.Sync.BoardChanged();
        c.Sync.AttachmentsChanged();
        await c.Sync.QuietElapsed(c.Root);
        await c.Sync.SyncOnExit();

        Assert.NotNull(c.Settings.Proposal(c.Root));
        Assert.Empty(c.Seen);
        Assert.False(Directory.Exists(Storage));

        Assert.True(c.Settings.Approve(c.Root).Success);                       // allowed: now it goes
        await c.Pass();
        await c.Sync.SyncNow(c.Root);
        Assert.True(File.Exists(StoredManifest));
    }

    [Fact(DisplayName = "Attaching a file schedules a sync even when no board file changed")]
    public async Task AttachingSchedulesASync()
    {
        (Computer a, Computer b) = await TwoConnected();
        int before = a.Seen.Count;
        a.WriteAttachment(Pic, Png);                                           // the app saved a file; nothing on the board moved

        a.Sync.AttachmentsChanged();

        Assert.NotNull(a.Sync.QuietDue(a.Root));                               // the wait has started...
        await a.Sync.QuietElapsed(a.Root);                                     // ...and when it ends the attachment goes up
        Assert.True(File.Exists(StoredManifest));
        Assert.True(a.Seen.Count > before);
        await b.Sync.SyncNow(b.Root);
        Assert.Equal(Png, b.ReadAttachment(Pic));
    }

    [Fact(DisplayName = "A project with no attachments creates nothing in the storage for them")]
    public async Task NoAttachmentsCreateNothing()
    {
        (Computer a, Computer b) = await TwoConnected();
        await a.Sync.SyncNow(a.Root);
        await b.Sync.SyncNow(b.Root);

        Assert.False(Directory.Exists(Path.Combine(Storage, "attachments")));
        Assert.False(Directory.Exists(a.AttachDir));                           // and no empty folder here
        Assert.Empty(a.AttachmentRuns);

        Directory.CreateDirectory(a.AttachDir);                                // an empty folder is the same: nothing to say
        await a.Sync.SyncNow(a.Root);
        Assert.False(Directory.Exists(Path.Combine(Storage, "attachments")));
    }

    [Fact(DisplayName = "Turning Sync attachments too off stops the attachments sync and deletes nothing, and it is saved with the project")]
    public async Task TurningTheSwitchOffStopsTheAttachmentsSync()
    {
        (Computer a, Computer b) = await TwoConnected();
        a.WriteAttachment(Pic, Png);
        await a.Sync.SyncNow(a.Root);
        Assert.True(a.Settings.SyncAttachments(a.Root));                       // on unless the project says otherwise
        Assert.DoesNotContain("attachments:", a.Read("openspec/config.yaml"));
        string state = Path.Combine(a.Settings.ProjectFolder(a.Root), "state-attachments");
        Assert.True(Directory.Exists(state));
        string[] stored = [.. Directory.EnumerateFiles(Path.Combine(Storage, "attachments"), "*", SearchOption.AllDirectories)];

        await using BunitContext ctx = PageContext(a);
        IRenderedComponent<SettingsPage> page = ctx.Render<SettingsPage>();
        Assert.Equal("true", Section(page).QuerySelector(".bs-attachments")!.GetAttribute("aria-pressed"));
        await page.InvokeAsync(() => Section(page).QuerySelector(".bs-attachments")!.Click());                  // the switch

        page.WaitForAssertion(() => Assert.Equal("false", Section(page).QuerySelector(".bs-attachments")!.GetAttribute("aria-pressed")));
        Assert.False(a.Settings.SyncAttachments(a.Root));
        Assert.Matches(@"(?m)^\s+attachments: false\s*$", a.Read("openspec/config.yaml"));                      // saved with the project, under boardStorage
        Assert.Equal("folder", a.Settings.Config(a.Root)!.Provider);          // the storage itself is as it was
        await a.Pass();
        int runs = a.AttachmentRuns.Count();

        File.Delete(Path.Combine(a.AttachDir, Pic));                           // a deletion here must not reach the storage while it is off
        a.WriteAttachment("wi1/n1/second.png", Png);
        await a.Sync.SyncNow(a.Root);
        await b.Sync.SyncNow(b.Root);

        Assert.Equal(runs, a.AttachmentRuns.Count());
        Assert.True(Directory.Exists(state));                                  // its state is left alone
        Assert.Equal(stored, Directory.EnumerateFiles(Path.Combine(Storage, "attachments"), "*", SearchOption.AllDirectories));   // nothing uploaded, nothing deleted
        Assert.True(b.HasAttachment(Pic));

        await page.InvokeAsync(() => Section(page).QuerySelector(".bs-attachments")!.Click());                  // and on again
        page.WaitForAssertion(() => Assert.DoesNotContain("attachments:", a.Read("openspec/config.yaml")));      // absent = on
        Assert.True(a.Settings.SyncAttachments(a.Root));
    }

    [Fact(DisplayName = "The attachments switch is not part of the approval: flipping it never asks again")]
    public async Task TheSwitchIsNotPartOfTheApproval()
    {
        Computer a = NewComputer("a");
        a.ConnectFolder(Storage);
        await a.Pass();
        string before = a.Settings.AddressHash(a.Root)!;

        Assert.True(a.Settings.SetSyncAttachments(a.Root, false).Success);
        Assert.Null(a.Settings.Proposal(a.Root));
        Assert.Equal(before, a.Settings.AddressHash(a.Root));

        Assert.True(a.Settings.SetSyncAttachments(a.Root, true).Success);
        Assert.Null(a.Settings.Proposal(a.Root));

        // another computer's repository says "off" for a storage this computer allowed: still allowed, and off
        a.Repo.Write("openspec/config.yaml", a.Read("openspec/config.yaml").Replace("provider: folder", "provider: folder\n    attachments: false"));
        Assert.Null(a.Settings.Proposal(a.Root));
        Assert.False(a.Settings.SyncAttachments(a.Root));
        Assert.Equal(before, a.Settings.AddressHash(a.Root));

        Assert.False(a.Settings.SetSyncAttachments(Path.Combine(temp, "not-a-project"), true).Success);            // nothing connected: nothing to switch
        a.Do(() => OpenSpecStoreApi.ReadText(Path.Combine(a.Root, "openspec", "config.yaml")));                    // the store has seen the file as the "pull" left it, so writing it is no conflict
        Assert.True(a.Settings.Disconnect(a.Root).Success);
        Assert.False(a.Settings.SetSyncAttachments(a.Root, false).Success);
    }

    [Fact(DisplayName = "The state line counts board files and attachments together, and says when only attachments moved")]
    public async Task TheStateLineCountsBothKindsTogether()
    {
        (Computer a, Computer b) = await TwoConnected();
        a.WriteAttachment(Pic, Png);
        await a.Sync.SyncNow(a.Root);
        Assert.Matches(@"^Synced at \d\d:\d\d: 1 attachment sent$", a.Line);                      // only attachments moved: said plainly
        await b.Sync.SyncNow(b.Root);
        Assert.Matches(@"^Synced at \d\d:\d\d: 1 attachment received$", b.Line);
        Assert.Equal(["Received 1 attachment from the storage"], b.Toasts.Where(t => t.StartsWith("Received")));

        a.WriteAttachment("wi1/n1/second.png", Png);
        a.WriteAttachment("wi1/n1/third.png", Png);
        await a.Sync.SyncNow(a.Root);
        await b.Sync.SyncNow(b.Root);
        Assert.Matches(@"^Synced at \d\d:\d\d: 2 attachments received$", b.Line);

        a.Repo.Write(Proposal, a.Read(Proposal) + "\nmore\n");                          // a board file and an attachment in one go: one line, one count
        a.WriteAttachment("wi1/n1/fourth.png", Png);
        await a.Sync.SyncNow(a.Root);
        Assert.Matches(@"^Synced at \d\d:\d\d: 2 sent$", a.Line);
        await b.Sync.SyncNow(b.Root);
        Assert.Matches(@"^Synced at \d\d:\d\d: 2 received$", b.Line);
        Assert.Equal("Received 2 changes from the storage", b.Toasts.Last(t => t.StartsWith("Received")));

        await b.Sync.SyncNow(b.Root);                                                    // nothing new: the line says so, and when it looked
        Assert.Matches(@"^Nothing new, checked at \d\d:\d\d$", b.Line);
    }

    private int ItemId(Computer c) => c.Do(() => WorkItemsApi.GetAll().First(w => w.Path.EndsWith("add-health-check", StringComparison.OrdinalIgnoreCase)).Id);

    [Fact(DisplayName = "An attachment changed on both computers is announced once, and logged on its work item when its path names one")]
    public async Task AnAttachmentConflictIsAnnouncedOnce()
    {
        (Computer a, Computer b) = await TwoConnected();
        int id = ItemId(a);
        string onItem = $"wi{id}/n1/pic.png", loose = "scratch/pic.png";
        a.WriteAttachment(onItem, Png);
        a.WriteAttachment(loose, Png);
        await a.Sync.SyncNow(a.Root);
        await b.Sync.SyncNow(b.Root);
        int notes = a.Do(() => NotesApi.GetForWorkItem(id).Count);
        int events = a.Do(() => Events(id, "sync-conflict").Count());

        // both change both files; B's copies are the newer ones and go up first
        foreach (string path in new[] { onItem, loose })
        {
            a.WriteAttachment(path, [.. Png, 0xA1]);
            File.SetLastWriteTimeUtc(Path.Combine(a.AttachDir, path), DateTime.UtcNow.AddMinutes(-20));
            b.WriteAttachment(path, [.. Png, 0xB2, 0xB3]);
            File.SetLastWriteTimeUtc(Path.Combine(b.AttachDir, path), DateTime.UtcNow.AddMinutes(-5));
        }
        await b.Sync.SyncNow(b.Root);
        await a.Sync.SyncNow(a.Root);

        Assert.Equal([.. Png, 0xB2, 0xB3], a.ReadAttachment(onItem));                  // the newer copy was kept
        string[] told = [.. a.Toasts.Where(t => t.Contains("changed on both computers"))];
        Assert.Single(told);
        Assert.Contains(onItem, told[0]);
        Assert.Contains("trash", told[0]);
        Assert.Equal(notes, a.Do(() => NotesApi.GetForWorkItem(id).Count));                 // no note, on any item
        WorkItemEvent logged = Assert.Single(a.Do(() => Events(id, "sync-conflict").Skip(events).ToList()));
        Assert.Contains("attachment n1/pic.png: the newer copy was kept, the other is in the trash", logged.Text);
        Assert.DoesNotContain(a.Do(() => WorkItemsApi.GetAll().SelectMany(w => WorkItemsApi.GetEvents(w.Id)).ToList()), e => e.Text.Contains("scratch"));   // the loose file has no item to log on

        await a.Sync.SyncNow(a.Root);                                                    // nothing more to say at the next look
        Assert.Single(a.Toasts, t => t.Contains("changed on both computers"));
    }

    private static SyncReport Report(SyncOutcome outcome, string? message = null, SyncBlock blocked = SyncBlock.None, IReadOnlyList<string>? tooLarge = null) =>
        new(outcome, message, [], [], [], [], [], [], [], blocked: blocked, tooLarge: tooLarge);

    [Fact(DisplayName = "A failing or blocked attachments sync shows in the state line, and Allow and sync lets it through once")]
    public async Task AFailingOrBlockedAttachmentsSyncShowsAndCanBeAllowed()
    {
        (Computer a, _) = await TwoConnected();
        a.WriteAttachment(Pic, Png);                                                     // a folder to sync
        a.Engine = o => o.WholeFilesOnly ? Task.FromResult(Report(SyncOutcome.Failed, "the attachments storage said no")) : BoardSyncApi.Sync(o);
        await a.Sync.SyncNow(a.Root);
        await a.Sync.SyncNow(a.Root);
        await a.Sync.SyncNow(a.Root);

        Assert.Equal("Attachments: the attachments storage said no", a.Line);            // the board's own sync was fine: the attachments' trouble is the state
        Assert.False(a.Sync.Status(a.Root).CanAllow);
        Assert.Single(a.Toasts, t => t.Contains("Attachments sync failed"));            // once, not at every attempt

        List<SyncOptions> seen = [];
        a.Engine = o =>
        {
            lock (seen) seen.Add(o);
            return o.WholeFilesOnly && !o.AllowMassDelete
                ? Task.FromResult(Report(SyncOutcome.Blocked, "The sync would delete 12 files.", SyncBlock.MassDelete))
                : o.WholeFilesOnly ? Task.FromResult(Report(SyncOutcome.NothingToDo)) : BoardSyncApi.Sync(o);
        };
        await a.Sync.SyncNow(a.Root);
        Assert.Equal("Attachments: The sync would delete 12 files.", a.Line);
        Assert.True(a.Sync.Status(a.Root).CanAllow);                                     // the deletion brake can be allowed, for the attachments too
        Assert.Contains(a.Toasts, t => t.Contains("Attachments sync paused") && t.Contains("12 files"));

        await using BunitContext ctx = PageContext(a);
        IRenderedComponent<SettingsPage> page = ctx.Render<SettingsPage>();
        await a.Pass();                                                                // opening the page queued a pass of its own: let it finish, so the allowance goes to the next pass
        Assert.Contains("bad", Section(page).QuerySelector(".bs-state")!.ClassName);
        page.WaitForAssertion(() => Assert.NotNull(Section(page).QuerySelector(".bs-allow")));   // the page offers it; its button asks for exactly this sync
        await a.Sync.SyncNow(a.Root, allowMassDelete: true);
        Assert.DoesNotContain("Attachments:", a.Line);

        Assert.False(a.Sync.Status(a.Root).CanAllow);
        lock (seen)
        {
            Assert.Contains(seen, o => o.WholeFilesOnly && o.AllowMassDelete);           // let through...
            Assert.DoesNotContain(seen, o => !o.WholeFilesOnly && o.AllowMassDelete);    // ...without lifting the board's own brake
        }
        a.Engine = null;
        await a.Sync.SyncNow(a.Root);
        Assert.False(a.AttachmentRuns.Last().AllowMassDelete);                           // once: the next sync has the brake again
    }

    [Fact(DisplayName = "Files over the size limit are listed in the settings and announced once")]
    public async Task FilesOverTheLimitAreListedAndAnnouncedOnce()
    {
        (Computer a, _) = await TwoConnected();
        a.WriteAttachment(Pic, Png);
        await using BunitContext ctx = PageContext(a);
        IRenderedComponent<SettingsPage> page = ctx.Render<SettingsPage>();
        Assert.Null(Section(page).QuerySelector(".bs-toolarge"));

        a.Engine = o => Task.FromResult(o.WholeFilesOnly ? Report(SyncOutcome.NothingToDo, tooLarge: ["wi1/n1/film.mov"]) : Report(SyncOutcome.NothingToDo));
        await a.Sync.SyncNow(a.Root);
        await a.Sync.SyncNow(a.Root);
        await a.Sync.SyncNow(a.Root);
        page.Render();
        Assert.Equal("1 file is over 64 MB and is not synced: wi1/n1/film.mov", Section(page).QuerySelector(".bs-toolarge")!.TextContent.Trim());
        Assert.Single(a.Toasts, t => t.Contains("over 64 MB"));                          // the first time this set was seen

        a.Engine = o => Task.FromResult(Report(SyncOutcome.NothingToDo, tooLarge: o.WholeFilesOnly ? ["wi1/n1/film.mov", "wi2/big.zip"] : ["add-health-check/dump.bin"]));
        await a.Sync.SyncNow(a.Root);                                                    // either sync can have some: one line, one more toast for the new set
        page.Render();
        Assert.StartsWith("3 files are over 64 MB and are not synced: add-health-check/dump.bin", Section(page).QuerySelector(".bs-toolarge")!.TextContent.Trim());
        Assert.Equal(2, a.Toasts.Count(t => t.Contains("over 64 MB")));

        a.Engine = o => Task.FromResult(Report(SyncOutcome.NothingToDo));                // fixed: the line goes, and a return of the problem is news again
        await a.Sync.SyncNow(a.Root);
        page.Render();
        Assert.Null(Section(page).QuerySelector(".bs-toolarge"));
        a.Engine = o => Task.FromResult(o.WholeFilesOnly ? Report(SyncOutcome.NothingToDo, tooLarge: ["wi1/n1/film.mov"]) : Report(SyncOutcome.NothingToDo));
        await a.Sync.SyncNow(a.Root);
        Assert.Equal(3, a.Toasts.Count(t => t.Contains("over 64 MB")));
    }

    [Fact(DisplayName = "One trash link reaches what both syncs kept")]
    public async Task OneTrashLinkReachesBothTrashes()
    {
        (Computer a, Computer b) = await TwoConnected();
        string? opened = null;
        await using BunitContext ctx = PageContext(b, opener: p => { opened = p; return (true, ""); });
        IRenderedComponent<SettingsPage> page = ctx.Render<SettingsPage>();
        foreach (string name in new[] { "one", "two", "three" }) a.WriteAttachment($"wi1/n1/{name}.png", Png);
        await a.Sync.SyncNow(a.Root);
        await b.Sync.SyncNow(b.Root);
        Assert.Null(b.Sync.Status(b.Root).TrashFolder);                                  // nothing removed yet: no link

        File.Delete(Path.Combine(a.AttachDir, "wi1/n1/one.png"));                        // only an attachment is removed
        await a.Sync.SyncNow(a.Root);
        await b.Sync.SyncNow(b.Root);
        string attachmentsTrash = Path.Combine(b.Settings.ProjectFolder(b.Root), "state-attachments", "trash");
        Assert.Equal(attachmentsTrash, b.Sync.Status(b.Root).TrashFolder);
        Assert.Equal(["1 file the sync removed is kept in the trash for 30 days"], b.Toasts.Where(t => t.Contains("kept in the trash")));

        File.Delete(Path.Combine(a.Root, Item, "notes", "n123.md"));                     // and now a board file too: both folders hold something
        File.Delete(Path.Combine(a.AttachDir, "wi1/n1/two.png"));
        await a.Sync.SyncNow(a.Root);
        await b.Sync.SyncNow(b.Root);

        string both = b.Settings.ProjectFolder(b.Root);
        Assert.Equal(both, b.Sync.Status(b.Root).TrashFolder);                           // one link, to the folder that holds both
        Assert.True(Directory.Exists(Path.Combine(both, "state", "trash")));
        Assert.True(Directory.Exists(Path.Combine(both, "state-attachments", "trash")));
        Assert.Equal("2 files the sync removed are kept in the trash for 30 days", b.Toasts.Last(t => t.Contains("kept in the trash")));   // one toast for the cycle
        page.Render();
        Assert.Single(Section(page).QuerySelectorAll(".bs-open-trash"));
        await page.InvokeAsync(() => Section(page).QuerySelector(".bs-open-trash")!.Click());
        page.WaitForAssertion(() => Assert.Equal(both, opened));
    }

    [Fact(DisplayName = "After the storage address changes, the board and the attachments both start as a first connection")]
    public async Task AnAddressChangeStartsOverForBoth()
    {
        string other = Path.Combine(temp, "storage-two");
        (Computer a, Computer b) = await TwoConnected();
        a.WriteAttachment(Pic, Png);
        a.Repo.Write(Item + "/extra.md", "only on a\n");
        await a.Sync.SyncNow(a.Root);                                                    // a's state remembers both as synced with the first storage
        Assert.True(File.Exists(Path.Combine(a.Settings.StateFolder(a.Root), "state.json")));
        Assert.True(File.Exists(Path.Combine(a.Settings.AttachmentsStateFolder(a.Root), "state.json")));

        Computer c = NewComputer("c");                                                   // a third computer fills the other storage with something else
        c.ConnectFolder(other);
        c.WriteAttachment("wi1/n1/from-c.png", [.. Png, 0xC1]);
        c.Repo.Write(Item + "/from-c.md", "only on c\n");
        await c.Sync.SyncNow(c.Root);
        Assert.True(File.Exists(Path.Combine(other, "attachments", "manifest.json")));

        Assert.True(a.Settings.SetValue(a.Root, "path", other).Success);                 // the address changes in this app's settings
        await a.Pass();
        await a.Sync.SyncNow(a.Root);

        // A first connection: what is here and not there goes up, what is there and not here comes down. Nothing here is taken for "deleted in the storage".
        Assert.Equal(Png, a.ReadAttachment(Pic));
        Assert.True(File.Exists(Path.Combine(a.Root, Item, "extra.md")));
        Assert.Equal([.. Png, 0xC1], a.ReadAttachment("wi1/n1/from-c.png"));
        Assert.True(File.Exists(Path.Combine(a.Root, Item, "from-c.md")));
        await c.Sync.SyncNow(c.Root);
        Assert.Equal(Png, c.ReadAttachment(Pic));                                        // and it reached the new storage
        Assert.True(File.Exists(Path.Combine(c.Root, Item, "extra.md")));
        Assert.False(Directory.Exists(Path.Combine(a.Settings.StateFolder(a.Root), "trash")));                // nothing was removed on the way
        Assert.False(Directory.Exists(Path.Combine(a.Settings.AttachmentsStateFolder(a.Root), "trash")));

        await b.Sync.SyncNow(b.Root);                                                    // the first storage is as it was left, untouched by the change
        Assert.False(File.Exists(Path.Combine(b.Root, Item, "from-c.md")));
    }

    private const string OldDir = "local/task-tracking", NewDir = "media/att";

    /// <summary>What the storage holds under attachments/ and when each file was last written: equal before and after means nothing was uploaded.</summary>
    private string[] StoredSnapshot() =>
        [.. Directory.EnumerateFiles(Path.Combine(Storage, "attachments"), "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal)
            .Select(f => $"{Path.GetRelativePath(Storage, f)} {new System.IO.FileInfo(f).Length} {File.GetLastWriteTimeUtc(f).Ticks}")];

    private async Task<(Computer A, Computer B)> TwoWithPictures()
    {
        (Computer a, Computer b) = await TwoConnected();
        a.WriteAttachment("wi1/n1/pic.png", Png);
        a.WriteAttachment("wi1/n1/other.png", [.. Png, 0x07]);
        await a.Sync.SyncNow(a.Root);
        await b.Sync.SyncNow(b.Root);
        Assert.Equal(Png, b.ReadAttachment("wi1/n1/pic.png"));
        return (a, b);
    }

    [Fact(DisplayName = "The attachments folder moved on another computer is followed here, and nothing is uploaded again")]
    public async Task ADifferentFolderFromAPullIsFollowed()
    {
        (Computer a, Computer b) = await TwoWithPictures();
        string[] before = StoredSnapshot();
        string oldFolder = b.AttachDir;

        b.App.SetAttachmentsDir(b.Root, NewDir);                                          // what a git pull did to config.yaml: this app's settings page was not involved
        b.Sync.BoardChanged();
        await b.Sync.QuietElapsed(b.Root);
        Assert.Contains(b.Toasts, t => t.StartsWith("Attachments moved to " + NewDir));

        Assert.Contains("to follow the project's settings", b.Toasts.Single(t => t.StartsWith("Attachments moved to")));
        Assert.Equal(Png, b.ReadAttachment("wi1/n1/pic.png"));                           // AttachDir is now the new folder
        Assert.EndsWith(Path.Combine("media", "att"), b.AttachDir);
        Assert.False(File.Exists(Path.Combine(oldFolder, "wi1/n1/pic.png")));
        Assert.Equal(b.AttachDir, b.AttachmentRuns.Last().LocalRoot);                    // the attachments sync looks at the new folder

        await b.Sync.SyncNow(b.Root);
        Assert.Equal(before, StoredSnapshot());                                          // keyed by the path inside the folder: nothing went up again
        Assert.False(Directory.Exists(Path.Combine(b.Settings.AttachmentsStateFolder(b.Root), "trash")));   // and nothing was taken for deleted
        await b.Sync.SyncNow(b.Root);
        Assert.Single(b.Toasts, t => t.StartsWith("Attachments moved to"));              // once

        // the other computer follows the same way and the two still agree
        a.App.SetAttachmentsDir(a.Root, NewDir);
        a.Sync.BoardChanged();
        await a.Sync.QuietElapsed(a.Root);
        Assert.Contains(a.Toasts, t => t.StartsWith("Attachments moved to"));
        await a.Sync.SyncNow(a.Root);
        Assert.Equal(before, StoredSnapshot());
    }

    [Fact(DisplayName = "A folder moved while the app was closed is followed when the project opens")]
    public async Task AFolderMovedWhileClosedIsFollowedOnOpen()
    {
        (_, Computer b) = await TwoWithPictures();
        string[] before = StoredSnapshot();
        b.Sync.Dispose();                                                                // the app is closed...
        b.App.SetAttachmentsDir(b.Root, NewDir);                                         // ...and a pull changes the folder
        b.Restart();
        await b.Pass();
        Assert.Contains(b.Toasts, t => t.StartsWith("Attachments moved to " + NewDir));

        Assert.Equal(Png, b.ReadAttachment("wi1/n1/pic.png"));
        await b.Sync.SyncNow(b.Root);
        Assert.Equal(before, StoredSnapshot());
    }

    [Fact(DisplayName = "A folder change made in this app's settings is not moved a second time by the sync service")]
    public async Task AChangeMadeInThisAppIsNotMovedAgain()
    {
        (_, Computer b) = await TwoWithPictures();
        string[] before = StoredSnapshot();

        // what the settings page does for "Move": it moves the files itself, tells the service, then saves the folder
        b.Do(() => Assert.True(NotesApi.MoveAttachments(b.Root, OldDir, NewDir).Success));
        b.Sync.AttachmentsFolderChangingHere(b.Root, NewDir, moved: true);
        Assert.True(b.App.SetAttachmentsDir(b.Root, NewDir).Success);
        b.Sync.BoardChanged();
        await b.Sync.QuietElapsed(b.Root);
        await b.Sync.SyncNow(b.Root);

        Assert.DoesNotContain(b.Toasts, t => t.StartsWith("Attachments moved to"));      // the page said it already
        Assert.Equal(Png, b.ReadAttachment("wi1/n1/pic.png"));
        Assert.Equal(2, Directory.EnumerateFiles(b.AttachDir, "*", SearchOption.AllDirectories).Count());   // no second copy, no "-2"
        Assert.Equal(before, StoredSnapshot());                                          // and the sync's memory followed the move
    }

    [Fact(DisplayName = "Keeping the old files where they are in the settings is not undone by the sync service, and loses nothing in the storage")]
    public async Task KeepingTheOldFilesIsRespected()
    {
        (_, Computer b) = await TwoWithPictures();
        string[] before = StoredSnapshot();
        string oldFolder = b.AttachDir;
        await using BunitContext ctx = PageContext(b);
        IRenderedComponent<SettingsPage> page = ctx.Render<SettingsPage>();

        await page.InvokeAsync(() => page.Find("input[aria-label='Attachments folder']").Change(NewDir));
        page.WaitForAssertion(() => Assert.NotNull(page.Find(".att-ask .att-keep")));
        await page.InvokeAsync(() => page.Find(".att-ask .att-keep").Click());
        page.WaitForAssertion(() => Assert.Equal(NewDir, b.App.AttachmentsDir(b.Root)));
        b.Sync.BoardChanged();
        await b.Sync.QuietElapsed(b.Root);
        await b.Sync.SyncNow(b.Root);

        Assert.DoesNotContain(b.Toasts, t => t.StartsWith("Attachments moved to"));
        Assert.True(File.Exists(Path.Combine(oldFolder, "wi1/n1/pic.png")));            // the user chose to keep them: they are where they were
        Assert.True(File.Exists(Path.Combine(oldFolder, "wi1/n1/other.png")));
        Assert.Equal(before.Length, StoredSnapshot().Length);                            // nothing in the storage was deleted because the watched folder changed
        Assert.DoesNotContain(b.AttachmentRuns, o => o.LocalRoot == b.AttachDir && o.AllowMassDelete);
        Assert.Equal(Png, b.ReadAttachment("wi1/n1/pic.png"));                           // the new folder is a first connection: what the storage holds comes down
    }
}
