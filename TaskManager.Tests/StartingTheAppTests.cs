using System.Text;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenSpecStore;
using Radzen;
using TaskManager.Api;
using TaskManager.AppState;
using TaskManager.Components.Layout;
using TaskManager.Components.Pages;
using TaskManager.Components.Shared;
using TaskManager.Helpers;

namespace TaskManager.Tests;

/// <summary>
/// Starting fast (#wi: start as fast as Notepad): the window is up before the repository is read. The project is registered at once (<see cref="Workspace.Register"/>), then read on the thread pool
/// (<see cref="Workspace.LoadAsync"/>): its headers first, then the rest, git, the duplicate scan and worklog tracking last, each step raising <see cref="Workspace.Changed"/>.
/// No test sleeps: a step holds at <see cref="Workspace.BeforeLoadStep"/> until the test opens its gate, so "before" and "after" are decided by the test, not by timing.
/// </summary>
public sealed class StartingTheAppTests : IDisposable
{
    private readonly TestRepo repo = new();
    private readonly string settingsFile = Path.Combine(Path.GetTempPath(), "rtm-start-" + Guid.NewGuid().ToString("N") + ".json");
    private readonly string logs = Path.Combine(Path.GetTempPath(), "rtm-start-logs-" + Guid.NewGuid().ToString("N")[..8]);
    private readonly string home = Path.Combine(Path.GetTempPath(), "rtm-start-agents-" + Guid.NewGuid().ToString("N")[..8]);
    private readonly Dictionary<string, SemaphoreSlim> gates = [];

    public void Dispose()
    {
        foreach (SemaphoreSlim g in gates.Values) g.Release(100);
        repo.Dispose();
        try { File.Delete(settingsFile); } catch (IOException) { }
        try { Directory.Delete(logs, recursive: true); } catch (IOException) { }
        try { Directory.Delete(home, recursive: true); } catch (IOException) { }
    }

    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(30);   // a bound for a hang, never waited out

    private TimeProvider? clock;   // set by a test that moves the page's 50 ms reload window by hand

    private BunitContext NewContext(out Workspace workspace, params string[] closed)
    {
        BunitContext ctx = new();
        if (clock is not null) ctx.Services.AddSingleton(clock);
        ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        ctx.Services.AddSingleton<UiService>();
        ctx.Services.AddRadzenComponents();
        ctx.Services.AddSingleton(new SettingsStore(settingsFile));
        ctx.Services.AddSingleton<Workspace>();
        workspace = ctx.Services.GetRequiredService<Workspace>();
        foreach (string step in closed) gates[step] = new SemaphoreSlim(0);
        workspace.BeforeLoadStep = (step, _) => { if (gates.TryGetValue(step, out SemaphoreSlim? gate)) gate.Wait(Limit); };   // the pool thread waits here until the test opens the step
        return ctx;
    }

    private void Open(string step) => gates[step].Release(100);

    [Fact(DisplayName = "Opening a repository returns before its files are read, and the board shows a skeleton until they are")]
    public async Task OpeningReturnsBeforeTheFilesAreRead()
    {
        await using BunitContext ctx = NewContext(out Workspace workspace, "headers");
        int readBefore = ItemsCache.FolderReads;

        Assert.True(workspace.Register(repo.Root).Success);
        Task load = workspace.LoadAsync(repo.Root);

        Assert.Equal(readBefore, ItemsCache.FolderReads);   // the pool thread stands at the headers gate: nothing was read
        Assert.True(workspace.IsLoading(repo.Root));
        Assert.True(workspace.Loading);
        Assert.Equal([repo.Root], workspace.Projects);   // the project is known at once: tabs, view and last repository are right
        Assert.Equal(repo.Root, workspace.ViewRoot);
        IRenderedComponent<BoardPage> board = ctx.Render<BoardPage>();
        Assert.NotEmpty(board.FindAll(".skeleton"));
        Assert.Empty(board.FindAll("article.card"));
        Assert.Equal(readBefore, ItemsCache.FolderReads);   // drawing the skeleton read nothing either

        Open("headers");
        await load.WaitAsync(Limit);
        board.WaitForAssertion(() => Assert.NotEmpty(board.FindAll("article.card")), Limit);
        Assert.Empty(board.FindAll(".skeleton"));
        Assert.False(workspace.IsLoading(repo.Root));
    }

