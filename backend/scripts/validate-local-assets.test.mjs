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

test('CLI staging array normalizes to the exact private JWK array', () => {
  const { dir, jwk } = fixture(); const input=join(dir,'staged.json'), output=join(dir,'signing_keys.json');
  writeFileSync(input, JSON.stringify([jwk]));
  assert.equal(run('validate-jwk', input, dir, output).status, 0);
  const value=JSON.parse(readFileSync(output)); assert.equal(value.length,1); assert.equal(value[0].alg,'ES256'); assert.ok(!value[0].d.includes('='));
  assert.equal(run('validate-jwk', output, dir).status, 0);
});

test('existing object and JWKS envelope shapes are rejected', () => {
  const { dir, jwk } = fixture(); const object=join(dir,'object.json'), envelope=join(dir,'envelope.json');
  writeFileSync(object, JSON.stringify(jwk)); writeFileSync(envelope, JSON.stringify({keys:[jwk]}));
  assert.notEqual(run('validate-jwk', object, dir).status,0);
  assert.notEqual(run('validate-jwk', envelope, dir).status,0);
});


test('JWKS must match kid and public P-256 coordinates without d', () => {
  const { dir, jwk } = fixture(); const signing=join(dir,'signing.json'), good=join(dir,'good-jwks.json'), bad=join(dir,'bad-jwks.json');
  writeFileSync(signing, JSON.stringify([jwk]));
  const publicJwk={ kty:jwk.kty, use:'sig', key_ops:['verify'], alg:jwk.alg, kid:jwk.kid, crv:jwk.crv, x:jwk.x, y:jwk.y };
  writeFileSync(good,JSON.stringify({keys:[publicJwk]})); writeFileSync(bad,JSON.stringify({keys:[{...publicJwk,d:jwk.d}]}));
  assert.equal(run('validate-jwks',signing,good,dir).status,0);
  assert.notEqual(run('validate-jwks',signing,bad,dir).status,0);
});


test('bootstrap generates only through configured private staging without stdout capture', () => {
  const source=readFileSync(fileURLToPath(new URL('./bootstrap-supabase-local.ps1', import.meta.url)),'utf8');
  for (const forbidden of [['signing','capture'].join('-'), ['normalize','cli','jwk'].join('-'), ['StandardOutput','ReadToEnd'].join('.'), String.fromCharCode(36)+'stdout']) assert.ok(!source.toLowerCase().includes(forbidden.toLowerCase()), `forbidden bootstrap pattern: ${forbidden}`);
  assert.match(source,/\.signing-stage-/);
  assert.match(source,/signing_keys_path/);
  assert.match(source,/Invoke-BoundedSilent/);
  assert.match(source,/validate-jwk[^\r\n]*\$stageSigningPath/);
  assert.doesNotMatch(source,/Invoke-Bounded\s+\$LocalCli/);
});

test('invalid scalar encoding and unexpected fields are rejected without secret output', () => {
  const { dir, jwk } = fixture(); const input=join(dir,'invalid.json');
  jwk.d='AA=='; jwk.secret='must-not-appear'; writeFileSync(input,JSON.stringify([jwk]));
  const result=run('validate-jwk',input,dir);
  assert.notEqual(result.status,0); assert.doesNotMatch(result.stderr,/must-not-appear/);
});
