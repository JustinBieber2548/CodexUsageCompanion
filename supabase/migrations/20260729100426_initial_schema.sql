-- Codex Usage Companion Version 1 schema.
-- This migration is immutable after deployment. Future schema changes require
-- a new migration file.

create extension if not exists pgcrypto with schema extensions;
create extension if not exists pg_cron with schema pg_catalog;
create extension if not exists pg_net with schema extensions;
create extension if not exists supabase_vault with schema vault;

create schema if not exists private;
revoke all on schema private from public, anon, authenticated;

create or replace function private.valid_thresholds(values_to_check numeric[])
returns boolean
language sql
immutable
strict
security invoker
set search_path = ''
as $$
  select coalesce(bool_and(value between 0 and 100), true)
  from unnest(values_to_check) as value;
$$;

revoke all on function private.valid_thresholds(numeric[]) from public, anon, authenticated;

create table public.profiles (
  id uuid primary key references auth.users (id) on delete cascade,
  telegram_chat_id text unique,
  timezone text not null default 'Asia/Bangkok'
    check (char_length(timezone) between 1 and 100),
  created_at timestamptz not null default now(),
  updated_at timestamptz not null default now()
);

create table public.devices (
  id uuid primary key,
  profile_id uuid not null references public.profiles (id) on delete cascade,
  device_name text not null
    check (char_length(device_name) between 1 and 100),
  app_version text not null
    check (app_version ~ '^[0-9]+\.[0-9]+\.[0-9]+(?:-[0-9A-Za-z.-]+)?(?:\+[0-9A-Za-z.-]+)?$'),
  last_heartbeat_at timestamptz,
  last_successful_read_at timestamptz,
  last_error_code text
    check (last_error_code is null or char_length(last_error_code) <= 100),
  last_error_message_sanitized text
    check (last_error_message_sanitized is null or char_length(last_error_message_sanitized) <= 500),
  created_at timestamptz not null default now(),
  updated_at timestamptz not null default now(),
  unique (profile_id, id)
);

create table public.usage_snapshots (
  id bigint generated always as identity primary key,
  profile_id uuid not null references public.profiles (id) on delete cascade,
  device_id uuid not null,
  short_term_name text,
  short_term_remaining numeric,
  short_term_used numeric,
  short_term_reset_at timestamptz,
  short_term_window_seconds bigint,
  short_term_confirmed boolean not null default false,
  short_term_estimated boolean not null default false,
  weekly_name text,
  weekly_remaining numeric,
  weekly_used numeric,
  weekly_reset_at timestamptz,
  weekly_window_seconds bigint,
  weekly_confirmed boolean not null default false,
  weekly_estimated boolean not null default false,
  source_type text not null
    check (char_length(source_type) between 1 and 100),
  source_freshness text not null
    check (source_freshness in (
      'fresh',
      'stale',
      'offline',
      'authenticationrequired',
      'unsupportedsource',
      'readererror'
    )),
  captured_at timestamptz not null,
  received_at timestamptz not null default now(),
  foreign key (profile_id, device_id)
    references public.devices (profile_id, id)
    on delete cascade,
  check (short_term_remaining is null or short_term_remaining between 0 and 100),
  check (short_term_used is null or short_term_used between 0 and 100),
  check (weekly_remaining is null or weekly_remaining between 0 and 100),
  check (weekly_used is null or weekly_used between 0 and 100),
  check (short_term_window_seconds is null or short_term_window_seconds > 0),
  check (weekly_window_seconds is null or weekly_window_seconds > 0),
  check (not (short_term_confirmed and short_term_estimated)),
  check (not (weekly_confirmed and weekly_estimated)),
  check (captured_at >= timestamptz '2020-01-01 00:00:00+00'),
  check (captured_at <= received_at + interval '5 minutes'),
  check (short_term_reset_at is null or short_term_reset_at >= timestamptz '2020-01-01 00:00:00+00'),
  check (weekly_reset_at is null or weekly_reset_at >= timestamptz '2020-01-01 00:00:00+00')
);

