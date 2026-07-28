begin;

select plan(59);

select has_table('public', 'profiles', 'profiles exists');
select has_table('public', 'entitlements', 'entitlements exists');
select hasnt_table('public', 'devices', 'devices is deferred');
select hasnt_table('public', 'security_events', 'security events are deferred');
select hasnt_table('public', 'billing_subscriptions', 'billing is deferred');
select hasnt_table('public', 'stripe_webhook_events', 'webhooks are deferred');
select col_is_pk('public', 'profiles', 'user_id', 'profile owner is the PK');
select col_is_pk('public', 'entitlements', 'user_id', 'entitlement owner is the PK');
select fk_ok('public', 'profiles', 'user_id', 'auth', 'users', 'id', 'profile cascades from auth user');
select fk_ok('public', 'entitlements', 'user_id', 'auth', 'users', 'id', 'entitlement cascades from auth user');
select is((select relrowsecurity from pg_class where oid = 'public.profiles'::regclass), true, 'profiles RLS enabled');
select is((select relforcerowsecurity from pg_class where oid = 'public.profiles'::regclass), true, 'profiles RLS forced');
select is((select relrowsecurity from pg_class where oid = 'public.entitlements'::regclass), true, 'entitlements RLS enabled');
select is((select relforcerowsecurity from pg_class where oid = 'public.entitlements'::regclass), true, 'entitlements RLS forced');
select policies_are('public', 'profiles', array['profiles_select_own','profiles_update_own'], 'only owner profile policies');
select policies_are('public', 'entitlements', array['entitlements_select_own','entitlements_writer_select','entitlements_writer_update'], 'only owner read and future writer policies');

select ok(not has_table_privilege('anon', 'public.profiles', 'SELECT'), 'anon cannot select profiles');
select ok(not has_table_privilege('anon', 'public.entitlements', 'SELECT'), 'anon cannot select entitlements');
select ok(not has_table_privilege('anon', 'public.profiles', 'INSERT'), 'anon cannot insert profiles');
select ok(not has_table_privilege('anon', 'public.entitlements', 'UPDATE'), 'anon cannot update entitlements');
select ok(has_schema_privilege('anon', 'public', 'USAGE'), 'anon has PostgREST schema usage');
select ok(not has_schema_privilege('anon', 'spotify_booster_private', 'USAGE'), 'anon cannot use helper schema');
select ok(not has_schema_privilege('authenticated', 'spotify_booster_private', 'USAGE'), 'authenticated cannot use helper schema');
select ok(has_column_privilege('authenticated', 'public.profiles', 'display_name', 'UPDATE'), 'authenticated updates display_name');
select ok(not has_column_privilege('authenticated', 'public.profiles', 'user_id', 'UPDATE'), 'authenticated cannot update owner');
select ok(not has_column_privilege('authenticated', 'public.profiles', 'created_at', 'UPDATE'), 'authenticated cannot update created_at');
select ok(not has_column_privilege('authenticated', 'public.profiles', 'updated_at', 'UPDATE'), 'authenticated cannot update updated_at');
select ok(not has_table_privilege('authenticated', 'public.entitlements', 'UPDATE'), 'authenticated cannot update entitlements');
select ok(not has_table_privilege('authenticated', 'public.entitlements', 'INSERT'), 'authenticated cannot insert entitlements');
select ok(not has_table_privilege('authenticated', 'public.entitlements', 'DELETE'), 'authenticated cannot delete entitlements');

select function_returns('public', 'spotify_booster_health_v1', array[]::text[], 'jsonb', 'health returns jsonb');
select is((select count(*)::integer from pg_proc where pronamespace='public'::regnamespace and proname='spotify_booster_health_v1'), 1, 'health has one overload');
select is((select provolatile from pg_proc where oid='public.spotify_booster_health_v1()'::regprocedure), 'i'::"char", 'health is immutable');
select is((select proparallel from pg_proc where oid='public.spotify_booster_health_v1()'::regprocedure), 's'::"char", 'health is parallel safe');
select is((select prosecdef from pg_proc where oid='public.spotify_booster_health_v1()'::regprocedure), false, 'health is security invoker');
select is((select proconfig from pg_proc where oid='public.spotify_booster_health_v1()'::regprocedure), array['search_path=pg_catalog']::text[], 'health search_path is fixed');
select ok((select not exists (select 1 from aclexplode(coalesce(proacl, acldefault('f', proowner))) where grantee = 0 and privilege_type = 'EXECUTE') from pg_proc where oid='public.spotify_booster_health_v1()'::regprocedure), 'PUBLIC cannot execute health');
select ok(has_function_privilege('anon', 'public.spotify_booster_health_v1()', 'EXECUTE'), 'anon executes health');
select ok(has_function_privilege('authenticated', 'public.spotify_booster_health_v1()', 'EXECUTE'), 'authenticated executes health');
select is(public.spotify_booster_health_v1(), '{"service":"spotify-booster-data","status":"ok","version":1}'::jsonb, 'health response exact');
select is((select count(*)::integer from pg_depend d join pg_proc p on p.oid=d.objid join pg_class c on c.oid=d.refobjid where p.oid='public.spotify_booster_health_v1()'::regprocedure), 0, 'health has no relation dependency');

