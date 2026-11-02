# Real-app verification without touching the user's desktop. Dot-source it:   . .\tests\hidden-app.ps1
#
#   $demo = New-DemoRepo                       # a scratch copy of this repo's openspec/ (never verify against the real one)
#   $app  = Start-HiddenApp $demo              # Debug build on a HIDDEN Windows desktop, CDP on port 9333; returns pid etc.
#   Eval "document.querySelectorAll('article.card').length"
#   Click (Center "document.querySelector('article.card .band')");  Shot "item-page";  Set-Theme dark;  Shot "item-page-dark"
#   Stop-HiddenApp $app                        # always, also after a failure: it unlocks the exe for the next build
#
# The window lives on a desktop made with CreateDesktop that is never shown: nothing appears on the user's screen, the app
# cannot take their foreground, and their keyboard never reaches it. Input goes over the Chrome DevTools Protocol
# (Input.dispatchMouseEvent / dispatchKeyEvent are real pointer and key events to the page), screenshots come from
# Page.captureScreenshot. Measured 1 Nov 2026: port up in ~2 s, rAF 64/s, clicks, pointer drags and Radzen popups all work.
$ErrorActionPreference = 'Stop'
$script:Repo = Split-Path -Parent $PSScriptRoot
$script:Exe = Join-Path $script:Repo "TaskManager\bin\Debug\net10.0\TaskManager.exe"
$script:Out = Join-Path $script:Repo "tests\out"          # gitignored: screenshots, scratch settings, WebView2 profile
$script:ShotDir = Join-Path $script:Out "shots"
$script:Port = 9333

Add-Type -TypeDefinition @"
using System;using System.Runtime.InteropServices;
public static class HiddenDesk {
  [StructLayout(LayoutKind.Sequential, CharSet=CharSet.Unicode)] struct STARTUPINFO { public int cb; public string r, desktop, title; public int x,y,xs,ys,xc,yc,fill,flags; public short show, cbr2; public IntPtr r2, i, o, e; }
  [StructLayout(LayoutKind.Sequential)] struct PROCESS_INFORMATION { public IntPtr hP, hT; public int pid, tid; }
  [DllImport("user32.dll", CharSet=CharSet.Unicode, SetLastError=true)] static extern IntPtr CreateDesktop(string n, string d, IntPtr dm, uint f, uint a, IntPtr sa);
  [DllImport("user32.dll")] static extern bool CloseDesktop(IntPtr h);
  [DllImport("kernel32.dll", CharSet=CharSet.Unicode, SetLastError=true)] static extern bool CreateProcess(string app, string cmd, IntPtr pa, IntPtr ta, bool inh, uint fl, IntPtr env, string dir, ref STARTUPINFO si, out PROCESS_INFORMATION pi);
  [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr h);
  public static IntPtr Create(string name) { var h = CreateDesktop(name, null, IntPtr.Zero, 0, 0x10000000, IntPtr.Zero); if (h == IntPtr.Zero) throw new Exception("CreateDesktop failed, error " + Marshal.GetLastWin32Error()); return h; }
  public static void Close(IntPtr h) { CloseDesktop(h); }
  public static int Start(string exe, string args, string dir, string desktop) {
    var si = new STARTUPINFO(); si.cb = Marshal.SizeOf(typeof(STARTUPINFO)); si.desktop = desktop;
    PROCESS_INFORMATION pi;
    if (!CreateProcess(exe, "\"" + exe + "\" " + args, IntPtr.Zero, IntPtr.Zero, false, 0, IntPtr.Zero, dir, ref si, out pi)) throw new Exception("CreateProcess failed, error " + Marshal.GetLastWin32Error());
    CloseHandle(pi.hT); CloseHandle(pi.hP); return pi.pid;
  }
}
"@

# A scratch repository to verify against: this repo's openspec/ copied under tests\out. Returns its path.
function New-DemoRepo([string]$Name = "demo") {
    $dir = Join-Path $script:Out $Name
    if (Test-Path $dir) { Remove-Item $dir -Recurse -Force }
    New-Item -ItemType Directory -Force (Join-Path $dir "openspec") | Out-Null
    Copy-Item (Join-Path $script:Repo "openspec\*") (Join-Path $dir "openspec") -Recurse
    $dir
}

