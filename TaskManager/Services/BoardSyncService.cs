using System.Collections.Concurrent;
using System.Text.Json;
using BoardSync;
using BoardSync.PublicDTOs;
using GitHistory;
using GitHistory.PublicDTOs;
using Notes;
using Notes.PublicDTOs;
using OpenSpecStore;
using WorkItems;
using WorkItems.PublicDTOs;

[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("TaskManager.Tests")]

namespace TaskManager.Services;

/// <summary>
/// How the service is timed. <paramref name="Quiet"/>: how long the board must stay unchanged before a sync starts (every change restarts the wait);
/// <paramref name="Run"/> replaces the engine call (tests watch or fake it); <paramref name="UtcNow"/> is the clock, for the engine and for the state line.
/// </summary>
public sealed record SyncSetup(TimeSpan Quiet, Func<SyncOptions, Task<SyncReport>>? Run = null, Func<DateTime>? UtcNow = null)
{
    public static SyncSetup Default => new(TimeSpan.FromMinutes(2));

    /// <summary>Test seam (the test assembly sets it once at load): no timer of any kind is ever created, whatever a setup says; the waits only record when they are due.</summary>
    internal static bool NoRealTimers { get; set; }

    /// <summary>Test seam: what <see cref="SettleWindow"/> is unless a setup says otherwise (the test assembly writes its files and syncs at once, so it turns the window off).</summary>
    internal static TimeSpan DefaultSettleWindow { get; set; } = SyncOptions.DefaultSettleWindow;

    /// <summary>How recently a file must have been written for a pass to leave it alone (see <see cref="SyncOptions.SettleWindow"/>); a pass that left files settling is followed by another after this wait.</summary>
    public TimeSpan SettleWindow { get; init; } = DefaultSettleWindow;

    /// <summary>The wait may be restarted by every change, but never ends later than this after the first change that no sync has taken yet: an agent that writes every minute still syncs.</summary>
    public TimeSpan MaxWait { get; init; } = TimeSpan.FromMinutes(15);

    /// <summary>How often an idle computer asks the storage whether its manifest moved (one conditional request) while the window has focus; a full pass runs only when it did. Infinite or zero: no check.</summary>
    public TimeSpan Idle { get; init; } = TimeSpan.FromSeconds(60);

    /// <summary>The same while the window does not have focus.</summary>
    public TimeSpan IdleUnfocused { get; init; } = TimeSpan.FromMinutes(5);

    /// <summary>Whether the window has focus now (the host wires its window; a service without one counts as focused).</summary>
    public Func<bool> Focused { get; init; } = () => true;

    /// <summary>A number from -1 to 1 that moves each idle interval by up to a quarter either way, so computers that started together do not ask together.</summary>
    public Func<double> Jitter { get; init; } = () => Random.Shared.NextDouble() * 2 - 1;

    /// <summary>False: no timer runs (the quiet wait and the idle check only record when they are due; a test calls QuietElapsed and IdleElapsed itself).</summary>
    public bool RealTimers { get; init; } = true;
}

/// <summary>What the Settings page shows for a project: one plain line, and whether a paused sync can be allowed.</summary>
/// <param name="TrashFolder">The folder the sync keeps what it removed in (30 days), when it exists; null otherwise. The attachments' trash is beside the board's: when both exist this is the sync folder that holds both.</param>
/// <param name="TooLarge">The line about files over the size limit (either sync), while there are any.</param>
/// <param name="Proposal">What the repository's settings ask for when this computer has not allowed it: the project stays inert until it does.</param>
public sealed record SyncStatus(bool Connected, string Line, bool CanAllow, bool Running, int Unsent, string? TrashFolder = null, StorageProposal? Proposal = null, string? SkippedLinks = null, string? TooLarge = null);

/// <summary>
/// Syncs each connected project's board (openspec/changes) with its storage in the background, one sync at a time per project, never on the UI
/// thread: when the project opens, once the board has been quiet for two minutes after a change (never later than fifteen minutes after the first change), when the app closes and on request.
/// Nothing syncs on a timer by itself: an idle computer only asks the storage, with one conditional request a minute (five when the window has no focus), whether the manifest moved, and syncs when it did.
/// The sync's own writes are not changes: they do not start a wait. A project set to "this folder only" has nothing here: no timer, no task, no file. What a sync brings in is told in a toast; what it had to drop in a conflict is kept on the work item.
/// </summary>
public sealed class BoardSyncService : IDisposable
{
    private sealed class Link(string root)
    {
        public readonly string Root = root;
        public Timer? Quiet;                // the one wait: restarted by every change, a sync when it elapses
        public DateTime? QuietDue;          // when it ends, by the service's clock; null = no wait pending
        public DateTime? FirstChangeAt;     // the first change no pass has taken yet: the wait never ends later than MaxWait after it
        public Timer? Idle;                 // the idle check: a conditional request for the manifest, a pass only when it moved
        public DateTime? IdleDue;
        public Dictionary<string, (long Length, long Ticks)>? Echo;   // the board's files (size, time) as the last pass that wrote files left them: a change report that finds them as they are was caused by that pass
        public bool Running, Allow, Dropped;
        public TaskCompletionSource<SyncReport?>? Again;   // a trigger arrived while a pass ran: the one more pass that follows, and who waits for it
        public Task Pass = Task.CompletedTask;             // the last pass started (it also covers the "again" pass that follows it)
        public readonly HashSet<string> Edited = new(StringComparer.Ordinal);   // board-relative paths the app wrote since the last sync that succeeded
        public long Cursor;
        public SyncReport? Last;
        public SyncReport? LastAtt;         // the last attachments sync (null: none yet, off, or no folder to sync)
        public bool AttFailedInRow;
        public string? AttBlockedToasted;
        public string? TooLargeToasted;     // the set of too-large files a toast was shown for
        public DateTime LastAt;
        public string? Problem;             // the storage could not be opened (a bad address)
        public bool FailedInRow;
        public string? BlockedToasted;
        public string? SkippedToasted;          // the set of skipped links a toast was shown for: one toast per set, not one per sync
        public string EditedFile = "";          // where Edited is kept between runs of the app
        public string? SeenDir;                 // the attachments folder (repository-relative) this computer last had in effect for the project; kept in attachments-dir.txt
        public readonly object SaveLock = new();
        public string? LastPause;               // what the status last showed as the git pause: shown again when the sync state cannot be read at that moment
    }

    private readonly BoardStorageSettings settings;
    private readonly UiService ui;
    private readonly SyncSetup setup;
    private readonly object gate = new();
    private readonly Dictionary<string, string> known = [];                  // key -> root, every open project
    private readonly Dictionary<string, Link> links = [];                    // key -> link, only the connected ones
    private readonly ConcurrentDictionary<string, int> runs = new();
    private readonly Dictionary<string, string> pendingToasted = [];          // key -> address hash a toast was shown for: one toast per proposal
    private long seenVersion;

    private readonly SettingsStore? app;   // where the project's attachments folder is read from; none: the Notes module's own value is used and nothing is followed

    public BoardSyncService(BoardStorageSettings settings, UiService ui, SyncSetup? setup = null, SettingsStore? app = null)
    {
        this.app = app;
        this.settings = settings;
        this.ui = ui;
        this.setup = setup ?? SyncSetup.Default;
        settings.Changed += ConfigureAll;
    }

    /// <summary>Runs work inside a project (the workspace switches the module state to it). The default runs it as it is.</summary>
    public Action<string, Action> InProject { get; set; } = (_, work) => work();

    /// <summary>
    /// The app attached or removed a file: it counts as a change, the wait restarts. The attachments folder is not watched (there is no change feed
    /// for it); files put there from outside are picked up by the next sync.
    /// </summary>
    public void AttachmentsChanged()
    {
        List<Link> all;
        lock (gate) all = [.. links.Values];
        foreach (Link l in all) Schedule(l);
    }

    /// <summary>Raised when a project's state line may have changed.</summary>
    public event Action? Changed;

    /// <summary>Raised after the service wrote events or notes on work items (the pages read again).</summary>
    public event Action? Touched;

    // ── which projects ──

