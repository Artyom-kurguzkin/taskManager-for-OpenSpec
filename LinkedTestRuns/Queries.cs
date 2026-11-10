using OpenSpecStore;
using LinkedTestRuns.Infrastructure;
using LinkedTestRuns.PublicDTOs;
using YamlDotNet.Serialization;

namespace LinkedTestRuns;

internal static class Queries
{
    private static readonly IDeserializer Reader = new DeserializerBuilder().Build();

    // ponytail: the board asks once per card, so the folder scan is reused for 2 s; a file watcher if that ever shows up.
    internal static TestRun Latest()
    {
        long version = OpenSpecStoreApi.Version;
        string? root = OpenSpecStoreApi.RepoRoot;
        bool cached = LinkedTestRunsModule.Cache.TryGetValue(root ?? "", out var c);
        if (cached && c.Version == version && DateTime.UtcNow - c.Checked < TimeSpan.FromSeconds(2)) return c.Run;

        IReadOnlyList<string> globs = Globs();
        FileInfo? newest = root is null ? null : globs.SelectMany(g => Glob.Find(root, g)).Distinct().Select(f => new FileInfo(f)).MaxBy(f => f.LastWriteTimeUtc);

        TestRun run = cached && c.File == newest?.FullName && c.Written == newest?.LastWriteTimeUtc
            ? c.Run with { Globs = globs }
            : new TestRun(newest?.FullName, globs, newest is null ? [] : ResultFile.Parse(newest.FullName));
        LinkedTestRunsModule.Cache[root ?? ""] = (version, newest?.FullName, newest?.LastWriteTimeUtc ?? default, DateTime.UtcNow, run);
        return run;
    }

    /// <summary>The globs of x-rtm.testResults; none when the list is not valid YAML (a hand edit must not take the app down).</summary>
    private static IReadOnlyList<string> Globs()
    {
        try
        {
            return OpenSpecStoreApi.OpenSpecRoot is string os && OpenSpecStoreApi.ReadRtmSection(Path.Combine(os, "config.yaml"), "testResults") is string s && s.Trim().Length > 0
                ? Reader.Deserialize<List<string>>(s) ?? []
                : [];
        }
        catch (Exception e) when (e is YamlDotNet.Core.YamlException or FormatException or InvalidCastException or InvalidOperationException) { return []; }
    }
}