create table public.latest_usage (
  profile_id uuid not null references public.profiles (id) on delete cascade,
  device_id uuid not null,
  short_term_name text,
  short_term_remaining numeric,
  short_term_used numeric,
  short_term_reset_at timestamptz,
  short_term_window_seconds bigint,
  short_term_confirmed boolean not null default false,
  short_term_estimated boolean not null default false,
  weekly_name text,
  weekly_remaining numeric,
  weekly_used numeric,
  weekly_reset_at timestamptz,
  weekly_window_seconds bigint,
  weekly_confirmed boolean not null default false,
  weekly_estimated boolean not null default false,
  source_type text not null,
  source_freshness text not null,
  captured_at timestamptz not null,
  received_at timestamptz not null default now(),
  primary key (profile_id, device_id),
  foreign key (profile_id, device_id)
    references public.devices (profile_id, id)
    on delete cascade,
  check (short_term_remaining is null or short_term_remaining between 0 and 100),
  check (short_term_used is null or short_term_used between 0 and 100),
  check (weekly_remaining is null or weekly_remaining between 0 and 100),
  check (weekly_used is null or weekly_used between 0 and 100),
  check (short_term_window_seconds is null or short_term_window_seconds > 0),
  check (weekly_window_seconds is null or weekly_window_seconds > 0),
  check (not (short_term_confirmed and short_term_estimated)),
  check (not (weekly_confirmed and weekly_estimated)),
  check (source_freshness in (
    'fresh',
    'stale',
    'offline',
    'authenticationrequired',
    'unsupportedsource',
    'readererror'
  )),
  check (captured_at >= timestamptz '2020-01-01 00:00:00+00'),
  check (captured_at <= received_at + interval '5 minutes')
);

create table public.notification_preferences (
  profile_id uuid primary key references public.profiles (id) on delete cascade,
  windows_enabled boolean not null default true,
  telegram_enabled boolean not null default true,
  short_term_thresholds numeric[] not null default array[10, 5, 0]::numeric[],
  weekly_thresholds numeric[] not null default array[10, 5]::numeric[],
  short_term_reset_warning boolean not null default true,
  reset_notifications boolean not null default true,
  offline_timeout_minutes integer not null default 15
    check (offline_timeout_minutes between 1 and 1440),
  usage_change_enabled boolean not null default false,
  quiet_hours_start time,
  quiet_hours_end time,
  created_at timestamptz not null default now(),
  updated_at timestamptz not null default now(),
  check (
    array_position(short_term_thresholds, null) is null
    and array_position(weekly_thresholds, null) is null
    and private.valid_thresholds(short_term_thresholds)
    and private.valid_thresholds(weekly_thresholds)
  )
);

create table public.notification_events (
  id bigint generated always as identity primary key,
  profile_id uuid not null references public.profiles (id) on delete cascade,
  device_id uuid references public.devices (id) on delete set null,
  idempotency_key text not null
    check (char_length(idempotency_key) between 1 and 250),
  event_key text not null
    check (char_length(event_key) between 1 and 150),
  channel text not null
    check (channel in ('windows', 'telegram')),
  threshold numeric
    check (threshold is null or threshold between 0 and 100),
  status text not null
    check (status in ('pending', 'delivered', 'failed', 'suppressed')),
  created_at timestamptz not null default now(),
  delivered_at timestamptz,
  error_sanitized text
    check (error_sanitized is null or char_length(error_sanitized) <= 500),
  unique (profile_id, idempotency_key),
  check (delivered_at is null or delivered_at >= created_at)
);

create table public.processed_telegram_updates (
  update_id bigint primary key,
  profile_id uuid not null references public.profiles (id) on delete cascade,
  processed_at timestamptz not null default now()
);

create index devices_profile_heartbeat_idx
  on public.devices (profile_id, last_heartbeat_at desc);
create index usage_snapshots_profile_captured_idx
  on public.usage_snapshots (profile_id, captured_at desc);
create index usage_snapshots_device_captured_idx
  on public.usage_snapshots (device_id, captured_at desc);
create index notification_events_profile_created_idx
  on public.notification_events (profile_id, created_at desc);
create index notification_events_pending_idx
  on public.notification_events (status, created_at)
  where status = 'pending';

alter table public.profiles enable row level security;
alter table public.devices enable row level security;
alter table public.usage_snapshots enable row level security;
alter table public.latest_usage enable row level security;
alter table public.notification_preferences enable row level security;
alter table public.notification_events enable row level security;
alter table public.processed_telegram_updates enable row level security;

grant select, insert, update on public.profiles to authenticated;
grant select, insert, update, delete on public.devices to authenticated;
grant select, insert on public.usage_snapshots to authenticated;
grant select, insert, update on public.latest_usage to authenticated;
grant select, insert, update, delete on public.notification_preferences to authenticated;
grant select on public.notification_events to authenticated;
grant usage, select on all sequences in schema public to authenticated;

