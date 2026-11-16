namespace Notes;

/// <summary>The module's state: the counter tests read. It keeps no cache of the notes: that lives in the host (see <see cref="NotesModule.Reader"/>).</summary>
internal sealed class NotesState
{
    public int FolderReads;
}

/// <summary>
/// Process-wide state. The app has one such state; a test runs against its own (<see cref="Isolated"/>).
/// </summary>
internal static class NotesModule
{
    private static readonly NotesState Shared = new();

    /// <summary>A test seam: the state of the code running from where it was set (one per test); null = the process-wide one, which is all the app ever uses.</summary>
    internal static readonly AsyncLocal<NotesState?> Isolated = new();

    private static NotesState S => Isolated.Value ?? Shared;

    /// <summary>The host's cached read of the notes, and its hand-over of the renumberings made since the last one (see NotesApi.UseNotes); null = read afresh on every call. Not per test: the reads they point to find the test's own caches.</summary>
    internal static Func<PublicDTOs.NotesRead>? Reader;
    internal static Func<IReadOnlyList<(int OldId, int NewId, string Path)>>? Drain;

    /// <summary>How many change folders' notes were read from disk (tests use it to prove an edit re-reads only its own folder).</summary>
    internal static int FolderReads { get => S.FolderReads; set => S.FolderReads = value; }
}
