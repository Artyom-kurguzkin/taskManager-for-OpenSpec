using System.Diagnostics;
using System.Globalization;
using BoardSync.PublicDTOs;
using GitHistory;
using OpenSpecStore;
using TaskManager.Services;
using WorkItems;
using WorkItems.PublicDTOs;

namespace TaskManager.Tests;

/// <summary>
/// A git merge that both branches touched leaves conflict markers in a board file, and it can be committed that way. The sync used to pause until someone edited the file by hand;
/// now the app rebuilds the file itself: the two sides come from git (the merge commit's parents, or HEAD and MERGE_HEAD while the merge is open, and their merge base), the board's
/// own field-wise merge runs over them, and a field both sides changed goes to the side whose commit is newer. Real git runs in temporary repositories, with fixed commit dates.
/// </summary>
public sealed class RepairingMergeMarkersTests : IDisposable
{
    private const string Item = "openspec/changes/add-health-check";
    private const string Yaml = Item + "/.openspec.yaml";
    private const string Tasks = Item + "/tasks.md";
    private const string Proposal = Item + "/proposal.md";
    private const int ItemId = 123;

    private readonly string temp = Directory.CreateTempSubdirectory("rtm-markers-").FullName;
    private readonly TestRepo repo = new();
    private readonly UiService ui = new();
    private readonly BoardStorageSettings settings;
    private readonly BoardSyncService sync;

    public RepairingMergeMarkersTests()
    {
        OpenSpecStoreApi.Open(repo.Root, keepOthers: true);
        settings = new BoardStorageSettings(Path.Combine(temp, "data"), StorageProviders.Builtin);
        sync = new BoardSyncService(settings, ui, new SyncSetup(TimeSpan.FromMinutes(2)), new SettingsStore(Path.Combine(temp, "app.json")));
        sync.InProject = In;
        sync.Attach(repo.Root);
        Assert.True(settings.Connect(repo.Root, "folder").Success);
        Assert.True(settings.SetValue(repo.Root, "path", Path.Combine(temp, "storage")).Success);
    }

