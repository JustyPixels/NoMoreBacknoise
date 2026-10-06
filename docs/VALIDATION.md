# Validation and release gates

v0.2.0-preview.1 uses VB-CABLE permanently. This preview does not certify the full hardware/installation matrix. Keep measured facts separate from planned tests. `validation.json` contains built-host synthetic timing; `cable-validation.json` contains assistant policy, package validation and read-only device inventory results.

## Automated checks

- Rust DSP tests: finite silence, settings ranges/nonfinite rejection, mute over bypass/raw, bypass without gain, raw fallback, hold preserving endings, hysteresis and raw/model alignment.
- Audio drift reader: disconnected input clears stale queues and outputs silence.
- Named pipe: same-user server, version negotiation, device enumeration, unavailable endpoint rejection, clean shutdown. Optional live check covers telemetry, mute over bypass and RAM recording commands without saving audio.
- Cable assistant: v1→v2 settings migration preserves profiles/device IDs/hotkeys/startup choices and leaves new network checks off; missing/renamed/old/new/disabled/partial classification policies; standard hardware/provider identity excludes A+B; reboot/default comparison; official package discovery; missing/corrupt ZIP rejection; offline complete extraction, original README equality, cached signature validation, wrong-signer and tampered-executable rejection. Classification fixtures do not replace actual driver-state tests.
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
| Cable setup | Isolated Windows 10/11 install/restart/remove, actual UAC cancellation, interrupted/partial installation, renamed/disabled endpoints, offline/no trust cache, updates and preserving the cable on app uninstall |

The development machine is Windows 10 22H2 x64 with .NET 10.0.303 and Rust 1.94.0. The protected local pipe and headset passed muted-preview telemetry/control/RAM recording checks on 2026-10-06. Package validation and migration checks also passed without executing the official installer or changing a driver. Updated routing results are recorded below when measured. A Windows 11/isolated installation environment is unavailable. Human speech/noise quality, actual UAC cancellation, fresh installation/reboot, TeamSpeak/Discord local capture and native-language/interactive UI review remain pending unless explicitly recorded. Synthetic timings exclude cable/hardware delay; no certified 50 ms total bound is claimed.

## Measured on 2026-10-06

The user's installed standard VB-CABLE 3.3.1.7 was identified through PnP ancestry/provider metadata: two active render endpoints and one active capture endpoint. The assistant classified it as Ready without launching a driver installer.

`scripts/test-cable-routing.ps1 -Live` passed physical headset → suppression → CABLE Input → CABLE Output with **two simultaneous WASAPI shared-mode consumers**. RNNoise and DeepFilterNet3 were both observed running; input and cable signal presence were detected. Mute over bypass and cable silence passed, bypass samples equalled aligned raw samples, and bounded RAM recording commands passed. No audio was exported. Signal presence does not validate normal/quiet speech quality or keyboard/fan removal.

| Live smoke result | RNNoise | DeepFilterNet3 |
|---|---:|---:|
| Maximum processing time per 10 ms frame | 1.2614 ms | 1.9965 ms |
| Maximum application-delay estimate during the sampled interval | 74.4735 ms | 95.5849 ms |

These delay estimates include reported model/application/WASAPI buffers and queues; they are not an end-to-end physical measurement. External VB-CABLE and hardware delay is excluded. **The 50 ms application target was not met in this sampled run** (a release build was also running on the machine). Keep estimates visible; timing optimization/reference-CPU idle tests remain pending.

Dashboard and assistant rendered in nine languages at 100/125/150/200%. Portuguese and Arabic samples were inspected; Arabic uses right-to-left layout. Human translation and interactive keyboard/display tests remain pending. Native app screen capture failed; the user stopped Computer Use with Escape and explicitly took responsibility for real TeamSpeak/Discord tests. Those internal app tests are deferred to the user and are **not** claimed passed.

The app installer and uninstaller passed an app-only test into a temporary workspace folder on Windows 10 22H2, with no prior app registration. The installed official ZIP matched the pinned hash. Uninstall preserved a synthetic unrelated file, and the existing VB-CABLE remained Ready at version 3.3.1.7. The vendor installer was never executed. This is not a clean-machine/isolated driver installation test. Sanitized aggregate local results are retained in [validation/windows10-2026-10-06.json](../validation/windows10-2026-10-06.json); CI release reports are separate.
