param([switch]$SkipBuild)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Set-Location -LiteralPath $root
$env:CARGO_HOME = Join-Path $root '.cache/cargo'
if (!$SkipBuild) {
    & "$PSScriptRoot/bootstrap.ps1"
    & cargo test --locked -p nmb-host --release
    if ($LASTEXITCODE) { throw 'Audio tests failed' }
    & cargo build --locked -p nmb-host --release
    if ($LASTEXITCODE) { throw 'Audio build failed' }
}
$bundle = Join-Path $root 'artifacts/NoMoreBacknoise-0.1.0-win-x64'
& dotnet publish app/NoMoreBacknoise.csproj -c Release -r win-x64 --self-contained true -o $bundle
if ($LASTEXITCODE) { throw 'Interface build failed' }
Copy-Item -LiteralPath target/release/nmb-host.exe -Destination $bundle
Copy-Item -LiteralPath LICENSE -Destination $bundle
Copy-Item -LiteralPath README.md -Destination $bundle
Copy-Item -LiteralPath docs -Destination $bundle -Recurse -Force
& "$PSScriptRoot/notices.ps1" -Destination (Join-Path $bundle 'third-party')
Copy-Item -LiteralPath THIRD_PARTY_NOTICES.md -Destination $bundle
$nsis = Join-Path $root '.cache/nsis/nsis-3.11/makensis.exe'
if (!(Test-Path -LiteralPath $nsis)) {
    $archive = Join-Path $root '.cache/nsis-3.11-mirror.zip'
    Invoke-WebRequest 'https://github.com/tauri-apps/binary-releases/releases/download/nsis-3.11/nsis-3.11.zip' -OutFile $archive
    if ((Get-FileHash -LiteralPath $archive).Hash -ne 'C7D27F780DDB6CFFB4730138CD1591E841F4B7EDB155856901CDF5F214394FA1') { throw 'Installer compiler checksum mismatch' }
    Expand-Archive -LiteralPath $archive -DestinationPath .cache/nsis -Force
}
$uninstallManifest = Join-Path $root 'artifacts/uninstall-files.nsh'
$uninstallLines = @(Get-ChildItem -LiteralPath $bundle -File -Recurse -Force | ForEach-Object {
    $relative = [IO.Path]::GetRelativePath($bundle,$_.FullName).Replace('$','$$')
    '    Delete "$INSTDIR\{0}"' -f $relative
})
$uninstallLines += @(Get-ChildItem -LiteralPath $bundle -Directory -Recurse -Force | Sort-Object { $_.FullName.Length } -Descending | ForEach-Object {
    $relative = [IO.Path]::GetRelativePath($bundle,$_.FullName).Replace('$','$$')
    '    RMDir "$INSTDIR\{0}"' -f $relative
})
[IO.File]::WriteAllLines($uninstallManifest,$uninstallLines,[Text.UTF8Encoding]::new($false))
& $nsis '/V2' ("/DBUNDLE=" + $bundle) ("/DUNINSTALL_MANIFEST=" + $uninstallManifest) (Join-Path $root 'installer/NoMoreBacknoise.nsi')
if ($LASTEXITCODE) { throw 'Installer build failed' }
Compress-Archive -Path (Join-Path $bundle '*') -DestinationPath artifacts/NoMoreBacknoise-0.1.0-portable-win-x64.zip -Force
$files = Get-ChildItem artifacts -File | Where-Object { $_.Name -like 'NoMoreBacknoise-0.1.0-*' -and $_.Extension -in '.exe','.zip' }
$lines = $files | ForEach-Object { (Get-FileHash -LiteralPath $_.FullName).Hash.ToLowerInvariant() + '  ' + $_.Name }
[IO.File]::WriteAllLines((Join-Path $root 'artifacts/SHA256SUMS.txt'), $lines, [Text.Encoding]::ASCII)
Write-Host 'Installer, portable ZIP and SHA256SUMS.txt are ready in artifacts.'
