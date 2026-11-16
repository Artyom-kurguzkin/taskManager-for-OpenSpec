using LinkedTestRuns.PublicDTOs;

namespace LinkedTestRuns;

/// <summary>The rules for a test link: is it in the latest results, under which class, and how did it end.</summary>
internal static class LinkRules
{
    /// <summary>Each link with its suite corrected when only one class in the latest results has that display name; a warning for the ones the results do not have.</summary>
    internal static List<CheckedTest> Check(IEnumerable<(string Suite, string Test)> tests)
    {
        TestRun run = Queries.Latest();
        List<CheckedTest> result = [];
        foreach ((string suite, string test) in tests)
        {
            string[] classes = [.. run.Results.Where(r => r.Test == test).Select(r => r.Suite).Distinct().Order(StringComparer.Ordinal)];
            if (classes.Length == 1 && classes[0] != suite)
                result.Add(new(classes[0], test, $"Test \"{test}\": suite corrected from {suite} to {classes[0]}."));
            else if (classes.Length > 1 && !classes.Contains(suite))
                result.Add(new(suite, test, $"Test {suite} / \"{test}\" is not in the latest results under that class; the display name is in {string.Join(", ", classes)}: use one of them as `suite`."));
            else
                result.Add(new(suite, test, Missing(run, suite, test)));
        }
        return result;
    }

    /// <summary>The warnings for links the latest results do not have (all of them when there is no results file).</summary>
    internal static IEnumerable<string> Missing(IEnumerable<(string Suite, string Test)> tests)
    {
        TestRun run = Queries.Latest();
        return [.. tests.Select(t => Missing(run, t.Suite, t.Test)).OfType<string>()];
    }

    private static string? Missing(TestRun run, string suite, string test) =>
        run.Results.Any(r => r.Suite == suite && r.Test == test) ? null
        : run.File is null
            ? $"Test {suite} / \"{test}\" cannot be checked: no results file was found (see x-rtm.testResults in openspec/config.yaml). Run the tests first."
            : $"Test {suite} / \"{test}\" is not in the latest results ({run.File}): use the class short name and the display name exactly as the file has them, and run the tests first.";

    internal static TestOutcome Outcome(string suite, string test)
    {
        string outcome = Queries.Latest().Results.FirstOrDefault(r => r.Suite == suite && r.Test == test)?.Outcome ?? "missing";
        return new(outcome, outcome switch { "passed" => "✓", "failed" => "✗", "skipped" => "-", _ => "?" });
    }
}
