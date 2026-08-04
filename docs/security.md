# Security and privacy

## Data classification

The desktop may store normalized remaining/used percentages, reset timestamps, device label and random device UUID, app version, freshness, sanitized error codes, notification keys, and local history.

The following data is intentionally excluded from local logs, diagnostics, and Supabase payloads:

- Codex prompts and completions;
- raw Codex service or CLI responses;
- Codex access tokens, cookies, or refresh material;
- Supabase passwords;
- Supabase secret/service-role keys;
- Telegram bot token and webhook/cron secrets.

## Credential handling

Mock mode is the default. The experimental OAuth reader requires an explicit checkbox and reads the current Windows user's local Codex `auth.json` only long enough to make a fixed-host HTTPS usage request. It rejects network paths and filesystem links, limits file/response sizes, does not follow redirects, does not refresh tokens, and never writes the file.

Supabase email/password sign-in uses the password for one HTTPS request and immediately clears the UI field. Only the rotating access/refresh session is serialized into Windows Credential Manager. The desktop's API key is publishable and expected to be recoverable from the executable.

Server-only values live in Supabase Edge Function secrets or Vault:

- Supabase secret key;
- Telegram bot token;
- Telegram webhook secret;
- notification cron secret;
- authorized Telegram chat/profile identifiers.

## Network trust boundaries

- The Codex OAuth reader calls only `https://chatgpt.com/backend-api/wham/usage`.
- The desktop calls its configured HTTPS Supabase project and GitHub's HTTPS API/release asset hosts.
- GitHub update assets must be Windows installer files, stay below 500 MB, and expose a SHA-256 digest that matches the download.
- Telegram accepts only POST requests with Telegram's secret header, ignores unauthorized chats, bounds payload size, and deduplicates update IDs.
- The cron function accepts only a constant-time-checked shared secret.

## Database controls

All public user tables have Row Level Security. Policies bind rows to `auth.uid()` and explicitly target the authenticated role. The desktop cannot access elevated keys. The ingest RPC validates ownership and rate-limits device writes. Admin-only maintenance functions use a locked search path and revoked execution grants.

The migration enables 30-day snapshot retention, 90-day notification-event retention, and 30-day Telegram update retention.

Supabase recommends publishable keys for desktop applications, secret keys only for server-controlled components, and `verify_jwt = false` plus application authorization when using the new non-JWT API keys. The implementation follows that model and validates the user's Auth bearer token itself. See [Understanding API keys](https://supabase.com/docs/guides/getting-started/api-keys), [Edge Function authentication](https://supabase.com/docs/guides/functions/auth), and [Row Level Security](https://supabase.com/docs/guides/database/postgres/row-level-security).

## Logging and diagnostics

Logs are structured, rotated at 2 MB, and pass messages/properties through token, bearer, password, cookie, JWT, and secret-key redaction. Diagnostics export is an allowlist summary, not a copy of settings or logs.

## Update threat model

The updater never installs without explicit approval:

1. GitHub Release metadata is parsed as stable semantic versioning.
2. The user reviews the new version and release notes.
3. The user approves the download.
4. The asset's GitHub SHA-256 digest must match.
5. Existing settings/state are validated and backed up.
6. The user separately approves automatic installation.
7. The installer shows progress without another wizard, preserves per-user data, and reopens the app when complete.

Public release artifacts should also be Authenticode-signed. GitHub unavailability is reported without affecting monitoring.

## Reporting vulnerabilities

Follow [SECURITY.md](../SECURITY.md). Do not include live tokens, passwords, cookies, database dumps, or private usage data in a report.
