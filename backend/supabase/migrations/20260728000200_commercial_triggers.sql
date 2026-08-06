create or replace function spotify_booster_private.touch_profile()
returns trigger
language plpgsql
security definer
set search_path = pg_catalog
as $function$
begin
  if new.user_id is distinct from old.user_id then
    raise exception using errcode = '22000', message = 'immutable_profile_owner';
  end if;
  if new.created_at is distinct from old.created_at then
    raise exception using errcode = '22000', message = 'immutable_profile_created_at';
  end if;
  new.updated_at := clock_timestamp();
  return new;
end;
$function$;

create or replace function spotify_booster_private.touch_entitlement()
returns trigger
language plpgsql
security definer
set search_path = pg_catalog
as $function$
begin
  if new.user_id is distinct from old.user_id then
    raise exception using errcode = '22000', message = 'immutable_entitlement_owner';
  end if;
  if new.issued_at is distinct from old.issued_at then
    raise exception using errcode = '22000', message = 'immutable_entitlement_issued_at';
  end if;
  if new.revision is distinct from old.revision + 1 then
    raise exception using errcode = '22000', message = 'entitlement_revision_must_increment_once';
  end if;
  if new.created_at is distinct from old.created_at then
    raise exception using errcode = '22000', message = 'immutable_entitlement_created_at';
  end if;
  new.updated_at := clock_timestamp();
  return new;
end;
$function$;

create or replace function spotify_booster_private.handle_auth_user_created()
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
    user_id, plan, status, features, revision, issued_at,
    valid_until, grace_until
  )
  values (new.id, 'free', 'active', '{}'::text[], 1, clock_timestamp(), null, null)
  on conflict (user_id) do nothing;

  return new;
end;
$function$;

revoke all on function spotify_booster_private.touch_profile()
from public, anon, authenticated;
revoke all on function spotify_booster_private.touch_entitlement()
from public, anon, authenticated;
revoke all on function spotify_booster_private.handle_auth_user_created()
from public, anon, authenticated;

create trigger profiles_touch_before_update
before update on public.profiles
for each row execute function spotify_booster_private.touch_profile();

create trigger entitlements_touch_before_update
before update on public.entitlements
for each row execute function spotify_booster_private.touch_entitlement();

create trigger spotify_booster_auth_user_created
after insert on auth.users
for each row execute function spotify_booster_private.handle_auth_user_created();