# Starts the Debug build on a hidden desktop with its own WebView2 profile and settings file, waits for the debug port,
# connects CDP. -Settings: a JSON settings file to start from (default: a fresh one that opens $RepoPath and $OpenRepos).
function Start-HiddenApp([Parameter(Mandatory)][string]$RepoPath, [string]$Settings, [string[]]$OpenRepos) {
    if (-not (Test-Path $script:Exe)) { throw "not built: $script:Exe  (dotnet build TaskManager)" }
    $running = Get-Process TaskManager -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $script:Exe }
    if ($running) { throw "a Debug instance is already running (pid $($running[0].Id)); Stop-HiddenApp it first, or Stop-Process -Id $($running[0].Id)" }
    New-Item -ItemType Directory -Force $script:ShotDir | Out-Null
    $RepoPath = (Resolve-Path $RepoPath).Path
    if (-not $Settings) {
        $Settings = Join-Path $script:Out "settings-$PID.json"
        @{ LastRepo = $RepoPath; OpenRepos = @(@($RepoPath) + @($OpenRepos | Where-Object { $_ })) } | ConvertTo-Json | Set-Content $Settings
    }
    $env:RTM_SETTINGS = $Settings
    $env:WEBVIEW2_USER_DATA_FOLDER = Join-Path $script:Out "webview"
    $env:WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS = "--remote-debugging-port=$script:Port"
    $name = "rtm-hidden-$PID"
    $desk = [HiddenDesk]::Create($name)
    $appPid = [HiddenDesk]::Start($script:Exe, "`"$RepoPath`"", (Split-Path $script:Exe), $name)
    $sw = [Diagnostics.Stopwatch]::StartNew()
    while ($true) {
        try { $null = Invoke-RestMethod "http://localhost:$script:Port/json"; break } catch { }
        if ($sw.Elapsed.TotalSeconds -gt 40) { Stop-Process -Id $appPid -Force -ErrorAction SilentlyContinue; [HiddenDesk]::Close($desk); throw "debug port $script:Port never opened (another WebView2 holding the profile? see Stop-HiddenApp)" }
        Start-Sleep -Milliseconds 300
    }
    Start-Sleep -Seconds 2   # Blazor circuit + first board render
    Connect-Cdp
    [pscustomobject]@{ Pid = $appPid; Desktop = $desk; DesktopName = $name; Repo = $RepoPath; Settings = $Settings }
}

# Stops only what Start-HiddenApp started: the app, the WebView2 processes of its profile, the desktop.
function Stop-HiddenApp($App) {
    if ($script:ws) { try { $script:ws.Dispose() } catch { }; $script:ws = $null }
    if ($App -and (Get-Process -Id $App.Pid -ErrorAction SilentlyContinue)) { Stop-Process -Id $App.Pid -Force }
    $profile = Join-Path $script:Out "webview"
    Get-CimInstance Win32_Process -Filter "Name='msedgewebview2.exe'" | Where-Object { $_.CommandLine -like "*$profile*" } | ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }
    Start-Sleep -Milliseconds 500
    if ($App) { [HiddenDesk]::Close($App.Desktop) }
}

# ── CDP ──
function Connect-Cdp {
    $target = (Invoke-RestMethod "http://localhost:$script:Port/json")[0].webSocketDebuggerUrl
    $script:ws = New-Object System.Net.WebSockets.ClientWebSocket
    $script:ws.ConnectAsync([Uri]$target, [Threading.CancellationToken]::None).Wait()
    $script:cdpId = 0
}
function Send-Cdp($method, $params) {
    $script:cdpId++; $myId = $script:cdpId
    $bytes = [Text.Encoding]::UTF8.GetBytes((@{ id = $myId; method = $method; params = $params } | ConvertTo-Json -Depth 10 -Compress))
    $script:ws.SendAsync([ArraySegment[byte]]::new($bytes), [Net.WebSockets.WebSocketMessageType]::Text, $true, [Threading.CancellationToken]::None).Wait()
    while ($true) {   # replies interleave with events: read until ours
        $buf = New-Object byte[] 4194304; $all = ""
        try { $cts = New-Object Threading.CancellationTokenSource(8000); do { $res = $script:ws.ReceiveAsync([ArraySegment[byte]]::new($buf), $cts.Token).Result; $all += [Text.Encoding]::UTF8.GetString($buf, 0, $res.Count) } while (-not $res.EndOfMessage) }
        catch { throw "TIMEOUT waiting for $method (a runaway loop in app.js? check msedgewebview2 CPU)" }
        $obj = $all | ConvertFrom-Json
        if ($obj.id -eq $myId) { return $obj }
    }
}
# JavaScript in the page; returns the value, or "JS EXC: ...".
function Eval($expr) { $r = Send-Cdp "Runtime.evaluate" @{ expression = $expr; returnByValue = $true }; if ($r.result.exceptionDetails) { return "JS EXC: " + $r.result.exceptionDetails.exception.description }; return $r.result.result.value }
# Screenshot to tests\out\shots\<name>.png; returns the path.
function Shot($name) { $r = Send-Cdp "Page.captureScreenshot" @{ format = "png" }; $p = Join-Path $script:ShotDir "$name.png"; [IO.File]::WriteAllBytes($p, [Convert]::FromBase64String($r.result.data)); $p }
function Set-Theme([ValidateSet("light", "dark")][string]$theme) { Eval "document.documentElement.dataset.theme = '$theme'; 'x'" | Out-Null; Start-Sleep -Milliseconds 300 }
# Real pointer / key events (CSS px, from getBoundingClientRect). $modifiers: 1 Alt, 2 Ctrl, 4 Meta, 8 Shift.
function Mouse($type, $x, $y, $buttons, $modifiers = 0) { if ($null -eq $x -or $null -eq $y) { return }; Send-Cdp "Input.dispatchMouseEvent" @{ type = $type; x = [int]$x; y = [int]$y; button = "left"; buttons = $buttons; clickCount = 1; modifiers = $modifiers } | Out-Null }
function Key($key, $code, $modifiers = 0) { Send-Cdp "Input.dispatchKeyEvent" @{ type = "keyDown"; key = $key; code = $key; windowsVirtualKeyCode = $code; modifiers = $modifiers } | Out-Null; Send-Cdp "Input.dispatchKeyEvent" @{ type = "keyUp"; key = $key; code = $key; windowsVirtualKeyCode = $code; modifiers = $modifiers } | Out-Null }
function Type-Text($text) { Send-Cdp "Input.insertText" @{ text = $text } | Out-Null }
# Centre of the element a JS expression returns, or $null (with a note) when there is none. Click/Drag take these.
function Center($expr) { $v = Eval "(()=>{const el=($expr); if(!el) return null; const r=el.getBoundingClientRect(); return JSON.stringify({x:r.x+r.width/2,y:r.y+r.height/2})})()"; if ($null -eq $v -or "$v" -like "JS EXC*") { Write-Host "  [no element: $($expr.Substring(0, [Math]::Min(90, $expr.Length)))]"; return $null }; $v | ConvertFrom-Json }
function Click($p, $modifiers = 0) { if ($null -eq $p) { return }; Mouse "mousePressed" $p.x $p.y 1 $modifiers; Mouse "mouseReleased" $p.x $p.y 0 $modifiers; Start-Sleep -Milliseconds 300 }
function Drag($from, $to, $shotName) {
    if ($null -eq $from -or $null -eq $to) { return }
    Mouse "mousePressed" $from.x $from.y 1
    for ($i = 1; $i -le 12; $i++) { Mouse "mouseMoved" ($from.x + ($to.x - $from.x) * $i / 12) ($from.y + ($to.y - $from.y) * $i / 12) 1; Start-Sleep -Milliseconds 25 }
    Start-Sleep -Milliseconds 250
    if ($shotName) { Shot $shotName | Out-Null }
    Mouse "mouseReleased" $to.x $to.y 0
    Start-Sleep -Milliseconds 900
}
# Board shorthands.
function ColSel($name) { "[...document.querySelectorAll('section.col')].find(c => c.querySelector('h3').textContent === '$name')" }
function Columns { Eval "JSON.stringify([...document.querySelectorAll('section.col h3')].map(h => h.textContent))" }
function Cards($column) { Eval "JSON.stringify([...$(ColSel $column).querySelectorAll('article.card .band')].map(b => b.textContent.trim()))" }
function Toasts { Eval "JSON.stringify([...document.querySelectorAll('.toast')].map(t => t.textContent.trim()))" }
