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
