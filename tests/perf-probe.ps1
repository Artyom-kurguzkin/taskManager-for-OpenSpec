# How fast the UI answers a click, measured in the real app on a hidden desktop (see hidden-app.ps1).   Run:  pwsh tests\perf-probe.ps1
#
# For each step a MutationObserver on document.body timestamps every DOM change after the click. Printed per step:
#   first    ms from the click to the first DOM change (how soon the user sees anything happen)
#   settled  ms from the click to the last DOM change (when the page stopped moving)
#   bursts   render bursts: runs of changes separated by gaps over 150 ms (1 is one render; more is a cascade)
# Stop the app before building (the exe is locked while it runs). The demo is a scratch git repo under tests\out, never the real openspec/.
. "$PSScriptRoot\hidden-app.ps1"

$port = 9365
$demo = New-DemoRepo "perf-probe"
$change = Join-Path $demo "openspec\changes\perf-probe"
New-Item -ItemType Directory -Force (Join-Path $change "notes"), (Join-Path $change "specs\probe") | Out-Null
$nl = "`n"
Set-Content (Join-Path $change ".openspec.yaml") ("schema: spec-driven${nl}created: 2026-11-01${nl}x-rtm:${nl}  id: 9001${nl}  title: Perf probe item${nl}  status: In Progress${nl}  priority: high${nl}  contributors: [demo]${nl}") -NoNewline
Set-Content (Join-Path $change "proposal.md") "# Perf probe item${nl}${nl}A **demo** item with a description, notes and subtasks, for timing the UI.${nl}${nl}- one${nl}- two${nl}" -NoNewline
$tasks = "# Tasks${nl}${nl}## 1. Work${nl}${nl}" + ((1..8 | ForEach-Object { "- [ ] 1.$_ Subtask number $_" }) -join $nl) + $nl
Set-Content (Join-Path $change "tasks.md") $tasks -NoNewline
Set-Content (Join-Path $change "specs\probe\spec.md") "## ADDED Requirements${nl}${nl}### Requirement: Probe${nl}The probe SHALL measure.${nl}${nl}#### Scenario: Measured${nl}- **WHEN** it runs${nl}- **THEN** it prints${nl}" -NoNewline
foreach ($n in 1..15) {
    Set-Content (Join-Path $change "notes\n$(9000 + $n).md") "---${nl}id: $(9000 + $n)${nl}by: demo${nl}created: 2026-11-$("{0:00}" -f (1 + $n % 9))T1$($n % 10):00:00${nl}---${nl}# Note $n${nl}${nl}Some **markdown** text with a [link](https://example.com) and a list:${nl}${nl}- a${nl}- b${nl}" -NoNewline
}
Push-Location $demo
try {
    git -c core.safecrlf=false init -q
    git -c core.safecrlf=false add -A
    git -c user.name=demo -c user.email=d@d commit -q -m "demo repo"
    1..40 | ForEach-Object { git -c user.name=demo -c user.email=d@d commit -q --allow-empty -m "work on #wi9001 step $_" }
} finally { Pop-Location }

$app = Start-HiddenApp $demo -Port $port
$rows = @()
try {
    Eval @"
window.__p = { t0: 0, ev: [] };
new MutationObserver(() => __p.ev.push(performance.now())).observe(document.body, { subtree: true, childList: true, attributes: true, characterData: true });
'ok'
"@ | Out-Null

    # Clicks at a point, timing the DOM changes that follow; waits until the page has been still for 1.2 s.
    function Measure-Step([string]$Name, [string]$Expr) {
        $p = Center $Expr
        if ($null -eq $p) { return [pscustomobject]@{ Step = $Name; 'first ms' = 'n/a'; 'settled ms' = 'n/a'; Bursts = 'n/a' } }
        Eval "__p.ev = []; __p.t0 = performance.now(); 'x'" | Out-Null
        Mouse "mousePressed" $p.x $p.y 1
        Mouse "mouseReleased" $p.x $p.y 0
        Start-Sleep -Milliseconds 2200
        $r = Eval "JSON.stringify({ t0: __p.t0, ev: __p.ev })" | ConvertFrom-Json
        $ev = @($r.ev)
        if ($ev.Count -eq 0) { return [pscustomobject]@{ Step = $Name; 'first ms' = 'none'; 'settled ms' = 'none'; Bursts = 0 } }
        $bursts = 1
        for ($i = 1; $i -lt $ev.Count; $i++) { if ($ev[$i] - $ev[$i - 1] -gt 150) { $bursts++ } }
        [pscustomobject]@{ Step = $Name; 'first ms' = [int]($ev[0] - $r.t0); 'settled ms' = [int]($ev[-1] - $r.t0); Bursts = $bursts }
    }

    $rows += Measure-Step "open item from the board" "document.querySelector(`"article.card[data-id='9001'] .band`")"
    $rows += Measure-Step "switch to Worklog" "document.querySelector(`".item-views input[value='worklog']`")?.closest('.seg-o')"
    $rows += Measure-Step "switch to Linked tasks" "document.querySelector(`".item-views input[value='linked']`")?.closest('.seg-o')"
    $rows += Measure-Step "tick a subtask" "document.querySelector('.sub input[type=checkbox]:not(:checked)')"
    $rows += Measure-Step "back to the board" "document.querySelector('.sheet-close')"
    $rows | Format-Table -AutoSize | Out-String | Write-Host
} finally {
    Stop-HiddenApp $app
}
