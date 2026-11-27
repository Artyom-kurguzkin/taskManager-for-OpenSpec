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
$script:CdpPort = 9333

Add-Type -TypeDefinition @"
using System;using System.Runtime.InteropServices;
public static class HiddenDesk {
  [StructLayout(LayoutKind.Sequential, CharSet=CharSet.Unicode)] struct STARTUPINFO { public int cb; public string r, desktop, title; public int x,y,xs,ys,xc,yc,fill,flags; public short show, cbr2; public IntPtr r2, i, o, e; }
  [StructLayout(LayoutKind.Sequential)] struct PROCESS_INFORMATION { public IntPtr hP, hT; public int pid, tid; }
  [DllImport("user32.dll", CharSet=CharSet.Unicode, SetLastError=true)] static extern IntPtr CreateDesktop(string n, string d, IntPtr dm, uint f, uint a, IntPtr sa);
  [DllImport("user32.dll")] static extern bool CloseDesktop(IntPtr h);
  [DllImport("kernel32.dll", CharSet=CharSet.Unicode, SetLastError=true)] static extern bool CreateProcess(string app, string cmd, IntPtr pa, IntPtr ta, bool inh, uint fl, IntPtr env, string dir, ref STARTUPINFO si, out PROCESS_INFORMATION pi);
  [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr h);
  [DllImport("kernel32.dll")] static extern uint ResumeThread(IntPtr t);
  [DllImport("kernel32.dll", CharSet=CharSet.Unicode)] static extern IntPtr CreateJobObject(IntPtr a, string n);
  [DllImport("kernel32.dll")] static extern bool SetInformationJobObject(IntPtr j, int c, ref JOBX i, uint n);
  [DllImport("kernel32.dll")] static extern bool AssignProcessToJobObject(IntPtr j, IntPtr p);
  [StructLayout(LayoutKind.Sequential)] struct JOBX { public long a, b; public uint flags; public UIntPtr c, d; public uint e; public UIntPtr f; public uint g, h; public ulong i1, i2, i3, i4, i5, i6; public UIntPtr m1, m2, m3, m4; }
  // One job per PowerShell process, never closed by hand: when this PowerShell exits (finished, crashed, forgotten Stop-HiddenApp),
  // Windows closes the handle and KILL_ON_JOB_CLOSE ends the app and the WebView2 processes it started. No ghost instance can outlive its check.
  static IntPtr job;
  static IntPtr Job() {
    if (job != IntPtr.Zero) return job;
    var j = CreateJobObject(IntPtr.Zero, null); var x = new JOBX(); x.flags = 0x2000;   // JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE
    if (j == IntPtr.Zero || !SetInformationJobObject(j, 9, ref x, (uint)Marshal.SizeOf(typeof(JOBX)))) throw new Exception("job object failed, error " + Marshal.GetLastWin32Error());
    return job = j;
  }
  public static IntPtr Create(string name) { var h = CreateDesktop(name, null, IntPtr.Zero, 0, 0x10000000, IntPtr.Zero); if (h == IntPtr.Zero) throw new Exception("CreateDesktop failed, error " + Marshal.GetLastWin32Error()); return h; }
  public static void Close(IntPtr h) { CloseDesktop(h); }
  public static int Start(string exe, string args, string dir, string desktop) {
    var si = new STARTUPINFO(); si.cb = Marshal.SizeOf(typeof(STARTUPINFO)); si.desktop = desktop;
    PROCESS_INFORMATION pi;
    // suspended until it is in the job, so nothing it starts can escape the job
    if (!CreateProcess(exe, "\"" + exe + "\" " + args, IntPtr.Zero, IntPtr.Zero, false, 0x4, IntPtr.Zero, dir, ref si, out pi)) throw new Exception("CreateProcess failed, error " + Marshal.GetLastWin32Error());
    bool inJob = AssignProcessToJobObject(Job(), pi.hP);
    ResumeThread(pi.hT); CloseHandle(pi.hT); CloseHandle(pi.hP);
    if (!inJob) throw new Exception("could not tie the app (pid " + pi.pid + ") to this PowerShell, error " + Marshal.GetLastWin32Error());
    return pi.pid;
  }
}
"@

