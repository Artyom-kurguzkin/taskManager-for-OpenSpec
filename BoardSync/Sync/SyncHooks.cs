namespace BoardSync.Sync;

/// <summary>Test seams, reachable through InternalsVisibleTo only.</summary>
internal sealed class SyncHooks
{
    /// <summary>Stops after the manifest was replaced, before any local file is written or the state is saved: what a crash at that point leaves behind.</summary>
    internal bool SkipLocalApply { get; init; }

    /// <summary>Hears the size of every buffer the local scan allocates for file content (a whole small file, or the chunk a large one is streamed through).</summary>
    internal Action<long>? OnScanBuffer { get; init; }
}

/// <summary>A sync that cannot go on, with a message a person can act on. Never leaves this assembly: the engine turns it into <see cref="PublicDTOs.SyncOutcome.Failed"/>.</summary>
internal sealed class SyncFailure(string message) : Exception(message);