grant select, insert, update, delete on public.profiles to service_role;
grant select, insert, update, delete on public.devices to service_role;
grant select, insert, update, delete on public.usage_snapshots to service_role;
grant select, insert, update, delete on public.latest_usage to service_role;
grant select, insert, update, delete on public.notification_preferences to service_role;
grant select, insert, update, delete on public.notification_events to service_role;
grant select, insert, update, delete on public.processed_telegram_updates to service_role;
grant usage, select on all sequences in schema public to service_role;

create policy "profiles_select_own"
  on public.profiles for select
  to authenticated
  using ((select auth.uid()) = id);

create policy "profiles_insert_own"
  on public.profiles for insert
  to authenticated
  with check ((select auth.uid()) = id);

create policy "profiles_update_own"
  on public.profiles for update
  to authenticated
  using ((select auth.uid()) = id)
  with check ((select auth.uid()) = id);

create policy "devices_select_own"
  on public.devices for select
  to authenticated
  using ((select auth.uid()) = profile_id);

create policy "devices_insert_own"
  on public.devices for insert
  to authenticated
  with check ((select auth.uid()) = profile_id);

create policy "devices_update_own"
  on public.devices for update
  to authenticated
  using ((select auth.uid()) = profile_id)
  with check ((select auth.uid()) = profile_id);

create policy "devices_delete_own"
  on public.devices for delete
  to authenticated
  using ((select auth.uid()) = profile_id);

create policy "usage_snapshots_select_own"
  on public.usage_snapshots for select
  to authenticated
  using ((select auth.uid()) = profile_id);

create policy "usage_snapshots_insert_own"
  on public.usage_snapshots for insert
  to authenticated
  with check ((select auth.uid()) = profile_id);

create policy "latest_usage_select_own"
  on public.latest_usage for select
  to authenticated
  using ((select auth.uid()) = profile_id);

create policy "latest_usage_insert_own"
  on public.latest_usage for insert
  to authenticated
  with check ((select auth.uid()) = profile_id);

create policy "latest_usage_update_own"
  on public.latest_usage for update
  to authenticated
  using ((select auth.uid()) = profile_id)
  with check ((select auth.uid()) = profile_id);

create policy "notification_preferences_select_own"
  on public.notification_preferences for select
  to authenticated
  using ((select auth.uid()) = profile_id);

create policy "notification_preferences_insert_own"
  on public.notification_preferences for insert
  to authenticated
  with check ((select auth.uid()) = profile_id);

create policy "notification_preferences_update_own"
  on public.notification_preferences for update
  to authenticated
  using ((select auth.uid()) = profile_id)
  with check ((select auth.uid()) = profile_id);

create policy "notification_preferences_delete_own"
  on public.notification_preferences for delete
  to authenticated
  using ((select auth.uid()) = profile_id);

create policy "notification_events_select_own"
  on public.notification_events for select
  to authenticated
  using ((select auth.uid()) = profile_id);

create or replace function public.ingest_usage(
  p_device_id uuid,
  p_device_name text,
  p_app_version text,
  p_source_type text,
  p_source_freshness text,
  p_captured_at timestamptz,
  p_short_term_name text,
  p_short_term_remaining numeric,
  p_short_term_used numeric,
  p_short_term_reset_at timestamptz,
  p_short_term_window_seconds bigint,
  p_short_term_confirmed boolean,
  p_short_term_estimated boolean,
  p_weekly_name text,
  p_weekly_remaining numeric,
  p_weekly_used numeric,
  p_weekly_reset_at timestamptz,
  p_weekly_window_seconds bigint,
  p_weekly_confirmed boolean,
  p_weekly_estimated boolean,
  p_error_code text,
  p_error_message_sanitized text
)
returns jsonb
language plpgsql
security invoker
set search_path = ''
as $$
declare
  v_profile_id uuid := (select auth.uid());
  v_last_heartbeat timestamptz;
  v_write_history boolean;
  v_snapshot_id bigint;
