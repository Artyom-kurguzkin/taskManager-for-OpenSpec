using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TaskManager.Mcp;

namespace TaskManager.Hosting;

/// <summary>
/// --mcp [--agent X]: the stdio server agents start from .mcp.json. Finds the headless server (server.json + /health),
/// starts it when absent, replaces it when its version differs, and relays JSON-RPC lines to POST /mcp unchanged.
/// When the server cannot be reached it serves the tools in this process instead.
/// stdout is protocol only: everything else goes to stderr.
/// </summary>
public static class McpShim
{
    private static readonly HttpClient Http = new() { Timeout = Timeout.InfiniteTimeSpan };
    private static readonly SemaphoreSlim EnsureLock = new(1);
    private static readonly string Token = Paths.Token();
    private static readonly UTF8Encoding Utf8 = new(false);
    private static int _port;

    public static async Task<int> RunAsync(string[] args)
    {
        string? agent = args.SkipWhile(a => a != "--agent").Skip(1).FirstOrDefault();
        if (agent is not null) Environment.SetEnvironmentVariable("RTM_AGENT", agent);
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("RTM_REPO"))) Environment.SetEnvironmentVariable("RTM_REPO", Environment.CurrentDirectory);

        if (await EnsureServerAsync() is { } port) _port = port;
        else
        {
            Console.Error.WriteLine("rtm-mcp: no headless server reachable; serving in this process");
            await ServeInProcessAsync();
            return 0;
        }

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

    /// <summary>POSTs one message; once more after re-ensuring the server if the connection failed (the server was killed or went idle).</summary>
    private static async Task RelayAsync(string line, Func<string, Task> write)
    {
        try
        {
            try { await PostAsync(line, write); }
            catch (HttpRequestException)
            {
                _port = await EnsureServerAsync() ?? throw new InvalidOperationException("the board server is gone and could not be restarted");
                await PostAsync(line, write);
            }
        }
        catch (Exception e)
        {   // a request must always get an answer, or the agent waits forever
            Console.Error.WriteLine($"rtm-mcp: {e.Message}");
            if (JsonNode.Parse(line)?["id"] is { } id)
                await write(new JsonObject { ["jsonrpc"] = "2.0", ["id"] = id.DeepClone(), ["error"] = new JsonObject { ["code"] = -32603, ["message"] = e.Message } }.ToJsonString());
        }
    }

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

    /// <summary>The port of a running server of this version: the existing one, an older one replaced, or a freshly started one.</summary>
    internal static async Task<int?> EnsureServerAsync()
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
            string exe = Environment.ProcessPath!;
            string args = Path.GetFileNameWithoutExtension(exe).Equals("dotnet", StringComparison.OrdinalIgnoreCase)
                ? $"\"{typeof(McpShim).Assembly.Location}\" --serve" : "--serve";
            // The server must not hold this shim's stdio pipes, or the agent never sees EOF after the shim exits (checked:
            // it hangs). CreateProcess hands every inheritable handle to the child, so make ours non-inheritable, redirect
            // all three of the child's streams to pipes of its own, and close our ends at once.
            if (OperatingSystem.IsWindows()) foreach (int std in new[] { -10, -11, -12 }) SetHandleInformation(GetStdHandle(std), 1, 0);
            using (Process server = Process.Start(new ProcessStartInfo(exe, args)
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

    private static async Task ServeInProcessAsync()
    {
        HostApplicationBuilder b = Host.CreateApplicationBuilder();
        b.Logging.ClearProviders();
        b.Logging.AddConsole(o => o.LogToStandardErrorThreshold = LogLevel.Trace);   // stdout belongs to the protocol
        b.Services.AddMcpServer().WithStdioServerTransport().AddBoardTools();
        await b.Build().RunAsync();
    }
}
