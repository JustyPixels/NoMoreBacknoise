# Local protocol v1

The WPF UI owns a random `nmb-<GUID>` named-pipe server using .NET `PipeOptions.CurrentUserOnly` and launches its bundled audio host as the pipe client. No network listener. JSON messages are UTF-8, one line each, version 1. Incoming host request size limit 64 KiB, UI response limit 256 KiB; telemetry queue bounded to 32 messages. Host stderr is drained separately. Newline-delimited messages must not contain literal newlines within a JSON string.

Request: `{ "version":1, "id":"unique-id", "op":"devices" }`. Replies have `version`, `requestId`, `type`; unsolicited events lack requestId. Unsupported versions/operations/invalid ranges return `type:error` with a message. `hello` declares hostVersion, engines, per-engine controls and unavailable optional features.

| Operation | Fields / behavior |
|---|---|
| devices | `devices: [{id,name,direction}]`, stable Windows endpoint identity |
| start | `route:{inputId,outputId?,monitorId?,monitorRaw}`, `settings`, `muted`, `bypass`; ACK then asynchronous status/telemetry |
| stop | Stops workers and discards session recording |
| configure | Optional `settings`, `muted`, `bypass`, `monitorRaw`; changes engines require stop/start |
| recordStart | Replaces earlier RAM test, max 60 s |
| recordStop | Retains raw/cleaned test until session stop |
| recordPlay | `monitorRaw` selects raw/cleaned RAM test, at the current playback cursor; headphones required, no change to cable feed |
| recordPlaybackStop | Returns monitor to live A/B |
| recordExport | `path`: absolute base WAV path; creates new `.raw.wav` and `.clean.wav` |
| shutdown | Stops session, ACK, exits |

Settings fields: engine (auto/rnnoise/deepfilter), backend (CPU only available), strength 0–100, attenuationDb 0–60, speechThreshold .05–.95, attackMs .1–100, holdMs 0–2000, releaseMs 1–2000, floorDb -80–0, gainDb -12–12, gateEnabled. UI/import/host all validate numeric ranges and finiteness. Unknown engines are rejected.

Telemetry: aligned raw/clean sample slices, spectra, peaks/RMS dBFS, speech probability, input clipping, per-interval peak processingMs, modelDelayMs, capture/render buffer estimates, input/output queuedMs, droppedSamples, underruns, selected engine, degraded flag and recordingSeconds. These are local and contain transient audio, so do not persist or include telemetry payloads in issue reports. Warning/error events include human-readable messages; fallback remains until explicit Retry. Missing input produces silence and reconnection is limited to the same saved identity.
