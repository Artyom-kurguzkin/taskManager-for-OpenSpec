namespace WorkItems;

/// <summary>The module's state: the swappable steps and the counters tests read. It keeps no cache of the board: that lives in the host (see <see cref="WorkItemsModule.Board"/>).</summary>
internal sealed class WorkItemsState
{
    public Func<string, IReadOnlyList<string>, (bool Success, string Message, bool CouldRun)> Archive = Infrastructure.OpenSpecCli.Archive;
    public int GitEpoch;
    public Func<(string Token, Func<string, IReadOnlyList<string>> AuthorsOf)?>? Git;
    public Func<string?>? Actor;
    public int FolderParses, DuplicateScans;
}

/// <summary>
/// Process-wide state. Only the archive step is swappable (tests replace the CLI call).
/// The app has one such state; a test runs against its own (<see cref="Isolated"/>), so tests that run at the same time never share a step or a counter.
/// </summary>
internal static class WorkItemsModule
{
    private static readonly WorkItemsState Shared = new();

    /// <summary>A test seam: the state of the code running from where it was set (one per test); null = the process-wide one, which is all the app ever uses.</summary>
    internal static readonly AsyncLocal<WorkItemsState?> Isolated = new();

    private static WorkItemsState S => Isolated.Value ?? Shared;

    /// <summary>Runs <c>openspec archive &lt;folder&gt; --yes</c> with the extra flags; CouldRun is false when the CLI never decided (see OpenSpecCli.Archive).</summary>
    internal static Func<string, IReadOnlyList<string>, (bool Success, string Message, bool CouldRun)> Archive { get => S.Archive; set => S.Archive = value; }

    /// <summary>The host's cached read of the board, and its look at the board without refreshing it (see WorkItemsApi.UseBoard); null = read afresh on every call. Not per test: the reads they point to find the test's own caches.</summary>
    internal static Func<PublicDTOs.BoardRead>? Board;
    internal static Func<PublicDTOs.BoardRead?>? LastSeen;

    /// <summary>Goes up when the contributor source is replaced, so every item takes its commit authors again (without parsing a folder).</summary>
    internal static int GitEpoch { get => S.GitEpoch; set => S.GitEpoch = value; }

    /// <summary>Git commit authors per change folder, with a token that changes on every commit; null outside git.</summary>
    internal static Func<(string Token, Func<string, IReadOnlyList<string>> AuthorsOf)?>? Git { get => S.Git; set => S.Git = value; }

    /// <summary>Who an event is recorded as (the host sets it); null = nobody configured.</summary>
    internal static Func<string?>? Actor { get => ScopedActor.Value ?? S.Actor; set => S.Actor = value; }

    /// <summary>The actor of one call (an agent's tool call), set for the code running from where it was set; null = the configured one, the app's.</summary>
    private static readonly AsyncLocal<Func<string?>?> ScopedActor = new();

    /// <summary>Signs what the code running from here writes as <paramref name="who"/> until the scope is disposed; the configured actor, and every other call's, stay as they are.</summary>
    internal static IDisposable ActingAs(string who)
    {
        Func<string?>? before = ScopedActor.Value;
        ScopedActor.Value = () => who;
        return new Undo(() => ScopedActor.Value = before);
    }

    private sealed class Undo(Action undo) : IDisposable
    {
        private Action? undo = undo;
        public void Dispose() => Interlocked.Exchange(ref undo, null)?.Invoke();
    }

    /// <summary>How many change folders were parsed (tests use it to prove an edit re-parses only its own folder).</summary>
    internal static int FolderParses { get => S.FolderParses; set => S.FolderParses = value; }

    /// <summary>How many times the duplicate-id scan ran (the app only runs it when the set of change folders changed).</summary>
    internal static int DuplicateScans { get => S.DuplicateScans; set => S.DuplicateScans = value; }
}
