using Features;
using GitHistory;
using Notes;
using Notes.PublicDTOs;
using OpenSpecStore;
using WorkItems;
using TaskManager.BackgroundJobs;
using TaskManager.Helpers;

namespace TaskManager.AppState;

/// <summary>
/// Composition-root glue for the UI: opens a repository (configuring every module), and tells pages to
/// re-read when files change on disk or after the app's own edits.
/// </summary>
public sealed class Workspace : IDisposable
{
    private readonly SettingsStore settings;
    private readonly UiService ui;   // the first window's: where a dialog asked for without a window goes
    private readonly List<UiService> windows = [];   // every open window's dialogs and toasts: a notice goes to all of them
    private readonly BoardSyncService? sync;   // null where no storage can be connected (tests that do not register it)
    private readonly object scanGate = new();   // held while the duplicate scan runs: a Notify from the scan's own writes (same thread) sees it and does not start another, any other thread waits its turn
    private readonly object pendingGate = new();
    private Task pending = Task.CompletedTask;
    private string? guideNotice;

    /// <summary>What to tell the user about agent guides the app just created or refreshed (once), or null.</summary>
    public string? TakeGuideNotice()
    {
        string? notice = guideNotice;
        guideNotice = null;
        return notice;
    }

    private void NoteGuide(string root, GuideChange change)
    {
        string line = change switch
        {
            GuideChange.Created => $"Created openspec/{AgentGuide.FileName} in {Path.GetFileName(root)}: the conventions agents should follow here. Add a line pointing to it in your CLAUDE.md / AGENTS.md.",
            GuideChange.Updated => $"Updated openspec/{AgentGuide.FileName} in {Path.GetFileName(root)} to the app's newer conventions.",
            _ => "",
        };
        if (line.Length > 0) guideNotice = guideNotice is null ? line : guideNotice + " " + line;
    }

    public Workspace(SettingsStore settings, UiService ui, BoardSyncService? sync = null)
    {
        this.settings = settings;
        this.ui = ui;
        windows.Add(ui);
        this.sync = sync;
        if (sync is not null) { sync.InProject = In; sync.Touched += Notify; }
        OpenSpecStoreApi.Changed += Notify;
        GitHistoryApi.Changed += OnGitChanged;
        plansSeen = PlansState;
        settings.Changed += RefreshGuides;
    }

    private string plansSeen;
    /// <summary>What the guides and the Notes module are configured from: every open project's plans folder and attachments (images) folder (the project's files folder, which a pull can change, or the old per-computer values).</summary>
    private string PlansState => string.Join(";", ConfiguredRoots().Select(r => r + "=" + settings.PlansDir(r) + "|" + settings.AttachmentsDir(r)));

    private readonly HashSet<string> configured = new(StringComparer.OrdinalIgnoreCase);   // the open projects ConfigureProject has been through: a project is configured when it opens, not when its folder is first read

    private List<string> ConfiguredRoots()
    {
        lock (configured) return [.. OpenSpecStoreApi.OpenRoots.Where(r => configured.Contains(Path.GetFullPath(r))).OrderBy(r => r, StringComparer.OrdinalIgnoreCase)];
    }

    /// <summary>Gives the Notes module the project's attachments folder and brings the project's agent guide up to date with its plans and attachments folders.</summary>
    private void ConfigureProject(string root)
    {
        lock (configured) configured.Add(Path.GetFullPath(root));
        NotesApi.SetAttachmentsDir(root, settings.AttachmentsDir(root));
        NoteGuide(root, AgentGuide.Ensure(root, settings.PlansDir(root), settings.AttachmentsDir(root)));
        plansSeen = PlansState;
    }

    /// <summary>Makes sure git does not ignore the project's events.log files (GitHistory is pointed at <paramref name="root"/>); says so once when it had to add an exception.</summary>
    private void TrackWorklog(string root)
    {
        if (WorklogTracking.Ensure(root) is string line) guideNotice = guideNotice is null ? line : guideNotice + " " + line;
    }

    /// <summary>The guide carries the plans folder,so moving it rewrites the guide in every open project (and says so at once). Other settings changes are ignored.</summary>
    private void RefreshGuides()
    {
        if (PlansState == plansSeen) return;
        plansSeen = PlansState;
        foreach (string root in OpenSpecStoreApi.OpenRoots) ConfigureProject(root);
        if (TakeGuideNotice() is string notice) Say(notice);
    }

