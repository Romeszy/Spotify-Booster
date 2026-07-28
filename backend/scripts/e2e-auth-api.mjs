import { spawnSync } from 'node:child_process';
import { randomBytes, randomUUID } from 'node:crypto';
import { readFileSync } from 'node:fs';
import http from 'node:http';
import https from 'node:https';
import { fileURLToPath } from 'node:url';

const backendRoot = fileURLToPath(new URL('..', import.meta.url));
const supabaseOrigin = new URL('https://127.0.0.1:54321');
const apiOrigin = new URL(process.env.SPOTIFY_BOOSTER_API_ORIGIN ??
  'http://127.0.0.1:5080');
const publishableKey = process.env.SPOTIFY_BOOSTER_PUBLISHABLE_KEY ??
  'sb_publishable_ACJWlzQHlZjBrEguHvfOxg_3BJgxAaH';
const certificatePath = process.env.SUPABASE_CA_FILE ??
  fileURLToPath(new URL('../supabase/.local/tls/supabase-local.crt', import.meta.url));
const apiLogPath = process.env.SPOTIFY_BOOSTER_API_LOG;
const supabaseCli = process.env.SUPABASE_CLI;
const certificate = readFileSync(certificatePath);
const dataApiClientSource = readFileSync(new URL(
  '../../src/SpotifyBooster.Api/DataApiClient.cs', import.meta.url), 'utf8');
const jwtLogPattern =
  /\beyJ[A-Za-z0-9_-]{7,}\.[A-Za-z0-9_-]{10,}\.[A-Za-z0-9_-]{10,}\b/u;
const projectId = 'spotify-booster-commercial-phase-a';
const createdSubjects = [];
const createdEmails = [];
const sensitiveValues = [];
let stoppedContainers = [];

class E2eError extends Error {
  constructor(category) {
    super(category);
    this.category = category;
  }
}

function ensure(condition, category) {
  if (!condition) throw new E2eError(category);
}

function secret(length) {
  const value = randomBytes(length).toString('base64url').slice(0, length);
  sensitiveValues.push(value);
  return value;
}

function generatedEmail(label) {
  const value = `phase-a-${label}-${randomUUID().replaceAll('-', '')}@example.invalid`;
  createdEmails.push(value);
  sensitiveValues.push(value);
  return value;
}

function requestJson(origin, path, {
  method = 'GET',
  token,
  body,
  includeApiKey = true,
  timeout = 5000
} = {}) {
  const client = origin.protocol === 'https:' ? https : http;
  const payload = body === undefined ? undefined : Buffer.from(JSON.stringify(body));
  const headers = { accept: 'application/json' };
  if (includeApiKey) headers.apikey = publishableKey;
  if (token) headers.authorization = `Bearer ${token}`;
  if (payload) {
    headers['content-type'] = 'application/json';
    headers['content-length'] = String(payload.length);
  }

  return new Promise((resolve, reject) => {
    const options = {
      method,
      headers,
      timeout,
      agent: false,
      ...(origin.protocol === 'https:'
        ? { ca: certificate, rejectUnauthorized: true }
        : {})
    };
    const call = client.request(new URL(path, origin), options, response => {
      const chunks = [];
      let length = 0;
      response.on('data', chunk => {
        length += chunk.length;
        if (length > 256 * 1024) {
          response.destroy(new E2eError('response_too_large'));
          return;
        }
        chunks.push(chunk);
      });
      response.on('end', () => {
        const text = Buffer.concat(chunks).toString('utf8');
        let json = null;
        if (text.length > 0) {
          try {
            json = JSON.parse(text);
          } catch {
            reject(new E2eError('invalid_json'));
            return;
          }
        }
        resolve({
          status: response.statusCode ?? 0,
          headers: response.headers,
          json,
          text
        });
      });
    });
    call.on('timeout', () => call.destroy(new E2eError('request_timeout')));
    call.on('error', error => reject(error instanceof E2eError
      ? error
      : new E2eError(`transport_${error.code ?? 'error'}`)));
    if (payload) call.write(payload);
    call.end();
  });
}

