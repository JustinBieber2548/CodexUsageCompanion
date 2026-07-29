# Resource usage observations

These are observations, not universal guarantees. WPF/.NET runtime versions, installed security software, DPI, Windows build, and enabled integrations affect results.

## 2026-07-29 smoke sample

- Environment: Windows 11 build 26200, .NET 8.0.29 runtime, self-contained x64 Release single-file build.
- Mode: Mock/Normal, `--background`, status window hidden, Supabase not configured.
- Sampling: after a five-second startup settle, process CPU and working set were sampled over ten seconds between polling events.
- CPU time during sample: 0.0156 seconds, approximately 0.16% of one CPU core.
- Working set: 105.71 MiB at sample start and 106.53 MiB at sample end.
- Files created: only `settings.json`, `state.json`, and the sanitized application log under `%LOCALAPPDATA%\CodexUsageCompanion`.
- Outcome: the process remained alive and responsive, used no busy loop, and was stopped cleanly by the test harness.

The one-second WPF countdown timer runs only while the status window exists; it performs no source or network poll. Source polling defaults to two minutes during relevant activity and five minutes otherwise, with bounded backoff after failures.

## Release measurement

Repeat this measurement on a clean Windows account with the signed installer, window visible/hidden, Supabase enabled/disabled, and each reader mode. Record at least a five-minute idle sample and a refresh sample in the release ticket. Investigate sustained CPU between polls, unbounded working-set growth, unexpected network traffic, or log growth before publishing.
