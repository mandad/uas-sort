#requires -Version 7.0
<#
.SYNOPSIS
  Runs `uas-sort.exe --selftest --result <tmp>` N times (60 s timeout each) and checks every run's exit code and
  result file (Ref §13, Part 13 contract). A run fails on a non-zero exit code, "ok": false, or any check with
  "status": "fail". The last run is the warm run: -MaxWarmFirstFrameMs gates its firstFrameMs (default 1000, the hard
  gate; 0 = not gated). -BaselineMs (default 370, the ReadyToRun baseline) only decides slowerThanBaseline, which is
  printed and never fails the script.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Exe,
    [int]$Runs = 2,
    [double]$MaxWarmFirstFrameMs = 1000,
    [double]$BaselineMs = 370
)

$ErrorActionPreference = 'Stop'
$exePath = (Resolve-Path $Exe).Path
$results = @()
for ($i = 1; $i -le $Runs; $i++) {
    $res = Join-Path $env:TEMP ("uas-sort-selftest-run{0}-{1}.json" -f $i, [guid]::NewGuid().ToString('N'))
    $p = Start-Process -FilePath $exePath -ArgumentList '--selftest', '--result', $res -PassThru
    $null = $p.Handle
    if (-not $p.WaitForExit(60000)) {
        Stop-Process -Id $p.Id -Force
        Write-Error "run ${i}: timed out after 60 s"
        exit 1
    }
    if (-not (Test-Path $res)) {
        Write-Error "run ${i}: no result file (exit $($p.ExitCode))"
        exit 1
    }
    $json = Get-Content $res -Raw | ConvertFrom-Json
    Remove-Item $res
    $failed = (@($json.checks) | Where-Object { $_.status -eq 'fail' } | ForEach-Object { "$($_.name): $($_.detail)" }) -join '; '
    $results += [pscustomobject]@{ Run = $i; Exit = $p.ExitCode; Ok = $json.ok; FirstFrameMs = $json.firstFrameMs; Checks = @($json.checks).Count; Failed = $failed }
}

$results | Format-Table -AutoSize | Out-String | Write-Host
$warm = $results[-1].FirstFrameMs
# Reported, never a failure (decision: 1000 ms is the gate; the 370 ms ReadyToRun baseline is informational).
Write-Host "slowerThanBaseline: $($warm -gt $BaselineMs)"
if ($results | Where-Object { $_.Exit -ne 0 -or -not $_.Ok -or $_.Failed }) {
    Write-Error 'selftest failed'
    exit 1
}
if ($MaxWarmFirstFrameMs -gt 0 -and $warm -gt $MaxWarmFirstFrameMs) {
    Write-Error "warm first frame $warm ms exceeds $MaxWarmFirstFrameMs ms"
    exit 1
}
Write-Host "OK: $Runs run(s), warm first frame $warm ms"
exit 0
