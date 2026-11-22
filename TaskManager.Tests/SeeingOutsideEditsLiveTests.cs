using System.IO.Pipelines;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using OpenSpecStore;
using TaskManager.AppState;
using TaskManager.Mcp;
using WorkItems;

namespace TaskManager.Tests;

/// <summary>
/// An edit made outside the app (an editor, git, a script) shows without a blanket re-read: the store verifies the tree against the stamps it holds (one listing, size and modified time of each file)
/// and only what differs is read again. Verification runs after the watcher's signal, on the next tool call once 2 seconds have passed since the last one, and on a 5-second timer while a window has focus.
/// Nothing waits for time or the real watcher: the clock is a FakeTimeProvider, the watcher's signal is the method its debounce calls, and the store's ReadLog and listing counter say what was touched.
/// </summary>
public sealed class SeeingOutsideEditsLiveTests : IAsyncLifetime
{
    private readonly TestRepo repo = new();
    private readonly FakeTimeProvider time = new();
    private readonly string temp = Directory.CreateTempSubdirectory("rtm-verify-").FullName;
    private ServiceProvider? services;
    private McpServer? server;
    private McpClient? client;
    private Workspace? workspace;

    public Task InitializeAsync()
    {
        // Far enough ahead of the files' modified times that none of them is "racy" (see the last test for the one that is).
        time.SetUtcNow(DateTimeOffset.UtcNow.AddHours(1));
        OpenSpecStoreModule.State.Time = time;
        OpenSpecStoreModule.State.RealWatcher = false;   // the watcher's signal is sent by the tests, never by the disk
        Caches.S.Items.Clock = () => time.GetUtcNow().UtcDateTime;
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        workspace?.Dispose();
        if (client is not null) await client.DisposeAsync();
        if (server is not null) await server.DisposeAsync();
        if (services is not null) await services.DisposeAsync();
        repo.Dispose();
        try { Directory.Delete(temp, true); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }

    private const string Health = "openspec/changes/add-health-check/.openspec.yaml";
    private const string Export = "openspec/changes/export-remittances/tasks.md";

    private static StoreState Store => OpenSpecStoreModule.State;

    private List<string> Log()
    {
        List<string> log = [];
        Store.ReadLog = log;
        return log;
    }

    /// <summary>The files read since the log was started, relative to the repository and with forward slashes.</summary>
    private List<string> Opened(List<string> log)
    {
        string root = Path.GetFullPath(repo.Root) + Path.DirectorySeparatorChar;
        lock (log) return [.. log.Select(p => p.StartsWith(root, StringComparison.OrdinalIgnoreCase) ? p[root.Length..].Replace('\\', '/') : p).Distinct().Order(StringComparer.Ordinal)];
    }

    private string TitleOfHealth() => WorkItemsApi.GetAll().First(w => w.Id == 123).Title;

    /// <summary>An edit from outside: the file's text and a modified time of its own, so size and time are not left to the file system's clock.</summary>
    private void OutsideEdit(string relative, string text, int minutesAgo)
    {
        string path = repo.PathOf(relative);
        File.WriteAllText(path, text);
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddMinutes(-minutesAgo));
    }

    private void OpenAndLoad()
    {
        OpenSpecStoreApi.Open(repo.Root);
        OpenSpecStoreApi.Verify();   // the first look: what the stamps are held against from here
        TitleOfHealth();
    }

    /// <summary>What the watcher's debounced timer runs when it fires (the test calls it instead of waiting for the file system and 300 ms).</summary>
    private static void WatcherSignals() => OpenSpecStoreModule.OnSettled(OpenSpecStoreModule.Current!);

    private async Task StartServer()
    {
        services = new ServiceCollection().AddLogging().AddMcpServer().AddBoardTools().Services.BuildServiceProvider();
        Pipe toServer = new(), toClient = new();
        server = McpServer.Create(
            new StreamServerTransport(toServer.Reader.AsStream(), toClient.Writer.AsStream()),
            services.GetRequiredService<IOptions<McpServerOptions>>().Value,
            services.GetRequiredService<ILoggerFactory>(), services);
        _ = server.RunAsync();
        client = await McpClient.CreateAsync(new StreamClientTransport(toServer.Writer.AsStream(), toClient.Reader.AsStream()));
    }

    private async Task ToolCall()
    {
        CallToolResult result = await client!.CallToolAsync("board_list", new Dictionary<string, object?> { ["repo"] = repo.Root });
        Assert.NotEqual(true, result.IsError);
    }

    [Fact(DisplayName = "An outside edit is picked up after the watcher's signal, by one listing and one file read")]
    public void AnOutsideEditIsPickedUpAfterTheWatcherSignals()
    {
        OpenAndLoad();
        Assert.Equal("Health-check endpoint", TitleOfHealth());
        List<string> log = Log();
        int listings = Store.Listings;

        OutsideEdit(Health, repo.Read(Health).Replace("title: Health-check endpoint", "title: Health-check endpoint, v2"), minutesAgo: 30);
        Assert.Equal("Health-check endpoint", TitleOfHealth());   // nothing has noticed yet
        WatcherSignals();

        Assert.Equal("Health-check endpoint, v2", TitleOfHealth());
        Assert.Equal(listings + 1, Store.Listings);   // one listing of the tree
        Assert.Contains("openspec/changes/add-health-check/.openspec.yaml", Opened(log));
        Assert.All(Opened(log), p => Assert.StartsWith("openspec/changes/add-health-check/", p));   // that item's files only: no other item, no config.yaml
    }

