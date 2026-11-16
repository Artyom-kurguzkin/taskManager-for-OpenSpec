using Features;
using Features.PublicDTOs;
using OpenSpecStore;

namespace TaskManager.AppState;

/// <summary>The features of each open repository, read again only when its content version or its session (a reopened repository) moves; an equal list keeps the old instance.</summary>
public static class FeaturesCache
{
    internal sealed class State
    {
        public readonly Dictionary<string, (long Generation, long Version, IReadOnlyList<Feature> Features)> Entries = new(StringComparer.OrdinalIgnoreCase);
        public int Loads;
    }

    /// <summary>How many times the features were read from disk (a test counts them).</summary>
    public static int Loads => Caches.S.Features.Loads;

    /// <summary>The features of the repository in scope.</summary>
    public static IReadOnlyList<Feature> Get()
    {
        State s = Caches.S.Features;
        long version = OpenSpecStoreApi.Version;
        string root = OpenSpecStoreApi.RepoRoot ?? "";
        long generation = OpenSpecStoreApi.RepoGeneration;   // a repository closed and opened again restarts its version at 0: only the session tells its cache apart
        lock (s.Entries) if (s.Entries.TryGetValue(root, out var c) && c.Generation == generation && c.Version == version) return c.Features;
        IReadOnlyList<Feature> read = FeaturesApi.Read();   // outside the lock: a read of the disk blocks nobody else's cache hit
        lock (s.Entries)
        {
            IReadOnlyList<Feature>? old = s.Entries.TryGetValue(root, out var before) && before.Generation == generation ? before.Features : null;
            read = Same.Keep(old, read);
            s.Entries[root] = (generation, version, read);
            s.Loads++;
            return read;
        }
    }

    /// <summary>Forgets what was read of a repository (all of them without a root): the next read goes to disk.</summary>
    public static void Invalidate(string? root = null)
    {
        State s = Caches.S.Features;
        lock (s.Entries) { if (root is null) s.Entries.Clear(); else s.Entries.Remove(root); }
    }
}
