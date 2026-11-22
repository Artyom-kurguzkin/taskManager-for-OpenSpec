namespace TaskManager.AppState;

/// <summary>
/// Everything the caches hold, so a test can have its own: the app has one state; a test runs against its own (<see cref="Caches.Isolated"/>),
/// as the modules do, and caches of tests that run at the same time never meet.
/// </summary>
internal sealed class CacheState
{
    public readonly ItemsCache.State Items = new();
    public readonly NotesCache.State Notes = new();
    public readonly FeaturesCache.State Features = new();
    public readonly GitCache.State Git = new();
    public readonly RenderedCache.State Rendered = new();
    public readonly TestResultsCache.State TestResults = new();
    /// <summary>What the pages read of items on demand (descriptions, events, notes), for the last 64 items used.</summary>
    public readonly BodyCache Bodies = new();
}

/// <summary>All caching of the app lives in this folder: one static class per kind of data, each remembering the module versions it was read at.</summary>
public static class Caches
{
    /// <summary>A test seam: the caches of the code running from where it was set (one set per test); null = the process-wide ones, which is all the app ever uses.</summary>
    internal static readonly AsyncLocal<CacheState?> Isolated = new();

    private static readonly CacheState Shared = new();

    internal static CacheState S => Isolated.Value ?? Shared;

    private static int installed;

    /// <summary>
    /// Hands the modules their cached reads (once; the app calls it at start, a test at its first test). A module answers its own Api calls from the cache of its kind,
    /// so the code inside it and the tool calls see what the screens see; with none installed a module reads afresh on every call.
    /// </summary>
    public static void Install()
    {
        if (Interlocked.Exchange(ref installed, 1) == 1) return;
        WorkItems.WorkItemsApi.UseBoard(ItemsCache.Board, ItemsCache.LastSeen);
        Notes.NotesApi.UseNotes(NotesCache.Get, NotesCache.TakeRenumbered, NotesCache.ForWorkItem);
        WorkItems.WorkItemsApi.UseDescriptions(ItemsCache.Description);
        WorkItems.WorkItemsApi.UseEvents(ItemsCache.Events);
        Features.FeaturesApi.UseReader(FeaturesCache.Get);
        GitHistory.GitHistoryApi.UseReads(new(GitCache.Changes, GitCache.Log, GitCache.Single, GitCache.People, GitCache.Uncommitted));
        LinkedTestRuns.LinkedTestRunsApi.UseReader(TestResultsCache.Get);
    }

    /// <summary>Forgets what every cache read of a repository (of all of them without a root): the next reads go to the modules.</summary>
    public static void Invalidate(string? root = null)
    {
        ItemsCache.Invalidate(root);
        NotesCache.Invalidate(root);
        FeaturesCache.Invalidate(root);
        TestResultsCache.Invalidate(root);
        GitCache.Invalidate(root);
        RenderedCache.Invalidate();
    }
}
