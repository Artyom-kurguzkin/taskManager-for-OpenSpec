using TaskManager.AppState;

namespace TaskManager.BackgroundJobs;

/// <summary>
/// What happens when the user closes the app while board files are still inside the settle window (written too recently to sync). The window's close button asks <see cref="MayClose"/>;
/// the dialog is the app's own choice dialog, so a test answers it with <see cref="UiService.Close"/> and nothing here touches the OS.
/// "Wait and sync" waits until the files settle (at most <see cref="BoardSyncService.SettleCap"/>), then runs the exit sync, then allows the close; "Close anyway" allows it at once
/// (the exit sync that follows takes what was settled and leaves the rest, as before); closing the dialog keeps the app open.
/// </summary>
public sealed class CloseGuard(BoardSyncService sync, UiService ui)
{
    public const string WaitAndSync = "wait";
    public const string CloseAnyway = "close";

    /// <summary>True when the window may close now; false when the user chose to stay.</summary>
    public async Task<bool> MayClose(CancellationToken stop = default)
    {
        IReadOnlyList<SettlingFolder> settling = sync.Settling();
        if (settling.Count == 0) return true;
        string? choice = await ui.Choose("Files are still settling", Text(settling), [(WaitAndSync, "Wait and sync"), (CloseAnyway, "Close anyway")]);
        switch (choice)
        {
            case WaitAndSync:
                await sync.WaitForSettle(stop);
                await sync.SyncOnExit(stop);   // the exit sync, with its own time limit
                return true;
            case CloseAnyway: return true;
            default: return false;
        }
    }

    private static string Text(IReadOnlyList<SettlingFolder> settling)
    {
        int files = settling.Sum(f => f.Files.Count);
        string folders = string.Join(", ", settling.Select(f => f.Folder));
        string names = string.Join(", ", settling.SelectMany(f => f.Files).Take(6)) + (files > 6 ? $" and {files - 6} more" : "");
        return $"{files} file{(files == 1 ? " was" : "s were")} changed a moment ago and {(files == 1 ? "is" : "are")} not synced yet ({(settling.Count == 1 ? "folder" : "folders")} {folders}): {names}. "
            + "Closing now leaves them out of the sync, and the other computers get none of that folder until this app runs again.";
    }
}
