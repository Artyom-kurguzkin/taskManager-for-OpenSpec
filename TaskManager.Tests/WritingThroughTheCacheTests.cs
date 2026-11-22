using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using OpenSpecStore;
using Radzen;
using TaskManager.AppState;
using TaskManager.Components.Pages;
using TaskManager.Helpers;
using TaskManager.Mcp;
using WorkItems;

namespace TaskManager.Tests;

/// <summary>
/// A write names what it wrote (#wi213): the store keeps the text and the stamp of every file it wrote, the cache patches that one item from them instead of walking the repository,
/// the writes of a burst raise one Changed after the burst, and the watcher's echo of the app's own write changes nothing. Nothing here waits for a clock: the debounce and the page's
/// timers run on a FakeTimeProvider the test moves by hand, and the store's ReadLog and the cache's counters say what was read.
/// </summary>
public sealed class WritingThroughTheCacheTests : IDisposable
{
    private readonly TestRepo repo = new();
    private readonly string settingsFile = Path.Combine(Path.GetTempPath(), "rtm-wt-" + Guid.NewGuid().ToString("N") + ".json");
    private readonly FakeTimeProvider time = new();

    public void Dispose()
    {
        repo.Dispose();
        try { File.Delete(settingsFile); } catch (IOException) { }
    }

    private BunitContext NewContext()
    {
        BunitContext ctx = new();
        ctx.Services.AddSingleton<TimeProvider>(time);
        ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        ctx.Services.AddSingleton<UiService>();
        ctx.Services.AddRadzenComponents();
        ctx.Services.AddSingleton(new SettingsStore(settingsFile));
        ctx.Services.AddSingleton<Workspace>();
        WorkItemsApi.ConfigureValidator(folder => (true, "valid"));
        OpenSpecStoreModule.State.RealWatcher = false;   // a late event from the disk would read the page in the middle of a step; the echo tests deliver theirs by hand
        OpenSpecStoreModule.State.Time = time;
        Assert.True(ctx.Services.GetRequiredService<Workspace>().Open(repo.Root).Success);
        return ctx;
    }

    private string Folder => repo.PathOf("openspec/changes/add-health-check");
    private string Yaml => Path.Combine(Folder, ".openspec.yaml");

    private static List<string> StartRecording()
    {
        List<string> log = [];
        OpenSpecStoreModule.State.ReadLog = log;
        return log;
    }

    private static List<string> Opened(List<string> log) { lock (log) return [.. log]; }

    private async Task PassOver(TimeSpan span)
    {
        time.Advance(span);
        await Task.Yield();
    }

    [Fact(DisplayName = "A tool write shows on the board without a restart")]
    public async Task AToolWriteShowsOnTheBoard()
    {
        await using BunitContext ctx = NewContext();
        Workspace workspace = ctx.Services.GetRequiredService<Workspace>();
        IRenderedComponent<BoardPage> board = ctx.Render<BoardPage>();
        board.Eventually(() => Assert.Contains("Health-check endpoint", board.Markup));
        Assert.DoesNotContain("Brand new from a tool", board.Markup);
        BoardTools tools = new(new BoardHost(workspace.NotifyWritten, DefaultToWorkingDirectory: false));

        await tools.BoardCreate("Brand new from a tool", "An agent creates this item through the board tools while the window is open; it must show in Planned.", "- One thing.", status: "Planned", agent: "Codex", repo: repo.Root);
        for (int passed = 0; passed < 800; passed += 50)   // the debounce, then the page's coalescing window, moved by hand
        {
            time.Advance(TimeSpan.FromMilliseconds(50));
            await board.InvokeAsync(() => { });
        }

        board.Eventually(() => Assert.Contains("Brand new from a tool", board.Markup));
    }

