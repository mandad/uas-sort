# tools/Deploy.psm1 — helpers for tools/deploy.ps1. Every function is covered by tools/deploy.tests.ps1.
# No Export-ModuleMember on purpose: every function below is exported, and later tasks append to this file.
Set-StrictMode -Version Latest

function Get-AppPublishConfig {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$ProjectPath,
        [Parameter(Mandatory)][ValidateSet('win-x64')][string]$Rid
    )
    $names = @('PublishAot', 'PublishReadyToRun', 'InvariantGlobalization', 'UseNls', 'SelfContained',
               'WindowsAppSDKSelfContained', 'WindowsPackageType', 'EnableMsixTooling', 'TreatWarningsAsErrors',
               'AssemblyName', 'RuntimeIdentifiers', 'NoWarn')
    # Evaluation only (no build): the properties exactly as a Release publish for $Rid sees them.
    $out = & dotnet msbuild $ProjectPath -nologo "-getProperty:$($names -join ',')" '-getItem:TrimmerRootAssembly' `
        '-p:Configuration=Release' "-p:RuntimeIdentifier=$Rid"
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet msbuild evaluation of '$ProjectPath' failed (exit $LASTEXITCODE): $($out -join ' ')"
    }
    $text = $out -join "`n"
    $start = $text.IndexOf('{')
    $end = $text.LastIndexOf('}')
    if ($start -lt 0 -or $end -le $start) { throw "dotnet msbuild printed no JSON: $text" }
    $json = $text.Substring($start, $end - $start + 1) | ConvertFrom-Json -AsHashtable
    $props = $json['Properties']
    $roots = @()
    if ($json.ContainsKey('Items') -and $json['Items'].ContainsKey('TrimmerRootAssembly')) {
        $roots = @($json['Items']['TrimmerRootAssembly'] | ForEach-Object { [string]$_['Identity'] })
    }
    $config = [ordered]@{}
    foreach ($n in $names) { $config[$n] = [string]$props[$n] }
    $config['TrimmerRootAssemblies'] = $roots
    $config['IlNoWarn'] = @(([string]$props['NoWarn']) -split '[;,\s]+' |
                            Where-Object { $_ -match '^IL\d{4}$' } | Sort-Object -Unique)
    [pscustomobject]$config
}

function Test-SelftestRun {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][int]$Run,
        [Parameter(Mandatory)][int]$ExitCode,
        [bool]$TimedOut = $false,
        [Parameter(Mandatory)][string]$ResultPath,
        [switch]$AllowNoPlaceholders
    )
    $reasons = [System.Collections.Generic.List[string]]::new()
    $firstFrameMs = $null
    if ($TimedOut) { $reasons.Add("run ${Run}: timed out") }
    elseif ($ExitCode -ne 0) { $reasons.Add("run ${Run}: exit code $ExitCode") }

    if (-not (Test-Path -LiteralPath $ResultPath -PathType Leaf)) {
        $reasons.Add("run ${Run}: no result file at '$ResultPath'")
    }
    else {
        $json = $null
        try { $json = Get-Content -LiteralPath $ResultPath -Raw | ConvertFrom-Json -AsHashtable }
        catch { $reasons.Add("run ${Run}: unreadable result file: $($_.Exception.Message)") }
        if ($null -ne $json) {
            if ($json['ok'] -ne $true) { $reasons.Add("run ${Run}: the result says ok = '$($json['ok'])'") }
            $ff = $json['firstFrameMs']
            if ($ff -is [long] -or $ff -is [int] -or $ff -is [double] -or $ff -is [decimal]) { $firstFrameMs = [double]$ff }
            else { $reasons.Add("run ${Run}: the result has no numeric firstFrameMs") }
            $checks = @($json['checks'] | Where-Object { $null -ne $_ })
            if ($checks.Count -eq 0) { $reasons.Add("run ${Run}: the result lists no checks") }
            foreach ($c in $checks) {
                switch ([string]$c['status']) {
                    'pass' { }
                    'fail' { $reasons.Add("run ${Run}: check '$($c['name'])' failed: $($c['detail'])") }
                    'notApplicable' {
                        if ([string]$c['name'] -ne 'placeholderVisibility') {
                            $reasons.Add("run ${Run}: check '$($c['name'])' is not applicable ($($c['detail'])); " +
                                         'only placeholderVisibility may be not applicable')
                        }
                        elseif (-not $AllowNoPlaceholders) {
                            $reasons.Add("run ${Run}: check '$($c['name'])' is not applicable ($($c['detail'])); " +
                                         'pass -AllowNoPlaceholders only if the video root really has no cloud-only files')
                        }
                    }
                    default { $reasons.Add("run ${Run}: check '$($c['name'])' has an unknown status '$($c['status'])'") }
                }
            }
        }
    }
    [pscustomobject]@{ Run = $Run; Ok = ($reasons.Count -eq 0); FirstFrameMs = $firstFrameMs; Reasons = $reasons.ToArray() }
}

