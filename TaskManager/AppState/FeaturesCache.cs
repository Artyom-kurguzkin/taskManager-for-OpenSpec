using Features;
using Features.PublicDTOs;
using OpenSpecStore;

namespace TaskManager.AppState;

/// <summary>
/// The features of each open repository, read again only when something outside the app's own writes may have changed them, the app wrote under specs/, its config.yaml changed (the declared features)
/// or its session (a reopened repository) moved; a write to a work item moves the content version but none of these, so it leaves the features alone. An equal list keeps the old instance.
/// </summary>
public static class FeaturesCache
{
    internal sealed class State
    {
        public readonly Dictionary<string, (long Generation, long Version, long Outside, long Cursor, long Config, IReadOnlyList<Feature> Features)> Entries = new(StringComparer.OrdinalIgnoreCase);
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
        (long Generation, long Version, long Outside, long Cursor, long Config, IReadOnlyList<Feature> Features) known;
        bool have;
        lock (s.Entries) have = s.Entries.TryGetValue(root, out known) && known.Generation == generation;
        if (have && known.Version == version) return known.Features;
        // the version moved: only an outside change, a change of config.yaml (declared features) or an own write under specs/ makes the features worth reading again; a write to a work item is none of them
        long outside = OpenSpecStoreApi.OutsideVersion, config = OpenSpecStoreApi.ConfigVersion;
        (IReadOnlyList<string> touched, long cursor) = OpenSpecStoreApi.ChangesSince(have ? known.Cursor : 0);   // asked before reading: a write that lands meanwhile shows next time
        string? specs = OpenSpecStoreApi.OpenSpecRoot is string openspec ? Path.Combine(openspec, "specs") : null;
        if (have && known.Outside == outside && known.Config == config && !touched.Any(p => specs is not null && Under(specs, p)))
        {
            lock (s.Entries) s.Entries[root] = known with { Version = version, Cursor = cursor };
            return known.Features;
        }
        IReadOnlyList<Feature> read = FeaturesApi.Read();   // outside the lock: a read of the disk blocks nobody else's cache hit
        lock (s.Entries)
        {
            IReadOnlyList<Feature>? old = s.Entries.TryGetValue(root, out var before) && before.Generation == generation ? before.Features : null;
            read = Same.Keep(old, read);
            s.Entries[root] = (generation, version, outside, cursor, config, read);
            s.Loads++;
            return read;
        }
    }

    /// <summary>True when <paramref name="path"/> is <paramref name="folder"/> or inside it.</summary>
    private static bool Under(string folder, string path) =>
        path.StartsWith(folder, StringComparison.OrdinalIgnoreCase) && (path.Length == folder.Length || path[folder.Length] is '\\' or '/');

    /// <summary>Forgets what was read of a repository (all of them without a root): the next read goes to disk.</summary>
    public static void Invalidate(string? root = null)
    {
        State s = Caches.S.Features;
        lock (s.Entries) { if (root is null) s.Entries.Clear(); else s.Entries.Remove(root); }
    }
}
