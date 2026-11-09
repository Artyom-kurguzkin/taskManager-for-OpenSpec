namespace TaskManager.Hosting;

/// <summary>What the Settings page shows about the headless MCP server.</summary>
public sealed record McpServerState(bool Running, int Port, int Pid);

/// <summary>
/// How the desktop app looks at, starts and stops the headless MCP server (tests pass fakes). The app does not host the
/// endpoint itself: the modules keep one current repository per process, so a tool call for another repository would
/// switch it under the window. The server is its own process and the window sees its writes through the file watcher.
/// </summary>
public sealed record McpServerControl(Func<Task<McpServerState>> Status, Func<Task> Start, Func<Task> Stop)
{
    public static McpServerControl Default { get; } = new(Probe, () => McpShim.EnsureServerAsync(), async () =>
    {
        if (Paths.ReadServerJson() is not { } info) return;
        await McpShim.ShutdownAsync(info.Port);
        for (int i = 0; i < 30 && await McpShim.VersionAtAsync(info.Port) is not null; i++) await Task.Delay(100);
    });

    private static async Task<McpServerState> Probe() =>
        Paths.ReadServerJson() is { } info && await McpShim.VersionAtAsync(info.Port) is not null ? new(true, info.Port, info.Pid) : new(false, 0, 0);
}