const supabase = (path, options) => requestJson(supabaseOrigin, path, options);
const api = (path, options) => requestJson(apiOrigin, path, {
  ...options,
  includeApiKey: false
});

function docker(args, input) {
  const result = spawnSync('docker', args, {
    cwd: backendRoot,
    encoding: 'utf8',
    input,
    windowsHide: true,
    timeout: 30000
  });
  ensure(result.status === 0, 'docker_command_failed');
  return result.stdout.trim();
}

function cli(args) {
  ensure(supabaseCli, 'supabase_cli_missing');
  const result = spawnSync(supabaseCli, args, {
    cwd: backendRoot,
    encoding: 'utf8',
    windowsHide: true,
    timeout: 180000,
    env: {
      ...process.env,
      SSL_CERT_FILE: certificatePath
    }
  });
  ensure(result.status === 0, 'supabase_stack_restart_failed');
}

function databaseScalar(sql) {
  return docker([
    'exec', '-i', `supabase_db_${projectId}`,
    'psql', '-U', 'postgres', '-d', 'postgres', '-Atq',
    '-v', 'ON_ERROR_STOP=1'
  ], sql);
}

function containerNames() {
  return docker([
    'ps', '-a',
    '--filter', `label=com.supabase.cli.project=${projectId}`,
    '--format', '{{.Names}}'
  ]).split(/\r?\n/u).filter(Boolean);
}

function findContainer(kind) {
  const suffix = `supabase_${kind}_${projectId}`;
  const name = containerNames().find(candidate => candidate === suffix);
  ensure(name, `container_${kind}_missing`);
  return name;
}

async function poll(check, timeoutMs, category) {
  const deadline = Date.now() + timeoutMs;
  while (Date.now() < deadline) {
    try {
      if (await check()) return;
    } catch {
      // A bounded transport failure is expected while dependencies transition.
    }
    await new Promise(resolve => setTimeout(resolve, 500));
  }
  throw new E2eError(category);
}

async function signup(email, password) {
  const response = await supabase('/auth/v1/signup', {
    method: 'POST',
    body: { email, password }
  });
  ensure(response.status === 200, `signup_http_${response.status}`);
  ensure(typeof response.json?.access_token === 'string', 'signup_access_missing');
  ensure(typeof response.json?.refresh_token === 'string', 'signup_refresh_missing');
  ensure(/^[0-9a-f-]{36}$/iu.test(response.json?.user?.id ?? ''),
    'signup_subject_invalid');
  createdSubjects.push(response.json.user.id);
  sensitiveValues.push(response.json.access_token, response.json.refresh_token);
  return {
    subject: response.json.user.id,
    access: response.json.access_token,
    refresh: response.json.refresh_token
  };
}

async function refresh(refreshToken) {
  return supabase('/auth/v1/token?grant_type=refresh_token', {
    method: 'POST',
    body: { refresh_token: refreshToken }
  });
}

function verifyMe(response, user, other) {
  ensure(response.status === 200, `api_me_http_${response.status}`);
  ensure(response.headers['content-type']?.startsWith('application/json'),
    'api_me_content_type');
  const me = response.json;
  ensure(me && !Array.isArray(me) && typeof me === 'object', 'api_me_shape');
  ensure(JSON.stringify(Object.keys(me).sort()) === JSON.stringify([
    'displayName', 'features', 'plan', 'revision', 'serverTime', 'status', 'userId'
  ]), 'api_me_fields');
  ensure(me.userId === user.subject, 'api_me_subject');
  ensure(me.userId !== other.subject, 'api_me_cross_subject');
  ensure(me.displayName === null, 'api_me_profile');
  ensure(me.plan === 'free' && me.status === 'active' && me.revision === 1,
    'api_me_free_entitlement');
  ensure(Array.isArray(me.features) && me.features.length === 0, 'api_me_features');
  ensure(Number.isFinite(Date.parse(me.serverTime)), 'api_me_server_time');
  ensure(!response.text.includes('@example.invalid'), 'api_me_email_disclosure');
  ensure(!response.text.toLowerCase().includes('token'), 'api_me_token_disclosure');
}

