using System.Diagnostics;

namespace TaskManager.Tests;

/// <summary>
/// tests/startup-time.ps1 measures the start of a build on the hidden desktop; its -WhatIf prints what it would do and starts nothing,
/// so the plan (the exe, the number of runs, the repository, warm or cold-ish) can be checked without opening a window.
/// </summary>
public sealed class StartupScriptTests
{
    private static string ScriptPath()
    {
        for (DirectoryInfo? d = new(AppContext.BaseDirectory); d is not null; d = d.Parent)
            if (File.Exists(Path.Combine(d.FullName, "tests", "startup-time.ps1"))) return Path.Combine(d.FullName, "tests", "startup-time.ps1");
        throw new FileNotFoundException("tests/startup-time.ps1 is not above " + AppContext.BaseDirectory);
    }

    private static (int Exit, string Output) RunScript(string arguments)
    {
        ProcessStartInfo psi = new("pwsh", $"-NoProfile -ExecutionPolicy Bypass -File \"{ScriptPath()}\" {arguments}")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        using Process p = Process.Start(psi)!;
        Task<string> error = p.StandardError.ReadToEndAsync();
        string output = p.StandardOutput.ReadToEnd();
        p.WaitForExit();
        return (p.ExitCode, output + error.Result);
    }

    [Fact(DisplayName = "startup-time.ps1 -WhatIf prints the exe, the runs and the repository it would measure and starts nothing")]
    public void WhatIfPrintsThePlan()
    {
        string exe = Path.Combine(Path.GetTempPath(), "rtm-not-a-build", "TaskManager.exe");
        string repo = Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar);

        (int exit, string output) = RunScript($"-WhatIf -Exe \"{exe}\" -Runs 3 -Repo \"{repo}\" -Tag whatif -Cold");

        Assert.Equal(0, exit);
        Assert.Contains(exe, output);   // a path that does not exist: nothing was started, nothing was checked
        Assert.Contains("runs:   3", output);
        Assert.Contains("cold-ish", output);
        Assert.Contains(repo, output);
        Assert.Contains("startup-whatif.txt", output);
    }
}
