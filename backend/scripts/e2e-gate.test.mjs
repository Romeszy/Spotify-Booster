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
  const sentinelCheck = atomicity.indexOf(
    'grep -Eq -- "$SENTINEL_ERROR_PATTERN"');
  const rollbackProof = atomicity.indexOf('RESULT="$(docker exec');

  assert.match(atomicity, /SENTINEL='SPOTIFY_BOOSTER_ATOMICITY_SENTINEL_[A-F0-9]+'/u);
  assert.match(atomicity,
    /SENTINEL_ERROR_PATTERN="ERROR:\[\[:space:\]\]\+\$\{SENTINEL\}\[\[:space:\]\]\+\\\\\(SQLSTATE\[\[:space:\]\]\+P0001\\\\\)"/u);
  const sentinelError =
    /ERROR:\s+SPOTIFY_BOOSTER_ATOMICITY_SENTINEL_7F31A9C2\s+\(SQLSTATE\s+P0001\)/u;
  assert.equal(sentinelError.test(
    "message = 'SPOTIFY_BOOSTER_ATOMICITY_SENTINEL_7F31A9C2';"), false);
  assert.equal(sentinelError.test(
    'ERROR: SPOTIFY_BOOSTER_ATOMICITY_SENTINEL_7F31A9C2 (SQLSTATE P0001)'), true);
  assert.match(atomicity, /errcode = 'P0001'/u);
  assert.match(atomicity, /message = 'SPOTIFY_BOOSTER_ATOMICITY_SENTINEL_[A-F0-9]+'/u);
  assert.ok(runner >= 0);
  assert.ok(sentinelCheck > runner);
  assert.ok(rollbackProof > sentinelCheck);
  assert.match(atomicity, /migration_atomicity=log_scan_failed/u);
  assert.match(atomicity, /sb_\(publishable\|secret\)_/u);
  assert.match(atomicity, /BEGIN\[\[:space:\]\]\+\(RSA \|EC \)\?PRIVATE KEY/u);
  assert.match(atomicity, /supabase_migrations\.schema_migrations/u);
  assert.match(atomicity, /to_regclass/u);
  assert.match(atomicity, /test "\$RESULT" = 'f\|f'/u);
  assert.match(atomicity, /drop table if exists/u);
  assert.match(atomicity, /rm -f -- "\$MIGRATION"/u);
  assert.doesNotMatch(atomicity,
    /SUPABASE_SERVICE_ROLE_KEY|sb_secret_[A-Za-z0-9_-]{20,}/u);
});

test('WSL gate confines runtime, logs, and cleanup to temporary paths', () => {
  assert.match(finalGate, /mktemp -d \/tmp\/spotify-booster-validation/u);
  assert.match(finalGate, />"\$TOOL_DIR\/dotnet-restore\.log"/u);
  assert.match(finalGate, />"\$TOOL_DIR\/dotnet-build\.log"/u);
  assert.match(finalGate, /SSL_CERT_FILE="\$CERT"/u);
  assert.match(finalGate, /kill "\$API_PID"/u);
  assert.match(finalGate, /"\$CLI" stop --project-id "\$PROJECT_ID"/u);
  assert.match(finalGate, /find "\$TOOL_DIR" -type f -name '\*\.log' -print0/u);
  assert.match(finalGate, /if ! scan_temporary_logs; then/u);
  assert.match(finalGate, /rm -rf -- "\$TOOL_DIR"/u);
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
  assert.match(finalGate, /migration_atomicity_expected_sentinel=ok/u);
  assert.match(finalGate, /migration_atomicity_log_scan=ok/u);
  assert.match(finalGate, /"\$CLI" start --workdir "\$PROJECT" >\/dev\/null 2>&1/u);
  assert.match(finalGate, /"\$CLI" db reset --local --workdir "\$PROJECT" >\/dev\/null 2>&1/u);
});
