alter table public.notification_events
  add column attempt_count integer not null default 0
    check (attempt_count between 0 and 20),
  add column next_attempt_at timestamptz;

drop index public.notification_events_pending_idx;

create index notification_events_delivery_retry_idx
  on public.notification_events (status, next_attempt_at, created_at)
  where status in ('pending', 'failed');
