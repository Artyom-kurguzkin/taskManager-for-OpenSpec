using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace TaskManager.Helpers;

/// <summary>What the helper swaps: the staged release, the install folder, the folder that keeps the old copy, the process to wait for, and the exe to start afterwards (null = only swap).</summary>
public sealed record UpdateApplication(string Staged, string Install, string Backup, int ProcessId, string? RestartExe);

/// <summary>
/// The small PowerShell script that replaces the app's files: an exe cannot replace itself while it runs, so the app starts the script and the script waits for the app's process to end, keeps the
/// old files in the backup folder (one rollback: the folder is replaced by the next update), copies the staged release over the install folder (files the release does not hold stay), puts
/// the old files back when the copy fails, and starts the app again when asked. Everything it does is written to its log.
/// </summary>
public static class UpdateHelper
{
    private const string Script = """
        param([int]$ProcessId, [string]$Staged, [string]$Install, [string]$Backup, [string]$Log, [string]$Restart = '', [int]$Retries = 30)
        $ErrorActionPreference = 'Stop'
        function Say($text) { Add-Content -LiteralPath $Log -Value ('{0:s} {1}' -f (Get-Date), $text) }
        function Copy-Tree($from, $to, $same = @()) { & robocopy.exe $from $to /E @same /R:$Retries /W:1 /NFL /NDL /NJH /NJS /NP | Out-Null; return $LASTEXITCODE }   # 0-7 are success codes, 8 and up are failures
        try {
            Say "Waiting for process $ProcessId to exit"
            Wait-Process -Id $ProcessId -Timeout 120 -ErrorAction SilentlyContinue
            if (Get-Process -Id $ProcessId -ErrorAction SilentlyContinue) { Say "The app is still running after 120 seconds; nothing was changed"; exit 2 }
            if (Test-Path -LiteralPath $Backup) { Remove-Item -LiteralPath $Backup -Recurse -Force }
            Say "Keeping the current files in $Backup"
            if ((Copy-Tree $Install $Backup) -ge 8) { Say "The backup could not be made; nothing was changed"; exit 3 }
            Say "Copying $Staged to $Install"
            if ((Copy-Tree $Staged $Install @('/IS')) -ge 8) {   # /IS: a file that looks the same (size and time) is copied too
                Say "The copy failed; the old files are restored from $Backup"
                $code = Copy-Tree $Backup $Install @('/IS')   # every file again: robocopy takes times within 2 seconds as equal
                Say "Restored the old files from $Backup (robocopy code $code; a file that was never replaced may still be locked)"
                exit 4
            }
            Say "Updated"
            if ($Restart) { Say "Starting $Restart"; Start-Process -FilePath $Restart -WorkingDirectory $Install }
        } catch { Say "The update failed: $($_.Exception.Message)"; exit 5 }
        """;

    /// <summary>Writes the script beside <paramref name="log"/> and starts it hidden and detached from this process; null when it could not be started. <paramref name="retries"/> is how often a locked file is tried again (once a second).</summary>
    public static Process? Launch(UpdateApplication apply, string log, int retries = 30)
    {
        ILogger logger = AppLog.Logger("Update");
        try
        {
            string folder = Path.GetDirectoryName(Path.GetFullPath(log))!;
            Directory.CreateDirectory(folder);
            string script = Path.Combine(folder, "apply-update.ps1");
            File.WriteAllText(script, Script, new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: true));   // the BOM: Windows PowerShell reads a file without one as ANSI
            ProcessStartInfo start = new("powershell.exe")
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                ArgumentList = { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-WindowStyle", "Hidden", "-File", script,
                    "-ProcessId", apply.ProcessId.ToString(), "-Staged", apply.Staged, "-Install", apply.Install, "-Backup", apply.Backup, "-Log", log, "-Retries", retries.ToString() },
            };
            if (apply.RestartExe is not null) { start.ArgumentList.Add("-Restart"); start.ArgumentList.Add(apply.RestartExe); }
            Process? process = Process.Start(start);
            logger.LogInformation("Started the update helper (pid {Pid}) to swap {Staged} into {Install} after process {App} exits; log {Log}", process?.Id, apply.Staged, apply.Install, apply.ProcessId, log);
            return process;
        }
        catch (Exception e)
        {
            logger.LogError("Could not start the update helper: {Message}", e.Message);
            return null;
        }
    }
}
