using GitHistory;
using Microsoft.Extensions.Time.Testing;
using Notes;
using OpenSpecStore;
using TaskManager.AppState;
using WorkItems;
using WorkItems.PublicDTOs;

namespace TaskManager.Tests;

/// <summary>
/// All caching of the app lives in AppState/: one class per kind of data, each remembering the module versions it was read at and reading again only when one moves.
/// The modules keep nothing; what these tests count are the reads the modules were asked to make. Nothing waits on real time: the clock is fake, the file watcher is driven.
/// </summary>
public sealed class CachingInAppStateTests : IDisposable
{
    private readonly string settingsDir = Directory.CreateTempSubdirectory("rtm-appstate").FullName;

    public void Dispose() { try { Directory.Delete(settingsDir, true); } catch (IOException) { } }

    /// <summary>Adds <paramref name="count"/> change folders with a few files and a note each, so "the whole board" is many folders.</summary>
    private static void ManyFolders(TestRepo repo, int count)
    {
        for (int i = 0; i < count; i++)
        {
            string dir = $"openspec/changes/bulk-{i:D3}";
            repo.Write(dir + "/.openspec.yaml", $"schema: spec-driven\ncreated: 2026-09-22\nx-rtm:\n  id: {200 + i}\n  title: Bulk item {i}\n  status: Planned\n  priority: low\n");
            repo.Write(dir + "/proposal.md", $"## Why\n\nBulk item {i} exists so the board has many folders.\n\n## What Changes\n\n- something\n");
            repo.Write(dir + "/tasks.md", "# Tasks\n\n## 1. Work\n\n- [ ] 1.1 First\n- [ ] 1.2 Second\n");
            repo.Write(dir + $"/notes/n{1000 + i}.md", $"---\nid: {1000 + i}\nby: Test\ncreated: 2026-10-02T09:14:00\n---\n# Note {i}\n\nBody {i}.\n");
        }
    }

    /// <summary>Everything a screen asks the caches for in one go.</summary>
    private static void ReadEverything()
    {
        ItemsCache.All(true); ItemsCache.All(); ItemsCache.Get(107); ItemsCache.Events(107);
        NotesCache.All(); NotesCache.Get(123);
        FeaturesCache.Get();
        TestResultsCache.Get();
    }

    [Fact(DisplayName = "A second read of the board with nothing changed reads no file")]
    public void SecondReadReadsNoFile()
    {
        WorkItemsApi.ConfigureContributors(null);
        TestResultsCache.Clock = new FakeTimeProvider();   // never advanced: the scan for test results is reused
        using TestRepo repo = new();
        ManyFolders(repo, 20);
        OpenSpecStoreApi.Open(repo.Root);
        ReadEverything();
        (int loads, int folders, int parses, int noteLoads, int noteReads, int featureLoads, int scans, int parsedResults) =
            (ItemsCache.Loads, ItemsCache.FolderReads, WorkItemsModule.FolderParses, NotesCache.Loads, NotesModule.FolderReads, FeaturesCache.Loads, TestResultsCache.Scans, TestResultsCache.Parses);
        Assert.True(folders >= 20 && noteReads >= 20, "the first read reads every folder");

        ReadEverything();
        ReadEverything();

        Assert.Equal((loads, folders, parses, noteLoads, noteReads, featureLoads, scans, parsedResults),
            (ItemsCache.Loads, ItemsCache.FolderReads, WorkItemsModule.FolderParses, NotesCache.Loads, NotesModule.FolderReads, FeaturesCache.Loads, TestResultsCache.Scans, TestResultsCache.Parses));
    }