    [Fact(DisplayName = "The board shows the cards once the headers are in, before git and the duplicate scan have run")]
    public async Task CardsBeforeGitAndTheScan()
    {
        await using BunitContext ctx = NewContext(out Workspace workspace, "git", "scan");
        Assert.True(workspace.Register(repo.Root).Success);
        IRenderedComponent<BoardPage> board = ctx.Render<BoardPage>();
        Task load = workspace.LoadAsync(repo.Root);

        board.WaitForAssertion(() => Assert.NotEmpty(board.FindAll("article.card")), Limit);   // the headers are in: the board is drawn
        Assert.False(workspace.IsGitReady(repo.Root));   // the pool thread stands at the git gate
        Assert.False(load.IsCompleted);

        Open("git");
        Open("scan");
        await load.WaitAsync(Limit);
        Assert.True(workspace.IsGitReady(repo.Root));
        Assert.NotEmpty(board.FindAll("article.card"));
    }

    [Fact(DisplayName = "The contributor filter says loading until git is ready, then lists the people")]
    public async Task ContributorFilterWaitsForGit()
    {
        await using BunitContext ctx = NewContext(out Workspace workspace, "git");
        ctx.Services.GetRequiredService<SettingsStore>().ToggleMenuSection("filters");   // the Filters block is open
        Assert.True(workspace.Register(repo.Root).Success);
        Task load = workspace.LoadAsync(repo.Root);
        await workspace.WaitLoaded(Limit);   // the headers are in, git is not
        ctx.Render<Overlays>();
        IRenderedComponent<SideNav> nav = ctx.Render<SideNav>();
        nav.ClickWhenReady("button.nav-toggle");   // the expanded menu holds the filters
        nav.WaitForAssertion(() => Assert.Contains("loading", nav.Find("[data-filter='contributor']").TextContent));
        Assert.Contains("loading", nav.Find("[data-filter='commit']").TextContent);

        Open("git");
        await load.WaitAsync(Limit);
        nav.WaitForAssertion(() =>
        {
            Assert.DoesNotContain("loading", nav.Find("[data-filter='contributor']").TextContent);
            Assert.NotNull(nav.Find("[data-filter='contributor'] [aria-label='contributor']"));   // the dropdown that lists everyone and the people of the board
        });
    }

    [Fact(DisplayName = "The startup marks are logged in order: main, window-created, first-render, headers-read, repo-loaded, git-ready, scan-done")]
    public async Task MarksInOrder()
    {
        ILoggerFactory factory = AppLog.Create(logs, console: false, LogLevel.Information);
        AppLog.Use(factory);
        await using BunitContext ctx = NewContext(out Workspace workspace, "headers");
        AppLog.Mark("main");
        AppLog.Mark("window-created");
        Assert.True(workspace.Register(repo.Root).Success);
        IRenderedComponent<BoardPage> board = ctx.Render<BoardPage>();   // draws the skeleton: the first render comes before any file was read
        Task load = workspace.LoadAsync(repo.Root);
        Open("headers");
        await load.WaitAsync(Limit);
        board.Render();
        factory.Dispose();

        string[] marks = [.. File.ReadAllLines(Assert.Single(Directory.GetFiles(logs, "run-*.log"))).Where(l => l.Contains("mark ")).Select(l => l[(l.IndexOf("mark ", StringComparison.Ordinal) + 5)..])];
        string project = Path.GetFileName(repo.Root);
        Assert.Equal(["main", "window-created", "first-render", "headers-read " + project, "repo-loaded " + project, "git-ready " + project, "scan-done " + project],
            marks.Select(m => System.Text.RegularExpressions.Regex.Replace(m, @" \+\d+$", "")).ToArray());
        Assert.All(marks, m => Assert.Matches(@" \+\d+$", m));   // each with the milliseconds since the process started
    }

