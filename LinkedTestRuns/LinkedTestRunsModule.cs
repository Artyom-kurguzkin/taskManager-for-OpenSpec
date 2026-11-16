namespace LinkedTestRuns;

/// <summary>The module keeps no cache: only the host's cached read, handed in once (see LinkedTestRunsApi.UseReader).</summary>
internal static class LinkedTestRunsModule
{
    internal static Func<PublicDTOs.TestRun>? Reader;
}
