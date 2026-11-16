using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Json;
using TaskManager.Api;
using TaskManager.Helpers;

namespace TaskManager.Tests;

/// <summary>
/// The app's endpoint for agents and the stdio relay in front of it. The endpoint runs in this process on a port the OS picks, with its own
/// folder (RTM_HOME's counterpart), so nothing here touches a running app; the one real <c>--mcp</c> process test is excluded from the fast suite:
/// <c>dotnet test --filter "Category=Server"</c>.
/// </summary>
public sealed class ServingTheBoardTests : IAsyncLifetime
{
    private const string ToolsList = """{"jsonrpc":"2.0","id":1,"method":"tools/list","params":{}}""";
    private const string Token = "a-token-for-this-test";

    private readonly string _home = Path.Combine(Path.GetTempPath(), "rtm-serve-" + Guid.NewGuid().ToString("N")[..8]);
    private readonly List<TestRepo> _repos = [];
    private readonly List<IAsyncDisposable> _sessions = [];
    private readonly List<Process> _started = [];
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(30) };
    private AgentEndpoint _endpoint = null!;

    public async Task InitializeAsync()
    {
        Directory.CreateDirectory(_home);
        File.WriteAllText(Path.Combine(_home, "token"), Token);   // where the relay reads it, as the app makes it
        _endpoint = await AgentEndpoint.StartAsync(new AgentEndpointOptions(0, _home, Token, Paths.Version));
    }

    public async Task DisposeAsync()
    {
        foreach (IAsyncDisposable s in _sessions) await s.DisposeAsync();
        foreach (Process p in _started) Kill(p);
        await _endpoint.DisposeAsync();
        _http.Dispose();
        foreach (TestRepo r in _repos) r.Dispose();
        try { Directory.Delete(_home, recursive: true); } catch { /* a process still releasing its log file */ }
    }

    // ---------- the endpoint ----------

    [Fact(DisplayName = "The app's endpoint answers /health without a token and writes server.json with its pid, port and version")]
    public async Task HealthAndServerJson()
    {
        using JsonDocument health = JsonDocument.Parse(await _http.GetStringAsync($"http://127.0.0.1:{_endpoint.Port}/health"));
        Assert.True(health.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal(Environment.ProcessId, health.RootElement.GetProperty("pid").GetInt32());

        (int Pid, int Port, string? Version) info = Paths.ReadServerJson(_home) ?? throw new Xunit.Sdk.XunitException("server.json was not written");
        Assert.Equal(Environment.ProcessId, info.Pid);
        Assert.Equal(_endpoint.Port, info.Port);
        Assert.False(string.IsNullOrWhiteSpace(info.Version));
        Assert.Equal(info.Version, health.RootElement.GetProperty("version").GetString());
        Assert.Matches(@"\+\d{14}$", info.Version);   // the build time is part of it, so a rebuilt app can be told from an older one
    }

    [Fact(DisplayName = "A request without the bearer token gets 401, and a request from a foreign Origin gets 403")]
    public async Task TokenAndOrigin()
    {
        int port = _endpoint.Port;

        Assert.Equal(HttpStatusCode.Unauthorized, (await Post(port, ToolsList, token: null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Post(port, ToolsList, token: "wrong")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Post(port, ToolsList, Token, origin: "http://evil.example")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Post(port, ToolsList, Token, origin: "http://127.0.0.1.evil.example")).StatusCode);

        HttpResponseMessage ok = await Post(port, ToolsList, Token, origin: $"http://localhost:{port}");
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        Assert.Contains("board_list", await ok.Content.ReadAsStringAsync());
    }

    [Fact(DisplayName = "The endpoint has no /shutdown: nobody can ask the app to exit over HTTP, with or without the token")]
    public async Task NoShutdown()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await Post(_endpoint.Port, "", token: null, path: "/shutdown")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Post(_endpoint.Port, "", Token, path: "/shutdown")).StatusCode);
        Assert.True(_endpoint.Status.Running);
    }

    // ---------- the relay ----------

    [Fact(DisplayName = "The stdio relay starts nothing: it forwards to the app that is running")]
    public async Task RelayForwardsToTheRunningApp()
    {
        PipeRelay relay = Relay(Repo().Root, "Tester");

        Assert.Contains("board_list", await relay.Ask(ToolsList));
        Assert.Equal(Environment.ProcessId, Paths.ReadServerJson(_home)?.Pid);   // still the one process that is serving
    }

    [Fact(DisplayName = "The real --mcp process relays to the app and ends when its stdin closes")]
    [Trait("Category", "Server")]
    public async Task TheRealRelayProcess()
    {
        TestRepo repo = Repo();
        Process shim = Launch("--mcp --agent Tester", repo.Root);

        Assert.Contains("board_list", await Exchange(shim, ToolsList));
        string answer = await Exchange(shim, NoteCall);
        Assert.DoesNotContain("\"isError\":true", answer);
        Assert.Contains("by: Tester", File.ReadAllText(Assert.Single(NotesNamed(repo.Root, "From the shim"))));
        shim.StandardInput.Close();
        Assert.True(shim.WaitForExit(10000), "the relay did not exit when stdin closed");
        Assert.True(shim.StandardOutput.ReadToEndAsync().Wait(5000), "stdout never reached EOF");
    }

    // ---------- the tools behind them ----------

    private const string NoteCall = """{"jsonrpc":"2.0","id":2,"method":"tools/call","params":{"name":"board_note","arguments":{"id":123,"title":"From the shim","body":"Written over MCP."}}}""";

    [Fact(DisplayName = "Two relays in two repositories share one endpoint, and each writes to its own repository under its own agent name")]
    public async Task EachRelayWritesToItsOwnRepository()
    {
        TestRepo first = Repo(), second = Repo();
        PipeRelay relay1 = Relay(first.Root, "Tester"), relay2 = Relay(second.Root, "Codex");

        string answer1 = await relay1.Ask(NoteCall), answer2 = await relay2.Ask(NoteCall);

        Assert.DoesNotContain("\"isError\":true", answer1);
        Assert.DoesNotContain("\"isError\":true", answer2);
        Assert.Contains("by: Tester", File.ReadAllText(Assert.Single(NotesNamed(first.Root, "From the shim"))));
        Assert.Contains("by: Codex", File.ReadAllText(Assert.Single(NotesNamed(second.Root, "From the shim"))));
    }

    [Fact(DisplayName = "The app has no default repository for a call: one that names none is refused with a message asking for repo")]
    public async Task NoDefaultRepository()
    {
        string answer = await (await Post(_endpoint.Port, """{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"board_list","arguments":{}}}""", Token)).Content.ReadAsStringAsync();

        Assert.Contains("\"isError\":true", answer);
        Assert.Contains("serves every repository", answer);
    }

    private TestRepo Repo()
    {
        TestRepo repo = new();
        _repos.Add(repo);
        return repo;
    }

    private PipeRelay Relay(string repo, string agent)
    {
        PipeRelay relay = new(_home, repo, agent);
        _sessions.Add(relay);
        return relay;
    }

    private static string[] NotesNamed(string root, string title) =>
        [.. Directory.EnumerateFiles(Path.Combine(root, "openspec", "changes"), "n*.md", SearchOption.AllDirectories).Where(f => File.ReadAllText(f).Contains(title))];

    // ---------- plumbing ----------

    /// <summary>The apphost next to the test assembly, else the Debug build of the TaskManager project.</summary>
    private static ProcessStartInfo Exe(string args)
    {
        string beside = Path.Combine(AppContext.BaseDirectory, "TaskManager.exe");
        if (File.Exists(beside)) return new ProcessStartInfo(beside, args);
        string dll = Path.Combine(AppContext.BaseDirectory, "TaskManager.dll");
        if (File.Exists(dll) && File.Exists(Path.Combine(AppContext.BaseDirectory, "TaskManager.runtimeconfig.json")))
            return new ProcessStartInfo("dotnet", $"\"{dll}\" {args}");
        string? dir = AppContext.BaseDirectory;
        while (dir is not null && !File.Exists(Path.Combine(dir, "RepoTaskManager.slnx"))) dir = Path.GetDirectoryName(dir);
        return new ProcessStartInfo(Path.Combine(dir ?? ".", "TaskManager", "bin", "Debug", "net10.0", "TaskManager.exe"), args);
    }

    private Process Launch(string args, string cwd)
    {
        ProcessStartInfo psi = Exe(args);
        psi.UseShellExecute = false;
        psi.CreateNoWindow = true;
        psi.WorkingDirectory = cwd;
        psi.Environment["RTM_HOME"] = _home;
        psi.RedirectStandardInput = psi.RedirectStandardOutput = true;
        psi.StandardOutputEncoding = Encoding.UTF8;
        Process p = Process.Start(psi)!;
        _started.Add(p);
        return p;
    }

    private static async Task<string> Exchange(Process shim, string line)
    {
        await shim.StandardInput.WriteLineAsync(line);
        await shim.StandardInput.FlushAsync();
        return await shim.StandardOutput.ReadLineAsync() ?? throw new Xunit.Sdk.XunitException("the relay closed stdout");
    }

    private Task<HttpResponseMessage> Post(int port, string json, string? token, string? origin = null, string path = "/mcp")
    {
        HttpRequestMessage req = new(HttpMethod.Post, $"http://127.0.0.1:{port}{path}") { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        req.Headers.Accept.ParseAdd("application/json");
        req.Headers.Accept.ParseAdd("text/event-stream");
        if (token is not null) req.Headers.Authorization = new("Bearer", token);
        if (origin is not null) req.Headers.TryAddWithoutValidation("Origin", origin);
        return _http.SendAsync(req);
    }

    private static void Kill(Process p)
    {
        try { if (!p.HasExited) p.Kill(entireProcessTree: true); } catch { /* already gone */ }
    }
}