async function verifyInitialReadiness() {
  await poll(async () => {
    const response = await api('/health/ready');
    if (response.status !== 200) return false;
    ensure(JSON.stringify(response.json) === JSON.stringify({ status: 'ready' }),
      'readiness_initial_200_invalid');
    return true;
  }, 45000, 'readiness_initial_200_timeout');
}

function isExactHealth(value) {
  return value && !Array.isArray(value) && typeof value === 'object' &&
    JSON.stringify(Object.keys(value).sort()) ===
      JSON.stringify(['service', 'status', 'version']) &&
    value.service === 'spotify-booster-data' &&
    value.status === 'ok' &&
    value.version === 1;
}

async function verifyPasswordBoundary() {
  const email = generatedEmail('password-boundary');
  const response = await supabase('/auth/v1/signup', {
    method: 'POST',
    body: { email, password: secret(9) }
  });
  ensure(response.status >= 400 && response.status < 500, 'password_9_accepted');
  ensure(!response.json?.access_token && !response.json?.refresh_token,
    'password_9_session_created');
  const escapedEmail = email.replaceAll("'", "''");
  ensure(databaseScalar(
    `select count(*) from auth.users where email='${escapedEmail}';\n`) === '0',
  'password_9_user_created');
}

async function verifyHeadersAndIdor(userA, userB) {
  const rpcPath = '/rest/v1/rpc/spotify_booster_me_v1';
  const apiKeyOnly = await supabase(rpcPath);
  ensure(apiKeyOnly.status === 401 || apiKeyOnly.status === 403,
    'rpc_bearer_not_required');
  ensure(dataApiClientSource.includes(
    'request.Headers.TryAddWithoutValidation("apikey", _options.PublishableKey);'),
  'api_apikey_header_missing');
  ensure(dataApiClientSource.includes(
    'request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);'),
  'api_bearer_header_missing');
  ensure(!dataApiClientSource.includes('DefaultRequestHeaders.Authorization ='),
    'api_shared_authorization_header');
  const both = await supabase(rpcPath, { token: userA.access });
  ensure(both.status === 200, 'rpc_required_headers_failed');

  for (const relation of ['profiles', 'entitlements']) {
    const response = await supabase(
      `/rest/v1/${relation}?select=user_id&user_id=eq.${userB.subject}`,
      { token: userA.access });
    ensure(response.status === 200, `idor_${relation}_http_${response.status}`);
    ensure(Array.isArray(response.json) && response.json.length === 0,
      `idor_${relation}_disclosure`);
  }
}

async function verifyRefreshReuse(userA) {
  const r1 = await refresh(userA.refresh);
  ensure(r1.status === 200, `refresh_r0_http_${r1.status}`);
  ensure(typeof r1.json?.refresh_token === 'string', 'refresh_r1_missing');
  ensure(r1.json.refresh_token !== userA.refresh, 'refresh_not_rotated');
  sensitiveValues.push(r1.json.refresh_token);
  if (typeof r1.json.access_token === 'string')
    sensitiveValues.push(r1.json.access_token);

  // Auth 2.109.1 intentionally permits one retry of the direct parent by
  // returning the active child. Advance once so R0 is an actual replay, not
  // that documented lost-response recovery path.
  const r2 = await refresh(r1.json.refresh_token);
  ensure(r2.status === 200, `refresh_r1_http_${r2.status}`);
  ensure(typeof r2.json?.refresh_token === 'string', 'refresh_r2_missing');
  ensure(r2.json.refresh_token !== r1.json.refresh_token, 'refresh_r2_not_rotated');
  sensitiveValues.push(r2.json.refresh_token);
  if (typeof r2.json.access_token === 'string')
    sensitiveValues.push(r2.json.access_token);

  // GoTrue 2.109.1 records refresh issuance at second granularity. Cross that
  // boundary so a configured zero-second reuse interval is tested unambiguously.
  await new Promise(resolve => setTimeout(resolve, 1100));
  const reuse = await refresh(userA.refresh);
  ensure(reuse.status >= 400, 'refresh_r0_reuse_accepted');
  const r1Family = await refresh(r1.json.refresh_token);
  ensure(r1Family.status >= 400, 'refresh_r1_family_survived');
  const r2Family = await refresh(r2.json.refresh_token);
  ensure(r2Family.status >= 400, 'refresh_r2_family_survived');
}