function Test-SelftestGate {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][object[]]$Runs,
        [double]$MaxWarmMs = 1000,
        [double]$BaselineMs = 370
    )
    $reasons = [System.Collections.Generic.List[string]]::new()
    if ($Runs.Count -ne 2) { $reasons.Add("the gate needs exactly 2 runs, got $($Runs.Count)") }
    foreach ($r in $Runs) { foreach ($x in $r.Reasons) { $reasons.Add($x) } }
    $cold = $null
    $warm = $null
    if ($Runs.Count -ge 1) { $cold = $Runs[0].FirstFrameMs }
    if ($Runs.Count -ge 2) { $warm = $Runs[1].FirstFrameMs }
    if ($null -ne $warm -and $warm -gt $MaxWarmMs) {
        $reasons.Add("the warm first frame $warm ms exceeds $MaxWarmMs ms")
    }
    [pscustomobject]@{
        Ok                 = ($reasons.Count -eq 0)
        ColdMs             = $cold
        WarmMs             = $warm
        MaxWarmMs          = $MaxWarmMs
        BaselineMs         = $BaselineMs
        SlowerThanBaseline = ($null -ne $warm -and $warm -gt $BaselineMs)
        Reasons            = $reasons.ToArray()
    }
}

function Invoke-SelftestProcess {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$FilePath,
        [Parameter(Mandatory)][string[]]$ArgumentList,
        [Parameter(Mandatory)][int]$TimeoutSec
    )
    $psi = [System.Diagnostics.ProcessStartInfo]::new($FilePath)
    foreach ($a in $ArgumentList) { $psi.ArgumentList.Add($a) }   # proper per-argument quoting
    $psi.UseShellExecute = $false
    $psi.WorkingDirectory = Split-Path -Parent $FilePath
    $p = [System.Diagnostics.Process]::Start($psi)
    try {
        $id = $p.Id
        if ($p.WaitForExit($TimeoutSec * 1000)) {
            $p.WaitForExit()
            return [pscustomobject]@{ ExitCode = $p.ExitCode; TimedOut = $false; ProcessId = $id }
        }
        $p.Kill($true)   # the whole tree: the selftest's WebView2 processes too
        $p.WaitForExit()
        return [pscustomobject]@{ ExitCode = $null; TimedOut = $true; ProcessId = $id }
    }
    finally { $p.Dispose() }
}

function Get-UasSortRid {
    [CmdletBinding()]
    param()
    $arch = [System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture
    if ($arch -eq [System.Runtime.InteropServices.Architecture]::X64) { return 'win-x64' }
    throw "uas-sort is published for x64 only (user decision 2026-09-28); this machine is $arch"
}

function Get-PeMachine {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$Path)
    $buf = [byte[]]::new(4096)
    $fs = [System.IO.File]::OpenRead($Path)
    try { $n = $fs.Read($buf, 0, $buf.Length) } finally { $fs.Dispose() }
    if ($n -lt 64 -or $buf[0] -ne 0x4D -or $buf[1] -ne 0x5A) { throw "'$Path' is not a PE file (no MZ header)" }
    $pe = [System.BitConverter]::ToInt32($buf, 0x3C)
    if ($pe -lt 64 -or ($pe + 6) -gt $n -or $buf[$pe] -ne 0x50 -or $buf[$pe + 1] -ne 0x45 -or
        $buf[$pe + 2] -ne 0 -or $buf[$pe + 3] -ne 0) {
        throw "'$Path' is not a PE file (no PE signature)"
    }
    $machine = [int][System.BitConverter]::ToUInt16($buf, $pe + 4)
    if ($machine -eq 0x8664) { return 'x64' }
    if ($machine -eq 0x014C) { return 'x86' }
    'unknown'
}

function Get-FolderStats {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$Path)
    $files = @(Get-ChildItem -LiteralPath $Path -Recurse -File -Force)
    $bytes = [long]0
    foreach ($f in $files) { $bytes += $f.Length }
    [pscustomobject]@{ Files = $files.Count; Bytes = $bytes; MB = [math]::Round($bytes / 1MB, 1) }
}

function Test-AotPublishOutput {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$PublishDir,
        [Parameter(Mandatory)][ValidateSet('win-x64')][string]$Rid
    )
    $reasons = [System.Collections.Generic.List[string]]::new()
    $sizeMB = 0.0
    if (-not (Test-Path -LiteralPath $PublishDir -PathType Container)) {
        $reasons.Add("the publish folder '$PublishDir' does not exist")
    }
    else {
        $exe = Join-Path $PublishDir 'uas-sort.exe'
        if (-not (Test-Path -LiteralPath $exe -PathType Leaf)) { $reasons.Add("uas-sort.exe missing in '$PublishDir'") }
        else {
            $want = 'x64'
            $got = Get-PeMachine -Path $exe
            if ($got -ne $want) { $reasons.Add("uas-sort.exe is $got, expected $want for $Rid") }
        }
        foreach ($m in @(Get-ChildItem -LiteralPath $PublishDir -Filter 'UasSort.*.dll' -File)) {
            $reasons.Add("managed assembly $($m.Name) in the output: this is not a Native AOT publish")
        }
        if (@(Get-ChildItem -LiteralPath $PublishDir -Filter '*.pri' -File).Count -eq 0) {
            $reasons.Add('no .pri resource file in the output (EnableMsixTooling must stay true, else startup fails with 0xC000027B)')
        }
        $sizeMB = (Get-FolderStats -Path $PublishDir).MB
    }
    [pscustomobject]@{ Ok = ($reasons.Count -eq 0); Reasons = $reasons.ToArray(); SizeMB = $sizeMB }
}
