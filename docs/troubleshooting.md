# Troubleshooting

## The app is running but no window appears

Double-click the tray icon or choose **Open dashboard / status**. Startup mode intentionally opens only the tray. If no icon appears, start the app normally and inspect `%LOCALAPPDATA%\CodexUsageCompanion\logs\application.log`.

## Usage says stale or offline

The last known values remain visible by design. Choose **Refresh now**. Then check:

- Codex or the selected source is available;
- Windows networking and system time are correct;
- the configured reader mode matches your environment;
- the active/idle refresh and stale/offline timeouts are sensible.

The app does not infer restored allowance from elapsed reset time. It waits for a new confirmed reading.

## Experimental OAuth says sign-in is needed

Open Codex and sign in using the official Codex interface, then refresh. The companion never refreshes or rewrites Codex authentication. Confirm the consent checkbox is enabled only if you intend to use this reader.

Do not paste `auth.json` or a token into an issue. If `CODEX_HOME` is customized, it must be an absolute local path; links and network paths are rejected.

## CLI RPC is unavailable

The current Windows account must be able to launch `codex app-server`. Microsoft Store/WindowsApps execution aliases can return access denied on some installations. Use Mock or the consent-gated OAuth reader until a launchable CLI is installed. The source investigation details are in [data-source-investigation.md](data-source-investigation.md).

## Cloud sync is not configured

In **Settings → Cloud sync**, enter the project HTTPS URL and the `sb_publishable_...` key, save, then sign in. Never put an `sb_secret_...` key in the desktop.

If authentication expires, sign in again. If Supabase is unavailable, the app keeps the newest pending snapshot and retries with backoff; local history and notifications continue.

## Supabase rejected an update

Confirm the desktop uses the project URL and publishable key from the same project, the user session is current, and all migrations/functions were deployed. A `rejected` result usually means the normalized payload or database version is incompatible; do not retry by adding a secret key.

Open **Supabase Dashboard → Edge Functions → ingest-usage → Logs**. A healthy request shows a successful invocation; failures expose only sanitized error codes from this project. If the function is missing, repeat the link/deploy steps in [Supabase setup](setup-supabase.md). Never paste authorization headers into a report.

## Telegram does not respond

Check:

1. the webhook points to the deployed `telegram-webhook` function;
2. Telegram and Supabase use the same webhook secret;
3. the numeric chat ID and Supabase profile UUID match the function secrets;
4. the profile has `telegram_chat_id` and enabled preferences;
5. the bot has received `/start`;
6. Edge Function logs contain only sanitized error markers.

See [Telegram setup](setup-telegram.md). Rotate the bot token and webhook secret immediately if either was exposed.

## Scheduled alerts do not run

Confirm the three `codex_usage_*` Vault secrets exist and match the deployed function/project. Check `cron.job` for the notification and retention schedules, then inspect `net._http_response` and sanitized function logs. The migration intentionally skips invocation with a warning when Vault is incomplete.

## Update check fails

Monitoring does not depend on GitHub. Check the repository setting uses `owner/repository`, the latest release has a stable semantic tag such as `v1.1.0`, and GitHub is reachable. Drafts and prereleases are not Version 1 update candidates.

An installer download also requires the GitHub asset metadata to include a SHA-256 digest. Re-upload/publish the asset through GitHub Releases if metadata is incomplete.

## Windows notifications are missing

Confirm **Settings → Notifications → Show Windows notifications** is enabled and Windows **Settings → System → Notifications** allows Codex Usage Companion. Installed builds use a Start Menu shortcut/AppUserModelID for native toasts; source runs fall back to a notification-area balloon if Windows rejects an unpackaged toast.

Use Mock/LowUsage to create a threshold event. Because delivery is deduplicated, repeating the same reset cycle will not display it again; switch to another mock reset cycle rather than deleting production state.

## Start with Windows failed

Disable and re-enable the setting, then inspect the current-user entry:

```powershell
Get-ItemProperty 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' -Name CodexUsageCompanion
```

Run in PowerShell. Success shows one quoted installed executable and `--background`. If it points to a development `dotnet.exe` or a removed build folder, disable startup, install the packaged app, and enable it again.

## Clock or time-zone does not match

Confirm Windows date/time and time zone are correct, then check the app's IANA/Windows-compatible display zone in Settings. The default is `Asia/Bangkok`; database values remain UTC.

In PowerShell, `Get-TimeZone` should show the Windows system zone. The app may intentionally display a different configured zone. An unrecognized identifier is rejected when saving settings rather than silently shifting a reset.

## Upgrade is blocked

Upgrade preparation validates the current settings and writes a backup before replacement. A block means the existing settings are invalid or the backup directory is unavailable. Preserve the current files, repair settings with the installed version, confirm free disk space/permissions, and retry.

Backups are under `%LOCALAPPDATA%\CodexUsageCompanion\backups`; downloaded installers are under `updates`.

## Safe diagnostics

Use **Export diagnostics** in the status window. The export is an allowlist summary and excludes credentials and raw responses. Review it before sharing. Redact usernames from paths if needed.
