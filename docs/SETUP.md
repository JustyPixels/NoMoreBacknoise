# Setup

Windows 10 22H2 or Windows 11, x64. Processing is offline on CPU. Extract the entire portable ZIP into a writable folder, or run the per-user installer. Keep `nmb-host.exe`, localization and runtime files with `NoMoreBacknoise.exe`.

## Cable routing

Open **VB-CABLE setup** on first use or in Settings. The installer also offers an unchecked finish-page option to open the same assistant. Both distributions contain the untouched official standard Pack45 ZIP, Windows driver 3.3.1.7. A working existing cable is reused, even with renamed endpoints. Disabled devices get activation guidance; partial installations get restart/Device Manager guidance rather than another installation.

Read the product, vendor, donationware license and expected changes, then explicitly consent to opening the official installer. **Prepare included package (offline)** needs no internet. Download and official-news buttons explain their VB-Audio internet access; no audio is sent. SHA256 and Authenticode signatures are checked before running. Pack45 uses the official installer window without undocumented silent commands. Only the vendor installer is elevated; the app stays unprivileged. UAC cancellation leaves setup deferred. Invalid packages are never executed; retry, use the included verified package or visit the official site.

Opening the assistant does not stop an active session. Choosing to launch a verified installer stops processing and discards that session's in-memory test, as explained before consent; export any test you need to keep first.

Install in the official window, then restart Windows yourself. The assistant records pending reboot and only marks the cable available after rechecking. Windows may change default devices during driver installation; the assistant compares all six playback/recording roles with the saved pre-installation IDs and offers Sound settings to restore your choices manually. It never changes defaults or restarts Windows. You can dismiss the comparison after reviewing it.

VB-CABLE is **VB-Audio Software donationware**, not covered by MIT. Read the unchanged README, [license/distribution rules](https://vb-audio.com/Services/licensing.htm), [official site](https://vb-audio.com/Cable/) and [donation page](https://vb-audio.com/Services/licensing.htm). Please donate/pay if useful or professionally used; organizations where users cannot see/pay need licenses. A+B and C+D are not included. Uninstalling NoMoreBacknoise++ preserves the shared VB-CABLE driver.

Physical microphone → NoMoreBacknoise++ → **CABLE Input** (playback/render device) → **CABLE Output** (recording/capture device) → call/game/OBS.

The names seem reversed because they describe entry/exit from the cable. Select CABLE Input in this app. Select CABLE Output as the microphone in consuming apps. Do not select CABLE Output as this app's source: that creates a feedback loop. Leave Windows defaults as you prefer.

- Discord: User Settings → Voice & Video → Input Device → CABLE Output. Avoid stacking multiple aggressive suppressors; compare Discord's processing with it disabled.
- OBS: add Audio Input Capture → CABLE Output. Avoid capturing your physical mic a second time.
- TeamSpeak: choose CABLE Output in capture/input settings; use the local microphone test without joining a server/call.
- Meeting software/games: choose CABLE Output in the app's microphone selector. Multiple apps can share the cable recording endpoint if their settings permit shared access.

## First launch and tuning

Four independent choices are shown: Windows startup (off), processing on launch (off), close to tray (on), update notifications (off). Processing starts only when you click Start, unless you explicitly enable automatic processing. Closing to tray keeps an active session running; use tray Quit to release the microphone.

Automatic benchmarks DF3 at startup and falls back to RNNoise if timing/model delay exceeds its budget. Natural favors speech, Balanced Aggressive is the starting point, Maximum Silence closes gaps harder. Strength affects RNNoise wet/dry mixing or DF3's attenuation limit. Advanced attenuation applies only to DF3. Speech sensitivity is a probability threshold, not speaker recognition. Lower it to retain quiet speech; increase hold/release if word endings disappear. Gain is manual; automatic leveling is excluded.

## Compare and record

Monitoring begins off. Select a headphone output, then enable Monitor. Listen Raw and Listen Clean switch the monitored signal. Monitoring is never sent back to the cable. Mute silences both output and monitoring; bypass forwards your unfiltered voice and is visibly labelled.

Record Test explicitly captures aligned raw/cleaned signals in RAM for up to 60 seconds. Enable headphone monitoring before recording to use **Play raw test / Play cleaned test** for an in-app comparison after stopping the recording. Switching those buttons during playback keeps the same position. Listen Raw/Clean returns to live monitoring. Playback only goes to your headphones; your live cleaned microphone continues feeding the cable. Stop Recording, then Export WAV to save both. Start another test replaces the earlier test. Stopping processing, changing routes/engine or quitting discards that session's recording, so export first. WAV export uses a new base name and never overwrites previous recordings.

Profiles contain only processing settings. Import/export never includes audio, device identities or voice features. User settings live in `%LOCALAPPDATA%\NoMoreBacknoise\settings.json`. Portable builds also use these per-user settings. Deleting that file resets the first-launch choices; disable Windows startup before resetting.

Updates are optional. App checks contact GitHub on launch and at most daily; installation stays manual. **Also check VB-CABLE updates** is a separate opt-in, off for new and migrated settings, and contacts the official VB-Audio product page at most daily. New official packages not yet approved are announced with the official-site path; integrated setup only uses the approved package shipped with this app. Updating an existing cable is a manual vendor procedure, potentially requiring removal, restart, installation and another restart; this assistant never performs it automatically. The app remains unsigned; VB-CABLE keeps its publisher's existing signatures.
