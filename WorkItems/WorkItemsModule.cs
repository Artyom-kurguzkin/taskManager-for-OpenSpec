namespace WorkItems;

/// <summary>Process-wide state. Only the archive step is swappable (tests replace the CLI call).</summary>
internal static class WorkItemsModule
{
    internal static Func<string, (bool Success, string Message)> Archive = Infrastructure.OpenSpecCli.Archive;

    /// <summary>Parsed work items, valid while OpenSpecStore's content version is unchanged.</summary>
    internal static (long Version, string Git, IReadOnlyList<PublicDTOs.WorkItem> Items)? Cache;

    /// <summary>Git commit authors per change folder, with a token that changes on every commit; null outside git.</summary>
    internal static Func<(string Token, Func<string, IReadOnlyList<string>> AuthorsOf)?>? Git;

    /// <summary>How many times the repo was parsed (tests use it to prove reads are cached).</summary>
    internal static int Loads;
}
