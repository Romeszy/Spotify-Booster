-- Phase A commercial schema. No Stripe, device, or privileged credential data.
create schema if not exists spotify_booster_private;
revoke all on schema spotify_booster_private from public, anon, authenticated;

create or replace function spotify_booster_private.features_are_valid(value text[])
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

revoke all on function spotify_booster_private.features_are_valid(text[])
from public, anon, authenticated;

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
    'active', 'trialing', 'past_due', 'canceled', 'unpaid', 'incomplete',
    'incomplete_expired', 'paused', 'refunded', 'revoked'
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
    grace_until is null or (valid_until is not null and grace_until >= valid_until)
  ),
  constraint entitlements_timestamps_valid check (updated_at >= created_at)
);

comment on table public.profiles is 'User-owned, non-authoritative display profile.';
comment on table public.entitlements is 'Server-authored effective commercial entitlement; no Stripe data.';
