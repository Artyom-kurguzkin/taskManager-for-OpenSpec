namespace LinkedTestRuns.PublicDTOs;

/// <summary>A test link checked against the latest results: the suite to link under (corrected when only one class has that display name) and what is wrong with it, if anything.</summary>
public sealed record CheckedTest(string Suite, string Test, string? Warning);

/// <summary>A linked test's outcome in the latest results: passed, failed, skipped or missing, with its mark (a check, a cross, a dash or a question mark).</summary>
public sealed record TestOutcome(string Outcome, string Mark);
