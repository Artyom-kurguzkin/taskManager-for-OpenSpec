# The exploratory checklist for the list, as a script: what a person using a board for a while would try, not what the fixture of a unit test happens to allow.
# Run after every integration; its summary and screenshots are the evidence.   pwsh tests\exploratory.ps1 -Port 9433
#
# On the used-board demo (New-DemoRepo -Used: list view grouped by status, a saved header sort on Updated, a stale manual Order, a mixed-priority group), for each grouping
# (none, status, priority, label) with the grid's header sort on and off: Up and Down on the first, middle and last row of the largest mixed group. A move inside the group is a
# swap with the neighbour shown (Assert-Swap). The first row going Up and the last going Down leave the group, by design: they are asserted as leaving it (and, without a header
# sort, landing in the group shown next to it). Then one drag, one status pill change and one subtask tick. Every step is photographed in light and dark
# (tests\out\shots\exploratory-<step>-<theme>.png) and timed: milliseconds from the action to the last DOM mutation it caused.
# Exits 1 when any step failed. Stop the app before building; it holds the Debug exe.
param([int]$Port = 9433)
. "$PSScriptRoot\hidden-app.ps1"

$script:Results = [Collections.Generic.List[object]]::new()
$script:Note = ""

function Note([string]$text) { $script:Note = $text }

# One step: runs the check, then photographs the page in light and dark. A throw is a failed step, not the end of the run.
function Step([string]$Name, [scriptblock]$Check) {
    $script:LastMs = 0; $script:Note = ""
    $result = "pass"
    try { & $Check | Out-Null } catch { $result = "FAIL"; $script:Note = ($_.Exception.Message -replace "\s+", " ").Trim() }
    $ms = $script:LastMs; $note = $script:Note
    try { Shot "exploratory-$Name-light" | Out-Null; Set-Theme dark; Shot "exploratory-$Name-dark" | Out-Null } catch { if ($result -eq "pass") { $result = "FAIL"; $note = "screenshot: $($_.Exception.Message)" } }
    try { Set-Theme light } catch { }
    $script:Results.Add([pscustomobject]@{ Step = $Name; Result = $result; Ms = $ms; Note = $note })
    Write-Host ("  {0,-4} {1,-40} {2,6} ms  {3}" -f $result, $Name, $ms, $note)
}

# The largest group whose rows mix high and low priority, else the largest (a flat list is one group, named ""). Completed, and any group whose rows are all archived, are skipped:
# an archived row can't change status, so it can't leave its group and the first-up steps would fail by design.
function Pick-Group {
    $groups = @(Eval "(()=>{ const g = []; let cur = null; for (const r of document.querySelectorAll('tr[data-group], tr[data-id]')) { if (!r.dataset.id) { cur = { group: r.dataset.group, n: 0, arch: 0, pr: new Set() }; g.push(cur); } else { if (!cur) { cur = { group: '', n: 0, arch: 0, pr: new Set() }; g.push(cur); } cur.n++; if (r.querySelector('span.box.cell.filled[data-sk]')) cur.arch++; cur.pr.add(r.querySelector('[data-pill=priority]')?.textContent.trim()); } } return JSON.stringify(g.map(x => ({ group: x.group, n: x.n, mixed: x.pr.size > 1, archived: x.arch === x.n }))); })()" | ConvertFrom-Json)
    $best = $groups | Where-Object { $_.group -ne "Completed" -and -not $_.archived } | Sort-Object @{ Expression = { $_.mixed }; Descending = $true }, @{ Expression = { $_.n }; Descending = $true } | Select-Object -First 1
    if (-not $best -or $best.n -lt 5) { throw "no group of five rows to try the arrow keys in (largest: $($best.group) with $($best.n))" }
    $best
}

function Layout-Text($layout) { (@($layout) | ForEach-Object { "[$($_.group)] " + ($_.ids -join ' ') }) -join "  " }