# A scratch repository to verify against: this repo's openspec/ copied under tests\out. Returns its path.
# -Used: a board somebody has been using, not a fresh install (a fresh one hid the arrow-key bug of #wi194). Besides the copy it writes
# <repo>\demo-settings.json, which Start-HiddenApp starts from: the list view grouped by status, sorted by priority, the grid's header sort on
# Updated (ascending), and a manual Order holding item: keys of two columns. The copy's largest open status group gets six items that mix high and
# low priority, with distinct Updated times except for one tied pair (events.log lines in 2040, after everything real). The preset is
# described in <repo>\demo-used.json: { Group, Ids (the six, in creation order), Tied (the two that share Updated) }.
function New-DemoRepo([string]$Name = "demo", [switch]$Used) {
    $dir = Join-Path $script:Out $Name
    if (Test-Path $dir) { Remove-Item $dir -Recurse -Force }
    New-Item -ItemType Directory -Force (Join-Path $dir "openspec") | Out-Null
    Copy-Item (Join-Path $script:Repo "openspec\*") (Join-Path $dir "openspec") -Recurse
    if ($Used) { Initialize-UsedBoard $dir }
    $dir
}

function Initialize-UsedBoard([string]$Dir) {
    $items = foreach ($f in Get-ChildItem (Join-Path $Dir "openspec\changes\*\.openspec.yaml") -File) {   # the open items; archived ones are a folder deeper
        $text = [IO.File]::ReadAllText($f.FullName)
        $id = [regex]::Match($text, '(?m)^  id:\s*(\d+)'); $st = [regex]::Match($text, '(?m)^  status:\s*(.+?)\s*\r?$')
        if ($id.Success -and $st.Success -and $text -notmatch '(?m)^  hidden:\s*true') { [pscustomobject]@{ Id = [int]$id.Groups[1].Value; File = $f.FullName; Status = $st.Groups[1].Value; Dir = $f.DirectoryName } }
    }
    $groups = $items | Where-Object { $_.Status -ne "Completed" } | Group-Object Status | Sort-Object Count -Descending
    if (-not $groups -or $groups[0].Count -lt 6) { throw "the copied openspec has no status group of six open items to mix priorities in" }
    $big = @($groups[0].Group | Sort-Object Id | Select-Object -First 6)
    for ($n = 0; $n -lt $big.Count; $n++) {
        $want = if ($n % 2 -eq 0) { "high" } else { "low" }
        $text = [IO.File]::ReadAllText($big[$n].File)
        if ($text -match '(?m)^  priority:') { $text = [regex]::Replace($text, '(?m)^  priority:.*$', "  priority: $want") }
        else { $text = [regex]::Replace($text, '(?m)^(  status:.*)$', "`$1`n  priority: $want") }
        [IO.File]::WriteAllText($big[$n].File, $text)
        # Updated is the newest event in events.log: items 0 and 1 share one, the others each have their own
        $day = if ($n -lt 2) { "2040-01-05" } else { "2040-01-0$($n - 1)" }
        [IO.File]::AppendAllText((Join-Path $big[$n].Dir "events.log"), "${day}T10:00:00 | demo | note | used-board fixture`n")
    }
    $other = @($groups | Select-Object -Skip 1 -First 1)[0].Group | Sort-Object Id | Select-Object -First 2
    $order = @("item:$($other[0].Id)", "item:$($big[3].Id)", "item:$($other[-1].Id)", "item:$($big[1].Id)") | Select-Object -Unique   # keys of two columns, in no particular order
    $gridSort = @{ Columns = @(@{ Property = "Modified"; SortOrder = 0; SortIndex = 0 }); Groups = @() } | ConvertTo-Json -Depth 6 -Compress   # Radzen's DataGridSettings: a header sort on Updated
    $board = @{ View = "list"; ListGroupBy = "status"; ListSwimlane = "none"; SortBy = "priority"; Order = @($order); ListSettings = $gridSort }
    $key = [IO.Path]::GetFullPath($Dir).TrimEnd('\').ToLowerInvariant()   # SettingsStore.Key
    $settings = @{ LastRepo = $Dir; OpenRepos = @($Dir); Boards = @{ $key = $board } }
    $settings | ConvertTo-Json -Depth 8 | Set-Content (Join-Path $Dir "demo-settings.json")
    @{ Group = $groups[0].Name; Ids = @($big | ForEach-Object Id); Tied = @($big[0].Id, $big[1].Id) } | ConvertTo-Json -Compress | Set-Content (Join-Path $Dir "demo-used.json")
}

# Starts the Debug build on a hidden desktop with its own WebView2 profile and settings file, waits for the debug port,
# connects CDP. -Settings: a JSON settings file to start from (default: a fresh one that opens $RepoPath and $OpenRepos).
function Start-HiddenApp([Parameter(Mandatory)][string]$RepoPath, [string]$Settings, [string[]]$OpenRepos, [int]$Port = 9333) {
    $script:CdpPort = $Port   # agents working in parallel each take their own port
    if (-not (Test-Path $script:Exe)) { throw "not built: $script:Exe  (dotnet build TaskManager)" }
    $running = Get-Process TaskManager -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $script:Exe }
    if ($running) { throw "a Debug instance is already running (pid $($running[0].Id)); Stop-HiddenApp it first, or Stop-Process -Id $($running[0].Id)" }
    New-Item -ItemType Directory -Force $script:ShotDir | Out-Null
    $RepoPath = (Resolve-Path $RepoPath).Path
    if (-not $Settings) {
        $Settings = Join-Path $script:Out "settings-$PID.json"
        $preset = Join-Path $RepoPath "demo-settings.json"   # New-DemoRepo -Used: a board with settings already saved; copied, so each start begins from the preset
        if (Test-Path $preset) { Copy-Item $preset $Settings -Force }
        else { @{ LastRepo = $RepoPath; OpenRepos = @(@($RepoPath) + @($OpenRepos | Where-Object { $_ })) } | ConvertTo-Json | Set-Content $Settings }
    }
    $env:RTM_SETTINGS = $Settings
    # the app starts the board server it owns (#wi143): give this one a home and a port of its own, or it would replace the user's
    $env:RTM_HOME = Join-Path $script:Out "rtm-home-$Port"; $env:RTM_PORT = "$($Port - 1900)"
    $env:WEBVIEW2_USER_DATA_FOLDER = Join-Path $script:Out "webview"
    $env:WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS = "--remote-debugging-port=$script:CdpPort"
    $name = "rtm-hidden-$PID"
    $desk = [HiddenDesk]::Create($name)
    $appPid = [HiddenDesk]::Start($script:Exe, "`"$RepoPath`"", (Split-Path $script:Exe), $name)
    $sw = [Diagnostics.Stopwatch]::StartNew()
    while ($true) {
        try { $null = Invoke-RestMethod "http://localhost:$script:CdpPort/json"; break } catch { }
        if ($sw.Elapsed.TotalSeconds -gt 40) { Stop-Process -Id $appPid -Force -ErrorAction SilentlyContinue; [HiddenDesk]::Close($desk); throw "debug port $script:CdpPort never opened (another WebView2 holding the profile? see Stop-HiddenApp)" }
        Start-Sleep -Milliseconds 300
    }
    Start-Sleep -Seconds 2   # Blazor circuit + first board render
    Connect-Cdp
    $script:LastApp = [pscustomobject]@{ Pid = $appPid; Desktop = $desk; DesktopName = $name; Repo = $RepoPath; Settings = $Settings }
    $script:LastApp
}

# Stops only what Start-HiddenApp started: the app, the WebView2 processes of its profile, the desktop.
function Stop-HiddenApp($App = $script:LastApp) {   # without an argument: the instance this script started last
    if ($script:ws) { try { $script:ws.Dispose() } catch { }; $script:ws = $null }
    if ($App -and (Get-Process -Id $App.Pid -ErrorAction SilentlyContinue)) { Stop-Process -Id $App.Pid -Force }
    $profile = Join-Path $script:Out "webview"
    Get-CimInstance Win32_Process -Filter "Name='msedgewebview2.exe'" | Where-Object { $_.CommandLine -like "*$profile*" } | ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }
    Start-Sleep -Milliseconds 500
    if ($App) { [HiddenDesk]::Close($App.Desktop) }
}

# ── CDP ──
function Connect-Cdp {
    $target = (Invoke-RestMethod "http://localhost:$script:CdpPort/json")[0].webSocketDebuggerUrl
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
function Set-Theme([ValidateSet("light", "dark", "novita", "ayu-mirage")][string]$theme) { Eval "document.documentElement.dataset.theme = '$theme'; 'x'" | Out-Null; Start-Sleep -Milliseconds 300 }
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

# ── The list, for checks that must not depend on how the fixture happens to be ordered (#wi194 passed because they did) ──
# Rows are the ids of tr[data-id] in DOM order, i.e. what the user sees top to bottom; a group is a tr[data-group] header and the rows below it.
function ListLayout {   # [{ group, ids }] top to bottom; the rows before the first header (a flat list) are group ""
    $json = Eval "(()=>{ const g = [{ group: '', ids: [] }]; for (const r of document.querySelectorAll('tr[data-group], tr[data-id]')) { if (r.dataset.id) g[g.length - 1].ids.push(+r.dataset.id); else g.push({ group: r.dataset.group, ids: [] }); } return JSON.stringify(g.filter((x, i) => i > 0 || x.ids.length)); })()"
    @($json | ConvertFrom-Json) | ForEach-Object { [pscustomobject]@{ group = $_.group; ids = [int[]]@($_.ids) } }
}
# The row ids in DOM order; with a group, only the rows under that group's header. Always an array (an empty one included).
function ListRows([string]$Group) {
    $layout = @(ListLayout)
    if ($PSBoundParameters.ContainsKey('Group')) { $layout = @($layout | Where-Object { $_.group -eq $Group }) }
    , [int[]]@($layout | ForEach-Object { $_.ids })
}
# The point to click for a row: its number cell, scrolled into view first (a list row outside the window cannot be clicked).
function RowPoint($id) { Center "(()=>{ const e = document.querySelector('tr[data-id=`"$id`"] td.num'); e?.scrollIntoView({ block: 'center' }); return e; })()" }
function RowSelected($id) { Eval "(()=>{ const m = document.querySelector('style[data-rowmarks]'), tr = document.querySelector('tr[data-id=`"$id`"]'); return !!m && !!tr && JSON.parse(m.dataset.selected || '[]').includes(tr.dataset.row); })()" }
# Selects exactly this row, as a user would: Shift-click (a plain click opens the item); any other selected row is Shift-clicked off. Throws unless it ends up selected.
function Select-Row($id) {
    foreach ($other in @(Eval "JSON.stringify([...document.querySelectorAll('tr[data-id]')].filter(tr => JSON.parse(document.querySelector('style[data-rowmarks]')?.dataset.selected || '[]').includes(tr.dataset.row)).map(tr => +tr.dataset.id))" | ConvertFrom-Json)) {
        if ($other -ne $id) { Click (RowPoint $other) 8 }
    }
    if ((RowSelected $id) -ne $true) { Click (RowPoint $id) 8; Start-Sleep -Milliseconds 400 }
    if ((RowSelected $id) -ne $true) { Click (RowPoint $id) 8 }   # once more: the first click can land while the list redraws
    if ((RowSelected $id) -ne $true) { throw "row $id is not selected after a Shift-click on it" }
}

# DOM timing: a MutationObserver on the page notes when the DOM last changed. Invoke-Timed runs an action and returns the milliseconds from just before it
# to the last DOM change of the burst it caused (also in $script:LastMs), once the page has been quiet for $QuietMs.
function Start-Probe {
    Eval "if (!window.__mut) { window.__mut = { t0: 0, first: 0, last: 0 }; new MutationObserver(() => { const n = performance.now(); if (!__mut.first) __mut.first = n; __mut.last = n; }).observe(document.body, { subtree: true, childList: true, attributes: true, characterData: true }); } 'ok'" | Out-Null
}
function Invoke-Timed([scriptblock]$Do, [int]$QuietMs = 300, [int]$TimeoutMs = 6000) {
    Start-Probe
    Eval "__mut.first = 0; __mut.last = 0; __mut.t0 = performance.now(); 'x'" | Out-Null
    & $Do | Out-Null
    $sw = [Diagnostics.Stopwatch]::StartNew()
    while ($true) {
        $r = Eval "JSON.stringify({ now: performance.now(), last: __mut.last, t0: __mut.t0 })" | ConvertFrom-Json
        if ($r.last -gt 0 -and ($r.now - $r.last) -gt $QuietMs) { break }
        if ($r.last -eq 0 -and ($r.now - $r.t0) -gt 1500) { break }   # nothing changed at all
        if ($sw.ElapsedMilliseconds -gt $TimeoutMs) { break }
        Start-Sleep -Milliseconds 50
    }
    $script:LastMs = if ($r.last -gt 0) { [int]($r.last - $r.t0) } else { 0 }
    $script:LastMs
}
# Sends a key to the page (the selected row is in the focused list), waits for the DOM to settle, returns the rows as ListRows does.
function Press([string]$Key, [int]$Code, [string]$Group) {
    $k = $Key; $c = $Code
    Invoke-Timed { Key $k $c } | Out-Null
    if ($PSBoundParameters.ContainsKey('Group')) { ListRows $Group } else { ListRows }
}
# Throws unless $after is $before with only $id and its neighbour in direction $dir (-1 / "ArrowUp" above it, 1 / "ArrowDown" below it) exchanged.
function Assert-Swap($before, $after, $id, $dir) {
    $d = if ("$dir" -eq "ArrowUp") { -1 } elseif ("$dir" -eq "ArrowDown") { 1 } else { [int]$dir }
    $b = @($before | ForEach-Object { [int]$_ }); $a = @($after | ForEach-Object { [int]$_ })
    $at = [array]::IndexOf($b, [int]$id); $to = $at + $d
    if ($at -lt 0 -or $to -lt 0 -or $to -ge $b.Count) { throw "Assert-Swap: row $id has no neighbour at $d in [$($b -join ' ')]" }
    $want = $b.Clone(); $want[$at] = $b[$to]; $want[$to] = $b[$at]
    if (($want -join ' ') -ne ($a -join ' ')) { throw "Assert-Swap: row $id $d should only swap with $($b[$to])`n  before:   $($b -join ' ')`n  expected: $($want -join ' ')`n  actual:   $($a -join ' ')" }
}

# The side menu's Organise section open (a collapsed menu opened first); the list's groups dropdown lives there.
function Open-Organise {
    if ((Eval "!!document.querySelector('.nav-view')") -ne $true) {
        # collapsed, the menu hides off the left edge (#wi211) and slides in when the pointer reaches it, as for a person: hover the edge, wait for the slide, then click the toggle
        Mouse "mouseMoved" 2 300 0; Start-Sleep -Milliseconds 400
        Click (Center "document.querySelector('button.nav-toggle')")
    }
    if ((Eval "document.querySelector('.nav-sec-h[data-sec=organise]')?.getAttribute('aria-expanded')") -ne 'true') { Click (Center "document.querySelector('.nav-sec-h[data-sec=organise]')") }
}
# The list's groups (side menu, Organise, "groups"): none, status, feature, label, priority, contributor, due date or start date.
function Set-ListGrouping([Parameter(Mandatory)][string]$Field) {
    for ($try = 0; $try -lt 3; $try++) {   # the dropdown does not always take the first click after a restart: look at what it reads, ask again when it is not the field
        Open-Organise
        Invoke-Timed {
            Click (Center "document.querySelector('[aria-label=groups]')")
            Click (Center "[...document.querySelectorAll('.rz-dropdown-panel li')].find(l => l.getAttribute('aria-label') === '$Field' && l.getClientRects().length)")
        } | Out-Null
        if ((Eval "document.querySelector('[aria-label=groups]')?.textContent.trim()") -like "*$Field*") { return }
        Eval "document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape' })); 'x'" | Out-Null
    }
    throw "the list's groups did not reach '$Field'"
}
# The grid header sort: a column title ("Updated") sorts it ascending (clicking the header cycles ascending, descending, off), "none" clears it. Throws when it cannot get there.
function Set-GridSort([Parameter(Mandatory)][string]$Column) {
    for ($i = 0; $i -lt 4; $i++) {
        $sorted = @(Eval "JSON.stringify([...document.querySelectorAll('th.rz-sortable-column')].map(t => ({ c: t.querySelector('.th-l')?.textContent.trim(), s: t.getAttribute('aria-sort') })).filter(x => x.s && x.s !== 'none'))" | ConvertFrom-Json)
        if ($Column -eq 'none' -and $sorted.Count -eq 0) { return }
        if ($Column -ne 'none' -and $sorted.Count -eq 1 -and $sorted[0].c -eq $Column -and $sorted[0].s -eq 'ascending') { return }
        $target = if ($Column -eq 'none') { $sorted[0].c } else { $Column }
        Invoke-Timed { Click (Center "(()=>{ const e = [...document.querySelectorAll('th.rz-sortable-column')].find(t => t.querySelector('.th-l')?.textContent.trim() === '$target')?.querySelector('.th-l'); e?.scrollIntoView({ inline: 'center', block: 'nearest' }); return e; })()") } | Out-Null   # the list scrolls sideways when it has many columns: a header off the 1280px window takes no click
        Eval "document.querySelector('.tbl-wrap')?.scrollTo({ left: 0 }); 'x'" | Out-Null   # and back: the row numbers are in the first column
    }
    throw "the grid header sort did not reach '$Column'"
}
