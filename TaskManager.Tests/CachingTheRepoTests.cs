using Features;
using GitHistory;
using Notes;
using OpenSpecStore;
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
}