async function verifyLocalLogout(userB, userA) {
  const logout = await supabase('/auth/v1/logout?scope=local', {
    method: 'POST',
    token: userB.access
  });
  ensure(logout.status === 204 || logout.status === 200,
    `logout_http_${logout.status}`);
  const refused = await refresh(userB.refresh);
  ensure(refused.status >= 400, 'logout_refresh_survived');
  verifyMe(await api('/v1/me', { token: userB.access }), userB, userA);
}

async function restartStackAndObserveHealth() {
  cli(['stop', '--project-id', projectId]);
  cli(['start', '--workdir', backendRoot]);

  let recoveryObservation = 'unobserved';
  const recoveryDeadline = Date.now() + 30000;
  while (Date.now() < recoveryDeadline) {
    try {
      const response = await supabase('/rest/v1/rpc/spotify_booster_health_v1');
      recoveryObservation = `http_${response.status}`;
      if (response.status === 200 && isExactHealth(response.json))
        break;
      if (response.status === 200) recoveryObservation = 'http_200_shape';
    } catch (error) {
      recoveryObservation = error instanceof E2eError
        ? error.category
        : 'unexpected_transport';
    }
    await new Promise(resolve => setTimeout(resolve, 500));
  }
  return recoveryObservation;
}

async function verifyDependencyRecovery(user, other) {
  const authContainer = findContainer('auth');
  const dbContainer = findContainer('db');
  stoppedContainers = [dbContainer, authContainer];
  docker(['stop', authContainer, dbContainer]);

  await poll(async () => {
    const response = await api('/health/ready');
    if (response.status !== 503) return false;
    ensure(JSON.stringify(response.json) === JSON.stringify({ status: 'unready' }),
      'readiness_503_not_redacted');
    return true;
  }, 45000, 'readiness_503_timeout');

  const recoveryObservation = await restartStackAndObserveHealth();
  stoppedContainers = [];
  ensure(recoveryObservation === 'http_200',
    `data_api_restart_${recoveryObservation}`);
  verifyMe(await api('/v1/me', { token: user.access }), user, other);

  await poll(async () => {
    const response = await api('/health/ready');
    if (response.status !== 200) return false;
    ensure(JSON.stringify(response.json) === JSON.stringify({ status: 'ready' }),
      'readiness_200_invalid');
    return true;
  }, 75000, 'readiness_200_timeout');
}

function restartStoppedContainers() {
  if (stoppedContainers.length === 0) return;
  for (const name of stoppedContainers)
    spawnSync('docker', ['start', name], {
      cwd: backendRoot,
      encoding: 'utf8',
      windowsHide: true,
      timeout: 30000
    });
  stoppedContainers = [];
}

