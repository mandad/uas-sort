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
