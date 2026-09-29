# Ref §2.5: Release publishes are Native AOT (never ReadyToRun), MetadataExtractor and XmpCore are rooted,
# and the only IL warnings that may be silenced are IL2104/IL3053 (the rooted assemblies' class).

function Test-AppPublishConfig([string]$Rid) {
    $project = Join-Path $RepoRoot 'src\UasSort.App\UasSort.App.csproj'
    $c = Get-AppPublishConfig -ProjectPath $project -Rid $Rid
    Assert-Equal 'true' $c.PublishAot "PublishAot for Release $Rid"
    Assert-True ($c.PublishReadyToRun -ne 'true') "PublishReadyToRun must never be a build configuration ($Rid)"
    Assert-True ($c.InvariantGlobalization -ne 'true') 'InvariantGlobalization must never be set'
    Assert-True ($c.UseNls -ne 'true') 'UseNls must never be set'
    Assert-Equal 'true' $c.SelfContained 'SelfContained'
    Assert-Equal 'true' $c.WindowsAppSDKSelfContained 'WindowsAppSDKSelfContained'
    Assert-Equal 'None' $c.WindowsPackageType 'WindowsPackageType'
    Assert-Equal 'true' $c.EnableMsixTooling 'EnableMsixTooling (else 0xC000027B at startup)'
    Assert-Equal 'true' $c.TreatWarningsAsErrors 'TreatWarningsAsErrors'
    Assert-Equal 'uas-sort' $c.AssemblyName 'AssemblyName'
    Assert-Equal 'win-x64' $c.RuntimeIdentifiers 'RuntimeIdentifiers (x64 only)'
    Assert-True ($c.TrimmerRootAssemblies -contains 'MetadataExtractor') 'TrimmerRootAssembly MetadataExtractor'
    Assert-True ($c.TrimmerRootAssemblies -contains 'XmpCore') 'TrimmerRootAssembly XmpCore'
    $extra = @($c.IlNoWarn | Where-Object { $_ -notin @('IL2104', 'IL3053') })
    Assert-True ($extra.Count -eq 0) "only IL2104/IL3053 may be in NoWarn; found $($extra -join ', ')"
}

Test-Case 'PublishConfig: Release win-x64 is Native AOT with the rooted assemblies' { Test-AppPublishConfig 'win-x64' }
