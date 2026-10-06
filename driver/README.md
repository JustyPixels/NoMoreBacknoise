# Experimental branded virtual microphone — source only

This milestone contains an allocation-free C++ integer audio bridge plus a reproducible integration into Microsoft's pinned SYSVAD framework. **It is not a tested or production-signed Windows driver. No kernel binary is shipped with v0.1.0.**

`AudioBridgeCore.h` provides a bounded PCM ring, one producer, independent capture-client cursors, generation changes on producer loss, 50 ms inactivity silence, underflow silence and a 20 ms per-client lag ceiling. `NoMoreBridge.h` wraps it with a kernel spin lock and PCM conversion. No inference, floating-point processing, audio saving or network access is in the kernel path.

`prepare.ps1` checks Microsoft commit `2dc3fd3a0cc84a2933f2194e7ec0871584979071`, copies its SYSVAD source to artifacts/driver-source, routes WaveRT render/capture buffers through the bridge, disables sample render-file saving, retains only speaker-feed and microphone endpoint pairs and applies branded names/root device identity. Microsoft sample licenses are retained. The sample's remaining descriptors/INF extension features require WDK/HLK review and cleanup before installation. Only PCM 48 kHz, mono/stereo, 16/24/32-bit is transferred; unsupported sample formats produce silence. The upstream INF currently targets Windows 11 build 22621+, so **Windows 10 driver compatibility remains pending**. The cable-based app supports Windows 10 independently.

## Build and test

Run `./driver/prepare.ps1`, then open the generated SYSVAD solution in Visual Studio with a matching WDK. Build the TabletAudioSample and required common projects for Release x64. Driver Verifier, kernel debugging, an isolated test VM/physical test machine and approved test-signing setup are required. No script changes the host's signing or security policy.

`tests.cpp` exercises the actual shared ring implementation in user mode. In a Visual Studio developer PowerShell:

```powershell
cl /std:c++17 /EHsc driver/tests.cpp /Fe:artifacts/bridge-tests.exe
./artifacts/bridge-tests.exe
```

`install-test.ps1 -Devcon <WDK-devcon.exe> -Inf <built-signed.inf> -IsolatedTestMachine` creates the root test device. `remove-test.ps1` removes that device. Verify the published OEM INF's provider before removing it from the driver store. Repeated install/remove, producer crashes, missing feed, underruns, multiple capture clients, sleep/resume and buffer-clock behavior still require **actual kernel tests**; passing the user-mode ring tests does not certify those behaviors.

Before consumer distribution: replace all sample identifiers/features as needed, review locking/IRQL and endpoint formats, pass Windows 10/11 and HLK validation, verify signing requirements, obtain production signing, package installation/removal and integrate endpoint routing. See [Microsoft SYSVAD documentation](https://learn.microsoft.com/en-us/samples/microsoft/windows-driver-samples/sysvad-virtual-audio-device-driver-sample/) and [kernel driver signing policy](https://learn.microsoft.com/en-us/windows-hardware/drivers/install/kernel-mode-code-signing-policy--windows-vista-and-later-). Signing secrets must remain outside the repository.