function cleanupUsers() {
  if (createdSubjects.length === 0 && createdEmails.length === 0) return;
  const subjects = createdSubjects
    .map(subject => `'${subject.replaceAll("'", "''")}'::uuid`).join(',');
  const emails = createdEmails
    .map(email => `'${email.replaceAll("'", "''")}'`).join(',');
  const predicates = [];
  if (subjects) predicates.push(`id in (${subjects})`);
  if (emails) predicates.push(`email in (${emails})`);
  const result = spawnSync('docker', [
    'exec', '-i', `supabase_db_${projectId}`,
    'psql', '-U', 'postgres', '-d', 'postgres', '-q',
    '-v', 'ON_ERROR_STOP=1'
  ], {
    cwd: backendRoot,
    encoding: 'utf8',
    input: `delete from auth.users where ${predicates.join(' or ')};\n`,
    windowsHide: true,
    timeout: 30000
  });
  ensure(result.status === 0, 'generated_users_cleanup_failed');
}

function verifyLogRedaction() {
  if (!apiLogPath) return;
  const text = readFileSync(apiLogPath, 'utf8');
  ensure(!jwtLogPattern.test(text),
    'api_log_jwt_disclosure');
  ensure(!/authorization:\s*bearer/iu.test(text),
    'api_log_authorization_disclosure');
  ensure(!/\brefresh_token\b/iu.test(text),
    'api_log_refresh_disclosure');
  ensure(!text.includes('@example.invalid'), 'api_log_email_disclosure');
  for (const value of sensitiveValues)
    ensure(!text.includes(value), 'api_log_sensitive_value_disclosure');
}

async function observeReadiness(durationMs) {
  const counts = { ready: 0, unready: 0, other: 0 };
  const deadline = Date.now() + durationMs;
  while (Date.now() < deadline) {
    try {
      const response = await api('/health/ready');
      if (response.status === 200) counts.ready += 1;
      else if (response.status === 503) counts.unready += 1;
      else counts.other += 1;
    } catch {
      counts.other += 1;
    }
    await new Promise(resolve => setTimeout(resolve, 500));
  }
  return counts;
}

function readinessProbeLogCount() {
  if (!apiLogPath) return 0;
  const text = readFileSync(apiLogPath, 'utf8');
  return text.match(/HttpClient\.DataApiReadinessProbe\.LogicalHandler\[100\]/gu)
    ?.length ?? 0;
}

if (process.argv.includes('--recovery-smoke')) {
  try {
    const initial = await supabase('/rest/v1/rpc/spotify_booster_health_v1');
    ensure(initial.status === 200, `recovery_smoke_initial_http_${initial.status}`);
    const observation = await restartStackAndObserveHealth();
    ensure(observation === 'http_200', `recovery_smoke_${observation}`);
    process.stdout.write('stack_restart_health=ok\n');
  } catch (error) {
    const category = error instanceof E2eError ? error.category : 'unexpected_failure';
    process.stderr.write(`recovery_smoke_failed:${category}\n`);
    process.exitCode = 1;
  }
  process.exit(process.exitCode ?? 0);
}

if (process.argv.includes('--api-recovery-smoke')) {
  try {
    const user = await signup(generatedEmail('recovery'), secret(10));
    const other = { subject: '00000000-0000-0000-0000-000000000000' };
    verifyMe(await api('/v1/me', { token: user.access }), user, other);

    const authContainer = findContainer('auth');
    const dbContainer = findContainer('db');
    stoppedContainers = [dbContainer, authContainer];
    docker(['stop', authContainer, dbContainer]);
    await poll(async () => (await api('/health/ready')).status === 503,
      45000, 'api_recovery_503_timeout');

    const observation = await restartStackAndObserveHealth();
    stoppedContainers = [];
    ensure(observation === 'http_200', `api_recovery_data_${observation}`);
    const recovered = await api('/v1/me', { token: user.access });
    ensure(recovered.status === 200, `api_recovery_http_${recovered.status}`);
    verifyMe(recovered, user, other);
    process.stdout.write('api_recovery_me=ok\n');
  } catch (error) {
    const category = error instanceof E2eError ? error.category : 'unexpected_failure';
    process.stderr.write(`api_recovery_smoke_failed:${category}\n`);
    process.exitCode = 1;
  } finally {
    try {
      restartStoppedContainers();
      cleanupUsers();
    } catch {
      process.stderr.write('api_recovery_smoke_failed:cleanup_failed\n');
      process.exitCode = 1;
    }
  }
  process.exit(process.exitCode ?? 0);
}

