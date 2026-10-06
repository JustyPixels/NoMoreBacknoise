# Contributing

Read docs/BUILD.md and docs/VALIDATION.md. Small pull requests with a concrete behavior change and relevant checks are welcome. Keep audio processing independent of UI rendering, queues bounded and audio/device data local. Do not add uploads, automatic recordings, voice effects, default-device changes or automatic leveling.

For engine changes, preserve upstream normalization/model state and supply pinned revisions, model checksums and redistribution licenses. State measured algorithm/device/application delay separately. Document difficult cases, especially quiet speech and overlapping voices.

Translate strings.json without removing keys. Test fonts, keyboard navigation, Arabic RTL and 100–200% scaling. Help with native-language review is welcome.

Never commit personal recordings, voice features, settings, access tokens, certificates or signing keys. Supply synthetic/publicly licensed fixtures or reproducible private-test instructions. Original contributions are under MIT; third-party code retains its upstream notice.
