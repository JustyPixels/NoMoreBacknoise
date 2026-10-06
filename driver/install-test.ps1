param([Parameter(Mandatory)][string]$Devcon,[Parameter(Mandatory)][string]$Inf,[switch]$IsolatedTestMachine)
$ErrorActionPreference='Stop'
if(!$IsolatedTestMachine){throw 'Run only in an isolated driver test machine, with its approved signing configuration.'}
if(!(Test-Path -LiteralPath $Inf)){throw 'Built, test-signed INF required'}
# Requires an elevated terminal in the test VM. This script does not change signing policy.
& $Devcon install $Inf 'Root\NoMoreBacknoise'
if($LASTEXITCODE){throw 'Driver install failed'}