if (process.argv.includes('--readiness-lifecycle-smoke')) {
  try {
    const user = await signup(generatedEmail('readiness'), secret(10));
    const other = { subject: '00000000-0000-0000-0000-000000000000' };
    verifyMe(await api('/v1/me', { token: user.access }), user, other);
    const pre = await observeReadiness(75000);
    process.stdout.write(
      `readiness_pre=ready_${pre.ready},unready_${pre.unready},other_${pre.other}\n`);
    process.stdout.write(`readiness_pre_probe_logs=${readinessProbeLogCount()}\n`);

    if (pre.ready > 0) {
      const authContainer = findContainer('auth');
      const dbContainer = findContainer('db');
      stoppedContainers = [dbContainer, authContainer];
      docker(['stop', authContainer, dbContainer]);
      await poll(async () => (await api('/health/ready')).status === 503,
        45000, 'lifecycle_503_timeout');
      const observation = await restartStackAndObserveHealth();
      stoppedContainers = [];
      ensure(observation === 'http_200', `lifecycle_data_${observation}`);
      verifyMe(await api('/v1/me', { token: user.access }), user, other);
      const post = await observeReadiness(75000);
      process.stdout.write(
        `readiness_post=ready_${post.ready},unready_${post.unready},other_${post.other}\n`);
      process.stdout.write(`readiness_post_probe_logs=${readinessProbeLogCount()}\n`);
    } else {
      process.stdout.write('readiness_post=skipped_pre_never_ready\n');
    }
  } catch (error) {
    const category = error instanceof E2eError ? error.category : 'unexpected_failure';
    process.stderr.write(`readiness_lifecycle_failed:${category}\n`);
    process.exitCode = 1;
  } finally {
    try {
      restartStoppedContainers();
      cleanupUsers();
    } catch {
      process.stderr.write('readiness_lifecycle_failed:cleanup_failed\n');
      process.exitCode = 1;
    }
  }
  process.exit(process.exitCode ?? 0);
}

try {
  ensure(supabaseOrigin.href === 'https://127.0.0.1:54321/',
    'supabase_origin_invalid');
  ensure(publishableKey.startsWith('sb_publishable_'), 'publishable_key_invalid');
  await verifyPasswordBoundary();
  const userA = await signup(generatedEmail('a'), secret(10));
  const userB = await signup(generatedEmail('b'), secret(10));
  verifyMe(await api('/v1/me', { token: userA.access }), userA, userB);
  verifyMe(await api('/v1/me', { token: userB.access }), userB, userA);
  await verifyInitialReadiness();
  await verifyHeadersAndIdor(userA, userB);
  await verifyRefreshReuse(userA);
  await verifyLocalLogout(userB, userA);
  await verifyDependencyRecovery(userB, userA);
  verifyLogRedaction();
  process.stdout.write([
    'auth_password_boundary=ok',
    'two_user_api_me=ok',
    'data_api_headers_rls_idor=ok',
    'refresh_reuse_family_revocation=ok',
    'local_logout_access_lifetime=ok',
    'readiness_stop_restart=ok',
    'sensitive_log_scan=ok'
  ].join('\n') + '\n');
} catch (error) {
  const category = error instanceof E2eError ? error.category : 'unexpected_failure';
  process.stderr.write(`auth_api_e2e_failed:${category}\n`);
  process.exitCode = 1;
} finally {
  try {
    restartStoppedContainers();
    cleanupUsers();
  } catch {
    process.stderr.write('auth_api_e2e_failed:cleanup_failed\n');
    process.exitCode = 1;
  }
}
