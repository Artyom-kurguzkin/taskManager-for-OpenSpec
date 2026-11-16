using System.IO.Pipelines;
using System.Net;
using System.Text;
using System.Text.Json;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using OpenSpecStore;
using Radzen;
using TaskManager.Api;
using TaskManager.Components.Pages;
using TaskManager.Mcp;
using TaskManager.AppState;
using TaskManager.BackgroundJobs;
using TaskManager.Helpers;

namespace TaskManager.Tests;

/// <summary>
/// The app is the board's one server: agents reach it through the stdio relay (<c>--mcp</c>), which only forwards. The listener runs in this
/// process on a port the OS picks, with its own home folder, so nothing here touches a running app. No test waits for a clock: a call is answered
/// after its write, and the pages' timers run on a clock the test moves by hand.
/// </summary>
public sealed class ServingAgentsFromTheAppTests : IAsyncLifetime
{
    private readonly TestRepo repo = new();
    private readonly string home = Path.Combine(Path.GetTempPath(), "rtm-agents-" + Guid.NewGuid().ToString("N")[..8]);
    private readonly string settingsFile = Path.Combine(Path.GetTempPath(), "rtm-agents-" + Guid.NewGuid().ToString("N") + ".json");
    private const string Token = "test-token";
    private readonly List<AgentEndpoint> endpoints = [];
    private readonly List<IAsyncDisposable> sessions = [];
    private readonly HttpClient http = new();

    public Task InitializeAsync()
    {
        Directory.CreateDirectory(home);
        File.WriteAllText(Path.Combine(home, "token"), Token);   // where the relay reads it, as the app makes it
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        foreach (IAsyncDisposable s in sessions) await s.DisposeAsync();
        foreach (AgentEndpoint e in endpoints) await e.DisposeAsync();
        http.Dispose();
        repo.Dispose();
        try { Directory.Delete(home, recursive: true); } catch (IOException) { }
        try { File.Delete(settingsFile); } catch (IOException) { }
    }

    private async Task<AgentEndpoint> Serve(Action? afterWrite = null, int port = 0)
    {
        AgentEndpoint e = await AgentEndpoint.StartAsync(new AgentEndpointOptions(port, home, Token, "1.0.0+20260101000000") { AfterWrite = afterWrite });
        endpoints.Add(e);
        return e;
    }

    private static string Call(string tool, object args, int id = 2) =>
        JsonSerializer.Serialize(new { jsonrpc = "2.0", id, method = "tools/call", @params = new { name = tool, arguments = args } });

