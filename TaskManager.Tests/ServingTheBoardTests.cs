using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Json;

namespace TaskManager.Tests;

/// <summary>
/// The headless server (<c>--serve</c>) and the stdio shim (<c>--mcp</c>), run as real processes with their own RTM_HOME.
/// Ports 7450-7459 belong to this class. Excluded from the fast suite: <c>dotnet test --filter "Category=Server"</c>.
/// </summary>
[Trait("Category", "Server")]
public sealed class ServingTheBoardTests : IDisposable
{
    private const string ToolsList = """{"jsonrpc":"2.0","id":1,"method":"tools/list","params":{}}""";

    private readonly string _home = Path.Combine(Path.GetTempPath(), "rtm-serve-" + Guid.NewGuid().ToString("N")[..8]);
    private readonly List<Process> _started = [];
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(5) };

    public ServingTheBoardTests() => Directory.CreateDirectory(_home);

    public void Dispose()
    {
        foreach (Process p in _started) Kill(p);
        // servers started by a shim are not ours: the pid is in server.json
        if (ReadServerJson() is { Pid: > 4 } info) KillPid(info.Pid);
        _http.Dispose();
        try { Directory.Delete(_home, recursive: true); } catch { /* a process still releasing its log file */ }
    }

    // ---------- the server ----------

    [Fact(DisplayName = "A headless server answers /health without a token and writes server.json with its pid, port and version")]
    public async Task HealthAndServerJson()
    {
        Process server = StartServer(7450);
        await WaitForHealth(7450);

        using JsonDocument health = JsonDocument.Parse(await _http.GetStringAsync("http://127.0.0.1:7450/health"));
        Assert.True(health.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal(server.Id, health.RootElement.GetProperty("pid").GetInt32());

        ServerInfo info = ReadServerJson() ?? throw new Xunit.Sdk.XunitException("server.json was not written");
        Assert.Equal(server.Id, info.Pid);
        Assert.Equal(7450, info.Port);
        Assert.False(string.IsNullOrWhiteSpace(info.Version));
        Assert.Equal(info.Version, health.RootElement.GetProperty("version").GetString());
        Assert.Matches(@"\+\d{14}$", info.Version);   // the build time is part of it, so a rebuilt app replaces a running older server
    }

    [Fact(DisplayName = "A request without the bearer token gets 401, and a request from a foreign Origin gets 403")]
    public async Task TokenAndOrigin()
    {
        StartServer(7451);
        await WaitForHealth(7451);
        string token = File.ReadAllText(Path.Combine(_home, "token")).Trim();

        Assert.Equal(HttpStatusCode.Unauthorized, (await Post(7451, ToolsList, token: null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Post(7451, ToolsList, token: "wrong")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Post(7451, ToolsList, token, origin: "http://evil.example")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Post(7451, ToolsList, token, origin: "http://127.0.0.1.evil.example")).StatusCode);

        HttpResponseMessage ok = await Post(7451, ToolsList, token, origin: "http://localhost:7451");
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        Assert.Contains("board_list", await ok.Content.ReadAsStringAsync());
    }

    [Fact(DisplayName = "POST /shutdown with the token stops the server")]
    public async Task ShutdownStopsTheServer()
    {
        Process server = StartServer(7452);
        await WaitForHealth(7452);
        string token = File.ReadAllText(Path.Combine(_home, "token")).Trim();

        Assert.Equal(HttpStatusCode.Unauthorized, (await Post(7452, "", token: null, path: "/shutdown")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Post(7452, "", token, path: "/shutdown")).StatusCode);

        Assert.True(server.WaitForExit(5000), "the server is still running 5 s after /shutdown");
        Assert.Null(ReadServerJson());
    }

    [Fact(DisplayName = "A headless server exits by itself after the idle time")]
    public async Task IdleExit()
    {
        Process server = StartServer(7453, "--idle 0.001");   // 3.6 s
        await WaitForHealth(7453);
        Assert.True(server.WaitForExit(30000), "the server is still running long after its idle time");
        Assert.Null(ReadServerJson());
    }

    // ---------- the shim ----------

    [Fact(DisplayName = "The stdio shim starts a headless server when none is running, forwards tools/list to it, and reuses it on the next start")]
    public async Task ShimStartsAndReuses()
    {
        Assert.Null(ReadServerJson());
        Process shim1 = StartShim(7454);
        Assert.Contains("board_list", await Exchange(shim1, ToolsList));
        ServerInfo first = ReadServerJson() ?? throw new Xunit.Sdk.XunitException("the shim did not start a server");
        Assert.Equal(7454, first.Port);
        await WaitForHealth(7454);
        shim1.StandardInput.Close();
        Assert.True(shim1.WaitForExit(10000), "the shim did not exit when stdin closed");
        // the server it started must not hold the shim's stdout open, or an agent never sees the shim end
        Assert.True(shim1.StandardOutput.ReadToEndAsync().Wait(5000), "stdout never reached EOF: the server inherited the shim's pipe");

        Process shim2 = StartShim(7454);
        Assert.Contains("board_list", await Exchange(shim2, ToolsList));
        Assert.Equal(first.Pid, ReadServerJson()?.Pid);   // same server, not a new one
        shim2.StandardInput.Close();
        Assert.True(shim2.WaitForExit(10000));
    }

    [Fact(DisplayName = "The stdio shim replaces a running server whose version differs")]
    public async Task ShimReplacesAnOlderServer()
    {
        // a stub "old server": /health reports another version, /shutdown is recorded and ends it
        using HttpListener stub = new();
        stub.Prefixes.Add("http://127.0.0.1:7455/");
        stub.Start();
        bool shutdownCalled = false;
        string? shutdownAuth = null;
        Task stubLoop = Task.Run(async () =>
        {
            while (stub.IsListening)
            {
                HttpListenerContext c;
                try { c = await stub.GetContextAsync(); } catch { return; }
                string path = c.Request.Url!.AbsolutePath;
                byte[] body = Encoding.UTF8.GetBytes(path == "/health" ? """{"ok":true,"pid":1,"version":"0.0.0.1"}""" : "{}");
                if (path == "/shutdown") { shutdownCalled = true; shutdownAuth = c.Request.Headers["Authorization"]; }
                c.Response.ContentType = "application/json";
                await c.Response.OutputStream.WriteAsync(body);
                c.Response.Close();
                if (path == "/shutdown") { stub.Stop(); return; }
            }
        });
        File.WriteAllText(Path.Combine(_home, "server.json"),
            """{"pid":1,"port":7455,"version":"0.0.0.1","started":"2020-01-01T00:00:00Z"}""");

        Process shim = StartShim(7456);
        Assert.Contains("board_list", await Exchange(shim, ToolsList));

        Assert.True(shutdownCalled, "the old server was not asked to shut down");
        Assert.StartsWith("Bearer ", shutdownAuth);
        await WaitForHealth(7456);
        ServerInfo now = ReadServerJson() ?? throw new Xunit.Sdk.XunitException("server.json is gone");
        Assert.Equal(7456, now.Port);
        Assert.NotEqual("0.0.0.1", now.Version);
        shim.StandardInput.Close();
        shim.WaitForExit(10000);
        await stubLoop;
    }

    // ---------- the tools behind them ----------

    private const string NoteCall = """{"jsonrpc":"2.0","id":2,"method":"tools/call","params":{"name":"board_note","arguments":{"id":123,"title":"From the shim","body":"Written over MCP."}}}""";

    [Fact(DisplayName = "Two shims in two repositories share one server, and each writes to its own repository under its own agent name")]
    public async Task EachShimWritesToItsOwnRepository()
    {
        string first = CopySampleRepo(), second = CopySampleRepo();
        Process shim1 = StartShim(7457, first, "Tester"), shim2 = StartShim(7457, second, "Codex");

        string answer1 = await Exchange(shim1, NoteCall), answer2 = await Exchange(shim2, NoteCall);

        Assert.DoesNotContain("\"isError\":true", answer1);
        Assert.DoesNotContain("\"isError\":true", answer2);
        Assert.Contains("by: Tester", File.ReadAllText(Assert.Single(NotesNamed(first, "From the shim"))));
        Assert.Contains("by: Codex", File.ReadAllText(Assert.Single(NotesNamed(second, "From the shim"))));
        Assert.Single(Process.GetProcessesByName("TaskManager"), p => p.Id == ReadServerJson()?.Pid);   // one server for both
    }

    [Fact(DisplayName = "A headless server has no default repository: a call that names none is refused with a message asking for repo")]
    public async Task NoDefaultRepository()
    {
        StartServer(7458);
        await WaitForHealth(7458);
        string token = File.ReadAllText(Path.Combine(_home, "token")).Trim();

        string answer = await (await Post(7458, """{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"board_list","arguments":{}}}""", token)).Content.ReadAsStringAsync();

        Assert.Contains("\"isError\":true", answer);
        Assert.Contains("serves every repository", answer);
    }

    private string CopySampleRepo()
    {
        string root = Path.Combine(_home, "repo-" + Guid.NewGuid().ToString("N")[..6]);
        void Copy(string from, string to)
        {
            Directory.CreateDirectory(to);
            foreach (string f in Directory.GetFiles(from)) File.Copy(f, Path.Combine(to, Path.GetFileName(f)));
            foreach (string d in Directory.GetDirectories(from)) Copy(d, Path.Combine(to, Path.GetFileName(d)));
        }
        Copy(Path.Combine(AppContext.BaseDirectory, "Fixtures", "sample-repo"), root);
        return root;
    }

    private static string[] NotesNamed(string root, string title) =>
        [.. Directory.EnumerateFiles(Path.Combine(root, "openspec", "changes"), "n*.md", SearchOption.AllDirectories).Where(f => File.ReadAllText(f).Contains(title))];

    // ---------- plumbing ----------

    private record ServerInfo(int Pid, int Port, string? Version);

    private ServerInfo? ReadServerJson()
    {
        try
        {
            using JsonDocument d = JsonDocument.Parse(File.ReadAllText(Path.Combine(_home, "server.json")));
            JsonElement r = d.RootElement;
            return new ServerInfo(r.GetProperty("pid").GetInt32(), r.GetProperty("port").GetInt32(), r.GetProperty("version").GetString());
        }
        catch { return null; }
    }

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

    private Process Launch(string args, int defaultPort, bool redirect, string? cwd = null)
    {
        ProcessStartInfo psi = Exe(args);
        psi.UseShellExecute = false;
        psi.CreateNoWindow = true;
        psi.WorkingDirectory = cwd ?? _home;
        psi.Environment["RTM_HOME"] = _home;
        psi.Environment["RTM_PORT"] = defaultPort.ToString();   // the port a shim-started server listens on
        if (redirect) { psi.RedirectStandardInput = psi.RedirectStandardOutput = true; psi.StandardOutputEncoding = Encoding.UTF8; }
        Process p = Process.Start(psi)!;
        _started.Add(p);
        return p;
    }

    private Process StartServer(int port, string extra = "") => Launch($"--serve --port {port} {extra}", port, redirect: false);

    private Process StartShim(int serverPort, string? cwd = null, string agent = "Tester") => Launch($"--mcp --agent {agent}", serverPort, redirect: true, cwd);

    private static async Task<string> Exchange(Process shim, string line)
    {
        await shim.StandardInput.WriteLineAsync(line);
        await shim.StandardInput.FlushAsync();
        Task<string?> read = shim.StandardOutput.ReadLineAsync();
        if (await Task.WhenAny(read, Task.Delay(30000)) != read) throw new Xunit.Sdk.XunitException("the shim did not answer within 30 s");
        return read.Result ?? throw new Xunit.Sdk.XunitException("the shim closed stdout");
    }

    private async Task WaitForHealth(int port)
    {
        for (int i = 0; i < 100; i++)
        {
            try { if ((await _http.GetAsync($"http://127.0.0.1:{port}/health")).IsSuccessStatusCode) return; } catch { /* not up yet */ }
            await Task.Delay(100);
        }
        throw new Xunit.Sdk.XunitException($"no server answered /health on port {port}");
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

    private static void KillPid(int pid)
    {
        try { Kill(Process.GetProcessById(pid)); } catch { /* not running */ }
    }
}
