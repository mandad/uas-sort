. 'C:\Users\damia\AppData\Local\Temp\uas-sort-spike-winui\env.ps1'
$log = "$T\logs\dotnet-run.log"; Remove-Item $log -ErrorAction SilentlyContinue
$env:UAS_SPIKE_LOG = $log
$sw = [Diagnostics.Stopwatch]::StartNew()
$p = Start-Process dotnet -ArgumentList 'run','--project','UasSpike.App' -PassThru -NoNewWindow -RedirectStandardOutput "$T\logs\dotnet-run.out" -RedirectStandardError "$T\logs\dotnet-run.err"
while ($sw.Elapsed.TotalSeconds -lt 120) { Start-Sleep 1; if ((Test-Path $log) -and (Select-String -Path $log -Pattern 'SELFTEST DONE' -Quiet)) { break }; if ($p.HasExited) { break } }
"dotnet run: selftest reached after $([int]$sw.Elapsed.TotalSeconds)s; dotnet exited=$($p.HasExited)"
Get-Process UasSpike.App -ErrorAction SilentlyContinue | % { "app pid $($_.Id) path $($_.Path)"; Stop-Process -Id $_.Id -Force }
Start-Sleep 2; if (-not $p.HasExited) { Stop-Process -Id $p.Id -Force }
Get-Content "$T\logs\dotnet-run.out","$T\logs\dotnet-run.err" -ErrorAction SilentlyContinue | Select-Object -Last 5
if (Test-Path $log) { Get-Content $log | Select-String 'OnLaunched|NavigationCompleted|SELFTEST' }
