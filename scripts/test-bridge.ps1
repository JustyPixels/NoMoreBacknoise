$ErrorActionPreference='Stop'
$root=Split-Path -Parent $PSScriptRoot
$finder=Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe'
$installation=& $finder -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
if(!$installation){throw 'Visual C++ toolchain required'}
$tools=Get-ChildItem -LiteralPath (Join-Path $installation 'VC/Tools/MSVC') -Directory | Sort-Object Name -Descending | Select-Object -First 1
$sdk=Join-Path ${env:ProgramFiles(x86)} 'Windows Kits/10'
$sdkVersion=Get-ChildItem -LiteralPath (Join-Path $sdk 'Include') -Directory | Sort-Object Name -Descending | Where-Object {Test-Path -LiteralPath (Join-Path $_.FullName 'ucrt')} | Select-Object -First 1
$env:INCLUDE="$($tools.FullName)/include;$($sdkVersion.FullName)/ucrt;$($sdkVersion.FullName)/shared;$($sdkVersion.FullName)/um"
$env:LIB="$($tools.FullName)/lib/x64;$sdk/Lib/$($sdkVersion.Name)/ucrt/x64;$sdk/Lib/$($sdkVersion.Name)/um/x64"
Set-Location -LiteralPath $root
New-Item -ItemType Directory -Force artifacts | Out-Null
& (Join-Path $tools.FullName 'bin/Hostx64/x64/cl.exe') /nologo /std:c++17 /EHsc /MT driver/tests.cpp /Foartifacts/bridge-tests.obj /Feartifacts/bridge-tests.exe
if($LASTEXITCODE){throw 'Bridge test compilation failed'}
& ./artifacts/bridge-tests.exe
if($LASTEXITCODE){throw 'Bridge behavior test failed'}
