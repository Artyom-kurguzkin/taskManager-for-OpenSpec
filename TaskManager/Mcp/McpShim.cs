using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using TaskManager.Helpers;

namespace TaskManager.Mcp;

/// <summary>
/// --mcp [--agent X]: the stdio entry agents start from .mcp.json, a tiny process that holds no logic. It finds the app's endpoint (server.json + /health) and
/// relays JSON-RPC lines to POST /mcp unchanged, adding where its agent works and what it is called. It never starts anything: the app serves, in its own process.
/// While no app answers, initialize, ping and tools/list are answered here (so the client connects) and every other request
/// gets an error asking the user to open the app; the next request finds the app once it is up, with no restart of the relay.
/// stdout is protocol only: everything else goes to stderr.
/// </summary>
public sealed class McpShim(string home, string? repo, string? agent)
{
    public const string AppNotRunning = "The Repo Tasks app is not running. Ask the user to open it; the board tools start with it.";

    private static readonly HttpClient Http = new() { Timeout = Timeout.InfiniteTimeSpan };
    private static readonly UTF8Encoding Utf8 = new(false);
    private int? _port;

    /// <summary>The process entry: stdin and stdout, the working directory as the repository, RTM_HOME for the app's folder.</summary>
    public static async Task<int> RunAsync(string[] args)
    {
        string? agent = args.SkipWhile(a => a != "--agent").Skip(1).FirstOrDefault() ?? Environment.GetEnvironmentVariable("RTM_AGENT");
        string repo = Environment.GetEnvironmentVariable("RTM_REPO") is { Length: > 0 } own ? own : Environment.CurrentDirectory;
        using StreamReader stdin = new(Console.OpenStandardInput(), Utf8);
        using StreamWriter stdout = new(Console.OpenStandardOutput(), Utf8);
        return await new McpShim(Paths.Home, repo, agent).RunAsync(stdin, stdout);
    }

    /// <summary>Relays lines from <paramref name="stdin"/> until it ends; answers go to <paramref name="stdout"/>.</summary>
    public async Task<int> RunAsync(TextReader stdin, TextWriter stdout)
    {
        _port = await FindServerAsync();
        if (_port is null) Console.Error.WriteLine("rtm-mcp: the Repo Tasks app is not running; tool calls ask for it until it is");

        SemaphoreSlim write = new(1);
        async Task Write(string line)
        {
            await write.WaitAsync();
            try { await stdout.WriteLineAsync(line); await stdout.FlushAsync(); } finally { write.Release(); }
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
    private async Task RelayAsync(string line, Func<string, Task> write)
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

    private async Task PostAsync(string line, Func<string, Task> write)
    {
        using HttpRequestMessage req = new(HttpMethod.Post, $"http://127.0.0.1:{_port}/mcp") { Content = new StringContent(line, Utf8, "application/json") };
        req.Headers.Authorization = new("Bearer", Token());
        req.Headers.Accept.ParseAdd("application/json");
        req.Headers.Accept.ParseAdd("text/event-stream");
        req.Headers.Add("X-Rtm-Repo", Uri.EscapeDataString(repo ?? ""));   // the tools default `repo` to this; encoded, a path need not be ASCII
        if (agent is { Length: > 0 }) req.Headers.Add("X-Rtm-Agent", Uri.EscapeDataString(agent));

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

    /// <summary>The port of an app that answers /health (server.json first), of any version, or null. Starts nothing.</summary>
    private async Task<int?> FindServerAsync() =>
        Paths.ReadServerJson(home) is { } info && await VersionAtAsync(info.Port) is not null ? info.Port : null;

    /// <summary>The bearer token the app made in its folder; read each time, as the app may start after the relay does.</summary>
    private string Token()
    {
        try { return File.ReadAllText(Path.Combine(home, "token")).Trim(); }
        catch (IOException) { return ""; }
    }

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
}
