#Requires -Version 7.4
<#
.SYNOPSIS
  Native AOT publish of uas-sort for win-x64 (x64 only), the --selftest gate (a cold run plus three warm runs;
  the warm first frame is their median), install to %LOCALAPPDATA%\Programs\uas-sort\<version>\, a Start-menu shortcut, and keep the 2 newest versions.
.DESCRIPTION
  Ref §2.6, §13 (UI smoke test), §14 step 13. Never touches user data: settings, drafts, reports, logs and the
  ledger backups in %LOCALAPPDATA%\uas-sort\, and every library or ledger under the video/photo roots.
  ReadyToRun is never a build configuration here; an AOT failure is diagnosed by hand and raised with the user.
  The gate runs alone set UASSORT_PLACEHOLDER_CHECK=1 (ruling P11-C5), so the selftest's placeholderVisibility
  check lists this PC's configured video root (read-only); the previous value is restored afterwards.
.EXAMPLE
  pwsh -NoProfile -ExecutionPolicy Bypass -File tools\deploy.ps1 -Version 0.1.0
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidatePattern('^\d+\.\d+\.\d+$')][string]$Version,
    [switch]$AllowNoPlaceholders,
    [switch]$Force,
    [ValidateRange(10, 600)][int]$TimeoutSec = 60
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'Deploy.psm1') -Force

function Stop-Deploy([string]$Message) {
    Write-Host "DEPLOY FAILED: $Message" -ForegroundColor Red
    Write-Host 'Nothing was installed. If this is a Native AOT build/trim/AOT warning, a selftest failure or a warm first frame over 1 s,'
    Write-Host "follow 'If Native AOT fails a concrete check (stop and ask)' in docs/superpowers/plans/2026-09-27-uas-sort/13-aot-deploy.md."
    exit 1
}

$repo        = Split-Path -Parent $PSScriptRoot
$project     = Join-Path $repo 'src\UasSort.App\UasSort.App.csproj'
$rid         = Get-UasSortRid
$publishDir  = Join-Path $repo "artifacts\publish\$rid"
$summaryPath = Join-Path $repo "artifacts\deploy-$rid.json"
$installRoot = Join-Path $env:LOCALAPPDATA 'Programs\uas-sort'
$shortcut    = Join-Path ([Environment]::GetFolderPath([Environment+SpecialFolder]::Programs)) 'uas-sort.lnk'
$baselineMs  = 370    # the spike's trimmed ReadyToRun x64 warm first frame (Ref §2.2)
$maxWarmMs   = 1000   # success criterion 4 (Ref §1.5), against the MEDIAN of the warm runs
$gateRuns    = 4      # run 1 cold, runs 2-4 warm (branch-2 ruling: one noisy warm run must not decide the gate)

Assert-DeployTargetSafe -Path $installRoot
Assert-DeployTargetSafe -Path $shortcut

Write-Host "== 1/5 Native AOT publish ($rid, version $Version)"
if (Test-Path -LiteralPath $publishDir) { Remove-Item -LiteralPath $publishDir -Recurse -Force }
& dotnet publish $project -c Release -r $rid -o $publishDir "-p:Version=$Version"
if ($LASTEXITCODE -ne 0) { Stop-Deploy "dotnet publish exited with $LASTEXITCODE" }

Write-Host '== 2/5 Native AOT output check'
$output = Test-AotPublishOutput -PublishDir $publishDir -Rid $rid
if (-not $output.Ok) { Stop-Deploy ($output.Reasons -join '; ') }
Write-Host "uas-sort.exe is native $rid; the folder is $($output.SizeMB) MB (ReadyToRun baseline 87-97 MB)"

Write-Host '== 3/5 --selftest gate (a cold run plus three warm runs)'
$exe = Join-Path $publishDir 'uas-sort.exe'
$gateDir = Join-Path ([System.IO.Path]::GetTempPath()) ('uas-sort-deploy-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $gateDir | Out-Null
$previousPlaceholderCheck = $env:UASSORT_PLACEHOLDER_CHECK
$env:UASSORT_PLACEHOLDER_CHECK = '1'   # ruling P11-C5: only these gate runs list the real video root
try {
    $runs = foreach ($i in 1..$gateRuns) {
        $result = Join-Path $gateDir "selftest-result-$i.json"
        $p = Invoke-SelftestProcess -FilePath $exe -ArgumentList @('--selftest', '--result', $result) -TimeoutSec $TimeoutSec
        $exit = if ($null -eq $p.ExitCode) { -1 } else { $p.ExitCode }
        $run = Test-SelftestRun -Run $i -ExitCode $exit -TimedOut $p.TimedOut -ResultPath $result -AllowNoPlaceholders:$AllowNoPlaceholders
        $label = if ($i -eq 1) { 'cold' } else { 'warm' }
        Write-Host "run $i ($label): exit $exit, first frame $($run.FirstFrameMs) ms$(if (-not $run.Ok) { ' - ' + ($run.Reasons -join '; ') })"
        $run
    }
    $gate = Test-SelftestGate -Runs @($runs) -MaxWarmMs $maxWarmMs -BaselineMs $baselineMs
}
finally {
    $env:UASSORT_PLACEHOLDER_CHECK = $previousPlaceholderCheck
    Remove-Item -LiteralPath $gateDir -Recurse -Force -ErrorAction SilentlyContinue
}

if (-not $gate.Ok) { Stop-Deploy ('selftest gate: ' + ($gate.Reasons -join '; ')) }
Write-Host ("GATE PASS: warm first frame median $($gate.WarmMs) ms (samples $($gate.WarmSamplesMs -join ', ') ms; " +
            "limit $maxWarmMs ms; ReadyToRun baseline $baselineMs ms)")
if ($gate.SlowerThanBaseline) {
    # Decision 3 (00-interfaces.md): reported to the user, never a failure.
    Write-Host ("NOTE: the warm first frame median $($gate.WarmMs) ms is slower than the 0.37 s ReadyToRun baseline; " +
                'recorded as slowerThanBaseline and reported in the completion summary.')
}

Write-Host "== 4/5 Install to $(Join-Path $installRoot $Version)"
$installed = Copy-UasSortBuild -PublishDir $publishDir -InstallRoot $installRoot -Version $Version -Force:$Force

Write-Host '== 5/5 Start-menu shortcut and old versions (keeping 2)'
New-UasSortShortcut -ShortcutPath $shortcut -TargetPath (Join-Path $installed 'uas-sort.exe') -Description "uas-sort $Version"
foreach ($removed in @(Remove-OldVersions -InstallRoot $installRoot -Current $Version -Keep 2)) { Write-Host "removed $removed" }

$commit = (& git -C $repo rev-parse HEAD).Trim()
$dirty = [bool](& git -C $repo status --porcelain)
[ordered]@{
    version = $Version; rid = $rid; commit = $commit; dirty = $dirty; sizeMB = $output.SizeMB
    coldMs = $gate.ColdMs; warmMs = $gate.WarmMs; warmSamplesMs = $gate.WarmSamplesMs; maxWarmMs = $maxWarmMs; baselineMs = $baselineMs
    slowerThanBaseline = $gate.SlowerThanBaseline; installedTo = $installed
    atUtc = (Get-Date).ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ssZ')
} | ConvertTo-Json | Set-Content -LiteralPath $summaryPath -Encoding utf8

Write-Host "DEPLOYED uas-sort $Version ($rid) to $installed; summary in $summaryPath"
exit 0
