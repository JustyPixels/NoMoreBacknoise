# NoMoreBacknoise++

Offline microphone noise suppression for Windows 10/11 x64. Built for gaming and calls, with adjustable RNNoise and DeepFilterNet3 processing, a speech-aware expander, and live before/after views. Your voice stays your voice: no pitch shifting, voice conversion or automatic leveling.

**Status: v0.2.0-preview.1 — VB-CABLE is the permanent routing solution.** A shared setup assistant prepares the official standard cable or reuses an existing installation. Custom driver development has been removed; earlier work remains in Git history and previous releases. GPU acceleration and enrolled-voice isolation are unavailable.

## Download and start

Get the installer or portable ZIP from [GitHub Releases](https://github.com/JustyPixels/NoMoreBacknoise/releases). Both include the .NET runtime, models and third-party notices. No account, AI subscription or network connection is needed for processing.

1. Open **VB-CABLE setup** on first use or in Settings. Both downloads include the intact official Pack45 ZIP (Windows driver **3.3.1.7**). Read the changes and consent, then choose offline preparation or download. Only the official installer requests administrator access. Restart Windows yourself and reopen the assistant to verify. You can postpone and use Preview only.
2. Open NoMoreBacknoise++, choose your physical microphone, and select **CABLE Input** as the cleaned output.
3. Click **Start**. Choose **CABLE Output** as the microphone in Discord, OBS, your meeting app or game. Windows defaults are left alone.
4. Begin with **Automatic / Balanced Aggressive**, then adjust strength while watching the raw (amber) and cleaned (mint) signals. Use headphones for optional listening comparisons.

See the [setup guide](docs/SETUP.md) and [troubleshooting](docs/TROUBLESHOOTING.md). Preview-only mode works without a cable, but does not provide a microphone to other apps.

## Features

- Local RNNoise and DeepFilterNet3 with upstream preprocessing and persistent model state.
- Natural, Balanced Aggressive, Maximum Silence and named custom profiles.
- Hysteresis, hold and release protect quiet word endings; suppression floor and gain are adjustable. Clipping protection limits output.
- Aligned raw/cleaned waveforms, spectrum, peak/RMS, speech activity, processing time and an application-delay estimate at about 30 updates per second.
- Optional headphone A/B monitoring; mute takes precedence over bypass and monitoring.
- Explicit test recordings capped at 60 seconds in memory. Export produces separate raw and cleaned WAV files. No audio is saved automatically.
- Optional tray controls and assigned global mute/bypass shortcuts. Startup, auto-processing and update checks are opt-in; close-to-tray is the initial default.
- English, Simplified Chinese, Spanish, Hindi, Arabic (RTL), Brazilian Portuguese, French, Russian and Japanese, with English fallback.
- Versioned, same-user-only local named-pipe control. Separate UI, processing, capture and output workers, bounded queues and clock-drift correction.

Aggressive settings can remove quiet syllables or distort speech. Nearby voices, TV and music are difficult cases; complete noise elimination is not promised. DeepFilterNet is selected automatically only after a startup processing benchmark passes. Delay estimates include application buffering and processing, **not external cable or hardware delay**. The 50 ms target is a goal to verify on your devices, not a universal guarantee.

## Development

See [BUILD.md](docs/BUILD.md), [protocol](docs/PROTOCOL.md), [validation](docs/VALIDATION.md), [contributing](CONTRIBUTING.md), and [third-party notices](THIRD_PARTY_NOTICES.md). Core layout:

| Folder | Purpose |
|---|---|
| `app` | .NET 10 WPF interface, profiles, tray, shortcuts, localization |
| `host` | Rust WASAPI routing, DSP, telemetry and in-memory recordings |
| `dependencies` | Approved VB-CABLE origin, version, checksum and interactive install policy |
| `scripts` | Pinned dependency bootstrap, verification and packaging |

## Preview limits

Review the validation report before treating this as a production release. A working cable is never automatically reinstalled, updated or removed. Disabled/incomplete devices get specific guidance. The app compares Windows default devices across setup, offers Sound settings for manual restoration and never changes defaults itself. The uninstaller removes only this app; VB-CABLE stays installed. Preview-only mode remains available when setup is postponed.

No enrolled-voice flow is shown. The [bounded feasibility review](docs/VOICE_ISOLATION.md) explains why no model has passed the admission criteria. Optional DirectML remains unavailable until a compatible streaming export and timing checks pass; the current CPU backend uses Tract.

## License

App source: [MIT](LICENSE), copyright **JustPixels**. Dependencies and weights retain their own licenses; see [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md). **VB-CABLE is made by VB-Audio Software (V. Burel / J. S. Loezic) and is donationware, not MIT.** If useful or professionally used, please [donate/pay for its license](https://vb-audio.com/Services/licensing.htm). Organizations where users cannot see/pay for it require licenses. Only the standard cable is included; A+B and C+D are excluded. Its [official distribution conditions](https://vb-audio.com/Services/licensing.htm) apply; the vendor README is unchanged.
