# Ref §13 UI smoke test: two runs, each exit 0 with a result file; the warm (second) run's firstFrameMs ≤ 1000;
# "not applicable" placeholder visibility needs -AllowNoPlaceholders. Baseline: ReadyToRun 0.37 s (Ref §2.2).

function New-ResultFile([string]$Dir, [string]$FileName, $Content) {
    $path = Join-Path $Dir $FileName
    $Content | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $path -Encoding utf8
    $path
}

function New-PassingResult([double]$FirstFrameMs) {
    @{ ok = $true; firstFrameMs = $FirstFrameMs
       checks = @(@{ name = 'templates'; status = 'pass'; detail = 'Anvil Mountain rendered' },
                  @{ name = 'placeholderVisibility'; status = 'pass'; detail = '3 cloud-only entries' }) }
}

function New-Run([int]$Run, [bool]$Ok, $FirstFrameMs, [string[]]$Reasons = @()) {
    [pscustomobject]@{ Run = $Run; Ok = $Ok; FirstFrameMs = $FirstFrameMs; Reasons = $Reasons }
}

Test-Case 'Gate: a clean run passes and reports its first frame' {
    $dir = New-TestDir
    try {
        $path = New-ResultFile $dir 'r.json' (New-PassingResult 412)
        $r = Test-SelftestRun -Run 1 -ExitCode 0 -ResultPath $path
        Assert-True $r.Ok ($r.Reasons -join '; ')
        Assert-Equal 412 $r.FirstFrameMs 'FirstFrameMs'
    }
    finally { Remove-Item -LiteralPath $dir -Recurse -Force }
}

Test-Case 'Gate: a non-zero exit code fails' {
    $dir = New-TestDir
    try {
        $path = New-ResultFile $dir 'r.json' (New-PassingResult 412)
        $r = Test-SelftestRun -Run 1 -ExitCode 1 -ResultPath $path
        Assert-True (-not $r.Ok) 'exit 1 must fail'
        Assert-True (($r.Reasons -join ' ') -like '*exit code 1*') ($r.Reasons -join '; ')
    }
    finally { Remove-Item -LiteralPath $dir -Recurse -Force }
}

Test-Case 'Gate: a timed-out run fails' {
    $dir = New-TestDir
    try {
        $r = Test-SelftestRun -Run 2 -ExitCode -1 -TimedOut $true -ResultPath (Join-Path $dir 'missing.json')
        Assert-True (-not $r.Ok) 'timeout must fail'
        Assert-True (($r.Reasons -join ' ') -like '*timed out*') ($r.Reasons -join '; ')
    }
    finally { Remove-Item -LiteralPath $dir -Recurse -Force }
}

Test-Case 'Gate: a missing result file fails even with exit 0' {
    $dir = New-TestDir
    try {
        $r = Test-SelftestRun -Run 1 -ExitCode 0 -ResultPath (Join-Path $dir 'missing.json')
        Assert-True (-not $r.Ok) 'no result file must fail'
        Assert-True (($r.Reasons -join ' ') -like '*no result file*') ($r.Reasons -join '; ')
    }
    finally { Remove-Item -LiteralPath $dir -Recurse -Force }
}

Test-Case 'Gate: ok=false in the result fails even with exit 0' {
    $dir = New-TestDir
    try {
        $content = New-PassingResult 400
        $content.ok = $false
        $r = Test-SelftestRun -Run 1 -ExitCode 0 -ResultPath (New-ResultFile $dir 'r.json' $content)
        Assert-True (-not $r.Ok) 'ok=false must fail'
    }
    finally { Remove-Item -LiteralPath $dir -Recurse -Force }
}

