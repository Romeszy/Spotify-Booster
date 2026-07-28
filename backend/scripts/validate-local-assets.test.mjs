import test from 'node:test';
import assert from 'node:assert/strict';
import { generateKeyPairSync } from 'node:crypto';
import { mkdtempSync, readFileSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { spawnSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';

const validator = fileURLToPath(new URL('./validate-local-assets.mjs', import.meta.url));
function fixture() {
  const dir = mkdtempSync(join(tmpdir(), 'spotify-booster-assets-'));
  const { privateKey } = generateKeyPairSync('ec', { namedCurve: 'P-256' });
  const jwk = privateKey.export({ format: 'jwk' });
  Object.assign(jwk, { use: 'sig', key_ops: ['sign','verify'], alg: 'ES256', ext: true, kid: '123e4567-e89b-42d3-a456-426614174000' });
  return { dir, jwk };
}
function run(...args) { return spawnSync(process.execPath, [validator, ...args], { encoding: 'utf8' }); }

test('CLI object normalizes to the exact one-element private JWK array', () => {
  const { dir, jwk } = fixture(); const input=join(dir,'capture.json'), output=join(dir,'signing_keys.json');
  writeFileSync(input, JSON.stringify(jwk));
  assert.equal(run('normalize-cli-jwk', input, dir, output).status, 0);
  const value=JSON.parse(readFileSync(output)); assert.equal(value.length,1); assert.equal(value[0].alg,'ES256'); assert.ok(!value[0].d.includes('='));
  assert.equal(run('validate-jwk', output, dir).status, 0);
});

test('existing object and JWKS envelope shapes are rejected', () => {
  const { dir, jwk } = fixture(); const object=join(dir,'object.json'), envelope=join(dir,'envelope.json');
  writeFileSync(object, JSON.stringify(jwk)); writeFileSync(envelope, JSON.stringify({keys:[jwk]}));
  assert.notEqual(run('validate-jwk', object, dir).status,0);
  assert.notEqual(run('validate-jwk', envelope, dir).status,0);
});

test('invalid scalar encoding and unexpected fields are rejected without secret output', () => {
  const { dir, jwk } = fixture(); const input=join(dir,'invalid.json');
  jwk.d='AA=='; jwk.secret='must-not-appear'; writeFileSync(input,JSON.stringify([jwk]));
  const result=run('validate-jwk',input,dir);
  assert.notEqual(result.status,0); assert.doesNotMatch(result.stderr,/must-not-appear/);
});
