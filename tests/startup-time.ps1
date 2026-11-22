# Startup timing of a build, on the hidden desktop (tests/hidden-app.ps1): never on the user's desktop, never against the real openspec/.
#
#   .\tests\startup-time.ps1 -Tag debug                                    # the Debug exe, 5 runs, warm, against a "used" demo copy of this repo's openspec/
#   .\tests\startup-time.ps1 -Exe tests\out\pub-small\TaskManager.exe -Tag r2r-small -Cold
#   .\tests\startup-time.ps1 -WhatIf                                       # prints the plan, starts nothing
#
# -KeepHome keeps one RTM_HOME for the whole series, so the saved index (RTM_HOME\index) of the first run is what the next runs paint from ("index-painted" mark).
# Each run starts the exe on a hidden desktop with a fresh RTM_HOME (its own run log, server.json and token), waits until the run log shows the window
# is up (the "window-created" mark or "The window is up") and then, when the marks exist, until "scan-done" (60 s at most per run), stops the app and
# reads that run's log: the time from the process's creation (Get-Process StartTime) to each line's timestamp. Marks (`mark <name> +<ms>`, written by
# AppLog.Mark) and the older log lines both count: main = mark main or "Program: Starting", window-created = mark or "The window is up",
# first-render, headers-read, repo-loaded, git-ready, scan-done (marks of the first project), "serving MCP". A mark a build does not write shows "-".
# page-up is not from the log: the script opens WebView2's debug port (-CdpPort, 9633) and notes when it first lists a page with a real address; it is the one signal every build has,
# also a build from before the run log (which has only agents-<date>.log with "serving MCP").
# Prints a table (median and max over the runs, n = runs that had the mark) and writes it to tests\out\startup-<tag>.txt.
#
# Warm and cold-ish: the default runs follow each other on the same repository folder, so everything is in the OS file cache after the first. -Cold copies
# the demo repository to a new folder before each run, so its files were just written by this process and are not read from the cache the way a repeat
# read is. That is only cold-ish: a true cold cache (the exe, the runtime, WebView2 and the repository read from disk) needs a reboot. No admin, no
# cache-dropping tools. The WebView2 profile (tests\out\webview) is shared by all runs, so it is warm after the first.
#
# The first run of a series pays the cache and the WebView2 profile; the median is over all runs on purpose, use -Runs 7 or more when it matters.
[CmdletBinding()]
param(
    [string]$Exe,                 # default: the Debug exe of this checkout (what hidden-app.ps1 uses)
    [int]$Runs = 5,
    [string]$Tag = "run",         # names the output file tests\out\startup-<tag>.txt
    [string]$Repo,                # a repository folder holding openspec/; default: a "used" demo copy made fresh under tests\out
    [switch]$KeepHome,            # one RTM_HOME for every run of the series (emptied before the first), so the index the first run saves is there for the next: the start with a saved index
    [switch]$Cold,                # copy the repository to a new folder before each run (cold-ish, see above)
    [int]$CdpPort = 9633,         # WebView2 debug port, polled for the "page-up" row (parallel agents each take their own)
    [switch]$WhatIf               # print the plan and start nothing
)
$ErrorActionPreference = 'Stop'
$wantExe = $Exe; $wantRepo = $Repo; $debugPort = $CdpPort   # hidden-app.ps1 is dot-sourced into this scope and sets $Exe and $Repo (its own script variables, as is $CdpPort): take ours first
. (Join-Path $PSScriptRoot "hidden-app.ps1")   # the hidden-desktop helpers: HiddenDesk, New-DemoRepo, Stop-HiddenApp, $script:Exe, $script:Out
$debugExe = $script:Exe   # what hidden-app.ps1 just set: this checkout's Debug exe

$Exe = if ($wantExe) { $wantExe } else { $debugExe }   # hidden-app.ps1's own default: this checkout's Debug exe
$Exe = if (Test-Path $Exe) { (Resolve-Path $Exe).Path } else { $Exe }
$outFile = Join-Path $script:Out "startup-$Tag.txt"
$repoLabel = if ($wantRepo) { $wantRepo } else { "(a New-DemoRepo -Used copy, made at the start: $(Join-Path $script:Out 'startup-demo'))" }

if ($WhatIf) {
    "startup-time plan"
    "  exe:    $Exe"
    "  runs:   $Runs ($(if ($Cold) { 'cold-ish: a fresh copy of the repository before each run' } else { 'warm: the same repository folder every run' }))"
    "  repo:   $repoLabel"
    "  tag:    $Tag"
    "  output: $outFile"
    "  home:   $(if ($KeepHome) { 'one RTM_HOME for the whole series: the saved index is there from the second run' } else { 'a fresh RTM_HOME per run: no saved index' })"
    "  each run: hidden desktop, $(if ($KeepHome) { 'the series' } else { 'fresh' }) RTM_HOME, wait for the window-up line (60 s at most), stop the app, read its run log"
    return
}

