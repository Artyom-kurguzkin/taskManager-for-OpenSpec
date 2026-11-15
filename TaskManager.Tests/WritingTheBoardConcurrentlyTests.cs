using OpenSpecStore;
using WorkItems.Infrastructure;

namespace TaskManager.Tests;

/// <summary>
/// The desktop app, the headless board server and agents in worktrees write the same change folders. Each of them is a module state of its
/// own here (the <c>Isolated</c> seam), with its own view of what it last read, which is what separate processes have.
/// </summary>
public sealed class WritingTheBoardConcurrentlyTests
{
    private const string Folder = "add-health-check";
    private const string Change = "openspec/changes/" + Folder;

    private static T As<T>(StoreState state, Func<T> run)
    {
        StoreState? before = OpenSpecStoreModule.Isolated.Value;
        OpenSpecStoreModule.Isolated.Value = state;
        try { return run(); }
        finally { OpenSpecStoreModule.Isolated.Value = before; }
    }

    [Fact(DisplayName = "Two writers with stale views of events.log both get their event appended")]
    public async Task EventsAreNeverLost()
    {
        using TestRepo repo = new();
        OpenSpecStoreApi.Open(repo.Root);
        string dir = repo.PathOf(Change);
        const int Writers = 3, Each = 10;

        // every writer is its own process-like state; they start together and append in turn, each with a view of the log that others keep outdating
        using ManualResetEventSlim go = new(false);
        Task<bool>[] writers = Enumerable.Range(0, Writers).Select(w => Task.Run(() =>
        {
            OpenSpecStoreModule.Isolated.Value = new StoreState();
            OpenSpecStoreApi.Open(repo.Root);
            EventLog.Read(dir);
            go.Wait();
            bool all = true;
            for (int i = 0; i < Each; i++) all &= EventLog.Append(dir, $"writer{w}", [("note", $"event {w}-{i}")]).Success;
            return all;
        })).ToArray();
        go.Set();

        Assert.All(await Task.WhenAll(writers), Assert.True);
        string log = repo.Read(Change + "/events.log");
        for (int w = 0; w < Writers; w++)
            for (int i = 0; i < Each; i++)
                Assert.Contains($"| note | event {w}-{i}\n", log);
    }

    [Fact(DisplayName = "A second writer of the same yaml is refused with the stale message and nothing is lost")]
    public void StaleYamlIsRefused()
    {
        using TestRepo repo = new();
        OpenSpecStoreApi.Open(repo.Root);
        string yaml = repo.PathOf(Change + "/.openspec.yaml");
        OpenSpecStoreApi.ReadRtmBlock(yaml);   // the app's view

        StoreState server = new();
        (bool serverOk, string _) = As(server, () =>
        {
            OpenSpecStoreApi.Open(repo.Root);
            OpenSpecStoreApi.ReadRtmBlock(yaml);
            return OpenSpecStoreApi.WriteRtmBlock(yaml, "id: 123\nstatus: Blocked\n");
        });
        (bool ok, string message) = OpenSpecStoreApi.WriteRtmBlock(yaml, "id: 123\nstatus: In Review\n");

        Assert.True(serverOk);
        Assert.False(ok);
        Assert.Equal(Commands.StaleMessage, message);
        Assert.Contains("status: Blocked", repo.Read(Change + "/.openspec.yaml"));
        Assert.DoesNotContain("In Review", repo.Read(Change + "/.openspec.yaml"));
    }

    [Fact(DisplayName = "A write holds the change folder's lock from the check to the move")]
    public void WriteNeedsTheLock()
    {
        using TestRepo repo = new();
        OpenSpecStoreApi.Open(repo.Root);
        string tasks = repo.PathOf(Change + "/tasks.md");
        string before = repo.Read(Change + "/tasks.md");
        FolderLock.WaitLimit.Value = TimeSpan.Zero;   // the 5 s wait is the lock's own; the test only needs to see that it is waited for and then refused

        IDisposable held = FolderLock.Hold(repo.Root, Folder);
        TimeoutException e = Assert.Throws<TimeoutException>(() => FolderLock.Hold(repo.Root, Folder));
        Assert.Contains("lock", e.Message);
        (bool ok, string message) = OpenSpecStoreApi.WriteText(tasks, "# Tasks\n\nmine\n");
        (bool deleted, string deleteMessage) = OpenSpecStoreApi.DeleteFolder(repo.PathOf(Change));
        Assert.False(ok);
        Assert.Contains("lock", message);
        Assert.False(deleted);
        Assert.Contains("lock", deleteMessage);
        Assert.Equal(before, repo.Read(Change + "/tasks.md"));   // nothing was touched, not even the check

        held.Dispose();
        Assert.True(OpenSpecStoreApi.WriteText(tasks, "# Tasks\n\nmine\n").Success);
        Assert.Equal("# Tasks\n\nmine\n", repo.Read(Change + "/tasks.md"));
        using (FolderLock.Hold(repo.Root, Folder)) { }   // and a write leaves the lock free
    }

