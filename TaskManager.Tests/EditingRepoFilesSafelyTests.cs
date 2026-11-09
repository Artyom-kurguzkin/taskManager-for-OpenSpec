using OpenSpecStore;
using WorkItems;

namespace TaskManager.Tests;

[Collection(nameof(ModuleStateCollection))]
public sealed class EditingRepoFilesSafelyTests
{
    private const string Yaml = "openspec/changes/add-health-check/.openspec.yaml";

    [Fact(DisplayName = "The app reads only its own x-rtm block from .openspec.yaml")]
    public void ReadsRtmBlock()
    {
        using TestRepo repo = new();
        OpenSpecStoreApi.Open(repo.Root);

        string? block = OpenSpecStoreApi.ReadRtmBlock(repo.PathOf(Yaml));

        Assert.NotNull(block);
        Assert.StartsWith("id: 123", block);
        Assert.Contains("status: In Progress", block);
        Assert.DoesNotContain("schema:", block);
    }

    [Fact(DisplayName = "A change created outside the app has no x-rtm block")]
    public void NoRtmBlock()
    {
        using TestRepo repo = new();
        OpenSpecStoreApi.Open(repo.Root);
        Assert.Null(OpenSpecStoreApi.ReadRtmBlock(repo.PathOf("openspec/changes/plain-change/.openspec.yaml")));
    }

    [Fact(DisplayName = "Saving metadata keeps every other key and comment byte-for-byte")]
    public void WriteKeepsOtherLines()
    {
        using TestRepo repo = new();
        OpenSpecStoreApi.Open(repo.Root);
        string before = repo.Read(Yaml);
        string path = repo.PathOf(Yaml);
        OpenSpecStoreApi.ReadRtmBlock(path);

        (bool ok, string _) = OpenSpecStoreApi.WriteRtmBlock(path, "id: 123\nstatus: Blocked\n");

        Assert.True(ok);
        string after = repo.Read(Yaml);
        string head = before[..before.IndexOf("x-rtm:", StringComparison.Ordinal)];
        Assert.StartsWith(head, after);                         // schema, created and the comment untouched
        Assert.EndsWith("x-rtm:\n  id: 123\n  status: Blocked\n", after);
    }

    [Fact(DisplayName = "Saving metadata to a change without x-rtm appends the block")]
    public void WriteAppendsBlock()
    {
        using TestRepo repo = new();
        OpenSpecStoreApi.Open(repo.Root);
        string rel = "openspec/changes/plain-change/.openspec.yaml";
        string before = repo.Read(rel);
        OpenSpecStoreApi.ReadRtmBlock(repo.PathOf(rel));

        OpenSpecStoreApi.WriteRtmBlock(repo.PathOf(rel), "id: 125\n");

        Assert.Equal(before + "x-rtm:\n  id: 125\n", repo.Read(rel));
    }

    [Fact(DisplayName = "Saving one section of the x-rtm block keeps the block's other sections")]
    public void SectionWriteKeepsSiblings()
    {
        using TestRepo repo = new();
        repo.Write("openspec/config.yaml", "schema: spec-driven\nx-rtm:\n  lanes:\n    In Progress: [High, Low]\n  features:\n  - functional/exports\n");
        OpenSpecStoreApi.Open(repo.Root);
        string path = repo.PathOf("openspec/config.yaml");

        Assert.Equal("In Progress: [High, Low]\n", OpenSpecStoreApi.ReadRtmSection(path, "lanes"));
        OpenSpecStoreApi.WriteRtmSection(path, "lanes", "Blocked:\n- Needs review\n");

        Assert.Equal("schema: spec-driven\nx-rtm:\n  lanes:\n    Blocked:\n    - Needs review\n  features:\n  - functional/exports\n", repo.Read("openspec/config.yaml"));
        OpenSpecStoreApi.WriteRtmSection(path, "lanes", null);
        Assert.Equal("schema: spec-driven\nx-rtm:\n  features:\n  - functional/exports\n", repo.Read("openspec/config.yaml"));
    }

    [Fact(DisplayName = "An edit is refused when the file changed on disk after the app read it")]
    public void StaleWriteRefused()
    {
        using TestRepo repo = new();
        OpenSpecStoreApi.Open(repo.Root);
        string path = repo.PathOf("openspec/changes/add-health-check/tasks.md");
        OpenSpecStoreApi.ReadText(path);
        repo.Write("openspec/changes/add-health-check/tasks.md", "# Tasks\n\nedited elsewhere\n");

        (bool ok, string message) = OpenSpecStoreApi.WriteText(path, "# Tasks\n\nmine\n");

        Assert.False(ok);
        Assert.Equal("This file changed on disk; your edit was not saved", message);
        Assert.Contains("edited elsewhere", repo.Read("openspec/changes/add-health-check/tasks.md"));
    }

    [Fact(DisplayName = "Writing a new file creates its folder")]
    public void WriteNewFile()
    {
        using TestRepo repo = new();
        OpenSpecStoreApi.Open(repo.Root);
        (bool ok, string _) = OpenSpecStoreApi.WriteText(repo.PathOf("openspec/changes/new-one/proposal.md"), "# Proposal\n");
        Assert.True(ok);
        Assert.Equal("# Proposal\n", repo.Read("openspec/changes/new-one/proposal.md"));
    }

