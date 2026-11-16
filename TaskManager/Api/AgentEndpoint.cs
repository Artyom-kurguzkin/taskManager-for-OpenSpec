using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TaskManager.Mcp;

namespace TaskManager.Api;

/// <summary>Where the endpoint listens and how it is guarded. <see cref="AfterWrite"/>: called after a tool call that wrote (the app passes <c>Workspace.Notify</c>).</summary>
public sealed record AgentEndpointOptions(int Port, string Home, string Token, string Version)
{
    public Action? AfterWrite { get; init; }
}

/// <summary>What Settings shows: serving on a port, or not, and why (another window of the app holds the port, or the problem).</summary>
public sealed record AgentEndpointStatus(bool Running, int Port, bool ServedByAnotherWindow, string? Problem = null);

/// <summary>The endpoint's status now (the host registers it; Settings reads it).</summary>
public delegate AgentEndpointStatus AgentStatus();

/// <summary>
/// The app's one door for agents, inside the app's own process: stateless Streamable HTTP MCP on 127.0.0.1 only, a bearer token on everything but GET /health, Origin validated,
/// and server.json (pid, port, version) in the app's folder for the stdio relay to find. There is no second process to start, replace or stop, and nothing here can ask the app to exit.
/// A second copy of the app finds the port taken: it serves nothing, leaves server.json alone and says another window serves agents.
/// </summary>
public sealed class AgentEndpoint : IAsyncDisposable
{
    private readonly WebApplication? app;
    private readonly string? serverJson;
    private readonly string? written;

    private AgentEndpoint(AgentEndpointStatus status, WebApplication? app = null, string? serverJson = null, string? written = null)
    {
        Status = status;
        this.app = app;
        this.serverJson = serverJson;
        this.written = written;
    }

    public AgentEndpointStatus Status { get; }
    public int Port => Status.Port;

