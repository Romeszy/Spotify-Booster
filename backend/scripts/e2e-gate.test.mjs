import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import test from 'node:test';

const e2e = readFileSync(new URL('./e2e-auth-api.mjs', import.meta.url), 'utf8');
const atomicity = readFileSync(new URL('./test-migration-atomicity.sh', import.meta.url),
  'utf8');
const finalGate = readFileSync(
  new URL('./run-local-auth-api-gate.sh', import.meta.url),
  'utf8');

test('real Auth/API gate keeps strict TLS and excludes privileged credentials', () => {
  assert.match(e2e, /https:\/\/127\.0\.0\.1:54321/u);
  assert.match(e2e, /rejectUnauthorized: true/u);
  assert.doesNotMatch(e2e, /rejectUnauthorized:\s*false/u);
  assert.doesNotMatch(e2e, /NODE_TLS_REJECT_UNAUTHORIZED/u);
  assert.doesNotMatch(e2e, /service_role|sb_secret/u);
  assert.match(e2e, /includeApiKey: false/u);
  assert.match(e2e, /headers\.authorization = `Bearer \$\{token\}`/u);
});

test('real Auth/API gate covers all destructive transitions and cleanup', () => {
  for (const evidence of [
    'password_9_user_created',
    'api_me_free_entitlement',
    'rpc_required_headers_failed',
    "for (const relation of ['profiles', 'entitlements'])",
    'refresh_r0_direct_parent_child_mismatch',
    'refresh_r0_reuse_accepted',
    'refresh_r1_family_survived',
    'refresh_r2_family_survived',
    'logout_refresh_survived',
    'readiness_503_not_redacted',
    'readiness_200_invalid',
    'restartStoppedContainers();',
    'cleanupUsers();'
  ])
    assert.ok(e2e.includes(evidence), `missing bounded gate evidence: ${evidence}`);
  assert.match(e2e, /finally\s*\{[\s\S]*restartStoppedContainers\(\);[\s\S]*cleanupUsers\(\);/u);
});

test('migration probe proves object and version rollback before safe cleanup', () => {
  const runner = atomicity.indexOf('"$CLI" migration up');
  const sqlState = atomicity.indexOf("errcode = 'P0001'");
  const sentinel = atomicity.indexOf(
    "message = 'SPOTIFY_BOOSTER_ATOMICITY_SENTINEL_7F31A9C2'");
  const diagnostics = atomicity.indexOf('get stacked diagnostics');
  const messageCheck = atomicity.indexOf('caught_message is distinct from');
  const sqlStateCheck = atomicity.indexOf(
    "caught_sqlstate is distinct from 'P0001'");
  const executionMarker = atomicity.indexOf(
    "perform nextval('public.spotify_booster_migration_atomicity_signal')");
  const rethrow = atomicity.indexOf('      raise;', executionMarker);
  const rollbackProof = atomicity.indexOf('RESULT="$(docker exec');

  assert.match(atomicity,
    /SIGNAL='public\.spotify_booster_migration_atomicity_signal'/u);
  assert.match(atomicity, /errcode = 'P0001'/u);
  assert.match(atomicity, /message = 'SPOTIFY_BOOSTER_ATOMICITY_SENTINEL_[A-F0-9]+'/u);
  assert.ok(runner >= 0);
  assert.ok(executionMarker >= 0);
  assert.ok(sqlState < sentinel);
  assert.ok(sentinel < diagnostics);
  assert.ok(diagnostics < messageCheck);
  assert.ok(messageCheck < sqlStateCheck);
  assert.ok(sqlStateCheck < executionMarker);
  assert.ok(executionMarker < rethrow);
  assert.ok(rethrow < runner);
  assert.ok(rollbackProof > runner);
  assert.match(atomicity, /caught_message is distinct from/u);
  assert.match(atomicity, /caught_sqlstate is distinct from 'P0001'/u);
  assert.match(atomicity, /migration_atomicity=log_scan_failed/u);
  assert.match(atomicity, /sb_\(publishable\|secret\)_/u);
  assert.match(atomicity, /BEGIN\[\[:space:\]\]\+\(RSA \|EC \)\?PRIVATE KEY/u);
  assert.match(atomicity, /supabase_migrations\.schema_migrations/u);
  assert.match(atomicity, /to_regclass/u);
  assert.match(atomicity, /select is_called and last_value = 1/u);
  assert.match(atomicity, /test "\$RESULT" = 'f\|f\|t'/u);
  assert.match(atomicity, /drop table if exists/u);
  assert.match(atomicity, /drop sequence if exists/u);
  assert.match(atomicity,
    /migration_atomicity=database_cleanup_verification_failed/u);
  assert.match(atomicity, /"\$cleanup_state" != 't\|t\|t'/u);
  assert.match(atomicity, /if ! find "\$LOG_DIR"[\s\S]*>"\$log_list"/u);
  assert.match(atomicity, /if \(\( grep_status != 1 \)\); then/u);
  assert.match(atomicity,
    /if ! rm -f -- "\$MIGRATION" \|\| \[\[ -e "\$MIGRATION" \]\]/u);
  assert.match(atomicity,
    /if ! rm -rf -- "\$LOG_DIR" \|\| \[\[ -e "\$LOG_DIR" \]\]/u);
  assert.doesNotMatch(atomicity,
    /SUPABASE_SERVICE_ROLE_KEY|sb_secret_[A-Za-z0-9_-]{20,}/u);
});

test('WSL gate confines runtime, logs, and cleanup to temporary paths', () => {
  assert.match(finalGate, /mktemp -d \/tmp\/spotify-booster-validation/u);
  assert.match(finalGate, />"\$TOOL_DIR\/dotnet-restore\.log"/u);
  assert.match(finalGate, />"\$TOOL_DIR\/dotnet-build\.log"/u);
  assert.match(finalGate, /SSL_CERT_FILE="\$CERT"/u);
  assert.match(finalGate, /kill "\$API_PID"/u);
  assert.match(finalGate,
    /SpotifyBooster\.Api\/bin\/Release\/net8\.0\/SpotifyBooster\.Api\.dll/u);
  assert.doesNotMatch(finalGate, /"\$DOTNET" run/u);
  assert.match(finalGate, /if ! wait_for_api_port_release; then/u);
  assert.match(finalGate, /validation_cleanup=api_port_remaining/u);
  assert.match(finalGate, /if is_api_port_listening; then[\s\S]*api_start=port_in_use/u);
  assert.match(finalGate, /"\$CLI" stop --project-id "\$PROJECT_ID"/u);
  assert.match(finalGate, /find "\$TOOL_DIR" -type f -name '\*\.log' -print0/u);
  assert.match(finalGate, /if ! find "\$TOOL_DIR"[\s\S]*>"\$log_list"/u);
  assert.match(finalGate, /if \(\( grep_status != 1 \)\); then/u);
  assert.match(finalGate, /if ! scan_temporary_logs; then/u);
  assert.match(finalGate,
    /grep -E '\^migration_atomicity=\[a-z0-9_\]\+\$' atomicity\.log >&2/u);
  assert.match(finalGate,
    /if ! rm -rf -- "\$TOOL_DIR" \|\| \[\[ -e "\$TOOL_DIR" \]\]/u);
  assert.match(finalGate,
    /docker ps -a[\s\S]*label=com\.supabase\.cli\.project="\$PROJECT_ID"/u);
  assert.match(finalGate, /validation_cleanup=containers_remaining/u);
  assert.doesNotMatch(finalGate, /NODE_TLS_REJECT_UNAUTHORIZED|--insecure|-k(?:\s|$)/u);
});

test('WSL gate verifies Node and lock integrity before the pinned CLI', () => {
  const nodeCheck = finalGate.indexOf("CURRENT_STAGE='node_version_check'");
  const packageCopy = finalGate.indexOf(
    'cp -- "$PROJECT/package.json" "$PROJECT/package-lock.json" "$TOOL_DIR/"');
  const npmCi = finalGate.indexOf('npm ci --offline --ignore-scripts=false');
  const cliCheck = finalGate.indexOf(`test "$("$CLI" --version)" = '2.109.1'`);

  assert.ok(nodeCheck >= 0);
  assert.ok(packageCopy > nodeCheck);
  assert.ok(npmCi > packageCopy);
  assert.ok(cliCheck > npmCi);
  assert.doesNotMatch(finalGate, /npm install/u);
  assert.doesNotMatch(finalGate, /dotnet-install\.sh|https:\/\/dot\.net\/v1/u);
  assert.match(finalGate,
    /https:\/\/builds\.dotnet\.microsoft\.com\/dotnet\/Sdk\/\$\{DOTNET_SDK_VERSION\}\/dotnet-sdk-\$\{DOTNET_SDK_VERSION\}-linux-x64\.tar\.gz/u);
  assert.match(finalGate,
    /DOTNET_SDK_SHA512='e94513dfe42271a85f01e87bd4272aa80b4ec13556f4531754802542225667775242c5e281a94837dae6cc65f7bcc457d2f663f240c0e2b7573fd909e786b1a5'/u);
  const sdkDownload = finalGate.indexOf('--output "$DOTNET_SDK_ARCHIVE"');
  const sdkHash = finalGate.indexOf('sha512sum --check --status');
  const sdkExtract = finalGate.indexOf('tar -xzf "$DOTNET_SDK_ARCHIVE"');
  assert.ok(sdkDownload >= 0);
  assert.ok(sdkHash > sdkDownload);
  assert.ok(sdkExtract > sdkHash);
  assert.match(e2e, /verifyInitialReadiness/u);
  assert.match(finalGate,
    /result changed: \(ready\|timeout\|transport\|http_status\|invalid_shape\)/u);
});

test('secret scanner distinguishes .NET categories from JWTs and exact values', () => {
  const jwtPattern =
    /\beyJ[A-Za-z0-9_-]{7,}\.[A-Za-z0-9_-]{10,}\.[A-Za-z0-9_-]{10,}\b/u;
  const namespace =
    'SpotifyBooster.Api.DataApiReadinessProbe.System.Net.Http.HttpClient';
  const fakeJwt =
    'eyJhbGciOiJFUzI1NiJ9.eyJzdWIiOiJmaXh0dXJlIn0.AAAAAAAAAAAAAAAAAAAA';
  const exactSensitiveValue = 'generated-sensitive-fixture';
  const cleanLog = `${namespace} request completed`;
  const taintedLog = `${cleanLog} ${exactSensitiveValue}`;

  assert.equal(jwtPattern.test(namespace), false);
  assert.equal(jwtPattern.test(fakeJwt), true);
  assert.equal(cleanLog.includes(exactSensitiveValue), false);
  assert.equal(taintedLog.includes(exactSensitiveValue), true);
  assert.match(e2e, /for \(const value of sensitiveValues\)/u);
  assert.match(e2e, /authorization:\\s\*bearer/u);
  assert.ok(e2e.includes('/\\brefresh_token\\b/iu'));
  for (const forbiddenEvidence of [
    'authorization:[[:space:]]*bearer',
    'refresh_token',
    '@example\\.invalid',
    'sb_(publishable|secret)',
    'service_role',
    'BEGIN[[:space:]]+(RSA |EC )?PRIVATE KEY',
    'postgres(ql)?://',
    'db_password',
    'pgpassword',
    '"(d|p|q|dp|dq|qi)"'
  ])
    assert.ok(finalGate.includes(forbiddenEvidence),
      `missing log scanner evidence: ${forbiddenEvidence}`);
  assert.match(finalGate, /grep -Fq -- "\$PUBLISHABLE_KEY"/u);
  assert.match(finalGate, /migration_atomicity_execution_marker=ok/u);
  assert.match(finalGate, /migration_atomicity_log_scan=ok/u);
  assert.match(finalGate, /"\$CLI" start --workdir "\$PROJECT" >\/dev\/null 2>&1/u);
  assert.match(finalGate, /"\$CLI" db reset --local --workdir "\$PROJECT" >\/dev\/null 2>&1/u);
});