    /// <summary>A project is open: if it is connected (and complete) it syncs once now. Returns that pass (completed when the project is not connected).</summary>
    public Task Attach(string root)
    {
        lock (gate) known[SettingsStore.Key(root)] = root;
        Task pass = Configure(root);
        ToastProposal(root, "This project asks to keep its board in step through a storage. Review it in Settings, Board storage: nothing syncs until you allow it on this computer.");
        return pass;
    }

    /// <summary>One toast per proposed address: the repository names a storage this computer has not allowed.</summary>
    private void ToastProposal(string root, string text)
    {
        if (settings.Proposal(root) is null || settings.AddressHash(root) is not { } hash) return;
        lock (gate)
        {
            string key = SettingsStore.Key(root);
            if (pendingToasted.TryGetValue(key, out string? shown) && shown == hash) return;
            pendingToasted[key] = hash;
        }
        ui.Toast(text, ToastKind.Warning);
    }

    /// <summary>The projects that stay open are the ones listed; the others stop syncing.</summary>
    public void Prune(IEnumerable<string> open)
    {
        HashSet<string> keep = [.. open.Select(SettingsStore.Key)];
        List<Link> drop = [];
        lock (gate)
        {
            foreach (string key in known.Keys.Where(k => !keep.Contains(k)).ToList()) known.Remove(key);
            foreach (string key in links.Keys.Where(k => !known.ContainsKey(k)).ToList()) { drop.Add(links[key]); links.Remove(key); }
        }
        foreach (Link l in drop) Stop(l);
    }

    private void ConfigureAll()
    {
        string[] roots;
        lock (gate) roots = [.. known.Values];
        foreach (string root in roots) Configure(root);
    }

