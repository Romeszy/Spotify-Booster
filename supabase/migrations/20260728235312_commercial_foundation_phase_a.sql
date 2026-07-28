-- Spotify Booster commercial foundation — Phase A.
--
-- This migration is additive. The synchronized remote baseline
-- 20260728234203_remote_schema.sql is immutable and must not be rewritten.
--
-- Transactionality: Supabase's migration runner applies this file as one
-- transaction. Statements intentionally fail on object/role name collisions
-- so unexpected remote drift is never adopted silently.
--
-- Index decision: both exposed tables are keyed by user_id. Those primary-key
-- indexes cover the foreign-key cascade, owner RLS predicates, and /v1/me join;
-- Phase A has no justified secondary index.

-- The immutable baseline grants future public objects broadly. Revoke those
-- defaults before creating Phase A objects so access is explicit and deny by
-- default. service_role is also removed from future implicit grants: Phase A
-- has no service-role path and future privileged writers must opt in.
alter default privileges for role postgres in schema public
  revoke all on tables from anon, authenticated, service_role;
alter default privileges for role postgres in schema public
  revoke all on sequences from anon, authenticated, service_role;
alter default privileges for role postgres in schema public
  revoke all on routines from anon, authenticated, service_role;

-- PostgreSQL's built-in PUBLIC EXECUTE default is global. A schema-scoped
-- revoke cannot remove it, so this statement deliberately has no IN SCHEMA.
alter default privileges for role postgres
  revoke execute on routines from public;

-- Remove inherited schema reachability as well. The two public API roles get
-- USAGE back explicitly after the Phase A surface is defined; service_role has
-- no implicit Phase A path.
revoke all on schema public from public, anon, authenticated, service_role;

create schema spotify_booster_private authorization postgres;

-- Private helper routines must never become callable through PostgREST merely
-- because they were created by postgres.
alter default privileges for role postgres in schema spotify_booster_private
  revoke execute on routines from anon, authenticated, service_role;
revoke all on schema spotify_booster_private
  from public, anon, authenticated, service_role;

create function spotify_booster_private.features_are_valid(value text[])
returns boolean
language sql
immutable
parallel safe
strict
set search_path = pg_catalog
as $function$
  select cardinality(value) <= 10
     and cardinality(value) = (
       select count(distinct feature)
       from unnest(value) as feature
     )
     and value <@ array[
       'AdvancedGain',
       'ExpertMode',
       'AdvancedBassBoost',
       'Equalizer',
       'CustomProfiles',
       'GlobalHotkeys',
       'TrayAdvancedControls',
       'AutoStart',
       'AdvancedDiagnostics',
       'FuturePremiumFeatures'
     ]::text[];
$function$;

alter function spotify_booster_private.features_are_valid(text[])
  owner to postgres;
revoke all on function spotify_booster_private.features_are_valid(text[])
  from public, anon, authenticated, service_role;

create table public.profiles (
  user_id uuid primary key references auth.users(id) on delete cascade,
  display_name text null,
  created_at timestamptz not null default now(),
  updated_at timestamptz not null default now(),
  constraint profiles_display_name_valid check (
    display_name is null or (
      display_name = btrim(display_name)
      and char_length(display_name) between 1 and 80
      and display_name !~ '[[:cntrl:]]'
    )
  ),
  constraint profiles_timestamps_valid check (updated_at >= created_at)
);

alter table public.profiles owner to postgres;
comment on table public.profiles is
  'User-owned, non-authoritative display profile.';

