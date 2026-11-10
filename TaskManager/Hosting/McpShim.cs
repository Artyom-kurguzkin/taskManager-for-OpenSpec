using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using TaskManager.Mcp;

namespace TaskManager.Hosting;

/// <summary>
/// --mcp [--agent X]: the stdio server agents start from .mcp.json. Finds the headless server (server.json + /health) and
/// relays JSON-RPC lines to POST /mcp unchanged. It never starts a server: the desktop app does, and the server ends with it.
/// While no server answers, initialize, ping and tools/list are answered here (so the client connects) and every other request
/// gets an error asking the user to open the app; the next request finds the server once it is up, with no restart of the shim.
/// stdout is protocol only: everything else goes to stderr.
/// </summary>
public static class McpShim
{
    public const string AppNotRunning = "The Repo Tasks app is not running. Ask the user to open it; the board tools start with it.";

    private static readonly HttpClient Http = new() { Timeout = Timeout.InfiniteTimeSpan };
    private static readonly SemaphoreSlim EnsureLock = new(1);
    private static readonly string Token = Paths.Token();
    private static readonly UTF8Encoding Utf8 = new(false);
    private static int? _port;

    public static async Task<int> RunAsync(string[] args)
    {
        string? agent = args.SkipWhile(a => a != "--agent").Skip(1).FirstOrDefault();
        if (agent is not null) Environment.SetEnvironmentVariable("RTM_AGENT", agent);
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("RTM_REPO"))) Environment.SetEnvironmentVariable("RTM_REPO", Environment.CurrentDirectory);

        _port = await FindServerAsync();
        if (_port is null) Console.Error.WriteLine("rtm-mcp: the Repo Tasks app is not running; tool calls ask for it until it is");

        using StreamReader stdin = new(Console.OpenStandardInput(), Utf8);
        using StreamWriter stdout = new(Console.OpenStandardOutput(), Utf8) { AutoFlush = true, NewLine = "\n" };
        SemaphoreSlim write = new(1);
        async Task Write(string line)
        {
            await write.WaitAsync();
            try { await stdout.WriteLineAsync(line); } finally { write.Release(); }
        }

        List<Task> pending = [];
        while (await stdin.ReadLineAsync() is { } line)   // newline-delimited JSON-RPC, as the SDK's stdio transport frames it
        {
            if (line.Trim().Length == 0) continue;
            pending.RemoveAll(t => t.IsCompleted);
            pending.Add(Task.Run(() => RelayAsync(line, Write)));   // concurrent: a slow tool call must not hold up a cancellation
        }
        await Task.WhenAll(pending);
        return 0;
    }

    /// <summary>Relays one message to the server; when none answers, answers it here (or with the "open the app" error).</summary>
    private static async Task RelayAsync(string line, Func<string, Task> write)
    {
        try
        {
            _port ??= await FindServerAsync();
            if (_port is not null)
            {
                try { await PostAsync(line, write); return; }
                catch (HttpRequestException) { _port = await FindServerAsync(); }   // the app was closed, or restarted on another port
                if (_port is not null) { await PostAsync(line, write); return; }
            }
            if (Offline(line) is { } answer) await write(answer);
        }
        catch (Exception e)
        {   // a request must always get an answer, or the agent waits forever
            Console.Error.WriteLine($"rtm-mcp: {e.Message}");
            if (JsonNode.Parse(line)?["id"] is { } id) await write(Error(id, e.Message));
        }
    }

    private static string Error(JsonNode id, string message) =>
        new JsonObject { ["jsonrpc"] = "2.0", ["id"] = id.DeepClone(), ["error"] = new JsonObject { ["code"] = -32603, ["message"] = message } }.ToJsonString();

    /// <summary>The reply when no server answers: null for a notification, the connection handshake answered here, else the error.</summary>
    private static string? Offline(string line)
    {
        if (JsonNode.Parse(line) is not JsonObject request || request["id"] is not { } id) return null;
        JsonNode? result = request["method"]?.GetValue<string>() switch
        {
            "initialize" => Initialize(request["params"]?["protocolVersion"]?.GetValue<string>()),
            "ping" => new JsonObject(),
            "tools/list" => JsonSerializer.SerializeToNode(new ListToolsResult { Tools = [.. Options.Value.ToolCollection!.Select(t => t.ProtocolTool)] }, McpJsonUtilities.DefaultOptions),
            _ => null,
        };
        return result is null ? Error(id, AppNotRunning)
            : new JsonObject { ["jsonrpc"] = "2.0", ["id"] = id.DeepClone(), ["result"] = result }.ToJsonString();
    }

    private static readonly Lazy<IOptions<McpServerOptions>> LazyOptions = new(() =>
    {   // the tool list and server info are the server's own definitions, built without a transport
        ServiceCollection services = new();
        services.AddMcpServer().AddBoardTools();
        return services.BuildServiceProvider().GetRequiredService<IOptions<McpServerOptions>>();
    });
    private static IOptions<McpServerOptions> Options => LazyOptions.Value;

    private static JsonObject Initialize(string? protocolVersion) => new()
    {
        ["protocolVersion"] = protocolVersion ?? "2025-06-18",
        ["capabilities"] = new JsonObject { ["tools"] = new JsonObject() },
        ["serverInfo"] = new JsonObject { ["name"] = Options.Value.ServerInfo?.Name, ["version"] = Options.Value.ServerInfo?.Version },
        ["instructions"] = Options.Value.ServerInstructions,
    };

    private static async Task PostAsync(string line, Func<string, Task> write)
    {
        using HttpRequestMessage req = new(HttpMethod.Post, $"http://127.0.0.1:{_port}/mcp") { Content = new StringContent(line, Utf8, "application/json") };
        req.Headers.Authorization = new("Bearer", Token);
        req.Headers.Accept.ParseAdd("application/json");
        req.Headers.Accept.ParseAdd("text/event-stream");
        req.Headers.Add("X-Rtm-Repo", Uri.EscapeDataString(Environment.GetEnvironmentVariable("RTM_REPO") ?? ""));   // the tools default `repo` to this; encoded, a path need not be ASCII
        if (Environment.GetEnvironmentVariable("RTM_AGENT") is { Length: > 0 } agent) req.Headers.Add("X-Rtm-Agent", Uri.EscapeDataString(agent));

        using HttpResponseMessage res = await Http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead);
        if (res.StatusCode == HttpStatusCode.Accepted) return;   // a notification: nothing to say back
        if (res.Content.Headers.ContentType?.MediaType == "text/event-stream")
        {
            using StreamReader sse = new(await res.Content.ReadAsStreamAsync(), Utf8);
            while (await sse.ReadLineAsync() is { } l)
                if (l.StartsWith("data:", StringComparison.Ordinal) && l[5..].Trim() is { Length: > 0 } data) await write(data);
            return;
        }
        string body = (await res.Content.ReadAsStringAsync()).ReplaceLineEndings("");   // JSON has no raw newlines inside strings
        if (body.Contains("\"jsonrpc\"")) await write(body);
        else if (!res.IsSuccessStatusCode) throw new InvalidOperationException($"the board server answered HTTP {(int)res.StatusCode}");
    }

    /// <summary>The port of a server that answers /health (server.json first), of any version, or null. Starts nothing.</summary>
    internal static async Task<int?> FindServerAsync() =>
        Paths.ReadServerJson() is { } info && await VersionAtAsync(info.Port) is not null ? info.Port : null;

    /// <summary>The arguments that start the headless server: with a parent pid it ends with that process and never idles out.</summary>
    public static string ServeArgs(int? parentPid)
    {
        string head = Path.GetFileNameWithoutExtension(Environment.ProcessPath!).Equals("dotnet", StringComparison.OrdinalIgnoreCase)
            ? $"\"{typeof(McpShim).Assembly.Location}\" --serve" : "--serve";
        return parentPid is { } pid ? $"{head} --parent {pid} --idle 0" : head;
    }

    /// <summary>
    /// The port of a running server of this version: the existing one, an older one replaced, or a freshly started one.
    /// Only the desktop app calls it (with its own pid as <paramref name="parentPid"/>): the shim never starts a server.
    /// </summary>
    internal static async Task<int?> EnsureServerAsync(int? parentPid = null)
    {
        await EnsureLock.WaitAsync();
        try
        {
            if (Paths.ReadServerJson() is { } info && await VersionAtAsync(info.Port) is { } version)
            {
                if (version == Paths.Version) return info.Port;
                await ShutdownAsync(info.Port);   // Bazel's way: an older server is asked to leave, then ours starts
                for (int i = 0; i < 50 && await VersionAtAsync(info.Port) is not null; i++) await Task.Delay(100);
            }
            // The server must not hold the stdio pipes of whoever starts it (an agent never sees EOF after a shim exits; checked:
            // it hangs). CreateProcess hands every inheritable handle to the child, so make ours non-inheritable, redirect
            // all three of the child's streams to pipes of its own, and close our ends at once.
            if (OperatingSystem.IsWindows()) foreach (int std in new[] { -10, -11, -12 }) SetHandleInformation(GetStdHandle(std), 1, 0);
            using (Process server = Process.Start(new ProcessStartInfo(Environment.ProcessPath!, ServeArgs(parentPid))
            { UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true })!)
            {
                server.StandardInput.Close();
                server.StandardOutput.Close();
                server.StandardError.Close();
            }
            for (int i = 0; i < 100; i++)
            {
                await Task.Delay(100);
                if (Paths.ReadServerJson() is { } now && await VersionAtAsync(now.Port) == Paths.Version) return now.Port;
            }
            return null;
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            Console.Error.WriteLine($"rtm-mcp: could not start the board server: {e.Message}");
            return null;
        }
        finally { EnsureLock.Release(); }
    }

    [System.Runtime.InteropServices.DllImport("kernel32.dll")] private static extern IntPtr GetStdHandle(int id);
    [System.Runtime.InteropServices.DllImport("kernel32.dll")] private static extern bool SetHandleInformation(IntPtr handle, int mask, int flags);

    /// <summary>The version a server on this port reports at /health, or null when nothing answers.</summary>
    internal static async Task<string?> VersionAtAsync(int port)
    {
        try
        {
            using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(2));
            using JsonDocument d = JsonDocument.Parse(await Http.GetStringAsync($"http://127.0.0.1:{port}/health", timeout.Token));
            return d.RootElement.GetProperty("version").GetString();
        }
        catch { return null; }
    }

    internal static async Task ShutdownAsync(int port)
    {
        try
        {
            using HttpRequestMessage req = new(HttpMethod.Post, $"http://127.0.0.1:{port}/shutdown");
            req.Headers.Authorization = new("Bearer", Token);
            using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(2));
            (await Http.SendAsync(req, timeout.Token)).Dispose();
        }
        catch { /* already gone */ }
    }
}
