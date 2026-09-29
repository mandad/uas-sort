#Requires -Version 7.4
<#
.SYNOPSIS
  Plain test runner for tools/Deploy.psm1 and the App's publish configuration (no Pester needed).
.EXAMPLE
  pwsh -NoProfile -ExecutionPolicy Bypass -File tools\deploy.tests.ps1 -Name "Gate:*"
#>
[CmdletBinding()]
param([string]$Name = '*')

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$script:RepoRoot = Split-Path -Parent $PSScriptRoot
Import-Module (Join-Path $PSScriptRoot 'Deploy.psm1') -Force

$script:Cases = [System.Collections.Generic.List[object]]::new()

function Test-Case([string]$CaseName, [scriptblock]$Body) {
    $script:Cases.Add([pscustomobject]@{ Name = $CaseName; Body = $Body })
}

function Assert-True([bool]$Condition, [string]$Message) {
    if (-not $Condition) { throw "Assert-True failed: $Message" }
}

function Assert-Equal($Expected, $Actual, [string]$Message) {
    if ($Expected -ne $Actual) { throw "Assert-Equal failed: $Message (expected '$Expected', got '$Actual')" }
}

function Assert-Throws([scriptblock]$Body, [string]$Like, [string]$Message) {
    try { & $Body }
    catch {
        if ($_.Exception.Message -like $Like) { return }
        throw "Assert-Throws failed: $Message (threw '$($_.Exception.Message)', expected like '$Like')"
    }
    throw "Assert-Throws failed: $Message (nothing was thrown)"
}

function New-TestDir {
    $dir = Join-Path ([System.IO.Path]::GetTempPath()) ('uas-sort-test-' + [guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $dir | Out-Null
    $dir
}

Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'deploy-tests') -Filter '*.Tests.ps1' -File |
    Sort-Object Name |
    ForEach-Object { . $_.FullName }

$ran = 0
$failed = 0
foreach ($case in $script:Cases) {
    if ($case.Name -notlike $Name) { continue }
    $ran++
    try {
        & $case.Body
        Write-Host "PASS $($case.Name)"
    }
    catch {
        $failed++
        Write-Host "FAIL $($case.Name): $($_.Exception.Message)" -ForegroundColor Red
    }
}
Write-Host "$ran run, $failed failed"
if ($ran -eq 0) { Write-Host "no test case matched '$Name'" -ForegroundColor Red; exit 1 }
exit ([int]($failed -gt 0))