    [Fact(DisplayName = "An edit to one item re-reads that item's folder and no other")]
    public void EditReadsOneFolder()
    {
        WorkItemsApi.ConfigureContributors(null);
        using TestRepo repo = new();
        ManyFolders(repo, 30);
        Caches.S.Items.Clock = () => DateTime.UtcNow.AddMinutes(10);   // reads happen long after the files were written: none is "racy" (a folder written within milliseconds of a read is read once more, see ItemsCache.Folder.Racy)
        OpenSpecStoreApi.Open(repo.Root);
        IReadOnlyList<WorkItem> before = ItemsCache.All(true);
        NotesCache.All();
        (int folders, int parses, int noteReads) = (ItemsCache.FolderReads, WorkItemsModule.FolderParses, NotesModule.FolderReads);

        Assert.True(WorkItemsApi.SetTitle(107, "Renamed export").Success);
        Assert.True(NotesApi.SaveBody(1005, "# Note 5\n\nChanged body.\n").Success);
        IReadOnlyList<WorkItem> after = ItemsCache.All(true);
        IReadOnlyList<Notes.PublicDTOs.Note> notes = NotesCache.All();

        Assert.Equal(folders + 2, ItemsCache.FolderReads);   // the edited item's folder, and the one the note was written in
        Assert.Equal(parses + 2, WorkItemsModule.FolderParses);
        Assert.Equal(noteReads + 1, NotesModule.FolderReads);   // only the work item whose note changed
        Assert.Equal(before.Select(w => w.Folder), after.Select(w => w.Folder));
        foreach ((WorkItem was, WorkItem now) in before.Zip(after))
            if (was.Id == 107) { Assert.NotSame(was, now); Assert.Equal("Renamed export", now.Title); }
            else if (was.Folder == "bulk-005") Assert.NotSame(was, now);   // its notes/ folder is part of the change folder
            else Assert.Same(was, now);
        Assert.Contains("Changed body.", notes.Single(n => n.Id == 1005).Body);
    }

    [Fact(DisplayName = "A commit outside the app refreshes git-derived data once")]
    public void CommitOutsideRefreshesOnce()
    {
        string root = Directory.CreateTempSubdirectory("rtm-gitcache").FullName;
        FakeTimeProvider clock = new();
        GitHistoryModule.S.Clock = clock;
        GitHistoryModule.S.DrivenWatchers = true;
        TempGit.Init(root);
        Directory.CreateDirectory(Path.Combine(root, "openspec", "changes", "alpha"));
        File.WriteAllText(Path.Combine(root, "openspec", "changes", "alpha", "tasks.md"), "- [ ] one\n");
        TempGit.Commit(root, "2026-11-01T10:00:00+00:00", "Create alpha");
        GitHistoryApi.Configure(root);
        GitHistoryApi.Watch(root);
        static void AskEverything() { GitCache.LastCommitOf("alpha"); GitCache.AuthorsOfChange("alpha"); GitCache.Recent(5); GitCache.Mentioning(_ => true); GitCache.AllAuthors(); GitCache.ChangesOf("x"); }
        AskEverything();
        int first = GitCache.Reads;
        AskEverything(); AskEverything();
        Assert.Equal(first, GitCache.Reads);   // HEAD did not move: git was not asked again

        File.WriteAllText(Path.Combine(root, "openspec", "changes", "alpha", "tasks.md"), "- [x] one\n");
        TempGit.Commit(root, "2026-11-02T10:00:00+00:00", "Tick alpha");
        GitHistoryModule.States[Path.GetFullPath(root)].Watcher!.Raise("refs/heads/master");
        GitHistoryModule.States[Path.GetFullPath(root)].Watcher!.Raise("index");
        clock.Advance(GitWatcher.QuietWait);
        AskEverything(); AskEverything();

        Assert.Equal("Tick alpha", GitCache.LastCommitOf("alpha")!.Subject);
        Assert.Equal(first + 3, GitCache.Reads);   // the change history, the log and the people: each once, however often they were asked
        int again = GitCache.Reads;
        AskEverything();
        Assert.Equal(again, GitCache.Reads);
    }

    [Fact(DisplayName = "A reopened repository is not served from the old repository's cache")]
    public void ReopenedRepositoryIsNotServedFromTheOldCache()
    {
        WorkItemsApi.ConfigureContributors(null);
        using TestRepo repo = new();
        OpenSpecStoreApi.Open(repo.Root);
        Assert.Equal("Monthly remittance export", ItemsCache.Get(107)!.Title);
        string yaml = repo.PathOf("openspec/changes/export-remittances/.openspec.yaml");
        string text = File.ReadAllText(yaml);
        DateTime stamp = File.GetLastWriteTimeUtc(yaml);

        OpenSpecStoreApi.Close();   // the folder changes while it is closed (a pull, another tool), and opens again with its version back at 0
        File.WriteAllText(yaml, text.Replace("Monthly remittance export", "Monthly remittance expor7"));
        File.SetLastWriteTimeUtc(yaml, stamp);   // same size, same time: only the repository session can tell it is another read
        OpenSpecStoreApi.Open(repo.Root);

        Assert.Equal("Monthly remittance expor7", ItemsCache.Get(107)!.Title);
    }

