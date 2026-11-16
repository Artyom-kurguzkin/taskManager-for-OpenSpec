using GitHistory;
using Microsoft.Extensions.Time.Testing;

namespace TaskManager.Tests;

/// <summary>
/// A commit or checkout made outside the app (a shell, an IDE) is noticed from .git itself, with no change under openspec/. The watcher is driven through its seam
/// (the event a file system watcher would raise, and a fake clock for its quiet wait), so nothing here waits on real time.
/// </summary>
public sealed class NoticingGitChangesTests : IDisposable
{
    private readonly string root = Directory.CreateTempSubdirectory("rtm-gitwatch").FullName;
    private readonly FakeTimeProvider clock = new();
    private int changes;

    public NoticingGitChangesTests()
    {
        GitHistoryModule.S.Clock = clock;
        GitHistoryModule.S.DrivenWatchers = true;
        TempGit.Init(root);
        Write("openspec/changes/alpha/tasks.md", "- [ ] one\n");
        Write("openspec/changes/beta/tasks.md", "- [ ] one\n");
        TempGit.Commit(root, "2026-11-01T10:00:00+00:00", "Create alpha and beta");
        GitHistoryApi.Configure(root);
        GitHistoryApi.Watch(root);
        GitHistoryApi.Changed += () => Interlocked.Increment(ref changes);
    }

    private void Write(string file, string text)
    {
        string path = Path.Combine(root, file);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }

    private GitWatcher Watcher => GitHistoryModule.States[Path.GetFullPath(root)].Watcher!;

    private void Settle() => clock.Advance(GitWatcher.QuietWait);

    [Fact(DisplayName = "A commit made outside the app updates the uncommitted dots and the last commit")]
    public void CommitOutsideTheApp()
    {
        Write("openspec/changes/alpha/tasks.md", "- [x] one\n");
        Assert.Equal(["alpha"], GitHistoryApi.Uncommitted(2));
        Assert.Equal("Create alpha and beta", GitHistoryApi.LastCommitOf("alpha")!.Subject);

        TempGit.Commit(root, "2026-11-02T10:00:00+00:00", "Tick alpha");
        Watcher.Raise("refs/heads/master");   // git writes the branch's ref file, then the index
        Watcher.Raise("refs/heads/main");
        Watcher.Raise("index");
        Settle();

        Assert.Equal(1, changes);
        Assert.Equal(1, GitHistoryApi.HeadVersion(root));
        Assert.Empty(GitHistoryApi.Uncommitted(2));                                          // no openspec edit moved the version: the commit alone refreshed it
        Assert.Equal("Tick alpha", GitHistoryApi.LastCommitOf("alpha")!.Subject);
    }

    [Fact(DisplayName = "A checkout to another branch refreshes the board")]
    public void CheckoutOtherBranch()
    {
        string first = GitHistoryApi.HeadToken();
        string branch = TempGit.Run(root, null, "rev-parse", "--abbrev-ref", "HEAD").Trim();
        TempGit.Run(root, null, "checkout", "-q", "-b", "side");
        Write("openspec/changes/beta/tasks.md", "- [x] one\n");
        TempGit.Commit(root, "2026-11-02T10:00:00+00:00", "Tick beta on side");
        Watcher.Raise("HEAD"); Settle();
        Assert.Equal("Tick beta on side", GitHistoryApi.LastCommitOf("beta")!.Subject);
        int afterSide = changes;

        TempGit.Run(root, null, "checkout", "-q", branch);
        Watcher.Raise("HEAD"); Settle();

        Assert.Equal(afterSide + 1, changes);
        Assert.Equal(first, GitHistoryApi.HeadToken());
        Assert.Equal("Create alpha and beta", GitHistoryApi.LastCommitOf("beta")!.Subject);
    }

