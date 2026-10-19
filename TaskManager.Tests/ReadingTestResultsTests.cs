using OpenSpecStore;
using TestResults;
using TestResults.PublicDTOs;

namespace TaskManager.Tests;

[Collection(nameof(ModuleStateCollection))]
public sealed class ReadingTestResultsTests
{
    internal const string Trx = """
        <?xml version="1.0" encoding="utf-8"?>
        <TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010">
          <Results>
            <UnitTestResult testId="1" testName="Ping test" outcome="Passed" endTime="2026-10-17T14:02:00.0000000+00:00" />
            <UnitTestResult testId="2" testName="Page load test" outcome="Failed" endTime="2026-10-17T14:02:05.0000000+00:00" />
          </Results>
          <TestDefinitions>
            <UnitTest id="1" name="Ping test"><TestMethod className="Suite543.UptimeTests" name="Ping" /></UnitTest>
            <UnitTest id="2" name="Page load test"><TestMethod className="Suite543.UptimeTests" name="PageLoad" /></UnitTest>
          </TestDefinitions>
        </TestRun>
        """;

    private const string JUnit = """
        <testsuites>
          <testsuite name="suite 543" timestamp="2026-10-17T14:02:00">
            <testcase name="ping test" classname="uptime" />
            <testcase name="page load test" classname="uptime"><failure message="too slow" /></testcase>
            <testcase name="visual diff" classname="uptime"><skipped /></testcase>
          </testsuite>
        </testsuites>
        """;

    /// <summary>Lists result globs in config.yaml (x-rtm.testResults), the way a user would.</summary>
    internal static void UseResults(TestRepo repo, params string[] globs) =>
        OpenSpecStoreApi.WriteRtmSection(repo.PathOf("openspec/config.yaml"), "testResults", string.Concat(globs.Select(g => $"- {g}\n")));

    [Fact(DisplayName = "A dotnet test TRX file is read as suite (test class), test, outcome and time")]
    public void ReadsTrx()
    {
        using TestRepo repo = new();
        OpenSpecStoreApi.Open(repo.Root);
        repo.Write("results/run.trx", Trx);
        UseResults(repo, "results/*.trx");

        TestRun run = TestResultsApi.Latest();

        Assert.EndsWith("run.trx", run.File);
        Assert.Contains(new TestResult("UptimeTests", "Ping test", "passed", new DateTime(2026, 10, 17, 14, 2, 0, DateTimeKind.Utc)), run.Results.Select(r => r with { Finished = r.Finished?.ToUniversalTime() }));
        Assert.Equal("failed", run.Results.Single(r => r.Test == "Page load test").Outcome);
    }

    [Fact(DisplayName = "A JUnit XML file (Playwright, Jest, pytest) is read, failures and skips included")]
    public void ReadsJUnit()
    {
        using TestRepo repo = new();
        OpenSpecStoreApi.Open(repo.Root);
        repo.Write("test-results/junit.xml", JUnit);
        UseResults(repo, "test-results/**/*.xml");

        TestRun run = TestResultsApi.Latest();

        Assert.Equal(["passed", "failed", "skipped"], run.Results.Select(r => r.Outcome));
        Assert.All(run.Results, r => Assert.Equal("suite 543", r.Suite));
    }

    [Fact(DisplayName = "The newest file matching the configured globs is the one shown")]
    public void NewestFileWins()
    {
        using TestRepo repo = new();
        OpenSpecStoreApi.Open(repo.Root);
        repo.Write("results/old.trx", Trx.Replace("Ping test", "Old ping"));
        File.SetLastWriteTime(repo.PathOf("results/old.trx"), DateTime.Now.AddDays(-1));
        repo.Write("results/new.trx", Trx);
        UseResults(repo, "results/*.trx");

        Assert.EndsWith("new.trx", TestResultsApi.Latest().File);
    }

    [Fact(DisplayName = "Without a results file the run is empty and says where it looked")]
    public void NoResults()
    {
        using TestRepo repo = new();
        OpenSpecStoreApi.Open(repo.Root);
        UseResults(repo, "results/*.trx");

        TestRun run = TestResultsApi.Latest();

        Assert.Null(run.File);
        Assert.Empty(run.Results);
        Assert.Equal(["results/*.trx"], run.Globs);
    }
}