begin
  if v_profile_id is null then
    raise exception using errcode = '28000', message = 'authentication_required';
  end if;

  if char_length(p_device_name) not between 1 and 100
    or char_length(p_source_type) not between 1 and 100
    or p_app_version !~ '^[0-9]+\.[0-9]+\.[0-9]+(?:-[0-9A-Za-z.-]+)?(?:\+[0-9A-Za-z.-]+)?$'
    or (p_error_code is not null and char_length(p_error_code) > 100)
    or (p_error_message_sanitized is not null and char_length(p_error_message_sanitized) > 500)
  then
    raise exception using errcode = '22023', message = 'invalid_metadata';
  end if;

  if p_source_freshness not in (
    'fresh',
    'stale',
    'offline',
    'authenticationrequired',
    'unsupportedsource',
    'readererror'
  ) then
    raise exception using errcode = '22023', message = 'invalid_freshness';
  end if;

  select d.last_heartbeat_at
  into v_last_heartbeat
  from public.devices as d
  where d.id = p_device_id
    and d.profile_id = v_profile_id;

  if v_last_heartbeat is not null and v_last_heartbeat > now() - interval '15 seconds' then
    raise exception using errcode = 'P0001', message = 'rate_limited';
  end if;

  insert into public.profiles (id)
  values (v_profile_id)
  on conflict (id) do nothing;

  insert into public.devices (
    id,
    profile_id,
    device_name,
    app_version,
    last_heartbeat_at,
    last_successful_read_at,
    last_error_code,
    last_error_message_sanitized
  )
  values (
    p_device_id,
    v_profile_id,
    p_device_name,
    p_app_version,
    now(),
    case
      when p_short_term_name is not null or p_weekly_name is not null then p_captured_at
      else null
    end,
    p_error_code,
    p_error_message_sanitized
  )
  on conflict (id) do update
  set
    device_name = excluded.device_name,
    app_version = excluded.app_version,
    last_heartbeat_at = excluded.last_heartbeat_at,
    last_successful_read_at = coalesce(
      excluded.last_successful_read_at,
      public.devices.last_successful_read_at
    ),
    last_error_code = excluded.last_error_code,
    last_error_message_sanitized = excluded.last_error_message_sanitized,
    updated_at = now()
  where public.devices.profile_id = v_profile_id;

  if p_short_term_name is null and p_weekly_name is null then
    return jsonb_build_object(
      'historyWritten',
      false,
      'snapshotId',
      null
    );
  end if;

  select not exists (
    select 1
    from public.latest_usage as current
    where current.profile_id = v_profile_id
      and current.device_id = p_device_id
      and current.short_term_remaining is not distinct from p_short_term_remaining
      and current.short_term_used is not distinct from p_short_term_used
      and current.short_term_reset_at is not distinct from p_short_term_reset_at
      and current.short_term_confirmed = p_short_term_confirmed
      and current.short_term_estimated = p_short_term_estimated
      and current.weekly_remaining is not distinct from p_weekly_remaining
      and current.weekly_used is not distinct from p_weekly_used
      and current.weekly_reset_at is not distinct from p_weekly_reset_at
      and current.weekly_confirmed = p_weekly_confirmed
      and current.weekly_estimated = p_weekly_estimated
      and current.source_freshness = p_source_freshness
      and current.received_at > now() - interval '1 hour'
  )
  into v_write_history;

  if v_write_history then
    insert into public.usage_snapshots (
      profile_id,
      device_id,
      short_term_name,
      short_term_remaining,
      short_term_used,
      short_term_reset_at,
      short_term_window_seconds,
      short_term_confirmed,
      short_term_estimated,
      weekly_name,
      weekly_remaining,
      weekly_used,
      weekly_reset_at,
      weekly_window_seconds,
      weekly_confirmed,
      weekly_estimated,
      source_type,
      source_freshness,
      captured_at
    )
    values (
      v_profile_id,
      p_device_id,
      p_short_term_name,
      p_short_term_remaining,
      p_short_term_used,
      p_short_term_reset_at,
      p_short_term_window_seconds,
      p_short_term_confirmed,
      p_short_term_estimated,
      p_weekly_name,
      p_weekly_remaining,
      p_weekly_used,
      p_weekly_reset_at,
      p_weekly_window_seconds,
      p_weekly_confirmed,
      p_weekly_estimated,
      p_source_type,
      p_source_freshness,
      p_captured_at
    )
    returning id into v_snapshot_id;
  end if;

  insert into public.latest_usage (
    profile_id,
    device_id,
    short_term_name,
    short_term_remaining,
    short_term_used,
    short_term_reset_at,
    short_term_window_seconds,
    short_term_confirmed,
    short_term_estimated,
    weekly_name,
    weekly_remaining,
    weekly_used,
    weekly_reset_at,
    weekly_window_seconds,
    weekly_confirmed,
    weekly_estimated,
    source_type,
    source_freshness,
    captured_at,
    received_at
  )
  values (
    v_profile_id,
    p_device_id,
    p_short_term_name,
    p_short_term_remaining,
    p_short_term_used,
    p_short_term_reset_at,
    p_short_term_window_seconds,
    p_short_term_confirmed,
    p_short_term_estimated,
    p_weekly_name,
    p_weekly_remaining,
    p_weekly_used,
    p_weekly_reset_at,
    p_weekly_window_seconds,
    p_weekly_confirmed,
    p_weekly_estimated,
    p_source_type,
    p_source_freshness,
    p_captured_at,
    now()
  )
  on conflict (profile_id, device_id) do update
  set
    short_term_name = excluded.short_term_name,
    short_term_remaining = excluded.short_term_remaining,
    short_term_used = excluded.short_term_used,
    short_term_reset_at = excluded.short_term_reset_at,
    short_term_window_seconds = excluded.short_term_window_seconds,
    short_term_confirmed = excluded.short_term_confirmed,
    short_term_estimated = excluded.short_term_estimated,
    weekly_name = excluded.weekly_name,
    weekly_remaining = excluded.weekly_remaining,
    weekly_used = excluded.weekly_used,
    weekly_reset_at = excluded.weekly_reset_at,
    weekly_window_seconds = excluded.weekly_window_seconds,
    weekly_confirmed = excluded.weekly_confirmed,
    weekly_estimated = excluded.weekly_estimated,
    source_type = excluded.source_type,
    source_freshness = excluded.source_freshness,
    captured_at = excluded.captured_at,
    received_at = excluded.received_at;

  return jsonb_build_object(
    'historyWritten',
    v_write_history,
    'snapshotId',
    v_snapshot_id
  );
