using OpenSpecStore;
using LinkedTestRuns.Infrastructure;
using LinkedTestRuns.PublicDTOs;

namespace LinkedTestRuns;

internal static class Queries
{
    /// <summary>The newest results: from the host's cache when it installed one (see LinkedTestRunsApi.UseReader), else read afresh.</summary>
    internal static TestRun Latest() => LinkedTestRunsModule.Reader?.Invoke() ?? Parse(Newest());

    internal static (string? File, DateTime Written, IReadOnlyList<string> Globs) Newest()
    {
        string? root = OpenSpecStoreApi.RepoRoot;
        IReadOnlyList<string> globs = Globs();
        FileInfo? newest = root is null ? null : globs.SelectMany(g => Glob.Find(root, g)).Distinct().Select(f => new FileInfo(f)).MaxBy(f => f.LastWriteTimeUtc);
        return (newest?.FullName, newest?.LastWriteTimeUtc ?? default, globs);
    }

    private static TestRun Parse((string? File, DateTime Written, IReadOnlyList<string> Globs) found) => Parse(found.File, found.Globs);

    internal static TestRun Parse(string? file, IReadOnlyList<string> globs) => new(file, globs, file is null ? [] : ResultFile.Parse(file));

    /// <summary>The globs of x-rtm.testResults; none when the list is not valid YAML (a hand edit must not take the app down).</summary>
    private static IReadOnlyList<string> Globs() => OpenSpecStoreApi.ReadConfigSection<List<string>>("testResults").Value ?? [];
}