    /// <summary>A separate window's dialogs and toasts join the main window's: a notice of the workspace (not of one action) is shown in every window until the returned handle is disposed (the window closed).</summary>
    public IDisposable AttachWindow(UiService window)
    {
        lock (windows) windows.Add(window);
        return new Detach(() => { lock (windows) windows.Remove(window); });
    }

    /// <summary>Tells the user something that concerns the whole workspace (a duplicate number renumbered, a guide rewritten) in every open window. An action's own result is a toast of the window that did it.</summary>
    public void Say(string text, ToastKind kind = ToastKind.Info)
    {
        UiService[] all;
        lock (windows) all = [.. windows];
        foreach (UiService window in all) window.Toast(text, kind);
    }

    private sealed class Detach(Action undo) : IDisposable
    {
        private Action? undo = undo;
        public void Dispose() => Interlocked.Exchange(ref undo, null)?.Invoke();
    }

    /// <summary>Raised on any change; components re-read through the module Apis and re-render.</summary>
    public event Action? Changed;

    /// <summary>The active repository: the one pages read and write, and the side menu names.</summary>
    public string? Root => OpenSpecStoreApi.RepoRoot;

    private string? viewRoot;

    /// <summary>
    /// The project whose saved view (<see cref="SettingsStore.Board"/>) the board and the side menu show and change. It is the project the user switched to by its tab;
    /// opening a card or adding an item in another project makes that one the active <see cref="Root"/> (its page reads it) but does not swap the arrangement the user made
    /// on the board. Unlike <see cref="Root"/> it does not move while another project is read in passing (<see cref="In{T}"/>).
    /// </summary>
    public string? ViewRoot => viewRoot is string v && OpenRepos.Any(r => SettingsStore.Key(r) == SettingsStore.Key(v)) ? viewRoot : Root;

    /// <summary>The repositories open as tabs that exist on disk, in display order: what the board and list show together.</summary>
    public IReadOnlyList<string> Projects
    {
        get
        {
            IReadOnlyList<string> open = OpenSpecStoreApi.OpenRoots;
            return OpenRepos.Where(r => open.Any(o => SettingsStore.Key(o) == SettingsStore.Key(r))).ToList();
        }
    }

    public (bool Success, string Message) Open(string root)
    {
        bool fresh = !OpenSpecStoreApi.OpenRoots.Any(r => SettingsStore.Key(r) == SettingsStore.Key(root));
        (bool ok, string message) = OpenSpecStoreApi.Open(root, keepOthers: true);
        if (!ok) return (ok, message);
        if (viewRoot is null || !OpenSpecStoreApi.OpenRoots.Any(r => SettingsStore.Key(r) == SettingsStore.Key(viewRoot))) viewRoot = root;   // the first project, or the one the view was on being gone
        if (message.Length > 0) guideNotice = message;   // openspec/ was just set up here: said together with the guide notice
        ItemsCache.Rescan(Path.GetFullPath(root));   // a project being opened is scanned for duplicate numbers once
        GitHistoryApi.Configure(root, reset: fresh);
        GitHistoryApi.Watch(root);
        WorkItemsApi.ConfigureContributors(() => GitHistoryApi.IsRepository() ? (GitHistoryApi.HeadToken(), GitHistoryApi.AuthorsOfChange) : null);
        WorkItemsApi.ConfigureActor(() => Me);   // the worklog's events say the user did it
        settings.Update(s => s with { LastRepo = OpenSpecStoreApi.RepoRoot });
        ConfigureProject(root);
        TrackWorklog(root);
        sync?.Attach(root);
        OpenTabs(root);
        KeepTabs();
        Notify(wait: true);   // a project being opened has its duplicate numbers repaired before its first page reads it
        return (true, "");
    }

    /// <summary>Makes an open project the active one and the one whose arrangement the board shows, keeping the tabs in their order ("Make it the active project").</summary>
    public (bool Success, string Message) Switch(string root)
    {
        string? viewBefore = viewRoot;
        viewRoot = root;
        (bool ok, string message) = Open(root);
        if (!ok) viewRoot = viewBefore;
        return (ok, message);
    }

