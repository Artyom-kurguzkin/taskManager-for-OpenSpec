namespace LinkedTestRuns.PublicDTOs;

/// <summary>The newest results file matching <paramref name="Globs"/> (repo-relative), or File = null when none matched.</summary>
public sealed record TestRun(string? File, IReadOnlyList<string> Globs, IReadOnlyList<TestResult> Results);

/// <summary>One test's outcome: "passed", "failed" or "skipped".</summary>
public sealed record TestResult(string Suite, string Test, string Outcome, DateTime? Finished);
