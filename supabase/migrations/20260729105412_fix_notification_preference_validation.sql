do $$
declare
  v_constraint_name text;
begin
  select constraint_row.conname
  into v_constraint_name
  from pg_constraint as constraint_row
  where constraint_row.conrelid = 'public.notification_preferences'::regclass
    and constraint_row.contype = 'c'
    and pg_get_constraintdef(constraint_row.oid) like '%valid_thresholds%';

  if v_constraint_name is not null then
    execute format(
      'alter table public.notification_preferences drop constraint %I',
      v_constraint_name
    );
  end if;
end;
$$;

alter table public.notification_preferences
  add constraint notification_preferences_thresholds_check
  check (
    array_position(short_term_thresholds, null) is null
    and array_position(weekly_thresholds, null) is null
    and cardinality(short_term_thresholds) between 1 and 10
    and cardinality(weekly_thresholds) between 1 and 10
    and 0 <= all(short_term_thresholds)
    and 100 >= all(short_term_thresholds)
    and 0 <= all(weekly_thresholds)
    and 100 >= all(weekly_thresholds)
  );

drop function private.valid_thresholds(numeric[]);