    /// <summary>The other tabs open in the store too (the board shows them all); one whose folder is gone is skipped.</summary>
    private void OpenTabs(string active)
    {
        foreach (string tab in OpenRepos.Where(t => !OpenSpecStoreApi.OpenRoots.Any(o => SettingsStore.Key(o) == SettingsStore.Key(t))))
            if (OpenSpecStoreApi.Open(tab, keepOthers: true).Success) { GitHistoryApi.Configure(tab); GitHistoryApi.Watch(tab); ConfigureProject(tab); TrackWorklog(tab); sync?.Attach(tab); }
        OpenSpecStoreApi.Use(active);
        GitHistoryApi.Configure(active);
    }

    /// <summary>Closes what is neither a tab nor the active repository.</summary>
    private void KeepTabs()
    {
        List<string> keep = [.. OpenRepos, .. Root is string r ? [r] : Array.Empty<string>()];
        OpenSpecStoreApi.Keep(keep);
        GitHistoryApi.Keep(keep);
        sync?.Prune(keep);
    }

    /// <summary>Reads or writes another open repository. The repository is the one of this call only (a scope), so the active one never moves: the watcher's timer, a sync pass and the page being drawn each read their own.</summary>
    public T In<T>(string root, Func<T> work)
    {
        string? active = Root;
        if (active is not null && SettingsStore.Key(active) == SettingsStore.Key(root)) return work();
        if (!OpenSpecStoreApi.OpenRoots.Any(r => SettingsStore.Key(r) == SettingsStore.Key(root))) throw new InvalidOperationException($"{root} is not open");
        using (OpenSpecStoreApi.InRepo(root))
        using (GitHistoryApi.InRepo(root))
            return work();
    }

    /// <summary>The scope of one window that keeps to its own project (a separate window): everything read or written from here until it is disposed is that project's, whatever tab the main window switched to. Null when the project is not open.</summary>
    public IDisposable? Scope(string root)
    {
        if (!OpenSpecStoreApi.OpenRoots.Any(r => SettingsStore.Key(r) == SettingsStore.Key(root))) return null;
        IDisposable store = OpenSpecStoreApi.InRepo(root), git = GitHistoryApi.InRepo(root);
        return new Detach(() => { git.Dispose(); store.Dispose(); });
    }

    public void In(string root, Action work) => In(root, () => { work(); return 0; });

    /// <summary>Repositories open as tabs; falls back to just the active one once settings have any value at all.</summary>
    public IReadOnlyList<string> OpenRepos =>
        settings.Current.OpenRepos is { Count: > 0 } list ? list : (Root is string r ? [r] : []);

    /// <summary>Opens a repository as a new tab (or switches to it if already open) and makes it active.</summary>
    public (bool Success, string Message) OpenTab(string root)
    {
        List<string> before = OpenRepos.ToList();   // snapshot before Open() switches Root, which OpenRepos falls back on
        string? viewBefore = viewRoot;
        viewRoot = root;   // switching to a tab is choosing its view; set first, so the board that settings.Update tells reads the right project's
        settings.Update(s => s with { OpenRepos = [.. before.Where(p => SettingsStore.Key(p) != SettingsStore.Key(root)), root] });   // first, so Open keeps every tab
        (bool ok, string message) = Open(root);
        if (!ok) { viewRoot = viewBefore; settings.Update(s => s with { OpenRepos = before }); }
        return (ok, message);
    }

    /// <summary>
    /// Moves the given tabs one place up (dir -1) or down (+1), each past the next tab that is not moving, as the arrow keys move
    /// selected cards; a scattered selection closes up as it goes. False when nothing could move.
    /// </summary>
    public bool MoveTabs(IReadOnlyCollection<string> roots, int dir)
    {
        List<string> tabs = OpenRepos.ToList();
        bool Moving(int i) => roots.Any(r => SettingsStore.Key(r) == SettingsStore.Key(tabs[i]));
        bool moved = false;
        IEnumerable<int> order = dir < 0 ? Enumerable.Range(1, Math.Max(0, tabs.Count - 1)) : Enumerable.Range(0, Math.Max(0, tabs.Count - 1)).Reverse();
        foreach (int i in order)
        {
            int j = i + Math.Sign(dir);
            if (Moving(i) && !Moving(j)) { (tabs[i], tabs[j]) = (tabs[j], tabs[i]); moved = true; }
        }
        if (moved) settings.Update(s => s with { OpenRepos = tabs });
        return moved;
    }

