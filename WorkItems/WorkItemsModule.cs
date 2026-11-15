namespace WorkItems;

/// <summary>The module's state: what is cached per repository, the swappable steps, and the counters tests read.</summary>
internal sealed class WorkItemsState
{
    public Func<string, IReadOnlyList<string>, (bool Success, string Message, bool CouldRun)> Archive = Infrastructure.OpenSpecCli.Archive;
    public readonly Dictionary<string, RepoCache> Cache = new(StringComparer.OrdinalIgnoreCase);
    public readonly object Gate = new();
    public int GitEpoch;
    public Func<(string Token, Func<string, IReadOnlyList<string>> AuthorsOf)?>? Git;
    public Func<string?>? Actor;
    public int Loads, FolderParses, DuplicateScans;
}

/// <summary>
/// Process-wide state. Only the archive step is swappable (tests replace the CLI call).
/// The app has one such state; a test runs against its own (<see cref="Isolated"/>), so tests that run at the same time never share a cache, a step or a counter.
/// </summary>
internal static class WorkItemsModule
{
    private static readonly WorkItemsState Shared = new();

    /// <summary>A test seam: the state of the code running from where it was set (one per test); null = the process-wide one, which is all the app ever uses.</summary>
    internal static readonly AsyncLocal<WorkItemsState?> Isolated = new();

    private static WorkItemsState S => Isolated.Value ?? Shared;

    /// <summary>Runs <c>openspec archive &lt;folder&gt; --yes</c> with the extra flags; CouldRun is false when the CLI never decided (see OpenSpecCli.Archive).</summary>
    internal static Func<string, IReadOnlyList<string>, (bool Success, string Message, bool CouldRun)> Archive { get => S.Archive; set => S.Archive = value; }

    /// <summary>Parsed work items per repository: the assembled list is valid while that repository's content version is unchanged, and each change folder keeps its own parsed item while its files are unchanged.</summary>
    internal static Dictionary<string, RepoCache> Cache => S.Cache;

    /// <summary>Guards <see cref="Cache"/>: the UI thread and the file watcher's timer both read the board.</summary>
    internal static object Gate => S.Gate;

    /// <summary>Goes up when the contributor source is replaced, so every item takes its commit authors again (without parsing a folder).</summary>
    internal static int GitEpoch { get => S.GitEpoch; set => S.GitEpoch = value; }

    /// <summary>Git commit authors per change folder, with a token that changes on every commit; null outside git.</summary>
    internal static Func<(string Token, Func<string, IReadOnlyList<string>> AuthorsOf)?>? Git { get => S.Git; set => S.Git = value; }

    /// <summary>Who an event is recorded as (the host sets it); null = nobody configured.</summary>
    internal static Func<string?>? Actor { get => S.Actor; set => S.Actor = value; }

    /// <summary>How many times the repo was parsed (tests use it to prove reads are cached).</summary>
    internal static int Loads { get => S.Loads; set => S.Loads = value; }

    /// <summary>How many change folders were parsed (tests use it to prove an edit re-parses only its own folder).</summary>
    internal static int FolderParses { get => S.FolderParses; set => S.FolderParses = value; }

    /// <summary>How many times the duplicate-id scan ran (the app only runs it when the set of change folders changed).</summary>
    internal static int DuplicateScans { get => S.DuplicateScans; set => S.DuplicateScans = value; }
}
