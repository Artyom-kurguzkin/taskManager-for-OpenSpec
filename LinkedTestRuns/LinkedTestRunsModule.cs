namespace LinkedTestRuns;

/// <summary>
/// Process-wide state: the last parsed run, reused while the config is unchanged and the newest file is the same.
/// The app has one such cache; a test runs against its own (<see cref="Isolated"/>).
/// </summary>
internal static class LinkedTestRunsModule
{
    private static readonly Dictionary<string, (long Version, string? File, DateTime Written, DateTime Checked, PublicDTOs.TestRun Run)> Shared = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>A test seam: the cache of the code running from where it was set (one per test); null = the process-wide one, which is all the app ever uses.</summary>
    internal static readonly AsyncLocal<Dictionary<string, (long Version, string? File, DateTime Written, DateTime Checked, PublicDTOs.TestRun Run)>?> Isolated = new();

    internal static Dictionary<string, (long Version, string? File, DateTime Written, DateTime Checked, PublicDTOs.TestRun Run)> Cache => Isolated.Value ?? Shared;   // per repository
}
