using LinkedTestRuns.PublicDTOs;

namespace LinkedTestRuns;

/// <summary>
/// The only public type in this assembly. Reads the newest TRX (dotnet test) or JUnit XML (Playwright, Jest, pytest)
/// file found at the globs in openspec/config.yaml under <c>x-rtm.testResults</c>. Never runs tests.
/// </summary>
public static class LinkedTestRunsApi
{
    /// <summary>The newest results: from the host's cache when it installed one, else read afresh.</summary>
    public static TestRun Latest() => Queries.Latest();

    /// <summary>The newest results file under the globs of x-rtm.testResults (its path and write time), found now by walking the folders; File is null when none matched.</summary>
    public static (string? File, DateTime Written, IReadOnlyList<string> Globs) Newest() => Queries.Newest();

    /// <summary>A results file parsed now (an empty run when <paramref name="file"/> is null).</summary>
    public static TestRun Parse(string? file, IReadOnlyList<string> globs) => Queries.Parse(file, globs);

    /// <summary>Hands the module the host's cached read for its own code and for <see cref="Latest"/>; null = read afresh on every call.</summary>
    public static void UseReader(Func<TestRun>? reader) => LinkedTestRunsModule.Reader = reader;

    /// <summary>Each test link checked against the latest results, in the order given: the suite it is to be linked under (corrected when exactly one class has that display name) and a warning when something is off.</summary>
    public static IReadOnlyList<CheckedTest> CheckLinks(IEnumerable<(string Suite, string Test)> tests) => LinkRules.Check(tests);

    /// <summary>The warnings for the links the latest results do not have, or all of them when no results file was found.</summary>
    public static IReadOnlyList<string> MissingLinks(IEnumerable<(string Suite, string Test)> tests) => [.. LinkRules.Missing(tests)];

    /// <summary>How a linked test ended in the latest results, with its mark.</summary>
    public static TestOutcome OutcomeOf(string suite, string test) => LinkRules.Outcome(suite, test);
}
