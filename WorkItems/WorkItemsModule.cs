namespace WorkItems;

/// <summary>Process-wide state. Only the archive step is swappable (tests replace the CLI call).</summary>
internal static class WorkItemsModule
{
    /// <summary>Runs <c>openspec archive &lt;folder&gt; --yes</c> with the extra flags; CouldRun is false when the CLI never decided (see OpenSpecCli.Archive).</summary>
    internal static Func<string, IReadOnlyList<string>, (bool Success, string Message, bool CouldRun)> Archive = Infrastructure.OpenSpecCli.Archive;

    /// <summary>Parsed work items per repository, each valid while that repository's content version is unchanged.</summary>
    internal static readonly Dictionary<string, (long Version, string Git, IReadOnlyList<PublicDTOs.WorkItem> Items)> Cache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Git commit authors per change folder, with a token that changes on every commit; null outside git.</summary>
    internal static Func<(string Token, Func<string, IReadOnlyList<string>> AuthorsOf)?>? Git;

    /// <summary>How many times the repo was parsed (tests use it to prove reads are cached).</summary>
    internal static int Loads;
}
