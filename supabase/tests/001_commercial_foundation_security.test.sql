begin;

set local role postgres;
create extension if not exists pgtap with schema extensions;
set local search_path = public, extensions, pgtap, auth, pg_catalog;

select plan(17);

select ok(
  not exists (
    select 1
    from pg_default_acl as defaults
    cross join lateral aclexplode(defaults.defaclacl) as privilege
    where defaults.defaclrole = 'postgres'::regrole
      and defaults.defaclnamespace = 'public'::regnamespace
      and defaults.defaclobjtype = 'r'
      and (
        privilege.grantee = 0
        or pg_get_userbyid(privilege.grantee) in (
          'anon',
          'authenticated',
          'service_role'
        )
      )
  ),
  'future public tables have no implicit Data API privileges'
);

select ok(
  not exists (
    select 1
    from pg_default_acl as defaults
    cross join lateral aclexplode(defaults.defaclacl) as privilege
    where defaults.defaclrole = 'postgres'::regrole
      and defaults.defaclnamespace = 'public'::regnamespace
      and defaults.defaclobjtype = 'S'
      and (
        privilege.grantee = 0
        or pg_get_userbyid(privilege.grantee) in (
          'anon',
          'authenticated',
          'service_role'
        )
      )
  ),
  'future public sequences have no implicit Data API privileges'
);

select ok(
  not exists (
    select 1
    from pg_default_acl as defaults
    cross join lateral aclexplode(defaults.defaclacl) as privilege
    where defaults.defaclrole = 'postgres'::regrole
      and defaults.defaclobjtype = 'f'
      and defaults.defaclnamespace in (
        0,
        'public'::regnamespace
      )
      and (
        privilege.grantee = 0
        or pg_get_userbyid(privilege.grantee) in (
          'anon',
          'authenticated',
          'service_role'
        )
      )
  ),
  'future public routines have no implicit Data API privileges'
);

select ok(
  not exists (
    select 1
    from pg_default_acl as defaults
    cross join lateral aclexplode(defaults.defaclacl) as privilege
    where defaults.defaclrole = 'postgres'::regrole
      and defaults.defaclnamespace = 'spotify_booster_private'::regnamespace
      and defaults.defaclobjtype = 'f'
      and (
        privilege.grantee = 0
        or pg_get_userbyid(privilege.grantee) in (
          'anon',
          'authenticated',
          'service_role'
        )
      )
  ),
  'future private routines have no implicit Data API privileges'
);

create function public.spotify_booster_default_acl_probe()
returns integer
language sql
immutable
parallel safe
set search_path = pg_catalog
as $function$
  select 1;
$function$;

select ok(
  not has_function_privilege(
    'anon',
    'public.spotify_booster_default_acl_probe()',
    'EXECUTE'
  )
  and not has_function_privilege(
    'authenticated',
    'public.spotify_booster_default_acl_probe()',
    'EXECUTE'
  )
  and not has_function_privilege(
    'service_role',
    'public.spotify_booster_default_acl_probe()',
    'EXECUTE'
  )
  and not has_function_privilege(
    'spotify_booster_entitlement_writer',
    'public.spotify_booster_default_acl_probe()',
    'EXECUTE'
  ),
  'a future public routine is not executable through any application role'
);

drop function public.spotify_booster_default_acl_probe();

create function spotify_booster_private.default_acl_probe()
returns integer
language sql
immutable
parallel safe
set search_path = pg_catalog
as $function$
  select 1;
$function$;

select ok(
  not has_function_privilege(
    'anon',
    'spotify_booster_private.default_acl_probe()',
    'EXECUTE'
  )
  and not has_function_privilege(
    'authenticated',
    'spotify_booster_private.default_acl_probe()',
    'EXECUTE'
  )
  and not has_function_privilege(
    'service_role',
    'spotify_booster_private.default_acl_probe()',
    'EXECUTE'
  )
  and not has_function_privilege(
    'spotify_booster_entitlement_writer',
    'spotify_booster_private.default_acl_probe()',
    'EXECUTE'
  ),
  'a future private routine is not executable through any application role'
);

drop function spotify_booster_private.default_acl_probe();

select ok(
  not has_table_privilege('service_role', 'public.profiles', 'SELECT'),
  'service_role receives no implicit profile read'
);

select ok(
  not has_table_privilege('service_role', 'public.entitlements', 'SELECT'),
  'service_role receives no implicit entitlement read'
);

select ok(
  not has_function_privilege(
    'service_role',
    'public.spotify_booster_health_v1()',
    'EXECUTE'
  ),
  'service_role receives no implicit health execution'
);

select ok(
  not has_function_privilege(
    'service_role',
    'public.spotify_booster_me_v1()',
    'EXECUTE'
  ),
  'service_role receives no implicit me execution'
);

select ok(
  not exists (
    select 1
    from pg_namespace as namespace
    cross join lateral aclexplode(
      coalesce(namespace.nspacl, acldefault('n', namespace.nspowner))
    ) as privilege
    where namespace.oid = 'public'::regnamespace
      and privilege.grantee = 0
  ),
  'PUBLIC has no public-schema privilege'
);

select ok(
  not has_schema_privilege('service_role', 'public', 'USAGE'),
  'service_role has no implicit public-schema usage'
);

select ok(
  (
    select not rolcanlogin
       and not rolinherit
       and not rolsuper
       and not rolcreatedb
       and not rolcreaterole
       and not rolreplication
       and not rolbypassrls
    from pg_roles
    where rolname = 'spotify_booster_entitlement_writer'
  ),
  'future entitlement writer is a constrained non-login role'
);

select ok(
  not pg_has_role(
    'anon',
    'spotify_booster_entitlement_writer',
    'MEMBER'
  )
  and not pg_has_role(
    'authenticated',
    'spotify_booster_entitlement_writer',
    'MEMBER'
  )
  and not pg_has_role(
    'service_role',
    'spotify_booster_entitlement_writer',
    'MEMBER'
  ),
  'no Data API role belongs to the future entitlement writer'
);

select indexes_are(
  'public',
  'profiles',
  array['profiles_pkey'],
  'profiles has only its owner primary-key index'
);

select indexes_are(
  'public',
  'entitlements',
  array['entitlements_pkey'],
  'entitlements has only its owner primary-key index'
);

select ok(
  not exists (
    select 1
    from auth.users as users
    left join public.profiles as profiles
      on profiles.user_id = users.id
    left join public.entitlements as entitlements
      on entitlements.user_id = users.id
    where profiles.user_id is null
       or entitlements.user_id is null
       or entitlements.plan <> 'free'
       or entitlements.status <> 'active'
       or entitlements.revision <> 1
  ),
  'every pre-existing Auth user was provisioned with neutral Free rows'
);

select * from finish();
rollback;
