# Supabase setup

Codex Usage Companion uses Supabase Auth, Postgres, Row Level Security, Edge Functions, Vault, `pg_cron`, and `pg_net`. Cloud sync is optional; the Windows app remains fully usable without it.

These steps follow the current Supabase guidance for [publishable and secret API keys](https://supabase.com/docs/guides/getting-started/migrating-to-new-api-keys), [Edge Function secrets](https://supabase.com/docs/guides/functions/secrets), and [scheduled functions](https://supabase.com/docs/guides/functions/schedule-functions).

## 1. Create and link a project

Create a Supabase project and keep email/password authentication enabled. Install the current CLI, then:

```powershell
supabase login
supabase link --project-ref YOUR_PROJECT_REF
```

Do not put an access token, database password, or secret key in this repository.

## 2. Review and apply migrations

The committed immutable migrations are:

- `supabase/migrations/20260729100426_initial_schema.sql`
- `supabase/migrations/20260729105151_notification_retry_state.sql`
- `supabase/migrations/20260729105412_fix_notification_preference_validation.sql`

Review it, then apply it:

```powershell
supabase db push --include-all
```

For local verification, start Docker Desktop and run:

```powershell
supabase start
supabase db reset
supabase db lint --local --level error
```

Never edit a migration that has reached a shared environment. For every later schema change:

```powershell
supabase migration new short_change_name
```

Put only the forward change in the new timestamped file, test from a clean database, and commit it with the application compatibility changes.

## 3. Deploy Edge Functions

The new publishable/secret keys are not JWTs, so these functions use `verify_jwt = false` and perform their own authorization:

```powershell
supabase functions deploy ingest-usage --no-verify-jwt
supabase functions deploy telegram-webhook --no-verify-jwt
supabase functions deploy notification-check --no-verify-jwt
```

`ingest-usage` verifies the user's Supabase Auth bearer token before calling the typed ingest RPC. `telegram-webhook` verifies Telegram's secret header and the authorized chat. `notification-check` verifies a separate cron secret.

Hosted functions automatically receive `SUPABASE_URL`, `SUPABASE_PUBLISHABLE_KEYS`, and `SUPABASE_SECRET_KEYS`. The implementation reads the `default` named key and retains legacy environment fallbacks only for transition.

## 4. Configure function secrets

Generate independent high-entropy values for the webhook and cron secrets. Configure these without committing an `.env` file:

```powershell
supabase secrets set TELEGRAM_BOT_TOKEN='YOUR_BOT_TOKEN'
supabase secrets set TELEGRAM_WEBHOOK_SECRET='A_LONG_RANDOM_WEBHOOK_SECRET'
supabase secrets set NOTIFICATION_CHECK_CRON_SECRET='A_DIFFERENT_LONG_RANDOM_SECRET'
supabase secrets set AUTHORIZED_PROFILE_ID='YOUR_SUPABASE_USER_UUID'
supabase secrets set AUTHORIZED_TELEGRAM_CHAT_ID='YOUR_NUMERIC_CHAT_ID'
```

The repository ignores local environment files. Use the Supabase dashboard's Edge Function Secrets page if shell history is a concern.

## 5. Configure Vault for scheduled invocation

The migration schedules the notification checker every five minutes. It safely emits a database warning until these Vault entries exist.

Run in the Supabase SQL editor, substituting real values:

```sql
select vault.create_secret(
  'https://YOUR_PROJECT_REF.supabase.co',
  'codex_usage_project_url'
);

select vault.create_secret(
  'YOUR_SUPABASE_SECRET_KEY',
  'codex_usage_notification_secret_key'
);

select vault.create_secret(
  'THE_SAME_NOTIFICATION_CHECK_CRON_SECRET',
  'codex_usage_notification_cron_secret'
);
```

The secret key is server-only and bypasses RLS. Never copy it into the Windows app. If a value changes, update its Vault secret and Edge Function secret together where applicable.

## 6. Configure the desktop

From **Settings → Cloud sync**, enter only:

- `https://YOUR_PROJECT_REF.supabase.co`;
- the `sb_publishable_...` key;
- the user's email;
- the password for that immediate sign-in.

Choose **Save and sign in**. The password is not retained; the resulting session is encrypted in Windows Credential Manager. A profile/device row is created on the first successful normalized upload.

## 7. Revoke a lost device or leaked client session

The publishable key is not a secret and does not bypass Row Level Security. A stolen user refresh token is sensitive.

1. In Supabase Auth, revoke the user's sessions (or reset the user's password and sign out other sessions).
2. Delete the lost device row as an administrator:

```sql
delete from public.devices
where profile_id = 'YOUR_SUPABASE_USER_UUID'
  and id = 'LOST_DEVICE_UUID';
```

3. On remaining Windows devices, sign out/in from **Settings → Cloud sync** if their sessions were revoked.
4. Review `usage_snapshots`, `notification_events`, and sanitized function logs for unexpected device activity.
5. Rotate the publishable key only if application-component rotation is desired; rotate the server secret key immediately if it was ever exposed.

The successful output for the SQL command is `DELETE 1`. `DELETE 0` means the IDs did not match; re-check them in the dashboard before taking any broader action.

## 8. Verify isolation

Create two Auth users and sign the desktop/test client in as each. Confirm:

- each user can see only their profile, devices, snapshots, preferences, and notification events;
- attempts to use another user's device UUID are rejected;
- the desktop works with the publishable key and never needs the secret key;
- repeated unchanged readings create hourly heartbeats rather than high-frequency history;
- invalid percentages, future-skewed timestamps, unknown fields, and malformed versions are rejected.

## Schema overview

- `profiles` — user-owned delivery identity.
- `devices` — heartbeat, latest read/error, and application version.
- `usage_snapshots` — meaningful normalized history.
- `latest_usage` — one latest status row per user.
- `notification_preferences` — thresholds and offline timeout.
- `notification_events` — idempotent server delivery state.
- `processed_telegram_updates` — webhook replay protection.

Retention removes snapshots after 30 days, notification event records after 90 days, and Telegram update IDs after 30 days.