end;
$$;

revoke all on function public.ingest_usage(
  uuid,
  text,
  text,
  text,
  text,
  timestamptz,
  text,
  numeric,
  numeric,
  timestamptz,
  bigint,
  boolean,
  boolean,
  text,
  numeric,
  numeric,
  timestamptz,
  bigint,
  boolean,
  boolean,
  text,
  text
) from public, anon;

grant execute on function public.ingest_usage(
  uuid,
  text,
  text,
  text,
  text,
  timestamptz,
  text,
  numeric,
  numeric,
  timestamptz,
  bigint,
  boolean,
  boolean,
  text,
  numeric,
  numeric,
  timestamptz,
  bigint,
  boolean,
  boolean,
  text,
  text
) to authenticated;

create or replace function private.cleanup_retention()
returns void
language plpgsql
security definer
set search_path = ''
as $$
begin
  delete from public.usage_snapshots
  where captured_at < now() - interval '30 days';

  delete from public.notification_events
  where created_at < now() - interval '90 days';

  delete from public.processed_telegram_updates
  where processed_at < now() - interval '30 days';
end;
$$;

revoke all on function private.cleanup_retention() from public, anon, authenticated;

create or replace function private.invoke_notification_check()
returns void
language plpgsql
security definer
set search_path = ''
as $$
declare
  v_project_url text;
  v_secret_key text;
  v_cron_secret text;
begin
  select decrypted_secret
  into v_project_url
  from vault.decrypted_secrets
  where name = 'codex_usage_project_url';

  select decrypted_secret
  into v_secret_key
  from vault.decrypted_secrets
  where name = 'codex_usage_notification_secret_key';

  select decrypted_secret
  into v_cron_secret
  from vault.decrypted_secrets
  where name = 'codex_usage_notification_cron_secret';

  if v_project_url is null or v_secret_key is null or v_cron_secret is null then
    raise warning 'Codex Usage notification-check Vault secrets are not configured';
    return;
  end if;

  perform net.http_post(
    url := rtrim(v_project_url, '/') || '/functions/v1/notification-check',
    headers := jsonb_build_object(
      'Content-Type',
      'application/json',
      'apikey',
      v_secret_key,
      'x-cron-secret',
      v_cron_secret
    ),
    body := '{}'::jsonb,
    timeout_milliseconds := 10000
  );
end;
$$;

revoke all on function private.invoke_notification_check() from public, anon, authenticated;

select cron.schedule(
  'codex-usage-retention',
  '17 3 * * *',
  'select private.cleanup_retention();'
);

select cron.schedule(
  'codex-usage-notification-check',
  '*/5 * * * *',
  'select private.invoke_notification_check();'
);