    /// <summary>Starts listening, or reports why it does not; it never throws for a port that is taken or a folder that cannot be written.</summary>
    public static async Task<AgentEndpoint> StartAsync(AgentEndpointOptions options)
    {
        byte[] token = Encoding.UTF8.GetBytes(options.Token);
        WebApplicationBuilder builder = WebApplication.CreateBuilder(new WebApplicationOptions { ContentRootPath = AppContext.BaseDirectory });
        builder.WebHost.UseUrls($"http://127.0.0.1:{options.Port}");   // loopback only
        builder.Logging.ClearProviders();
        builder.Logging.AddProvider(new FileLoggerProvider(Path.Combine(options.Home, "logs", $"agents-{DateTime.Now:yyyyMMdd}.log")));
        builder.Logging.AddFilter("Microsoft", LogLevel.Warning);
        builder.Services.Configure<HostOptions>(o => o.ShutdownTimeout = TimeSpan.FromSeconds(2));
        builder.Services.Configure<ConsoleLifetimeOptions>(o => o.SuppressStatusMessages = true);
        builder.Services.AddMcpServer()
            .WithHttpTransport(o => o.Stateless = true)
            .AddBoardTools(new BoardHost(options.AfterWrite, DefaultToWorkingDirectory: false));   // the app serves every repository: a call names its own (the relay does it for the agent)

        WebApplication app = builder.Build();
        // Kestrel does not flow the starter's AsyncLocals into a request. A request runs in the context that started the endpoint instead: the app's own is empty, and a test's carries its own module state.
        ExecutionContext? starter = ExecutionContext.Capture();
        app.Use(async (ctx, next) =>
        {
            if (ctx.Request.Headers.Origin.Count > 0 && !IsLocalOrigin(ctx.Request.Headers.Origin[0]!))
            {   // the spec: a local server MUST validate Origin (DNS rebinding)
                ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
                return;
            }
            bool open = HttpMethods.IsGet(ctx.Request.Method) && ctx.Request.Path == "/health";
            if (!open && !HasToken(ctx.Request.Headers.Authorization.ToString(), token))
            {
                ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
                ctx.Response.Headers.WWWAuthenticate = "Bearer";
                return;
            }
            // what the relay knows about its agent: where it works and what it is called (percent-encoded: a header is ASCII)
            (string?, string?) caller = (Header(ctx, "X-Rtm-Repo"), Header(ctx, "X-Rtm-Agent"));
            Task rest = Task.CompletedTask;
            void Enter() { BoardMcp.Caller.Value = caller; rest = next(); }   // the rest of the pipeline starts in the starter's context and keeps it across its awaits
            if (starter is null) Enter(); else ExecutionContext.Run(starter, _ => Enter(), null);
            await rest;
        });
        app.MapGet("/health", () => Results.Json(new { ok = true, pid = Environment.ProcessId, version = options.Version }));
        app.MapMcp("/mcp");

        try { await app.StartAsync(); }
        catch (Exception e)
        {   // typically: another window of the app already holds the port
            bool other = HeldByAnotherWindow(options.Home, options.Port);   // decided from files and the process table, never from a probe that can time out under load
            app.Logger.LogWarning(e, "could not listen on 127.0.0.1:{Port}", options.Port);
            try { await app.DisposeAsync(); } catch (Exception) { /* never started */ }
            return new AgentEndpoint(new AgentEndpointStatus(false, options.Port, other, other ? null : e.Message));
        }

        int actual = new Uri(app.Urls.First()).Port;   // differs from the asked port for port 0
        string file = Path.Combine(options.Home, "server.json");
        string json = JsonSerializer.Serialize(new { pid = Environment.ProcessId, port = actual, version = options.Version, started = DateTime.UtcNow });
        try
        {
            Directory.CreateDirectory(options.Home);
            File.WriteAllText(file, json);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {   // serving without being found is no use, but the window must still open
            app.Logger.LogError(e, "could not write {File}", file);
            return new AgentEndpoint(new AgentEndpointStatus(true, actual, false, "server.json could not be written; agents cannot find the app"), app);
        }
        app.Logger.LogInformation("serving MCP on http://127.0.0.1:{Port}/mcp (pid {Pid})", actual, Environment.ProcessId);
        return new AgentEndpoint(new AgentEndpointStatus(true, actual, false), app, file, json);
    }

    public async ValueTask DisposeAsync()
    {
        if (app is null) return;
        try
        {
            using CancellationTokenSource limit = new(TimeSpan.FromSeconds(3));
            await app.StopAsync(limit.Token);
        }
        catch (Exception e) when (e is OperationCanceledException or IOException) { }
        await app.DisposeAsync();
        try { if (serverJson is not null && File.Exists(serverJson) && File.ReadAllText(serverJson) == written) File.Delete(serverJson); }   // not a successor's file
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }

    /// <summary>
    /// The port is taken by another window of the app when this home's server.json (written by the one that serves, before its start returned) names that port and a live process.
    /// Whatever the bind failure looked like, this answer does not change.
    /// </summary>
    internal static bool HeldByAnotherWindow(string home, int port)
    {
        try
        {   // read here, not through the host's Paths: the folder is the options' home, and this file does not move with the host's folders
            using JsonDocument d = JsonDocument.Parse(File.ReadAllText(Path.Combine(home, "server.json")));
            if (d.RootElement.GetProperty("port").GetInt32() != port) return false;
            using System.Diagnostics.Process p = System.Diagnostics.Process.GetProcessById(d.RootElement.GetProperty("pid").GetInt32());
            return !p.HasExited;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or KeyNotFoundException or InvalidOperationException or FormatException or ArgumentException or System.ComponentModel.Win32Exception)
        {   // no file, an unreadable one, or no such process: nobody else is known to serve
            return false;
        }
    }

    private static bool IsLocalOrigin(string origin) =>
        Uri.TryCreate(origin, UriKind.Absolute, out Uri? u) && u.Scheme == Uri.UriSchemeHttp && u.Host is "127.0.0.1" or "localhost";

    private static bool HasToken(string header, byte[] token) =>
        header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
        && CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(header[7..].Trim()), token);

    private static string? Header(HttpContext ctx, string name) => ctx.Request.Headers[name].ToString() is { Length: > 0 } v ? Uri.UnescapeDataString(v) : null;
}

/// <summary>Appends one line per log entry to a file; the endpoint's only log sink.</summary>
internal sealed class FileLoggerProvider : ILoggerProvider
{
    private readonly string _file;
    private readonly object _lock = new();

    public FileLoggerProvider(string file)
    {
        _file = file;
        try { Directory.CreateDirectory(Path.GetDirectoryName(file)!); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }

    public ILogger CreateLogger(string category) => new Logger(this, category);
    public void Dispose() { }

    private sealed class Logger(FileLoggerProvider owner, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel level) => level != LogLevel.None;

        public void Log<TState>(LogLevel level, EventId id, TState state, Exception? error, Func<TState, Exception?, string> format)
        {
            string line = $"{DateTime.Now:HH:mm:ss.fff} {level,-11} {category}: {format(state, error)}{(error is null ? "" : " " + error)}{Environment.NewLine}";
            lock (owner._lock)
                try { File.AppendAllText(owner._file, line); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { /* a log line is never worth a crash */ }
        }
    }
}
