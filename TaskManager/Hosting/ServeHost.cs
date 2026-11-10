using System.Globalization;
using System.Net;
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

namespace TaskManager.Hosting;

/// <summary>
/// --serve [--port N] [--idle H] [--parent PID]: the one headless MCP server for every agent and repository; the desktop app starts
/// it with its own pid as --parent, and it then exits when that process ends (and never idles out unless --idle says so).
/// Stateless Streamable HTTP on 127.0.0.1 only, bearer token on everything but GET /health, Origin validated,
/// server.json for discovery, POST /shutdown, exit after H idle hours (0 = never; fractions allowed).
/// </summary>
public static class ServeHost
{
    public static async Task<int> RunAsync(string[] args)
    {
        int port = int.TryParse(Option(args, "--port"), out int p) ? p : Paths.Port;
        int? parentPid = int.TryParse(Option(args, "--parent"), out int pp) ? pp : null;   // the desktop app: the server lives and dies with it
        double idleHours = double.TryParse(Option(args, "--idle"), CultureInfo.InvariantCulture, out double h) ? h : parentPid is null ? 3 : 0;
        TimeSpan idle = TimeSpan.FromHours(idleHours);
        byte[] token = Encoding.UTF8.GetBytes(Paths.Token());

        WebApplicationBuilder builder = WebApplication.CreateBuilder(new WebApplicationOptions { ContentRootPath = AppContext.BaseDirectory });
        builder.WebHost.UseUrls($"http://127.0.0.1:{port}");   // loopback only
        builder.Logging.ClearProviders();   // stdout is never a log
        builder.Logging.AddProvider(new FileLoggerProvider(Path.Combine(Paths.Logs, $"serve-{DateTime.Now:yyyyMMdd}.log")));
        builder.Logging.AddFilter("Microsoft", LogLevel.Warning);
        builder.Services.Configure<HostOptions>(o => o.ShutdownTimeout = TimeSpan.FromSeconds(2));
        builder.Services.AddMcpServer()
            .WithHttpTransport(o => o.Stateless = true)
            .AddBoardTools();
        BoardMcp.DefaultToWorkingDirectory = false;   // every repository's server: a call names its repository (the shim does it for the agent)

        WebApplication app = builder.Build();
        long lastRequest = Environment.TickCount64;

        app.Use(async (ctx, next) =>
        {
            Interlocked.Exchange(ref lastRequest, Environment.TickCount64);
            if (ctx.Request.Headers.Origin.Count > 0 && !IsLocalOrigin(ctx.Request.Headers.Origin[0]))
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
            // what the shim knows about its agent: where it works and what it is called (percent-encoded: a header is ASCII)
            BoardMcp.Caller.Value = (Header(ctx, "X-Rtm-Repo"), Header(ctx, "X-Rtm-Agent"));
            await next();
        });
        app.MapGet("/health", () => Results.Json(new { ok = true, pid = Environment.ProcessId, version = Paths.Version }));
        app.MapPost("/shutdown", (IHostApplicationLifetime life) =>
        {
            _ = Task.Run(async () => { await Task.Delay(100); life.StopApplication(); });   // let the 200 out first
            return Results.Json(new { ok = true });
        });
        app.MapMcp("/mcp");

        try { await app.StartAsync(); }
        catch (Exception e)
        {   // typically: another server already holds the port
            app.Logger.LogError(e, "could not listen on 127.0.0.1:{Port}", port);
            return 1;
        }
        int actual = new Uri(app.Urls.First()).Port;   // differs from `port` for --port 0
        Directory.CreateDirectory(Paths.Home);
        File.WriteAllText(Paths.ServerJson, JsonSerializer.Serialize(new { pid = Environment.ProcessId, port = actual, version = Paths.Version, started = DateTime.UtcNow }));
        app.Logger.LogInformation("serving MCP on http://127.0.0.1:{Port}/mcp (pid {Pid}, idle exit {Idle} h)", actual, Environment.ProcessId, idleHours);

        IHostApplicationLifetime lifetime = app.Lifetime;
        if (parentPid is { } parent)
            _ = Task.Run(async () =>
            {
                try
                {
                    using System.Diagnostics.Process p = System.Diagnostics.Process.GetProcessById(parent);
                    await p.WaitForExitAsync(lifetime.ApplicationStopping);
                }
                catch (ArgumentException) { /* the parent is already gone */ }
                catch (OperationCanceledException) { return; }   // stopping anyway
                app.Logger.LogInformation("parent process {Parent} ended, exiting", parent);
                lifetime.StopApplication();
            });
        if (idle > TimeSpan.Zero)
        {
            TimeSpan poll = TimeSpan.FromMilliseconds(Math.Clamp(idle.TotalMilliseconds / 4, 100, 30000));
            _ = Task.Run(async () =>
            {
                try
                {
                    while (true)
                    {
                        await Task.Delay(poll, lifetime.ApplicationStopping);
                        if (Environment.TickCount64 - Interlocked.Read(ref lastRequest) < idle.TotalMilliseconds) continue;
                        app.Logger.LogInformation("idle for {Idle} h, exiting", idleHours);
                        lifetime.StopApplication();
                        return;
                    }
                }
                catch (OperationCanceledException) { /* stopping anyway */ }
            });
        }

        try { await app.WaitForShutdownAsync(); }
        finally
        {
            if (Paths.ReadServerJson() is { } mine && mine.Pid == Environment.ProcessId) File.Delete(Paths.ServerJson);   // not a successor's file
        }
        return 0;
    }

    private static bool IsLocalOrigin(string origin) =>
        Uri.TryCreate(origin, UriKind.Absolute, out Uri? u) && u.Scheme == Uri.UriSchemeHttp && u.Host is "127.0.0.1" or "localhost";

    private static bool HasToken(string header, byte[] token) =>
        header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
        && CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(header[7..].Trim()), token);

    private static string? Header(HttpContext ctx, string name) => ctx.Request.Headers[name].ToString() is { Length: > 0 } v ? Uri.UnescapeDataString(v) : null;

    private static string? Option(string[] args, string name) => args.SkipWhile(a => a != name).Skip(1).FirstOrDefault();
}

/// <summary>Appends one line per log entry to a file; the server's only log sink (stdout stays silent).</summary>
internal sealed class FileLoggerProvider : ILoggerProvider
{
    private readonly string _file;
    private readonly object _lock = new();

    public FileLoggerProvider(string file)
    {
        _file = file;
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
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
                try { File.AppendAllText(owner._file, line); } catch (IOException) { /* a log line is never worth a crash */ }
        }
    }
}