    [Fact(DisplayName = "Examining an item twice does not walk the changes folder again")]
    public void ExaminingTwiceDoesNotWalk()
    {
        Write("openspec/changes/alpha/tasks.md", "- [x] one\n");
        Assert.Equal(["alpha"], GitHistoryApi.Uncommitted(5));
        GitHistoryApi.LastCommitOf("alpha");
        int processes = GitHistoryModule.Processes, walks = GitHistoryModule.Walks, reads = GitHistoryModule.HeadReads;

        for (int i = 0; i < 3; i++) { GitHistoryApi.Uncommitted(5); GitHistoryApi.LastCommitOf("alpha"); }

        Assert.Equal((processes, walks, reads), (GitHistoryModule.Processes, GitHistoryModule.Walks, GitHistoryModule.HeadReads));   // no git process, no walk, no HEAD read from disk

        Write("openspec/changes/alpha/tasks.md", "- [x] one\n- [ ] two\n");
        Write("openspec/changes/beta/tasks.md", "- [x] one\n");
        Assert.Equal(["alpha", "beta"], GitHistoryApi.Uncommitted(6).Order());              // the openspec version moved: asked again
        Assert.Equal(walks, GitHistoryModule.Walks);                                         // and still no walk
    }

    [Fact(DisplayName = "Git's lock files do not trigger a refresh")]
    public void LockFilesAreIgnored()
    {
        Watcher.Raise("index.lock");
        Watcher.Raise("HEAD.lock");
        Watcher.Raise("refs/heads/main.lock");
        Watcher.Raise("packed-refs.lock");
        Watcher.Raise("objects/ab/cdef");   // never watched
        Settle();

        Assert.Equal(0, changes);
        Assert.Equal(0, GitHistoryApi.HeadVersion(root));
    }

    [Fact(DisplayName = "A burst of git events is one refresh, and one that changed nothing is none")]
    public void BurstAndNoChange()
    {
        Watcher.Raise("HEAD"); Watcher.Raise("index"); Watcher.Raise("refs/heads/main");   // git touched its files but nothing moved
        Settle();
        Assert.Equal(0, changes);

        TempGit.Run(root, null, "commit", "-q", "--allow-empty", "-m", "Empty");
        for (int i = 0; i < 5; i++) { Watcher.Raise("refs/heads/main"); clock.Advance(TimeSpan.FromMilliseconds(100)); }   // each event waits again
        Assert.Equal(0, changes);
        Settle();

        Assert.Equal(1, changes);
        Assert.Equal(1, GitHistoryApi.HeadVersion(root));
    }

    [Fact(DisplayName = "A watcher overflow re-checks everything")]
    public void OverflowRechecks()
    {
        Assert.Equal("Create alpha and beta", GitHistoryApi.LastCommitOf("alpha")!.Subject);
        Write("openspec/changes/alpha/tasks.md", "- [x] one\n");
        TempGit.Commit(root, "2026-11-02T10:00:00+00:00", "Missed commit");   // no event arrived for it
        Assert.Equal("Create alpha and beta", GitHistoryApi.LastCommitOf("alpha")!.Subject);

        Watcher.RaiseError();
        Settle();

        Assert.Equal(1, changes);
        Assert.Equal("Missed commit", GitHistoryApi.LastCommitOf("alpha")!.Subject);

        Watcher.RaiseError();   // nothing moved this time, but an overflow cannot know that: it still tells the pages
        Settle();
        Assert.Equal(2, changes);
    }

    [Fact(DisplayName = "Changing the git user name outside the app is picked up")]
    public void UserNameFollowsConfig()
    {
        Assert.Equal("Artyom Test", GitHistoryApi.UserName());
        TempGit.Run(root, null, "config", "user.name", "Someone Else");
        Assert.Equal("Artyom Test", GitHistoryApi.UserName());   // asked once, until git's config moves

        Watcher.Raise("config");
        Settle();

        Assert.Equal("Someone Else", GitHistoryApi.UserName());
    }

    [Fact(DisplayName = "A repository that is no longer watched raises nothing")]
    public void StoppedWatcherIsSilent()
    {
        GitWatcher watcher = Watcher;
        GitHistoryApi.Unwatch(root);
        TempGit.Run(root, null, "commit", "-q", "--allow-empty", "-m", "Empty");

        watcher.Raise("HEAD");
        Settle();

        Assert.Equal(0, changes);
    }

    public void Dispose()
    {
        GitHistoryApi.Unwatch(root);
        foreach (string f in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)) File.SetAttributes(f, FileAttributes.Normal);
        try { Directory.Delete(root, true); } catch (IOException) { }   // ponytail: temp folder, best effort
    }
}
