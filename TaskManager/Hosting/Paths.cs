using System.Security.Cryptography;
using System.Text.Json;

namespace TaskManager.Hosting;

/// <summary>Where the headless server keeps its files; RTM_HOME overrides the folder so tests never touch the user's.</summary>
public static class Paths
{
    public const int DefaultPort = 7431;

    public static string Home { get; } = Environment.GetEnvironmentVariable("RTM_HOME") is { Length: > 0 } h
        ? h
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RepoTaskManager");

    public static string ServerJson => Path.Combine(Home, "server.json");
    public static string Logs => Path.Combine(Home, "logs");
    /// <summary>
    /// The assembly version plus the newest build time among the app's own dlls: the version number alone never changes between
    /// builds, and a shim must replace a server that runs older code.
    /// </summary>
    public static string Version { get; } = $"{typeof(Paths).Assembly.GetName().Version}+{Directory.EnumerateFiles(AppContext.BaseDirectory, "*.dll")
        .Where(f => Path.GetFileName(f) is var n && (n.StartsWith("TaskManager", StringComparison.Ordinal) || n is "OpenSpecStore.dll" or "WorkItems.dll" or "Notes.dll" or "Features.dll" or "GitHistory.dll" or "LinkedTestRuns.dll"))
        .Select(File.GetLastWriteTimeUtc).DefaultIfEmpty(DateTime.UnixEpoch).Max():yyyyMMddHHmmss}";

    /// <summary>RTM_PORT lets a shim-started server (and the tests) listen somewhere other than 7431.</summary>
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

    /// <summary>server.json as written by a running server, or null.</summary>
    public static (int Pid, int Port, string? Version)? ReadServerJson()
    {
        try
        {
            using JsonDocument d = JsonDocument.Parse(File.ReadAllText(ServerJson));
            JsonElement r = d.RootElement;
            return (r.GetProperty("pid").GetInt32(), r.GetProperty("port").GetInt32(), r.GetProperty("version").GetString());
        }
        catch { return null; }
    }
}
