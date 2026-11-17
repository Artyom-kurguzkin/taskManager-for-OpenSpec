using Bunit;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using OpenSpecStore;
using Radzen;
using TaskManager.AppState;
using TaskManager.Components;
using TaskManager.Components.Layout;
using TaskManager.Components.Pages;
using TaskManager.Components.Shared;
using TaskManager.Helpers;
using WorkItems;

namespace TaskManager.Tests;

/// <summary>
/// What a write costs the window (#wi195): the page draws first, the file write, its lock wait and the duplicate scan run on the thread pool,
/// and the page's own write does not make it read again. No test sleeps: the pool's work is awaited through the tasks it returns.
/// </summary>
public sealed class WritingWithoutBlockingTests : IDisposable
{
    private readonly TestRepo repo = new();
    private readonly string settingsFile = Path.Combine(Path.GetTempPath(), "rtm-ui-" + Guid.NewGuid().ToString("N") + ".json");
    private readonly FakeTimeProvider time = new();

    public void Dispose()
    {
        repo.Dispose();
        try { File.Delete(settingsFile); } catch (IOException) { }
    }

    private IRenderedComponent<Overlays>? overlays;

    private BunitContext NewContext()
    {
        BunitContext ctx = new();
        ctx.Services.AddSingleton<TimeProvider>(time);
        ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        ctx.Services.AddSingleton<UiService>();
        ctx.Services.AddRadzenComponents();
        ctx.Services.AddSingleton(new SettingsStore(settingsFile));
        ctx.Services.AddSingleton<Workspace>();
        OpenSpecStoreModule.State.RealWatcher = false;   // a late event from the disk would read the page in the middle of a step
        Assert.True(ctx.Services.GetRequiredService<Workspace>().Open(repo.Root).Success);
        overlays = ctx.Render<Overlays>();
        return ctx;
    }

    /// <summary>A page that shows a word and runs its commands in the background, as the pages do.</summary>
    private sealed class Probe : RepoPage
    {
        public string Text = "before";
        protected override void Load() => Text = "before";
        protected override void BuildRenderTree(RenderTreeBuilder builder) => builder.AddContent(0, Text);
        public Task<bool> Go(Func<(bool Success, string Message)> command, string? show = null) =>
            RunInBackground(command, show is null ? null : () => Text = show);
    }

    [Fact(DisplayName = "A command whose folder lock is held draws within one dispatcher turn and finishes when the lock is released")]
    public async Task ALockedFolderDoesNotFreezeThePage()
    {
        await using BunitContext ctx = NewContext();
        IRenderedComponent<WorkItemPage> page = ctx.Render<WorkItemPage>(p => p.Add(c => c.Id, 123));
        page.InvokeAsync(() => page.Find(".item-views input[value='linked']").ChangeAsync("linked")).GetAwaiter().GetResult();
        page.WaitForAssertion(() => Assert.NotEmpty(page.FindAll("#sub-1-3")));
        FolderLock.WaitLimit.Value = TimeSpan.FromSeconds(10);   // the write waits for the holder; the test releases it long before
        IDisposable held = FolderLock.Hold(repo.Root, "add-health-check");
        int drawn = RenderCounts.Of<WorkItemPage>();

        Task tick = page.InvokeAsync(() => page.Find("#sub-1-3").ChangeAsync(true));
        await page.InvokeAsync(() => { });   // the dispatcher's next turn: it is free, the write is waiting on the pool

        Assert.False(tick.IsCompleted);
        Assert.True(RenderCounts.Of<WorkItemPage>() > drawn);   // drawn after the handler's synchronous part, before the write
        Assert.Contains("- [~] 1.3", repo.Read("openspec/changes/add-health-check/tasks.md"));

        held.Dispose();
        await tick;
        Assert.Contains("- [x] 1.3", repo.Read("openspec/changes/add-health-check/tasks.md"));
    }

