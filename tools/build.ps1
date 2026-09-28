#requires -Version 7.0
<#
.SYNOPSIS
  Builds uas-sort (no switch), or with -CheckBannedApi builds tests/UasSort.BannedApi.Probe (outside the solution)
  and checks Ref §2.4: every BannedSymbols.txt entry has exactly one probe call, and every probe call raises
  exactly one RS0030 (none elsewhere).
#>
[CmdletBinding()]
param([switch]$CheckBannedApi)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
Push-Location $repo
try {
    if (-not $CheckBannedApi) {
        dotnet build uas-sort.slnx
        exit $LASTEXITCODE
    }

    $banned = @(Get-Content (Join-Path $repo 'BannedSymbols.txt') |
        ForEach-Object { $_.Trim() } |
        Where-Object { $_ -and -not $_.StartsWith('//') } |
        ForEach-Object { ($_ -split ';', 2)[0].Trim() })

    $probeFile = Join-Path $repo 'tests/UasSort.BannedApi.Probe/Probe.cs'
    $probeLines = @(Get-Content $probeFile)
    $expected = @{}
    for ($i = 0; $i -lt $probeLines.Count; $i++) {
        if ($probeLines[$i] -match '//\s*probe:\s*(\S+)\s*$') { $expected[$i + 1] = $Matches[1] }
    }

    $failures = [System.Collections.Generic.List[string]]::new()
    $probedIds = @($expected.Values)
    foreach ($id in $banned) { if ($probedIds -cnotcontains $id) { $failures.Add("no probe call for $id") } }
    foreach ($id in $probedIds) { if ($banned -cnotcontains $id) { $failures.Add("probe marker $id is not in BannedSymbols.txt") } }
    $probedIds | Group-Object -CaseSensitive | Where-Object Count -gt 1 |
        ForEach-Object { $failures.Add("probe marker $($_.Name) appears $($_.Count) times") }

    $output = & dotnet build tests/UasSort.BannedApi.Probe/UasSort.BannedApi.Probe.csproj --no-incremental -tl:off `
        -clp:NoSummary -p:TreatWarningsAsErrors=false 2>&1 | Out-String
    if ($LASTEXITCODE -ne 0) { Write-Host $output; throw "probe build failed (exit $LASTEXITCODE)" }

    $hits = @([regex]::Matches($output, 'Probe\.cs\((\d+),(\d+)\): warning RS0030: [^\r\n]*') |
        ForEach-Object Value | Sort-Object -Unique)
    $byLine = @{}
    foreach ($h in $hits) {
        $line = [int]([regex]::Match($h, 'Probe\.cs\((\d+),').Groups[1].Value)
        $byLine[$line] = 1 + ($byLine[$line] ?? 0)
    }
    foreach ($line in $expected.Keys) {
        $n = $byLine[$line] ?? 0
        if ($n -ne 1) { $failures.Add("line ${line} ($($expected[$line])): expected 1 RS0030, got $n") }
    }
    foreach ($line in $byLine.Keys) {
        if (-not $expected.ContainsKey($line)) { $failures.Add("line ${line}: RS0030 on a line without a probe marker") }
    }

    if ($failures.Count -gt 0) {
        $failures | ForEach-Object { Write-Host "FAIL $_" }
        exit 1
    }
    Write-Host "OK: $($banned.Count) banned entries, $($expected.Count) probe calls, $($hits.Count) RS0030 (one per call)"
    exit 0
}
finally {
    Pop-Location
}
