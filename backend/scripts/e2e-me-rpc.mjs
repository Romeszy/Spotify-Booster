import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { request } from 'node:https';
import { spawnSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';

const backendRoot = fileURLToPath(new URL('..', import.meta.url));
const certificate = readFileSync(new URL('../supabase/.local/tls/supabase-local.crt',
  import.meta.url));
const origin = new URL('https://127.0.0.1:54321');
const publishableKey = 'sb_publishable_ACJWlzQHlZjBrEguHvfOxg_3BJgxAaH';
const createdSubjects = [];

class E2eError extends Error {
  constructor(category) {
    super(category);
    this.category = category;
  }
}
function fail(category) {
  throw new E2eError(category);
}

function send(path, { method = 'GET', token, body } = {}) {
  return new Promise((resolve, reject) => {
    const payload = body === undefined ? undefined : Buffer.from(JSON.stringify(body));
    const headers = {
      accept: 'application/json',
      apikey: publishableKey
    };
    if (token) headers.authorization = `Bearer ${token}`;
    if (payload) {
      headers['content-type'] = 'application/json';
      headers['content-length'] = String(payload.length);
    }
    const call = request(new URL(path, origin), {
      method,
      headers,
      ca: certificate,
      rejectUnauthorized: true,
      timeout: 5000
    }, response => {
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
        try {
          resolve({
            status: response.statusCode,
            json: JSON.parse(Buffer.concat(chunks).toString('utf8'))
          });
        } catch {
          reject(new E2eError('invalid_json'));
        }
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

async function createUser() {
  const suffix = crypto.randomUUID().replaceAll('-', '');
  const response = await send('/auth/v1/signup', {
    method: 'POST',
    body: {
      email: `me-contract-${suffix}@example.invalid`,
      password: `E2e-${suffix.slice(0, 16)}`
    }
  });
  if (response.status !== 200) fail(`signup_http_${response.status}`);
  if (typeof response.json.access_token !== 'string') fail('signup_session_missing');
  if (!/^[0-9a-f-]{36}$/i.test(response.json.user?.id ?? ''))
    fail('signup_subject_invalid');
  createdSubjects.push(response.json.user.id);
  return { subject: response.json.user.id, token: response.json.access_token };
}

function cleanup() {
  if (createdSubjects.length === 0) return;
  const ids = createdSubjects.map(subject => `'${subject}'::uuid`).join(',');
  const result = spawnSync('docker', [
    'exec', 'supabase_db_spotify-booster-commercial-phase-a',
    'psql', '-U', 'postgres', '-d', 'postgres', '-v', 'ON_ERROR_STOP=1',
    '-c', `delete from auth.users where id in (${ids})`
  ], { cwd: backendRoot, encoding: 'utf8', windowsHide: true });
  assert.equal(result.status, 0, 'generated local users must be removed');
}

try {
  const userA = await createUser();
  const userB = await createUser();
  const [meA, meB] = await Promise.all([
    send('/rest/v1/rpc/spotify_booster_me_v1', { token: userA.token }),
    send('/rest/v1/rpc/spotify_booster_me_v1', { token: userB.token })
  ]);

  for (const [response, user, other] of [
    [meA, userA, userB],
    [meB, userB, userA]
  ]) {
    if (response.status !== 200) fail(`rpc_http_${response.status}`);
    if (!response.json || Array.isArray(response.json) ||
        typeof response.json !== 'object')
      fail('rpc_shape_invalid');
    const current = response.json;
    if (JSON.stringify(Object.keys(current).sort()) !==
        JSON.stringify(['displayName', 'features', 'plan', 'revision', 'status', 'userId']))
      fail('rpc_fields_invalid');
    if (current.userId !== user.subject) fail('rpc_subject_mismatch');
    if (current.userId === other.subject) fail('rpc_cross_user_disclosure');
    if (current.displayName !== null) fail('rpc_profile_invalid');
    if (current.plan !== 'free' || current.status !== 'active')
      fail('rpc_entitlement_invalid');
    if (current.revision !== 1) fail('rpc_revision_invalid');
    if (!Array.isArray(current.features) || current.features.length !== 0)
      fail('rpc_features_invalid');
  }
  process.stdout.write('real_me_rpc_e2e_ok\n');
} catch (error) {
  const category = error instanceof E2eError ? error.category : 'contract_assertion';
  process.stderr.write(`real_me_rpc_e2e_failed:${category}\n`);
  process.exitCode = 1;
} finally {
  cleanup();
}