create table public.entitlements (
  user_id uuid primary key references auth.users(id) on delete cascade,
  plan text not null,
  status text not null,
  features text[] not null default '{}'::text[],
  revision bigint not null,
  issued_at timestamptz not null default now(),
  valid_until timestamptz null,
  grace_until timestamptz null,
  created_at timestamptz not null default now(),
  updated_at timestamptz not null default now(),
  constraint entitlements_plan_valid check (plan in ('free', 'pro')),
  constraint entitlements_status_valid check (status in (
    'active',
    'trialing',
    'past_due',
    'canceled',
    'unpaid',
    'incomplete',
    'incomplete_expired',
    'paused',
    'refunded',
    'revoked'
  )),
  constraint entitlements_features_valid check (
    spotify_booster_private.features_are_valid(features)
  ),
  constraint entitlements_revision_valid check (revision > 0),
  constraint entitlements_plan_duration_valid check (
    (plan = 'free' and valid_until is null and grace_until is null)
    or (plan = 'pro' and valid_until is not null)
  ),
  constraint entitlements_valid_until_valid check (
    valid_until is null or valid_until >= issued_at
  ),
  constraint entitlements_grace_until_valid check (
    grace_until is null
    or (valid_until is not null and grace_until >= valid_until)
  ),
  constraint entitlements_timestamps_valid check (updated_at >= created_at)
);

alter table public.entitlements owner to postgres;
comment on table public.entitlements is
  'Server-authored effective commercial entitlement; no Stripe data.';

create function spotify_booster_private.touch_profile()
returns trigger
language plpgsql
security definer
set search_path = pg_catalog
as $function$
begin
  if new.user_id is distinct from old.user_id then
    raise exception using
      errcode = '22000',
      message = 'immutable_profile_owner';
  end if;

  if new.created_at is distinct from old.created_at then
    raise exception using
      errcode = '22000',
      message = 'immutable_profile_created_at';
  end if;

  new.updated_at := clock_timestamp();
  return new;
end;
$function$;

alter function spotify_booster_private.touch_profile() owner to postgres;
revoke all on function spotify_booster_private.touch_profile()
  from public, anon, authenticated, service_role;

create function spotify_booster_private.touch_entitlement()
returns trigger
language plpgsql
security definer
set search_path = pg_catalog
as $function$
begin
  if new.user_id is distinct from old.user_id then
    raise exception using
      errcode = '22000',
      message = 'immutable_entitlement_owner';
  end if;

  if new.issued_at is distinct from old.issued_at then
    raise exception using
      errcode = '22000',
      message = 'immutable_entitlement_issued_at';
  end if;

  if new.revision is distinct from old.revision + 1 then
    raise exception using
      errcode = '22000',
      message = 'entitlement_revision_must_increment_once';
  end if;

  if new.created_at is distinct from old.created_at then
    raise exception using
      errcode = '22000',
      message = 'immutable_entitlement_created_at';
  end if;

  new.updated_at := clock_timestamp();
  return new;
end;
$function$;

alter function spotify_booster_private.touch_entitlement()
  owner to postgres;
revoke all on function spotify_booster_private.touch_entitlement()
  from public, anon, authenticated, service_role;

create function spotify_booster_private.handle_auth_user_created()
returns trigger
language plpgsql
security definer
set search_path = pg_catalog
as $function$
begin
  insert into public.profiles (user_id)
  values (new.id)
  on conflict (user_id) do nothing;

  insert into public.entitlements (
    user_id,
    plan,
    status,
    features,
    revision,
    issued_at,
    valid_until,
    grace_until
  )
  values (
    new.id,
    'free',
    'active',
    '{}'::text[],
    1,
    clock_timestamp(),
    null,
    null
  )
  on conflict (user_id) do nothing;

  return new;
end;
$function$;

alter function spotify_booster_private.handle_auth_user_created()
  owner to postgres;
revoke all on function spotify_booster_private.handle_auth_user_created()
  from public, anon, authenticated, service_role;

create trigger profiles_touch_before_update
before update on public.profiles
for each row
execute function spotify_booster_private.touch_profile();

create trigger entitlements_touch_before_update
before update on public.entitlements
for each row
execute function spotify_booster_private.touch_entitlement();

create trigger spotify_booster_auth_user_created
after insert on auth.users
for each row
execute function spotify_booster_private.handle_auth_user_created();

-- Existing Auth users predate the new signup trigger. Provision only neutral
-- Free rows and never trust user metadata for an authorization decision.
insert into public.profiles (user_id)
select id
from auth.users
on conflict (user_id) do nothing;