    [Fact(DisplayName = "Drain waits for a queued command's write to land")]
    public async Task DrainWaitsForAQueuedWrite()
    {
        using Workspace workspace = new(new SettingsStore(settingsFile), new UiService());
        Assert.True(workspace.Open(repo.Root).Success);
        FolderLock.WaitLimit.Value = TimeSpan.FromSeconds(10);   // the write waits for the holder; the test releases it long before
        IDisposable held = FolderLock.Hold(repo.Root, "add-health-check");
        Task<(bool Success, string Message)> command = workspace.DoLater(() => WorkItemsApi.SetSubtaskDone(123, "1.3", true));

        Task<bool> drain = workspace.DrainAsync(TimeSpan.FromSeconds(30));
        Assert.False(drain.IsCompleted);                                  // the command is still waiting for the folder
        Assert.Contains("- [~] 1.3", repo.Read("openspec/changes/add-health-check/tasks.md"));

        held.Dispose();
        Assert.True(await drain);
        Assert.Contains("- [x] 1.3", repo.Read("openspec/changes/add-health-check/tasks.md"));   // landed before Drain said so
        Assert.True((await command).Success);
    }

    [Fact(DisplayName = "Drain returns false when a command holds past the limit")]
    public async Task DrainGivesUpAtTheLimit()
    {
        using Workspace workspace = new(new SettingsStore(settingsFile), new UiService());
        Assert.True(workspace.Open(repo.Root).Success);
        FolderLock.WaitLimit.Value = TimeSpan.FromSeconds(10);
        IDisposable held = FolderLock.Hold(repo.Root, "add-health-check");
        Task<(bool Success, string Message)> command = workspace.DoLater(() => WorkItemsApi.SetSubtaskDone(123, "1.3", true));

        Task<bool> drain = workspace.DrainAsync(TimeSpan.FromSeconds(5), time);
        Assert.False(drain.IsCompleted);
        time.Advance(TimeSpan.FromSeconds(5));   // the limit passes while the command still holds

        Assert.False(await drain);
        held.Dispose();
        await command;   // the command itself is not cancelled: it finishes when the lock is free
        Assert.True(await workspace.DrainAsync(TimeSpan.FromSeconds(30)));   // and then nothing is left
    }

    [Fact(DisplayName = "Drain does not wait for the refresh a closed window can no longer run")]
    public async Task DrainDoesNotWaitForTheRefresh()
    {
        using Workspace workspace = new(new SettingsStore(settingsFile), new UiService());
        OpenSpecStoreModule.State.RealWatcher = false;
        Assert.True(workspace.Open(repo.Root).Success);
        TaskCompletionSource entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        using ManualResetEventSlim release = new();
        int first = 0;
        workspace.Changed += () => { if (Interlocked.Exchange(ref first, 1) == 0) { entered.SetResult(); release.Wait(); } };   // what the page's redraw does once the window's loop has ended: it never returns
        Task<(bool Success, string Message)> one = workspace.DoLater(() => WorkItemsApi.SetSubtaskDone(123, "1.3", true));
        await entered.Task;   // the first command's write is down and its refresh is stuck

        Task<bool> drain = workspace.DrainAsync(TimeSpan.FromSeconds(30), time);
        time.Advance(TimeSpan.FromSeconds(30));
        Assert.True(await drain);   // the write is down; the stuck refresh is not waited for
        Assert.Contains("- [x] 1.3", repo.Read("openspec/changes/add-health-check/tasks.md"));

        Assert.True((await workspace.DoLater(() => WorkItemsApi.SetSubtaskDone(123, "2.1", true))).Success);   // and the queue is free for the next write
        Assert.Contains("- [x] 2.1", repo.Read("openspec/changes/add-health-check/tasks.md"));
        release.Set();
        await one;
    }

    [Fact(DisplayName = "Notify called from three threads at once runs the duplicate scan once, after Changed")]
    public async Task NotifyScansOnce()
    {
        WorkItemsApi.ConfigureContributors(null);
        using Workspace workspace = new(new SettingsStore(settingsFile), new UiService());
        Assert.True(workspace.Open(repo.Root).Success);
        int scans = WorkItemsModule.DuplicateScans;
        int changed = 0;
        workspace.Changed += () => Interlocked.Increment(ref changed);
        EditingWorkItemsTests.Duplicate(repo);   // a git pull brings a folder with a used number
        OpenSpecStoreApi.Refresh();
        using ManualResetEventSlim go = new();

        Task[] callers = [.. Enumerable.Range(0, 3).Select(_ => Task.Run(() => { go.Wait(); workspace.Notify(); }))];
        go.Set();
        await Task.WhenAll(callers);
        await workspace.Resolved;

        Assert.Equal(scans + 1, WorkItemsModule.DuplicateScans);
        Assert.Equal("second-check", WorkItemsApi.Get(126)!.Folder);
        Assert.True(changed >= 4);   // each call said so at once, and the renumbering said so again
    }