    /// <summary>Closes a tab; the last remaining one can't be closed. Switches to another tab if the active one closes.</summary>
    public void CloseTab(string repo)
    {
        if (OpenRepos.Count <= 1) return;
        List<string> remaining = OpenRepos.Where(p => SettingsStore.Key(p) != SettingsStore.Key(repo)).ToList();
        if (viewRoot is string v && SettingsStore.Key(v) == SettingsStore.Key(repo)) viewRoot = null;   // Open below (or the fallback to Root) picks the next
        settings.Update(s => s with { OpenRepos = remaining });
        if (Root is string r && SettingsStore.Key(r) == SettingsStore.Key(repo)) Open(remaining[^1]);
        else KeepTabs();
        Notify();
    }

    /// <summary>Who the app writes as: the name in Settings, else git user.name, else the OS user.</summary>
    public string Me => settings.Current.Name ?? GitHistoryApi.UserName() ?? Environment.UserName;

    public IReadOnlyDictionary<string, string> FeatureNames => FeaturesCache.Get().ToDictionary(f => f.Path, f => f.Name);

    /// <summary>Runs a module command and refreshes the UI when it succeeds.</summary>
    public (bool Success, string Message) Do((bool Success, string Message) result)
    {
        if (result.Success) Notify();
        return result;
    }

    /// <summary>The duplicate scan of the last <see cref="Notify"/> calls, finished or not: what a test awaits (the app never does).</summary>
    public Task Resolved { get { lock (pendingGate) return pending; } }

    /// <summary>
    /// Runs a module command on the thread pool, one at a time in the order asked (two quick edits of one file would otherwise read the same text and
    /// the later write would drop the earlier one), then refreshes the UI as <see cref="Do"/> does. The caller keeps the window free meanwhile.
    /// <paramref name="written"/> runs on the pool after a successful command and before the refresh.
    /// </summary>
    public async Task<(bool Success, string Message)> DoLater(Func<(bool Success, string Message)> command, Action? written = null)
    {
        // Task.Run carries the caller's ambient state (the AsyncLocal repository scope, the test's module state) to the pool thread.
        // The refresh (Do) runs after the write is down and the queue is free: it draws on the window's thread, which is gone once the window closed, and must not hold the next write or Drain.
        TaskCompletionSource landed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Task<(bool Success, string Message)> run = Task.Run(async () =>
        {
            (bool Success, string Message) result;
            await writes.WaitAsync().ConfigureAwait(false);
            try
            {
                result = command();
                if (result.Success) written?.Invoke();
            }
            finally { writes.Release(); landed.SetResult(); }
            return Do(result);
        });
        lock (laterGate) later = later.IsCompleted ? landed.Task : Task.WhenAll(later, landed.Task);   // registered before the first await: Drain sees every command asked so far
        return await run;
    }

    private readonly SemaphoreSlim writes = new(1, 1);
    private readonly object laterGate = new();
    private Task later = Task.CompletedTask;

    /// <summary>
    /// Waits for the commands <see cref="DoLater"/> has queued or is running to write (not for the refresh that follows each), up to <paramref name="limit"/> (on <paramref name="time"/>, the system clock by default);
    /// false when the limit passed first. The app calls it when the last window closes, so a status or tick asked just before closing is written. A command is never cancelled.
    /// </summary>
    public async Task<bool> DrainAsync(TimeSpan limit, TimeProvider? time = null)
    {
        Task all;
        lock (laterGate) all = later;
        try { await all.WaitAsync(limit, time ?? TimeProvider.System).ConfigureAwait(false); return true; }
        catch (TimeoutException) { return false; }
        catch (Exception) { return true; }   // a command that failed reached its own caller; it is not left waiting
    }

    /// <summary>
    /// Any change (an edit here, a file from outside, a folder copied in) tells the pages at once; numbers used twice are resolved afterwards on the
    /// thread pool (<see cref="Resolved"/>): the later change is renumbered, told in a toast, gets a note saying what happened, and the pages are told again.
    /// </summary>
    public void Notify() => Notify(wait: false);

    private void Notify(bool wait)
    {
        if (Monitor.IsEntered(scanGate) || wait)   // the scan's own writes raise this again (its caller reads when it is done); a project being opened needs the numbers repaired before it goes on
        {
            if (wait && !Monitor.IsEntered(scanGate)) Resolve();
            sync?.BoardChanged();
            Changed?.Invoke();
            return;
        }
        sync?.BoardChanged();   // the one place the board's content is reported changing: connected projects sync two quiet minutes after
        Changed?.Invoke();
        Task scan = Task.Run(() => { if (Resolve()) { sync?.BoardChanged(); Changed?.Invoke(); } });
        lock (pendingGate) pending = Task.WhenAll(pending, scan);
    }