    public void Dispose()
    {
        sync.Dispose();
        repo.Dispose();
        try { Directory.Delete(temp, true); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }

    private static void In(string root, Action work)
    {
        OpenSpecStoreApi.Use(root);
        GitHistoryApi.Configure(root);
        work();
    }

    // ── git, with fixed dates ──

    private static DateTime Day(int d) => new(2026, 11, d, 10, 0, 0, DateTimeKind.Utc);

    private (int Exit, string Output) Git(DateTime? at, params string[] args)
    {
        ProcessStartInfo psi = new("git") { WorkingDirectory = repo.Root, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (string a in new[] { "-c", "user.name=Tester", "-c", "user.email=t@example.com", "-c", "commit.gpgsign=false", "-c", "core.autocrlf=false", "-c", "core.safecrlf=false" }.Concat(args)) psi.ArgumentList.Add(a);
        if (at is DateTime d) psi.Environment["GIT_AUTHOR_DATE"] = psi.Environment["GIT_COMMITTER_DATE"] = d.ToString("s", CultureInfo.InvariantCulture) + "Z";
        using Process p = Process.Start(psi)!;
        Task<string> err = p.StandardError.ReadToEndAsync();
        string output = p.StandardOutput.ReadToEnd();
        p.WaitForExit();
        err.Wait();
        return (p.ExitCode, output);
    }

    private void Commit(string message, DateTime at)
    {
        Git(null, "add", "-A");
        Git(at, "commit", "-q", "--allow-empty", "-m", message);
    }

    private string Head => Git(null, "rev-parse", "HEAD").Output.Trim();

    /// <summary>A repository whose first commit is the board as it is now, with the item's yaml replaced by <paramref name="baseYaml"/>; the storage has seen the board before git came in.</summary>
    private async Task StartRepository(string baseYaml, string? baseTasks = null)
    {
        await sync.LastPass(repo.Root);                                        // the connect already synced the untouched board
        repo.Write(Yaml, baseYaml);
        if (baseTasks is not null) repo.Write(Tasks, baseTasks);
        Git(null, "init", "-q", "-b", "main");
        GitHistoryApi.Configure(repo.Root, reset: true);
        Commit("base", Day(1));
        await sync.SyncNow(repo.Root);                                         // the storage holds the base: what git merges next is a real change on top of it
    }

    /// <summary>"other" (theirs) and "main" (ours) each change <paramref name="file"/> from the base, theirs committed on <paramref name="theirsDay"/>, ours on <paramref name="oursDay"/>; then git merges other into main.</summary>
    private void MergeOther(string file, string oursText, string theirsText, int oursDay, int theirsDay)
    {
        Git(null, "checkout", "-q", "-b", "other");
        repo.Write(file, theirsText);
        Commit("their change", Day(theirsDay));
        Git(null, "checkout", "-q", "main");
        repo.Write(file, oursText);
        Commit("our change", Day(oursDay));
        Assert.Equal(1, Git(Day(20), "merge", "-q", "--no-edit", "other").Exit);          // a conflict: git wrote the markers
        Assert.Contains("<<<<<<<", repo.Read(file));
    }

    private void CommitTheMergeWithItsMarkers() => Git(Day(21), "commit", "-q", "--no-edit", "-a");

    private static string YamlWith(string status, string priority, string contributors = "[Artyom, Tom]") =>
        $"schema: spec-driven\ncreated: 2026-09-12\n# keep this comment\nx-rtm:\n  id: {ItemId}\n  title: Health-check endpoint\n  status: {status}\n  priority: {priority}\n  contributors: {contributors}\n  features: [non-functional/security]\n";

    private static IEnumerable<WorkItemEvent> Conflicts() => WorkItemsApi.GetEvents(ItemId).Where(e => e.Kind == "sync-conflict");

    private static void NoMarkers(string text)
    {
        Assert.DoesNotContain("<<<<<<<", text);
        Assert.DoesNotContain("=======", text);
        Assert.DoesNotContain(">>>>>>>", text);
    }

    // ── committed markers ──

    [Fact(DisplayName = "Conflict markers committed in a work item's yaml are repaired from the merge's two parents and the newer side wins each field")]
    public async Task CommittedMarkersAreRepairedAndTheNewerSideWinsEachField()
    {
        await StartRepository(YamlWith("In Progress", "high"));
        MergeOther(Yaml, oursText: YamlWith("Blocked", "low"), theirsText: YamlWith("Planned", "high"), oursDay: 5, theirsDay: 7);       // theirs is the later commit
        CommitTheMergeWithItsMarkers();

        SyncReport? report = await sync.SyncNow(repo.Root);

        string yaml = repo.Read(Yaml);
        NoMarkers(yaml);
        Assert.Contains("  status: Planned\n", yaml);                  // both changed it: theirs is newer
        Assert.Contains("  priority: low\n", yaml);                    // only ours changed it: kept, though ours is the older commit
        Assert.Contains("# keep this comment", yaml);
        Assert.Equal(SyncOutcome.Synced, report!.Outcome);
        Assert.DoesNotContain("Paused", sync.Status(repo.Root).Line);
    }

    [Fact(DisplayName = "The real case: HEAD's Blocked and high, committed later, beat Planned and low and no marker line is left")]
    public async Task TheRealCaseKeepsTheLaterCommitsValues()
    {
        await StartRepository(YamlWith("In Progress", "low"));
        MergeOther(Yaml, oursText: YamlWith("Blocked", "high"), theirsText: YamlWith("Planned", "low"), oursDay: 9, theirsDay: 3);        // HEAD (ours) is the later commit
        CommitTheMergeWithItsMarkers();

        await sync.SyncNow(repo.Root);

        string yaml = repo.Read(Yaml);
        NoMarkers(yaml);
        Assert.Contains("  status: Blocked\n  priority: high\n", yaml);
        Assert.DoesNotContain("Planned", yaml);
    }

    [Fact(DisplayName = "A field changed on one side only keeps that change and lists combine")]
    public async Task OneSidedChangesAreKeptAndListsCombine()
    {
        await StartRepository(YamlWith("In Progress", "high"));
        MergeOther(Yaml, oursText: YamlWith("In Progress", "high", "[Artyom, Tom, Ann]"), theirsText: YamlWith("In Progress", "high", "[Artyom, Tom, Bob]"), oursDay: 5, theirsDay: 6);
        CommitTheMergeWithItsMarkers();

        await sync.SyncNow(repo.Root);

        string yaml = repo.Read(Yaml);
        NoMarkers(yaml);
        Assert.Contains("  contributors: [Artyom, Tom, Ann, Bob]\n", yaml);
        Assert.Contains("  status: In Progress\n", yaml);
        Assert.Empty(Conflicts());                                     // nothing was lost, so nothing is reported
    }

    [Fact(DisplayName = "The repair writes the working tree only and logs what was kept and dropped on the item")]
    public async Task TheRepairTouchesTheWorkingTreeOnlyAndLogsTheConflict()
    {
        await StartRepository(YamlWith("In Progress", "high"));
        MergeOther(Yaml, oursText: YamlWith("Blocked", "high"), theirsText: YamlWith("Planned", "high"), oursDay: 5, theirsDay: 7);
        CommitTheMergeWithItsMarkers();
        string head = Head;

        SyncReport? report = await sync.SyncNow(repo.Root);

        Assert.Equal(head, Head);                                                         // nothing was committed
        Assert.Equal("", Git(null, "diff", "--cached", "--name-only").Output.Trim());     // nothing was staged
        Assert.Contains(Yaml, Git(null, "diff", "--name-only").Output.Replace('\\', '/')); // the working tree differs from the commit
        Assert.Contains("add-health-check/.openspec.yaml", report!.Uploaded);             // an app edit: it goes up with this sync
        WorkItemEvent e = Assert.Single(Conflicts());
        Assert.Contains("status", e.Text);
        Assert.Contains("Planned", e.Text);                                                // kept
        Assert.Contains("Blocked", e.Text);                                                // dropped
        Assert.Contains(ui.Toasts, t => t.Text.Contains("conflict markers", StringComparison.OrdinalIgnoreCase));
    }

    [Fact(DisplayName = "Markers in tasks.md are repaired per subtask")]
    public async Task TasksAreRepairedPerSubtask()
    {
        string baseTasks = "# Tasks\n\n## 1. Endpoint\n\n- [x] 1.1 Add /health route\n\n## 2. Hardening\n\n- [ ] 2.1 Hide internals from anonymous callers\n";
        await StartRepository(YamlWith("In Progress", "high"), baseTasks);
        MergeOther(Tasks, oursText: baseTasks + "- [ ] 2.2 Rate limit it\n", theirsText: baseTasks + "- [ ] 2.2 Audit the calls\n", oursDay: 5, theirsDay: 6);
        CommitTheMergeWithItsMarkers();

        await sync.SyncNow(repo.Root);

        string tasks = repo.Read(Tasks);
        NoMarkers(tasks);
        Assert.Contains("- [ ] 2.1 Hide internals from anonymous callers", tasks);
        Assert.Contains("Rate limit it", tasks);
        Assert.Contains("Audit the calls", tasks);
    }

    // ── a merge that is still open ──

    [Fact(DisplayName = "A merge in progress is repaired but the sync stays paused until git finishes it")]
    public async Task AnOpenMergeIsRepairedButTheSyncStaysPaused()
    {
        await StartRepository(YamlWith("In Progress", "high"));
        MergeOther(Yaml, oursText: YamlWith("Blocked", "high"), theirsText: YamlWith("Planned", "high"), oursDay: 4, theirsDay: 8);      // MERGE_HEAD is the newer commit

        SyncReport? paused = await sync.SyncNow(repo.Root);

        Assert.Equal(SyncOutcome.Blocked, paused!.Outcome);
        Assert.Contains("merge is in progress", paused.Message);
        string yaml = repo.Read(Yaml);
        NoMarkers(yaml);
        Assert.Contains("  status: Planned\n", yaml);
        Assert.Contains(Yaml, Git(null, "diff", "--name-only", "--diff-filter=U").Output.Replace('\\', '/'));     // git still owns the merge: the file is still unmerged in the index
        Assert.Single(Conflicts());

        Git(null, "add", "-A");                                                           // the user finishes it
        Git(Day(22), "commit", "-q", "--no-edit");
        SyncReport? done = await sync.SyncNow(repo.Root);

        Assert.Equal(SyncOutcome.Synced, done!.Outcome);
        Assert.Contains("add-health-check/.openspec.yaml", done.Uploaded);
    }

    // ── what is not repaired ──

    [Fact(DisplayName = "Markers in a file the board has no merge rule for still pause the sync")]
    public async Task AFileWithoutAMergeRuleStaysPaused()
    {
        await StartRepository(YamlWith("In Progress", "high"));
        string marked = "# Add health check\n\n<<<<<<< HEAD\nours\n=======\ntheirs\n>>>>>>> other\n";
        repo.Write(Proposal, marked);

        SyncReport? report = await sync.SyncNow(repo.Root);

        Assert.Equal(SyncOutcome.Blocked, report!.Outcome);
        Assert.Contains("proposal.md still has merge conflict markers", report.Message);
        Assert.Equal(marked, repo.Read(Proposal));                                         // not touched
    }

    [Fact(DisplayName = "A merge result that is not valid yaml is left alone and the sync stays paused")]
    public async Task AResultThatIsNotYamlStaysPaused()
    {
        await StartRepository(YamlWith("In Progress", "high"));
        string marked = "schema: spec-driven\nx-rtm:\n  id: 123\n<<<<<<< HEAD\n  title: [unclosed\n=======\n  title: fine\n>>>>>>> other\n";
        repo.Write(Yaml, marked);

        SyncReport? report = await sync.SyncNow(repo.Root);

        Assert.Equal(SyncOutcome.Blocked, report!.Outcome);
        Assert.Equal(marked, repo.Read(Yaml));
    }

    [Fact(DisplayName = "When git cannot name the sides the hunks in the file are merged and the first side wins what both changed")]
    public async Task WithoutSidesTheHunksInTheFileAreUsed()
    {
        await StartRepository(YamlWith("In Progress", "high"));
        repo.Write(Yaml, "schema: spec-driven\nx-rtm:\n  id: 123\n  title: Health-check endpoint\n<<<<<<< HEAD\n  status: Blocked\n  priority: high\n=======\n  status: Planned\n  priority: low\n>>>>>>> other\n  features: [a]\n");

        SyncReport? report = await sync.SyncNow(repo.Root);

        string yaml = repo.Read(Yaml);
        NoMarkers(yaml);
        Assert.Contains("  status: Blocked\n  priority: high\n", yaml);
        Assert.Contains("  features: [a]\n", yaml);
        Assert.Equal(SyncOutcome.Synced, report!.Outcome);
        Assert.NotEmpty(Conflicts());
    }
}
