begin;

select plan(110);

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
select ok((select not exists (select 1 from aclexplode(coalesce(relacl, acldefault('r', relowner))) where grantee=0) from pg_class where oid='public.profiles'::regclass), 'PUBLIC has no profile table privileges');
select ok((select not exists (select 1 from aclexplode(coalesce(relacl, acldefault('r', relowner))) where grantee=0) from pg_class where oid='public.entitlements'::regclass), 'PUBLIC has no entitlement table privileges');
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

select function_returns('public', 'spotify_booster_me_v1', array[]::text[], 'jsonb', 'me RPC returns jsonb');
select is((select count(*)::integer from pg_proc where pronamespace='public'::regnamespace and proname='spotify_booster_me_v1'), 1, 'me RPC has one overload');
select is((select pronargs from pg_proc where oid='public.spotify_booster_me_v1()'::regprocedure), 0::smallint, 'me RPC accepts no client identity');
select is((select provolatile from pg_proc where oid='public.spotify_booster_me_v1()'::regprocedure), 's'::"char", 'me RPC is stable');
select is((select proparallel from pg_proc where oid='public.spotify_booster_me_v1()'::regprocedure), 's'::"char", 'me RPC is parallel safe');
select is((select prosecdef from pg_proc where oid='public.spotify_booster_me_v1()'::regprocedure), false, 'me RPC is security invoker');
select is((select proconfig from pg_proc where oid='public.spotify_booster_me_v1()'::regprocedure), array['search_path=pg_catalog']::text[], 'me RPC search_path is fixed');
select is((select pg_get_userbyid(proowner) from pg_proc where oid='public.spotify_booster_me_v1()'::regprocedure), 'postgres', 'me RPC has the expected owner');
select ok((select not exists (select 1 from aclexplode(coalesce(proacl, acldefault('f', proowner))) where grantee=0 and privilege_type='EXECUTE') from pg_proc where oid='public.spotify_booster_me_v1()'::regprocedure), 'PUBLIC cannot execute me RPC');
select ok(not has_function_privilege('anon', 'public.spotify_booster_me_v1()', 'EXECUTE'), 'anon cannot execute me RPC');
select ok(has_function_privilege('authenticated', 'public.spotify_booster_me_v1()', 'EXECUTE'), 'authenticated executes me RPC');
select is((select proargnames from pg_proc where oid='public.spotify_booster_me_v1()'::regprocedure), null::text[], 'me RPC exposes no named identity argument');

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
select is((public.spotify_booster_me_v1()->>'userId')::uuid, '00000000-0000-0000-0000-00000000000a'::uuid, 'A me RPC is JWT scoped to A');
select is(public.spotify_booster_me_v1()->>'plan', 'free', 'A me RPC returns Free entitlement');
select is(public.spotify_booster_me_v1()->'features', '[]'::jsonb, 'A me RPC returns the feature array');
select ok(not (public.spotify_booster_me_v1()::text like '%00000000-0000-0000-0000-00000000000b%'), 'A me RPC never exposes B');
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
set local search_path = public, extensions, pg_catalog;
update public.entitlements set plan='pro', valid_until=issued_at+interval '30 days', revision=revision+1 where user_id='00000000-0000-0000-0000-00000000000a';
reset role;
select is((select revision from public.entitlements where user_id='00000000-0000-0000-0000-00000000000a'), 2::bigint, 'writer increments exactly once');
select ok((select updated_at > created_at from public.entitlements where user_id='00000000-0000-0000-0000-00000000000a'), 'entitlement timestamp assigned server-side');

select ok(not has_table_privilege('authenticated', 'public.profiles', 'INSERT'), 'authenticated cannot insert profiles');
select ok(not has_table_privilege('authenticated', 'public.profiles', 'DELETE'), 'authenticated cannot delete profiles');
select ok(has_column_privilege('authenticated', 'public.entitlements', 'plan', 'SELECT'), 'authenticated selects public entitlement plan');
select ok(not has_function_privilege('anon', 'spotify_booster_private.touch_profile()', 'EXECUTE'), 'anon cannot execute profile trigger helper');
select ok(not has_function_privilege('authenticated', 'spotify_booster_private.touch_profile()', 'EXECUTE'), 'authenticated cannot execute profile trigger helper');
select ok(not has_function_privilege('anon', 'spotify_booster_private.touch_entitlement()', 'EXECUTE'), 'anon cannot execute entitlement trigger helper');
select ok(not has_function_privilege('authenticated', 'spotify_booster_private.touch_entitlement()', 'EXECUTE'), 'authenticated cannot execute entitlement trigger helper');
select ok(not has_function_privilege('anon', 'spotify_booster_private.handle_auth_user_created()', 'EXECUTE'), 'anon cannot execute signup helper');
select ok(not has_function_privilege('authenticated', 'spotify_booster_private.handle_auth_user_created()', 'EXECUTE'), 'authenticated cannot execute signup helper');
select ok((select not exists (select 1 from aclexplode(coalesce(proacl, acldefault('f', proowner))) where grantee=0 and privilege_type='EXECUTE') from pg_proc where oid='spotify_booster_private.touch_profile()'::regprocedure), 'PUBLIC cannot execute profile trigger helper');
select ok((select not exists (select 1 from aclexplode(coalesce(proacl, acldefault('f', proowner))) where grantee=0 and privilege_type='EXECUTE') from pg_proc where oid='spotify_booster_private.touch_entitlement()'::regprocedure), 'PUBLIC cannot execute entitlement trigger helper');
select ok((select not exists (select 1 from aclexplode(coalesce(proacl, acldefault('f', proowner))) where grantee=0 and privilege_type='EXECUTE') from pg_proc where oid='spotify_booster_private.handle_auth_user_created()'::regprocedure), 'PUBLIC cannot execute signup helper');
select is((select proconfig from pg_proc where oid='spotify_booster_private.touch_profile()'::regprocedure), array['search_path=pg_catalog']::text[], 'profile helper search_path fixed');
select is((select proconfig from pg_proc where oid='spotify_booster_private.touch_entitlement()'::regprocedure), array['search_path=pg_catalog']::text[], 'entitlement helper search_path fixed');
select is((select proconfig from pg_proc where oid='spotify_booster_private.handle_auth_user_created()'::regprocedure), array['search_path=pg_catalog']::text[], 'signup helper search_path fixed');
select has_trigger('public', 'profiles', 'profiles_touch_before_update', 'profile update trigger installed');
select has_trigger('public', 'entitlements', 'entitlements_touch_before_update', 'entitlement update trigger installed');
select has_trigger('auth', 'users', 'spotify_booster_auth_user_created', 'signup trigger installed');

