# Setup

Windows 10 22H2 or Windows 11, x64. Processing is offline on CPU. Extract the entire portable ZIP into a writable folder, or run the per-user installer. Keep `nmb-host.exe`, localization and runtime files with `NoMoreBacknoise.exe`.

## Cable routing

Download VB-CABLE directly from https://vb-audio.com/Cable/ and follow its publisher's installation/restart instructions. NoMoreBacknoise++ neither installs nor redistributes the cable.

Physical microphone → NoMoreBacknoise++ → **CABLE Input** (playback/render device) → **CABLE Output** (recording/capture device) → call/game/OBS.

The names seem reversed because they describe entry/exit from the cable. Select CABLE Input in this app. Select CABLE Output as the microphone in consuming apps. Do not select CABLE Output as this app's source: that creates a feedback loop. Leave Windows defaults as you prefer.

- Discord: User Settings → Voice & Video → Input Device → CABLE Output. Avoid stacking multiple aggressive suppressors; compare Discord's processing with it disabled.
- OBS: add Audio Input Capture → CABLE Output. Avoid capturing your physical mic a second time.
- Meeting software/games: choose CABLE Output in the app's microphone selector. Multiple apps can share the cable recording endpoint if their settings permit shared access.

## First launch and tuning

Four independent choices are shown: Windows startup (off), processing on launch (off), close to tray (on), update notifications (off). Processing starts only when you click Start, unless you explicitly enable automatic processing. Closing to tray keeps an active session running; use tray Quit to release the microphone.

Automatic benchmarks DF3 at startup and falls back to RNNoise if timing/model delay exceeds its budget. Natural favors speech, Balanced Aggressive is the starting point, Maximum Silence closes gaps harder. Strength affects RNNoise wet/dry mixing or DF3's attenuation limit. Advanced attenuation applies only to DF3. Speech sensitivity is a probability threshold, not speaker recognition. Lower it to retain quiet speech; increase hold/release if word endings disappear. Gain is manual; automatic leveling is excluded.

## Compare and record

Monitoring begins off. Select a headphone output, then enable Monitor. Listen Raw and Listen Clean switch the monitored signal. Monitoring is never sent back to the cable. Mute silences both output and monitoring; bypass forwards your unfiltered voice and is visibly labelled.

Record Test explicitly captures aligned raw/cleaned signals in RAM for up to 60 seconds. Enable headphone monitoring before recording to use **Play raw test / Play cleaned test** for an in-app comparison after stopping the recording. Switching those buttons during playback keeps the same position. Listen Raw/Clean returns to live monitoring. Playback only goes to your headphones; your live cleaned microphone continues feeding the cable. Stop Recording, then Export WAV to save both. Start another test replaces the earlier test. Stopping processing, changing routes/engine or quitting discards that session's recording, so export first. WAV export uses a new base name and never overwrites previous recordings.

Profiles contain only processing settings. Import/export never includes audio, device identities or voice features. User settings live in `%LOCALAPPDATA%\NoMoreBacknoise\settings.json`. Portable builds also use these per-user settings. Deleting that file resets the first-launch choices; disable Windows startup before resetting.

Updates are optional. When enabled, checks contact GitHub on launch and no more than daily; installation is manual from the releases page. No audio is sent. The initial app is unsigned; production application/driver signing is not included in this preview.
