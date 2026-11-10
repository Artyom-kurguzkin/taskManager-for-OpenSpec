namespace Notes;

/// <summary>Process-wide state: the parsed notes, the assembled list valid while OpenSpecStore's content version is unchanged and each change's notes/ folder valid while its files are.</summary>
internal static class NotesModule
{
    internal static readonly Dictionary<string, Queries.NotesCache> Cache = new(StringComparer.OrdinalIgnoreCase);   // per repository

    /// <summary>Guards <see cref="Cache"/>: the UI thread and the file watcher's timer both read the notes.</summary>
    internal static readonly object Gate = new();
    internal static int Loads;

    /// <summary>How many change folders' notes were read from disk (tests use it to prove an edit re-reads only its own folder).</summary>
    internal static int FolderReads;
}