    [Fact(DisplayName = "A temp file left by a crash is removed by the next write")]
    public void CrashLeftoversAreRemoved()
    {
        using TestRepo repo = new();
        OpenSpecStoreApi.Open(repo.Root);
        repo.Write(Change + "/tasks.md.0a1b2c3d.tmp", "half");
        repo.Write(Change + "/tasks.md.tmp", "half, the old fixed name");
        repo.Write(Change + "/proposal.md.0a1b2c3d.tmp", "another file's temp");
        repo.Write(Change + "/tasks.md.0a1b2c3d.keep", "not a temp");

        Assert.True(OpenSpecStoreApi.WriteText(repo.PathOf(Change + "/tasks.md"), "# Tasks\n\nnew\n").Success);

        Assert.False(File.Exists(repo.PathOf(Change + "/tasks.md.0a1b2c3d.tmp")));
        Assert.False(File.Exists(repo.PathOf(Change + "/tasks.md.tmp")));
        Assert.True(File.Exists(repo.PathOf(Change + "/proposal.md.0a1b2c3d.tmp")));
        Assert.True(File.Exists(repo.PathOf(Change + "/tasks.md.0a1b2c3d.keep")));
    }

    [Fact(DisplayName = "Two writers never share a temp file name")]
    public void TempNamesAreUnique()
    {
        string file = Path.Combine(Path.GetTempPath(), "tasks.md");
        string[] names = Enumerable.Range(0, 200).Select(_ => Commands.TempName(file)).ToArray();

        Assert.Equal(names.Length, names.Distinct().Count());
        Assert.All(names, n =>
        {
            Assert.StartsWith(file + ".", n);
            Assert.EndsWith(".tmp", n);   // the sync's scan ignores *.tmp
            Assert.Matches(@"\.[0-9a-f]{8}\.tmp$", n);
        });
    }

    [Fact(DisplayName = "A worktree and the main repository lock the same file")]
    public void WorktreesShareTheLock()
    {
        string top = Path.Combine(Path.GetTempPath(), "rtm-tests", Guid.NewGuid().ToString("N"));
        try
        {
            string main = Path.Combine(top, "main"), worktree = Path.Combine(top, "wt");
            Directory.CreateDirectory(Path.Combine(main, ".git", "worktrees", "wt"));
            Directory.CreateDirectory(worktree);
            File.WriteAllText(Path.Combine(worktree, ".git"), $"gitdir: {Path.Combine(main, ".git", "worktrees", "wt")}\n");

            string fromMain = FolderLock.PathOf(main, Folder), fromWorktree = FolderLock.PathOf(worktree, Folder);

            Assert.Equal(fromMain, fromWorktree);
            Assert.StartsWith(Path.Combine(main, ".git", "rtm", "locks"), fromMain);
            Assert.EndsWith(Folder + ".lock", fromMain);
            FolderLock.WaitLimit.Value = TimeSpan.Zero;
            using (FolderLock.Hold(main, Folder))
                Assert.Throws<TimeoutException>(() => FolderLock.Hold(worktree, Folder));
        }
        finally { Directory.Delete(top, recursive: true); }
    }

    // ---- read-share chunk ----

    [Fact(DisplayName = "A board file can be read while another handle holds it open for writing")]
    public void ReadingSharesTheFile()
    {
        using TestRepo repo = new();
        OpenSpecStoreApi.Open(repo.Root);
        string path = repo.PathOf("openspec/config.yaml");
        repo.Write("openspec/config.yaml", "schema: spec-driven\n");

        // a writer that shares (the board server, the OpenSpec CLI, the sync) keeps the file open while the app reads it
        using FileStream writer = new(path, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite | FileShare.Delete);

        Assert.Equal("schema: spec-driven\n", OpenSpecStoreApi.ReadText(path));
    }

    [Fact(DisplayName = "Reading keeps the file's encoding and a UTF-8 BOM is not returned as text")]
    public void ReadingKeepsTheEncoding()
    {
        using TestRepo repo = new();
        OpenSpecStoreApi.Open(repo.Root);
        string withBom = repo.PathOf("openspec/bom.md"), utf16 = repo.PathOf("openspec/utf16.md");
        File.WriteAllText(withBom, "café ✓\n", new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        File.WriteAllText(utf16, "café ✓\n", System.Text.Encoding.Unicode);

        Assert.Equal("café ✓\n", OpenSpecStoreApi.ReadText(withBom));
        Assert.Equal("café ✓\n", OpenSpecStoreApi.ReadText(utf16));
        Assert.Equal("", OpenSpecStoreApi.ReadText(repo.PathOf("openspec/missing.md")));
    }
}