insert into auth.users (id, email, raw_user_meta_data)
values
  ('00000000-0000-0000-0000-00000000000a', 'a@example.invalid', '{"plan":"pro","display_name":"attacker"}'::jsonb),
  ('00000000-0000-0000-0000-00000000000b', 'b@example.invalid', '{}'::jsonb);

select is((select count(*)::integer from public.profiles), 2, 'signup creates profiles');
select is((select count(*)::integer from public.entitlements), 2, 'signup creates entitlements');
select is((select plan from public.entitlements where user_id='00000000-0000-0000-0000-00000000000a'), 'free', 'metadata cannot grant Pro');
select is((select status from public.entitlements where user_id='00000000-0000-0000-0000-00000000000a'), 'active', 'signup entitlement active');
select is((select revision from public.entitlements where user_id='00000000-0000-0000-0000-00000000000a'), 1::bigint, 'signup revision one');
select is((select display_name from public.profiles where user_id='00000000-0000-0000-0000-00000000000a'), null, 'metadata cannot set profile');

set local role authenticated;
set local request.jwt.claim.sub = '00000000-0000-0000-0000-00000000000a';
select results_eq('select user_id from public.profiles order by user_id', $$values ('00000000-0000-0000-0000-00000000000a'::uuid)$$, 'A reads only own profile');
select results_eq('select user_id from public.entitlements order by user_id', $$values ('00000000-0000-0000-0000-00000000000a'::uuid)$$, 'A reads only own entitlement');
select lives_ok($$update public.profiles set display_name='Alice' where user_id='00000000-0000-0000-0000-00000000000a'$$, 'A updates own display name');
select results_eq($$update public.profiles set display_name='Stolen' where user_id='00000000-0000-0000-0000-00000000000b' returning user_id$$, $$select null::uuid where false$$, 'A cannot update B');
reset role;

select ok((select updated_at > created_at from public.profiles where user_id='00000000-0000-0000-0000-00000000000a'), 'profile timestamp assigned server-side');
select throws_ok($$update public.entitlements set revision=revision where user_id='00000000-0000-0000-0000-00000000000a'$$, '22000', 'entitlement_revision_must_increment_once', 'same revision rejected');
select throws_ok($$update public.entitlements set revision=revision+2 where user_id='00000000-0000-0000-0000-00000000000a'$$, '22000', 'entitlement_revision_must_increment_once', 'revision jump rejected');
select throws_ok($$update public.entitlements set features=array['Equalizer','Equalizer'], revision=revision+1 where user_id='00000000-0000-0000-0000-00000000000a'$$, '23514', null, 'duplicate feature rejected');
select throws_ok($$update public.entitlements set features=array['Unknown'], revision=revision+1 where user_id='00000000-0000-0000-0000-00000000000a'$$, '23514', null, 'unknown feature rejected');
select throws_ok($$update public.entitlements set plan='free', valid_until=now()+interval '1 day', revision=revision+1 where user_id='00000000-0000-0000-0000-00000000000a'$$, '23514', null, 'free duration rejected');

set local role spotify_booster_entitlement_writer;
select lives_ok($$update public.entitlements set plan='pro', valid_until=issued_at+interval '30 days', revision=revision+1 where user_id='00000000-0000-0000-0000-00000000000a'$$, 'controlled writer updates with exact revision');
reset role;
select is((select revision from public.entitlements where user_id='00000000-0000-0000-0000-00000000000a'), 2::bigint, 'writer increments exactly once');

select * from finish();
rollback;
