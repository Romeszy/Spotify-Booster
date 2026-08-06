-- Revoke first; expose only the exact Phase A surface.
revoke all on table public.profiles from public, anon, authenticated;
revoke all on table public.entitlements from public, anon, authenticated;
revoke all on all sequences in schema public from public, anon, authenticated;
revoke all on schema spotify_booster_private from public, anon, authenticated;

alter table public.profiles enable row level security;
alter table public.profiles force row level security;
alter table public.entitlements enable row level security;
alter table public.entitlements force row level security;

create policy profiles_select_own
on public.profiles
for select
to authenticated
using ((select auth.uid()) = user_id);

create policy profiles_update_own
on public.profiles
for update
to authenticated
using ((select auth.uid()) = user_id)
with check ((select auth.uid()) = user_id);

create policy entitlements_select_own
on public.entitlements
for select
to authenticated
using ((select auth.uid()) = user_id);

grant usage on schema public to anon, authenticated;
grant select (user_id, display_name, created_at, updated_at)
on public.profiles to authenticated;
grant update (display_name)
on public.profiles to authenticated;
grant select (
  user_id, plan, status, features, revision, issued_at, valid_until,
  grace_until, created_at, updated_at
) on public.entitlements to authenticated;

-- A non-login role reserved for a future server-side billing writer. It is not
-- granted to anon/authenticated and has no API endpoint or login credential.
do $block$
begin
  if not exists (select 1 from pg_roles where rolname = 'spotify_booster_entitlement_writer') then
    create role spotify_booster_entitlement_writer nologin noinherit nosuperuser
      nocreatedb nocreaterole noreplication;
  end if;
end
$block$;
grant spotify_booster_entitlement_writer to postgres;
revoke all on table public.entitlements from spotify_booster_entitlement_writer;
grant select on public.entitlements to spotify_booster_entitlement_writer;
grant update (plan, status, features, revision, valid_until, grace_until)
on public.entitlements to spotify_booster_entitlement_writer;
grant usage on schema spotify_booster_private to spotify_booster_entitlement_writer;
grant execute on function spotify_booster_private.features_are_valid(text[])
to spotify_booster_entitlement_writer;

create policy entitlements_writer_select
on public.entitlements
for select
to spotify_booster_entitlement_writer
using (true);

create policy entitlements_writer_update
on public.entitlements
for update
to spotify_booster_entitlement_writer
using (true)
with check (true);

create or replace function public.spotify_booster_health_v1()
returns jsonb
language sql
immutable
parallel safe
security invoker
set search_path = pg_catalog
as $function$
  select '{"service":"spotify-booster-data","status":"ok","version":1}'::jsonb;
$function$;

revoke all on function public.spotify_booster_health_v1() from public;
grant execute on function public.spotify_booster_health_v1()
to anon, authenticated;
