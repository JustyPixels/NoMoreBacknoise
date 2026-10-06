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
```

For initial dependencies and packaging runtime/tool downloads, internet access is needed. Normal processing thereafter is offline. Bootstrap pins RNNoise/DF source commits and verifies the DF3 model archive. Packaging checks the pinned NSIS ZIP SHA256, publishes a self-contained .NET runtime and collects dependency notices. Output: installer, portable ZIP, SHA256SUMS.txt under artifacts. Cache and artifacts are ignored by Git.

For development, copy `target/release/nmb-host.exe` next to the built UI executable, with the localization folder. Run the UI there. `nmb-host --self-test` measures synthetic processing; `--devices` enumerates endpoints. `--process-file INPUT.wav OUTPUT.wav [rnnoise|deepfilter]` processes a mono 48 kHz fixture with model delay compensation and refuses to overwrite output.

`./scripts/verify.ps1 -Live` additionally opens the first physical microphone briefly, in muted preview mode, to verify telemetry/mute precedence and RAM recording controls. It saves no audio. Use only on your own authorized test device.

UI render checks (no microphone, first-launch or settings writes):

```powershell
./artifacts/NoMoreBacknoise-0.1.0-win-x64/NoMoreBacknoise.exe --verify-ui D:\NoMoreBacknoise\artifacts\ui-check
```

This writes nine locales at four render scales for review. It is not a substitute for native UI, keyboard or real monitor-DPI tests.

## Experimental driver

See [driver/README.md](../driver/README.md). WDK and kernel tests are separate prerequisites. Never add a driver binary to the consumer installer before isolated tests, signing and distribution checks pass. GitHub Windows CI builds/tests/packages the app; a `v*` tag publishes a **prerelease** with checksums only after checks succeed.