# Where two id lists first differ, with a few rows around it: "from place 3: was a b c | now a c b".
function Ids-Diff($was, $now) {
    $w = @($was); $n = @($now); $i = 0
    while ($i -lt $w.Count -and $i -lt $n.Count -and $w[$i] -eq $n[$i]) { $i++ }
    $from = [Math]::Max(0, $i - 2)
    "from place $i of $($w.Count): was " + (@($w | Select-Object -Skip $from -First 8) -join ' ') + " | now " + (@($n | Select-Object -Skip $from -First 8) -join ' ')
}

# No group but the ones named has changed: same rows in the same order (the order the groups are shown in is not looked at). -AnyOrder: the same rows, in any order, for a list
# whose header sort the key dropped on purpose (BoardPage.DropGridSort): every group then goes back to the board's own order, which is not a row that moved.
function Assert-Untouched($before, $after, [string[]]$Except = @(), [switch]$AnyOrder) {
    foreach ($g in @($before)) {
        if ($Except -contains $g.group) { continue }
        $now = @($after | Where-Object { $_.group -eq $g.group } | ForEach-Object { $_.ids })
        $was = @($g.ids)
        if ($AnyOrder) { $was = @($was | Sort-Object); $now = @($now | Sort-Object) }
        if (($was -join ' ') -ne ($now -join ' ')) { throw "group '$($g.group)' moved though the key was pressed in another: $(Ids-Diff $g.ids $now)" }
    }
}

# Up on a group's first row or Down on its last leaves the group (no group beyond it: it stays). Throws unless that is all that happened.
# With the grid's header sort on, the group shown next to it is not necessarily the board's, so landing anywhere else is accepted then.
function Assert-Left($before, $after, [int]$id, [int]$dir, [string]$homeGroup, [bool]$sorted) {
    $b = @($before); $a = @($after)
    $homeBefore = @($b | Where-Object { $_.group -eq $homeGroup } | ForEach-Object { $_.ids })
    $homeAfter = @($a | Where-Object { $_.group -eq $homeGroup } | ForEach-Object { $_.ids })
    $moved = $homeAfter -notcontains $id
    $order = @($b | ForEach-Object { $_.group }); $at = [array]::IndexOf($order, $homeGroup)
    $neighbour = if ($homeGroup -ne "" -and $at + $dir -ge 0 -and $at + $dir -lt $order.Count) { $order[$at + $dir] } else { $null }
    if (-not $moved) {
        if ($neighbour -and $neighbour -ne "Completed" -and -not $sorted) { throw "row $id at the edge of '$homeGroup' did not enter '$neighbour', the group shown next to it" }
        Assert-Untouched $b $a -AnyOrder:$sorted   # nothing beyond the edge: nothing may change, the group's own order included
        if (($homeBefore -join ' ') -ne ($homeAfter -join ' ')) { throw "row $id at the edge of '$(if ($homeGroup) { $homeGroup } else { 'the list' })' stayed, but the rows were reordered: $(Ids-Diff $homeBefore $homeAfter)" }
        Note "row $id at the edge of '$(if ($homeGroup) { $homeGroup } else { 'the list' })' stayed"
        return $false
    }
    $landed = @($a | Where-Object { $_.group -ne $homeGroup -and $_.ids -contains $id })
    if ($landed.Count -ne 1) { throw "row $id left '$homeGroup' but is in $($landed.Count) other groups" }
    $into = $landed[0].group
    Assert-Untouched $b $a @($homeGroup, $into) -AnyOrder:$sorted
    $homeWas = @($homeBefore | Where-Object { $_ -ne $id })
    if (($homeWas -join ' ') -ne ($homeAfter -join ' ')) { throw "'$homeGroup' lost more than row $id`: $(Ids-Diff $homeWas $homeAfter)" }
    $intoWas = @($b | Where-Object { $_.group -eq $into } | ForEach-Object { $_.ids })
    $intoNow = @($landed[0].ids | Where-Object { $_ -ne $id })
    if (($intoNow -join ' ') -ne ($intoWas -join ' ')) { throw "'$into' was reordered, not only joined by row $id`: $(Ids-Diff $intoWas $intoNow)" }
    if (-not $sorted -and $into -ne $neighbour) { throw "row $id left '$homeGroup' into '$into', not '$neighbour', the group shown next to it" }
    Note "row $id left '$homeGroup' for '$into'$(if ($into -ne $neighbour) { " (not the group shown next to it: '$neighbour')" })"
    return $true
}


