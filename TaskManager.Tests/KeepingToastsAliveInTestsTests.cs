using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

namespace TaskManager.Tests;

/// <summary>
/// A toast leaves the screen after 3.5 seconds of the wall clock, and <c>UiService.Toasts</c> drops it with it. A test that asserts on a toast has to keep it, or a busy machine (the full suite
/// running in parallel) expires it between the sync raising it and the assertion: the list is then empty and the test fails once in a while, for no reason in the product.
/// </summary>
public sealed class KeepingToastsAliveInTestsTests
{
    [Fact(DisplayName = "Every test class that asserts on toasts keeps them: its UiService never expires a toast")]
    public void EveryTestClassThatAssertsOnToastsKeepsThem() => Check();

    private static void Check([CallerFilePath] string here = "")
    {
        Regex plain = new(@"new UiService\(\)|UiService\s+\w+\s*=\s*new\(\)|UiService\s+\w+\s*=\s*new\(\)\s*,\s*\w+\s*=\s*new\(\)");
        List<string> offenders = [];
        foreach (string file in Directory.GetFiles(Path.GetDirectoryName(here)!, "*.cs"))
        {
            string[] lines = File.ReadAllLines(file);
            if (!lines.Any(l => l.Contains(".Toasts") || l.Contains("Toasts =>"))) continue;   // does not look at toasts: the default lifetime is fine
            for (int i = 0; i < lines.Length; i++)
                if (plain.IsMatch(lines[i]) && !lines[i].Contains("ToastLifetime") && !lines[i].TrimStart().StartsWith("//"))
                    offenders.Add($"{Path.GetFileName(file)}:{i + 1}: {lines[i].Trim()}");
        }
        Assert.True(offenders.Count == 0, "A UiService in a test class that asserts on toasts must be created with { ToastLifetime = Timeout.InfiniteTimeSpan }:\n" + string.Join('\n', offenders));
    }
}