    private async Task<string> Post(AgentEndpoint endpoint, string json, string agent = "Tester")
    {
        using HttpRequestMessage req = new(HttpMethod.Post, $"http://127.0.0.1:{endpoint.Port}/mcp") { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        req.Headers.Accept.ParseAdd("application/json");
        req.Headers.Accept.ParseAdd("text/event-stream");
        req.Headers.Authorization = new("Bearer", Token);
        req.Headers.Add("X-Rtm-Repo", Uri.EscapeDataString(repo.Root));
        req.Headers.Add("X-Rtm-Agent", agent);
        using HttpResponseMessage res = await http.SendAsync(req);
        return await res.Content.ReadAsStringAsync();
    }

    private string[] NotesNamed(string title) =>
        [.. Directory.EnumerateFiles(Path.Combine(repo.Root, "openspec", "changes"), "n*.md", SearchOption.AllDirectories).Where(f => File.ReadAllText(f).Contains(title))];

    private PipeRelay StartRelay(string agent = "Tester")
    {
        PipeRelay relay = new(home, repo.Root, agent);
        sessions.Add(relay);
        return relay;
    }

    [Fact(DisplayName = "The app serves agents itself: no second process")]
    public async Task TheAppServesAgentsItself()
    {
        AgentEndpoint endpoint = await Serve();

        using JsonDocument health = JsonDocument.Parse(await http.GetStringAsync($"http://127.0.0.1:{endpoint.Port}/health"));
        Assert.Equal(Environment.ProcessId, health.RootElement.GetProperty("pid").GetInt32());   // the process that asked is the one that answers: nothing was spawned
        using JsonDocument file = JsonDocument.Parse(File.ReadAllText(Path.Combine(home, "server.json")));
        Assert.Equal(Environment.ProcessId, file.RootElement.GetProperty("pid").GetInt32());
        Assert.Equal(endpoint.Port, file.RootElement.GetProperty("port").GetInt32());
        Assert.Equal(new AgentEndpointStatus(true, endpoint.Port, false), endpoint.Status);

        Assert.Contains("board_list", await StartRelay().Ask("""{"jsonrpc":"2.0","id":1,"method":"tools/list","params":{}}"""));   // the relay found it from server.json alone
        Assert.DoesNotContain(typeof(AgentEndpoint).Assembly.GetTypes(), t => t.Name is "ServeHost" or "McpServerControl");   // the headless server and its start/stop switch are gone
    }

    [Fact(DisplayName = "An agent's write shows in the open window without a refresh")]
    public async Task AnAgentsWriteShowsInTheOpenWindow()
    {
        FakeTimeProvider time = new();
        await using BunitContext ctx = new();
        ctx.Services.AddSingleton<TimeProvider>(time);
        ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        ctx.Services.AddSingleton<UiService>();
        ctx.Services.AddRadzenComponents();
        ctx.Services.AddSingleton(new SettingsStore(settingsFile));
        ctx.Services.AddSingleton<Workspace>();
        Workspace workspace = ctx.Services.GetRequiredService<Workspace>();
        Assert.True(workspace.Open(repo.Root).Success);
        IRenderedComponent<WorkItemPage> page = ctx.Render<WorkItemPage>(p => p.Add(c => c.Id, 123));
        Assert.Contains("Health-check endpoint", page.Markup);
        Assert.DoesNotContain("Written by the agent", page.Markup);
        AgentEndpoint endpoint = await Serve(workspace.Notify);

        string answer = await Post(endpoint, Call("board_note", new { id = 123, title = "Written by the agent", body = "Seen in the window." }));

        Assert.DoesNotContain("\"isError\":true", answer);
        for (int passed = 0; passed < 300; passed += 50)   // the page's coalescing window, moved by hand
        {
            time.Advance(TimeSpan.FromMilliseconds(50));
            await page.InvokeAsync(() => { });
        }
        Assert.Contains("Written by the agent", page.Markup);   // no Notify, no reload, no navigation from the test
    }

    [Fact(DisplayName = "An edit made in the app after an agent's write is not lost and does not overwrite it")]
    public async Task AnEditAfterAnAgentsWriteDoesNotOverwriteIt()
    {
        Assert.True(OpenSpecStoreApi.Open(repo.Root).Success);
        AgentEndpoint endpoint = await Serve();
        string file = repo.PathOf("openspec/changes/add-health-check/.openspec.yaml");
        string shown = OpenSpecStoreApi.ReadText(file);   // the window read the file and has it open in an editor

        string answer = await Post(endpoint, Call("board_update", new { id = 123, priority = "low" }));   // the agent changes the same file
        Assert.DoesNotContain("\"isError\":true", answer);
        string agents = File.ReadAllText(file);
        Assert.Contains("priority: low", agents);

        (bool saved, string message) = OpenSpecStoreApi.WriteText(file, shown.Replace("# keep this comment", "# keep this comment (edited in the app)"));

        Assert.False(saved);                                   // the app's edit, made on what it read before the agent wrote, is refused, not applied over it
        Assert.Contains("changed on disk", message);
        Assert.Equal(agents, File.ReadAllText(file));          // the agent's write is whole
        string now = OpenSpecStoreApi.ReadText(file);          // the window shows what is there now ...
        Assert.True(OpenSpecStoreApi.WriteText(file, now.Replace("# keep this comment", "# keep this comment (edited in the app)")).Success);   // ... and the edit made on that goes through
        Assert.Contains("priority: low", File.ReadAllText(file));
        Assert.Contains("(edited in the app)", File.ReadAllText(file));
    }

    [Fact(DisplayName = "A second app instance leaves the first one serving")]
    public async Task ASecondInstanceLeavesTheFirstServing()
    {
        AgentEndpoint first = await Serve();
        string before = File.ReadAllText(Path.Combine(home, "server.json"));

        AgentEndpoint second = await Serve(port: first.Port);   // the same port: the first window's app holds it

        Assert.False(second.Status.Running);
        Assert.True(second.Status.ServedByAnotherWindow);
        Assert.Equal(first.Port, second.Status.Port);
        Assert.Equal(before, File.ReadAllText(Path.Combine(home, "server.json")));   // not overwritten
        await second.DisposeAsync();
        Assert.Equal(before, File.ReadAllText(Path.Combine(home, "server.json")));   // and not removed when the second one ends
        using HttpRequestMessage leave = new(HttpMethod.Post, $"http://127.0.0.1:{first.Port}/shutdown");
        leave.Headers.Authorization = new("Bearer", Token);
        using HttpResponseMessage asked = await http.SendAsync(leave);
        Assert.Equal(HttpStatusCode.NotFound, asked.StatusCode);   // nobody can ask it to leave, not even with the token
        Assert.True(first.Status.Running);
        Assert.Contains("board_list", await Post(first, """{"jsonrpc":"2.0","id":1,"method":"tools/list","params":{}}"""));
    }

    [Fact(DisplayName = "A taken port is another window's when this home's server.json names it and a live process, whether or not the holder answers")]
    public async Task ATakenPortIsAnotherWindowsFromServerJsonAlone()
    {
        using System.Net.Sockets.TcpListener silent = new(IPAddress.Loopback, 0);   // holds the port and never answers HTTP: a /health probe could only time out
        silent.Start();
        int port = ((IPEndPoint)silent.LocalEndpoint).Port;
        File.WriteAllText(Path.Combine(home, "server.json"), JsonSerializer.Serialize(new { pid = Environment.ProcessId, port, version = "1.0.0", started = DateTime.UtcNow }));

        AgentEndpoint second = await Serve(port: port);

        Assert.False(second.Status.Running);
        Assert.True(second.Status.ServedByAnotherWindow);
        Assert.Null(second.Status.Problem);
    }

    [Fact(DisplayName = "A taken port is not another window's when server.json is missing, names another port, or names a process that is gone")]
    public async Task ATakenPortWithoutALiveServerJsonIsAProblem()
    {
        using System.Net.Sockets.TcpListener foreign = new(IPAddress.Loopback, 0);
        foreign.Start();
        int port = ((IPEndPoint)foreign.LocalEndpoint).Port;
        string file = Path.Combine(home, "server.json");

        AgentEndpoint none = await Serve(port: port);
        Assert.False(none.Status.ServedByAnotherWindow);
        Assert.NotNull(none.Status.Problem);

        File.WriteAllText(file, JsonSerializer.Serialize(new { pid = Environment.ProcessId, port = port + 1, version = "1.0.0" }));
        Assert.False((await Serve(port: port)).Status.ServedByAnotherWindow);

        File.WriteAllText(file, JsonSerializer.Serialize(new { pid = int.MaxValue, port, version = "1.0.0" }));
        Assert.False((await Serve(port: port)).Status.ServedByAnotherWindow);
    }

    [Fact(DisplayName = "With the app closed, an agent is told to open it")]
    public async Task WithTheAppClosedAnAgentIsToldToOpenIt()
    {
        PipeRelay relay = StartRelay();
        string call = Call("board_note", new { id = 123, title = "Sent while closed", body = "Waits for the app." });

        string refused = await relay.Ask(call);

        Assert.Contains("\"error\"", refused);
        Assert.Contains(McpShim.AppNotRunning, refused);
        Assert.Contains("\"id\":2", refused);
        Assert.False(File.Exists(Path.Combine(home, "server.json")));   // the relay started nothing
        Assert.Empty(NotesNamed("Sent while closed"));
        Assert.Contains("repo-tasks", await relay.Ask("""{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-06-18","capabilities":{},"clientInfo":{"name":"t","version":"1"}}}"""));   // the client still connects

        await Serve();   // the app opens: the same relay, no restart
        string answer = await relay.Ask(call);

        Assert.DoesNotContain("\"error\"", answer);
        Assert.DoesNotContain("\"isError\":true", answer);
        Assert.Contains("by: Tester", File.ReadAllText(Assert.Single(NotesNamed("Sent while closed"))));
    }
}

/// <summary>The relay as the agent starts it, on pipes instead of stdin and stdout: lines in, lines out.</summary>
internal sealed class PipeRelay : IAsyncDisposable
{
    private readonly StreamWriter toRelay;
    private readonly StreamReader fromRelay;
    private readonly Task running;

    public PipeRelay(string home, string repoRoot, string agent)
    {
        Pipe input = new(), output = new();
        toRelay = new StreamWriter(input.Writer.AsStream(), new UTF8Encoding(false)) { AutoFlush = true, NewLine = "\n" };
        fromRelay = new StreamReader(output.Reader.AsStream(), Encoding.UTF8);
        StreamReader stdin = new(input.Reader.AsStream(), Encoding.UTF8);
        StreamWriter stdout = new(output.Writer.AsStream(), new UTF8Encoding(false)) { AutoFlush = true, NewLine = "\n" };
        running = Task.Run(async () =>
        {
            await new McpShim(home, repoRoot, agent).RunAsync(stdin, stdout);
            await output.Writer.CompleteAsync();
        });
    }

    public async Task<string> Ask(string line)
    {
        await toRelay.WriteLineAsync(line);
        return await fromRelay.ReadLineAsync() ?? throw new Xunit.Sdk.XunitException("the relay closed its output");
    }

    public async ValueTask DisposeAsync()
    {
        toRelay.Close();   // the agent ended: stdin reaches EOF and the relay returns
        await running;
    }
}