Test-Case 'Gate: a failed check fails and is named' {
    $dir = New-TestDir
    try {
        $content = New-PassingResult 400
        $content.checks += @{ name = 'map'; status = 'fail'; detail = 'no pong within 20 s' }
        $r = Test-SelftestRun -Run 1 -ExitCode 0 -ResultPath (New-ResultFile $dir 'r.json' $content)
        Assert-True (-not $r.Ok) 'a failed check must fail'
        Assert-True (($r.Reasons -join ' ') -like "*'map' failed*no pong*") ($r.Reasons -join '; ')
    }
    finally { Remove-Item -LiteralPath $dir -Recurse -Force }
}

Test-Case 'Gate: a not-applicable placeholder check needs -AllowNoPlaceholders' {
    $dir = New-TestDir
    try {
        $content = New-PassingResult 400
        $content.checks[1].status = 'notApplicable'
        $path = New-ResultFile $dir 'r.json' $content
        $without = Test-SelftestRun -Run 1 -ExitCode 0 -ResultPath $path
        Assert-True (-not $without.Ok) 'notApplicable without the switch must fail'
        Assert-True (($without.Reasons -join ' ') -like '*-AllowNoPlaceholders*') ($without.Reasons -join '; ')
        $with = Test-SelftestRun -Run 1 -ExitCode 0 -ResultPath $path -AllowNoPlaceholders
        Assert-True $with.Ok ($with.Reasons -join '; ')
    }
    finally { Remove-Item -LiteralPath $dir -Recurse -Force }
}

Test-Case 'Gate: only placeholderVisibility may be not applicable, even with -AllowNoPlaceholders' {
    $dir = New-TestDir
    try {
        $content = New-PassingResult 400
        $content.checks[0].status = 'notApplicable'
        $path = New-ResultFile $dir 'r.json' $content
        $r = Test-SelftestRun -Run 1 -ExitCode 0 -ResultPath $path -AllowNoPlaceholders
        Assert-True (-not $r.Ok) 'a notApplicable templates check must fail'
        Assert-True (($r.Reasons -join ' ') -like "*'templates'*only placeholderVisibility*") ($r.Reasons -join '; ')
    }
    finally { Remove-Item -LiteralPath $dir -Recurse -Force }
}

Test-Case 'Gate: a result without firstFrameMs or checks fails' {
    $dir = New-TestDir
    try {
        $r = Test-SelftestRun -Run 1 -ExitCode 0 -ResultPath (New-ResultFile $dir 'r.json' @{ ok = $true })
        Assert-True (-not $r.Ok) 'missing firstFrameMs and checks must fail'
        $all = $r.Reasons -join ' '
        Assert-True ($all -like '*firstFrameMs*' -and $all -like '*no checks*') $all
    }
    finally { Remove-Item -LiteralPath $dir -Recurse -Force }
}

function New-RawResultFile([string]$Dir, [string]$Text) {
    $path = Join-Path $Dir 'r.json'
    [System.IO.File]::WriteAllText($path, $Text)
    $path
}

function Assert-NotAnObjectResultFails([string]$Text) {
    $dir = New-TestDir
    try {
        $r = Test-SelftestRun -Run 1 -ExitCode 0 -ResultPath (New-RawResultFile $dir $Text)
        Assert-True (-not $r.Ok) 'a result that is not a JSON object must fail'
        Assert-True ($null -eq $r.FirstFrameMs) "FirstFrameMs must stay null, got '$($r.FirstFrameMs)'"
        Assert-True (($r.Reasons -join ' ') -like '*run 1: the result file is empty or not a JSON object*') ($r.Reasons -join '; ')
        $g = Test-SelftestGate -Runs @($r, (New-Run 2 $true 300))
        Assert-True (-not $g.Ok) 'the gate must fail'
    }
    finally { Remove-Item -LiteralPath $dir -Recurse -Force }
}

Test-Case 'Gate: an empty (0-byte) result file fails even with exit 0' { Assert-NotAnObjectResultFails '' }

Test-Case 'Gate: a whitespace-only result file fails even with exit 0' { Assert-NotAnObjectResultFails " `r`n`t " }

Test-Case "Gate: a result file holding the JSON text 'null' fails even with exit 0" { Assert-NotAnObjectResultFails 'null' }