    private Task Configure(string root)
    {
        string key = SettingsStore.Key(root);
        bool connected = settings.Config(root) is not null && settings.Problem(root) is null;
        bool asked = !connected && settings.Proposal(root) is not null;   // waiting for this computer's approval: not a disconnect, nothing the app wrote is forgotten
        Link? link;
        bool created = false, disconnected = false;
        lock (gate)
        {
            links.TryGetValue(key, out link);
            if (!connected)
            {
                links.Remove(key);
                disconnected = true;
            }
            else if (link is null)
            {
                links[key] = link = new Link(root) { Cursor = OpenSpecStoreApi.ChangesSince(long.MaxValue).Cursor, EditedFile = Path.Combine(settings.ProjectFolder(root), EditedFileName) };
                LoadEdited(link);
                link.SeenDir = ReadMemory(root, SeenDirFile);
                created = true;
            }
        }
        if (!connected)
        {
            if (link is not null) { Stop(link); if (disconnected && !asked) ForgetEdited(link); }
            Changed?.Invoke();
            return Task.CompletedTask;
        }
        if (created)
        {
            try { if (GitAttributes.Ensure(root) is string notice) ui.Toast(notice, ToastKind.Info); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { ui.Toast($"Could not write .gitattributes: {e.Message}", ToastKind.Warning); }
        }
        link!.Problem = null;
        Task pass = Request(link);   // a full pass: the storage may hold what this computer missed, and what changed here may not be there yet
        Changed?.Invoke();
        return pass;
    }

    private static void Stop(Link l)
    {
        lock (l)
        {
            l.Dropped = true;
            l.Quiet?.Dispose();
            l.Idle?.Dispose();
            l.QuietDue = null;
            l.FirstChangeAt = null;
            l.IdleDue = null;
            l.Echo = null;
            l.Again?.TrySetResult(null);
            l.Again = null;
        }
        SaveEdited(l);   // what the app wrote and no sync has taken yet is there when the project is opened again
    }

    public bool IsActive(string root) { lock (gate) return links.ContainsKey(SettingsStore.Key(root)); }

    /// <summary>How many times the engine ran for this project (also after it stopped syncing).</summary>
    public int Runs(string root) => runs.GetValueOrDefault(SettingsStore.Key(root));

    private Link? LinkOf(string root) { lock (gate) return links.GetValueOrDefault(SettingsStore.Key(root)); }

    // ── triggers: a project opens, the board has been quiet for two minutes after a change, the app closes, or the user asks ──

    /// <summary>The service's clock as the state line shows it (local time).</summary>
    private DateTime Now() => (setup.UtcNow?.Invoke() ?? DateTime.UtcNow).ToLocalTime();

    private DateTime UtcNow() => setup.UtcNow?.Invoke() ?? DateTime.UtcNow;

    private bool RealTimers => setup.RealTimers && !SyncSetup.NoRealTimers;

    /// <summary>How many timer objects exist now (the quiet wait's and the idle check's, over every project); none while <see cref="SyncSetup.RealTimers"/> is off or the test seam is on.</summary>
    internal int LiveTimers
    {
        get
        {
            List<Link> all;
            lock (gate) all = [.. links.Values];
            int n = 0;
            foreach (Link l in all) lock (l) n += (l.Quiet is null ? 0 : 1) + (l.Idle is null ? 0 : 1);
            return n;
        }
    }

    /// <summary>The board's content changed (the app's own save, the board server's write or an outside edit): the two-minute wait starts again.</summary>
    public void BoardChanged()
    {
        if (app is not null)   // config.yaml can change from outside (a pull): the folder is looked at on every change, and a different one gets a pass that follows it
        {
            List<Link> open;
            lock (gate) open = [.. links.Values];
            foreach (Link l in open) if (l.SeenDir is { } seen && seen != app.AttachmentsDir(l.Root)) Schedule(l);
        }
        long version = OpenSpecStoreApi.TotalVersion;
        if (Interlocked.Exchange(ref seenVersion, version) == version) return;   // only a page redrawing: no content moved
        List<Link> all;
        lock (gate) all = [.. links.Values];
        foreach (Link l in all)
        {
            bool wroteByApp = Absorb(l);
            if (!wroteByApp && IsEchoOfTheSync(l)) continue;   // the watcher saw the sync's own writes: nothing here is unsynced, so no second pass (the pages still redraw, the workspace does that itself)
            Schedule(l);
        }
    }

    /// <summary>
    /// (Re)starts the quiet wait: one timer per project, one sync when it elapses. The wait restarts with every change but never ends later than
    /// <see cref="SyncSetup.MaxWait"/> after the first change that no pass has taken yet.
    /// </summary>
    private void Schedule(Link l, TimeSpan? wait = null)
    {
        lock (l)
        {
            if (l.Dropped) return;
            DateTime now = UtcNow();
            l.FirstChangeAt ??= now;
            DateTime due = now + (wait ?? setup.Quiet), cap = l.FirstChangeAt.Value + setup.MaxWait;
            if (wait is not null && l.QuietDue is { } pending && pending < due) due = pending;   // a shorter wait never pushes a pending one back
            if (due > cap) due = cap;
            l.QuietDue = due;
            if (!RealTimers) return;
            l.Quiet ??= new Timer(_ => QuietElapsed(l.Root));
            l.Quiet.Change(due > now ? due - now : TimeSpan.Zero, Timeout.InfiniteTimeSpan);
        }
    }

    // ── the sync's own writes are not changes to sync ──

    /// <summary>
    /// After a pass that wrote files here (downloads, merges, deletions, replaced copies) the board's files are noted as that pass left them. The watcher reports those writes a moment later
    /// (the store's version moves after its debounce, so it cannot be read at the end of the pass); a report that finds every file as noted, with nothing the app wrote itself, has no news.
    /// Nothing is noted while the app has unsent edits: those need their pass anyway.
    /// </summary>
    private void NoteSyncWrites(Link l, SyncReport report)
    {
        bool wrote = report.Downloaded.Count + report.Merged.Count + report.DeletedHere.Count + report.Replaced.Count > 0;
        lock (l)
        {
            if (l.Dropped) return;
            if (l.Edited.Count > 0) { l.Echo = null; return; }
            if (wrote) l.Echo = StampOf(Path.Combine(l.Root, "openspec", "changes"));
        }
    }

    private static bool IsEchoOfTheSync(Link l)
    {
        lock (l)
        {
            if (l.Echo is not { } noted) return false;
            Dictionary<string, (long Length, long Ticks)>? now = StampOf(Path.Combine(l.Root, "openspec", "changes"));
            if (now is not null && now.Count == noted.Count && now.All(f => noted.TryGetValue(f.Key, out var was) && was == f.Value)) return true;
            l.Echo = null;   // something else changed: the next pass that writes notes the files again
            return false;
        }
    }

    /// <summary>Every file of the board with its size and write time: a stat walk, no bytes read; null when it could not be done.</summary>
    private static Dictionary<string, (long Length, long Ticks)>? StampOf(string local)
    {
        try
        {
            Dictionary<string, (long Length, long Ticks)> stamp = new(StringComparer.Ordinal);
            if (Directory.Exists(local))
                foreach (FileInfo f in new DirectoryInfo(local).EnumerateFiles("*", SearchOption.AllDirectories)) stamp[f.FullName] = (f.Length, f.LastWriteTimeUtc.Ticks);
            return stamp;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return null; }
    }

    // ── the idle check ──

    /// <summary>(Re)starts the idle wait: every minute while the window has focus, every five when not, moved by up to a quarter either way.</summary>
    private void ScheduleIdle(Link l)
    {
        lock (l)
        {
            if (l.Dropped) return;
            TimeSpan every = setup.Focused() ? setup.Idle : setup.IdleUnfocused;
            if (every <= TimeSpan.Zero || every == Timeout.InfiniteTimeSpan) { l.IdleDue = null; return; }
            TimeSpan wait = TimeSpan.FromTicks((long)(every.Ticks * (1 + 0.25 * Math.Clamp(setup.Jitter(), -1, 1))));
            l.IdleDue = UtcNow() + wait;
            if (!RealTimers) return;
            l.Idle ??= new Timer(_ => _ = IdleElapsed(l.Root));
            l.Idle.Change(wait, Timeout.InfiniteTimeSpan);
        }
    }

    /// <summary>When the next idle check is due, by the service's clock; null when there is none.</summary>
    public DateTime? IdleDue(string root) => LinkOf(root) is { } l ? Locked(l, () => l.IdleDue) : null;

    /// <summary>
    /// The idle wait ended (the timer calls this): one conditional request for the manifest with the version saved by the last pass; a full pass only when the storage answers that it changed.
    /// Nothing is asked while a pass runs or a wait for one is pending (that pass looks anyway). Never throws and shows nothing: a storage that cannot be reached is tried again next time.
    /// </summary>
    public async Task IdleElapsed(string root)
    {
        if (LinkOf(root) is not { } l) return;
        try
        {
            bool busy;
            lock (l) busy = l.Dropped || l.Running || l.QuietDue is not null;
            if (!busy && await RemoteChanged(l)) await Request(l);
        }
        catch (Exception) { }
        finally { ScheduleIdle(l); }
    }

    private async Task<bool> RemoteChanged(Link l)
    {
        (IBoardStorage? storage, _) = settings.Open(l.Root);
        if (storage is null) return false;
        try
        {
            string? version = SavedVersion(Path.Combine(settings.StateFolder(l.Root), "state.json"));
            StoredFile? file = await storage.GetIfChanged("manifest.json", version);
            return file is null ? version is not null : !file.IsNotModified;   // no manifest and none known: nothing to fetch; one that went away is news
        }
        finally { (storage as IDisposable)?.Dispose(); }
    }

    /// <summary>The storage version the last pass saved in state.json (its first property, so only the start of the file is read); null when there is none.</summary>
    private static string? SavedVersion(string stateFile)
    {
        try
        {
            using FileStream stream = new(stateFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);   // a pass may be saving it
            byte[] head = new byte[8192];
            int n = stream.Read(head, 0, head.Length);
            Utf8JsonReader reader = new(head.AsSpan(0, n), isFinalBlock: false, default);
            while (reader.Read())
            {
                if (reader.TokenType != JsonTokenType.PropertyName || reader.CurrentDepth != 1) continue;
                if (!reader.ValueTextEquals("storageVersion") && !reader.ValueTextEquals("StorageVersion")) return null;   // the version is written first: another property first means there is none
                return reader.Read() && reader.TokenType == JsonTokenType.String ? reader.GetString() : null;
            }
            return null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException) { return null; }
    }

    /// <summary>When the pending wait for this project ends, by the service's clock; null when nothing is pending.</summary>
    public DateTime? QuietDue(string root) => LinkOf(root) is { } l ? Locked(l, () => l.QuietDue) : null;

    private static T Locked<T>(Link l, Func<T> read) { lock (l) return read(); }

    /// <summary>The wait ended without another change (the timer calls this): one sync. Returns that pass; completed when the project is not connected.</summary>
    public Task QuietElapsed(string root) => LinkOf(root) is { } l ? Request(l) : Task.CompletedTask;

    /// <summary>The pass last started for this project (completed when none): what a test awaits after a page click.</summary>
    public Task LastPass(string root) => LinkOf(root) is { } l ? Locked(l, () => l.Pass) : Task.CompletedTask;

    /// <summary>
    /// One pass for the project, now, or one more right after the one running (a trigger that arrives while a pass runs makes exactly one more pass, however many arrive).
    /// The returned task completes with the report of the pass it caused or joined; null when the project is not connected.
    /// </summary>
    private Task<SyncReport?> Request(Link l, bool allow = false)
    {
        Task<SyncReport?> result;
        lock (l)
        {
            if (l.Dropped) return Task.FromResult<SyncReport?>(null);
            l.Allow |= allow;
            if (l.Running) return (l.Again ??= new(TaskCreationOptions.RunContinuationsAsynchronously)).Task;
            l.Running = true;
            result = Task.Run(() => Pass(l));
            l.Pass = result;
        }
        Changed?.Invoke();
        return result;
    }

    private async Task<SyncReport?> Pass(Link l)
    {
        SyncReport? first = await Once(l);
        while (true)
        {
            TaskCompletionSource<SyncReport?>? again;
            lock (l)
            {
                again = l.Again;
                l.Again = null;
                if (again is null) { l.Running = false; break; }
            }
            again.TrySetResult(await Once(l));
        }
        ScheduleIdle(l);
        Changed?.Invoke();
        return first;
    }

    private async Task<SyncReport?> Once(Link l)
    {
        bool allow;
        lock (l)
        {
            allow = l.Allow;
            l.Allow = false;
            l.QuietDue = null;                                      // this pass takes what the wait was for; a change from here on starts it again
            l.FirstChangeAt = null;
            l.Quiet?.Change(Timeout.Infinite, Timeout.Infinite);
        }
        try { return await RunOnce(l, allow); }
        catch (Exception e) { SyncReport report = new(SyncOutcome.Failed, settings.Scrub(l.Root, e.Message), [], [], [], [], [], [], []); Handle(l, report, []); return report; }   // a bug in here must never end the app: it shows as a failed sync
    }

    /// <summary>Syncs now (after the one running, if any) and returns what that sync did; null when the project is not connected.</summary>
    public Task<SyncReport?> SyncNow(string root, bool allowMassDelete = false) => LinkOf(root) is { } l ? Request(l, allowMassDelete) : Task.FromResult<SyncReport?>(null);

    private static readonly TimeSpan ExitLimit = TimeSpan.FromSeconds(15);

    /// <summary>
    /// The app is closing: every project whose wait is still pending (changes not synced yet) syncs once, awaited for at most 15 seconds (<paramref name="stop"/> ends it earlier);
    /// a failure or the limit is swallowed, so the app always closes.
    /// </summary>
    public async Task SyncOnExit(CancellationToken stop = default)
    {
        List<Link> all;
        lock (gate) all = [.. links.Values];
        List<Task> passes = [];
        foreach (Link l in all)
        {
            bool pending;
            lock (l) pending = l.QuietDue is not null;
            if (pending) passes.Add(Request(l));
            else lock (l) if (l.Running) passes.Add(l.Pass);
        }
        try { await Task.WhenAll(passes).WaitAsync(ExitLimit, stop); }
        catch (Exception e) when (e is TimeoutException or OperationCanceledException) { }
        catch (Exception) { }   // a failed pass is already shown as a failed sync; closing never waits on it
    }

    // ── one sync ──

    private async Task<SyncReport?> RunOnce(Link l, bool allow)
    {
        string root = l.Root;
        (IBoardStorage? storage, string? problem) = settings.Open(root);
        if (storage is null)
        {
            l.Problem = problem;
            ToastProposal(root, "The board storage address in config.yaml changed. Confirm it in Settings, Board storage: nothing is synced until you allow it on this computer.");
            Changed?.Invoke();
            return null;
        }
        l.Problem = null;
        FollowAttachmentsFolder(l);               // before anything syncs: a folder that moved on another computer is moved here first, so the board brings notes that already say so
        settings.KeepStateForThisAddress(root);   // the sync's memory belongs to one storage: another address starts over, for the board and the attachments
        string? git = GitFolder(root);
        RepairMarkers(l, git);                    // before the pause for markers is looked at: a file git left half merged is rebuilt here, the pause is for what cannot be
        HashSet<string> sent = GatherEdited(l);
        bool allowAttachments = allow, allowBoard = allow;
        lock (l) if (allow && l.LastAtt is { Outcome: SyncOutcome.Blocked } && l.Last is not { Outcome: SyncOutcome.Blocked }) allowBoard = false;   // "Allow and sync" was for the attachments' pause: the board's brake is not lifted with it
        SyncOptions options = new(Path.Combine(root, "openspec", "changes"), settings.StateFolder(root), storage, settings.DeviceId(), setup.UtcNow, allowBoard, sent, git, SettleWindow: setup.SettleWindow);
        Changed?.Invoke();
        string key = SettingsStore.Key(root);
        SyncReport report;
        try
        {
            report = await Engine(options);
            runs.AddOrUpdate(key, 1, (_, n) => n + 1);
            if (report.Message is { Length: > 0 } message) report = report with { Message = settings.Scrub(root, message) };   // a key id is only ever shown as its last four characters
            SyncReport? attachments = report.Outcome is SyncOutcome.Synced or SyncOutcome.NothingToDo ? await SyncAttachments(l, storage, allowAttachments) : null;   // after a board sync that did not fail or pause
            Handle(l, report, sent, attachments, attachmentsRan: report.Outcome is SyncOutcome.Synced or SyncOutcome.NothingToDo);
            NoteSyncWrites(l, report);
            if (setup.SettleWindow > TimeSpan.Zero && report.Settling.Count + (attachments?.Settling.Count ?? 0) > 0) Schedule(l, setup.SettleWindow);   // files were written too recently to take: one more pass once they have settled
        }
        finally { (storage as IDisposable)?.Dispose(); }
        return report;
    }

    // ── conflict markers a git merge left in board files ──

    /// <summary>
    /// A git merge that both branches touched can leave markers in .openspec.yaml, tasks.md, a note or events.log, and be committed that way. Each such file is rebuilt from the two sides of
    /// the merge (git names them, see <see cref="GitHistoryApi.ConflictSides"/>; without them the hunks in the file are the sides) by the board's own merge, the newer side winning what
    /// both changed. It writes the working tree only (never stages or commits), counts as an edit of the app so the sync uploads it, and puts one sync-conflict event per lost change on the item.
    /// A file that cannot be rebuilt stays as it is and pauses the sync, as does a merge that is still open: git owns that state, the sync goes on when it is finished.
    /// </summary>
    private void RepairMarkers(Link l, string? git)
    {
        if (git is null) return;
        string root = l.Root, local = Path.Combine(root, "openspec", "changes");
        List<(string Path, MarkerRepair Repair)> repaired = [];
        InProject(root, () =>
        {
            try
            {
                foreach (string rel in BoardSyncApi.MarkedFiles(local, settings.StateFolder(root)))
                {
                    if (!BoardSyncApi.CanRepairMarkers(rel)) continue;
                    string full = Path.Combine(local, rel.Replace('/', Path.DirectorySeparatorChar));
                    string? text = DataFile.ReadText(full);
                    if (text is null) continue;
                    ConflictSides? sides = GitHistoryApi.ConflictSides("openspec/changes/" + rel, text);
                    if (BoardSyncApi.RepairMarkers(rel, text, sides?.Base, sides?.Ours, sides?.Theirs, sides?.TheirsIsNewer ?? false) is not { } repair) continue;
                    DataFile.WriteAtomic(full, repair.Text);
                    lock (l) if (AddEdited(l, [full])) ScheduleSave(l);
                    repaired.Add((rel, repair));
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }   // what could not be written still has its markers: the sync pauses for it
            if (repaired.Count == 0) return;
            OpenSpecStoreApi.Refresh();       // files were rewritten: the store reads them again before anything is written beside them
            foreach ((string path, MarkerRepair repair) in repaired)
                foreach (MergeConflict c in repair.Conflicts)
                {
                    try { LogRepairedConflict(local, path, c, repair.FromGit); }
                    catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
                }
        });
        if (repaired.Count == 0) return;
        ui.Toast(repaired.Count == 1
            ? $"Repaired the conflict markers git left in {repaired[0].Path}: the newer side won what both sides changed (see the work item's worklog)"
            : $"Repaired the conflict markers git left in {repaired.Count} board files: the newer side won what both sides changed. {string.Join(", ", repaired.Take(3).Select(r => r.Path))}{(repaired.Count > 3 ? "…" : "")}", ToastKind.Info);
        Touched?.Invoke();
    }

    private static void LogRepairedConflict(string local, string path, MergeConflict c, bool fromGit)
    {
        if (ItemOf(local, path) is not { } found) return;
        string how = fromGit ? "the commit that is newer won" : "git's sides were not available, so the first side won";
        WorkItemsApi.LogEvent(found.Item.Id, "sync-conflict", $"{(c.What == path ? found.File : c.What)}: kept \"{Clip(c.Kept)}\", dropped \"{Clip(c.Dropped)}\" (conflict markers from a git merge were repaired; {how})");
    }

    private Task<SyncReport> Engine(SyncOptions options) => setup.Run is { } run ? run(options) : BoardSyncApi.Sync(options);

    // ── the attachments folder: a second sync beside the board's, into the same storage under "attachments/" ──

    /// <summary>
    /// Syncs the project's attachments folder (whole files only: a picture is never merged) through the storage the board sync just used, so an
    /// attachment never reaches a storage the board would not reach. A folder that is not there is not made, and a storage that holds no attachments
    /// is not written to for a project without any: nothing happens until a file or a remote manifest exists.
    /// </summary>
    private async Task<SyncReport?> SyncAttachments(Link l, IBoardStorage storage, bool allow)
    {
        string root = l.Root;
        if (!settings.SyncAttachments(root)) return null;   // switched off: nothing runs, the state folder is left as it is, nothing is deleted in the storage
        string dir = app is null ? NotesApi.AttachmentsRoot(root) : Path.GetFullPath(Path.Combine(root, app.AttachmentsDir(root))), state = settings.AttachmentsStateFolder(root);
        KeepStateForThisFolder(root, state);
        IBoardStorage view = BoardSyncApi.Under(storage, AttachmentsPrefix);
        SyncReport report;
        try
        {
            // The folder was never made, or was moved or deleted. The engine would refuse a known folder that is gone (and delete nothing), and would make state for an unknown one: say nothing.
            if (!Directory.Exists(dir) && (File.Exists(Path.Combine(state, "state.json")) || await view.Get("manifest.json") is null)) return null;
            SyncOptions options = new(dir, state, view, settings.DeviceId(), setup.UtcNow, allow, null, null, WholeFilesOnly: true, SettleWindow: setup.SettleWindow);
            report = await Engine(options);
        }
        catch (BoardStorageException e) { report = new SyncReport(SyncOutcome.Failed, e.Message, [], [], [], [], [], [], []); }
        if (report.Message is { Length: > 0 } message) report = report with { Message = settings.Scrub(root, message) };
        return report;
    }

    // ── the attachments folder is a project setting: it can change under this computer ──

    private const string SeenDirFile = "attachments-dir.txt", StateDirFile = "attachments-state-dir.txt";

    private string? ReadMemory(string root, string name)
    {
        try { return DataFile.ReadText(Path.Combine(settings.ProjectFolder(root), name))?.Trim() is { Length: > 0 } text ? text : null; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return null; }
    }

    private void WriteMemory(string root, string name, string value)
    {
        try { Directory.CreateDirectory(settings.ProjectFolder(root)); DataFile.WriteAtomic(Path.Combine(settings.ProjectFolder(root), name), value); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }   // the next look writes it again
    }

    /// <summary>
    /// The project's attachments folder is a setting in the repository, so a pull can change it. A different folder than the one this computer last had, with files in the old one,
    /// is followed here: they are moved (once; the notes may already say the new place, which the move allows) and one toast says so. Compared on open as well, for a change that came
    /// while the app was closed. Runs inside the sync loop, so a move waits for a sync that is running. A change made in this app's settings was recorded by
    /// <see cref="AttachmentsFolderChangingHere"/> first and is never moved here (the settings page asks the user itself).
    /// </summary>
    private void FollowAttachmentsFolder(Link l)
    {
        if (app is null) return;
        string root = l.Root, current = app.AttachmentsDir(root);
        string? seen;
        lock (l) { seen = l.SeenDir; l.SeenDir = current; }
        if (seen == current) return;
        WriteMemory(root, SeenDirFile, current);
        if (seen is null) return;   // the first look at this project: nothing to follow from
        AttachmentsMoveResult? result = null;
        InProject(root, () =>
        {
            try { if (NotesApi.CountAttachmentFiles(root, seen) > 0) result = NotesApi.MoveAttachments(root, seen, current); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { result = new AttachmentsMoveResult(false, e.Message); }
        });
        if (result is null) return;   // nothing in the old folder: this computer has nothing to move (the attachments sync brings what the storage holds)
        if (!result.Success)
        {
            ui.Toast($"Attachments could not be moved to {current} to follow the project's settings: {result.Message}", ToastKind.Warning);
            return;
        }
        WriteMemory(root, StateDirFile, current);   // the moved files are the same set: the attachments sync's memory is about the new place now
        ui.Toast($"Attachments moved to {current} to follow the project's settings: {result.Message}", ToastKind.Info);
        Touched?.Invoke();
        lock (l) { if (!l.Dropped) Schedule(l); }   // the notes the move rewrote are the app's writes
    }

    /// <summary>
    /// This app's settings page changes the attachments folder (and, for "Move", moves the files itself): it says so BEFORE it saves, so the service does not take the change for a pull.
    /// <paramref name="moved"/>: the files went with it, so the attachments sync's memory (keyed by the path inside the folder) is still right. A folder changed without moving the files
    /// ("Keep them where they are") is a different set: the sync starts over for it as a first connection, which never deletes anything in the storage.
    /// </summary>
    public void AttachmentsFolderChangingHere(string root, string newDir, bool moved)
    {
        if (LinkOf(root) is not { } l) return;
        lock (l) l.SeenDir = newDir;
        WriteMemory(root, SeenDirFile, newDir);
        if (moved) WriteMemory(root, StateDirFile, newDir);
    }

    /// <summary>
    /// The attachments sync's memory is keyed by paths inside the folder. When the folder it was made for is not the one in effect (and no move carried it over), it is about files that are
    /// not there: dropped, so those paths are not taken for deletions. The first sync after that is a first connection.
    /// </summary>
    private void KeepStateForThisFolder(string root, string state)
    {
        if (app is null) return;
        string current = app.AttachmentsDir(root);
        string? made = ReadMemory(root, StateDirFile);
        if (made == current) return;
        try { if (made is not null) BoardStorageSettings.ForgetSyncState(state); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return; }   // not recorded: tried again at the next sync
        WriteMemory(root, StateDirFile, current);
    }

    private const string AttachmentsPrefix = "attachments";

    private static string? GitFolder(string root)
    {
        string git = Path.Combine(root, ".git");
        return Directory.Exists(git) || File.Exists(git) ? git : null;
    }

    /// <summary>
    /// The board-relative paths the app wrote since the last sync that succeeded. The module that writes tells the store (every own write is
    /// recorded there, in this process); the board server writes in another process, so it appends the paths it wrote to a file in the data
    /// folder that is read and cleared here. What git or an editor wrote is in neither: it is an outside edit. The set is kept in a file in the
    /// project's data folder too, so a restart of the app does not turn the app's unsynced edits into outside edits.
    /// </summary>
    private HashSet<string> GatherEdited(Link l)
    {
        DrainServerWrites();
        lock (l)
        {
            Absorb(l);
            return [.. l.Edited];
        }
    }

    /// <summary>Takes what the store recorded since the last look into the set (the app wrote it) and keeps the set on disk.</summary>
    private static bool Absorb(Link l)
    {
        lock (l)
        {
            (IReadOnlyList<string> paths, long cursor) = OpenSpecStoreApi.ChangesSince(l.Cursor);
            l.Cursor = cursor;
            bool grew = AddEdited(l, paths);
            if (grew) ScheduleSave(l);
            return grew;
        }
    }

    /// <summary>Adds the full paths that lie in the project's board to the set, a folder as the files in it; true when the set grew.</summary>
    private static bool AddEdited(Link l, IEnumerable<string> fullPaths)
    {
        string local = Path.Combine(l.Root, "openspec", "changes");
        int before = l.Edited.Count;
        foreach (string full in fullPaths)
        {
            string rel = Path.GetRelativePath(local, full);
            if (rel.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(rel)) continue;
            if (Directory.Exists(full))
                foreach (string file in Directory.EnumerateFiles(full, "*", SearchOption.AllDirectories)) l.Edited.Add(Path.GetRelativePath(local, file).Replace('\\', '/'));
            else l.Edited.Add(rel.Replace('\\', '/'));
        }
        return l.Edited.Count != before;
    }

    // ── the set of app edits survives a restart ──

    internal const string EditedFileName = "edited-in-app.txt";

    /// <summary>The most this app reads of a file in its data folder that another process can also write (server-writes.log, edited-in-app.txt).</summary>
    internal const long MaxLogBytes = 16 * 1024 * 1024;

    private static void LoadEdited(Link l)
    {
        try
        {
            if (!File.Exists(l.EditedFile) || new FileInfo(l.EditedFile).Length > MaxLogBytes) return;   // too large to be ours: unreadable, and the next save replaces it
            foreach (string line in DataFile.ReadLines(l.EditedFile))
                if (line.Trim() is { Length: > 0 } path && !path.StartsWith("..", StringComparison.Ordinal) && !Path.IsPathRooted(path)) l.Edited.Add(path.Replace('\\', '/'));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }   // unreadable = none: the worst case is a stale file taken for an outside edit
    }

    /// <summary>The set grew or shrank: it is written at once (a small file, and only when the set changed), so nothing waits on a timer.</summary>
    private static void ScheduleSave(Link l)
    {
        lock (l) if (l.Dropped) return;
        SaveEdited(l);
    }

    /// <summary>Writes the set to its file (one path per line, atomically); an empty set removes the file.</summary>
    private static void SaveEdited(Link l)
    {
        string[] paths;
        lock (l)
        {
            paths = [.. l.Edited.Order(StringComparer.Ordinal)];
        }
        if (l.EditedFile.Length == 0) return;
        lock (l.SaveLock)
        {
            try
            {
                if (paths.Length == 0) { DataFile.Delete(l.EditedFile); return; }
                Directory.CreateDirectory(Path.GetDirectoryName(l.EditedFile)!);
                DataFile.WriteAtomic(l.EditedFile, string.Join('\n', paths) + "\n");
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }   // the next change writes it again
        }
    }

    private static void ForgetEdited(Link l)
    {
        lock (l) l.Edited.Clear();
        SaveEdited(l);
    }

    private readonly object drainGate = new();

    /// <summary>
    /// Reads and clears the board server's log of the files it wrote and hands every path to the project it lies in (the log is one file for all
    /// projects, so no project may take what is another's). The sets are saved before the log is deleted, so a crash in between loses nothing, and a
    /// batch a crashed run left behind (.reading) is read first. Paths of a project that is not syncing yet stay in the log until it is, if the file still exists.
    /// </summary>
    private void DrainServerWrites()
    {
        string file = Path.Combine(settings.DataFolder, ServerWrites.FileName);
        lock (drainGate)
        {
            List<string> batches = [.. Directory.Exists(settings.DataFolder) ? Directory.EnumerateFiles(settings.DataFolder, ServerWrites.FileName + ".*.reading") : []];
            try
            {
                if (File.Exists(file))
                {
                    string taken = file + "." + Guid.NewGuid().ToString("N") + ".reading";
                    File.Move(file, taken);
                    batches.Add(taken);
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }   // the server is writing it just now: the next sync reads it
            if (batches.Count == 0) return;

            List<string> lines = [];
            List<string> unread = [];
            foreach (string batch in batches)
            {
                try
                {
                    // Beyond the cap the file is not read at all and is deleted with the other batches: its paths are lost, so those board files look like outside
                    // edits at the next sync (the worst case is old content replaced by the synced version, which the stale toast and the trash cover).
                    if (new FileInfo(batch).Length > MaxLogBytes) continue;
                    foreach (string raw in DataFile.ReadLines(batch))
                    {
                        if (raw.Trim() is not { Length: > 0 } text) continue;
                        try { lines.Add(Path.GetFullPath(text)); }
                        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException) { }   // one bad line (a NUL, a device name) is skipped, never the batch
                    }
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException) { unread.Add(batch); }
            }
            List<Link> all;
            lock (gate) all = [.. links.Values];
            List<string> unclaimed = [];
            foreach (string path in lines.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                bool claimed = false;
                foreach (Link l in all)
                {
                    string local = Path.GetFullPath(Path.Combine(l.Root, "openspec", "changes")) + Path.DirectorySeparatorChar;
                    if (!path.StartsWith(local, StringComparison.OrdinalIgnoreCase)) continue;
                    lock (l) AddEdited(l, [path]);
                    claimed = true;
                }
                if (!claimed && (File.Exists(path) || Directory.Exists(path))) unclaimed.Add(path);
            }
            foreach (Link l in all) SaveEdited(l);
            try { if (unclaimed.Count > 0) File.AppendAllLines(file, unclaimed); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { unread.AddRange(batches); }   // keep the batches: nothing is lost, the next sync reads them again
            foreach (string batch in batches.Except(unread))
                try { File.Delete(batch); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        }
    }

    // ── what a report means ──

    /// <summary>
    /// One look at what a sync cycle did: the board's report and, when it ran, the attachments' (<paramref name="attachments"/>; <paramref name="attachmentsRan"/> says whether the
    /// cycle got that far, so a failed board sync leaves the last attachments report as it was). One state line and one toast per kind of news for both.
    /// </summary>
    private void Handle(Link l, SyncReport report, HashSet<string> sent, SyncReport? attachments = null, bool attachmentsRan = false)
    {
        string project = Path.GetFileName(l.Root.TrimEnd('\\', '/'));
        lock (l)
        {
            l.Last = report;
            l.LastAt = Now();
            if (attachmentsRan) l.LastAtt = attachments;
            if (report.Outcome is SyncOutcome.Synced or SyncOutcome.NothingToDo)
            {
                l.Edited.ExceptWith(sent);   // only what this sync saw: a write made while it ran stays for the next
                ScheduleSave(l);
                l.FailedInRow = false;
                l.BlockedToasted = null;
            }
        }
        switch (report.Outcome)
        {
            case SyncOutcome.Failed:
                if (!l.FailedInRow) ui.Toast($"Sync failed for {project}: {report.Message}", ToastKind.Warning);   // once: not at every sync while offline
                l.FailedInRow = true;
                break;
            case SyncOutcome.Blocked:
                // One toast when a pause starts. A git pause is one pause however its message changes (one more conflicted file); the deletion brake counts what it would delete.
                string pause = report.Blocked == SyncBlock.GitBusy ? "git" : report.Message ?? "";
                if (l.BlockedToasted != pause)
                    ui.Toast(report.Blocked == SyncBlock.GitBusy ? $"Sync paused for {project}: {report.Message}" : $"Sync paused for {project}: {report.Message} See Settings, Board storage.", ToastKind.Warning);
                l.BlockedToasted = pause;
                break;
            default:
                int received = Received(report), receivedFiles = attachments is null ? 0 : Received(attachments);
                if (received + receivedFiles > 0)
                    ui.Toast(received == 0
                        ? $"Received {receivedFiles} attachment{(receivedFiles == 1 ? "" : "s")} from the storage"
                        : $"Received {received + receivedFiles} change{(received + receivedFiles == 1 ? "" : "s")} from the storage", ToastKind.Info);
                if (report.BackedUp.Count > 0)
                    ui.Toast($"First sync of {project}: {report.BackedUp.Count} file{(report.BackedUp.Count == 1 ? "" : "s")} here differed from the storage's and were replaced. Your copies are in the backup folder {report.BackupFolder ?? Path.GetDirectoryName(report.BackedUp[0])}", ToastKind.Warning);
                if (report.Stale.Count > 0)
                    ui.Toast(report.Stale.Count == 1
                        ? "1 board file was older than the synced version and was replaced by it"
                        : $"{report.Stale.Count} board files were older than the synced version and were replaced by it", ToastKind.Info);
                int trashed = report.Trashed.Count + (attachments?.Trashed.Count ?? 0);
                if (trashed > 0)
                    ui.Toast(trashed == 1
                        ? $"1 file the sync removed is kept in the trash for {TrashDays} days"
                        : $"{trashed} files the sync removed are kept in the trash for {TrashDays} days", ToastKind.Info);
                if (report.SkippedLinks.Count > 0)
                {
                    string set = string.Join('\n', report.SkippedLinks.Order(StringComparer.Ordinal));
                    if (l.SkippedToasted != set) ui.Toast(SkippedText(report.SkippedLinks), ToastKind.Warning);   // once for a set, not at every look
                    l.SkippedToasted = set;
                }
                else l.SkippedToasted = null;
                // report.Replaced (copies of files a download replaced) is not announced: nothing was removed, and the trash link shows as soon as the folder exists.
                if (report.Conflicts.Count > 0) Record(l, report.Conflicts);
                if (attachmentsRan) HandleAttachments(l, project, attachments);
                ToastTooLarge(l, project, report, attachments);
                break;
        }
        Changed?.Invoke();
    }

    private static int Received(SyncReport r) => r.Downloaded.Concat(r.Merged).Concat(r.DeletedHere).Distinct().Count();

    /// <summary>What only the attachments' sync has to say: it failed or paused, its first connection replaced files, it had a conflict. What it received and trashed is told with the board's.</summary>
    private void HandleAttachments(Link l, string project, SyncReport? report)
    {
        switch (report)
        {
            case { Outcome: SyncOutcome.Failed }:
                if (!l.AttFailedInRow) ui.Toast($"Attachments sync failed for {project}: {report.Message}", ToastKind.Warning);   // once: not at every sync while offline
                l.AttFailedInRow = true;
                return;
            case { Outcome: SyncOutcome.Blocked }:
                if (l.AttBlockedToasted != report.Message) ui.Toast($"Attachments sync paused for {project}: {report.Message} See Settings, Board storage.", ToastKind.Warning);
                l.AttBlockedToasted = report.Message;
                return;
        }
        l.AttFailedInRow = false;
        l.AttBlockedToasted = null;
        if (report is null) return;
        if (report.BackedUp.Count > 0)
            ui.Toast($"First sync of {project}'s attachments: {report.BackedUp.Count} file{(report.BackedUp.Count == 1 ? "" : "s")} here differed from the storage's and were replaced. Your copies are in the backup folder {report.BackupFolder ?? Path.GetDirectoryName(report.BackedUp[0])}", ToastKind.Warning);
        if (report.Conflicts.Count > 0) RecordAttachmentConflicts(l, report.Conflicts);
    }

    /// <summary>
    /// A file changed on both computers: the newer whole copy was kept and the other is in the trash. One toast for the sync naming the file (or how many); and when the path
    /// is <c>wi&lt;id&gt;/…</c> and that work item exists, one sync-conflict event on it. Never a note, and nothing at all on an item for a path that names none.
    /// </summary>
    private void RecordAttachmentConflicts(Link l, IReadOnlyList<SyncConflict> conflicts)
    {
        ui.Toast(conflicts.Count == 1
            ? $"Attachment {conflicts[0].Path} changed on both computers: the newer copy was kept, the other is in the trash"
            : $"{conflicts.Count} attachments changed on both computers: the newer copies were kept, the others are in the trash. {string.Join(", ", conflicts.Take(3).Select(c => c.Path))}{(conflicts.Count > 3 ? "…" : "")}", ToastKind.Warning);
        bool logged = false;
        InProject(l.Root, () =>
        {
            foreach (SyncConflict c in conflicts)
            {
                string[] parts = c.Path.Split('/', 2);
                if (parts.Length != 2 || !parts[0].StartsWith("wi", StringComparison.Ordinal) || !int.TryParse(parts[0].AsSpan(2), out int id)) continue;
                try
                {
                    if (WorkItemsApi.GetAll(includeHidden: true).All(w => w.Id != id)) continue;
                    logged |= WorkItemsApi.LogEvent(id, "sync-conflict", $"attachment {parts[1]}: the newer copy was kept, the other is in the trash").Success;
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
            }
        });
        if (logged)
        {
            Touched?.Invoke();
            lock (l) { if (!l.Dropped) Schedule(l); }   // the event is the app's write: it goes up on the next sync
        }
    }

    private const long BigMb = 64;

    /// <summary>Files over the size limit, from both syncs: one line while any exist (see <see cref="TooLargeText"/>) and one toast the first time a given set is seen.</summary>
    private void ToastTooLarge(Link l, string project, SyncReport board, SyncReport? attachments)
    {
        string? text = TooLargeText(board, attachments);
        string set = text is null ? "" : string.Join('\n', board.TooLarge.Concat(attachments?.TooLarge ?? []).Order(StringComparer.Ordinal));
        if (text is not null && l.TooLargeToasted != set) ui.Toast(text, ToastKind.Warning);
        l.TooLargeToasted = set.Length == 0 ? null : set;
    }

    private static string? TooLargeText(SyncReport board, SyncReport? attachments)
    {
        List<string> all = [.. board.TooLarge, .. attachments?.TooLarge ?? []];
        if (all.Count == 0) return null;
        return all.Count == 1 ? $"1 file is over {BigMb} MB and is not synced: {all[0]}" : $"{all.Count} files are over {BigMb} MB and are not synced: {all[0]}…";
    }

    /// <summary>The words for links the sync refused to follow: how many and the first one.</summary>
    private static string SkippedText(IReadOnlyList<string> links) =>
        links.Count == 1
            ? $"1 link inside the board was skipped: the sync never follows links. {links[0]}"
            : $"{links.Count} links inside the board were skipped: the sync never follows links. {links[0]}…";

    private const int TrashDays = 30;   // how long the engine keeps its trash (BoardSync's SyncStateStore.TrashKeepDays)

    // ── conflicts: the side that lost is never gone ──

    private void Record(Link l, IReadOnlyList<SyncConflict> conflicts)
    {
        string local = Path.Combine(l.Root, "openspec", "changes");
        List<(SyncConflict Conflict, string Why)> toFile = [];
        int recorded = 0;
        InProject(l.Root, () =>
        {
            OpenSpecStoreApi.Refresh();   // the sync rewrote files: the store reads them again before anything is written beside them
            foreach (SyncConflict c in conflicts)
            {
                try { if (OnItem(local, c)) { recorded++; continue; } }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
                toFile.Add((c, "the work item is gone or could not be written"));
            }
        });
        if (recorded > 0)
        {
            ui.Toast($"{recorded} sync conflict{(recorded == 1 ? "" : "s")}: what was dropped is kept in the worklog of the work item", ToastKind.Warning);
            Touched?.Invoke();
            lock (l) { if (!l.Dropped) Schedule(l); }   // the events and notes are the app's writes: they go up on the next sync
        }
        if (toFile.Count > 0) KeepInFiles(l, toFile);
    }

    private static string Clip(string text)
    {
        string one = string.Join(" / ", text.Replace("\r", "").Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        return one.Length <= 120 ? one : one[..117] + "...";
    }

    /// <summary>The work item whose folder holds the board-relative path (an archived one too) and the path inside that folder; null when there is none.</summary>
    private static (WorkItem Item, string File)? ItemOf(string local, string path)
    {
        string[] segments = path.Split('/');
        int depth = segments[0] == "archive" && segments.Length > 2 ? 2 : 1;
        string folder = Path.GetFullPath(Path.Combine(local, string.Join('/', segments.Take(depth))));
        string file = string.Join('/', segments.Skip(depth));
        if (file.Length == 0) return null;
        WorkItem? item = WorkItemsApi.GetAll(includeHidden: true).FirstOrDefault(w => string.Equals(Path.GetFullPath(w.Path).TrimEnd('\\', '/'), folder.TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase));
        return item is null || !Directory.Exists(item.Path) ? null : (item, file);
    }

    /// <summary>Puts one conflict on its work item (an event for a changed field, a note for lost text); false when there is no such item.</summary>
    private static bool OnItem(string local, SyncConflict c)
    {
        if (ItemOf(local, c.Path) is not { } found) return false;
        (WorkItem item, string file) = found;

        bool field = file is ".openspec.yaml" or "tasks.md" && c.Kept != "deleted";
        if (field)
            return WorkItemsApi.LogEvent(item.Id, "sync-conflict", $"{(c.What == c.Path ? file : c.What)}: kept \"{Clip(c.Kept)}\", dropped \"{Clip(c.Dropped)}\" ({Whose(c)})").Success;

        (bool ok, _, int noteId) = NotesApi.Create(item.Id, "Sync");
        if (!ok) return false;
        string what = c.What == c.Path ? "" : ", " + c.What;
        NotesApi.SetTitle(noteId, $"Sync conflict: {file}{what}");
        string fence = new('`', Math.Max(3, LongestRun(c.Dropped, '`') + 1));
        string kept = c.Kept == "deleted" ? "the deletion" : $"the text now in `{file}`";
        bool saved = NotesApi.SaveBody(noteId, $"Two computers changed the same place in `{file}` ({c.What}). The sync kept {kept} and dropped {Whose(c)}; the text it dropped is here in full.\n\n{fence}\n{c.Dropped.Replace("\r\n", "\n").TrimEnd('\n')}\n{fence}\n").Success;
        NotesApi.StampAgent(noteId, "Sync");   // marked like an agent's note: it stands out until the user has looked at it
        return saved;
    }

    /// <summary>Which side lost, from the point of view of the computer that synced.</summary>
    private static string Whose(SyncConflict c) => c.DroppedHere ? "this computer's change" : "another computer's change";

    private static int LongestRun(string text, char c)
    {
        int best = 0, run = 0;
        foreach (char x in text) { run = x == c ? run + 1 : 0; best = Math.Max(best, run); }
        return best;
    }

    private void KeepInFiles(Link l, List<(SyncConflict Conflict, string Why)> items)
    {
        string dir = Path.Combine(settings.ProjectFolder(l.Root), "conflicts");
        try { Directory.CreateDirectory(dir); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }   // the writes below say what failed
        foreach ((SyncConflict c, string why) in items)
        {
            string name = $"{Now():yyyyMMdd-HHmmss}-{new string([.. c.Path.Select(ch => char.IsLetterOrDigit(ch) || ch is '.' or '-' ? ch : '_')])}.txt";
            try { DataFile.WriteAtomic(Path.Combine(dir, name), $"{c.Path}\n{c.What}; kept: {c.Kept}; dropped: {Whose(c)}\nThe text that was dropped ({why}):\n\n{c.Dropped}\n"); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                ui.Toast($"A sync conflict could not be kept ({e.Message}); this is what was dropped from {c.Path}: {Clip(c.Dropped)}", ToastKind.Warning);   // never gone: at least said
                continue;
            }
        }
        ui.Toast($"{items.Count} sync conflict{(items.Count == 1 ? "" : "s")} could not be put on a work item; what was dropped is kept in {dir}", ToastKind.Warning);
    }

    // ── what the page shows ──

    public SyncStatus Status(string root)
    {
        if (settings.Unreadable(root)) return new(false, settings.Problem(root) ?? "", false, false, 0);
        if (settings.Config(root) is null) return new(false, "This folder only", false, false, 0);
        if (settings.Proposal(root) is { } proposal) return new(true, proposal.Line, false, false, 0, null, proposal);   // asked, not allowed: nothing runs, whatever a link still remembers
        if (LinkOf(root) is not { } l) return new(true, settings.Problem(root) ?? "Not syncing", false, false, 0);
        string local = Path.Combine(root, "openspec", "changes");
        string? paused;
        try { paused = l.LastPause = BoardSyncApi.GitPause(local, GitFolder(root), settings.StateFolder(root)); }   // git in the middle of a merge: shown now, not only after the next sync
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { paused = l.LastPause; }   // a pass is saving the state right now: what the last render showed
        string boardTrash = Path.Combine(settings.StateFolder(root), "trash"), attachmentsTrash = Path.Combine(settings.AttachmentsStateFolder(root), "trash");
        // One link for both syncs' trash: the folder that is there, or the sync folder that holds both when both are.
        string? trash = Directory.Exists(boardTrash) ? Directory.Exists(attachmentsTrash) ? settings.ProjectFolder(root) : boardTrash : Directory.Exists(attachmentsTrash) ? attachmentsTrash : null;
        int unsent;
        lock (l)
        {
            HashSet<string> pending = new(l.Edited, StringComparer.Ordinal);
            foreach (string full in OpenSpecStoreApi.ChangesSince(l.Cursor).Paths)
                if (Path.GetRelativePath(local, full) is { } rel && !rel.StartsWith("..", StringComparison.Ordinal) && !Path.IsPathRooted(rel)) pending.Add(rel.Replace('\\', '/'));
            unsent = pending.Count;
            // only the deletion brake can be allowed (the board's, or the attachments' when the board's sync was fine); a git pause clears when git is done
            bool canAllow = paused is null && (l.Last is { Outcome: SyncOutcome.Blocked, Blocked: SyncBlock.MassDelete } || l.Last is { Outcome: SyncOutcome.Synced or SyncOutcome.NothingToDo } && l.LastAtt is { Outcome: SyncOutcome.Blocked, Blocked: SyncBlock.MassDelete });
            string line = Line(l, unsent, paused);
            return new(true, line, canAllow, l.Running && l.Last is null, unsent, trash, null, l.Last is { SkippedLinks.Count: > 0 } last ? SkippedText(last.SkippedLinks) : null,
                l.Last is { } board ? TooLargeText(board, l.LastAtt) : null);
        }
    }

    private string Line(Link l, int unsent, string? paused)
    {
        if (l.Problem is not null) return l.Problem;
        string more = unsent > 0 ? $" ({unsent} change{(unsent == 1 ? "" : "s")} not sent)" : "";
        if (paused is not null) return $"Paused: {paused}{more}";
        switch (l.Last)
        {
            case null: return l.Running ? "Syncing…" : l.QuietDue is { } due0 ? $"Waiting to sync at {Clock(due0)}" : "Waiting to sync";
            case { Outcome: SyncOutcome.Failed } r: return $"Failed: {r.Message}{more}";
            case { Outcome: SyncOutcome.Blocked } r: return $"Paused: {r.Message}{more}";
            case { Outcome: SyncOutcome.Synced or SyncOutcome.NothingToDo } when l.LastAtt is { Outcome: SyncOutcome.Failed or SyncOutcome.Blocked } a: return $"Attachments: {a.Message}{more}";   // the board was fine: the attachments' trouble is the state
            case { Outcome: SyncOutcome.NothingToDo } when l.QuietDue is null && l.LastAtt is { Outcome: SyncOutcome.Synced }: return SyncedLine(l.Last, l.LastAtt, l.LastAt);   // only attachments moved
            case { Outcome: SyncOutcome.Synced or SyncOutcome.NothingToDo } when l.QuietDue is { } due: return $"Waiting to sync at {Clock(due)}{more}";   // a change since the last sync: the wait is what is next
            case { Outcome: SyncOutcome.NothingToDo }: return $"Nothing new, checked at {l.LastAt:HH:mm}";
            default: return SyncedLine(l.Last, l.LastAtt, l.LastAt);
        }
    }

    /// <summary>One line for both syncs: the board's files and the attachments are counted together; when only attachments moved, it says so.</summary>
    private string Clock(DateTime utc) => utc.ToLocalTime().ToString("HH:mm");

    private static string SyncedLine(SyncReport board, SyncReport? attachments, DateTime at)
    {
        (int boardReceived, int boardSent) = Counts(board);
        (int fileReceived, int fileSent) = attachments is null ? (0, 0) : Counts(attachments);
        List<string> parts = [];
        if (boardReceived + boardSent == 0 && fileReceived + fileSent > 0)
        {
            if (fileReceived > 0) parts.Add($"{fileReceived} attachment{(fileReceived == 1 ? "" : "s")} received");
            if (fileSent > 0) parts.Add($"{fileSent} attachment{(fileSent == 1 ? "" : "s")} sent");
        }
        else
        {
            if (boardReceived + fileReceived > 0) parts.Add($"{boardReceived + fileReceived} received");
            if (boardSent + fileSent > 0) parts.Add($"{boardSent + fileSent} sent");
        }
        return $"Synced at {at:HH:mm}" + (parts.Count > 0 ? ": " + string.Join(", ", parts) : "");
    }

    private static (int Received, int Sent) Counts(SyncReport r) => (Received(r), r.Uploaded.Concat(r.DeletedInStorage).Distinct().Count());

    /// <summary>Asks the storage whether it answers, with what is saved now.</summary>
    public async Task<(bool Success, string Message)> Test(string root)
    {
        (IBoardStorage? storage, string? problem) = settings.Open(root);
        if (storage is null) return (false, problem ?? "This project is not connected to a storage.");
        try { (bool ok, string message) = await BoardSyncApi.TestConnection(storage); return (ok, settings.Scrub(root, message)); }
        catch (Exception e) when (e is not OperationCanceledException) { return (false, settings.Scrub(root, $"Could not reach the storage: {e.Message}")); }
    }

    public void Dispose()
    {
        settings.Changed -= ConfigureAll;
        List<Link> all;
        lock (gate) { all = [.. links.Values]; links.Clear(); }
        foreach (Link l in all) Stop(l);
    }
}

/// <summary>
/// The board server writes in its own process, so the desktop app cannot see those writes as its own. The server appends the full path of every
/// file it wrote to this file in the data folder (about twice a second); the desktop app reads it at the next sync and clears it. Git's writes
/// are in neither place, which is how they stay outside edits. At 1 MB (a server running with no desktop app) the file is first reduced to its distinct paths, which is a small fraction of it; only if it is still that large does it stop growing, and the paths written after that are not recorded, so they look like outside edits (the worst case is old content replaced by the synced version, which the stale toast and the trash cover).
/// </summary>
public static class ServerWrites
{
    public const string FileName = "server-writes.log";
    private const long Cap = 1_000_000;

    /// <summary>Appends the paths this process wrote after <paramref name="cursor"/>; returns the position to ask from next time.</summary>
    public static long Flush(string dataFolder, long cursor)
    {
        (IReadOnlyList<string> paths, long next) = OpenSpecStoreApi.ChangesSince(cursor);
        if (paths.Count == 0) return next;
        string file = Path.Combine(dataFolder, FileName);
        Directory.CreateDirectory(dataFolder);
        if (File.Exists(file) && new FileInfo(file).Length >= Cap)
            File.WriteAllLines(file, DataFile.ReadLines(file).Distinct());   // the same path is logged again at every flush: only distinct ones are needed
        if (!File.Exists(file) || new FileInfo(file).Length < Cap) File.AppendAllLines(file, paths.Distinct());
        return next;
    }

    /// <summary>Flushes until <paramref name="ct"/> ends; starts from now, so what was written before the server started is not claimed.</summary>
    public static async Task Run(string dataFolder, CancellationToken ct)
    {
        long cursor = OpenSpecStoreApi.ChangesSince(long.MaxValue).Cursor;
        try
        {
            while (true)
            {
                await Task.Delay(500, ct);
                try { cursor = Flush(dataFolder, cursor); }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }   // the desktop app is reading the file just now: the paths go in next time
            }
        }
        catch (OperationCanceledException) { }
    }
}
