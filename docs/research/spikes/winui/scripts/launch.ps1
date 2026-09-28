param([string]$Exe, [string]$Tag, [int]$WaitSec = 10)
$T = 'C:\Users\damia\AppData\Local\Temp\uas-sort-spike-winui'
$log = Join-Path $T "logs\$Tag.log"
Remove-Item $log -ErrorAction SilentlyContinue
$env:UAS_SPIKE_LOG = $log
$env:DOTNET_BUNDLE_EXTRACT_BASE_DIR = Join-Path $T "tmp\extract"
Add-Type -ReferencedAssemblies System.Drawing -TypeDefinition @'
using System; using System.Runtime.InteropServices; using System.Drawing;
public static class W {
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr hdc, uint flags);
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
  [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
  public static string Shot(IntPtr h, string printPath, string screenPath) {
    RECT r; GetWindowRect(h, out r); int w = r.R - r.L, ht = r.B - r.T;
    using (var bmp = new Bitmap(w, ht)) using (var g = Graphics.FromImage(bmp)) {
      IntPtr hdc = g.GetHdc(); bool ok = PrintWindow(h, hdc, 2); g.ReleaseHdc(hdc); bmp.Save(printPath);
      using (var b2 = new Bitmap(w, ht)) using (var g2 = Graphics.FromImage(b2)) { g2.CopyFromScreen(r.L, r.T, 0, 0, new Size(w, ht)); b2.Save(screenPath); }
      return "rect=" + r.L + "," + r.T + " " + w + "x" + ht + " printWindowOk=" + ok;
    }
  }
}
'@
[W]::SetProcessDPIAware() | Out-Null
$sw = [Diagnostics.Stopwatch]::StartNew()
$p = Start-Process -FilePath $Exe -PassThru
"started pid=$($p.Id)"
$deadline = (Get-Date).AddSeconds($WaitSec)
while ((Get-Date) -lt $deadline) {
  Start-Sleep -Milliseconds 250
  if ($p.HasExited) { break }
  if ((Test-Path $log) -and (Select-String -Path $log -Pattern 'SELFTEST DONE' -Quiet)) { break }
}
"waited $([int]$sw.Elapsed.TotalMilliseconds) ms"
$p.Refresh()
if ($p.HasExited) { "PROCESS EXITED code=$($p.ExitCode)" } else {
  "alive: responding=$($p.Responding) ws=$([int]($p.WorkingSet64/1MB))MB hwnd=$($p.MainWindowHandle) title='$($p.MainWindowTitle)'"
  $kids = Get-CimInstance Win32_Process -Filter "ParentProcessId=$($p.Id)" | Select-Object -ExpandProperty Name
  "child processes: $($kids -join ', ')"
  if ($p.MainWindowHandle -ne 0) {
    [W]::SetForegroundWindow($p.MainWindowHandle) | Out-Null; Start-Sleep -Milliseconds 700
    [W]::Shot($p.MainWindowHandle, (Join-Path $T "logs\$Tag-printwindow.png"), (Join-Path $T "logs\$Tag-screen.png"))
  }
  Stop-Process -Id $p.Id -Force
  Start-Sleep -Milliseconds 1500
  "stopped; msedgewebview2 left: " + ((Get-CimInstance Win32_Process -Filter "Name='msedgewebview2.exe'" | ? { $_.CommandLine -like '*uas-sort-spike*' } | Measure-Object).Count)
}
"--- log ---"
if (Test-Path $log) { Get-Content $log } else { "(no log)" }
