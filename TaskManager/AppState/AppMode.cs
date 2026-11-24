using WorkItems;
using WorkItems.PublicDTOs;
using TaskManager.AppState;

namespace TaskManager.AppState;

/// <summary>What one window of the app is. Detached: a separate window holding one work item of one project (address <see cref="StartPath"/>); it shows only that page.
/// Each window has its own; the main window's has neither.</summary>
public sealed class AppMode
{
    public bool Detached { get; set; }

    /// <summary>The address to open first (item/&lt;n&gt;); taken once by the layout.</summary>
    public string? StartPath { get; set; }

    /// <summary>The project this window reads and writes whatever the main window switches to meanwhile (null: the active one, as in the main window).</summary>
    public string? Repo { get; set; }
}

/// <summary>Opens a work item in a separate window of this process.</summary>
public delegate (bool Success, string Message) WindowLauncher(string repoRoot, int itemId);

/// <summary>What the host needs to build one more window: its own mode and dialogs, and the title. <see cref="Closed"/> is called when the window is gone.</summary>
public sealed record WindowRequest(AppMode Mode, UiService Ui, string Title, Action Closed);

/// <summary>Builds and shows a window for the request (the host: one Photino window on the UI thread). May throw; the caller says so as a message.</summary>
public delegate void WindowFactory(WindowRequest request);

/// <summary>
/// Separate windows of this one process. A window gets its own mode and its own <see cref="UiService"/> (its dialogs and toasts), and shares everything else
/// with the main window: the repositories are per call and the author per call, so a second window on an open project is safe.
/// </summary>
public sealed class ItemWindows(Workspace workspace, WindowFactory create, Func<UiService>? newUi = null)
{
    /// <summary>One more full window (side menu, any page, its own panes) on <paramref name="path"/>, a base-relative address ("" for the board). Not detached, and not pinned to a project:
    /// it follows the shared active project like the main window (a per-window project would need every page to read its own).</summary>
    public (bool Success, string Message) OpenFull(string path)
    {
        UiService ui = newUi?.Invoke() ?? new();
        IDisposable attached = workspace.AttachWindow(ui);
        try
        {
            create(new WindowRequest(new AppMode { StartPath = path }, ui, "Repo tasks", attached.Dispose));
            return (true, "");
        }
        catch (Exception e)
        {
            attached.Dispose();
            return (false, $"Couldn't open a new window: {e.Message}");
        }
    }

    public (bool Success, string Message) Open(string repoRoot, int itemId)
    {
        if (!workspace.Projects.Any(p => SettingsStore.Key(p) == SettingsStore.Key(repoRoot)))
            return (false, $"Couldn't open a separate window: {Path.GetFileName(repoRoot)} isn't open.");
        WorkItem? item = workspace.In(repoRoot, () => ItemsCache.Get(itemId));
        if (item is null) return (false, $"Couldn't open a separate window: #wi{itemId} isn't in {Path.GetFileName(repoRoot)}.");
        UiService ui = newUi?.Invoke() ?? new();
        IDisposable attached = workspace.AttachWindow(ui);
        try
        {
            create(new WindowRequest(new AppMode { Detached = true, StartPath = $"item/{itemId}", Repo = repoRoot }, ui, $"#wi{itemId} {item.Title} — Repo tasks", attached.Dispose));
            return (true, "");
        }
        catch (Exception e)
        {
            attached.Dispose();
            return (false, $"Couldn't open a separate window: {e.Message}");
        }
    }
}
