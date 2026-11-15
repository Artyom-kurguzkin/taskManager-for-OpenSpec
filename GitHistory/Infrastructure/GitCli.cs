using System.ComponentModel;
using System.Diagnostics;

namespace GitHistory.Infrastructure;

internal static class GitCli
{
    /// <summary>
    /// A test seam (the test assembly turns it on, the product never does): in a folder with no .git in it or above it, git's answer is known without asking
    /// (repository commands fail, user.name is the global one, read once), so no process is started: each start costs about 80 ms on Windows.
    /// The call is still counted in <see cref="GitHistoryModule.Processes"/>.
    /// </summary>
    internal static bool AnswerWithoutSpawnWhenNoRepository;
    private static string? globalUserName;   // "" = git has none

    private static bool HasDotGit(string root)
    {
        for (DirectoryInfo? d = new(root); d is not null; d = d.Parent)
            if (Directory.Exists(Path.Combine(d.FullName, ".git")) || File.Exists(Path.Combine(d.FullName, ".git"))) return true;
        return false;
    }

    /// <summary>Runs git in the configured repo. Exit code -1 means git could not be started.</summary>
    internal static (int Exit, string Output, string Error) Run(params string[] args)
    {
        if (GitHistoryModule.RepoRoot is not string root || !Directory.Exists(root)) return (-1, "", "No repository is open");
        Interlocked.Increment(ref GitHistoryModule.S.Processes);
        bool noRepo = AnswerWithoutSpawnWhenNoRepository && !HasDotGit(root);
        bool globalName = noRepo && args is ["config", "user.name"];
        if (noRepo && args is ["rev-parse" or "check-ignore" or "log" or "show" or "add" or "commit", ..])
            return (128, "", "fatal: not a git repository (or any of the parent directories): .git");
        if (globalName && globalUserName is not null)
            return globalUserName.Length > 0 ? (0, globalUserName + "\n", "") : (1, "", "");
        ProcessStartInfo psi = new("git")
        {
            WorkingDirectory = root, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true,
        };
        foreach (string a in args) psi.ArgumentList.Add(a);
        try
        {
            using Process p = Process.Start(psi)!;
            Task<string> err = p.StandardError.ReadToEndAsync();
            string output = p.StandardOutput.ReadToEnd();
            if (!p.WaitForExit(TimeSpan.FromSeconds(20))) { p.Kill(true); return (-1, "", "git timed out"); }
            if (globalName) globalUserName = p.ExitCode == 0 ? output.Trim() : "";
            return (p.ExitCode, output, err.Result);
        }
        catch (Win32Exception e) { return (-1, "", "git is not installed or not on PATH: " + e.Message); }
    }
}
