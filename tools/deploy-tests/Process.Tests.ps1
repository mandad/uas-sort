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

Test-Case 'Rid: this x64 PC is win-x64' {
    Assert-Equal 'win-x64' (Get-UasSortRid) 'Get-UasSortRid'
}

Test-Case 'Pe: the machine type comes from the PE header' {
    $dir = New-TestDir
    try {
        New-FakePe (Join-Path $dir 'x.exe') 0x8664
        New-FakePe (Join-Path $dir 'i.exe') 0x014C
        Set-Content -LiteralPath (Join-Path $dir 'text.exe') -Value 'not a PE file at all, just some text'
        Assert-Equal 'x64' (Get-PeMachine -Path (Join-Path $dir 'x.exe')) 'x64'
        Assert-Equal 'x86' (Get-PeMachine -Path (Join-Path $dir 'i.exe')) 'x86'
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
        New-FakeAotPublish $dir 0x014C
        $r = Test-AotPublishOutput -PublishDir $dir -Rid win-x64
        Assert-True (-not $r.Ok) 'an x86 exe is not a win-x64 publish'
        Assert-True (($r.Reasons -join ' ') -like '*is x86, expected x64*') ($r.Reasons -join '; ')
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
