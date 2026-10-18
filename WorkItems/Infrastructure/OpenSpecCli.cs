using System.Diagnostics;
using OpenSpecStore;

namespace WorkItems.Infrastructure;

/// <summary>Archives a change exactly as the CLI does it, so delta specs merge into openspec/specs the official way.</summary>
internal static class OpenSpecCli
{
    // ponytail: pinned CLI version via npx; prefer a global `openspec` once teams install it.
    private const string Npx = "npx -y @fission-ai/openspec@1.14.3";

    internal static (bool Success, string Message) Archive(string folder)
    {
        if (OpenSpecStoreApi.RepoRoot is not string root) return (false, "No repository is open");
        string command = $"{Npx} archive {folder} --yes";
        ProcessStartInfo psi = OperatingSystem.IsWindows()
            ? new("cmd.exe", $"/c {command}")
            : new("/bin/sh", $"-c \"{command}\"");
        psi.WorkingDirectory = root;
        psi.RedirectStandardOutput = psi.RedirectStandardError = true;
        psi.UseShellExecute = false;
        psi.CreateNoWindow = true;
        try
        {
            using Process p = Process.Start(psi)!;
            string output = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
            if (!p.WaitForExit(TimeSpan.FromMinutes(2))) { p.Kill(true); return (false, "openspec archive timed out"); }
            return p.ExitCode == 0 ? (true, "") : (false, output.Trim());
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return (false, "Couldn't run the OpenSpec CLI (needs Node.js/npx on PATH): " + e.Message);
        }
    }
}
