using Features;
using GitHistory;
using Notes;
using OpenSpecStore;
using TaskManager.Services;
using WorkItems;

namespace TaskManager.Tests;

/// <summary>
/// The UI asks the modules for the same data many times per click. Each module parses the repo once and serves
/// later reads from memory until something changes: the app's own write, an outside edit, or a repo switch.
/// </summary>
[Collection(nameof(ModuleStateCollection))]
public sealed class CachingTheRepoTests
{
    [Fact(DisplayName = "Reading work items repeatedly parses the repo only once")]
    public void WorkItemsReadOnce()
    {
        using TestRepo repo = new();
        OpenSpecStoreApi.Open(repo.Root);
        int before = WorkItemsModule.Loads;

        WorkItemsApi.GetAll(); WorkItemsApi.GetAll(); WorkItemsApi.Get(123); WorkItemsApi.GetWarnings(); WorkItemsApi.NextId();

        Assert.Equal(before + 1, WorkItemsModule.Loads);
    }

    [Fact(DisplayName = "The app's own edit is visible on the very next read")]
    public void OwnWriteInvalidates()
    {
        using TestRepo repo = new();
        OpenSpecStoreApi.Open(repo.Root);
        Assert.Equal("In Progress", WorkItemsApi.Get(123)!.Status);

        WorkItemsApi.SetStatus(123, "Blocked");

        Assert.Equal("Blocked", WorkItemsApi.Get(123)!.Status);
    }

    [Fact(DisplayName = "An edit made in another editor shows up once the change is noticed")]
    public async Task OutsideEditInvalidates()
    {
        using TestRepo repo = new();
        OpenSpecStoreApi.Open(repo.Root);
        Assert.Equal("Planned", WorkItemsApi.Get(107)!.Status);
        TaskCompletionSource noticed = new();
        void Handler() => noticed.TrySetResult();
        OpenSpecStoreApi.Changed += Handler;
        try
        {
            repo.Write("openspec/changes/export-remittances/.openspec.yaml", "schema: spec-driven\nx-rtm:\n  id: 107\n  status: Blocked\n");
            await Task.WhenAny(noticed.Task, Task.Delay(3000));
            Assert.Equal("Blocked", WorkItemsApi.Get(107)!.Status);
        }
        finally { OpenSpecStoreApi.Changed -= Handler; }
    }

    [Fact(DisplayName = "Features and notes are also parsed once until something changes")]
    public void FeaturesAndNotesReadOnce()
    {
        using TestRepo repo = new();
        OpenSpecStoreApi.Open(repo.Root);
        int f = FeaturesModule.Loads, n = NotesModule.Loads;

        FeaturesApi.GetAll(); FeaturesApi.GetAll();
        NotesApi.GetAll(); NotesApi.Get(123); NotesApi.ResolveLinks("#n123 #wi107 [[runbook/uptime]]");

        Assert.Equal((f + 1, n + 1), (FeaturesModule.Loads, NotesModule.Loads));
        NotesApi.SaveBody(123, "# Ping test\n\nchanged\n");
        Assert.Equal("# Ping test\n\nchanged\n", NotesApi.Get(123)!.Body);
    }

    [Fact(DisplayName = "Switching to another repository never shows the previous repo's data")]
    public void RepoSwitchInvalidates()
    {
        using TestRepo first = new();
        using TestRepo second = new();
        second.Write("openspec/changes/export-remittances/.openspec.yaml", "schema: spec-driven\nx-rtm:\n  id: 107\n  title: Second repo item\n");
        OpenSpecStoreApi.Open(first.Root);
        Assert.Equal("Monthly remittance export", WorkItemsApi.Get(107)!.Title);

        OpenSpecStoreApi.Open(second.Root);

        Assert.Equal("Second repo item", WorkItemsApi.Get(107)!.Title);
    }

    [Fact(DisplayName = "Whether the folder is a git repository is checked once, not on every read")]
    public void GitRepoCheckedOnce()
    {
        string dir = Directory.CreateTempSubdirectory("rtm-nogit").FullName;
        GitHistoryApi.Configure(dir);
        int before = GitHistoryModule.RepoChecks;

        GitHistoryApi.IsRepository(); GitHistoryApi.Mentioning("#wi1"); GitHistoryApi.AuthorsOfChange("x"); GitHistoryApi.IsRepository();

        Assert.Equal(before + 1, GitHistoryModule.RepoChecks);
    }

    [Fact(DisplayName = "The git user name is looked up once per repository")]
    public void UserNameLookedUpOnce()
    {
        string dir = Directory.CreateTempSubdirectory("rtm-nogit").FullName;
        GitHistoryApi.Configure(dir);
        int before = GitHistoryModule.UserNameLookups;

        GitHistoryApi.UserName(); GitHistoryApi.UserName(); GitHistoryApi.UserName();

        Assert.Equal(before + 1, GitHistoryModule.UserNameLookups);
    }