    [Fact(DisplayName = "A read that overlaps a write of the same file records nothing, so the write's own record stands")]
    public void ReadDuringWriteRecordsNothing()
    {
        OpenSpecStoreApi.Open(repo.Root);
        string tasks = Path.GetFullPath(repo.PathOf("openspec/changes/add-health-check/tasks.md"));
        OpenSpecStoreModule.LastSeen[tasks] = "what the write recorded";

        using (OpenSpecStore.Commands.HoldPath(tasks))   // a write of the file is in flight
            Assert.Contains("1.3 Ping test", OpenSpecStore.Queries.ReadText(tasks));   // the read still answers ...
        Assert.Equal("what the write recorded", OpenSpecStoreModule.LastSeen[tasks]);   // ... but leaves the record alone

        OpenSpecStore.Queries.ReadText(tasks);   // no write in flight: a read records as before
        Assert.Contains("1.3 Ping test", OpenSpecStoreModule.LastSeen[tasks]);
    }

    [Fact(DisplayName = "A writer that holds the path gate reads and records its own LastSeen")]
    public void HolderOfTheGateRecordsItsRead()
    {
        OpenSpecStoreApi.Open(repo.Root);
        string tasks = Path.GetFullPath(repo.PathOf("openspec/changes/add-health-check/tasks.md"));
        OpenSpecStoreModule.LastSeen[tasks] = "a stale view";   // another writer changed the file since

        (bool success, string message) = OpenSpecStore.Commands.UpdateText(tasks, text => text + "appended\n");

        Assert.True(success, message);   // the read inside the hold refreshed the record, so no stale refusal
        Assert.EndsWith("appended\n", OpenSpecStoreModule.LastSeen[tasks]);
    }

    [Fact(DisplayName = "The page's own echo causes no second Load, and a change from outside still does")]
    public async Task OwnEchoIsNotLoaded()
    {
        await using BunitContext ctx = NewContext();
        Workspace workspace = ctx.Services.GetRequiredService<Workspace>();
        IRenderedComponent<Probe> page = ctx.Render<Probe>();
        int loads = page.Instance.Loads;

        Assert.True(await page.InvokeAsync(() => page.Instance.Go(() => WorkItemsApi.SetSubtaskDone(123, "1.3", true), show: "after")));
        await workspace.Resolved;
        time.Advance(TimeSpan.FromMilliseconds(100));   // past the page's reload window
        await page.InvokeAsync(() => { });

        Assert.Equal(loads, page.Instance.Loads);
        Assert.Equal("after", page.Markup);   // the shown result stays: nothing read it away

        Assert.True(WorkItemsApi.SetSubtaskDone(123, "2.1", true).Success);   // a change that is not the page's own
        workspace.Notify();
        page.WaitForAssertion(() => Assert.Equal(loads + 1, page.Instance.Loads));
    }

    [Fact(DisplayName = "A failed write restores the previous projection and shows why in a toast")]
    public async Task FailedWriteRestores()
    {
        await using BunitContext ctx = NewContext();
        IRenderedComponent<Probe> page = ctx.Render<Probe>();
        int loads = page.Instance.Loads;

        bool ok = await page.InvokeAsync(() => page.Instance.Go(() => (false, "The disk said no"), show: "after"));

        Assert.False(ok);
        page.WaitForAssertion(() => Assert.Equal("before", page.Markup));   // read again from what is written
        Assert.Equal(loads + 1, page.Instance.Loads);
        overlays!.WaitForAssertion(() => Assert.Contains("The disk said no", overlays.Find(".toast.warning").TextContent));
    }
}