    [Fact(DisplayName = "A dropped watcher signal is caught by the next tool call once 2 seconds have passed since the last verification")]
    public async Task ADroppedSignalIsCaughtByTheNextToolCallAfterTwoSeconds()
    {
        OpenSpecStoreApi.Open(repo.Root);
        await StartServer();
        await ToolCall();   // verifies: the first call has nothing to compare with
        TitleOfHealth();
        OutsideEdit(Health, repo.Read(Health).Replace("title: Health-check endpoint", "title: Health-check endpoint, v2"), minutesAgo: 30);   // no signal: the watcher dropped it

        time.Advance(TimeSpan.FromSeconds(1.9));
        await ToolCall();
        Assert.Equal("Health-check endpoint", TitleOfHealth());   // not yet: under 2 seconds

        time.Advance(TimeSpan.FromSeconds(0.2));
        int listings = Store.Listings;
        await ToolCall();

        Assert.Equal(listings + 1, Store.Listings);
        Assert.Equal("Health-check endpoint, v2", TitleOfHealth());
    }

    [Fact(DisplayName = "A tool call within 2 seconds of a verification lists nothing and reads nothing")]
    public async Task AToolCallWithinTwoSecondsListsNothing()
    {
        OpenSpecStoreApi.Open(repo.Root);
        await StartServer();
        await ToolCall();
        await ToolCall();   // the caches the call itself fills are warm
        List<string> log = Log();
        int listings = Store.Listings;

        time.Advance(TimeSpan.FromSeconds(1.5));
        await ToolCall();

        Assert.Equal(listings, Store.Listings);
        Assert.Empty(Opened(log));
    }

    [Fact(DisplayName = "The focused-window timer lists the tree every 5 seconds and reads nothing while it is unchanged, and stops while no window is focused")]
    public void TheTimerListsWhileFocusedAndReadsNothing()
    {
        OpenAndLoad();
        workspace = new Workspace(new SettingsStore(Path.Combine(temp, "settings.json")), new UiService());
        bool focused = true;
        workspace.StartVerifying(time, () => focused);
        List<string> log = Log();
        int listings = Store.Listings;

        time.Advance(TimeSpan.FromSeconds(4.9));
        Assert.Equal(listings, Store.Listings);
        time.Advance(TimeSpan.FromSeconds(0.1));
        Assert.Equal(listings + 1, Store.Listings);
        time.Advance(TimeSpan.FromSeconds(5));
        Assert.Equal(listings + 2, Store.Listings);
        TitleOfHealth();
        Assert.Empty(Opened(log));   // listed twice, read nothing: the tree did not change

        focused = false;
        time.Advance(TimeSpan.FromSeconds(30));
        Assert.Equal(listings + 2, Store.Listings);

        focused = true;
        OutsideEdit(Health, repo.Read(Health).Replace("title: Health-check endpoint", "title: Health-check endpoint, v2"), minutesAgo: 30);
        time.Advance(TimeSpan.FromSeconds(5));
        Assert.Equal("Health-check endpoint, v2", TitleOfHealth());   // a missed signal is caught by the timer
        Assert.Equal(listings + 3, Store.Listings);
    }

    [Fact(DisplayName = "A verification re-reads only the entries whose size or mtime changed, and a racy stamp is checked again")]
    public void AVerificationReadsOnlyWhatDiffersAndChecksRacyStampsAgain()
    {
        OpenAndLoad();
        string tasks = repo.PathOf(Export);
        int Done() => WorkItemsApi.GetAll().First(w => w.Folder == "export-remittances").Subtasks.Count(s => s.Done);
        Assert.Equal(1, Done());

        // A changed size and time: that folder is read again, no other.
        List<string> log = Log();
        OutsideEdit(Export, "# Tasks\n\n## 1. Export\n\n- [x] 1.1 CSV columns agreed\n- [x] 1.2 Export job\n- [ ] 1.3 Third\n", minutesAgo: 20);
        OpenSpecStoreApi.Verify();
        Assert.Equal(2, Done());
        Assert.NotEmpty(Opened(log));
        Assert.All(Opened(log), p => Assert.StartsWith("openspec/changes/export-remittances/", p));

        // Nothing changed since: another verification reads nothing.
        log = Log();
        OpenSpecStoreApi.Verify();
        Assert.Equal(2, Done());
        Assert.Empty(Opened(log));

        // A racy stamp: written at the moment of the look, and then written again with the same size and the same time.
        DateTime at = time.GetUtcNow().UtcDateTime;
        File.SetLastWriteTimeUtc(tasks, at.AddMilliseconds(-5));
        OpenSpecStoreApi.Verify();   // looked at within the file's own clock tick: the stamp cannot vouch for it
        string same = "# Tasks\n\n## 1. Export\n\n- [x] 1.1 CSV columns agreed\n- [x] 1.2 Export job\n- [x] 1.3 Third\n";
        string before = File.ReadAllText(tasks);
        Assert.Equal(before.Length, same.Length);
        File.WriteAllText(tasks, same);
        File.SetLastWriteTimeUtc(tasks, at.AddMilliseconds(-5));   // the same size and time: the stamps say nothing changed
        OpenSpecStoreApi.Verify();
        Assert.Equal(3, Done());   // checked again, so seen

        // Time moves on: one more check settles it, and from then on nothing is read.
        time.Advance(TimeSpan.FromMinutes(1));
        OpenSpecStoreApi.Verify();
        Done();
        log = Log();
        OpenSpecStoreApi.Verify();
        Done();
        Assert.Empty(Opened(log));
    }
}
