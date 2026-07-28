-- User-scoped read model for GET /v1/me. The caller supplies no identity
-- parameter: PostgREST establishes auth.uid() from the verified bearer JWT.
create or replace function public.spotify_booster_me_v1()
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
revoke all on function public.spotify_booster_me_v1() from public, anon;
grant execute on function public.spotify_booster_me_v1() to authenticated;