select throws_ok($$update public.profiles set user_id='00000000-0000-0000-0000-00000000000b' where user_id='00000000-0000-0000-0000-00000000000a'$$, '22000', 'immutable_profile_owner', 'profile owner immutable');
select throws_ok($$update public.profiles set created_at=created_at-interval '1 day' where user_id='00000000-0000-0000-0000-00000000000a'$$, '22000', 'immutable_profile_created_at', 'profile created_at immutable');
select throws_ok($$update public.entitlements set issued_at=issued_at-interval '1 day', revision=revision+1 where user_id='00000000-0000-0000-0000-00000000000a'$$, '22000', 'immutable_entitlement_issued_at', 'entitlement issued_at immutable');
select throws_ok($$update public.entitlements set user_id='00000000-0000-0000-0000-00000000000b', revision=revision+1 where user_id='00000000-0000-0000-0000-00000000000a'$$, '22000', 'immutable_entitlement_owner', 'entitlement owner immutable');
select ok(not has_column_privilege('spotify_booster_entitlement_writer', 'public.entitlements', 'user_id', 'UPDATE'), 'controlled writer cannot update entitlement owner column');
select throws_ok($$update public.entitlements set revision=revision-1 where user_id='00000000-0000-0000-0000-00000000000a'$$, '22000', 'entitlement_revision_must_increment_once', 'revision decrement rejected');
select throws_ok($$update public.entitlements set features=array['AdvancedGain','ExpertMode','AdvancedBassBoost','Equalizer','CustomProfiles','GlobalHotkeys','TrayAdvancedControls','AutoStart','AdvancedDiagnostics','FuturePremiumFeatures','Unknown'], revision=revision+1 where user_id='00000000-0000-0000-0000-00000000000a'$$, '23514', null, 'more than ten features rejected');
select throws_ok($$update public.entitlements set plan='free', valid_until=null, grace_until=issued_at+interval '1 day', revision=revision+1 where user_id='00000000-0000-0000-0000-00000000000a'$$, '23514', null, 'Free grace timestamp rejected');
select throws_ok($$update public.entitlements set plan='pro', valid_until=null, grace_until=null, revision=revision+1 where user_id='00000000-0000-0000-0000-00000000000a'$$, '23514', null, 'Pro without valid_until rejected');
select throws_ok($$update public.entitlements set valid_until=issued_at-interval '1 second', revision=revision+1 where user_id='00000000-0000-0000-0000-00000000000a'$$, '23514', null, 'valid_until before issued_at rejected');
select throws_ok($$update public.entitlements set grace_until=valid_until-interval '1 second', revision=revision+1 where user_id='00000000-0000-0000-0000-00000000000a'$$, '23514', null, 'grace before valid_until rejected');

delete from auth.users where id='00000000-0000-0000-0000-00000000000b';
select is((select count(*)::integer from public.profiles where user_id='00000000-0000-0000-0000-00000000000b'), 0, 'auth deletion cascades profile');
select is((select count(*)::integer from public.entitlements where user_id='00000000-0000-0000-0000-00000000000b'), 0, 'auth deletion cascades entitlement');
insert into auth.users (id, email, raw_user_meta_data) values ('00000000-0000-0000-0000-00000000000b', 'b-again@example.invalid', '{"plan":"pro"}'::jsonb);
select is((select count(*)::integer from public.profiles where user_id='00000000-0000-0000-0000-00000000000b'), 1, 're-registration recreates one profile');
select is((select plan from public.entitlements where user_id='00000000-0000-0000-0000-00000000000b'), 'free', 're-registration recreates Free entitlement');

select * from finish();
rollback;
