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

function Test-PathUnder {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$Path, [Parameter(Mandatory)][string]$Root)
    $p = [System.IO.Path]::GetFullPath($Path).TrimEnd('\') + '\'
    $r = [System.IO.Path]::GetFullPath($Root).TrimEnd('\') + '\'
    $p.StartsWith($r, [System.StringComparison]::OrdinalIgnoreCase)
}

function Assert-DeployTargetSafe {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$Path)
    $full = [System.IO.Path]::GetFullPath($Path)
    # User data first: settings, drafts, reports, logs, ledger backups, and every synced library.
    $deny = @(Join-Path $env:LOCALAPPDATA 'uas-sort')
    foreach ($v in 'OneDrive', 'OneDriveConsumer', 'OneDriveCommercial') {
        $val = [Environment]::GetEnvironmentVariable($v)
        if ($val) { $deny += $val }
    }
    foreach ($d in $deny) {
        if (Test-PathUnder -Path $full -Root $d) {
            throw "Refusing '$full': it is under '$d' (user data; deploy never writes there)"
        }
    }
    $allow = @(
        (Join-Path $env:LOCALAPPDATA 'Programs'),
        [Environment]::GetFolderPath([Environment+SpecialFolder]::Programs),
        [System.IO.Path]::GetTempPath()
    )
    foreach ($a in $allow) {
        if (Test-PathUnder -Path $full -Root $a) { return }
    }
    throw "Refusing '$full': deploy writes only under %LOCALAPPDATA%\Programs, the Start-menu Programs folder or %TEMP%"
}

function Copy-UasSortBuild {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$PublishDir,
        [Parameter(Mandatory)][string]$InstallRoot,
        [Parameter(Mandatory)][string]$Version,
        [switch]$Force
    )
    Assert-DeployTargetSafe -Path $InstallRoot
    $dest = Join-Path $InstallRoot $Version
    if (Test-Path -LiteralPath $dest) {
        if (-not $Force) {
            throw "Version $Version is already installed at '$dest'; pass a higher -Version (or -Force to replace it)"
        }
        if (-not (Test-Path -LiteralPath (Join-Path $dest 'uas-sort.exe') -PathType Leaf)) {
            throw "Refusing to replace '$dest': it holds no uas-sort.exe"
        }
        Remove-Item -LiteralPath $dest -Recurse -Force
    }
    New-Item -ItemType Directory -Path $dest -Force | Out-Null
    Copy-Item -Path (Join-Path $PublishDir '*') -Destination $dest -Recurse -Force
    $a = Get-FolderStats -Path $PublishDir
    $b = Get-FolderStats -Path $dest
    if ($a.Files -ne $b.Files -or $a.Bytes -ne $b.Bytes) {
        throw "Copy check failed: '$PublishDir' has $($a.Files) files / $($a.Bytes) bytes, '$dest' has $($b.Files) / $($b.Bytes)"
    }
    $dest
}

function Get-VersionsToPrune {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$InstallRoot,
        [Parameter(Mandatory)][string]$Current,
        [ValidateRange(1, 100)][int]$Keep = 2
    )
    if (-not (Test-Path -LiteralPath $InstallRoot -PathType Container)) { return }
    $currentVersion = [version]$Current
    $others = foreach ($d in Get-ChildItem -LiteralPath $InstallRoot -Directory) {
        $v = $null
        if (-not [version]::TryParse($d.Name, [ref]$v)) { continue }
        if ($v -eq $currentVersion) { continue }
        if (-not (Test-Path -LiteralPath (Join-Path $d.FullName 'uas-sort.exe') -PathType Leaf)) { continue }
        [pscustomobject]@{ Version = $v; Path = $d.FullName }
    }
    @($others) | Where-Object { $null -ne $_ } | Sort-Object Version -Descending |
        Select-Object -Skip ($Keep - 1) | ForEach-Object { $_.Path }
}

function Remove-OldVersions {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$InstallRoot,
        [Parameter(Mandatory)][string]$Current,
        [ValidateRange(1, 100)][int]$Keep = 2
    )
    Assert-DeployTargetSafe -Path $InstallRoot
    foreach ($dir in @(Get-VersionsToPrune -InstallRoot $InstallRoot -Current $Current -Keep $Keep)) {
        try {
            Remove-Item -LiteralPath $dir -Recurse -Force
            $dir
        }
        catch { Write-Warning "Could not remove '$dir' (is that version running?): $($_.Exception.Message)" }
    }
}

function New-UasSortShortcut {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$ShortcutPath,
        [Parameter(Mandatory)][string]$TargetPath,
        [Parameter(Mandatory)][string]$Description
    )
    Assert-DeployTargetSafe -Path $ShortcutPath
    $shell = New-Object -ComObject WScript.Shell
    try {
        $lnk = $shell.CreateShortcut($ShortcutPath)
        $lnk.TargetPath = $TargetPath
        $lnk.WorkingDirectory = Split-Path -Parent $TargetPath
        $lnk.IconLocation = "$TargetPath,0"
        $lnk.Description = $Description
        $lnk.Save()
    }
    finally { [void][System.Runtime.InteropServices.Marshal]::ReleaseComObject($shell) }
}
