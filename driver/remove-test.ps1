param([Parameter(Mandatory)][string]$Devcon,[switch]$IsolatedTestMachine)
$ErrorActionPreference='Stop'
if(!$IsolatedTestMachine){throw 'Run only in an isolated driver test machine.'}
& $Devcon remove 'Root\NoMoreBacknoise'
if($LASTEXITCODE){throw 'Driver device removal failed'}
Write-Host 'Remove the corresponding published OEM INF from the driver store with pnputil after verifying its provider and device identity.'
