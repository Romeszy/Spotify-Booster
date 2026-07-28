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
  assert.match(atomicity, /supabase_migrations\.schema_migrations/u);
  assert.match(atomicity, /to_regclass/u);
  assert.match(atomicity, /test "\$RESULT" = 'f\|f'/u);
  assert.match(atomicity, /drop table if exists/u);
  assert.match(atomicity, /rm -f -- "\$MIGRATION"/u);
  assert.doesNotMatch(atomicity, /service_role|sb_secret/u);
});

test('WSL gate confines runtime, logs, and cleanup to temporary paths', () => {
  assert.match(finalGate, /mktemp -d \/tmp\/spotify-booster-validation/u);
  assert.match(finalGate, />"\$TOOL_DIR\/dotnet-restore\.log"/u);
  assert.match(finalGate, />"\$TOOL_DIR\/dotnet-build\.log"/u);
  assert.match(finalGate, /SSL_CERT_FILE="\$CERT"/u);
  assert.match(finalGate, /kill "\$API_PID"/u);
  assert.match(finalGate, /"\$CLI" stop --project-id "\$PROJECT_ID"/u);
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
  assert.match(finalGate, /authorization:\[\[:space:\]\]\*bearer\|refresh_token/u);
});