    [Fact(DisplayName = "An agent tool call that arrives during the load waits for it instead of answering from an empty board")]
    public async Task AgentCallWaitsForTheLoad()
    {
        await using BunitContext ctx = NewContext(out Workspace workspace, "headers");
        Directory.CreateDirectory(home);
        File.WriteAllText(Path.Combine(home, "token"), "t");
        TaskCompletionSource arrived = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Assert.True(workspace.Register(repo.Root).Success);
        Task load = workspace.LoadAsync(repo.Root);
        await using AgentEndpoint endpoint = await AgentEndpoint.StartAsync(new AgentEndpointOptions(0, home, "t", "1.0.0+20260101000000")
        {
            BeforeCall = async () => { arrived.TrySetResult(); await workspace.WaitLoaded(Limit); },
        });
        using HttpClient http = new();
        using HttpRequestMessage req = new(HttpMethod.Post, $"http://127.0.0.1:{endpoint.Port}/mcp")
        {
            Content = new StringContent("""{"jsonrpc":"2.0","id":2,"method":"tools/call","params":{"name":"board_list","arguments":{"limit":5}}}""", Encoding.UTF8, "application/json"),
        };
        req.Headers.Accept.ParseAdd("application/json");
        req.Headers.Accept.ParseAdd("text/event-stream");
        req.Headers.Authorization = new("Bearer", "t");
        req.Headers.Add("X-Rtm-Repo", Uri.EscapeDataString(repo.Root));
        Task<HttpResponseMessage> call = http.SendAsync(req);

        await arrived.Task.WaitAsync(Limit);   // the call reached the endpoint
        Assert.False(call.IsCompleted);        // and stands at the load: the headers gate is shut
        Open("headers");
        using HttpResponseMessage answer = await call.WaitAsync(Limit);
        string text = await answer.Content.ReadAsStringAsync();
        Assert.Contains("Health-check endpoint", text);   // answered from the loaded board
        Assert.False(workspace.IsLoading(repo.Root));
        await load.WaitAsync(Limit);
    }

