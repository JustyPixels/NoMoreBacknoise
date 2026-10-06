# Troubleshooting

- **Other apps hear raw voice:** select CABLE Output in those apps, with CABLE Input as this app's output. Preview-only mode cannot create a recording endpoint.
- **No signal/device:** check Windows microphone access yourself, the physical connection and app selection. Refresh Devices retains the saved input identity. Reconnect the same microphone; another microphone is never selected silently. Retry after an error. Check output selection separately.
- **Feedback/echo:** use headphones and choose a headphone output distinct from the cable. Turn monitoring off if speakers feed the microphone. Avoid capturing the physical mic and cable simultaneously in OBS.
- **Quiet words disappear:** use Natural, lower the speech threshold/strength, increase hold/release or disable the expander. Maximum Silence trades speech detail for quieter gaps.
- **Clipping:** lower microphone hardware/input gain. Output clipping protection cannot repair already clipped input. Manual output gain is not automatic leveling.
- **Delay/dropouts:** try RNNoise, close CPU-heavy apps, and inspect processing/delay values. The estimate excludes device/cable latency. Bluetooth may add large external delay and is best effort. Shared-mode Windows conversion handles devices running at other sample rates.
- **Fallback:** DeepFilterNet errors fall back to RNNoise; loss of both engines forwards raw audio and visibly marks an unfiltered microphone. Mute still overrides raw fallback. Retry explicitly to restore filtering. If the host exits, the cable receives no new audio; this app does not switch Windows defaults.
- **Disconnect/sleep:** bounded stale buffers are discarded and the selected endpoint is reopened. No automatic switch to a different physical device. Some hardware creates a new endpoint ID after reinstall; select it explicitly.
- **Portable doesn't launch:** extract all files, including runtime, host and localization. Do not run the EXE inside the ZIP.
- **Shortcut unavailable:** choose a different Ctrl/Alt/Shift combination. Shortcuts are unassigned by default.
- **Uninstall:** quit the app from its tray before uninstalling; per-user settings are retained. Remove `%LOCALAPPDATA%\NoMoreBacknoise` manually if desired. VB-CABLE is managed by its publisher and remains installed.

Report the app version, Windows version, engine, device/sample rate, approximate timing and reproduction steps. Do not upload personal recordings, credentials, voice features or complete local telemetry.
