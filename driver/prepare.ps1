$ErrorActionPreference='Stop'
$root=Split-Path -Parent $PSScriptRoot
$source=Join-Path $root '.deps/Windows-driver-samples'
if(!(Test-Path -LiteralPath $source)) {
    & git clone --filter=blob:none --no-checkout https://github.com/microsoft/Windows-driver-samples.git $source
    if($LASTEXITCODE){throw 'SYSVAD clone failed'}
    & git -C $source sparse-checkout set audio/sysvad
    & git -C $source checkout 2dc3fd3a0cc84a2933f2194e7ec0871584979071
}
if((& git -C $source rev-parse HEAD) -ne '2dc3fd3a0cc84a2933f2194e7ec0871584979071'){throw 'Unexpected SYSVAD revision'}
$generated=Join-Path $root 'artifacts/driver-source'
New-Item -ItemType Directory -Force $generated | Out-Null
Copy-Item -LiteralPath (Join-Path $source 'audio/sysvad') -Destination $generated -Recurse -Force
$sysvad=Join-Path $generated 'sysvad'
$common=Join-Path $sysvad 'EndpointsCommon'
Copy-Item -LiteralPath "$PSScriptRoot/AudioBridgeCore.h","$PSScriptRoot/NoMoreBridge.h" -Destination $common
$cpp=Join-Path $common 'minwavertstream.cpp';$code=Get-Content -LiteralPath $cpp -Raw
$code=$code.Replace('#include "minwavertstream.h"',"#include `"minwavertstream.h`"`n#include `"NoMoreBridge.h`"")
$code=$code.Replace('m_ToneGenerator.GenerateSine(m_pDmaBuffer + bufferOffset, runWrite);','NmbCapture(m_NmbReader, m_pDmaBuffer + bufferOffset, runWrite, m_pWfExt);')
$code=$code.Replace('m_SaveData.WriteData(m_pDmaBuffer + bufferOffset, runWrite);','NmbRender(this, m_pDmaBuffer + bufferOffset, runWrite, m_pWfExt);')
$code=$code.Replace('case KSSTATE_STOP:',"case KSSTATE_STOP:`n            NmbProducerLost(this);")
$code=$code.Replace('case KSSTATE_PAUSE:',"case KSSTATE_PAUSE:`n            NmbProducerLost(this);")
$code=$code.Replace('DPF_ENTER(("[CMiniportWaveRTStream::~CMiniportWaveRTStream]"));','NmbProducerLost(this);')
$code=$code.Replace('else if (!g_DoNotCreateDataFiles)','else if (false)') # Never save render audio to files.
Set-Content -LiteralPath $cpp -Value $code -Encoding UTF8
$header=Join-Path $common 'minwavertstream.h';$code=Get-Content -LiteralPath $header -Raw
$code=$code.Replace('#include "tonegenerator.h"',"#include `"tonegenerator.h`"`n#include `"AudioBridgeCore.h`"")
$code=$code.Replace('BOOLEAN                     m_bCapture;',"BOOLEAN                     m_bCapture;`n    nmb::Reader m_NmbReader = {};")
Set-Content -LiteralPath $header -Value $code -Encoding UTF8
$pairs=Join-Path $sysvad 'TabletAudioSample/minipairs.h';$code=Get-Content -LiteralPath $pairs -Raw
foreach($endpoint in 'SpeakerHp','Hdmi','Spdif','MicArray1','MicArray2','MicArray3'){$code=$code.Replace("    &$($endpoint)Miniports,",'')}
Set-Content -LiteralPath $pairs -Value $code -Encoding UTF8
$inf=Join-Path $sysvad 'TabletAudioSample/ComponentizedAudioSample.inx';$code=Get-Content -LiteralPath $inf -Raw
$code=$code.Replace('Root\sysvad_ComponentizedAudioSample','Root\NoMoreBacknoise')
$code=$code.Replace('TODO-Set-Provider','JustPixels').Replace('TODO-Set-Manufacturer','JustPixels').Replace('TODO-Set-Copyright','Copyright 2026 JustPixels; Microsoft sample portions retain their license')
$code=$code.Replace('SYSVAD Wave Speaker"','NoMoreBacknoise++ Feed"').Replace('SYSVAD Topology Speaker"','NoMoreBacknoise++ Feed"')
$code=$code.Replace('SYSVAD Wave Microphone Headphone"','NoMoreBacknoise++ Microphone"').Replace('SYSVAD Topology Microphone Headphone"','NoMoreBacknoise++ Microphone"')
$code=$code.Replace('Virtual Audio Device (WDM) - Tablet Sample','NoMoreBacknoise++ Virtual Audio')
Set-Content -LiteralPath $inf -Value $code -Encoding UTF8
Copy-Item -LiteralPath (Join-Path $source 'LICENSE') -Destination (Join-Path $generated 'SYSVAD-LICENSE.txt')
$adapter=Join-Path $sysvad 'adapter.cpp';$code=Get-Content -LiteralPath $adapter -Raw
$code=$code.Replace('extern "C" DRIVER_INITIALIZE DriverEntry;',"void NmbInitialize();`nextern `"C`" DRIVER_INITIALIZE DriverEntry;")
$code=$code.Replace('DPF(D_TERSE, ("[DriverEntry]"));',"NmbInitialize();`n    DPF(D_TERSE, (`"[DriverEntry]`"));")
Set-Content -LiteralPath $adapter -Value $code -Encoding UTF8
Write-Host "Experimental SYSVAD integration prepared in $sysvad. WDK build and isolated kernel tests are still required."
