param([Parameter(Mandatory)][string]$Destination)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$env:CARGO_HOME = Join-Path $root '.cache/cargo'
New-Item -ItemType Directory -Force $Destination | Out-Null
Copy-Item -LiteralPath (Join-Path $root '.deps/rnnoise/COPYING') -Destination (Join-Path $Destination 'RNNoise-COPYING.txt')
foreach ($name in 'LICENSE-MIT','LICENSE-APACHE') { Copy-Item -LiteralPath (Join-Path $root ".deps/DeepFilterNet/$name") -Destination (Join-Path $Destination "DeepFilterNet-$name.txt") }
Copy-Item -LiteralPath (Join-Path $root 'third_party/wasapi/LICENSE.txt') -Destination (Join-Path $Destination 'wasapi-LICENSE.txt')
$metadata = & cargo metadata --locked --format-version 1 --filter-platform x86_64-pc-windows-msvc
if ($LASTEXITCODE) { throw 'Cannot enumerate dependency notices' }
$packages = ($metadata | ConvertFrom-Json -AsHashtable).packages | Sort-Object name,version
$index = @('Rust dependency inventory (including build dependencies). Full upstream notices follow.','')
foreach ($package in $packages) {
    $index += "$($package.name) $($package.version) | $($package.license) | $($package.repository)"
    if ($package.source -like 'registry+*') {
        $directory = Split-Path -Parent $package.manifest_path
        $licenseFiles = Get-ChildItem -LiteralPath $directory -File | Where-Object { $_.Name -match '^(LICENSE|COPYING|NOTICE)' }
        $output = Join-Path $Destination ("rust/" + $package.name + '-' + $package.version)
        New-Item -ItemType Directory -Force $output | Out-Null
        foreach ($file in $licenseFiles) { Copy-Item -LiteralPath $file.FullName -Destination $output }
        if (!$licenseFiles) { Set-Content -LiteralPath (Join-Path $output 'LICENSE-REFERENCE.txt') -Value ("License: $($package.license)`nSource: $($package.repository)") }
    }
}
Set-Content -LiteralPath (Join-Path $Destination 'RUST-INVENTORY.txt') -Value $index -Encoding UTF8
# NuGet runtime packages contain notices; publish does not automatically copy them.
$assets = Get-Content -LiteralPath (Join-Path $root 'app/obj/project.assets.json') -Raw | ConvertFrom-Json -AsHashtable
foreach ($entry in $assets.project.frameworks.Values.downloadDependencies | Where-Object {$_.name -match '^Microsoft\.(NETCore|WindowsDesktop)\.App\.Runtime\.win-x64$'}) {
    $runtimeVersion = $entry.version.Trim('[',']').Split(',')[0].Trim()
    $folderName = $entry.name + '-' + $runtimeVersion
    $output = Join-Path $Destination $folderName
    New-Item -ItemType Directory -Force $output | Out-Null
    $found = $false
    foreach ($folder in $assets.packageFolders.Keys) {
        $directory = Join-Path $folder ($entry.name.ToLowerInvariant() + '/' + $runtimeVersion)
        if (Test-Path -LiteralPath $directory) {
            Get-ChildItem -LiteralPath $directory -File | Where-Object { $_.Name -match '(LICENSE|THIRD.?PARTY.?NOTICES)' } | ForEach-Object { Copy-Item -LiteralPath $_.FullName -Destination $output; $found = $true }
        }
    }
    if (!$found) { throw "Missing runtime license notices: $($entry.name)" }
}
