# Telegram bot setup

Telegram is optional and server-side. The Windows application never receives the bot token.

## 1. Create the bot

In Telegram, open the verified `@BotFather` account:

1. Run `/newbot`.
2. Choose a display name and unique username.
3. Store the token in a password manager.
4. Do not paste the token into source, desktop settings, issues, screenshots, or logs.

## 2. Determine the authorized chat

Send a message such as `/start` to the new bot. Use Telegram's `getUpdates` endpoint once, from a private terminal, to read the numeric `message.chat.id`. Clear the token-bearing command from shell history afterward, or use a short private script/environment variable instead of placing the token literally on the command line.

The Version 1 bot intentionally authorizes one Supabase profile UUID and one Telegram chat ID. Messages from every other chat are acknowledged and ignored.

## 3. Configure Supabase

Follow [Supabase setup](setup-supabase.md) and set:

- `TELEGRAM_BOT_TOKEN`;
- `TELEGRAM_WEBHOOK_SECRET`;
- `AUTHORIZED_PROFILE_ID`;
- `AUTHORIZED_TELEGRAM_CHAT_ID`;
- `NOTIFICATION_CHECK_CRON_SECRET`.

Set the user's `telegram_chat_id` and enable delivery:

```sql
update public.profiles
set telegram_chat_id = 'YOUR_NUMERIC_CHAT_ID',
    updated_at = now()
where id = 'YOUR_SUPABASE_USER_UUID';

insert into public.notification_preferences (profile_id, telegram_enabled)
values ('YOUR_SUPABASE_USER_UUID', true)
on conflict (profile_id)
do update set telegram_enabled = excluded.telegram_enabled,
              updated_at = now();
```

Run this only as an administrator in the SQL editor. The desktop user cannot update another profile because of Row Level Security.

## 4. Register the webhook

After deploying `telegram-webhook`, call Telegram's `setWebhook` API with:

- URL: `https://YOUR_PROJECT_REF.supabase.co/functions/v1/telegram-webhook`
- secret token: the exact `TELEGRAM_WEBHOOK_SECRET`
- allowed updates: `message`

Telegram will send the secret in `X-Telegram-Bot-Api-Secret-Token`; the function rejects a mismatch using constant-time comparison.

## 5. Commands

- `/start` — privacy-aware introduction.
- `/usage` — latest known 5-hour and weekly values.
- `/status` — device freshness and last monitoring state.
- `/notifications` — enabled alert types and thresholds.
- `/help` — command list.

Stale usage is always labeled with its age. The bot never claims a reset has restored allowance until a synchronized device reading confirms it.

## 6. Test

1. Send `/usage` from the authorized chat.
2. Confirm a fresh reading says monitoring is online.
3. Stop the desktop reader long enough to exceed the offline timeout and verify one offline alert.
4. Restart the app and verify one restored alert.
5. Send commands from a different chat and verify the bot sends no reply.
6. Replay a captured update ID in a safe test project and verify it is marked duplicate.

If delivery fails, use the sanitized Edge Function logs and [Troubleshooting](troubleshooting.md). Never log the bot token or request authorization headers.
