begin;

create extension if not exists pgtap with schema extensions;
select plan(3);

insert into auth.users (id, email)
values
  ('10000000-0000-4000-8000-000000000001', 'owner-one@example.invalid'),
  ('20000000-0000-4000-8000-000000000002', 'owner-two@example.invalid');

insert into public.profiles (id)
values
  ('10000000-0000-4000-8000-000000000001'),
  ('20000000-0000-4000-8000-000000000002');

insert into public.devices (id, profile_id, device_name, app_version)
values
  (
    '11000000-0000-4000-8000-000000000001',
    '10000000-0000-4000-8000-000000000001',
    'Owner one device',
    '1.0.0'
  ),
  (
    '22000000-0000-4000-8000-000000000002',
    '20000000-0000-4000-8000-000000000002',
    'Owner two device',
    '1.0.0'
  );

set local role authenticated;
set local request.jwt.claims =
  '{"sub":"10000000-0000-4000-8000-000000000001","role":"authenticated"}';

select is(
  (select count(*) from public.profiles),
  1::bigint,
  'an authenticated user sees only their profile'
);

select is(
  (select count(*) from public.devices),
  1::bigint,
  'an authenticated user sees only their device'
);

update public.devices
set device_name = 'Cross-profile tamper'
where profile_id = '20000000-0000-4000-8000-000000000002';

reset role;

select is(
  (
    select device_name
    from public.devices
    where id = '22000000-0000-4000-8000-000000000002'
  ),
  'Owner two device',
  'RLS prevents a cross-profile update'
);

select * from finish();
rollback;
