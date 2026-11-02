namespace LinkedTestRuns;

/// <summary>Process-wide state: the last parsed run, reused while the config is unchanged and the newest file is the same.</summary>
internal static class LinkedTestRunsModule
{
    internal static readonly Dictionary<string, (long Version, string? File, DateTime Written, DateTime Checked, PublicDTOs.TestRun Run)> Cache = new(StringComparer.OrdinalIgnoreCase);   // per repository
}
