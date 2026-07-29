# Manual test plan

Complete this plan on Windows 11 before each public release. Record the app version, Windows build, tester, date, and result. Use a non-production Supabase project for destructive or replay tests.

## Installation and local durability

- Install as a standard user and confirm files go under `%LOCALAPPDATA%\Programs\Codex Usage Companion`.
- Confirm settings/state/logs go under `%LOCALAPPDATA%\CodexUsageCompanion`.
- Change settings, create history, stop the app, restart it, and confirm all values persist.
- Uninstall and reinstall; confirm user data remains.
- Install a newer version; confirm the pre-upgrade backup manifest, settings, history, notification state, and pending synchronization remain.
- Enter invalid JSON in a disposable test settings file and confirm upgrade preparation aborts without replacing the app.

## Tray and status UI

- Launch normally and with `--background`.
- Confirm the tray tooltip includes short-term, weekly, and status information when available.
- Verify every tray menu item: Codex Usage, Refresh now, Open dashboard/status, Notifications, Start with Windows, Settings, View logs, Check for updates, About, Exit.
- Close the status window and confirm monitoring continues.
- Confirm reset countdowns update locally each second without additional network reads.
- Verify the status window clearly labels stale, offline, authentication-required, unsupported-source, and reader-error states.

## Reader modes

- In Mock/Normal, confirm values change and the UI remains responsive.
- In Mock/LowUsage, verify 10%, 5%, and weekly threshold behavior.
- In Mock/Reset, verify the first reading says reset time arrived/awaiting confirmation and the next confirmed reading may show 100%.
- In Mock/Stale, verify stale labeling.
- In Mock/AuthenticationFailure, NetworkFailure, and Unsupported, verify safe messages without secrets.
- Select ExperimentalOAuth without consent and verify it refuses to read.
- After informed consent, test the OAuth reader with a disposable account; confirm `auth.json` modification time does not change.
- Test CLI RPC on a machine with a launchable Codex CLI and on one where Windows blocks it; verify the failure is bounded and sanitized.

## Notification deduplication

- Cross 10%, 5%, and 0% short-term thresholds and 10%/5% weekly thresholds.
- Restart after each alert and confirm the same reset-cycle alert does not repeat.
- Raise remaining allowance above the rearm margin, then cross again and confirm one new threshold event.
- Move the clock past a reset timestamp without a new reading and verify no 100% claim.
- Confirm the 10-minute short-term reset warning fires once.
- Stop successful reads for 15 minutes, verify one offline event, wait longer, and verify no duplicate.
- Restore a fresh read and verify one restored event.
- Repeat with Windows notifications disabled and verify state still advances without popups.

## Sleep, wake, and network recovery

- Put Windows to sleep across a scheduled refresh and reset boundary.
- Wake offline; verify the last known data remains visible and stale.
- Restore networking; verify refresh and cloud synchronization recover.
- Interrupt Supabase for multiple refreshes, then restore it; verify only the newest queued snapshot uploads and local history remains.

## Security and privacy

- Search logs and diagnostics for test passwords, bearer tokens, JWTs, cookies, bot tokens, and Supabase secret keys; none should appear.
- Confirm the desktop configuration contains only a publishable Supabase key.
- Attempt a cross-user database query and cross-device ingest; verify Row Level Security/ownership rejection.
- Send an oversized, malformed, extra-field, future-skewed, or out-of-range ingest request; verify rejection.
- Send a Telegram webhook with a wrong secret, unauthorized chat, and duplicate update; verify rejection/ignore/deduplication.

## Updates and rollback

- Check when current, newer, older, malformed, prerelease, and unavailable GitHub metadata is returned.
- Confirm automatic checks occur no more than once in 24 hours.
- Confirm an update displays version and release notes.
- Decline download; confirm nothing installs.
- Approve download with a wrong digest; confirm installation is blocked.
- Approve a correct signed installer; confirm a backup is created and a second confirmation is required.
- Roll back using the previous GitHub Release installer and the instructions in [releasing.md](releasing.md).

## Exit criteria

- .NET build: zero warnings and zero errors.
- All .NET, Deno, and database lint/migration checks pass.
- No severity-high security finding remains open.
- Signing and installer digest are verified.
- All required manual cases pass or have a documented release-blocking disposition.
