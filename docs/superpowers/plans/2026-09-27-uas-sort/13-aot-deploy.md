# Part 13 — Native AOT publish and deploy

**Goal:** prove that the finished app publishes as Native AOT for `win-x64` and `win-arm64` (the `win-arm64` publish is pending the user's ARM64 answer, see Task 13.6) with warnings as errors, ship `tools/deploy.ps1` (AOT publish for this machine's RID, the `--selftest` gate run twice, install to `%LOCALAPPDATA%\Programs\uas-sort\<version>\`, a Start-menu `.lnk`, keep 2 versions, never touch user data), then run the completion criteria of the single-pass build and document build, run and deploy in the README. The deploy logic lives in a small PowerShell module whose functions are tested by a plain `pwsh` test runner (no extra tools).

**Ref sections:** Ref §2.1 (Packaging row), §2.2 (Native AOT rather than ReadyToRun), §2.5 (App csproj: `PublishAot`, `TrimmerRootAssembly`, targeted `NoWarn`, ReadyToRun never a configuration), §2.6 (commands), §11 (binaries in `%LOCALAPPDATA%\Programs\uas-sort\<version>\`, Selftest row), §13 (UI smoke test: `--selftest`, `selftest-result.json`, `firstFrameMs`, 60 s timeout, two runs, `-AllowNoPlaceholders`), §14 step 13, Completion criteria, Maintenance after the build, "If Native AOT fails a concrete check"; §15 Q5 (publish for the machine's own RID). Main spec §2.2, §10 (`--selftest` row), §11.

**Depends on:** Parts 01–12 — Part 01 (solution skeleton, `global.json`, `Directory.Build.props`, the App csproj with Release `PublishAot=true` and `TrimmerRootAssembly`, the stack-proof AOT publish and its stop rule, the stack-proof record `docs/research/10-stack-proof.md`, `tools/build.ps1 -CheckBannedApi`, `tools/r.sh`), Part 11 (the full `--selftest` of `uas-sort.exe`, which honours the `--selftest --result <path>` contract below through Part 01's `LaunchOptions.Parse` (extended by Part 11) and `SelfTestResult(Ok, FirstFrameMs, Checks)`), Part 12 (the CLI and its `%TEMP%` run test `CliPlanRunTests`, part of the completion criteria). Names, signatures and owners follow `00-interfaces.md` (the cross-part registry); it wins over this part's text on those.

**Cross-part contract consumed here (defined here; Part 01's minimal selftest and Part 11's full selftest honour it; registry decision 2):**

- `uas-sort.exe --selftest --result <path> [--only <check>[,<check>…]]` writes `selftest-result.json` to exactly `<path>` and exits 0 (all checks pass or are not applicable) or 1. The flags are the `LaunchOptions` constants (`SelfTestFlag`, `ResultFlag`, `OnlyFlag`; namespace `UasSort.App`); without `--result` the file goes to `%TEMP%\uas-sort-selftest-result.json` (`LaunchOptions.DefaultResultFileName`). There is no `--selftest-result` flag. `deploy.ps1` never passes `--only` (the gate runs every check). Under `--selftest`, `Program.Main` skips `SingleInstanceGate.Claim`, so the two gate runs never collide with a running instance.
- The result file is UTF-8 JSON written from `SelfTestResult(bool Ok, double FirstFrameMs, IReadOnlyList<SelfTestCheck> Checks)` and `SelfTestCheck(string Name, string Status, string Detail)` through `SelfTestJsonContext` (camelCase; namespace `UasSort.App.SelfTest`), of this shape (no `v`/`kind` properties; extra properties are ignored):

```json
{ "ok": true,
  "firstFrameMs": 412,
  "checks": [ { "name": "templates", "status": "pass", "detail": "Anvil Mountain rendered" },
              { "name": "placeholderVisibility", "status": "notApplicable", "detail": "no cloud-only files under the video root" } ] }
```

- `status` is one of `pass`, `fail`, `notApplicable`. Only the placeholder-visibility check (`placeholderVisibility`, Ref §13) may report `notApplicable`; the gate then fails unless `-AllowNoPlaceholders` is passed, and it always fails when any other check reports `notApplicable`.
- Startup gate (registry decision 3): the warm (second) run's `firstFrameMs` ≤ 1000 ms is the hard gate. A warm first frame slower than the 370 ms ReadyToRun baseline sets `slowerThanBaseline`, which is recorded in `artifacts\deploy-<rid>.json` and reported to the user in the completion summary; it is not a failure and stops nothing.

**Test commands used in this part:**

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File tools\deploy.tests.ps1                    # every deploy check
pwsh -NoProfile -ExecutionPolicy Bypass -File tools\deploy.tests.ps1 -Name "Gate:*"      # one group (wildcard on the case name)
```

From WSL, run them through `tools/r.sh` (Part 01), then `dotnet build-server shutdown`.

---

### Task 13.1 — Deploy test runner and the publish-configuration check

**Files:**
- Create: `tools/deploy.tests.ps1`
- Create: `tools/deploy-tests/PublishConfig.Tests.ps1`
- Create: `tools/Deploy.psm1`

**Interfaces:**
- Consumes (Part 01): `src/UasSort.App/UasSort.App.csproj` with Release `PublishAot=true`, `<TrimmerRootAssembly Include="MetadataExtractor;XmpCore"/>`, `AssemblyName=uas-sort`, `RuntimeIdentifiers=win-x64;win-arm64`, `SelfContained=true`, `WindowsAppSDKSelfContained=true`, `WindowsPackageType=None`, `EnableMsixTooling=true`; `TreatWarningsAsErrors=true` from `Directory.Build.props`.
- Produces (defined here):
  - `tools/deploy.tests.ps1 [-Name <wildcard>]` — runs every `Test-Case` registered by `tools/deploy-tests/*.Tests.ps1`; prints `PASS`/`FAIL` per case and `N run, M failed`; exit 0 only if at least one case ran and none failed.
  - Test helpers in the runner's script scope: `Test-Case([string]$CaseName, [scriptblock]$Body)`, `Assert-True([bool]$Condition, [string]$Message)`, `Assert-Equal($Expected, $Actual, [string]$Message)`, `Assert-Throws([scriptblock]$Body, [string]$Like, [string]$Message)`, `New-TestDir` (returns a new `%TEMP%\uas-sort-test-<guid>` path), `$RepoRoot`.
  - `Get-AppPublishConfig -ProjectPath <string> -Rid <'win-x64'|'win-arm64'>` → `[pscustomobject]` with string properties `PublishAot`, `PublishReadyToRun`, `InvariantGlobalization`, `UseNls`, `SelfContained`, `WindowsAppSDKSelfContained`, `WindowsPackageType`, `EnableMsixTooling`, `TreatWarningsAsErrors`, `AssemblyName`, `RuntimeIdentifiers`, `NoWarn`, plus `TrimmerRootAssemblies` (string[]) and `IlNoWarn` (string[]: the `ILnnnn` codes in `NoWarn`).

- [ ] **Step 1: Write the runner and the failing publish-configuration test**

`tools/deploy.tests.ps1`:

```powershell
#Requires -Version 7.4
<#
.SYNOPSIS
  Plain test runner for tools/Deploy.psm1 and the App's publish configuration (no Pester needed).
.EXAMPLE
  pwsh -NoProfile -ExecutionPolicy Bypass -File tools\deploy.tests.ps1 -Name "Gate:*"
#>
[CmdletBinding()]
param([string]$Name = '*')

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$script:RepoRoot = Split-Path -Parent $PSScriptRoot
Import-Module (Join-Path $PSScriptRoot 'Deploy.psm1') -Force

$script:Cases = [System.Collections.Generic.List[object]]::new()

function Test-Case([string]$CaseName, [scriptblock]$Body) {
    $script:Cases.Add([pscustomobject]@{ Name = $CaseName; Body = $Body })
}

function Assert-True([bool]$Condition, [string]$Message) {
    if (-not $Condition) { throw "Assert-True failed: $Message" }
}

function Assert-Equal($Expected, $Actual, [string]$Message) {
    if ($Expected -ne $Actual) { throw "Assert-Equal failed: $Message (expected '$Expected', got '$Actual')" }
}

function Assert-Throws([scriptblock]$Body, [string]$Like, [string]$Message) {
    try { & $Body }
    catch {
        if ($_.Exception.Message -like $Like) { return }
        throw "Assert-Throws failed: $Message (threw '$($_.Exception.Message)', expected like '$Like')"
    }
    throw "Assert-Throws failed: $Message (nothing was thrown)"
}

function New-TestDir {
    $dir = Join-Path ([System.IO.Path]::GetTempPath()) ('uas-sort-test-' + [guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $dir | Out-Null
    $dir
}

Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'deploy-tests') -Filter '*.Tests.ps1' -File |
    Sort-Object Name |
    ForEach-Object { . $_.FullName }

$ran = 0
$failed = 0
foreach ($case in $script:Cases) {
    if ($case.Name -notlike $Name) { continue }
    $ran++
    try {
        & $case.Body
        Write-Host "PASS $($case.Name)"
    }
    catch {
        $failed++
        Write-Host "FAIL $($case.Name): $($_.Exception.Message)" -ForegroundColor Red
    }
}
Write-Host "$ran run, $failed failed"
if ($ran -eq 0) { Write-Host "no test case matched '$Name'" -ForegroundColor Red; exit 1 }
exit ([int]($failed -gt 0))
```

`tools/deploy-tests/PublishConfig.Tests.ps1`:

```powershell
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
    $rids = @($c.RuntimeIdentifiers -split ';' | Where-Object { $_ })
    Assert-True ($rids -contains 'win-x64' -and $rids -contains 'win-arm64') "RuntimeIdentifiers = '$($c.RuntimeIdentifiers)'"
    Assert-True ($c.TrimmerRootAssemblies -contains 'MetadataExtractor') 'TrimmerRootAssembly MetadataExtractor'
    Assert-True ($c.TrimmerRootAssemblies -contains 'XmpCore') 'TrimmerRootAssembly XmpCore'
    $extra = @($c.IlNoWarn | Where-Object { $_ -notin @('IL2104', 'IL3053') })
    Assert-True ($extra.Count -eq 0) "only IL2104/IL3053 may be in NoWarn; found $($extra -join ', ')"
}

Test-Case 'PublishConfig: Release win-x64 is Native AOT with the rooted assemblies' { Test-AppPublishConfig 'win-x64' }
Test-Case 'PublishConfig: Release win-arm64 is Native AOT with the rooted assemblies' { Test-AppPublishConfig 'win-arm64' }
```

- [ ] **Step 2: Run it and watch it fail**

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File tools\deploy.tests.ps1 -Name "PublishConfig:*"
```

Expected: the runner stops at `Import-Module` with `The specified module '...\tools\Deploy.psm1' was not loaded because no valid module file was found`, exit code 1.

- [ ] **Step 3: Implement `Get-AppPublishConfig`**

`tools/Deploy.psm1` (later tasks append functions below; there is deliberately no `Export-ModuleMember`, so every function is exported):

```powershell
# tools/Deploy.psm1 — helpers for tools/deploy.ps1. Every function is covered by tools/deploy.tests.ps1.
# No Export-ModuleMember on purpose: every function below is exported, and later tasks append to this file.
Set-StrictMode -Version Latest

function Get-AppPublishConfig {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$ProjectPath,
        [Parameter(Mandatory)][ValidateSet('win-x64', 'win-arm64')][string]$Rid
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
```

- [ ] **Step 4: Run it and watch it pass**

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File tools\deploy.tests.ps1 -Name "PublishConfig:*"
```

Expected:

```text
PASS PublishConfig: Release win-arm64 is Native AOT with the rooted assemblies
PASS PublishConfig: Release win-x64 is Native AOT with the rooted assemblies
2 run, 0 failed
```

(Cases run in registration order; the two lines may appear in either order.) If an assertion fails, the App csproj from Part 01 is wrong: correct `src/UasSort.App/UasSort.App.csproj` to the values of Ref §2.5 (the assertion message names the property), rebuild with `dotnet build uas-sort.slnx`, and re-run. A failure of the `NoWarn` assertion is an AOT/trim warning being hidden: remove the extra code from `NoWarn`; if that makes the publish fail, follow **If Native AOT fails a concrete check (stop and ask)** at the end of this part.

- [ ] **Step 5: Commit**

```powershell
git add tools/deploy.tests.ps1 tools/deploy-tests/PublishConfig.Tests.ps1 tools/Deploy.psm1
git commit -m "test: deploy test runner and Native AOT publish-configuration check

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_015Z1pwYTXofSxv2csCcXa4z"
```

---

### Task 13.2 — Selftest gate evaluation

**Files:**
- Create: `tools/deploy-tests/Gate.Tests.ps1`
- Modify: `tools/Deploy.psm1` (append)

**Interfaces:**
- Consumes: the selftest result contract at the top of this part.
- Produces (defined here):
  - `Test-SelftestRun -Run <int> -ExitCode <int> [-TimedOut <bool>] -ResultPath <string> [-AllowNoPlaceholders]` → `[pscustomobject]@{ Run; Ok [bool]; FirstFrameMs [double] or $null; Reasons [string[]] }`.
  - `Test-SelftestGate -Runs <object[]> [-MaxWarmMs <double> = 1000] [-BaselineMs <double> = 370]` → `[pscustomobject]@{ Ok; ColdMs; WarmMs; MaxWarmMs; BaselineMs; SlowerThanBaseline [bool]; Reasons [string[]] }`. `Ok` requires exactly two runs, both `Ok`, and the second (warm) run's `FirstFrameMs` ≤ `MaxWarmMs`. `SlowerThanBaseline` is `WarmMs > BaselineMs` and does not change `Ok`; it is recorded in the summary file and reported to the user in the completion summary (decision 3); it is not an AOT concrete-check failure. In `Test-SelftestRun`, `notApplicable` is accepted (with `-AllowNoPlaceholders`) only from the `placeholderVisibility` check; from any other check it is a failure.

- [ ] **Step 1: Write the failing tests**

`tools/deploy-tests/Gate.Tests.ps1`:

```powershell
# Ref §13 UI smoke test: two runs, each exit 0 with a result file; the warm (second) run's firstFrameMs ≤ 1000;
# "not applicable" placeholder visibility needs -AllowNoPlaceholders. Baseline: ReadyToRun 0.37 s (Ref §2.2).

function New-ResultFile([string]$Dir, [string]$FileName, $Content) {
    $path = Join-Path $Dir $FileName
    $Content | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $path -Encoding utf8
    $path
}

function New-PassingResult([double]$FirstFrameMs) {
    @{ ok = $true; firstFrameMs = $FirstFrameMs
       checks = @(@{ name = 'templates'; status = 'pass'; detail = 'Anvil Mountain rendered' },
                  @{ name = 'placeholderVisibility'; status = 'pass'; detail = '3 cloud-only entries' }) }
}

function New-Run([int]$Run, [bool]$Ok, $FirstFrameMs, [string[]]$Reasons = @()) {
    [pscustomobject]@{ Run = $Run; Ok = $Ok; FirstFrameMs = $FirstFrameMs; Reasons = $Reasons }
}

Test-Case 'Gate: a clean run passes and reports its first frame' {
    $dir = New-TestDir
    try {
        $path = New-ResultFile $dir 'r.json' (New-PassingResult 412)
        $r = Test-SelftestRun -Run 1 -ExitCode 0 -ResultPath $path
        Assert-True $r.Ok ($r.Reasons -join '; ')
        Assert-Equal 412 $r.FirstFrameMs 'FirstFrameMs'
    }
    finally { Remove-Item -LiteralPath $dir -Recurse -Force }
}

Test-Case 'Gate: a non-zero exit code fails' {
    $dir = New-TestDir
    try {
        $path = New-ResultFile $dir 'r.json' (New-PassingResult 412)
        $r = Test-SelftestRun -Run 1 -ExitCode 1 -ResultPath $path
        Assert-True (-not $r.Ok) 'exit 1 must fail'
        Assert-True (($r.Reasons -join ' ') -like '*exit code 1*') ($r.Reasons -join '; ')
    }
    finally { Remove-Item -LiteralPath $dir -Recurse -Force }
}

Test-Case 'Gate: a timed-out run fails' {
    $dir = New-TestDir
    try {
        $r = Test-SelftestRun -Run 2 -ExitCode -1 -TimedOut $true -ResultPath (Join-Path $dir 'missing.json')
        Assert-True (-not $r.Ok) 'timeout must fail'
        Assert-True (($r.Reasons -join ' ') -like '*timed out*') ($r.Reasons -join '; ')
    }
    finally { Remove-Item -LiteralPath $dir -Recurse -Force }
}

Test-Case 'Gate: a missing result file fails even with exit 0' {
    $dir = New-TestDir
    try {
        $r = Test-SelftestRun -Run 1 -ExitCode 0 -ResultPath (Join-Path $dir 'missing.json')
        Assert-True (-not $r.Ok) 'no result file must fail'
        Assert-True (($r.Reasons -join ' ') -like '*no result file*') ($r.Reasons -join '; ')
    }
    finally { Remove-Item -LiteralPath $dir -Recurse -Force }
}

Test-Case 'Gate: ok=false in the result fails even with exit 0' {
    $dir = New-TestDir
    try {
        $content = New-PassingResult 400
        $content.ok = $false
        $r = Test-SelftestRun -Run 1 -ExitCode 0 -ResultPath (New-ResultFile $dir 'r.json' $content)
        Assert-True (-not $r.Ok) 'ok=false must fail'
    }
    finally { Remove-Item -LiteralPath $dir -Recurse -Force }
}

Test-Case 'Gate: a failed check fails and is named' {
    $dir = New-TestDir
    try {
        $content = New-PassingResult 400
        $content.checks += @{ name = 'map'; status = 'fail'; detail = 'no pong within 20 s' }
        $r = Test-SelftestRun -Run 1 -ExitCode 0 -ResultPath (New-ResultFile $dir 'r.json' $content)
        Assert-True (-not $r.Ok) 'a failed check must fail'
        Assert-True (($r.Reasons -join ' ') -like "*'map' failed*no pong*") ($r.Reasons -join '; ')
    }
    finally { Remove-Item -LiteralPath $dir -Recurse -Force }
}

Test-Case 'Gate: a not-applicable placeholder check needs -AllowNoPlaceholders' {
    $dir = New-TestDir
    try {
        $content = New-PassingResult 400
        $content.checks[1].status = 'notApplicable'
        $path = New-ResultFile $dir 'r.json' $content
        $without = Test-SelftestRun -Run 1 -ExitCode 0 -ResultPath $path
        Assert-True (-not $without.Ok) 'notApplicable without the switch must fail'
        Assert-True (($without.Reasons -join ' ') -like '*-AllowNoPlaceholders*') ($without.Reasons -join '; ')
        $with = Test-SelftestRun -Run 1 -ExitCode 0 -ResultPath $path -AllowNoPlaceholders
        Assert-True $with.Ok ($with.Reasons -join '; ')
    }
    finally { Remove-Item -LiteralPath $dir -Recurse -Force }
}

Test-Case 'Gate: only placeholderVisibility may be not applicable, even with -AllowNoPlaceholders' {
    $dir = New-TestDir
    try {
        $content = New-PassingResult 400
        $content.checks[0].status = 'notApplicable'
        $path = New-ResultFile $dir 'r.json' $content
        $r = Test-SelftestRun -Run 1 -ExitCode 0 -ResultPath $path -AllowNoPlaceholders
        Assert-True (-not $r.Ok) 'a notApplicable templates check must fail'
        Assert-True (($r.Reasons -join ' ') -like "*'templates'*only placeholderVisibility*") ($r.Reasons -join '; ')
    }
    finally { Remove-Item -LiteralPath $dir -Recurse -Force }
}

Test-Case 'Gate: a result without firstFrameMs or checks fails' {
    $dir = New-TestDir
    try {
        $r = Test-SelftestRun -Run 1 -ExitCode 0 -ResultPath (New-ResultFile $dir 'r.json' @{ ok = $true })
        Assert-True (-not $r.Ok) 'missing firstFrameMs and checks must fail'
        $all = $r.Reasons -join ' '
        Assert-True ($all -like '*firstFrameMs*' -and $all -like '*no checks*') $all
    }
    finally { Remove-Item -LiteralPath $dir -Recurse -Force }
}

Test-Case 'Gate: the warm run passes at 1000 ms and fails at 1001 ms' {
    $at = Test-SelftestGate -Runs @((New-Run 1 $true 2400), (New-Run 2 $true 1000))
    Assert-True $at.Ok ($at.Reasons -join '; ')
    $over = Test-SelftestGate -Runs @((New-Run 1 $true 2400), (New-Run 2 $true 1001))
    Assert-True (-not $over.Ok) '1001 ms must fail the gate'
    Assert-True (($over.Reasons -join ' ') -like '*1001*1000*') ($over.Reasons -join '; ')
}

Test-Case 'Gate: only the second run counts as warm' {
    $g = Test-SelftestGate -Runs @((New-Run 1 $true 2500), (New-Run 2 $true 600))
    Assert-True $g.Ok ($g.Reasons -join '; ')
    Assert-Equal 2500 $g.ColdMs 'ColdMs'
    Assert-Equal 600 $g.WarmMs 'WarmMs'
}

Test-Case 'Gate: slower than the 0.37 s ReadyToRun baseline is flagged (reported, not a failure) and does not fail the gate' {
    $slow = Test-SelftestGate -Runs @((New-Run 1 $true 900), (New-Run 2 $true 371))
    Assert-True $slow.Ok 'the 1 s gate still passes'
    Assert-True $slow.SlowerThanBaseline '371 ms is slower than 370 ms'
    $even = Test-SelftestGate -Runs @((New-Run 1 $true 900), (New-Run 2 $true 370))
    Assert-True (-not $even.SlowerThanBaseline) '370 ms meets the baseline'
}

Test-Case 'Gate: a failed first run fails the gate and carries its reason' {
    $g = Test-SelftestGate -Runs @((New-Run 1 $false 300 @('run 1: exit code 1')), (New-Run 2 $true 300))
    Assert-True (-not $g.Ok) 'a failed run must fail the gate'
    Assert-True (($g.Reasons -join ' ') -like '*run 1: exit code 1*') ($g.Reasons -join '; ')
}

Test-Case 'Gate: exactly two runs are required' {
    $g = Test-SelftestGate -Runs @((New-Run 1 $true 300))
    Assert-True (-not $g.Ok) 'one run must fail the gate'
}
```

- [ ] **Step 2: Run them and watch them fail**

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File tools\deploy.tests.ps1 -Name "Gate:*"
```

Expected: every `Gate:` case prints `FAIL ... The term 'Test-SelftestRun' is not recognized` (or `'Test-SelftestGate'`), last line `14 run, 14 failed`, exit code 1.

- [ ] **Step 3: Implement the gate functions** (append to `tools/Deploy.psm1`)

```powershell
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
```

- [ ] **Step 4: Run them and watch them pass**

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File tools\deploy.tests.ps1 -Name "Gate:*"
```

Expected: 14 `PASS Gate: ...` lines, last line `14 run, 0 failed`, exit code 0.

- [ ] **Step 5: Commit**

```powershell
git add tools/deploy-tests/Gate.Tests.ps1 tools/Deploy.psm1
git commit -m "feat: selftest gate evaluation for deploy (two runs, warm first frame, baseline flag)

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_015Z1pwYTXofSxv2csCcXa4z"
```

---

### Task 13.3 — Selftest process runner, machine RID and AOT output check

**Files:**
- Create: `tools/deploy-tests/Process.Tests.ps1`
- Modify: `tools/Deploy.psm1` (append)

**Interfaces:**
- Produces (defined here):
  - `Invoke-SelftestProcess -FilePath <string> -ArgumentList <string[]> -TimeoutSec <int>` → `[pscustomobject]@{ ExitCode [int] or $null; TimedOut [bool]; ProcessId [int] }`. Arguments are passed through `ProcessStartInfo.ArgumentList` (quoted correctly, spaces included); on timeout the whole process tree (WebView2 children included) is killed.
  - `Get-UasSortRid` → `'win-x64'` or `'win-arm64'` from the OS architecture (throws otherwise).
  - `Get-PeMachine -Path <string>` → `'x64'`, `'arm64'`, `'x86'` or `'unknown'` from the PE header (reads 4 KB).
  - `Get-FolderStats -Path <string>` → `[pscustomobject]@{ Files [int]; Bytes [long]; MB [double] }` (recursive, hidden files included).
  - `Test-AotPublishOutput -PublishDir <string> -Rid <'win-x64'|'win-arm64'>` → `[pscustomobject]@{ Ok; Reasons [string[]]; SizeMB [double] }`: `uas-sort.exe` exists with the RID's PE machine, no managed `UasSort.*.dll` is in the folder (they are compiled into the exe under Native AOT), and at least one `*.pri` exists (Ref §2.5, `EnableMsixTooling`).

- [ ] **Step 1: Write the failing tests**

`tools/deploy-tests/Process.Tests.ps1`:

```powershell
function New-FakePe([string]$Path, [uint16]$Machine) {
    $b = [byte[]]::new(512)
    $b[0] = 0x4D; $b[1] = 0x5A                                   # MZ
    [System.BitConverter]::GetBytes([int]0x80).CopyTo($b, 0x3C)  # e_lfanew
    $b[0x80] = 0x50; $b[0x81] = 0x45                             # PE\0\0
    [System.BitConverter]::GetBytes($Machine).CopyTo($b, 0x84)
    [System.IO.File]::WriteAllBytes($Path, $b)
}

function New-FakeAotPublish([string]$Dir, [uint16]$Machine) {
    New-FakePe (Join-Path $Dir 'uas-sort.exe') $Machine
    Set-Content -LiteralPath (Join-Path $Dir 'resources.pri') -Value 'pri'
    Set-Content -LiteralPath (Join-Path $Dir 'Microsoft.ui.xaml.dll') -Value 'native'
}

$script:Pwsh = (Get-Process -Id $PID).Path

Test-Case 'Process: the exit code is returned' {
    $r = Invoke-SelftestProcess -FilePath $Pwsh -ArgumentList @('-NoProfile', '-NonInteractive', '-Command', 'exit 3') -TimeoutSec 60
    Assert-Equal 3 $r.ExitCode 'ExitCode'
    Assert-True (-not $r.TimedOut) 'TimedOut'
}

Test-Case 'Process: an argument with spaces arrives as one argument' {
    $dir = New-TestDir
    try {
        $spaced = Join-Path $dir 'with space'
        New-Item -ItemType Directory -Path $spaced | Out-Null
        $script = Join-Path $dir 'write-arg.ps1'
        Set-Content -LiteralPath $script -Value 'param([string]$Out) Set-Content -LiteralPath $Out -Value "ok"'
        $out = Join-Path $spaced 'result file.json'
        $r = Invoke-SelftestProcess -FilePath $Pwsh -ArgumentList @('-NoProfile', '-NonInteractive', '-File', $script, '-Out', $out) -TimeoutSec 60
        Assert-Equal 0 $r.ExitCode 'ExitCode'
        Assert-True (Test-Path -LiteralPath $out) "the child wrote '$out'"
    }
    finally { Remove-Item -LiteralPath $dir -Recurse -Force }
}

Test-Case 'Process: a hung process is killed at the timeout' {
    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    $r = Invoke-SelftestProcess -FilePath $Pwsh -ArgumentList @('-NoProfile', '-NonInteractive', '-Command', 'Start-Sleep -Seconds 120') -TimeoutSec 2
    $sw.Stop()
    Assert-True $r.TimedOut 'TimedOut'
    Assert-Equal $null $r.ExitCode 'ExitCode is null on timeout'
    Assert-True ($sw.Elapsed.TotalSeconds -lt 30) "returned after $($sw.Elapsed.TotalSeconds) s"
    Assert-Equal $null (Get-Process -Id $r.ProcessId -ErrorAction SilentlyContinue) 'the process is gone'
}

Test-Case 'Rid: follows the OS architecture' {
    $arch = [System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture
    $expected = if ($arch -eq [System.Runtime.InteropServices.Architecture]::Arm64) { 'win-arm64' } else { 'win-x64' }
    Assert-Equal $expected (Get-UasSortRid) 'Get-UasSortRid'
}

Test-Case 'Pe: the machine type comes from the PE header' {
    $dir = New-TestDir
    try {
        New-FakePe (Join-Path $dir 'x.exe') 0x8664
        New-FakePe (Join-Path $dir 'a.exe') 0xAA64
        Set-Content -LiteralPath (Join-Path $dir 'text.exe') -Value 'not a PE file at all, just some text'
        Assert-Equal 'x64' (Get-PeMachine -Path (Join-Path $dir 'x.exe')) 'x64'
        Assert-Equal 'arm64' (Get-PeMachine -Path (Join-Path $dir 'a.exe')) 'arm64'
        Assert-Throws { Get-PeMachine -Path (Join-Path $dir 'text.exe') } '*not a PE file*' 'text file'
    }
    finally { Remove-Item -LiteralPath $dir -Recurse -Force }
}

Test-Case 'AotOutput: a native x64 publish passes' {
    $dir = New-TestDir
    try {
        New-FakeAotPublish $dir 0x8664
        $r = Test-AotPublishOutput -PublishDir $dir -Rid win-x64
        Assert-True $r.Ok ($r.Reasons -join '; ')
    }
    finally { Remove-Item -LiteralPath $dir -Recurse -Force }
}

Test-Case 'AotOutput: a managed UasSort assembly means it is not a Native AOT publish' {
    $dir = New-TestDir
    try {
        New-FakeAotPublish $dir 0x8664
        Set-Content -LiteralPath (Join-Path $dir 'UasSort.Core.dll') -Value 'il'
        $r = Test-AotPublishOutput -PublishDir $dir -Rid win-x64
        Assert-True (-not $r.Ok) 'UasSort.Core.dll must fail'
        Assert-True (($r.Reasons -join ' ') -like '*UasSort.Core.dll*not a Native AOT publish*') ($r.Reasons -join '; ')
    }
    finally { Remove-Item -LiteralPath $dir -Recurse -Force }
}

Test-Case 'AotOutput: the wrong architecture fails' {
    $dir = New-TestDir
    try {
        New-FakeAotPublish $dir 0x8664
        $r = Test-AotPublishOutput -PublishDir $dir -Rid win-arm64
        Assert-True (-not $r.Ok) 'an x64 exe is not a win-arm64 publish'
        Assert-True (($r.Reasons -join ' ') -like '*is x64, expected arm64*') ($r.Reasons -join '; ')
    }
    finally { Remove-Item -LiteralPath $dir -Recurse -Force }
}

Test-Case 'AotOutput: a missing exe or pri fails' {
    $dir = New-TestDir
    try {
        $r = Test-AotPublishOutput -PublishDir $dir -Rid win-x64
        Assert-True (-not $r.Ok) 'an empty folder must fail'
        $all = $r.Reasons -join ' '
        Assert-True ($all -like '*uas-sort.exe missing*' -and $all -like '*no .pri*') $all
    }
    finally { Remove-Item -LiteralPath $dir -Recurse -Force }
}

Test-Case 'FolderStats: counts files and bytes recursively, hidden files included' {
    $dir = New-TestDir
    try {
        New-Item -ItemType Directory -Path (Join-Path $dir 'sub') | Out-Null
        [System.IO.File]::WriteAllBytes((Join-Path $dir 'a.bin'), [byte[]]::new(1000))
        [System.IO.File]::WriteAllBytes((Join-Path $dir 'sub\b.bin'), [byte[]]::new(24))
        (Get-Item -LiteralPath (Join-Path $dir 'sub\b.bin')).Attributes = 'Hidden'
        $s = Get-FolderStats -Path $dir
        Assert-Equal 2 $s.Files 'Files'
        Assert-Equal 1024 $s.Bytes 'Bytes'
    }
    finally { Remove-Item -LiteralPath $dir -Recurse -Force }
}
```

- [ ] **Step 2: Run them and watch them fail**

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File tools\deploy.tests.ps1 -Name "Process:*"
pwsh -NoProfile -ExecutionPolicy Bypass -File tools\deploy.tests.ps1 -Name "*Output:*"
```

Expected: each case prints `FAIL ... is not recognized as a name of a cmdlet, function ...` (`Invoke-SelftestProcess`, `Test-AotPublishOutput`); the summaries end `3 run, 3 failed` and `4 run, 4 failed`, exit code 1.

- [ ] **Step 3: Implement the functions** (append to `tools/Deploy.psm1`)

```powershell
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
    if ($arch -eq [System.Runtime.InteropServices.Architecture]::Arm64) { return 'win-arm64' }
    throw "uas-sort is published for x64 and ARM64 only; this machine is $arch"
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
    if ($machine -eq 0xAA64) { return 'arm64' }
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
        [Parameter(Mandatory)][ValidateSet('win-x64', 'win-arm64')][string]$Rid
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
            $want = if ($Rid -eq 'win-x64') { 'x64' } else { 'arm64' }
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
```

- [ ] **Step 4: Run them and watch them pass**

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File tools\deploy.tests.ps1
```

Expected: every case `PASS`, last line `26 run, 0 failed` (2 PublishConfig + 14 Gate + 10 from this task), exit code 0.

- [ ] **Step 5: Commit**

```powershell
git add tools/deploy-tests/Process.Tests.ps1 tools/Deploy.psm1
git commit -m "feat: selftest process runner, machine RID and Native AOT output check for deploy

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_015Z1pwYTXofSxv2csCcXa4z"
```

---

### Task 13.4 — Install helpers: safe targets, versioned copy, keep 2 versions, Start-menu shortcut

**Files:**
- Create: `tools/deploy-tests/Install.Tests.ps1`
- Modify: `tools/Deploy.psm1` (append)

**Interfaces:**
- Consumes: `Get-FolderStats` (Task 13.3).
- Produces (defined here):
  - `Test-PathUnder -Path <string> -Root <string>` → `[bool]` (equal or below, case-insensitive, full paths).
  - `Assert-DeployTargetSafe -Path <string>` → throws unless the path is under `%LOCALAPPDATA%\Programs`, the Start-menu Programs folder or `%TEMP%`, and never under `%LOCALAPPDATA%\uas-sort` or any `%OneDrive%` / `%OneDriveConsumer%` / `%OneDriveCommercial%` folder (so a video root, photo root, ledger or settings folder can never be a deploy target).
  - `Copy-UasSortBuild -PublishDir <string> -InstallRoot <string> -Version <string> [-Force]` → the installed version folder path. Refuses an existing version folder unless `-Force`, and `-Force` replaces only a folder that holds `uas-sort.exe`. Verifies file count and byte total after the copy.
  - `Get-VersionsToPrune -InstallRoot <string> -Current <string> [-Keep <int> = 2]` → the full paths to delete: version-named folders (parsable as `System.Version`) that hold `uas-sort.exe`, other than `Current`, beyond the newest `Keep − 1` of them. Anything else in the install root is never returned.
  - `Remove-OldVersions -InstallRoot <string> -Current <string> [-Keep <int> = 2]` → the removed paths; a folder that can't be removed (a running version) is a warning, not a failure.
  - `New-UasSortShortcut -ShortcutPath <string> -TargetPath <string> -Description <string>` → creates or replaces the `.lnk` through the `WScript.Shell` COM object (working directory = the target's folder, icon = the exe).

- [ ] **Step 1: Write the failing tests**

`tools/deploy-tests/Install.Tests.ps1`:

```powershell
function New-FakeVersion([string]$Root, [string]$Name, [bool]$WithExe = $true) {
    $d = Join-Path $Root $Name
    New-Item -ItemType Directory -Path $d -Force | Out-Null
    if ($WithExe) { Set-Content -LiteralPath (Join-Path $d 'uas-sort.exe') -Value 'exe' }
    $d
}

Test-Case 'Safe: refuses the per-PC data folder %LOCALAPPDATA%\uas-sort and anything below it' {
    Assert-Throws { Assert-DeployTargetSafe -Path (Join-Path $env:LOCALAPPDATA 'uas-sort') } '*user data*' 'the folder itself'
    Assert-Throws { Assert-DeployTargetSafe -Path (Join-Path $env:LOCALAPPDATA 'uas-sort\settings.json') } '*user data*' 'settings.json'
}

Test-Case 'Safe: refuses a OneDrive folder even inside an allowed root' {
    $dir = New-TestDir
    $saved = $env:OneDrive
    try {
        $env:OneDrive = Join-Path $dir 'OneDrive'
        Assert-Throws { Assert-DeployTargetSafe -Path (Join-Path $dir 'OneDrive\Pictures\UAS Videos') } '*user data*' 'OneDrive'
    }
    finally {
        $env:OneDrive = $saved
        Remove-Item -LiteralPath $dir -Recurse -Force
    }
}

Test-Case 'Safe: refuses anything outside the allow list' {
    Assert-Throws { Assert-DeployTargetSafe -Path 'D:\uas-sort' } '*deploy writes only under*' 'D:\'
    Assert-Throws { Assert-DeployTargetSafe -Path 'C:\uas-sort' } '*deploy writes only under*' 'C:\uas-sort'
}

Test-Case 'Safe: allows Programs, the Start menu and %TEMP%' {
    Assert-DeployTargetSafe -Path (Join-Path $env:LOCALAPPDATA 'Programs\uas-sort')
    Assert-DeployTargetSafe -Path (Join-Path ([Environment]::GetFolderPath('Programs')) 'uas-sort.lnk')
    Assert-DeployTargetSafe -Path (Join-Path ([System.IO.Path]::GetTempPath()) 'uas-sort-test-x\Programs\uas-sort')
}

Test-Case 'Copy: copies every file into the version folder and verifies it' {
    $dir = New-TestDir
    try {
        $pub = Join-Path $dir 'publish'
        New-Item -ItemType Directory -Path (Join-Path $pub 'MapAssets') -Force | Out-Null
        Set-Content -LiteralPath (Join-Path $pub 'uas-sort.exe') -Value 'exe'
        Set-Content -LiteralPath (Join-Path $pub 'MapAssets\map.js') -Value 'js'
        $root = Join-Path $dir 'Programs\uas-sort'
        $dest = Copy-UasSortBuild -PublishDir $pub -InstallRoot $root -Version '0.1.0'
        Assert-Equal (Join-Path $root '0.1.0') $dest 'destination'
        Assert-True (Test-Path -LiteralPath (Join-Path $dest 'MapAssets\map.js')) 'nested file copied'
        Assert-Equal (Get-FolderStats $pub).Bytes (Get-FolderStats $dest).Bytes 'bytes'
    }
    finally { Remove-Item -LiteralPath $dir -Recurse -Force }
}

Test-Case 'Copy: an installed version is refused without -Force and replaced with it' {
    $dir = New-TestDir
    try {
        $pub = Join-Path $dir 'publish'
        New-Item -ItemType Directory -Path $pub | Out-Null
        Set-Content -LiteralPath (Join-Path $pub 'uas-sort.exe') -Value 'new'
        $root = Join-Path $dir 'Programs\uas-sort'
        $old = New-FakeVersion $root '0.1.0'
        Set-Content -LiteralPath (Join-Path $old 'stale.txt') -Value 'old'
        Assert-Throws { Copy-UasSortBuild -PublishDir $pub -InstallRoot $root -Version '0.1.0' } '*already installed*' 'no -Force'
        Copy-UasSortBuild -PublishDir $pub -InstallRoot $root -Version '0.1.0' -Force | Out-Null
        Assert-True (-not (Test-Path -LiteralPath (Join-Path $old 'stale.txt'))) 'the old folder was replaced'
        Assert-Equal 'new' (Get-Content -LiteralPath (Join-Path $old 'uas-sort.exe')) 'new exe'
    }
    finally { Remove-Item -LiteralPath $dir -Recurse -Force }
}

Test-Case 'Copy: -Force never replaces a folder without uas-sort.exe' {
    $dir = New-TestDir
    try {
        $pub = Join-Path $dir 'publish'
        New-Item -ItemType Directory -Path $pub | Out-Null
        Set-Content -LiteralPath (Join-Path $pub 'uas-sort.exe') -Value 'new'
        $root = Join-Path $dir 'Programs\uas-sort'
        $foreign = New-FakeVersion $root '0.1.0' $false
        Set-Content -LiteralPath (Join-Path $foreign 'keep.txt') -Value 'keep'
        Assert-Throws { Copy-UasSortBuild -PublishDir $pub -InstallRoot $root -Version '0.1.0' -Force } '*holds no uas-sort.exe*' 'foreign folder'
        Assert-True (Test-Path -LiteralPath (Join-Path $foreign 'keep.txt')) 'untouched'
    }
    finally { Remove-Item -LiteralPath $dir -Recurse -Force }
}

Test-Case 'Prune: keeps the current version and the newest other one' {
    $dir = New-TestDir
    try {
        $root = Join-Path $dir 'Programs\uas-sort'
        '0.1.0', '0.1.1', '0.2.0', '0.10.0' | ForEach-Object { New-FakeVersion $root $_ | Out-Null }
        $prune = @(Get-VersionsToPrune -InstallRoot $root -Current '0.10.0')
        Assert-Equal 2 $prune.Count "pruned: $($prune -join ', ')"
        Assert-True ($prune -contains (Join-Path $root '0.1.0') -and $prune -contains (Join-Path $root '0.1.1')) ($prune -join ', ')
    }
    finally { Remove-Item -LiteralPath $dir -Recurse -Force }
}

Test-Case 'Prune: never returns non-version folders or folders without uas-sort.exe' {
    $dir = New-TestDir
    try {
        $root = Join-Path $dir 'Programs\uas-sort'
        '0.1.0', '0.2.0', '0.3.0' | ForEach-Object { New-FakeVersion $root $_ | Out-Null }
        New-FakeVersion $root 'notes' | Out-Null
        New-FakeVersion $root '0.0.9' $false | Out-Null
        $prune = @(Get-VersionsToPrune -InstallRoot $root -Current '0.3.0')
        Assert-Equal 1 $prune.Count "pruned: $($prune -join ', ')"
        Assert-Equal (Join-Path $root '0.1.0') $prune[0] 'only 0.1.0'
    }
    finally { Remove-Item -LiteralPath $dir -Recurse -Force }
}

Test-Case 'Prune: a lower current version keeps itself and the newest other one' {
    $dir = New-TestDir
    try {
        $root = Join-Path $dir 'Programs\uas-sort'
        '0.1.0', '0.1.1', '0.2.0' | ForEach-Object { New-FakeVersion $root $_ | Out-Null }
        $prune = @(Get-VersionsToPrune -InstallRoot $root -Current '0.1.0')
        Assert-Equal 1 $prune.Count "pruned: $($prune -join ', ')"
        Assert-Equal (Join-Path $root '0.1.1') $prune[0] 'only 0.1.1'
        $removed = @(Remove-OldVersions -InstallRoot $root -Current '0.1.0')
        Assert-Equal 1 $removed.Count 'removed one'
        Assert-True ((Test-Path (Join-Path $root '0.1.0')) -and (Test-Path (Join-Path $root '0.2.0'))) 'kept 0.1.0 and 0.2.0'
    }
    finally { Remove-Item -LiteralPath $dir -Recurse -Force }
}

Test-Case 'Prune: a missing install root prunes nothing' {
    $dir = New-TestDir
    try {
        $prune = @(Get-VersionsToPrune -InstallRoot (Join-Path $dir 'nothing-here') -Current '0.1.0')
        Assert-Equal 0 $prune.Count 'nothing'
    }
    finally { Remove-Item -LiteralPath $dir -Recurse -Force }
}

Test-Case 'Shortcut: created through WScript.Shell with target, working directory and icon' {
    $dir = New-TestDir
    try {
        $exe = New-FakeVersion $dir '0.1.0'
        $exe = Join-Path $exe 'uas-sort.exe'
        $lnk = Join-Path $dir 'uas-sort.lnk'
        New-UasSortShortcut -ShortcutPath $lnk -TargetPath $exe -Description 'uas-sort 0.1.0'
        Assert-True (Test-Path -LiteralPath $lnk) 'the .lnk exists'
        $shell = New-Object -ComObject WScript.Shell
        try {
            $read = $shell.CreateShortcut($lnk)
            Assert-Equal $exe $read.TargetPath 'TargetPath'
            Assert-Equal (Split-Path -Parent $exe) $read.WorkingDirectory 'WorkingDirectory'
            Assert-Equal 'uas-sort 0.1.0' $read.Description 'Description'
        }
        finally { [void][System.Runtime.InteropServices.Marshal]::ReleaseComObject($shell) }
    }
    finally { Remove-Item -LiteralPath $dir -Recurse -Force }
}
```

- [ ] **Step 2: Run them and watch them fail**

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File tools\deploy.tests.ps1 -Name "Safe:*"
pwsh -NoProfile -ExecutionPolicy Bypass -File tools\deploy.tests.ps1 -Name "Prune:*"
```

Expected: every case prints `FAIL ... is not recognized` (`Assert-DeployTargetSafe`, `Get-VersionsToPrune`); summaries `4 run, 4 failed` and `4 run, 4 failed`, exit code 1.

- [ ] **Step 3: Implement the install helpers** (append to `tools/Deploy.psm1`)

```powershell
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
```

- [ ] **Step 4: Run the whole runner and watch it pass**

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File tools\deploy.tests.ps1
```

Expected: every case `PASS`, last line `38 run, 0 failed`, exit code 0.

- [ ] **Step 5: Commit**

```powershell
git add tools/deploy-tests/Install.Tests.ps1 tools/Deploy.psm1
git commit -m "feat: deploy install helpers (safe targets, versioned copy, keep 2 versions, Start-menu shortcut)

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_015Z1pwYTXofSxv2csCcXa4z"
```

---

### Task 13.5 — `tools/deploy.ps1` and the x64 selftest gate

**Files:**
- Create: `tools/deploy.ps1`

**Interfaces:**
- Consumes: every function of `tools/Deploy.psm1` (Tasks 13.1–13.4); `uas-sort.exe --selftest --result <path>` (contract at the top of this part; `LaunchOptions` of Part 01, extended by Part 11; the full selftest of Part 11).
- Produces (defined here): `pwsh -NoProfile -ExecutionPolicy Bypass -File tools\deploy.ps1 -Version <major.minor.patch> [-AllowNoPlaceholders] [-Force] [-TimeoutSec <int> = 60]`. Exit 0 = deployed; exit 1 = nothing installed (publish, output check or gate failed). Writes only: `artifacts\publish\<rid>\` and `artifacts\deploy-<rid>.json` in the repo, a `%TEMP%\uas-sort-deploy-<guid>\` gate folder (deleted), `%LOCALAPPDATA%\Programs\uas-sort\<version>\`, and `<Start-menu Programs>\uas-sort.lnk`; removes only old version folders there. The summary file `artifacts\deploy-<rid>.json` holds `version`, `rid`, `commit`, `dirty`, `sizeMB`, `coldMs`, `warmMs`, `maxWarmMs`, `baselineMs`, `slowerThanBaseline`, `installedTo`, `atUtc`.

- [ ] **Step 1: Confirm the gate is missing (failing check)**

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File tools\deploy.ps1 -Version 0.1.0
```

Expected: `The argument 'tools\deploy.ps1' is not recognized as the name of a script file.` and a non-zero exit code (pwsh returns 64 for a missing `-File` script).

- [ ] **Step 2: Write `tools/deploy.ps1`**

```powershell
#Requires -Version 7.4
<#
.SYNOPSIS
  Native AOT publish of uas-sort for this machine's RID, the --selftest gate (two runs), install to
  %LOCALAPPDATA%\Programs\uas-sort\<version>\, a Start-menu shortcut, and keep the 2 newest versions.
.DESCRIPTION
  Ref §2.6, §13 (UI smoke test), §14 step 13. Never touches user data: settings, drafts, reports, logs and the
  ledger backups in %LOCALAPPDATA%\uas-sort\, and every library or ledger under the video/photo roots.
  ReadyToRun is never a build configuration here; an AOT failure is diagnosed by hand and raised with the user.
.EXAMPLE
  pwsh -NoProfile -ExecutionPolicy Bypass -File tools\deploy.ps1 -Version 0.1.0
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidatePattern('^\d+\.\d+\.\d+$')][string]$Version,
    [switch]$AllowNoPlaceholders,
    [switch]$Force,
    [ValidateRange(10, 600)][int]$TimeoutSec = 60
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'Deploy.psm1') -Force

function Stop-Deploy([string]$Message) {
    Write-Host "DEPLOY FAILED: $Message" -ForegroundColor Red
    Write-Host 'Nothing was installed. If this is a Native AOT build/trim/AOT warning, a selftest failure or a warm first frame over 1 s,'
    Write-Host "follow 'If Native AOT fails a concrete check (stop and ask)' in docs/superpowers/plans/2026-09-27-uas-sort/13-aot-deploy.md."
    exit 1
}

$repo        = Split-Path -Parent $PSScriptRoot
$project     = Join-Path $repo 'src\UasSort.App\UasSort.App.csproj'
$rid         = Get-UasSortRid
$publishDir  = Join-Path $repo "artifacts\publish\$rid"
$summaryPath = Join-Path $repo "artifacts\deploy-$rid.json"
$installRoot = Join-Path $env:LOCALAPPDATA 'Programs\uas-sort'
$shortcut    = Join-Path ([Environment]::GetFolderPath([Environment+SpecialFolder]::Programs)) 'uas-sort.lnk'
$baselineMs  = 370    # the spike's trimmed ReadyToRun x64 warm first frame (Ref §2.2)
$maxWarmMs   = 1000   # success criterion 4 (Ref §1.5)

Assert-DeployTargetSafe -Path $installRoot
Assert-DeployTargetSafe -Path $shortcut

Write-Host "== 1/5 Native AOT publish ($rid, version $Version)"
if (Test-Path -LiteralPath $publishDir) { Remove-Item -LiteralPath $publishDir -Recurse -Force }
& dotnet publish $project -c Release -r $rid -o $publishDir "-p:Version=$Version"
if ($LASTEXITCODE -ne 0) { Stop-Deploy "dotnet publish exited with $LASTEXITCODE" }

Write-Host '== 2/5 Native AOT output check'
$output = Test-AotPublishOutput -PublishDir $publishDir -Rid $rid
if (-not $output.Ok) { Stop-Deploy ($output.Reasons -join '; ') }
Write-Host "uas-sort.exe is native $rid; the folder is $($output.SizeMB) MB (ReadyToRun baseline 87-97 MB)"

Write-Host '== 3/5 --selftest gate (two runs)'
$exe = Join-Path $publishDir 'uas-sort.exe'
$gateDir = Join-Path ([System.IO.Path]::GetTempPath()) ('uas-sort-deploy-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $gateDir | Out-Null
try {
    $runs = foreach ($i in 1, 2) {
        $result = Join-Path $gateDir "selftest-result-$i.json"
        $p = Invoke-SelftestProcess -FilePath $exe -ArgumentList @('--selftest', '--result', $result) -TimeoutSec $TimeoutSec
        $exit = if ($null -eq $p.ExitCode) { -1 } else { $p.ExitCode }
        $run = Test-SelftestRun -Run $i -ExitCode $exit -TimedOut $p.TimedOut -ResultPath $result -AllowNoPlaceholders:$AllowNoPlaceholders
        $label = if ($i -eq 1) { 'cold' } else { 'warm' }
        Write-Host "run $i ($label): exit $exit, first frame $($run.FirstFrameMs) ms$(if (-not $run.Ok) { ' - ' + ($run.Reasons -join '; ') })"
        $run
    }
    $gate = Test-SelftestGate -Runs @($runs) -MaxWarmMs $maxWarmMs -BaselineMs $baselineMs
}
finally { Remove-Item -LiteralPath $gateDir -Recurse -Force -ErrorAction SilentlyContinue }

if (-not $gate.Ok) { Stop-Deploy ('selftest gate: ' + ($gate.Reasons -join '; ')) }
Write-Host "GATE PASS: warm first frame $($gate.WarmMs) ms (limit $maxWarmMs ms; ReadyToRun baseline $baselineMs ms)"
if ($gate.SlowerThanBaseline) {
    # Decision 3 (00-interfaces.md): reported to the user, never a failure.
    Write-Host ("NOTE: warm first frame $($gate.WarmMs) ms is slower than the 0.37 s ReadyToRun baseline; " +
                'recorded as slowerThanBaseline and reported in the completion summary.')
}

Write-Host "== 4/5 Install to $(Join-Path $installRoot $Version)"
$installed = Copy-UasSortBuild -PublishDir $publishDir -InstallRoot $installRoot -Version $Version -Force:$Force

Write-Host '== 5/5 Start-menu shortcut and old versions (keeping 2)'
New-UasSortShortcut -ShortcutPath $shortcut -TargetPath (Join-Path $installed 'uas-sort.exe') -Description "uas-sort $Version"
foreach ($removed in @(Remove-OldVersions -InstallRoot $installRoot -Current $Version -Keep 2)) { Write-Host "removed $removed" }

$commit = (& git -C $repo rev-parse HEAD).Trim()
$dirty = [bool](& git -C $repo status --porcelain)
[ordered]@{
    version = $Version; rid = $rid; commit = $commit; dirty = $dirty; sizeMB = $output.SizeMB
    coldMs = $gate.ColdMs; warmMs = $gate.WarmMs; maxWarmMs = $maxWarmMs; baselineMs = $baselineMs
    slowerThanBaseline = $gate.SlowerThanBaseline; installedTo = $installed
    atUtc = (Get-Date).ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ssZ')
} | ConvertTo-Json | Set-Content -LiteralPath $summaryPath -Encoding utf8

Write-Host "DEPLOYED uas-sort $Version ($rid) to $installed; summary in $summaryPath"
exit 0
```

- [ ] **Step 3: Run the deploy scripts' own checks, then the real gate on x64**

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File tools\deploy.tests.ps1
dotnet build uas-sort.slnx
pwsh -NoProfile -ExecutionPolicy Bypass -File tools\deploy.ps1 -Version 0.1.0
```

Expected (numbers vary):

```text
38 run, 0 failed
...
== 1/5 Native AOT publish (win-x64, version 0.1.0)
  UasSort.App -> C:\dev\uas-sort\artifacts\publish\win-x64\
== 2/5 Native AOT output check
uas-sort.exe is native win-x64; the folder is 1xx.x MB (ReadyToRun baseline 87-97 MB)
== 3/5 --selftest gate (two runs)
run 1 (cold): exit 0, first frame 9xx ms
run 2 (warm): exit 0, first frame 3xx ms
GATE PASS: warm first frame 3xx ms (limit 1000 ms; ReadyToRun baseline 370 ms)
== 4/5 Install to C:\Users\damia\AppData\Local\Programs\uas-sort\0.1.0
== 5/5 Start-menu shortcut and old versions (keeping 2)
DEPLOYED uas-sort 0.1.0 (win-x64) to C:\Users\damia\AppData\Local\Programs\uas-sort\0.1.0; summary in C:\dev\uas-sort\artifacts\deploy-win-x64.json
```

Exit code 0. Then check the summary:

```powershell
Get-Content artifacts\deploy-win-x64.json | ConvertFrom-Json | Format-List version, rid, sizeMB, coldMs, warmMs, slowerThanBaseline
```

Expected: `slowerThanBaseline` is `False` or `True`. `True` is not a failure (decision 3): the run printed the `NOTE: warm first frame ...` line, and Task 13.8 reports the warm value and the 370 ms baseline to the user.

Handling the outcomes:
- `run 1 ... no result file at '...selftest-result-1.json'` while the exit code is 0: the App's selftest does not honour `--result <path>`. In `src/UasSort.App` (Part 01's `LaunchOptions.Parse`, extended by Part 11), make `LaunchOptions.ResultPath` take the path given after `--result` (`LaunchOptions.ResultFlag`) and make `SelfTestRunner` write the `SelfTestResult` JSON there through `SelfTestSandbox.WriteResult(path, utf8Json)` (keep the `%TEMP%\uas-sort-selftest-<guid>\` sandbox folder for everything else), keeping the shape at the top of this part, then re-run `tools\deploy.ps1 -Version 0.1.0 -Force`. Commit that App change separately (`fix: selftest writes its result to --result <path>`).
- `check 'placeholderVisibility' is not applicable`: the selftest found no cloud-only files under this PC's video root. Stop and ask the user whether the configured video root really has none; pass `-AllowNoPlaceholders` only on the user's answer.
- `check '...' is not applicable ...; only placeholderVisibility may be not applicable`: a selftest check other than `placeholderVisibility` broke the result contract. Fix that check in Part 11's `SelfTestChecks` (it must report `pass` or `fail`), commit it separately (`fix: selftest check reports pass or fail`), and re-run with `-Force`.
- `NOTE: warm first frame ... slower than the 0.37 s ReadyToRun baseline`: not a failure; continue.
- Any other failure (publish error or AOT/trim warning, a failed check, a timeout, warm > 1000 ms): follow **If Native AOT fails a concrete check (stop and ask)** below. Do not continue to Task 13.6.

- [ ] **Step 4: Check that nothing else was touched**

```powershell
Get-ChildItem "$env:LOCALAPPDATA\Programs\uas-sort" | Select-Object Name
(New-Object -ComObject WScript.Shell).CreateShortcut((Join-Path ([Environment]::GetFolderPath('Programs')) 'uas-sort.lnk')).TargetPath
Get-ChildItem ([System.IO.Path]::GetTempPath()) -Directory -Filter 'uas-sort-deploy-*'
git status --porcelain
```

Expected: `0.1.0` (plus at most one older version folder); the target `C:\Users\damia\AppData\Local\Programs\uas-sort\0.1.0\uas-sort.exe`; no `uas-sort-deploy-*` folder left; `git status` lists only `tools/deploy.ps1` (artifacts are git-ignored).

- [ ] **Step 5: Commit**

```powershell
git add tools/deploy.ps1
git commit -m "feat: deploy.ps1 (Native AOT publish, two-run selftest gate, install, Start-menu shortcut, keep 2 versions)

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_015Z1pwYTXofSxv2csCcXa4z"
```

---

### Task 13.6 — Native AOT publish for `win-arm64`

**Pending the user's answer (`00-interfaces.md`, "Open for user" 1, ARM64):** the user has not yet said whether they have or plan any ARM64 PC. Until they answer, this task stays as written and is executed as written. If the user answers that ARM64 is not needed, the plan change is made then, as one edit set: drop this task, the `win-arm64` case of `PublishConfig.Tests.ps1`, the README's ARM64 cross-linker requirement and the `win-arm64` publish in the maintenance steps, and remove `win-arm64` from `RuntimeIdentifiers`/`Platforms` (Part 01's App csproj and the index's Global Constraints).

ARM64 is built here and stays UNVERIFIED at run time (no ARM64 PC; Ref §15 Q5): the check is that the cross-compiled Native AOT publish succeeds with warnings as errors and produces a native ARM64 `uas-sort.exe`. `deploy.ps1` is not involved (it publishes for the machine's own RID).

**Files:**
- None created or modified (the output goes to the git-ignored `artifacts\publish\win-arm64\`).

**Interfaces:**
- Consumes: `Test-AotPublishOutput`, `Get-FolderStats` (Task 13.3); the Release `win-arm64` publish configuration checked in Task 13.1.
- Produces: `artifacts\publish\win-arm64\` (native ARM64 `uas-sort.exe`), recorded in this task's commit message.

- [ ] **Step 1: Failing check — no ARM64 publish yet**

```powershell
Import-Module .\tools\Deploy.psm1 -Force
Test-AotPublishOutput -PublishDir artifacts\publish\win-arm64 -Rid win-arm64 | Format-List
```

Expected: `Ok : False`, `Reasons : {the publish folder '...\artifacts\publish\win-arm64' does not exist}`.

- [ ] **Step 2: Check the ARM64 cross-linker prerequisite**

Native AOT links with MSVC; a `win-arm64` publish on an x64 PC needs the MSVC ARM64 build tools, which the `VCTools` workload of the prerequisites does not always include.

```powershell
$vc = 'C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools\VC\Tools\MSVC'
@(Get-ChildItem -Path "$vc\*\bin\Hostx64\arm64\link.exe" -ErrorAction SilentlyContinue).Count
```

Expected: `1` or more. If it prints `0`, stop and ask the user to install the component (install rule, Ref §1.1: never design around a missing tool), with this message and command, then wait:

```text
The win-arm64 Native AOT publish needs the MSVC ARM64 build tools, which are not installed. Please run (elevated):

& "C:\Program Files (x86)\Microsoft Visual Studio\Installer\vs_installer.exe" modify --installPath "C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools" --add Microsoft.VisualStudio.Component.VC.Tools.ARM64 --passive

and tell me when it has finished.
```

- [ ] **Step 3: Publish `win-arm64`**

```powershell
if (Test-Path artifacts\publish\win-arm64) { Remove-Item artifacts\publish\win-arm64 -Recurse -Force }
dotnet publish src\UasSort.App\UasSort.App.csproj -c Release -r win-arm64 -o artifacts\publish\win-arm64
```

Expected: exit code 0, no `warning` and no `error` lines, last line `UasSort.App -> C:\dev\uas-sort\artifacts\publish\win-arm64\`. Any IL/trim/AOT warning (an error under `TreatWarningsAsErrors`) or an ILC/link failure other than the missing prerequisite of Step 2 → **If Native AOT fails a concrete check (stop and ask)** below, with `-r win-arm64` in its commands.

- [ ] **Step 4: The check passes**

```powershell
Import-Module .\tools\Deploy.psm1 -Force
Test-AotPublishOutput -PublishDir artifacts\publish\win-arm64 -Rid win-arm64 | Format-List
Get-PeMachine -Path artifacts\publish\win-arm64\uas-sort.exe
```

Expected: `Ok : True`, `Reasons : {}`, a `SizeMB` value, then `arm64`.

- [ ] **Step 5: Commit (records the result; no tracked files change)**

```powershell
$size = (Test-AotPublishOutput -PublishDir artifacts\publish\win-arm64 -Rid win-arm64).SizeMB
git commit --allow-empty -m "build: verify the win-arm64 Native AOT publish ($size MB, native ARM64; run-time UNVERIFIED)

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_015Z1pwYTXofSxv2csCcXa4z"
```

---

### Task 13.7 — README: build, test, install, dry run, maintenance

**Files:**
- Modify: `README.md`

**Interfaces:**
- Consumes: the commands of Ref §2.6; `tools/deploy.ps1` (Task 13.5); `tools/deploy.tests.ps1` (Task 13.1); the CLI contract and `tests/acceptance/README.md` (Part 12).
- Produces: README sections `## Requirements`, `## Build and test`, `## Install`, `## Dry run (CLI)`, `## Maintenance`, and the source layout in `## Repository layout`.

- [ ] **Step 1: Failing check**

```powershell
pwsh -NoProfile -Command "if (Select-String -Path README.md -SimpleMatch 'Build and run instructions will be added with the code.') { 'stale'; exit 1 }; if (-not (Select-String -Path README.md -SimpleMatch 'tools\deploy.ps1 -Version')) { 'no deploy'; exit 1 }; 'ok'"
```

Expected: prints `stale`, exit code 1.

- [ ] **Step 2: Edit `README.md`**

Replace the heading line

```markdown
## Requirements (planned)
```

with

```markdown
## Requirements
```

In that section, replace the line

```markdown
  - Optional: VS Code + C# Dev Kit (`winget install Microsoft.VisualStudioCode`), or Visual Studio 2026 Insiders for the XAML designer and Hot Reload
```

with

```markdown
  - Only to cross-publish the ARM64 build on an x64 PC: the MSVC ARM64 build tools
    ```powershell
    & "C:\Program Files (x86)\Microsoft Visual Studio\Installer\vs_installer.exe" modify --installPath "C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools" --add Microsoft.VisualStudio.Component.VC.Tools.ARM64 --passive
    ```
  - Optional: VS Code + C# Dev Kit (`winget install Microsoft.VisualStudioCode`), or Visual Studio 2026 Insiders for the XAML designer and Hot Reload
```

Replace the line

```markdown
Build and run instructions will be added with the code.
```

with

````markdown
## Build and test

Run everything from the repository root (`C:\dev\uas-sort`) in PowerShell 7 on Windows; `global.json` pins the SDK, and warnings are errors in every project.

```powershell
dotnet build uas-sort.slnx                          # the first restore takes ~8-13 min (~1.5-2 GB of NuGet packages)
dotnet test --solution uas-sort.slnx                # the whole suite (xUnit v3 on Microsoft.Testing.Platform)
dotnet test --project tests/UasSort.Core.Tests/UasSort.Core.Tests.csproj -- --filter-method "*Zachar*"   # one project, narrowed
pwsh -NoProfile -ExecutionPolicy Bypass -File tools\build.ps1 -CheckBannedApi   # every banned file-system call must fail the build
pwsh -NoProfile -ExecutionPolicy Bypass -File tools\deploy.tests.ps1            # checks for the deploy script
```

From WSL, run the same commands through `tools/r.sh` (it calls the Windows `dotnet.exe` and `pwsh.exe` with the working directory set to the repository), then `dotnet build-server shutdown` so the compiler server releases its file locks.

## Install

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File tools\deploy.ps1 -Version 0.1.0
```

`deploy.ps1` publishes the app as Native AOT for this PC's architecture (`win-x64` or `win-arm64`) and runs the published `uas-sort.exe --selftest` twice: both runs must pass, and the second (warm) run must reach its first frame within 1 s. Only then does it copy the build to `%LOCALAPPDATA%\Programs\uas-sort\<version>\`, point the Start-menu shortcut **uas-sort** at it, and delete all but the two newest versions. It never touches settings, drafts, reports, logs or any ledger.

- Use a higher `-Version` for every install (`-Force` replaces an installed version with the same number).
- If your video root has no cloud-only files, the selftest's placeholder check reports "not applicable"; add `-AllowNoPlaceholders` only in that case.
- Per-PC state lives in `%LOCALAPPDATA%\uas-sort\` (settings, drafts, reports, logs and the local ledger backup). The shared offload history lives in `<videoRoot>\.uas-sort\`.
- To uninstall, delete `%LOCALAPPDATA%\Programs\uas-sort\` and the Start-menu shortcut. Keep `<videoRoot>\.uas-sort\`: it is the offload history for every PC.

## Dry run (CLI)

```powershell
dotnet run --project src/UasSort.Cli -- plan --card E:\ --json
dotnet run --project src/UasSort.Cli -- plan --card E:\ --expect tests\acceptance\first-card-expected.json
```

`uas-sort-cli plan` scans a card and prints the plan the app would propose. It writes nothing, anywhere (no drafts, no ledger, no files; logs go to stderr) and has no cleanup command. Roots and tuning come from the app's settings, or from `--video-root`, `--photo-root`, `--radius-mi` (5-100), `--gap-days` (0-7) and `--settings <file>`. Exit codes: 0 = plan printed (even with blocking issues), 1 = the card source was refused (the reason is on stderr), 2 = any other error. `--expect` compares the plan with your expected folder list and prints the differences and the edit count; see [`tests/acceptance/README.md`](tests/acceptance/README.md).

## Maintenance

The SDK is pinned in `global.json` (`11.0.100-rc.1.26425.128`). When .NET 11 RC2 (~Oct 13) and GA (Nov 10) ship: install the new SDK, set its exact version in `global.json` (at GA also `"allowPrerelease": false`), move `System.IO.Hashing` in `Directory.Packages.props` to the matching version, then re-run the build, the tests, the banned-API check and `deploy.ps1` (which repeats the Native AOT publish and the selftest gate). `AnalysisLevel` stays pinned, so new analyzer rules don't appear by surprise.
````

In `## Repository layout`, replace the fenced block and the line after it

````markdown
```
docs/
  superpowers/specs/
    2026-09-27-uas-sort-design.md             # main design spec — start here
    2026-09-27-uas-sort-design-reference.md   # detailed companion (types, rules, tests)
  research/
````

with

````markdown
```
src/
  UasSort.Core/        UI-free logic: model, probes, time and place, library index, planning, offload, cleanup
  UasSort.Platform/    the only code that touches disk, Win32 or the shell
  UasSort.Review/      view models (no WinUI types)
  UasSort.App/         WinUI 3 app (uas-sort.exe), map pane, --selftest
  UasSort.Cli/         uas-sort-cli plan (dry run)
tests/                 unit, integration and view-model tests; acceptance/ holds the first-card expectations
tools/                 build.ps1, deploy.ps1, r.sh (WSL), place-index and fixture builders
docs/
  superpowers/specs/
    2026-09-27-uas-sort-design.md             # main design spec — start here
    2026-09-27-uas-sort-design-reference.md   # detailed companion (types, rules, tests)
  superpowers/plans/
    2026-09-27-uas-sort.md                    # implementation plan (index of the build parts)
  research/
````

and replace the line

```markdown
Planned source layout (from the spec): `src/UasSort.Core` (UI-free logic), `src/UasSort.Platform` (the only code that touches disk/Win32), `src/UasSort.Review` (view models), `src/UasSort.App` (WinUI shell), `src/UasSort.Cli` (dry-run planner), plus matching test projects.
```

with

```markdown
Dependencies point inwards: App → Review → Core ← Platform, and the CLI uses Platform and Core only. Direct file-system APIs outside Platform fail the build.
```

- [ ] **Step 3: The check passes**

```powershell
pwsh -NoProfile -Command "if (Select-String -Path README.md -SimpleMatch 'Build and run instructions will be added with the code.') { 'stale'; exit 1 }; if (-not (Select-String -Path README.md -SimpleMatch 'tools\deploy.ps1 -Version')) { 'no deploy'; exit 1 }; 'ok'"
```

Expected: `ok`, exit code 0. Also open `README.md` and confirm no raw `<` appears outside code spans and fences (the repository rule for Markdown; the new text uses `<videoRoot>` and `<version>` only inside backticks).

- [ ] **Step 4: Commit**

```powershell
git add README.md
git commit -m "docs: README build, test, install, dry-run and maintenance instructions

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_015Z1pwYTXofSxv2csCcXa4z"
```

---

### Task 13.8 — Completion criteria (Ref §14)

The single-pass build is complete only when every step below passes on the same commit. A failure is fixed in the owning part's code (use superpowers:systematic-debugging), committed, and this task restarts at Step 1. An AOT concrete-check failure is never fixed by changing the build type: follow the stop-and-ask procedure below.

**Files:**
- Modify: `README.md` (status line only, Step 8)

**Interfaces:**
- Consumes: the whole solution (Parts 01–12), `tools/build.ps1 -CheckBannedApi` (Part 01), the CLI run test `CliPlanRunTests` (Part 12), `tools/deploy.tests.ps1`, `tools/deploy.ps1` (this part).
- Produces: the completion record (the commit of Step 8, carrying the gate numbers).

- [ ] **Step 1: Clean tree**

```powershell
git status --porcelain
```

Expected: no output.

- [ ] **Step 2: The full solution builds with warnings as errors (Debug and Release)**

```powershell
dotnet build uas-sort.slnx -tl:off
dotnet build uas-sort.slnx -c Release -tl:off
```

Expected, each: exit code 0 and the summary

```text
Build succeeded.
    0 Warning(s)
    0 Error(s)
```

- [ ] **Step 3: The whole test suite passes**

```powershell
dotnet test --solution uas-sort.slnx
```

Expected: exit code 0; every test project reports `failed: 0` in its Microsoft.Testing.Platform summary, and the run ends with `Test run summary: Passed!`. The suite includes unit, IO guard policy, card cleanup, invariants, performance, newness and ledger, golden replay, synthetic media, fake-FS tripwire, fault injection, Windows integration, view-model tests and the CLI run. Confirm the CLI run ran (and was not filtered away):

```powershell
dotnet test --project tests/UasSort.Platform.Tests/UasSort.Platform.Tests.csproj -- --filter-class "*CliPlanRunTests*"
```

Expected: `failed: 0`, `succeeded:` at least 1.

- [ ] **Step 4: The banned-API probe raises every expected RS0030**

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File tools\build.ps1 -CheckBannedApi
$LASTEXITCODE
```

Expected: Part 01's summary line `OK: 73 banned entries, 73 probe calls, 73 RS0030 (one per call)` (the counts equal the number of `BannedSymbols.txt` entries; no `FAIL` lines), and `$LASTEXITCODE` prints `0`.

- [ ] **Step 5: The deploy checks pass**

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File tools\deploy.tests.ps1
```

Expected: last line `38 run, 0 failed`, exit code 0.

- [ ] **Step 6: The Native AOT publish passes `--selftest` through the deploy gate on x64**

Use the next unused version (`0.1.0` if Task 13.5 left no install of the current commit; otherwise `0.1.1`, `0.1.2`, …):

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File tools\deploy.ps1 -Version 0.1.1
Get-Content artifacts\deploy-win-x64.json | ConvertFrom-Json | Format-List version, commit, dirty, sizeMB, coldMs, warmMs, slowerThanBaseline
```

Expected: `GATE PASS: warm first frame NNN ms (limit 1000 ms; ReadyToRun baseline 370 ms)` with NNN ≤ 1000, `DEPLOYED uas-sort 0.1.1 (win-x64) ...`, exit code 0; the summary shows the current `git rev-parse HEAD` as `commit`, `dirty : False`, and `slowerThanBaseline` either `False` or `True`. If the gate fails: stop and ask — follow **If Native AOT fails a concrete check (stop and ask)** below; the build is not complete until the user has decided. If `slowerThanBaseline` is `True`, continue, and state the warm value and the 370 ms baseline in the completion commit message and the final summary to the user (Step 8; decision 3).

- [ ] **Step 7: Release the build servers (after WSL-driven builds) and re-check the tree**

```powershell
dotnet build-server shutdown
git status --porcelain
```

Expected: `shutdown` reports each server shut down; `git status` prints nothing.

- [ ] **Step 8: Record completion (README status line) and commit**

In `README.md`, replace

```markdown
> **Status: design complete, implementation not started.**
> The approved design spec lives in [`docs/superpowers/specs/`](docs/superpowers/specs/). The initial, feature-complete version is built in one pass (see [Build approach](#build-approach)).
```

with

```markdown
> **Status: initial version built; the first-real-card acceptance is next.**
> The approved design spec lives in [`docs/superpowers/specs/`](docs/superpowers/specs/) and the implementation plan in [`docs/superpowers/plans/`](docs/superpowers/plans/). The initial, feature-complete version was built in one pass (see [Build approach](#build-approach)); the user's acceptance on a real card follows (dry run, rehearsal, real offload, card cleanup).
```

Then commit with the measured numbers from `artifacts\deploy-win-x64.json`:

```powershell
$s = Get-Content artifacts\deploy-win-x64.json | ConvertFrom-Json
git add README.md
git commit -m "chore: completion criteria met (build, tests, banned-API probe, AOT selftest gate: warm $($s.warmMs) ms, ReadyToRun baseline $($s.baselineMs) ms, slowerThanBaseline $($s.slowerThanBaseline), $($s.sizeMB) MB)

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_015Z1pwYTXofSxv2csCcXa4z"
```

Then tell the user the build is complete, with the warm first frame, the 1000 ms gate and the 370 ms ReadyToRun baseline from `artifacts\deploy-win-x64.json`; when `slowerThanBaseline` is `True`, say so plainly in the final summary ("the warm first frame NNN ms passes the 1 s gate but is slower than the 370 ms ReadyToRun baseline"); it is reported, not a failure (decision 3). Then hand over the first-real-card acceptance (Ref §13): fill in `tests/acceptance/first-card-expected.json` (template in `tests/acceptance/`, Part 12), snapshot the card listing, run `uas-sort-cli plan --card E:\ --json --expect tests\acceptance\first-card-expected.json` (passes at ≤ 2 edits), rehearse into `%TEMP%\uas-sort-rehearsal\{video,photo}`, do the real offload, then clean up one old eligible clip.

---

## If Native AOT fails a concrete check (stop and ask)

A concrete check is: a Native AOT build or publish error, an IL/trim/AOT warning (an error under `TreatWarningsAsErrors`), a selftest failure or timeout, or a warm first frame over 1 s. A warm first frame slower than the 0.37 s ReadyToRun baseline (`slowerThanBaseline : True`) is not a concrete check: it is reported in the completion summary (Task 13.8, decision 3). Ref §2.2 and §14: ReadyToRun is used **only by hand, only to tell whether the failure is AOT-specific**, and the result is **raised with the user**. Never add `PublishReadyToRun` to a project file, never remove `PublishAot`, never widen `NoWarn`, and never continue to a later task while this is open.

- [ ] **A. Keep the AOT evidence**

```powershell
New-Item -ItemType Directory -Force artifacts\diag | Out-Null
git rev-parse HEAD
dotnet publish src\UasSort.App\UasSort.App.csproj -c Release -r win-x64 -o artifacts\diag\aot-win-x64 2>&1 | Tee-Object artifacts\diag\aot-publish-win-x64.txt
```

(Use `win-arm64` in every command of this section when the failure is the ARM64 publish; its selftest can't run on this PC, so skip step C for it.)

- [ ] **B. Hand-run a trimmed ReadyToRun publish of the same commit** (the spike's baseline configuration; overrides on the command line only)

```powershell
dotnet publish src\UasSort.App\UasSort.App.csproj -c Release -r win-x64 -p:PublishAot=false -p:PublishReadyToRun=true -p:PublishTrimmed=true -o artifacts\diag\r2r-win-x64 2>&1 | Tee-Object artifacts\diag\r2r-publish-win-x64.txt
```

- [ ] **C. Run the same selftest gate against both builds**

```powershell
Import-Module .\tools\Deploy.psm1 -Force
foreach ($build in 'aot-win-x64', 'r2r-win-x64') {
    $exe = "artifacts\diag\$build\uas-sort.exe"
    if (-not (Test-Path $exe)) { "${build}: no exe (publish failed)"; continue }
    $runs = foreach ($i in 1, 2) {
        $result = Join-Path ([System.IO.Path]::GetTempPath()) "uas-sort-diag-$build-$i.json"
        $p = Invoke-SelftestProcess -FilePath (Resolve-Path $exe).Path -ArgumentList @('--selftest', '--result', $result) -TimeoutSec 60
        Test-SelftestRun -Run $i -ExitCode ($p.ExitCode ?? -1) -TimedOut $p.TimedOut -ResultPath $result
        Remove-Item -LiteralPath $result -ErrorAction SilentlyContinue
    }
    $g = Test-SelftestGate -Runs @($runs)
    "${build}: ok=$($g.Ok) cold=$($g.ColdMs) ms warm=$($g.WarmMs) ms size=$((Get-FolderStats "artifacts\diag\$build").MB) MB; $($g.Reasons -join '; ')"
}
```

- [ ] **D. Decide what the evidence says** (write it down, don't act on it)
  - AOT fails and ReadyToRun passes the same check → the failure is AOT-specific.
  - Both fail the same check → not AOT-specific: it is a bug in the code; fix it in the owning part, then re-run the failed AOT check (this is not a build-type question, and the stop ends here unless AOT still fails).
  - The AOT warm start is over 1 s while ReadyToRun's is within it → the startup failure is AOT-specific (the cold and warm values of both builds go into the message).

- [ ] **E. Stop and ask the user**, with this message filled from the outputs above (`...` are the measured values), then wait for the answer before any further work on this part:

```text
Native AOT failed a concrete check at commit <git rev-parse HEAD> (Part 13, Task 13.N).
Failed check: <publish error | IL/trim/AOT warning ILxxxx from <assembly> | selftest check '<name>' | timeout | warm first frame ... ms (limit 1000 ms, ReadyToRun baseline 370 ms)>
AOT evidence: artifacts\diag\aot-publish-win-x64.txt (first error or warning: ...); gate: cold ... ms, warm ... ms, ... MB
ReadyToRun (same commit, by hand, for diagnosis only): publish <ok|failed>; gate: cold ... ms, warm ... ms, ... MB
Conclusion: <AOT-specific | not AOT-specific>
Per the spec (Ref §2.2, §14) I have not switched the build type and nothing has been installed from the ReadyToRun build.
How do you want to proceed?
```

- [ ] **F. Clean up the diagnosis output** once the user has answered: `Remove-Item artifacts\diag -Recurse -Force` (git-ignored; nothing to commit).

---

## Maintenance after the build: RC2 and GA SDK updates (not executed in this pass)

Ref §2.2 and §14 "Maintenance after the build". RC1's go-live support ends when RC2 ships (~Oct 13, date UNVERIFIED); GA ships Nov 10. Apply each update when it ships, as one commit, and re-run the gates.

- [ ] **RC2**
  1. `winget upgrade Microsoft.DotNet.SDK.Preview`, then `dotnet --list-sdks` and note the exact `11.0.100-rc.2.*` version.
  2. In `global.json`, set `"version"` to that exact string (keep `"rollForward": "latestFeature"`, `"allowPrerelease": true` and the `test` runner).
  3. In `Directory.Packages.props`, move `System.IO.Hashing` to the RC2 build that shipped with that SDK: `dotnet package search System.IO.Hashing --exact-match --prerelease --format json` lists it (the `11.0.0-rc.2.*` version whose build number matches the SDK's runtime).
  4. In `README.md` (`## Requirements` and `## Maintenance`), replace `11.0.100-rc.1.26425.128` / `rc.1` with the RC2 version.
  5. Re-run the gates: `dotnet build uas-sort.slnx -tl:off`, `dotnet test --solution uas-sort.slnx`, `pwsh -NoProfile -ExecutionPolicy Bypass -File tools\build.ps1 -CheckBannedApi`, `pwsh -NoProfile -ExecutionPolicy Bypass -File tools\deploy.tests.ps1`, `pwsh -NoProfile -ExecutionPolicy Bypass -File tools\deploy.ps1 -Version <next patch version>` (the Native AOT publish and `--selftest`), and the `win-arm64` publish of Task 13.6. Any AOT concrete-check failure → the stop-and-ask procedure above.
  6. Commit: `build: move to the .NET 11 RC2 SDK` (with the session trailer lines).
- [ ] **GA (Nov 10)**
  1. Install the GA SDK (`winget install Microsoft.DotNet.SDK.11`; the GA package id is UNVERIFIED until it ships — `winget search Microsoft.DotNet.SDK` shows it), and confirm `11.0.100` in `dotnet --list-sdks`.
  2. `global.json`: `"version": "11.0.100"` and **`"allowPrerelease": false`**; keep `"rollForward": "latestFeature"` and the `test` runner.
  3. `Directory.Packages.props`: `System.IO.Hashing` `11.0.0`. Leave `Microsoft.Extensions.TimeProvider.Testing` at `10.10.0` unless an 11.x has been published. `AnalysisLevel` stays pinned (`11.0-recommended`, or `10.0-recommended` if that fallback was taken in Part 01).
  4. README: the SDK line in `## Requirements` becomes `winget install` of the GA package, and `## Maintenance` names `11.0.100`.
  5. Re-run the same gates as for RC2.
  6. Commit: `build: move to the .NET 11 GA SDK (allowPrerelease false)`.
  7. The preview SDK may then be uninstalled (`winget uninstall Microsoft.DotNet.SDK.Preview`) once the GA gates are green.

---

## Part 13 — Produces (summary)

- `tools/Deploy.psm1` — `Get-AppPublishConfig`, `Test-SelftestRun`, `Test-SelftestGate`, `Invoke-SelftestProcess`, `Get-UasSortRid`, `Get-PeMachine`, `Get-FolderStats`, `Test-AotPublishOutput`, `Test-PathUnder`, `Assert-DeployTargetSafe`, `Copy-UasSortBuild`, `Get-VersionsToPrune`, `Remove-OldVersions`, `New-UasSortShortcut`.
- `tools/deploy.tests.ps1` (runner, `-Name` filter) and `tools/deploy-tests/{PublishConfig,Gate,Process,Install}.Tests.ps1` — 38 cases, incl. the Release AOT configuration for both RIDs (never ReadyToRun, rooted `MetadataExtractor;XmpCore`, only IL2104/IL3053 silenceable).
- `tools/deploy.ps1 -Version x.y.z [-AllowNoPlaceholders] [-Force] [-TimeoutSec 60]` — Native AOT publish for the machine's RID, output check, `--selftest --result <path>` twice (60 s each; both exit 0; warm ≤ 1000 ms is the hard gate; slower than the 370 ms ReadyToRun baseline is recorded as `slowerThanBaseline` and reported, not a failure), install to `%LOCALAPPDATA%\Programs\uas-sort\<version>\`, Start-menu `uas-sort.lnk`, keep 2 versions, summary `artifacts\deploy-<rid>.json`; never writes user data.
- The selftest result contract `--selftest --result <path> [--only <check>[,<check>…]]` → `{ ok, firstFrameMs, checks[{name,status,detail}] }` (defined here; honoured by Part 01's `LaunchOptions`/`SelfTestResult` as extended by Part 11's selftest; only `placeholderVisibility` may be `notApplicable`).
- Verified `win-x64` (gated, installed) and `win-arm64` (built, native ARM64, run-time UNVERIFIED; pending the user's ARM64 answer) Native AOT publishes.
- README: requirements (incl. the ARM64 cross-linker), build and test, install, dry run (CLI), maintenance, source layout, and the final status line.
- The completion record (Task 13.8), the stop-and-ask procedure for AOT concrete-check failures, and the RC2/GA maintenance steps.