    [Fact(DisplayName = "A status change updates its own item in the cache from what was written, with no directory pass")]
    public async Task AStatusChangeUpdatesItsOwnItem()
    {
        await using BunitContext ctx = NewContext();
        Workspace workspace = ctx.Services.GetRequiredService<Workspace>();
        Assert.Equal("In Progress", ItemsCache.Get(123)!.Status);   // the board is read once, as the first page does
        WorkItems.PublicDTOs.WorkItem other = ItemsCache.Get(107)!;
        int loads = ItemsCache.Loads, passes = ItemsCache.Passes, patches = ItemsCache.Patches;

        (bool ok, string message) = await workspace.DoLater(() => WorkItemsApi.SetStatus(123, "Blocked"));
        List<string> log = StartRecording();
        WorkItems.PublicDTOs.WorkItem written = ItemsCache.Get(123)!;

        Assert.True(ok, message);
        Assert.Equal("Blocked", written.Status);   // what was written, as the cache holds it
        Assert.Equal(passes, ItemsCache.Passes);     // no walk of the changes folder, no look at any other folder
        Assert.True(ItemsCache.Patches > patches);   // the item's header and its worklog are two writes, each brought into the cache from its own text
        Assert.Equal(loads + ItemsCache.Patches - patches, ItemsCache.Loads);   // and every assembly of the board since was one of those patches
        Assert.Same(other, ItemsCache.Get(107));     // the others are the objects they were
        List<string> opened = Opened(log);
        Assert.DoesNotContain(opened, p => p.EndsWith(".openspec.yaml") || p.EndsWith("events.log"));   // the files this write made are not read back: the store holds their text
        Assert.All(opened, p => Assert.StartsWith(Folder, p, StringComparison.OrdinalIgnoreCase));      // and nothing of any other item is opened
        Assert.Contains("status: Blocked", File.ReadAllText(Yaml));
    }

    [Fact(DisplayName = "Five subtask ticks in a row raise one Changed after the burst, and the items shown during the burst are the written ones")]
    public async Task ABurstOfTicksRaisesOneChanged()
    {
        await using BunitContext ctx = NewContext();
        Workspace workspace = ctx.Services.GetRequiredService<Workspace>();
        ItemsCache.Get(123);
        int changed = 0;
        workspace.Changed += () => Interlocked.Increment(ref changed);
        (string Number, bool Done)[] ticks = [("1.1", false), ("2.1", true), ("1.1", true), ("2.1", false), ("1.1", false)];
        bool[] one = [false, false, true, true, false], two = [false, true, true, false, false];   // 1.1 and 2.1 after each tick

        for (int n = 0; n < ticks.Length; n++)
        {
            (bool ok, string message) = await workspace.DoLater(() => WorkItemsApi.SetSubtaskDone(123, ticks[n].Number, ticks[n].Done));
            Assert.True(ok, message);
            WorkItems.PublicDTOs.WorkItem shown = ItemsCache.Get(123)!;
            Assert.Equal((one[n], two[n]), (shown.Subtasks.First(s => s.Number == "1.1").Done, shown.Subtasks.First(s => s.Number == "2.1").Done));   // the item shown is the item written
            await PassOver(TimeSpan.FromMilliseconds(100));   // each tick is inside the 300 ms of the one before
        }
        Assert.Equal(0, changed);                             // nothing told the pages during the burst

        await PassOver(TimeSpan.FromMilliseconds(400));
        await workspace.Resolved;

        Assert.Equal(1, changed);                             // one, after the burst
        await PassOver(TimeSpan.FromSeconds(5));
        Assert.Equal(1, changed);
    }

    [Fact(DisplayName = "The watcher's echo of the app's own write changes nothing")]
    public async Task TheWatchersEchoChangesNothing()
    {
        await using BunitContext ctx = NewContext();
        Workspace workspace = ctx.Services.GetRequiredService<Workspace>();
        ItemsCache.Get(123);
        (bool ok, string message) = await workspace.DoLater(() => WorkItemsApi.SetStatus(123, "Blocked"));
        Assert.True(ok, message);
        await PassOver(TimeSpan.FromMilliseconds(400));
        await workspace.Resolved;
        await PassOver(TimeSpan.FromSeconds(3));   // the store's own-write window is over: only the stamp of what was written can tell the echo
        WorkItems.PublicDTOs.WorkItem written = ItemsCache.Get(123)!;
        int changed = 0, loads = ItemsCache.Loads, passes = ItemsCache.Passes;
        long outside = OpenSpecStoreApi.OutsideVersion;
        workspace.Changed += () => Interlocked.Increment(ref changed);
        List<string> log = StartRecording();

        OpenSpecStoreModule.ObserveChange(OpenSpecStoreModule.Repos[Path.GetFullPath(repo.Root)], Yaml);   // what the file system reports for the write that was made
        await PassOver(TimeSpan.FromSeconds(1));
        await workspace.Resolved;

        Assert.Equal(0, changed);
        Assert.Equal(outside, OpenSpecStoreApi.OutsideVersion);   // not an outside change
        Assert.Same(written, ItemsCache.Get(123));
        Assert.Equal((loads, passes), (ItemsCache.Loads, ItemsCache.Passes));
        Assert.Empty(Opened(log));
    }