# The app stopped and started again on the same repository, from the preset: grouped by status, header sort on Updated, the stale manual Order.
function Restart-App {
    Stop-HiddenApp $script:app
    $script:app = Start-HiddenApp $demo -Port $Port
    Start-Probe
    $script:shown = "status"   # the preset's grouping
}
$demo = New-DemoRepo "demo-exploratory" -Used
$script:app = Start-HiddenApp $demo -Port $Port
$shown = ""   # the grouping on screen, so a step does not choose it again
try {
    Start-Probe
    Write-Host "exploratory checklist on $demo (CDP port $Port)"

    # ── the arrow keys, every grouping, header sort on and off ──
    foreach ($grouping in "none", "status", "priority", "label") {
        foreach ($sortOn in $true, $false) {
            foreach ($position in "first", "middle", "last") {
                foreach ($key in "ArrowUp", "ArrowDown") {
                    $name = "$grouping-sort$(if ($sortOn) { 'On' } else { 'Off' })-$position-$($key.Substring(5).ToLower())"
                    Step $name {
                        # An arrow key drops the grid's header sort; a step that wants it on starts the app again, so each starts from the board as saved, the way a user finds it after a restart (the preset: header sort on Updated, stale manual Order).
                        if ($sortOn) { Restart-App }
                        if ($shown -ne $grouping) { Set-ListGrouping $grouping; $script:shown = $grouping }
                        Set-GridSort $(if ($sortOn) { "Updated" } else { "none" })
                        $g = Pick-Group
                        $rows = ListRows $g.group
                        $at = switch ($position) { "first" { 0 } "middle" { [int][Math]::Floor($rows.Count / 2) } "last" { $rows.Count - 1 } }
                        $id = $rows[$at]; $dir = if ($key -eq "ArrowUp") { -1 } else { 1 }
                        Select-Row $id
                        $layoutBefore = @(ListLayout)
                        $after = Press $key $(if ($dir -lt 0) { 38 } else { 40 }) -Group $g.group
                        $pressMs = $script:LastMs
                        $layoutAfter = @(ListLayout)
                        $failure = $null
                        try {
                            if (($at -eq 0 -and $dir -lt 0) -or ($at -eq $rows.Count - 1 -and $dir -gt 0)) { Assert-Left $layoutBefore $layoutAfter $id $dir $g.group $sortOn | Out-Null }
                            else { Assert-Swap $rows $after $id $dir; Assert-Untouched $layoutBefore $layoutAfter @($g.group) -AnyOrder:$sortOn; Note "'$($g.group)' $($rows.Count) rows, $id $key, swapped with its neighbour" }
                        }
                        catch { $failure = $_ }
                        if ((ListRows $g.group) -notcontains $id) { Press $(if ($dir -lt 0) { "ArrowDown" } else { "ArrowUp" }) $(if ($dir -lt 0) { 40 } else { 38 }) | Out-Null }   # a row that left goes back, so the group keeps its size for the next step
                        $script:LastMs = $pressMs
                        if ($failure) { throw $failure }
                    }
                }
            }
        }
    }

    # ── the row the board puts first, shown near the end of its group by the header sort: Up must swap it with the row shown above it, not send it to the group above ──
    Step "status-sortOn-internal-first-up" {
        Restart-App   # the preset again: header sort on Updated
        $used = Get-Content (Join-Path $demo "demo-used.json") | ConvertFrom-Json
        $id = $used.Tied[0]   # high priority and the newest Updated of its group: first in the board's order, nearly last under the header sort
        $rows = ListRows $used.Group
        $at = [array]::IndexOf($rows, [int]$id)
        if ($at -lt 1 -or $at -gt $rows.Count - 2) { throw "fixture: row $id is at place $at of $($rows.Count) in '$($used.Group)', not in the middle" }
        Select-Row $id
        $layoutBefore = @(ListLayout)
        $after = Press "ArrowUp" 38 -Group $used.Group
        Assert-Swap $rows $after $id -1
        Assert-Untouched $layoutBefore @(ListLayout) @($used.Group) -AnyOrder
        Note "'$($used.Group)' $($rows.Count) rows, $id (place $at) ArrowUp swapped with its neighbour"
    }

    # ── one drag, one status pill change, one subtask tick ──
    Step "drag" {
        if ($shown -ne "status") { Set-ListGrouping "status"; $script:shown = "status" }
        Set-GridSort "none"
        $g = Pick-Group
        $rows = ListRows $g.group
        $id = $rows[1]; $target = $rows[4]
        Select-Row $id
        $from = Center "document.querySelector('tr[data-id=`"$id`"] td.num')"
        $to = Center "document.querySelector('tr[data-id=`"$target`"] td.num')"
        if ($null -eq $from -or $null -eq $to) { throw "rows $id and $target are not both on screen" }
        $allBefore = [int[]](ListRows)
        Mouse "mousePressed" $from.x $from.y 1
        for ($i = 1; $i -le 12; $i++) { Mouse "mouseMoved" $from.x ($from.y + ($to.y - $from.y) * $i / 12) 1; Start-Sleep -Milliseconds 25 }
        Start-Sleep -Milliseconds 250
        Invoke-Timed { Mouse "mouseReleased" $to.x $to.y 0 } | Out-Null
        $now = ListRows $g.group
        $allAfter = [int[]](ListRows)
        if ((($allAfter | Sort-Object) -join " ") -ne (($allBefore | Sort-Object) -join " ")) { throw "the drag lost or added rows" }
        if ([array]::IndexOf($now, $id) -eq 1) { throw "row $id did not move when dragged from place 1 onto row $target`n  before: $($rows -join ' ')`n  after:  $($now -join ' ')" }
        Note "row $id dragged onto $target in '$($g.group)': place 1 -> $([array]::IndexOf($now, $id))"
    }

    Step "status-pill" {
        if ($shown -ne "none") { Set-ListGrouping "none"; $script:shown = "none" }
        Set-GridSort "none"
        $id = Eval "[...document.querySelectorAll('tr[data-id] [data-pill=status]')][3].closest('tr').dataset.id"   # an archived row's status is not a button
        $pill = "tr[data-id=`"$id`"] [data-pill=status]"
        $current = (Eval "document.querySelector('$pill')?.textContent.trim()")
        $want = if ($current -eq "Blocked") { "Confirmed" } else { "Blocked" }
        Click (Center "(()=>{ const e = document.querySelector('$pill'); e?.scrollIntoView({ block: 'center' }); return e; })()")
        $item = Center "[...document.querySelectorAll('.menu-item')].find(i => i.textContent.trim() === '$want')"
        if ($null -eq $item) { throw "the status menu of row $id offers no '$want'" }
        Invoke-Timed { Click $item } | Out-Null
        $now = Eval "document.querySelector('$pill')?.textContent.trim()"
        if ($now -ne $want) { throw "row $id status pill reads '$now' after choosing '$want' (was '$current')" }
        Note "row $id status '$current' -> '$now'"
    }

    Step "subtask-tick" {
        if ($shown -ne "none") { Set-ListGrouping "none"; $script:shown = "none" }
        # an item shown in the list that has two or more unticked subtasks (the demo is a copy of the board, so which one that is changes as the board's work gets done)
        $shownIds = @((Eval "JSON.stringify([...document.querySelectorAll('tr[data-id]')].map(r => r.dataset.id))") | ConvertFrom-Json)
        $withSubtasks = $null
        foreach ($dir in Get-ChildItem (Join-Path $demo "openspec\changes") -Directory) {
            $tasks = Join-Path $dir.FullName "tasks.md"; $yaml = Join-Path $dir.FullName ".openspec.yaml"
            if (-not (Test-Path $tasks) -or -not (Test-Path $yaml) -or @(Select-String -Path $tasks -Pattern '^\s*- \[ \]').Count -lt 2) { continue }   # two or more unticked: ticking the last one of an item changes the page more than one tick
            $m = Select-String -Path $yaml -Pattern '^\s+id:\s*(\d+)' | Select-Object -First 1
            if ($m -and $shownIds -contains $m.Matches[0].Groups[1].Value) { $withSubtasks = [int]$m.Matches[0].Groups[1].Value; break }
        }
        if (-not $withSubtasks) { throw "fixture: no item in the list has two unticked subtasks" }
        $title = "(()=>{ const e = document.querySelector('tr[data-id=`"$withSubtasks`"] .row-title'); e?.scrollIntoView({ block: 'center' }); return e; })()"
        Click (Center $title)   # with the menu expanded the first click examines the item, the second opens it
        Start-Sleep -Milliseconds 600
        if ("$(Eval "!!document.querySelector('.sub input[type=checkbox]')")" -notmatch 'true') { Click (Center $title); Start-Sleep -Milliseconds 1200 }
        $linked = Center "document.querySelector('.item-views input[value=linked]')?.closest('.seg-o')"   # the item page's subtasks are in its Linked tasks view
        if ($linked) { Click $linked; Start-Sleep -Milliseconds 800 }
        $box = Center "document.querySelector('.sub input[type=checkbox]:not(:checked)')"
        if ($null -eq $box) { throw "item $withSubtasks opened with no unticked subtask on its page" }
        # the page's own numbers: "X of Y done" in the Subtasks tile header and the open (unticked) rows; done subtasks sit in a folded "N done" group, so counting ticked boxes would see none
        $counts = "(()=>{ const t = document.querySelector('[data-tile=subtasks]'); const m = /(\d+) of (\d+) done/.exec(t?.querySelector('.count')?.textContent ?? ''); return JSON.stringify({ done: m ? +m[1] : -1, open: t ? t.querySelectorAll('.sub input[type=checkbox]:not(:checked)').length : -1 }); })()"
        $before = (Eval $counts) | ConvertFrom-Json
        Invoke-Timed { Click $box } | Out-Null
        $after = (Eval $counts) | ConvertFrom-Json
        if ($after.done -ne $before.done + 1 -or $after.open -ne $before.open - 1) { throw "ticking a subtask of item ${withSubtasks}: done $($before.done) -> $($after.done), open $($before.open) -> $($after.open) (expected done +1, open -1)" }
        Note "item $withSubtasks subtask ticked (done $($before.done) -> $($after.done), open $($before.open) -> $($after.open))"
    }
}
finally {
    Stop-HiddenApp $script:app
}

Write-Host ""
$table = $script:Results | Format-Table Step, Result, @{ Label = "Ms"; Expression = { $_.Ms }; Alignment = "Right" }, Note -AutoSize -Wrap | Out-String -Width 220
Write-Host $table
$failed = @($script:Results | Where-Object { $_.Result -ne "pass" })
$summary = Join-Path $script:Out "exploratory-summary.txt"
Set-Content $summary $table
Write-Host "$($script:Results.Count - $failed.Count) of $($script:Results.Count) steps passed; screenshots in $script:ShotDir (exploratory-<step>-light/dark.png); summary in $summary"
if ($failed.Count -gt 0) { exit 1 }