    // ── Id allocation under the shared lock ──

    private static string RunGit(string dir, params string[] args)
    {
        System.Diagnostics.ProcessStartInfo psi = new("git") { WorkingDirectory = dir, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (string a in args) psi.ArgumentList.Add(a);
        using System.Diagnostics.Process p = System.Diagnostics.Process.Start(psi)!;
        string output = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
        p.WaitForExit();
        Assert.True(p.ExitCode == 0, $"git {string.Join(' ', args)} failed: {output}");
        return output;
    }

    /// <summary>A bare .git folder is enough for the allocator to find its common dir; no git needed.</summary>
    private static string StubCommonDir(TestRepo repo)
    {
        string dotGit = Path.Combine(repo.Root, ".git");
        Directory.CreateDirectory(Path.Combine(dotGit, "rtm"));
        return Path.Combine(dotGit, "rtm");
    }

    [Fact(DisplayName = "Two worktrees of one repository share the id counter, so an item created in the second gets a number above the first's")]
    public void WorktreesShareTheCounter()
    {
        try { RunGit(Path.GetTempPath(), "--version"); }
        catch (System.ComponentModel.Win32Exception) { return; }   // xunit 2 has no runtime skip: without git on PATH this test has nothing to check

        using TestRepo repo = new();
        string worktree = Path.Combine(Path.GetTempPath(), "rtm-tests", Guid.NewGuid().ToString("N"));
        bool added = false;
        try
        {
            RunGit(repo.Root, "init", "-q");
            RunGit(repo.Root, "-c", "user.name=t", "-c", "user.email=t@t", "add", "-A");
            RunGit(repo.Root, "-c", "user.name=t", "-c", "user.email=t@t", "commit", "-q", "-m", "fixture");
            RunGit(repo.Root, "worktree", "add", "-q", worktree, "-b", "wt");
            added = true;

            OpenSpecStoreApi.Open(repo.Root);
            int first = WorkItemsApi.Create("one").Id;
            OpenSpecStoreApi.Open(worktree);
            (bool ok, string message, int second) = WorkItemsApi.Create("two");

            Assert.True(ok, message);
            Assert.True(second > first, $"second {second} should be above first {first}");
        }
        finally
        {
            OpenSpecStoreApi.Close();
            if (added) try { RunGit(repo.Root, "worktree", "remove", "--force", worktree); } catch (Exception e) when (e is Xunit.Sdk.XunitException or IOException) { }
            try { Directory.Delete(worktree, recursive: true); } catch (Exception e) when (e is IOException or UnauthorizedAccessException or DirectoryNotFoundException) { }
        }
    }

    [Fact(DisplayName = "A deleted item does not free its number: the next item gets a higher one")]
    public void DeletedItemKeepsItsNumber()
    {
        using TestRepo repo = new();
        StubCommonDir(repo);
        OpenSpecStoreApi.Open(repo.Root);
        (bool _, string _, int deleted) = WorkItemsApi.Create("short lived");
        string path = WorkItemsApi.Get(deleted)!.Path;

        Directory.Delete(path, recursive: true);
        (bool ok, string message, int next) = WorkItemsApi.Create("replacement");

        Assert.True(ok, message);
        Assert.True(next > deleted, $"{next} should be above the deleted {deleted}");
    }

    [Fact(DisplayName = "Allocation waits while another process holds the lock, then continues")]
    public void WaitsForTheLock()
    {
        using TestRepo repo = new();
        string dir = StubCommonDir(repo);
        OpenSpecStoreApi.Open(repo.Root);
        using ManualResetEventSlim held = new();
        Task holder = Task.Run(() =>
        {
            using FileStream fs = new(Path.Combine(dir, "work-item.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            held.Set();
            Thread.Sleep(300);
        });
        Assert.True(held.Wait(TimeSpan.FromSeconds(5)));

        System.Diagnostics.Stopwatch watch = System.Diagnostics.Stopwatch.StartNew();
        (bool ok, string message, int _) = WorkItemsApi.Create("waited");
        watch.Stop();

        Assert.True(ok, message);
        Assert.True(watch.ElapsedMilliseconds >= 250, $"returned after {watch.ElapsedMilliseconds} ms, before the holder let go");
        holder.Wait();
    }

    [Fact(DisplayName = "A lock file left behind by a dead process does not block allocation")]
    public void StaleLockFileDoesNotBlock()
    {
        using TestRepo repo = new();
        string dir = StubCommonDir(repo);
        File.WriteAllText(Path.Combine(dir, "work-item.lock"), "pid 999999");
        OpenSpecStoreApi.Open(repo.Root);

        (bool ok, string message, int _) = WorkItemsApi.Create("after a crash");

        Assert.True(ok, message);
    }

    [Fact(DisplayName = "Notes allocate their ids through the same lock")]
    public void NotesUseTheCounter()
    {
        using TestRepo repo = new();
        string dir = StubCommonDir(repo);
        File.WriteAllText(Path.Combine(dir, "note"), "500");
        OpenSpecStoreApi.Open(repo.Root);
        int item = WorkItemsApi.GetAll(includeHidden: true)[0].Id;

        (bool ok, string message, int id) = Notes.NotesApi.Create(item, "t");

        Assert.True(ok, message);
        Assert.Equal(501, id);
    }
}
