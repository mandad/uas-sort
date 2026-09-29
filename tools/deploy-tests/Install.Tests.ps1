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
