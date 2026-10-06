# Build from source

Windows x64, .NET SDK 10, Rust 1.94.0 MSVC toolchain, Git, Visual Studio C++ Build Tools and Windows SDK. The ordinary app build does not require WDK. Run in PowerShell 7 from the repository root:

```powershell
./scripts/bootstrap.ps1
$env:CARGO_HOME = Join-Path $PWD '.cache/cargo'
cargo test --locked -p nmb-host
cargo build --locked -p nmb-host --release
dotnet build app/NoMoreBacknoise.csproj -c Release
./scripts/package.ps1 -SkipBuild
./scripts/verify.ps1
./scripts/verify-cable.ps1
```

For initial dependencies and packaging runtime/tool downloads, internet access is needed. Normal processing thereafter is offline. Bootstrap pins RNNoise/DF source commits and verifies the DF3 model archive. Packaging checks the pinned NSIS ZIP SHA256, publishes a self-contained .NET runtime and collects dependency notices. Output: installer, portable ZIP, SHA256SUMS.txt under artifacts. Cache and artifacts are ignored by Git.

For development, copy `target/release/nmb-host.exe` next to the built UI executable, with the localization folder. Run the UI there. `nmb-host --self-test` measures synthetic processing; `--devices` enumerates endpoints. `--process-file INPUT.wav OUTPUT.wav [rnnoise|deepfilter]` processes a mono 48 kHz fixture with model delay compensation and refuses to overwrite output.

`./scripts/verify.ps1 -Live` additionally opens the first physical microphone briefly, in muted preview mode, to verify telemetry/mute precedence and RAM recording controls. It saves no audio. Use only on your own authorized test device.

UI render checks (no microphone, first-launch or settings writes):

```powershell
./artifacts/NoMoreBacknoise-0.2.0-preview.1-win-x64/NoMoreBacknoise.exe --verify-ui D:\NoMoreBacknoise\artifacts\ui-check
```

This writes nine locales at four render scales for review. It is not a substitute for native UI, keyboard or real monitor-DPI tests.

## Cable packaging and release

No custom driver or SYSVAD dependency is built. `scripts/cable-package.ps1` obtains the full ZIP pinned in `dependencies/vb-cable.json`, validates SHA256 and Authenticode signer identities, and includes it intact with the original README. No build/check command installs a driver. For offline builds, prepopulate `.cache/vb-cable/VBCABLE_Driver_Pack45.zip` and the other dependency/tool/runtime caches. For development, prepare the dependency folder beside the UI with this script and copy the host there.

`version.json` controls artifact names and installer version; keep app/host versions in sync. `--verify-cable` tests settings migration, identity policies, versions, reboot/default comparisons, hashes, tampered signatures and offline full extraction. It never executes the installer. Read-only inventory results contain counts/version, not personal device IDs. Render checks also cover the setup assistant in all nine locales at four scales. Interactive and native-language review remain separate.

Windows CI builds/tests/packages the app; a tag matching `version.json` publishes a prerelease only after automated checks pass. Isolated Windows 10/11 driver installation tests remain a separate manual gate. The NoMoreBacknoise++ uninstaller must preserve VB-CABLE.

`scripts/test-cable-routing.ps1 -Live` is an explicit local routing smoke test requiring one physical microphone and canonical standard VB-CABLE endpoints. It sends live processed audio to the cable and opens two local capture consumers. Disconnect voice calls/servers first; do not run it if other apps could transmit the cable. It tests both engines, mute/bypass and RAM recordings; it saves aggregate metrics only, never audio. This manual check is deliberately excluded from CI.