    [Fact(DisplayName = "A failed write leaves the cache as it was")]
    public async Task AFailedWriteLeavesTheCache()
    {
        await using BunitContext ctx = NewContext();
        Workspace workspace = ctx.Services.GetRequiredService<Workspace>();
        WorkItems.PublicDTOs.WorkItem before = ItemsCache.Get(123)!;
        string shown = OpenSpecStoreApi.ReadText(Yaml);   // what a window read and has open in an editor
        File.WriteAllText(Yaml, shown.Replace("priority: high", "priority: low"));   // someone changed the file since: the app's write is refused
        int changed = 0, loads = ItemsCache.Loads, patches = ItemsCache.Patches;
        long version = OpenSpecStoreApi.Version;
        workspace.Changed += () => Interlocked.Increment(ref changed);

        (bool ok, string message) = await workspace.DoLater(() => OpenSpecStoreApi.WriteText(Yaml, shown.Replace("status: In Progress", "status: Blocked")));
        await PassOver(TimeSpan.FromSeconds(1));

        Assert.False(ok);
        Assert.Contains("changed on disk", message);
        Assert.False(OpenSpecStoreModule.State.Written.ContainsKey(Path.GetFullPath(Yaml)));   // nothing is remembered of a write that did not happen
        Assert.Equal(version, OpenSpecStoreApi.Version);
        Assert.Equal(0, changed);
        Assert.Same(before, ItemsCache.Get(123));   // the cache still holds what the app last read: the refused write changed neither it nor the version it is valid for
        Assert.Equal((loads, patches), (ItemsCache.Loads, ItemsCache.Patches));
    }

    [Fact(DisplayName = "An outside edit to the same item still wins at the next verification")]
    public async Task AnOutsideEditStillWins()
    {
        await using BunitContext ctx = NewContext();
        Workspace workspace = ctx.Services.GetRequiredService<Workspace>();
        ItemsCache.Get(123);
        (bool ok, string message) = await workspace.DoLater(() => WorkItemsApi.SetStatus(123, "Blocked"));
        Assert.True(ok, message);
        Assert.Equal("Blocked", ItemsCache.Get(123)!.Status);

        // an edit from outside: another status of the same length, the file's time moved on
        string text = File.ReadAllText(Yaml);
        File.WriteAllText(Yaml, text.Replace("status: Blocked", "status: Planned"));
        File.SetLastWriteTimeUtc(Yaml, File.GetLastWriteTimeUtc(Yaml).AddMinutes(5));
        OpenSpecStoreApi.Refresh();   // the verification that exists today: the next read goes to disk

        Assert.Equal("Planned", ItemsCache.Get(123)!.Status);

        // and an edit that keeps the file's size and time: the verification still reads the file, because the store only trusts what it wrote until then
        (ok, message) = await workspace.DoLater(() => WorkItemsApi.SetStatus(123, "Blocked"));
        Assert.True(ok, message);
        DateTime stamped = File.GetLastWriteTimeUtc(Yaml);
        File.WriteAllText(Yaml, File.ReadAllText(Yaml).Replace("status: Blocked", "status: Planned"));
        File.SetLastWriteTimeUtc(Yaml, stamped);
        OpenSpecStoreApi.Refresh();

        Assert.Equal("Planned", ItemsCache.Get(123)!.Status);
    }
}