    /// <summary>The guides check and the duplicate scan of every open project; true when a number was changed. Under <see cref="scanGate"/>: Notify is called from the watcher's timer, tool calls and the window at once.</summary>
    private bool Resolve()
    {
        bool renumbered = false;
        lock (scanGate)
        {
            if (PlansState != plansSeen) RefreshGuides();   // config.yaml changed from outside (a pull): the attachments folder follows
            foreach (string project in OpenSpecStoreApi.OpenRoots)
                try { In(project, () =>
                {
                    // Numbers can only collide when a change folder appeared, came from outside or moved: an ordinary edit of one item's files never does.
                    string stamp = WorkItemsApi.FolderStamp();
                    bool folders = ItemsCache.ScannedStamp(project) != stamp;
                    // One repair for every caller (an agent's tool call runs the same): the work items when a folder moved, then the notes, which can collide without any folder changing (a merge adds a file to a folder both sides had).
                    DuplicateRepairs repaired = NotesApi.RepairDuplicates(folders);
                    foreach (RenumberedItem item in repaired.Items)
                        Say($"#wi{item.OldId} was used twice: {item.Folder} is now #wi{item.NewId}", ToastKind.Warning);
                    if (folders) ItemsCache.Scanned(project, WorkItemsApi.FolderStamp());   // after the renumbering, which moved numbers
                    foreach ((int oldId, int newId, string path) in repaired.Notes.Select(n => (n.OldId, n.NewId, n.Path)))
                        Say($"#n{oldId} was used twice: {Path.GetFileName(Path.GetDirectoryName(Path.GetDirectoryName(path)))} has a note that is now #n{newId}", ToastKind.Warning);
                    renumbered |= repaired.Items.Count > 0 || repaired.Notes.Count > 0;
                }); }
                // Notify runs on the file watcher's timer and the pool: an error here would be thrown on a background thread and end the app.
                // Files moving under the read (a merge, an archive, Explorer) are the usual cause, and the next change refreshes again.
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                {
                    Say($"Couldn't read {Path.GetFileName(project)} just now ({e.Message}); it refreshes on the next change.", ToastKind.Warning);
                }
                // A file the readers did not foresee must never end the app (this also runs inside Open at startup): the board shows what it can.
                catch (Exception e)
                {
                    Say($"Couldn't check {Path.GetFileName(project)} for duplicate numbers ({e.Message}).", ToastKind.Warning);
                }
        }
        return renumbered;
    }

    /// <summary>A commit, checkout or staging made outside the app: pages re-read what git says (dots, last commit, contributors). No board file changed, so nothing is renumbered or synced.</summary>
    private void OnGitChanged() => Changed?.Invoke();

    /// <summary>An attachment file was saved or removed by the app: the connected storage's sync follows after the quiet wait, even when no board file changed.</summary>
    public void AttachmentsChanged() => sync?.AttachmentsChanged();

    /// <summary>The settings page is about to save another attachments folder (<paramref name="moved"/>: it moved the files itself): the sync service must not follow it as if a pull had changed it.</summary>
    public void AttachmentsFolderChangingHere(string repo, string dir, bool moved) => sync?.AttachmentsFolderChangingHere(repo, dir, moved);

    /// <summary>The project a new work item goes to: the only one, or the one the user picks (null when cancelled).</summary>
    public async Task<string?> PickProject(string title, UiService? on = null)
    {
        IReadOnlyList<string> projects = Projects;
        if (projects.Count <= 1) return Root;
        return await (on ?? ui).Choose(title, "", projects.Select(r => (r, Path.GetFileName(r))).ToList());
    }

    /// <summary>Creates "New work item" (assigned to me unless <paramref name="who"/> says otherwise) and refreshes every view.</summary>
    public (bool Ok, string Message, int Id) CreateItem(string status = "Backlog", string[]? features = null, string[]? who = null, string? project = null)
    {
        (bool ok, string message, int id) = project is null ? WorkItemsApi.CreateNew(status, features, who ?? [Me])
            : In(project, () => WorkItemsApi.CreateNew(status, features, who ?? [Me]));
        if (ok) Notify();
        return (ok, message, id);
    }

    public void Dispose()
    {
        OpenSpecStoreApi.Changed -= Notify;
        GitHistoryApi.Changed -= OnGitChanged;
        if (sync is not null) sync.Touched -= Notify;
    }
}
