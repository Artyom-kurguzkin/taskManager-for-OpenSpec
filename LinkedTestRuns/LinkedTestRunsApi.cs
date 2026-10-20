using LinkedTestRuns.PublicDTOs;

namespace LinkedTestRuns;

/// <summary>
/// The only public type in this assembly. Reads the newest TRX (dotnet test) or JUnit XML (Playwright, Jest, pytest)
/// file found at the globs in openspec/config.yaml under <c>x-rtm.testResults</c>. Never runs tests.
/// </summary>
public static class LinkedTestRunsApi
{
    public static TestRun Latest() => Queries.Latest();
}
