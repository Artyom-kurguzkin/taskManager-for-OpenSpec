namespace TaskManager.AppState;

/// <summary>Opens one more full window of this process (side menu, its own panes) on <paramref name="path"/> (a base-relative address, "" for the board).
/// The host registers it; a window without it (tests) cannot open one.</summary>
public delegate (bool Success, string Message) MainWindowLauncher(string path);
