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

    private void CommitAs(string author, string file, string text, string message)
    {
        string path = Path.Combine(root, file);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
        Git("add", "-A");
        Git("-c", $"user.name={author}", "commit", "-q", "-m", message);
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

    public void Dispose()
    {
        foreach (string f in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)) File.SetAttributes(f, FileAttributes.Normal); // git marks objects read-only
        try { Directory.Delete(root, true); } catch (IOException) { } // ponytail: temp folder, best effort
    }
}