    /// <summary>Adds <paramref name="count"/> change folders with a few files and a note each, so "the whole board" is many folders.</summary>
    private static void ManyFolders(TestRepo repo, int count)
    {
        for (int i = 0; i < count; i++)
        {
            string dir = $"openspec/changes/bulk-{i:D3}";
            repo.Write(dir + "/.openspec.yaml", $"schema: spec-driven\ncreated: 2026-09-22\nx-rtm:\n  id: {200 + i}\n  title: Bulk item {i}\n  status: Planned\n  priority: low\n");
            repo.Write(dir + "/proposal.md", $"## Why\n\nBulk item {i} exists so the board has many folders.\n\n## What Changes\n\n- something\n");
            repo.Write(dir + "/tasks.md", "# Tasks\n\n## 1. Work\n\n- [ ] 1.1 First\n- [ ] 1.2 Second\n");
            repo.Write(dir + $"/specs/functional/bulk{i:D3}/spec.md", $"## ADDED Requirements\n\n### Requirement: Bulk {i}\n\nThe system SHALL exist.\n\n#### Scenario: Exists\n\n- **WHEN** asked\n- **THEN** it exists\n");
            repo.Write(dir + $"/notes/n{1000 + i}.md", $"---\nid: {1000 + i}\nby: Test\ncreated: 2026-10-02T09:14:00\n---\n# Note {i}\n\nBody {i}.\n");
        }
    }

    [Fact(DisplayName = "After one work item changes, only that item's folder is parsed again; the others keep their objects")]
    public void OnlyTheChangedFolderIsParsedAgain()
    {
        WorkItemsApi.ConfigureContributors(null);
        using TestRepo repo = new();
        ManyFolders(repo, 30);
        OpenSpecStoreApi.Open(repo.Root);
        IReadOnlyList<WorkItems.PublicDTOs.WorkItem> before = WorkItemsApi.GetAll(true);
        int parses = WorkItemsModule.FolderParses;

        Assert.True(WorkItemsApi.SetTitle(107, "Renamed export").Success);
        IReadOnlyList<WorkItems.PublicDTOs.WorkItem> after = WorkItemsApi.GetAll(true);

        Assert.Equal(parses + 1, WorkItemsModule.FolderParses);
        Assert.Equal(before.Select(w => w.Folder), after.Select(w => w.Folder));
        foreach ((WorkItems.PublicDTOs.WorkItem was, WorkItems.PublicDTOs.WorkItem now) in before.Zip(after))
            if (was.Id == 107) { Assert.NotSame(was, now); Assert.Equal("Renamed export", now.Title); }
            else Assert.Same(was, now);
    }

    [Fact(DisplayName = "After one note changes, only its work item's notes are read again")]
    public void OnlyTheChangedNotesFolderIsReadAgain()
    {
        using TestRepo repo = new();
        ManyFolders(repo, 30);
        OpenSpecStoreApi.Open(repo.Root);
        IReadOnlyList<Notes.PublicDTOs.Note> before = NotesApi.GetAll();
        int reads = NotesModule.FolderReads;

        Assert.True(NotesApi.SaveBody(1005, "# Note 5\n\nChanged body.\n").Success);
        IReadOnlyList<Notes.PublicDTOs.Note> after = NotesApi.GetAll();

        Assert.Equal(reads + 1, NotesModule.FolderReads);
        Assert.Equal(before.Count, after.Count);
        Assert.Contains("Changed body.", after.Single(n => n.Id == 1005).Body);
        foreach (Notes.PublicDTOs.Note note in after.Where(n => n.Id != 1005))
            Assert.Same(before.Single(n => n.Id == note.Id), note);
    }

    [Fact(DisplayName = "A write command does not re-read the whole board before writing")]
    public void AWriteDoesNotRebuildTheBoard()
    {
        WorkItemsApi.ConfigureContributors(null);
        using TestRepo repo = new();
        ManyFolders(repo, 30);
        OpenSpecStoreApi.Open(repo.Root);
        string number = WorkItemsApi.Get(207)!.Subtasks.First().Number;
        Assert.True(WorkItemsApi.SetTitle(205, "Earlier edit").Success);   // leaves the version bumped, as the UI's previous click would
        int parses = WorkItemsModule.FolderParses;
        long version = OpenSpecStoreApi.Version;

        Assert.True(WorkItemsApi.SetSubtaskDone(207, number, true).Success);

        Assert.True(WorkItemsModule.FolderParses - parses <= 3, $"a command parsed {WorkItemsModule.FolderParses - parses} folders; it should touch its own and the one edited before");
        Assert.True(WorkItemsApi.Get(207)!.Subtasks.First().Done);
        Assert.True(OpenSpecStoreApi.Version - version <= 2, "one tasks.md write and one events.log line: one bump each");
    }

