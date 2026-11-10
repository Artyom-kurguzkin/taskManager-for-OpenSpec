namespace WorkItems;

/// <summary>Process-wide state. Only the archive step is swappable (tests replace the CLI call).</summary>
internal static class WorkItemsModule
{
    /// <summary>Runs <c>openspec archive &lt;folder&gt; --yes</c> with the extra flags; CouldRun is false when the CLI never decided (see OpenSpecCli.Archive).</summary>
    internal static Func<string, IReadOnlyList<string>, (bool Success, string Message, bool CouldRun)> Archive = Infrastructure.OpenSpecCli.Archive;

    /// <summary>Parsed work items per repository: the assembled list is valid while that repository's content version is unchanged, and each change folder keeps its own parsed item while its files are unchanged.</summary>
    internal static readonly Dictionary<string, RepoCache> Cache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Guards <see cref="Cache"/>: the UI thread and the file watcher's timer both read the board.</summary>
    internal static readonly object Gate = new();

    /// <summary>Goes up when the contributor source is replaced, so every item takes its commit authors again (without parsing a folder).</summary>
    internal static int GitEpoch;

    /// <summary>Git commit authors per change folder, with a token that changes on every commit; null outside git.</summary>
    internal static Func<(string Token, Func<string, IReadOnlyList<string>> AuthorsOf)?>? Git;

    /// <summary>Who an event is recorded as (the host sets it); null = nobody configured.</summary>
    internal static Func<string?>? Actor;

    /// <summary>How many times the repo was parsed (tests use it to prove reads are cached).</summary>
    internal static int Loads;

    /// <summary>How many change folders were parsed (tests use it to prove an edit re-parses only its own folder).</summary>
    internal static int FolderParses;

    /// <summary>How many times the duplicate-id scan ran (the app only runs it when the set of change folders changed).</summary>
    internal static int DuplicateScans;
}
