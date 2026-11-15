namespace Notes;

/// <summary>The module's state: the parsed notes per repository and the counters tests read.</summary>
internal sealed class NotesState
{
    public readonly Dictionary<string, Queries.NotesCache> Cache = new(StringComparer.OrdinalIgnoreCase);
    public readonly object Gate = new();
    public int Loads, FolderReads;
}

/// <summary>
/// Process-wide state: the parsed notes, the assembled list valid while OpenSpecStore's content version is unchanged and each change's notes/ folder valid while its files are.
/// The app has one such state; a test runs against its own (<see cref="Isolated"/>).
/// </summary>
internal static class NotesModule
{
    private static readonly NotesState Shared = new();

    /// <summary>A test seam: the state of the code running from where it was set (one per test); null = the process-wide one, which is all the app ever uses.</summary>
    internal static readonly AsyncLocal<NotesState?> Isolated = new();

    private static NotesState S => Isolated.Value ?? Shared;

    internal static Dictionary<string, Queries.NotesCache> Cache => S.Cache;   // per repository

    /// <summary>Guards <see cref="Cache"/>: the UI thread and the file watcher's timer both read the notes.</summary>
    internal static object Gate => S.Gate;
    internal static int Loads { get => S.Loads; set => S.Loads = value; }

    /// <summary>How many change folders' notes were read from disk (tests use it to prove an edit re-reads only its own folder).</summary>
    internal static int FolderReads { get => S.FolderReads; set => S.FolderReads = value; }
}