    [Fact(DisplayName = "One file write bumps the content version exactly once")]
    public void OneWriteOneBump()
    {
        using TestRepo repo = new();
        OpenSpecStoreApi.Open(repo.Root);
        long version = OpenSpecStoreApi.Version;

        Assert.True(OpenSpecStoreApi.WriteText(repo.PathOf("openspec/changes/plain-change/extra.md"), "x").Success);

        Assert.Equal(version + 1, OpenSpecStoreApi.Version);
    }

    [Fact(DisplayName = "The duplicate-id scan runs when a change folder appears, not after an ordinary edit")]
    public void DuplicateScanOnlyWhenFoldersChange()
    {
        WorkItemsApi.ConfigureContributors(null);
        using TestRepo repo = new();
        string settingsFile = Path.Combine(Path.GetTempPath(), "rtm-ui-" + Guid.NewGuid().ToString("N") + ".json");
        using Workspace workspace = new(new SettingsStore(settingsFile), new UiService());
        Assert.True(workspace.Open(repo.Root).Success);
        int scans = WorkItemsModule.DuplicateScans;
        Assert.True(scans >= 1, "opening a project scans it once");

        workspace.Do(WorkItemsApi.SetTitle(107, "Ordinary edit"));
        workspace.Do(WorkItemsApi.SetStatus(107, "Blocked"));
        workspace.Notify();
        Assert.Equal(scans, WorkItemsModule.DuplicateScans);

        repo.Write("openspec/changes/copied-in/.openspec.yaml", "schema: spec-driven\ncreated: 2026-10-30\nx-rtm:\n  id: 107\n  title: Copied in\n");
        repo.Write("openspec/changes/copied-in/proposal.md", "## Why\n\ncopied\n");
        OpenSpecStoreApi.Refresh();
        workspace.Notify();

        Assert.Equal(scans + 1, WorkItemsModule.DuplicateScans);
        Assert.Contains(WorkItemsApi.GetAll(true), w => w.Folder == "copied-in" && w.Id != 107);   // and the scan did its job
        try { File.Delete(settingsFile); } catch (IOException) { }
    }

    [Fact(DisplayName = "A folder whose files did not change keeps its revision; a changed folder gets a new one")]
    public void RevisionOnlyChangesWithTheFolder()
    {
        WorkItemsApi.ConfigureContributors(null);
        using TestRepo repo = new();
        OpenSpecStoreApi.Open(repo.Root);
        Dictionary<int, string> before = WorkItemsApi.GetAll(true).ToDictionary(w => w.Id, w => w.Revision);
        Assert.All(before.Values, r => Assert.Equal(12, r.Length));

        WorkItemsApi.SetTitle(107, "AAAA");
        string first = WorkItemsApi.Get(107)!.Revision;
        WorkItemsApi.SetTitle(107, "BBBB");   // same length, within the clock's tick: the revision still moves
        string second = WorkItemsApi.Get(107)!.Revision;

        Assert.NotEqual(before[107], first);
        Assert.NotEqual(first, second);
        foreach (WorkItems.PublicDTOs.WorkItem w in WorkItemsApi.GetAll(true).Where(w => w.Id != 107 && before.ContainsKey(w.Id)))
            Assert.Equal(before[w.Id], w.Revision);
    }

    [Fact(DisplayName = "Re-reading the board after one edit takes a small fraction of a cold load")]
    public void ReReadIsMuchCheaperThanColdLoad()
    {
        WorkItemsApi.ConfigureContributors(null);
        using TestRepo repo = new();
        ManyFolders(repo, 60);
        OpenSpecStoreApi.Open(repo.Root);
        WorkItemsApi.GetAll(true); NotesApi.GetAll();   // warm up the JIT
        // The bar is the work done, not the time it takes (a loaded machine stretches both): folders parsed by a cold load against by a re-read.
        // The milliseconds are only printed.
        System.Diagnostics.Stopwatch sw = System.Diagnostics.Stopwatch.StartNew();
        WorkItemsModule.Cache.Clear(); NotesModule.Cache.Clear();
        int parses = WorkItemsModule.FolderParses;
        WorkItemsApi.GetAll(true); NotesApi.GetAll();
        double cold = sw.Elapsed.TotalMilliseconds;
        int coldParses = WorkItemsModule.FolderParses - parses;

        int rereadParses = 0;
        double reread = 0;
        for (int i = 0; i < 5; i++)
        {
            parses = WorkItemsModule.FolderParses;
            WorkItemsApi.SetTitle(210, "Edit " + i);
            sw.Restart();
            WorkItemsApi.GetAll(true); NotesApi.GetAll();
            reread = sw.Elapsed.TotalMilliseconds;
            rereadParses = Math.Max(rereadParses, WorkItemsModule.FolderParses - parses);
        }

        Console.WriteLine($"cold load {cold:F1} ms ({coldParses} folders parsed), re-read after one edit {reread:F1} ms ({rereadParses} folders parsed)");
        Assert.True(coldParses >= 60, $"a cold load parsed only {coldParses} folders");
        Assert.True(rereadParses <= 2, $"a re-read after one edit parsed {rereadParses} folders; the cold load parsed {coldParses}");
    }
}
