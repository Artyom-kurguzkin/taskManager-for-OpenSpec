using System.Globalization;
using System.Xml.Linq;
using LinkedTestRuns.PublicDTOs;

namespace LinkedTestRuns.Infrastructure;

/// <summary>Reads a TRX (dotnet test) or JUnit XML file into flat test results. An unreadable file gives no results.</summary>
internal static class ResultFile
{
    internal static IReadOnlyList<TestResult> Parse(string path)
    {
        try
        {
            XElement root = XDocument.Load(path).Root!;
            return root.Name.LocalName == "TestRun" ? Trx(root) : JUnit(root);
        }
        catch (Exception e) when (e is IOException or System.Xml.XmlException or UnauthorizedAccessException) { return []; }
    }

    // TRX: suite = test class (short name), test = the test's display name.
    private static List<TestResult> Trx(XElement run)
    {
        XNamespace ns = run.Name.Namespace;
        Dictionary<string, string> classes = run.Descendants(ns + "UnitTest").ToDictionary(
            u => (string)u.Attribute("id")!,
            u => ((string?)u.Element(ns + "TestMethod")?.Attribute("className") ?? "").Split('.')[^1]);
        return run.Descendants(ns + "UnitTestResult").Select(r => new TestResult(
            classes.GetValueOrDefault((string?)r.Attribute("testId") ?? "", ""),
            (string?)r.Attribute("testName") ?? "",
            ((string?)r.Attribute("outcome")) switch { "Passed" => "passed", "Failed" or "Error" or "Timeout" or "Aborted" => "failed", _ => "skipped" },
            Time((string?)r.Attribute("endTime")))).ToList();
    }

    // JUnit: suite = testsuite@name, test = testcase@name; <failure>/<error> = failed, <skipped> = skipped.
    private static List<TestResult> JUnit(XElement root) =>
        root.DescendantsAndSelf("testsuite").SelectMany(s => s.Elements("testcase").Select(t => new TestResult(
            (string?)s.Attribute("name") ?? "",
            (string?)t.Attribute("name") ?? "",
            t.Element("failure") is not null || t.Element("error") is not null ? "failed" : t.Element("skipped") is not null ? "skipped" : "passed",
            Time((string?)s.Attribute("timestamp"))))).ToList();

    private static DateTime? Time(string? s) =>
        DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTime t) ? t : null;
}
