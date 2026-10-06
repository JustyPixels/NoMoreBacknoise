$ErrorActionPreference='Stop'
$root=Split-Path -Parent $PSScriptRoot
$version=(Get-Content (Join-Path $root 'version.json') -Raw | ConvertFrom-Json).version
$bundle=Join-Path $root "artifacts/NoMoreBacknoise-$version-win-x64"
$output=Join-Path $root 'artifacts/cable-check'
New-Item -ItemType Directory -Path $output -Force | Out-Null
$process=Start-Process -FilePath (Join-Path $bundle 'NoMoreBacknoise.exe') -ArgumentList '--verify-cable',('"'+$output+'"') -WindowStyle Hidden -PassThru
if(!$process.WaitForExit(60000)) { $process.Kill();throw 'Cable checks timed out' }
if($process.ExitCode -ne 0) {if(Test-Path (Join-Path $output 'cable-error.txt')) {Get-Content (Join-Path $output 'cable-error.txt')};throw 'Cable checks failed'}
Copy-Item -LiteralPath (Join-Path $output 'cable-validation.json') -Destination (Join-Path $root 'artifacts/cable-validation.json')
Write-Host 'Cable preparation, identity policies, reboot tracking and settings migration checks passed; no driver installed.'
