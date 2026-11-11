using System.Diagnostics;

namespace TaskManager.Tests;

/// <summary>A fact that is skipped, with the reason, when Node.js is not on the PATH.</summary>
public sealed class NodeFactAttribute : FactAttribute
{
    public NodeFactAttribute()
    {
        try
        {
            ProcessStartInfo psi = new("node", "--version") { RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true };
            using Process? p = Process.Start(psi);
            p?.WaitForExit(10_000);
        }
        catch (System.ComponentModel.Win32Exception) { Skip = "Node.js is not installed (node is not on the PATH), so the node tests were not run"; }
    }
}

/// <summary>The markdown editors' table helper keeps its table operations in pure functions (wwwroot/js/md-table.js), tested with node's own runner.</summary>
public sealed class MarkdownTableHelperTests
{
    private static string? FindTestFile()
    {
        for (string? dir = AppContext.BaseDirectory; dir is not null; dir = Path.GetDirectoryName(dir))
        {
            string file = Path.Combine(dir, "tests", "md-table.test.mjs");
            if (File.Exists(file)) return file;
        }
        return null;
    }

    [NodeFact(DisplayName = "The markdown table helper's table operations pass their node tests")]
    public void TableOperationsPassTheirNodeTests()
    {
        string? file = FindTestFile();
        Assert.True(file is not null, "tests/md-table.test.mjs was not found above the test binaries");

        ProcessStartInfo psi = new("node") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
        psi.ArgumentList.Add("--test");
        psi.ArgumentList.Add(file!);
        using Process p = Process.Start(psi)!;
        Task<string> stdout = p.StandardOutput.ReadToEndAsync(), stderr = p.StandardError.ReadToEndAsync();
        Assert.True(p.WaitForExit(60_000), "node --test did not finish in a minute");
        Assert.True(p.ExitCode == 0, stdout.Result + stderr.Result);
    }
}