    [Fact(DisplayName = "index.html paints a skeleton and takes the theme from storage before Blazor boots")]
    public void IndexPaintsASkeleton()
    {
        string html = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "wwwroot", "index.html"));
        int app = html.IndexOf("id=\"app\"", StringComparison.Ordinal);
        int end = html.IndexOf("</body>", StringComparison.Ordinal);
        Assert.True(app > 0 && end > app);
        string inApp = html[app..html.IndexOf("<script", app, StringComparison.Ordinal)];   // what #app holds before any script runs
        Assert.Contains("class=\"skeleton", inApp);   // the first frame is the skeleton, not "Loading…"
        Assert.DoesNotContain("Loading…", inApp);
        int blazor = html.IndexOf("blazor.webview.js", StringComparison.Ordinal);
        string themeScript = System.Text.RegularExpressions.Regex.Match(html, @"<script src=""(js/theme[^""]*)""").Groups[1].Value;   // CSP allows scripts from the app only: the theme is set by a file, before Blazor
        Assert.NotEmpty(themeScript);
        Assert.True(html.IndexOf(themeScript, StringComparison.Ordinal) < blazor);
        string js = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "wwwroot", themeScript));
        Assert.Contains("localStorage", js);
        Assert.Contains("data-theme", js.Replace("dataset.theme", "data-theme"));
        Assert.Contains("localStorage", File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "wwwroot", "js", "app.js")));   // the Settings theme is stored for the next start
    }

    [Fact(DisplayName = "A page whose reload window is open when git becomes ready reads again when the window ends")]
    public async Task GitReadyInsideAReloadWindowStillReloads()
    {
        // the page's 50 ms reload window is moved by hand: the repository step's change opens one, and git's change (which writes nothing to the store, so its stamp is the one the page read) lands inside it
        Microsoft.Extensions.Time.Testing.FakeTimeProvider time = new();
        clock = time;
        await using BunitContext ctx = NewContext(out Workspace workspace, "repo", "git");
        TaskCompletionSource atGit = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Action<string, string> hold = workspace.BeforeLoadStep!;
        workspace.BeforeLoadStep = (step, p) => { if (step == "git") atGit.TrySetResult(); hold(step, p); };
        Assert.True(workspace.Register(repo.Root).Success);
        IRenderedComponent<BoardPage> board = ctx.Render<BoardPage>();
        Task load = workspace.LoadAsync(repo.Root);

        board.WaitForAssertion(() => Assert.NotEmpty(board.FindAll("article.card")), Limit);   // the headers are in
        time.Advance(TimeSpan.FromMilliseconds(60));   // the window the headers' change opened ends
        Open("repo");
        await atGit.Task.WaitAsync(Limit);   // the repository step is done and its change raised
        board.WaitForAssertion(() => Assert.False(workspace.IsGitReady(repo.Root)), Limit);
        int loads = 0;
        board.WaitForAssertion(() => { loads = board.Instance.Loads; Assert.True(loads >= 3); }, Limit);   // the page read for the repository step, which opened a window that stays open
        Open("git");
        await load.WaitAsync(Limit);
        Assert.True(workspace.IsGitReady(repo.Root));   // git's change was raised inside the window: it was held
        time.Advance(TimeSpan.FromMilliseconds(60));   // the window ends
        board.WaitForAssertion(() => Assert.True(board.Instance.Loads > loads), Limit);   // the page read again for git (the commit filter and the uncommitted marks)
    }

    [Fact(DisplayName = "A header sort saved with the list is applied when the rows arrive from the load, and stays when git becomes ready")]
    public async Task SavedHeaderSortSurvivesTheLoad()
    {
        // two items in one status: the high one (123) has the newest worklog time, so the board's own order puts it first and an ascending sort on Updated puts it last
        repo.Write("openspec/changes/export-remittances/.openspec.yaml", repo.Read("openspec/changes/export-remittances/.openspec.yaml").Replace("status: Planned", "status: In Progress"));
        repo.Write("openspec/changes/add-health-check/events.log", "2040-01-05T10:00:00 | test | created | newest\n");
        repo.Write("openspec/changes/export-remittances/events.log", "2040-01-01T10:00:00 | test | created | oldest\n");
        await using BunitContext ctx = NewContext(out Workspace workspace, "git");
        SettingsStore settings = ctx.Services.GetRequiredService<SettingsStore>();
        string sortedByUpdated = """{"Columns":[{"Property":"Modified","SortIndex":0,"SortOrder":0}],"Groups":[]}""";   // Radzen's DataGridSettings: the header sort on Updated, ascending
        settings.SaveBoard(repo.Root, settings.Board(repo.Root) with { View = "list", ListGroupBy = "status", ListSettings = sortedByUpdated });
        Assert.True(workspace.Register(repo.Root).Success);
        ctx.Render<Overlays>();
        IRenderedComponent<BoardPage> board = ctx.Render<BoardPage>();
        Task load = workspace.LoadAsync(repo.Root);

        static List<int> Shown(IRenderedComponent<BoardPage> b) => [.. b.FindAll("tr[data-id]").Select(r => int.Parse(r.GetAttribute("data-id")!)).Where(id => id is 107 or 123)];   // the two of In Progress, in the order shown
        static string? Sort(IRenderedComponent<BoardPage> b) => b.FindAll("th.rz-sortable-column").First(h => h.TextContent.Contains("Updated")).GetAttribute("aria-sort");
        // The sort is applied after the rows are first drawn (Radzen's after-render, then a second draw), and the first sort draw is cold work: on an idle machine it takes ~0.7 s, and at the start of a full
        // run, with every class starting at once, seconds. While it runs the page's dispatcher is busy, so a wait's check is not even run (bUnit: "Check count: 0"). So no wait here is a time budget:
        // each stands until its state is reached, bounded only by Limit (a hang), never waited out. A sort lost for good still fails: it is never reached.
        board.WaitForAssertion(() => Assert.NotEmpty(board.FindAll("tr[data-id]")), Limit);   // the headers are in: the list is drawn, git is not ready yet
        board.WaitForAssertion(() => { Assert.Equal("ascending", Sort(board)); Assert.Equal([107, 123], Shown(board)); }, Limit);

        int loads = board.Instance.Loads;
        Open("git");
        await load.WaitAsync(Limit);
        board.WaitForAssertion(() => Assert.True(board.Instance.Loads > loads), Limit);
        board.WaitForAssertion(() => { Assert.Equal("ascending", Sort(board)); Assert.Equal([107, 123], Shown(board)); }, Limit);
    }
}