    [Fact(DisplayName = "An equal reload keeps the same item instances so nothing redraws")]
    public void EqualReloadKeepsInstances()
    {
        WorkItemsApi.ConfigureContributors(null);
        using TestRepo repo = new();
        OpenSpecStoreApi.Open(repo.Root);
        BoardRead board = ItemsCache.Board();
        IReadOnlyList<WorkItem> visible = ItemsCache.All();
        IReadOnlyList<Features.PublicDTOs.Feature> features = FeaturesCache.Get();
        int folders = ItemsCache.FolderReads;

        string yaml = repo.PathOf("openspec/changes/add-health-check/.openspec.yaml");   // an item with a worklog: its modified time is the worklog's, not its files'
        File.WriteAllText(yaml, File.ReadAllText(yaml));   // saved again, nothing changed in it
        File.SetLastWriteTimeUtc(yaml, File.GetLastWriteTimeUtc(yaml).AddMinutes(5));
        OpenSpecStoreApi.Refresh();   // what the file watcher does when it notices an outside save

        BoardRead reloaded = ItemsCache.Board();
        Assert.Equal(folders + 1, ItemsCache.FolderReads);   // the folder was looked at and read again ...
        Assert.Same(board, reloaded);                         // ... and said the same: the very same board, items and all
        Assert.Same(visible, ItemsCache.All());
        Assert.Same(features, FeaturesCache.Get());
    }

    [Fact(DisplayName = "Caches of one test do not leak into another")]
    public void CachesAreIsolatedPerTest()
    {
        Assert.Equal((0, 0, 0, 0), (ItemsCache.Loads, NotesCache.Loads, FeaturesCache.Loads, GitCache.Reads));   // a test starts with nothing read by any other
        using TestRepo repo = new();
        OpenSpecStoreApi.Open(repo.Root);
        ReadEverything();
        Assert.True(ItemsCache.Loads > 0 && NotesCache.Loads > 0 && FeaturesCache.Loads > 0);

        CacheState mine = Caches.S;
        Caches.Isolated.Value = new();   // another test's caches, as far as this code can tell
        try
        {
            Assert.Equal((0, 0, 0), (ItemsCache.Loads, NotesCache.Loads, FeaturesCache.Loads));
            Assert.Equal("Monthly remittance export", ItemsCache.Get(107)!.Title);   // read again from the modules, not from the other set
            Assert.Equal(1, ItemsCache.Loads);
        }
        finally { Caches.Isolated.Value = mine; }
        Assert.True(ItemsCache.Loads > 1 || NotesCache.Loads > 0);
    }

    /// <summary>What a page asks for when it loads after a write: the project's statuses, lanes and warnings, the board, the features and the uncommitted set.</summary>
    private static void LoadAPage()
    {
        WorkItemsApi.GetStatuses(); WorkItemsApi.GetLanes(); WorkItemsApi.GetWarnings();
        ItemsCache.All(); FeaturesCache.Get(); GitCache.Uncommitted();
    }

    private static List<Action> QueueGitRefreshes()
    {
        List<Action> queued = [];
        Caches.S.Git.Background = work => { queued.Add(work); return Task.CompletedTask; };   // the thread-pool work, run by the test when it chooses
        return queued;
    }

    [Fact(DisplayName = "A status change reads config.yaml zero times and spawns no git process before the page renders")]
    public void StatusChangeReadsNoConfigAndSpawnsNoGit()
    {
        WorkItemsApi.ConfigureContributors(null);
        using TestRepo repo = new();
        TempGit.Init(repo.Root);
        TempGit.Commit(repo.Root, "2026-11-01T10:00:00+00:00", "Start");
        OpenSpecStoreApi.Open(repo.Root);
        GitHistoryApi.Configure(repo.Root);
        List<Action> refreshes = QueueGitRefreshes();
        LoadAPage();   // the page as first drawn
        (int configReads, int processes) = (OpenSpecStoreModule.ConfigReads, GitHistoryModule.Processes);
        Assert.True(configReads > 0 && processes > 0, "the first draw reads config.yaml and asks git");

        Assert.True(WorkItemsApi.SetStatus(107, "In Progress").Success);
        LoadAPage();   // what every page does when the write is reported
        LoadAPage();

        Assert.Equal(configReads, OpenSpecStoreModule.ConfigReads);
        Assert.Equal(processes, GitHistoryModule.Processes);
        Assert.Single(refreshes);   // git is asked after the frame, once, however many pages asked
    }

