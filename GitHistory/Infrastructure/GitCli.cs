using System.ComponentModel;
using System.Diagnostics;

namespace GitHistory.Infrastructure;

internal static class GitCli
{
    /// <summary>Runs git in the configured repo. Exit code -1 means git could not be started.</summary>
    internal static (int Exit, string Output, string Error) Run(params string[] args)
    {
        if (GitHistoryModule.RepoRoot is not string root || !Directory.Exists(root)) return (-1, "", "No repository is open");
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
            return (p.ExitCode, output, err.Result);
        }
        catch (Win32Exception e) { return (-1, "", "git is not installed or not on PATH: " + e.Message); }
    }
}