if (-not (Test-Path $Exe)) { throw "no exe at $Exe" }
if ($Runs -lt 1) { throw "-Runs must be at least 1" }
if ($wantRepo) { $Repo = (Resolve-Path $wantRepo).Path } else { $Repo = New-DemoRepo "startup-demo" -Used }
New-Item -ItemType Directory -Force $script:ShotDir | Out-Null

# settings for one run: the demo's preset when it has one (a used board), else a fresh file that opens the repository
function New-RunSettings([string]$Path, [string]$RepoDir, [string]$PresetDir) {
    $preset = Join-Path $PresetDir "demo-settings.json"
    if (Test-Path $preset) {
        $s = Get-Content $preset -Raw | ConvertFrom-Json
        $s.LastRepo = $RepoDir; $s.OpenRepos = @($RepoDir)
        if ($s.Boards) {   # the boards are keyed by the lower-cased folder (SettingsStore.Key)
            $boards = [ordered]@{}
            foreach ($p in $s.Boards.PSObject.Properties) { $boards[$RepoDir.TrimEnd('\').ToLowerInvariant()] = $p.Value }
            $s.Boards = $boards
        }
        $s | ConvertTo-Json -Depth 8 | Set-Content $Path
    }
    else { @{ LastRepo = $RepoDir; OpenRepos = @($RepoDir) } | ConvertTo-Json | Set-Content $Path }
}

# The first log line that matches, as milliseconds since the process started; $null when there is none. The lines carry the time of day only.
function Get-LineMs([string[]]$Lines, [string]$Pattern, [datetime]$Start) {
    foreach ($l in $Lines) {
        if ($l -notmatch $Pattern) { continue }
        $m = [regex]::Match($l, '^(\d\d):(\d\d):(\d\d)\.(\d\d\d) ')
        if (-not $m.Success) { continue }
        $t = $Start.Date.AddHours([int]$m.Groups[1].Value).AddMinutes([int]$m.Groups[2].Value).AddSeconds([int]$m.Groups[3].Value).AddMilliseconds([int]$m.Groups[4].Value)
        if ($t -lt $Start.AddSeconds(-1)) { $t = $t.AddDays(1) }   # a run that crossed midnight
        return [math]::Round(($t - $Start).TotalMilliseconds)
    }
    $null
}

# the marks to read: name, then the regex of the line that carries it (a mark line, or the older line a build without marks writes)
$marks = @(
    @{ Name = "main";           Pattern = ' mark main\b|Program: Starting with args' }
    @{ Name = "window-created"; Pattern = ' mark window-created\b|Program: The window is up' }
    @{ Name = "first-render";   Pattern = ' mark first-render\b' }
    @{ Name = "index-painted";  Pattern = ' mark index-painted\b' }
    @{ Name = "headers-read";   Pattern = ' mark headers-read\b' }
    @{ Name = "repo-loaded";    Pattern = ' mark repo-loaded\b' }
    @{ Name = "git-ready";      Pattern = ' mark git-ready\b' }
    @{ Name = "scan-done";      Pattern = ' mark scan-done\b' }
    @{ Name = "serving MCP";    Pattern = 'serving MCP' }
    @{ Name = "page-up";       Pattern = '(?!)' }   # not from the log: the first page target on WebView2's debug port (see the loop), the same signal for every build
)
$windowUp = ' mark window-created\b|Program: The window is up|mark first-render\b'

$results = @()   # one hashtable per run: mark -> ms
$notes = @()
for ($i = 1; $i -le $Runs; $i++) {
    $runRepo = $Repo
    if ($Cold) {
        $runRepo = Join-Path $script:Out "startup-cold-$Tag-$i"
        if (Test-Path $runRepo) { Remove-Item $runRepo -Recurse -Force }
        New-Item -ItemType Directory -Force $runRepo | Out-Null
        Copy-Item (Join-Path $Repo "*") $runRepo -Recurse
    }
    $runHome = Join-Path $script:Out $(if ($KeepHome) { "startup-home-$Tag" } else { "startup-home-$Tag-$i" })
    if ((-not $KeepHome -or $i -eq 1) -and (Test-Path $runHome)) { Remove-Item $runHome -Recurse -Force }
    New-Item -ItemType Directory -Force $runHome | Out-Null
    $settings = Join-Path $runHome "settings.json"
    New-RunSettings $settings $runRepo $Repo

    $env:RTM_SETTINGS = $settings
    $env:RTM_HOME = $runHome; $env:RTM_PORT = [string](7731 + [int](($debugPort - 9633) / 100))   # not the user's board server
    $env:WEBVIEW2_USER_DATA_FOLDER = Join-Path $script:Out "webview"
    $env:WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS = "--remote-debugging-port=$debugPort"   # the page-up signal; the same port on every run, so every build pays the same

    $name = "rtm-startup-$PID-$i"
    $desk = [HiddenDesk]::Create($name)
    $app = $null
    try {
        $appPid = [HiddenDesk]::Start($Exe, "`"$runRepo`"", (Split-Path $Exe), $name)
        $app = [pscustomobject]@{ Pid = $appPid; Desktop = $desk }
        $startedAt = (Get-Process -Id $appPid).StartTime
        $sw = [Diagnostics.Stopwatch]::StartNew()
        $logs = Join-Path $runHome "logs"
        $lines = @(); $logUp = $false; $upAt = $null; $cdpMs = $null
        while ($sw.Elapsed.TotalSeconds -lt 60) {
            Start-Sleep -Milliseconds 100
            $now = $sw.Elapsed.TotalSeconds
            # the run log: run-<stamp>-<pid>.log, or agents-<date>.log in a build from before the run log (it has no window line, only "serving MCP")
            $file = Get-ChildItem $logs -Filter "run-*-$appPid.log" -ErrorAction SilentlyContinue | Select-Object -First 1
            if (-not $file) { $file = Get-ChildItem $logs -Filter "agents-*.log" -ErrorAction SilentlyContinue | Select-Object -First 1 }
            if ($file) {
                try { $fs = [IO.File]::Open($file.FullName, 'Open', 'Read', 'ReadWrite'); try { $lines = @((New-Object IO.StreamReader($fs)).ReadToEnd() -split '\r?\n') } finally { $fs.Dispose() } } catch { }   # the app has it open for writing: read it with the sharing it allows
                if (-not $logUp -and ($lines -join "`n") -match $windowUp) { $logUp = $true; if ($null -eq $upAt) { $upAt = $now } }
            }
            # the page is up: WebView2's debug port lists a page target with a real address (the same signal for every build, with or without marks)
            if ($null -eq $cdpMs) {
                try {
                    $targets = Invoke-RestMethod "http://localhost:$debugPort/json" -TimeoutSec 5
                    if (@($targets | Where-Object { $_.type -eq 'page' -and $_.url -and $_.url -ne 'about:blank' }).Count -gt 0) { $cdpMs = [math]::Round(((Get-Date) - $startedAt).TotalMilliseconds); if ($null -eq $upAt) { $upAt = $now } }
                } catch { $cdpErr = "$_" }
            }
            if ($null -eq $upAt) { continue }
            $text = $lines -join "`n"
            $hasMarks = $text -match ' mark (main|headers-read|repo-loaded)\b'   # a build with the marks writes "main" at once: wait for scan-done, not for the short settle that builds without marks get
            if ($hasMarks -and $text -match ' mark scan-done\b' -and $null -ne $cdpMs) { break }
            if (-not $hasMarks -and $null -ne $cdpMs -and ($now - $upAt) -gt 3 -and ($logUp -or ($now - $upAt) -gt 10)) { break }   # a build without the load marks: a short settle after the window is up
            if (($now - $upAt) -gt 25) { break }
        }
        if ($null -eq $upAt) { $notes += "run ${i}: no window-up line or page within 60 s" }
        if ($null -eq $cdpMs) { $notes += "run ${i}: no page target on the debug port $debugPort ($cdpErr)" }
    }
    finally {
        if ($app) { Stop-HiddenApp $app } else { [HiddenDesk]::Close($desk) }
    }
    $one = @{}
    if ($lines.Count -gt 0) { foreach ($m in $marks) { $ms = Get-LineMs $lines $m.Pattern $startedAt; if ($null -ne $ms) { $one[$m.Name] = $ms } } }
    if ($null -ne $cdpMs) { $one["page-up"] = $cdpMs }
    $results += , $one
    Write-Host ("run {0}/{1}: {2}" -f $i, $Runs, (($marks | ForEach-Object { if ($one.ContainsKey($_.Name)) { "$($_.Name) $($one[$_.Name])" } }) -join ", "))
    if ($Cold) { Remove-Item $runRepo -Recurse -Force -ErrorAction SilentlyContinue }
}

function Get-Median([double[]]$v) { $s = @($v | Sort-Object); $n = $s.Count; if ($n -eq 0) { return $null }; if ($n % 2) { $s[($n - 1) / 2] } else { ($s[$n / 2 - 1] + $s[$n / 2]) / 2 } }

$table = New-Object Collections.Generic.List[string]
$table.Add("startup $Tag  exe $Exe  runs $Runs  $(if ($Cold) { 'cold-ish (fresh repo copy per run)' } else { 'warm' })  repo $Repo  $(Get-Date -Format 'yyyy-MM-dd HH:mm')")
$table.Add(("{0,-16} {1,10} {2,10} {3,4}" -f "mark", "median ms", "max ms", "n"))
foreach ($m in $marks) {
    $v = @($results | Where-Object { $_.ContainsKey($m.Name) } | ForEach-Object { $_[$m.Name] })
    if ($v.Count -eq 0) { $table.Add(("{0,-16} {1,10} {2,10} {3,4}" -f $m.Name, "-", "-", 0)) }
    else { $table.Add(("{0,-16} {1,10} {2,10} {3,4}" -f $m.Name, (Get-Median $v), ($v | Measure-Object -Maximum).Maximum, $v.Count)) }
}
foreach ($n in $notes) { $table.Add($n) }
$table | Set-Content $outFile
$table
