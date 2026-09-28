param([string]$Exe, [int]$Runs = 3)
$T = 'C:\Users\damia\AppData\Local\Temp\uas-sort-spike-modernstack'
$env:DOTNET_BUNDLE_EXTRACT_BASE_DIR = "$T\tmp\bundle"
for ($i = 1; $i -le $Runs; $i++) {
  $out = "$T\tmp\probe-$i.txt"; Remove-Item $out -ErrorAction SilentlyContinue
  $env:SPIKE_PROBE_OUT = $out
  $sw = [Diagnostics.Stopwatch]::StartNew()
  $p = Start-Process -FilePath $Exe -PassThru
  if (-not $p.WaitForExit(20000)) { $p.Kill(); "run $i TIMEOUT (killed)" ; continue }
  $sw.Stop()
  $r = if (Test-Path $out) { Get-Content $out } else { "no probe file" }; if (Test-Path "$out.log") { $r += " LOG: " + ((Get-Content "$out.log") -join " / ") ; Remove-Item "$out.log" }
  "run $i exit=$($p.ExitCode) wall=$($sw.ElapsedMilliseconds)ms $r"
}
