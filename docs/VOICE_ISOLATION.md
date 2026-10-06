# Bounded enrolled-voice feasibility review

Decision for v0.1.0: **unavailable**. General suppression ships; no enrollment or working-looking isolation UI is exposed. A voice/speaker recognition gate would only mute segments and cannot separate overlapping speakers, so it is excluded.

Admission requires publicly accessible pretrained weights, documented redistribution rights, offline reproducible *streaming waveform extraction*, ≤50 ms application delay, processing below half the available frame time on a named reference CPU, median SI-SDR improvement ≥5 dB on held-out overlapping-speaker mixtures, and clean-speech STOI drop ≤0.03. Custom training is outside v1.

## Screened candidates (2026-10-06)

| Candidate and primary evidence | Screening outcome |
|---|---|
| [BUTSpeechFIT SpeakerBeam](https://github.com/BUTSpeechFIT/speakerbeam/blob/main/README.md) | README describes a training recipe and evaluating the trained checkpoint. A redistributable pretrained streaming checkpoint meeting the admission criteria was not established in this review. No custom training performed. |
| [SpeakerBeam-SS research](https://arxiv.org/abs/2407.01857) | Paper presents actual streaming extraction and reduced computational complexity. The research result alone does not establish accessible licensed weights or timing/quality on our Windows host. Not admitted without reproducible checkpoint validation. |
| [penta2himajin/tse-conv-tasnet-48k](https://huggingface.co/penta2himajin/tse-conv-tasnet-48k) | Search index describes causal 48 kHz extraction, but direct model access returned HTTP 401 during review. We could not establish usable accessible weights and redistribution rights. |
| [TSExcalibur](https://huggingface.co/swc2/TSExcalibur) | Public card declares Apache-2.0 and a target-extraction toolkit. A verified low-delay streaming deployment and reproducible weight/inference package were not established; not admitted on a toolkit description alone. |

These are screening outcomes, not claims that the models cannot work. No candidate reached the reproducible-inference gate; therefore SI-SDR/STOI/CPU acceptance measurements are **not available**, and no fabricated pass/fail performance numbers are reported. Revisit when a specific licensed checkpoint and streaming inference reference are obtainable.

## Reproducible acceptance procedure for a future candidate

Pin source revision, checkpoint SHA256, license and inference dependencies. Record the reference CPU model, Windows version, sample rate, block size, causal receptive context, lookahead and buffering. Split enrollment and held-out speakers/utterances to prevent contamination; form 0/-5/+5 dB overlapping-speaker mixtures and keep enrollment separate from test mixtures. Include quiet words, interruptions and clean speech. Preserve the candidate's official normalization/state.

Run warmup then sustained timing, report p50/p95/p99/max and real-time factor. Delay must include resampling, model lookahead and application buffers. Compute SI-SDR against the target and improvement relative to the mixture, plus clean-speech STOI change using a pinned metric implementation. Require *all* thresholds above before adding enrollment.

If admitted, accept 30 seconds of clean, unclipped enrollment; reject unsuitable segments, discard recordings immediately after feature extraction, encrypt only model-specific features with Windows user-scoped DPAPI, and provide delete/re-enroll. No voice features belong in ordinary profile exports, logs, GitHub or telemetry. This storage path is deliberately not created while isolation is unavailable.

## GPU feasibility

The pinned libDF ONNX inference uses Tract's pulse/stateful graph execution and preprocessing. Switching an execution provider is not supported by that backend. [ONNX Runtime DirectML](https://onnxruntime.ai/docs/execution-providers/DirectML-ExecutionProvider.html) has specific session/concurrency restrictions; a future implementation needs a supported causal/stateful export, matching preprocessing/state and numerical parity, then compatibility and end-to-end timing checks. GPU is not offered as a selectable backend in this release; CPU is the working default.