insert into public.entitlements (
  user_id,
  plan,
  status,
  features,
  revision,
  issued_at,
  valid_until,
  grace_until
)
select
  id,
  'free',
  'active',
  '{}'::text[],
  1,
  clock_timestamp(),
  null,
  null
from auth.users
on conflict (user_id) do nothing;

-- Revoke first, then expose only the exact Phase A surface.
revoke all on table public.profiles
  from public, anon, authenticated, service_role;
revoke all on table public.entitlements
  from public, anon, authenticated, service_role;

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
  on public.profiles
  to authenticated;
grant update (display_name)
  on public.profiles
  to authenticated;
grant select (
  user_id,
  plan,
  status,
  features,
  revision,
  issued_at,
  valid_until,
  grace_until,
  created_at,
  updated_at
)
  on public.entitlements
  to authenticated;

-- Phase A has no entitlement-writing endpoint or credential. This role exists
-- only to prove the future server-writer boundary and deliberately fails the
-- migration if a role of the same name already exists with unknown attributes.
create role spotify_booster_entitlement_writer
  nologin
  noinherit
  nosuperuser
  nocreatedb
  nocreaterole
  noreplication
  nobypassrls;

grant spotify_booster_entitlement_writer to postgres;
grant usage on schema public to spotify_booster_entitlement_writer;
grant usage on schema spotify_booster_private
  to spotify_booster_entitlement_writer;
grant execute on function
  spotify_booster_private.features_are_valid(text[])
  to spotify_booster_entitlement_writer;
grant select on public.entitlements
  to spotify_booster_entitlement_writer;
grant update (
  plan,
  status,
  features,
  revision,
  valid_until,
  grace_until
)
  on public.entitlements
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

-- Public readiness capability: constant, data-free, and table-independent.
create function public.spotify_booster_health_v1()
returns jsonb
language sql
immutable
parallel safe
security invoker
set search_path = pg_catalog
as $function$
  select '{"service":"spotify-booster-data","status":"ok","version":1}'::jsonb;
$function$;

alter function public.spotify_booster_health_v1() owner to postgres;
revoke all on function public.spotify_booster_health_v1()
  from public, anon, authenticated, service_role;
grant execute on function public.spotify_booster_health_v1()
  to anon, authenticated;

-- User-scoped read model for GET /v1/me. Identity is sourced exclusively from
-- the verified JWT context; callers cannot provide a user identifier.
create function public.spotify_booster_me_v1()
returns jsonb
language sql
stable
parallel safe
security invoker
set search_path = pg_catalog
as $function$
  select pg_catalog.jsonb_build_object(
    'userId', profile.user_id,
    'displayName', profile.display_name,
    'plan', entitlement.plan,
    'status', entitlement.status,
    'revision', entitlement.revision,
    'features', pg_catalog.to_jsonb(entitlement.features)
  )
  from public.profiles as profile
  inner join public.entitlements as entitlement
    on entitlement.user_id = profile.user_id
  where profile.user_id = (select auth.uid());
$function$;

alter function public.spotify_booster_me_v1() owner to postgres;
revoke all on function public.spotify_booster_me_v1()
  from public, anon, authenticated, service_role;
grant execute on function public.spotify_booster_me_v1()
  to authenticated;

-- Rollback policy:
-- * A failed db push is rolled back atomically by the Supabase migration
--   runner, including migration-history state.
-- * After a successful deployment, do not edit this file or restore the
--   baseline's permissive default ACLs. Snapshot Phase A data, then ship a new,
--   reviewed forward migration using the destructive sequence below:
--   drop function public.spotify_booster_me_v1();
--   drop function public.spotify_booster_health_v1();
--   drop trigger spotify_booster_auth_user_created on auth.users;
--   drop table public.entitlements;
--   drop table public.profiles;
--   revoke spotify_booster_entitlement_writer from postgres;
--   revoke usage on schema public
--     from spotify_booster_entitlement_writer;
--   drop schema spotify_booster_private cascade;
--   drop role spotify_booster_entitlement_writer;
-- The hardened public default privileges intentionally survive rollback.
