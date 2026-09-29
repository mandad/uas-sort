# tools/vendor-maplibre.ps1 — pin and vendor the map engine into src\UasSort.App\MapAssets (Ref §9.6 Assets).
# Default: MapLibre GL JS 6.11.2 as ES modules (.mjs).
#   -RenameToJs : fallback 1 — rename every .mjs to .js, rewrite relative import specifiers, record the worker URL.
#   -Leaflet    : fallback 3 — Leaflet 1.9.4 instead of MapLibre.
param([switch]$RenameToJs, [switch]$Leaflet)
$ErrorActionPreference = 'Stop'
$repo   = Split-Path -Parent $PSScriptRoot
$assets = Join-Path $repo 'src\UasSort.App\MapAssets'
$lib    = Join-Path $assets 'lib'
$fonts  = Join-Path $assets 'fonts'
$tmp    = Join-Path ([IO.Path]::GetTempPath()) ("uas-sort-vendor-" + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory $tmp | Out-Null

function Get-NpmPackage([string]$Name, [string]$Version) {
    $meta = Invoke-RestMethod "https://registry.npmjs.org/$Name/$Version"
    $tgz = Join-Path $tmp "$Name-$Version.tgz"
    Invoke-WebRequest $meta.dist.tarball -OutFile $tgz
    $actual = 'sha512-' + [Convert]::ToBase64String([Security.Cryptography.SHA512]::HashData([IO.File]::ReadAllBytes($tgz)))
    if ($actual -ne $meta.dist.integrity) { throw "Integrity mismatch for $Name@${Version}: $actual vs $($meta.dist.integrity)" }
    $dir = Join-Path $tmp $Name
    New-Item -ItemType Directory $dir | Out-Null
    & tar.exe -xzf $tgz -C $dir
    if ($LASTEXITCODE -ne 0) { throw "tar failed for $tgz" }
    [pscustomobject]@{ Dir = (Join-Path $dir 'package'); Integrity = $meta.dist.integrity }
}

try {
    if (Test-Path $lib) { Remove-Item $lib -Recurse -Force }
    New-Item -ItemType Directory $lib | Out-Null

    if ($Leaflet) {
        $p = Get-NpmPackage 'leaflet' '1.9.4'
        Copy-Item (Join-Path $p.Dir 'dist\leaflet.js'), (Join-Path $p.Dir 'dist\leaflet.css') $lib
        Copy-Item (Join-Path $p.Dir 'dist\images') (Join-Path $lib 'images') -Recurse
        $manifest = [ordered]@{ engine = 'leaflet'; version = '1.9.4'; entry = 'leaflet.js'; worker = $null; integrity = $p.Integrity }
    }
    else {
        $version = '6.11.2'
        $p = Get-NpmPackage 'maplibre-gl' $version
        $dist = Join-Path $p.Dir 'dist'
        if (-not (Test-Path (Join-Path $dist 'maplibre-gl.mjs'))) { throw 'dist\maplibre-gl.mjs is missing: the package layout changed; stop and re-check Ref §9.6' }
        Copy-Item (Join-Path $dist 'maplibre-gl.css') $lib
        # the production modules only: the *-dev.mjs twins (~2.5 MB) are never loaded, and would make -RenameToJs pick the dev worker
        Get-ChildItem $dist -Filter 'maplibre-gl*.mjs' | Where-Object Name -notlike '*-dev.mjs' | Copy-Item -Destination $lib
        $entry = 'maplibre-gl.mjs'; $worker = $null
        if ($RenameToJs) {
            foreach ($f in Get-ChildItem $lib -Filter '*.mjs') {
                $text = Get-Content -LiteralPath $f.FullName -Raw
                $text = [regex]::Replace($text, '(["''])\./([\w.-]+)\.mjs\1', '$1./$2.js$1')
                Set-Content -LiteralPath (Join-Path $lib ($f.BaseName + '.js')) -Value $text -NoNewline -Encoding utf8NoBOM
                Remove-Item -LiteralPath $f.FullName
            }
            $entry = 'maplibre-gl.js'
            $w = Get-ChildItem $lib -Filter '*worker*.js' | Select-Object -First 1
            if (-not $w) { throw 'No worker file after the rename' }
            $worker = 'lib/' + $w.Name
        }
        $manifest = [ordered]@{ engine = 'maplibre'; version = $version; entry = $entry; worker = $worker; integrity = $p.Integrity }
    }
    $manifest | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $lib 'manifest.json') -Encoding utf8NoBOM

    foreach ($stack in 'Noto Sans Regular', 'Noto Sans Bold') {
        $d = Join-Path $fonts $stack
        New-Item -ItemType Directory $d -Force | Out-Null
        foreach ($range in '0-255', '256-511') {
            Invoke-WebRequest "https://tiles.openfreemap.org/fonts/$([uri]::EscapeDataString($stack))/$range.pbf" -OutFile (Join-Path $d "$range.pbf")
        }
    }
    Get-ChildItem $lib, $fonts -Recurse -File | ForEach-Object { '{0,10:N0}  {1}' -f $_.Length, $_.FullName.Substring($assets.Length + 1) }
}
finally { Remove-Item $tmp -Recurse -Force -ErrorAction SilentlyContinue }
