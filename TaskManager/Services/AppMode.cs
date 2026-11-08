using System.Diagnostics;

namespace TaskManager.Services;

/// <summary>How this process runs. Detached: a second app process holding one work item in its own window (started with
/// --repo and --item); it shows only that page and leaves the settings file to the main window.</summary>
public sealed class AppMode
{
    public bool Detached { get; set; }

    /// <summary>The address to open first (item/&lt;n&gt;); taken once by the layout.</summary>
    public string? StartPath { get; set; }
}

/// <summary>Opens a work item in a separate window: a second app process on the same repository.</summary>
public delegate (bool Success, string Message) WindowLauncher(string repoRoot, int itemId);

public static class WindowLaunchers
{
    /// <summary>The same executable with --repo and --item. It inherits the environment (settings file, WebView2 variables).</summary>
    public static (bool Success, string Message) Default(string repoRoot, int itemId)
    {
        try
        {
            string exe = Environment.ProcessPath ?? throw new InvalidOperationException("the executable's path is unknown");
            ProcessStartInfo start = new(exe) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(exe) ?? "" };
            start.ArgumentList.Add("--repo");
            start.ArgumentList.Add(repoRoot);
            start.ArgumentList.Add("--item");
            start.ArgumentList.Add(itemId.ToString());
            Process.Start(start)?.Dispose();
            return (true, "");
        }
        catch (Exception e) { return (false, $"Couldn't open a separate window: {e.Message}"); }
    }
}