Test-Case 'Gate: a result file holding a JSON array fails even with exit 0' { Assert-NotAnObjectResultFails '[1, 2]' }

Test-Case 'Gate: a result with ok and checks but no firstFrameMs fails' {
    $dir = New-TestDir
    try {
        $content = New-PassingResult 400
        $content.Remove('firstFrameMs')
        $r = Test-SelftestRun -Run 2 -ExitCode 0 -ResultPath (New-ResultFile $dir 'r.json' $content)
        Assert-True (-not $r.Ok) 'missing firstFrameMs must fail'
        Assert-True (($r.Reasons -join ' ') -like '*run 2: the result has no numeric firstFrameMs*') ($r.Reasons -join '; ')
    }
    finally { Remove-Item -LiteralPath $dir -Recurse -Force }
}

Test-Case 'Gate: a run reported Ok without a numeric firstFrameMs never passes the gate' {
    foreach ($ff in @($null, 'fast')) {
        $cold = Test-SelftestGate -Runs @((New-Run 1 $true $ff), (New-Run 2 $true 300))
        Assert-True (-not $cold.Ok) "a cold run with firstFrameMs '$ff' must fail the gate"
        Assert-True (($cold.Reasons -join ' ') -like '*run 1*no numeric firstFrameMs*') ($cold.Reasons -join '; ')
        $warm = Test-SelftestGate -Runs @((New-Run 1 $true 300), (New-Run 2 $true $ff))
        Assert-True (-not $warm.Ok) "a warm run with firstFrameMs '$ff' must fail the gate"
        Assert-True (($warm.Reasons -join ' ') -like '*run 2*no numeric firstFrameMs*') ($warm.Reasons -join '; ')
    }
}

Test-Case 'Gate: the warm run passes at 1000 ms and fails at 1001 ms' {
    $at = Test-SelftestGate -Runs @((New-Run 1 $true 2400), (New-Run 2 $true 1000))
    Assert-True $at.Ok ($at.Reasons -join '; ')
    $over = Test-SelftestGate -Runs @((New-Run 1 $true 2400), (New-Run 2 $true 1001))
    Assert-True (-not $over.Ok) '1001 ms must fail the gate'
    Assert-True (($over.Reasons -join ' ') -like '*1001*1000*') ($over.Reasons -join '; ')
}

Test-Case 'Gate: only the second run counts as warm' {
    $g = Test-SelftestGate -Runs @((New-Run 1 $true 2500), (New-Run 2 $true 600))
    Assert-True $g.Ok ($g.Reasons -join '; ')
    Assert-Equal 2500 $g.ColdMs 'ColdMs'
    Assert-Equal 600 $g.WarmMs 'WarmMs'
}

Test-Case 'Gate: slower than the 0.37 s ReadyToRun baseline is flagged (reported, not a failure) and does not fail the gate' {
    $slow = Test-SelftestGate -Runs @((New-Run 1 $true 900), (New-Run 2 $true 371))
    Assert-True $slow.Ok 'the 1 s gate still passes'
    Assert-True $slow.SlowerThanBaseline '371 ms is slower than 370 ms'
    $even = Test-SelftestGate -Runs @((New-Run 1 $true 900), (New-Run 2 $true 370))
    Assert-True (-not $even.SlowerThanBaseline) '370 ms meets the baseline'
}

Test-Case 'Gate: a failed first run fails the gate and carries its reason' {
    $g = Test-SelftestGate -Runs @((New-Run 1 $false 300 @('run 1: exit code 1')), (New-Run 2 $true 300))
    Assert-True (-not $g.Ok) 'a failed run must fail the gate'
    Assert-True (($g.Reasons -join ' ') -like '*run 1: exit code 1*') ($g.Reasons -join '; ')
}

Test-Case 'Gate: exactly two runs are required' {
    $g = Test-SelftestGate -Runs @((New-Run 1 $true 300))
    Assert-True (-not $g.Ok) 'one run must fail the gate'
}
