using System.Diagnostics;
using GitHistory;
using GitHistory.PublicDTOs;

namespace TaskManager.Tests;

[Collection(nameof(ModuleStateCollection))]
public sealed class ReadingGitHistoryTests : IDisposable
{
    private readonly string root = Directory.CreateTempSubdirectory("rtm-git").FullName;

    private string Git(params string[] args)
    {
        ProcessStartInfo psi = new("git") { WorkingDirectory = root, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (string a in args) psi.ArgumentList.Add(a);
        using Process p = Process.Start(psi)!;
        string output = p.StandardOutput.ReadToEnd();
        p.WaitForExit();
        return output;
    }

    private void Commit(string file, string text, string message)
    {
        string path = Path.Combine(root, file);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
        Git("add", "-A");
        Git("commit", "-q", "-m", message);
    }

    private void InitRepo()
    {
        Git("init", "-q");
        Git("config", "user.name", "Artyom Test");
        Git("config", "user.email", "artyom@example.com");
        Git("config", "commit.gpgsign", "false");
        GitHistoryApi.Configure(root);
    }

    [Fact(DisplayName = "Only commits that mention the whole #wi token are linked")]
    public void WholeTokenMentions()
    {
        InitRepo();
        Commit("a.txt", "1", "Add /health route #wi123");
        Commit("b.txt", "2", "Fix #wi1234");

        CommitInfo c = Assert.Single(GitHistoryApi.Mentioning("#wi123"));

        Assert.Equal("Add /health route #wi123", c.Subject);
        Assert.Equal(7, c.Hash.Length);
    }

    [Fact(DisplayName = "The repository's git user name is used as the default identity")]
    public void UserName()
    {
        InitRepo();
        Assert.Equal("Artyom Test", GitHistoryApi.UserName());
    }

    [Fact(DisplayName = "Committing from a work item stages only that work item's folder")]
    public void ScopedCommit()
    {
        InitRepo();
        Commit("openspec/changes/a/tasks.md", "1", "init a");
        Commit("openspec/changes/b/tasks.md", "1", "init b");
        File.WriteAllText(Path.Combine(root, "openspec/changes/a/tasks.md"), "2");
        File.WriteAllText(Path.Combine(root, "openspec/changes/b/tasks.md"), "2");

        (bool ok, string message) = GitHistoryApi.Commit(["openspec/changes/a"], "Update #wi1 A");

        Assert.True(ok, message);
        Assert.Equal("Update #wi1 A", GitHistoryApi.Mentioning("#wi1")[0].Subject);
        Assert.Contains("openspec/changes/b/tasks.md", Git("status", "--porcelain"));
        Assert.DoesNotContain("openspec/changes/a/tasks.md", Git("status", "--porcelain"));
    }

    [Fact(DisplayName = "A folder that is not a git repository has no history and says so")]
    public void NotARepository()
    {
        GitHistoryApi.Configure(root);
        Assert.False(GitHistoryApi.IsRepository());
        Assert.Empty(GitHistoryApi.Mentioning("#wi1"));
    }

    private void CommitAs(string author, string file, string text, string message, string? email = null)
    {
        email ??= author.Replace(' ', '.') + "@example.com";   // different people, different emails (shared email = same person)
        string path = Path.Combine(root, file);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
        Git("add", "-A");
        Git("-c", $"user.name={author}", "-c", $"user.email={email}", "commit", "-q", "-m", message);
    }

    [Fact(DisplayName = "A change's contributors are the authors of commits that touched its folder, also after it is archived")]
    public void AuthorsFollowArchive()
    {
        InitRepo();
        CommitAs("Alice", "openspec/changes/session-timeout/proposal.md", "a", "Create");
        CommitAs("Bob", "openspec/changes/other/proposal.md", "b", "Unrelated");
        CommitAs("Carol", "openspec/changes/session-timeout/tasks.md", "c", "Tasks");
        Directory.CreateDirectory(Path.Combine(root, "openspec/changes/archive"));
        Git("mv", "openspec/changes/session-timeout", "openspec/changes/archive/2026-09-18-session-timeout");
        Git("-c", "user.name=Alice", "commit", "-q", "-m", "Archive");

        Assert.Equal(["Alice", "Carol"], GitHistoryApi.AuthorsOfChange("2026-09-18-session-timeout"));
        Assert.Equal(["Bob"], GitHistoryApi.AuthorsOfChange("other"));
        Assert.Empty(GitHistoryApi.AuthorsOfChange("never-committed"));
    }

    [Fact(DisplayName = "Everyone who ever committed to the repo, on any branch and any file, is listed once, alphabetically")]
    public void AllAuthors()
    {
        InitRepo();
        CommitAs("Carol", "README.md", "a", "Readme");
        CommitAs("Alice", "src/x.cs", "b", "Code");
        CommitAs("Carol", "src/y.cs", "c", "More");
        Git("checkout", "-q", "-b", "side");
        CommitAs("Bob", "side.txt", "d", "Side work");
        Git("checkout", "-q", "-");

        Assert.Equal(["Alice", "Bob", "Carol"], GitHistoryApi.AllAuthors());

        CommitAs("Dan", "z.txt", "e", "New person");
        Assert.Equal(["Alice", "Bob", "Carol", "Dan"], GitHistoryApi.AllAuthors());   // a new commit refreshes it
    }

    [Fact(DisplayName = "One person committing under name variants or differently-cased emails is listed once, by their most-used name")]
    public void SamePersonOnce()
    {
        InitRepo();
        CommitAs("Ann Lee", "a.txt", "1", "work", "ann@work.org");
        CommitAs("ann-lee", "openspec/changes/x/a.md", "2", "home", "ann@home.com");   // same name apart from case and hyphen
        CommitAs("ann-lee", "b.txt", "3", "home", "ann@home.com");
        CommitAs("Bob Stone", "c.txt", "4", "work", "bob@work.org");
        CommitAs("bstone", "d.txt", "5", "laptop", "BOB@Work.org");                      // same email apart from case
        CommitAs("Cy", "e.txt", "6", "other", "cy@x.org");

        Assert.Equal(["ann-lee", "Bob Stone", "Cy"], GitHistoryApi.AllAuthors());
        Assert.Equal(["ann-lee"], GitHistoryApi.AuthorsOfChange("x"));
        Assert.Equal("ann-lee", GitHistoryApi.PersonName("Ann Lee"));
        Assert.Equal("Someone Else", GitHistoryApi.PersonName("Someone Else"));
    }

    [Fact(DisplayName = "A new commit shows up in a change's contributors without reopening the repo")]
    public void AuthorsSeeNewCommits()
    {
        InitRepo();
        CommitAs("Alice", "openspec/changes/x/proposal.md", "a", "Create");
        Assert.Equal(["Alice"], GitHistoryApi.AuthorsOfChange("x"));
        string before = GitHistoryApi.HeadToken();

        CommitAs("Dan", "openspec/changes/x/tasks.md", "b", "More");

        Assert.NotEqual(before, GitHistoryApi.HeadToken());
        Assert.Equal(["Dan", "Alice"], GitHistoryApi.AuthorsOfChange("x"));
    }

    [Fact(DisplayName = "Recent commits come newest first, and picked hashes resolve to their commits")]
    public void RecentAndResolve()
    {
        InitRepo();
        Commit("a.txt", "1", "First");
        Commit("b.txt", "2", "Second");
        Commit("c.txt", "3", "Third");

        IReadOnlyList<CommitInfo> recent = GitHistoryApi.Recent(2);

        Assert.Equal(["Third", "Second"], recent.Select(c => c.Subject));
        string first = GitHistoryApi.Recent(10).Single(c => c.Subject == "First").Hash;
        Assert.Equal(["First"], GitHistoryApi.Commits([first]).Select(c => c.Subject));
        Assert.Empty(GitHistoryApi.Commits(["deadbee"]));   // unknown hash: nothing, no error
    }

    [Fact(DisplayName = "Commit questions run git once per HEAD: Recent, Mentioning and Commits answer from one log until a new commit lands")]
    public void OneLogPerHead()
    {
        InitRepo();
        Commit("a.txt", "1", "First #wi1");
        Commit("b.txt", "2", "Second #wi2");
        string first = GitHistoryApi.Recent(10).Single(c => c.Subject.StartsWith("First")).Hash;   // warms the repo check and the log

        int before = GitHistoryModule.Processes;
        Assert.Equal(2, GitHistoryApi.Recent(50).Count);
        Assert.Equal(["Second #wi2"], GitHistoryApi.Mentioning("#wi2").Select(c => c.Subject));
        Assert.Empty(GitHistoryApi.Mentioning("#wi22"));
        Assert.Equal(["First #wi1"], GitHistoryApi.Commits([first]).Select(c => c.Subject));
        Assert.Equal(first, GitHistoryApi.Commits([first])[0].Hash);
        Assert.Equal(before, GitHistoryModule.Processes);

        Commit("c.txt", "3", "Third #wi2");
        before = GitHistoryModule.Processes;
        Assert.Equal(3, GitHistoryApi.Recent(50).Count);
        Assert.Equal(2, GitHistoryApi.Mentioning("#wi2").Count);
        Assert.Equal(before + 1, GitHistoryModule.Processes);
    }

    [Fact(DisplayName = "A new commit is seen on the next question without restarting the app")]
    public void NewCommitSeen()
    {
        InitRepo();
        Commit("a.txt", "1", "First");
        Assert.Equal(["First"], GitHistoryApi.Recent(10).Select(c => c.Subject));

        Commit("b.txt", "2", "Second #wi9");

        Assert.Equal(["Second #wi9", "First"], GitHistoryApi.Recent(10).Select(c => c.Subject));
        Assert.Single(GitHistoryApi.Mentioning("#wi9"));
        Git("reset", "-q", "--hard", "HEAD~1");   // a reset moves the tip too
        Assert.Equal(["First"], GitHistoryApi.Recent(10).Select(c => c.Subject));
        Assert.Empty(GitHistoryApi.Mentioning("#wi9"));
    }

    [Fact(DisplayName = "A HEAD recorded in packed-refs is read as a HEAD token")]
    public void PackedRefsHead()
    {
        InitRepo();
        Commit("a.txt", "1", "First");
        string loose = GitHistoryApi.HeadToken();
        Git("pack-refs", "--all", "--prune");
        Assert.Empty(Directory.EnumerateFiles(Path.Combine(root, ".git", "refs", "heads")));   // only packed-refs holds the branch now

        string packed = GitHistoryApi.HeadToken();

        Assert.Equal(Git("rev-parse", "HEAD").Trim(), packed);
        Assert.Equal(loose, packed);   // packing refs does not look like a new commit
        Commit("b.txt", "2", "Second");
        Assert.NotEqual(packed, GitHistoryApi.HeadToken());
    }

    [Fact(DisplayName = "A hash older than the cached log is still found")]
    public void OlderThanLog()
    {
        InitRepo();
        Commit("a.txt", "1", "Oldest");
        Commit("b.txt", "2", "Middle");
        Commit("c.txt", "3", "Newest");
        string oldest = Git("rev-parse", "--short", "HEAD~2").Trim();
        int limit = GitHistoryModule.LogLimit;
        GitHistoryModule.LogLimit = 2;
        try
        {
            Assert.Equal(["Newest", "Middle"], GitHistoryApi.Recent(10).Select(c => c.Subject));   // the log stops at 2 commits

            Assert.Equal(["Oldest"], GitHistoryApi.Commits([oldest]).Select(c => c.Subject));
            int before = GitHistoryModule.Processes;
            Assert.Equal(["Oldest"], GitHistoryApi.Commits([oldest]).Select(c => c.Subject));   // and it is remembered
            Assert.Equal(before, GitHistoryModule.Processes);
        }
        finally { GitHistoryModule.LogLimit = limit; }
    }

    [Fact(DisplayName = "A linked work tree reads its HEAD token without starting git")]
    public void WorktreeHeadToken()
    {
        InitRepo();
        Commit("a.txt", "1", "First");
        string wt = Path.Combine(Path.GetTempPath(), "rtm-wt-" + Guid.NewGuid().ToString("N")[..8]);
        Git("worktree", "add", "-q", "-b", "side", wt);
        try
        {
            GitHistoryApi.Configure(wt);
            string head = Git("-C", wt, "rev-parse", "HEAD").Trim();
            int before = GitHistoryModule.Processes;

            Assert.Equal(head, GitHistoryApi.HeadToken());
            Assert.Equal(before, GitHistoryModule.Processes);
        }
        finally
        {
            Git("worktree", "remove", "--force", wt);
            GitHistoryApi.Configure(root);
        }
    }

    public void Dispose()
    {
        foreach (string f in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)) File.SetAttributes(f, FileAttributes.Normal); // git marks objects read-only
        try { Directory.Delete(root, true); } catch (IOException) { } // ponytail: temp folder, best effort
    }
}
