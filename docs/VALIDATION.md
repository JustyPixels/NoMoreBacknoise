# Validation and release gates

v0.1.0 is a cable-based preview, not certification of the full plan's hardware matrix. Keep measured facts separate from planned tests. `artifacts/validation.json` is generated from the built host and includes synthetic benchmark context.

## Automated checks

- Rust DSP tests: finite silence, settings ranges/nonfinite rejection, mute over bypass/raw, bypass without gain, raw fallback, hold preserving endings, hysteresis and raw/model alignment.
- Audio drift reader: disconnected input clears stale queues and outputs silence.
- Named pipe: same-user server, version negotiation, device enumeration, unavailable endpoint rejection, clean shutdown. Optional live check covers telemetry, mute over bypass and RAM recording commands without saving audio.
- C++ bridge tests: actual transfer, producer ownership, producer disappearance/deadline silence, underruns, independent consumers, buffer wrap and bounded lag. This is user-mode validation only.
- Nine locale dictionaries have complete English-key coverage; render checks at 100/125/150/200%. Native-language review and real display/keyboard QA remain separate.
- Self-contained WPF publish, NSIS installer construction, portable archive, dependency notices and SHA256 checksums.

Synthetic harmonic/noise tests prove execution, output validity and rough processing timing. They do **not** prove speech intelligibility, nearby-speaker isolation, real hardware delay or perceptual quality. No personal audio is checked into source or release artifacts.

## Manual matrix — pending unless a dated result is recorded

| Test | Required cases |
|---|---|
| Suppression | Clean speech; fans, keyboard, TV, music, nearby speaker mixtures; quiet words, beginnings/endings, silence, clipping; listening plus licensed SI-SDR/STOI fixtures |
| Timing/load | ≤50 ms application target; separate hardware/cable delay; long sessions, CPU contention, drift, bounded queue latency and engine switches |
| Devices | USB, built-in, headset; 44.1/48/96 kHz; unplug/reconnect, permission denial, unavailable IDs, sleep/resume; Bluetooth best effort |
| Routing | VB-CABLE, Discord, OBS, meetings, games and multiple consumers; mute/bypass/fallback precedence; output and monitor loss |
| Failure chain | Inject model errors/allocation failures/nonfinite output; DF→RNNoise→raw with persistent unfiltered notice, Windows notification, Retry; host process failure |
| UI | All nine languages, Arabic RTL, fonts, keyboard access, 100–200% displays, minimum window size, global shortcut conflict handling |
| Packaging | Fresh Windows 10 22H2 and Windows 11 x64, installer/uninstall, portable extract/launch, settings persistence, startup/tray opt-ins, daily update behavior |
| Driver | WDK compile, isolated install/remove loops, verifier/HLK, producer crashes, underruns, multiple capture clients and production signing |

The development machine is Windows 10 22H2 x64 with .NET 10.0.303 and Rust 1.94.0. Local timing reports name the measured engine and warn when DF3 falls back. A Windows 11 machine, VB-CABLE installation and full device collection are not present in this workspace. Thus the complete routing/device/installer matrix and certified 50 ms bound remain unverified. Do not label milestone 2 or 3 complete based on source or ring tests.