    [Fact(DisplayName = "A config.yaml write from outside is seen by the next GetStatuses")]
    public void OutsideConfigWriteIsSeen()
    {
        WorkItemsApi.ConfigureContributors(null);
        using TestRepo repo = new();
        OpenSpecStoreApi.Open(repo.Root);
        Assert.Equal(WorkItemsApi.Statuses, WorkItemsApi.GetStatuses());
        int reads = OpenSpecStoreModule.ConfigReads;
        WorkItemsApi.GetStatuses(); WorkItemsApi.GetLanes();
        Assert.Equal(reads, OpenSpecStoreModule.ConfigReads);   // read once, parsed once, however often it is asked

        string config = repo.PathOf("openspec/config.yaml");
        File.WriteAllText(config, File.ReadAllText(config).Replace("x-rtm:", "x-rtm:\n  statuses: [Backlog, Doing, In Progress, Completed]"));
        OpenSpecStoreApi.Refresh();   // what the file watcher does when it notices an outside save

        Assert.Equal(["Backlog", "Doing", "In Progress", "Completed"], WorkItemsApi.GetStatuses());
        Assert.Equal(reads + 1, OpenSpecStoreModule.ConfigReads);
        Assert.Equal(["High priority", "Lower priority"], WorkItemsApi.GetLanes()["In Progress"]);   // the other sections come from the new text too
    }

    [Fact(DisplayName = "An item write leaves FeaturesCache untouched")]
    public void ItemWriteLeavesFeaturesAlone()
    {
        WorkItemsApi.ConfigureContributors(null);
        using TestRepo repo = new();
        OpenSpecStoreApi.Open(repo.Root);
        IReadOnlyList<Features.PublicDTOs.Feature> features = FeaturesCache.Get();
        int loads = FeaturesCache.Loads;

        Assert.True(WorkItemsApi.SetTitle(107, "Renamed export").Success);
        Assert.True(WorkItemsApi.SetStatus(107, "In Progress").Success);

        Assert.Same(features, FeaturesCache.Get());
        Assert.Equal(loads, FeaturesCache.Loads);

        Directory.CreateDirectory(repo.PathOf("openspec/specs/billing/invoices"));   // a capability appears under specs/
        File.WriteAllText(repo.PathOf("openspec/specs/billing/invoices/spec.md"), "# invoices\n");
        OpenSpecStoreApi.Refresh();
        IReadOnlyList<Features.PublicDTOs.Feature> after = FeaturesCache.Get();
        Assert.Equal(loads + 1, FeaturesCache.Loads);   // a new capability under specs/ is read
        Assert.Contains(after, f => f.Path == "billing/invoices");
    }

    [Fact(DisplayName = "Uncommitted returns the last set at once and raises Changed when the refreshed set differs")]
    public void UncommittedReturnsTheLastSetAndRefreshesInTheBackground()
    {
        string root = Directory.CreateTempSubdirectory("rtm-dirty").FullName;
        TempGit.Init(root);
        Directory.CreateDirectory(Path.Combine(root, "openspec", "changes", "alpha"));
        string tasks = Path.Combine(root, "openspec", "changes", "alpha", "tasks.md");
        File.WriteAllText(tasks, "- [ ] one\n");
        TempGit.Commit(root, "2026-11-01T10:00:00+00:00", "Create alpha");
        GitHistoryApi.Configure(root);
        List<Action> refreshes = QueueGitRefreshes();
        int changed = 0;
        void OnChanged() => changed++;
        GitHistoryApi.Changed += OnChanged;
        try
        {
            IReadOnlySet<string> first = GitCache.Uncommitted(1);   // nothing known yet: git is asked now
            Assert.Empty(first);
            Assert.Empty(refreshes);

            File.WriteAllText(tasks, "- [x] one\n");
            int processes = GitHistoryModule.Processes;
            Assert.Same(first, GitCache.Uncommitted(2));   // the version moved: the last set comes back at once ...
            Assert.Same(first, GitCache.Uncommitted(2));
            Assert.Equal(processes, GitHistoryModule.Processes);   // ... with no git process on the caller's thread
            Assert.Single(refreshes);   // one refresh at a time
            Assert.Equal(0, changed);

            refreshes.Single()();   // the thread-pool work
            Assert.Equal(1, changed);   // the set differs: the page reloads
            Assert.Equal(["alpha"], GitCache.Uncommitted(2));
            Assert.Equal(processes + 1, GitHistoryModule.Processes);

            Assert.Equal(["alpha"], GitCache.Uncommitted(3));   // moved again, the set is the same after the refresh: nobody is told
            refreshes[1]();
            Assert.Equal(1, changed);
        }
        finally { GitHistoryApi.Changed -= OnChanged; }
    }
}
