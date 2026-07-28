import test, { after, afterEach } from 'node:test';
import assert from 'node:assert/strict';
import { createPrivateKey, generateKeyPairSync, sign } from 'node:crypto';
import { mkdtempSync, readdirSync, readFileSync, rmSync, symlinkSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { spawnSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';

const validator = fileURLToPath(new URL('./validate-local-assets.mjs', import.meta.url));
const fixturePrefixes=['spotify-booster-assets-','spotify-booster-outside-'];
const initialFixtureNames=new Set(readdirSync(tmpdir()).filter(name=>fixturePrefixes.some(prefix=>name.startsWith(prefix))));
const fixtureDirectories=new Set();
function fixtureDirectory(prefix) {
  const directory=mkdtempSync(join(tmpdir(),prefix));
  fixtureDirectories.add(directory);
  return directory;
}
afterEach(()=>{
  for (const directory of fixtureDirectories) rmSync(directory,{recursive:true,force:true});
  fixtureDirectories.clear();
});
after(()=>{
  const remaining=readdirSync(tmpdir()).filter(name=>fixturePrefixes.some(prefix=>name.startsWith(prefix))&&!initialFixtureNames.has(name));
  assert.deepEqual(remaining,[],'test suite must leave no new private fixture directory');
});
function fixture() {
  const dir = fixtureDirectory('spotify-booster-assets-');
  const { privateKey } = generateKeyPairSync('ec', { namedCurve: 'P-256' });
  const jwk = privateKey.export({ format: 'jwk' });
  Object.assign(jwk, { use: 'sig', key_ops: ['sign','verify'], alg: 'ES256', ext: true, kid: '123e4567-e89b-42d3-a456-426614174000' });
  return { dir, jwk };
}
function run(...args) { return spawnSync(process.execPath, [validator, ...args], { encoding: 'utf8' }); }
function runWithInput(input, ...args) { return spawnSync(process.execPath, [validator, ...args], { encoding: 'utf8', input }); }

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


test('atomic writer flushes the staged file before close and same-directory rename', () => {
  const source=readFileSync(fileURLToPath(new URL('./validate-local-assets.mjs', import.meta.url)),'utf8');
  const write=source.indexOf('writeFileSync(fd'); const flush=source.indexOf('fsyncSync(fd)',write); const close=source.indexOf('closeSync(fd)',flush); const rename=source.indexOf('renameSync(temp, path)',close);
  assert.ok(write>=0 && write<flush && flush<close && close<rename);
  assert.match(source,/const temp = `\$\{path\}\.tmp-/);
});

test('bootstrap generates only through configured private staging without stdout capture', () => {
  const source=readFileSync(fileURLToPath(new URL('./bootstrap-supabase-local.ps1', import.meta.url)),'utf8');
  for (const forbidden of [['signing','capture'].join('-'), ['normalize','cli','jwk'].join('-'), ['StandardOutput','ReadToEnd'].join('.'), String.fromCharCode(36)+'stdout']) assert.ok(!source.toLowerCase().includes(forbidden.toLowerCase()), `forbidden bootstrap pattern: ${forbidden}`);
  assert.match(source,/\.signing-stage-/);
  assert.match(source,/signing_keys_path/);
  assert.match(source,/Invoke-BoundedSilent/);
  assert.match(source,/validate-jwk[^\r\n]*\$stageSigningPath/);
  assert.doesNotMatch(source,/Invoke-Bounded\s+\$LocalCli/);
  assert.match(source,/Stop-ProcessTree \$process/);
  assert.match(source,/validate-jwt-stdin/);
  assert.match(source,/delete from auth\.users/);
  assert.doesNotMatch(source,/\$_\.Exception\.Message/);
});

test('invalid scalar encoding and unexpected fields are rejected without secret output', () => {
  const { dir, jwk } = fixture(); const input=join(dir,'invalid.json');
  jwk.d='AA=='; jwk.secret='must-not-appear'; writeFileSync(input,JSON.stringify([jwk]));
  const result=run('validate-jwk',input,dir);
  assert.notEqual(result.status,0); assert.doesNotMatch(result.stderr,/must-not-appear/);
});

test('native parse details and injected secret markers never enter error output', () => {
  const { dir } = fixture(); const input=join(dir,'malformed.json');
  writeFileSync(input,'{"d":"SENSITIVE_PARSE_MARKER",');
  const result=run('validate-jwk',input,dir);
  assert.notEqual(result.status,0);
  assert.equal(result.stderr,'local_asset_validation_failed:unexpected_input\n');
  assert.doesNotMatch(result.stderr,/SENSITIVE_PARSE_MARKER|JSON|position/i);
});

test('canonical confinement refuses symlinked staging paths', () => {
  const { dir, jwk } = fixture(); const outside=fixtureDirectory('spotify-booster-outside-');
  const target=join(outside,'key.json'); writeFileSync(target,JSON.stringify([jwk]));
  const link=join(dir,'linked'); symlinkSync(outside,link,'junction');
  const result=run('validate-jwk',join(link,'key.json'),dir);
  assert.notEqual(result.status,0);
  assert.match(result.stderr,/symlink_or_reparse_refused/);
});

test('an issued-shape ES256 JWT is verified from stdin without token output', () => {
  const { dir, jwk } = fixture(); const jwks=join(dir,'jwks.json');
  const publicJwk={ kty:jwk.kty, use:'sig', key_ops:['verify'], alg:jwk.alg, kid:jwk.kid, crv:jwk.crv, x:jwk.x, y:jwk.y };
  writeFileSync(jwks,JSON.stringify({keys:[publicJwk]}));
  const header=Buffer.from(JSON.stringify({alg:'ES256',kid:jwk.kid,typ:'JWT'})).toString('base64url');
  const payload=Buffer.from(JSON.stringify({sub:'00000000-0000-0000-0000-000000000001',exp:4102444800})).toString('base64url');
  const signingInput=`${header}.${payload}`;
  const signature=sign('sha256',Buffer.from(signingInput),{key:createPrivateKey({key:jwk,format:'jwk'}),dsaEncoding:'ieee-p1363'}).toString('base64url');
  const token=`${signingInput}.${signature}`;
  const result=runWithInput(token,'validate-jwt-stdin',jwks,dir);
  assert.equal(result.status,0);
  assert.doesNotMatch(result.stdout+result.stderr,new RegExp(token.replace(/[.*+?^${}()|[\]\\]/g,'\\$&')));
});
