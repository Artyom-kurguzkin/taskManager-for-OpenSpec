using Features;
using GitHistory;
using Notes;
using OpenSpecStore;
using WorkItems;

namespace TaskManager.Services;

/// <summary>
/// Composition-root glue for the UI: opens a repository (configuring every module), and tells pages to
/// re-read when files change on disk or after the app's own edits.
/// </summary>
public sealed class Workspace : IDisposable
{
    private readonly SettingsStore settings;
    private readonly UiService ui;
    private readonly BoardSyncService? sync;   // null where no storage can be connected (tests that do not register it)
    private bool resolving;
    private readonly Dictionary<string, string> scanned = new(StringComparer.OrdinalIgnoreCase);   // per project: the WorkItemsApi.FolderStamp the last duplicate-number scan saw
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
        this.sync = sync;
        if (sync is not null) { sync.InProject = In; sync.Touched += Notify; }
        OpenSpecStoreApi.Changed += Notify;
        plansSeen = PlansState;
        settings.Changed += RefreshGuides;
    }

    private string plansSeen;
    /// <summary>What the guides and the Notes module are configured from: the plans folders (this computer's) and every open project's attachments folder (the project's own setting, which a pull can change).</summary>
    private string PlansState => string.Join(";", (settings.Current.PlansDirs ?? []).Select(kv => kv.Key + "=" + kv.Value)) + "|" + string.Join(";", ConfiguredRoots().Select(r => r + "=" + settings.AttachmentsDir(r)));

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
        if (TakeGuideNotice() is string notice) ui.Toast(notice);
    }

    /// <summary>Raised on any change; components re-read through the module Apis and re-render.</summary>
    public event Action? Changed;

    /// <summary>The active repository: the one pages read and write, and the side menu names.</summary>
    public string? Root => OpenSpecStoreApi.RepoRoot;

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
        if (message.Length > 0) guideNotice = message;   // openspec/ was just set up here: said together with the guide notice
        scanned.Remove(Path.GetFullPath(root));   // a project being opened is scanned for duplicate numbers once
        GitHistoryApi.Configure(root, reset: fresh);
        WorkItemsApi.ConfigureContributors(() => GitHistoryApi.IsRepository() ? (GitHistoryApi.HeadToken(), GitHistoryApi.AuthorsOfChange) : null);
        WorkItemsApi.ConfigureActor(() => Me);   // the worklog's events say the user did it
        settings.Update(s => s with { LastRepo = OpenSpecStoreApi.RepoRoot });
        ConfigureProject(root);
        TrackWorklog(root);
        sync?.Attach(root);
        OpenTabs(root);
        KeepTabs();
        Notify();
        return (true, "");
    }

    /// <summary>The other tabs open in the store too (the board shows them all); one whose folder is gone is skipped.</summary>
    private void OpenTabs(string active)
    {
        foreach (string tab in OpenRepos.Where(t => !OpenSpecStoreApi.OpenRoots.Any(o => SettingsStore.Key(o) == SettingsStore.Key(t))))
            if (OpenSpecStoreApi.Open(tab, keepOthers: true).Success) { GitHistoryApi.Configure(tab); ConfigureProject(tab); TrackWorklog(tab); sync?.Attach(tab); }
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

    /// <summary>Reads or writes another open repository, then comes back to the active one.</summary>
    public T In<T>(string root, Func<T> work)
    {
        string? active = Root;
        if (active is not null && SettingsStore.Key(active) == SettingsStore.Key(root)) return work();
        if (!OpenSpecStoreApi.Use(root)) throw new InvalidOperationException($"{root} is not open");
        GitHistoryApi.Configure(root);
        try { return work(); }
        finally { if (active is not null) { OpenSpecStoreApi.Use(active); GitHistoryApi.Configure(active); } }
    }

    public void In(string root, Action work) => In(root, () => { work(); return 0; });

    /// <summary>Repositories open as tabs; falls back to just the active one once settings have any value at all.</summary>
    public IReadOnlyList<string> OpenRepos =>
        settings.Current.OpenRepos is { Count: > 0 } list ? list : (Root is string r ? [r] : []);

    /// <summary>Opens a repository as a new tab (or switches to it if already open) and makes it active.</summary>
    public (bool Success, string Message) OpenTab(string root)
    {
        List<string> before = OpenRepos.ToList();   // snapshot before Open() switches Root, which OpenRepos falls back on
        settings.Update(s => s with { OpenRepos = [.. before.Where(p => SettingsStore.Key(p) != SettingsStore.Key(root)), root] });   // first, so Open keeps every tab
        (bool ok, string message) = Open(root);
        if (!ok) settings.Update(s => s with { OpenRepos = before });
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
        settings.Update(s => s with { OpenRepos = remaining });
        if (Root is string r && SettingsStore.Key(r) == SettingsStore.Key(repo)) Open(remaining[^1]);
        else KeepTabs();
        Notify();
    }

    /// <summary>Who the app writes as: the name in Settings, else git user.name, else the OS user.</summary>
    public string Me => settings.Current.Name ?? GitHistoryApi.UserName() ?? Environment.UserName;

    public IReadOnlyDictionary<string, string> FeatureNames => FeaturesApi.GetAll().ToDictionary(f => f.Path, f => f.Name);

    /// <summary>Runs a module command and refreshes the UI when it succeeds.</summary>
    public (bool Success, string Message) Do((bool Success, string Message) result)
    {
        if (result.Success) Notify();
        return result;
    }

    /// <summary>
    /// Any change (an edit here, a file from outside, a folder copied in) first resolves numbers used twice, so a duplicate
    /// never reaches a page: the later change is renumbered, told in a toast, and gets a note saying what happened.
    /// </summary>
    public void Notify()
    {
        if (PlansState != plansSeen) RefreshGuides();   // config.yaml changed from outside (a pull): the attachments folder follows
        if (!resolving)   // the writes below raise Changed again
        {
            resolving = true;
            try
            {
                foreach (string project in OpenSpecStoreApi.OpenRoots)
                    try { In(project, () =>
                    {
                        // Numbers can only collide when a change folder appeared, came from outside or moved: an ordinary edit of one item's files never does.
                        string stamp = WorkItemsApi.FolderStamp();
                        if (!scanned.TryGetValue(project, out string? seen) || seen != stamp)
                        {
                            foreach ((int oldId, int newId, string folder) in WorkItemsApi.ResolveDuplicateIds())
                            {
                                (bool ok, string _, int noteId) = NotesApi.Create(newId, "App");
                                if (ok && NotesApi.SetTitle(noteId, $"Renumbered from #wi{oldId}").Success) NotesApi.SaveBody(noteId, $"Two changes carried the number #wi{oldId}. This one ({folder}) was created later, so it is now #wi{newId}. Anything that refers to #wi{oldId} means the other change.\n");
                                ui.Toast($"#wi{oldId} was used twice: {folder} is now #wi{newId}", ToastKind.Warning);
                            }
                            scanned[project] = WorkItemsApi.FolderStamp();   // after the renumbering, which moved numbers
                        }
                        // A note can come in under a number another one has without any folder changing (a merge adds a file to a folder both sides had): after the work items, whose numbers the notes belong to.
                        foreach ((int oldId, int newId, string path) in NotesApi.RepairDuplicateIds())
                            ui.Toast($"#n{oldId} was used twice: {Path.GetFileName(Path.GetDirectoryName(Path.GetDirectoryName(path)))} has a note that is now #n{newId}", ToastKind.Warning);
                    }); }
                    // Notify runs on the file watcher's timer: an error here would be thrown on a background thread and end the app.
                    // Files moving under the read (a merge, an archive, Explorer) are the usual cause, and the next change refreshes again.
                    catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                    {
                        ui.Toast($"Couldn't read {Path.GetFileName(project)} just now ({e.Message}); it refreshes on the next change.", ToastKind.Warning);
                    }
                    // A file the readers did not foresee must never end the app (this also runs inside Open at startup): the board shows what it can.
                    catch (Exception e)
                    {
                        ui.Toast($"Couldn't check {Path.GetFileName(project)} for duplicate numbers ({e.Message}).", ToastKind.Warning);
                    }
            }
            finally { resolving = false; }
        }
        sync?.BoardChanged();   // the one place the board's content is reported changing: connected projects sync two quiet minutes after
        Changed?.Invoke();
    }

    /// <summary>An attachment file was saved or removed by the app: the connected storage's sync follows after the quiet wait, even when no board file changed.</summary>
    public void AttachmentsChanged() => sync?.AttachmentsChanged();

    /// <summary>The settings page is about to save another attachments folder (<paramref name="moved"/>: it moved the files itself): the sync service must not follow it as if a pull had changed it.</summary>
    public void AttachmentsFolderChangingHere(string repo, string dir, bool moved) => sync?.AttachmentsFolderChangingHere(repo, dir, moved);

    /// <summary>The project a new work item goes to: the only one, or the one the user picks (null when cancelled).</summary>
    public async Task<string?> PickProject(string title)
    {
        IReadOnlyList<string> projects = Projects;
        if (projects.Count <= 1) return Root;
        return await ui.Choose(title, "", projects.Select(r => (r, Path.GetFileName(r))).ToList());
    }

    /// <summary>Creates "New work item" (assigned to me unless <paramref name="who"/> says otherwise) and refreshes every view.</summary>
    public (bool Ok, string Message, int Id) CreateItem(string status = "Backlog", string[]? features = null, string[]? who = null, string? project = null)
    {
        (bool ok, string message, int id) = project is null ? WorkItemsApi.Create("New work item", status, features, who ?? [Me])
            : In(project, () => WorkItemsApi.Create("New work item", status, features, who ?? [Me]));
        if (ok) Notify();
        return (ok, message, id);
    }

    public void Dispose()
    {
        OpenSpecStoreApi.Changed -= Notify;
        if (sync is not null) sync.Touched -= Notify;
    }
}
