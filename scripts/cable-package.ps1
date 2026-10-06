param([Parameter(Mandatory)][string]$Destination)
$ErrorActionPreference='Stop'
$root=Split-Path -Parent $PSScriptRoot
$manifestPath=Join-Path $root 'dependencies/vb-cable.json'
$manifest=Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
if($manifest.schemaVersion -ne 1 -or $manifest.installMode -ne 'interactive' -or $manifest.silentInstallationValidated) {throw 'Unapproved cable installation mode'}
if($manifest.url -ne ('https://download.vb-audio.com/Download_CABLE/'+$manifest.package)) {throw 'Unapproved package origin'}
$cache=Join-Path $root '.cache/vb-cable'
New-Item -ItemType Directory -Path $cache -Force | Out-Null
$archive=Join-Path $cache $manifest.package
if(!(Test-Path -LiteralPath $archive)) {Invoke-WebRequest -Uri $manifest.url -OutFile $archive}
if((Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash -ne $manifest.sha256) {throw 'VB-CABLE checksum mismatch. Remove the invalid cached ZIP and retry.'}
$extracted=Join-Path $cache ('verify-'+[guid]::NewGuid().ToString('N'))
Expand-Archive -LiteralPath $archive -DestinationPath $extracted
foreach($signed in @(@{File=$manifest.installer;Signer=$manifest.installerSigner},@{File=$manifest.catalog;Signer=$manifest.catalogSigner})) {
    $signature=Get-AuthenticodeSignature -LiteralPath (Join-Path $extracted $signed.File)
    if($signature.Status -ne 'Valid' -or $signature.SignerCertificate.GetNameInfo([Security.Cryptography.X509Certificates.X509NameType]::SimpleName,$false) -ne $signed.Signer) {throw ('Invalid VB-CABLE signature: '+$signed.File)}
}
New-Item -ItemType Directory -Path $Destination -Force | Out-Null
Copy-Item -LiteralPath $manifestPath -Destination $Destination
Copy-Item -LiteralPath $archive -Destination $Destination
Copy-Item -LiteralPath (Join-Path $extracted 'readme.txt') -Destination (Join-Path $Destination 'VB-CABLE-readme.txt')
Write-Host 'Official, intact VB-CABLE ZIP verified and included. No driver was installed.'
