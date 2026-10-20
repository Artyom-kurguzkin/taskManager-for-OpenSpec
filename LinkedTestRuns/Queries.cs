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
        if (LinkedTestRunsModule.Cache is { } c && c.Version == version && DateTime.UtcNow - c.Checked < TimeSpan.FromSeconds(2)) return c.Run;

        IReadOnlyList<string> globs = Globs();
        string? root = OpenSpecStoreApi.RepoRoot;
        FileInfo? newest = root is null ? null : globs.SelectMany(g => Glob.Find(root, g)).Distinct().Select(f => new FileInfo(f)).MaxBy(f => f.LastWriteTimeUtc);

        TestRun run = LinkedTestRunsModule.Cache is { } old && old.File == newest?.FullName && old.Written == newest?.LastWriteTimeUtc
            ? old.Run with { Globs = globs }
            : new TestRun(newest?.FullName, globs, newest is null ? [] : ResultFile.Parse(newest.FullName));
        LinkedTestRunsModule.Cache = (version, newest?.FullName, newest?.LastWriteTimeUtc ?? default, DateTime.UtcNow, run);
        return run;
    }

    private static IReadOnlyList<string> Globs() =>
        OpenSpecStoreApi.OpenSpecRoot is string os && OpenSpecStoreApi.ReadRtmSection(Path.Combine(os, "config.yaml"), "testResults") is string s && s.Trim().Length > 0
            ? Reader.Deserialize<List<string>>(s) ?? []
            : [];
}
