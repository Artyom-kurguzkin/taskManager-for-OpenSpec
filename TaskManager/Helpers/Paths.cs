using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;

namespace TaskManager.Helpers;

/// <summary>Where the app keeps what agents need to find it (server.json, token, logs); RTM_HOME overrides the folder so tests never touch the user's.</summary>
public static class Paths
{
    public const int DefaultPort = 7431;

    public static string Home { get; } = Environment.GetEnvironmentVariable("RTM_HOME") is { Length: > 0 } h
        ? h
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RepoTaskManager");

    public static string ServerJson => Path.Combine(Home, "server.json");
    public static string Logs => Path.Combine(Home, "logs");

    /// <summary>
    /// The folder the run logs, crash notes and freeze dumps go to: <paramref name="chosen"/> (the user's setting, an absolute path) when it is a folder that is there and can be written, else <see cref="Logs"/>
    /// with a warning that says why. The run markers and publish-stop files are not asked here: they stay in <see cref="Logs"/>, where a publish and the next start look. RTM_HOME moves <see cref="Logs"/>, so
    /// tests and demos have their own default.
    /// </summary>
    public static (string Folder, string? Warning) LogsFor(string? chosen)
    {
        if (string.IsNullOrWhiteSpace(chosen)) return (Logs, null);
        string text = chosen.Trim();
        try
        {
            if (!Path.IsPathFullyQualified(text)) return (Logs, $"The logs folder {text} is not an absolute path; logging to {Logs} instead.");
            string full = Path.GetFullPath(text);
            if (!Directory.Exists(full)) return (Logs, $"The logs folder {full} is not there; logging to {Logs} instead.");
            string probe = Path.Combine(full, $".rtm-write-{Environment.ProcessId}-{Guid.NewGuid():N}.tmp");
            File.WriteAllText(probe, "");
            File.Delete(probe);
            return (full, null);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return (Logs, $"The logs folder {text} cannot be written ({e.Message}); logging to {Logs} instead.");
        }
    }
    /// <summary>One file per repository: what the app read of its change folders, so the next start paints the board at once (see AppState/IndexFile.cs).</summary>
    public static string Index => Path.Combine(Home, "index");
    /// <summary>
    /// The assembly version plus the time this build was made, embedded in the assembly by TaskManager.csproj (AssemblyMetadata "BuildTime"): the version number alone never changes between
    /// builds, and the version says which build of the app answers. It does not depend on loose files beside the exe, which an older publish may have left behind.
    /// </summary>
    public static string Version { get; } = $"{typeof(Paths).Assembly.GetName().Version}+{typeof(Paths).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>().FirstOrDefault(a => a.Key == "BuildTime")?.Value ?? "unknown"}";

    /// <summary>RTM_PORT lets the app listen somewhere other than 7431 (a second copy of it, a demo).</summary>
    public static int Port => int.TryParse(Environment.GetEnvironmentVariable("RTM_PORT"), out int p) ? p : DefaultPort;

    /// <summary>The bearer token: random 32 bytes (base64url), created on first use.</summary>
    public static string Token()
    {
        string file = Path.Combine(Home, "token");
        Directory.CreateDirectory(Home);
        for (int i = 0; i < 40; i++)
        {
            try
            {   // the read is inside the try too: while another process is writing the file, reading it is a sharing violation, not a value
                if (File.Exists(file) && File.ReadAllText(file).Trim() is { Length: > 0 } t) return t;
                // CreateNew: two processes racing to create it agree on the winner's value
                using FileStream fs = new(file, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                using StreamWriter w = new(fs);
                w.Write(Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_'));
            }
            catch (IOException) { Thread.Sleep(50); }
        }
        return File.ReadAllText(file).Trim();
    }

    /// <summary>server.json as written by the running app (in <paramref name="home"/>, default the app's folder), or null.</summary>
    public static (int Pid, int Port, string? Version)? ReadServerJson(string? home = null)
    {
        try
        {
            using JsonDocument d = JsonDocument.Parse(File.ReadAllText(Path.Combine(home ?? Home, "server.json")));
            JsonElement r = d.RootElement;
            return (r.GetProperty("pid").GetInt32(), r.GetProperty("port").GetInt32(), r.GetProperty("version").GetString());
        }
        catch { return null; }
    }
}
