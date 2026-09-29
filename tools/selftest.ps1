# tools/selftest.ps1 — build the App and run one --selftest (Ref §13). Used by Part 11 tasks as their UI test step.
param(
    [string]$Only = '',
    [ValidateSet('Debug', 'Release')][string]$Configuration = 'Debug',
    [int]$TimeoutSec = 60
)
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$rid = 'win-x64'   # x64 only (user decision 2026-09-28)
& dotnet build (Join-Path $repo 'src\UasSort.App\UasSort.App.csproj') -c $Configuration -r $rid -tl:off | Out-Host
if ($LASTEXITCODE -ne 0) { Write-Host "BUILD FAILED"; exit $LASTEXITCODE }

$exe = Get-ChildItem (Join-Path $repo 'src\UasSort.App\bin') -Recurse -Filter 'uas-sort.exe' |
       Where-Object { $_.FullName -like "*\$Configuration\*" -and $_.FullName -like "*\$rid\*" } |
       Sort-Object LastWriteTimeUtc -Descending | Select-Object -First 1
if (-not $exe) { Write-Host "uas-sort.exe not found under src\UasSort.App\bin"; exit 2 }

$result = Join-Path ([IO.Path]::GetTempPath()) ("uas-sort-selftest-result-{0}.json" -f [guid]::NewGuid().ToString('N'))
$psi = [Diagnostics.ProcessStartInfo]::new($exe.FullName)
$psi.UseShellExecute = $false
foreach ($a in @('--selftest', '--result', $result)) { $psi.ArgumentList.Add($a) }
if ($Only) { $psi.ArgumentList.Add('--only'); $psi.ArgumentList.Add($Only) }
$p = [Diagnostics.Process]::Start($psi)
if (-not $p.WaitForExit($TimeoutSec * 1000)) {
    & taskkill.exe /T /F /PID $p.Id | Out-Null
    Write-Host "TIMEOUT after $TimeoutSec s"
    exit 124
}
if (Test-Path -LiteralPath $result) { Get-Content -LiteralPath $result -Raw | Write-Host; Remove-Item -LiteralPath $result }
else { Write-Host "NO RESULT FILE" }
Write-Host "exit code $($p.ExitCode)"
exit $p.ExitCode
