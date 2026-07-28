import test, { after, afterEach } from 'node:test';
import assert from 'node:assert/strict';
import { createECDH, createPrivateKey, sign } from 'node:crypto';
import { mkdtempSync, readdirSync, readFileSync, rmSync, statSync, symlinkSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { spawnSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';

const validator = fileURLToPath(new URL('./validate-local-assets.mjs', import.meta.url));
const bootstrap = fileURLToPath(new URL('./bootstrap-supabase-local.ps1', import.meta.url));
const opensslHelp=spawnSync('openssl',['x509','-help'],{encoding:'utf8'});
const canGenerateDatedCertificates=opensslHelp.status===0 &&
  `${opensslHelp.stdout}${opensslHelp.stderr}`.includes('-not_before');
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
  const jwk = jwkFromScalar(Buffer.from([1]));
  return { dir, jwk };
}
function run(...args) { return spawnSync(process.execPath, [validator, ...args], { encoding: 'utf8' }); }
function runWithInput(input, ...args) { return spawnSync(process.execPath, [validator, ...args], { encoding: 'utf8', input }); }
function jwkFromScalar(scalar) {
  const ecdh=createECDH('prime256v1');
  ecdh.setPrivateKey(scalar);
  const publicPoint=ecdh.getPublicKey(undefined,'uncompressed');
  return {
    kty:'EC', use:'sig', key_ops:['sign','verify'], alg:'ES256',
    kid:'123e4567-e89b-42d3-a456-426614174000', crv:'P-256',
    x:publicPoint.subarray(1,33).toString('base64url'),
    y:publicPoint.subarray(33,65).toString('base64url'),
    d:scalar.toString('base64url'), ext:true
  };
}
function assertRejected(result, category) {
  assert.notEqual(result.status,0);
  assert.equal(result.stderr,`local_asset_validation_failed:${category}\n`);
}
function runOpenSsl(directory, ...args) {
  const result=spawnSync('openssl',args,{cwd:directory,encoding:'utf8'});
  assert.equal(result.status,0,result.stderr);
}
function generateCertificate(directory, name, { san=true, expired=false }={}) {
  const key=join(directory,`${name}.key`);
  const request=join(directory,`${name}.csr`);
  const certificate=join(directory,`${name}.crt`);
  runOpenSsl(directory,'req','-new','-newkey','ec',
    '-pkeyopt','ec_paramgen_curve:P-256','-nodes','-keyout',key,
    '-out',request,'-subj','/CN=localhost');
  const args=['x509','-req','-in',request,'-signkey',key,'-out',certificate,
    '-set_serial','1','-not_before',expired?'20000101000000Z':'20200101000000Z',
    '-not_after',expired?'20000102000000Z':'20991231235959Z'];
  if (san) {
    const extensions=join(directory,`${name}.cnf`);
    writeFileSync(extensions,'subjectAltName=IP:127.0.0.1\n');
    args.push('-extfile',extensions);
  }
  runOpenSsl(directory,...args);
  return { certificate, key };
}

test('CLI staging array normalizes to the exact private JWK array', () => {
  const { dir, jwk } = fixture(); const input=join(dir,'staged.json'), output=join(dir,'signing_keys.json');
  writeFileSync(input, JSON.stringify([jwk]));
  assert.equal(run('validate-jwk', input, dir, output).status, 0);
  const value=JSON.parse(readFileSync(output)); assert.equal(value.length,1); assert.equal(value[0].alg,'ES256'); assert.ok(!value[0].d.includes('='));
  assert.equal(run('validate-jwk', output, dir).status, 0);
  if (process.platform !== 'win32') assert.equal(statSync(output).mode & 0o777,0o600);
});

test('private scalars of 1, 31, and 32 bytes normalize to 32 bytes idempotently', () => {
  for (const length of [1,31,32]) {
    const { dir }=fixture();
    const scalar=Buffer.alloc(length); scalar[0]=1; scalar[length-1]=1;
    const input=join(dir,`scalar-${length}.json`);
    const first=join(dir,`normalized-${length}.json`);
    const second=join(dir,`normalized-again-${length}.json`);
    writeFileSync(input,JSON.stringify([jwkFromScalar(scalar)]));
    assert.equal(run('validate-jwk',input,dir,first).status,0);
    assert.equal(Buffer.from(JSON.parse(readFileSync(first,'utf8'))[0].d,'base64url').length,32);
    assert.equal(run('validate-jwk',first,dir,second).status,0);
    assert.equal(readFileSync(second,'utf8'),readFileSync(first,'utf8'));
  }
});

test('empty, 33-byte, padded, and invalid-alphabet private scalars are rejected', () => {
  const invalidValues=[
    ['', 'invalid_d_encoding'],
    [Buffer.alloc(33,1).toString('base64url'), 'invalid_d_length'],
    ['AQ==', 'invalid_d_encoding'],
    ['AQ+', 'invalid_d_encoding']
  ];
  for (const [value,category] of invalidValues) {
    const { dir, jwk }=fixture();
    const input=join(dir,'invalid-scalar.json');
    writeFileSync(input,JSON.stringify([{...jwk,d:value}]));
    assertRejected(run('validate-jwk',input,dir),category);
  }
});

test('zero private scalar and an off-curve public point are rejected', () => {
  const zeroFixture=fixture();
  const zeroInput=join(zeroFixture.dir,'zero.json');
  writeFileSync(zeroInput,JSON.stringify([{...zeroFixture.jwk,d:Buffer.from([0]).toString('base64url')}]));
  assertRejected(run('validate-jwk',zeroInput,zeroFixture.dir),'invalid_private_scalar');

  const pointFixture=fixture();
  const pointInput=join(pointFixture.dir,'off-curve.json');
  writeFileSync(pointInput,JSON.stringify([{...pointFixture.jwk,x:Buffer.alloc(32).toString('base64url')}]));
  assertRejected(run('validate-jwk',pointInput,pointFixture.dir),'invalid_p256_key_material');
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

test('JWKS comparison uses the normalized P-256 coordinates', () => {
  let jwk;
  for (let scalar=1; scalar<4096; scalar++) {
    const candidate=jwkFromScalar(Buffer.from([scalar >> 8, scalar & 0xff]));
    if (Buffer.from(candidate.x,'base64url')[0]===0 || Buffer.from(candidate.y,'base64url')[0]===0) {
      jwk=candidate;
      break;
    }
  }
  assert.ok(jwk,'deterministic scalar search must find a coordinate with a leading zero');
  const shortX=Buffer.from(jwk.x,'base64url')[0]===0;
  jwk={...jwk,[shortX?'x':'y']:Buffer.from(jwk[shortX?'x':'y'],'base64url').subarray(1).toString('base64url')};
  const { dir }=fixture();
  const signing=join(dir,'short-coordinate.json');
  const normalized=join(dir,'normalized-coordinate.json');
  const jwks=join(dir,'jwks.json');
  writeFileSync(signing,JSON.stringify([jwk]));
  assert.equal(run('validate-jwk',signing,dir,normalized).status,0);
  const [normalizedJwk]=JSON.parse(readFileSync(normalized,'utf8'));
  writeFileSync(jwks,JSON.stringify({keys:[{
    kty:'EC', use:'sig', key_ops:['verify'], alg:'ES256', kid:normalizedJwk.kid,
    crv:'P-256', x:normalizedJwk.x, y:normalizedJwk.y
  }]}));
  assert.equal(run('validate-jwks',signing,jwks,dir).status,0);
  assert.equal(Buffer.from(normalizedJwk.x,'base64url').length,32);
  assert.equal(Buffer.from(normalizedJwk.y,'base64url').length,32);
});

test('TLS validation rejects expired, missing-SAN, and mismatched certificate pairs',
  { skip:!canGenerateDatedCertificates }, () => {
    const expiredFixture=fixture();
    const expired=generateCertificate(expiredFixture.dir,'expired',{expired:true});
    assertRejected(
      run('validate-tls',expired.certificate,expired.key,expiredFixture.dir),
      'certificate_expired_or_not_yet_valid');

    const missingSanFixture=fixture();
    const missingSan=generateCertificate(
      missingSanFixture.dir,'missing-san',{san:false});
    assertRejected(
      run('validate-tls',missingSan.certificate,missingSan.key,missingSanFixture.dir),
      'certificate_missing_loopback_ip_san');

    const mismatchFixture=fixture();
    const valid=generateCertificate(mismatchFixture.dir,'valid');
    const different=generateCertificate(mismatchFixture.dir,'different');
    assertRejected(
      run('validate-tls',valid.certificate,different.key,mismatchFixture.dir),
      'tls_certificate_key_mismatch');
  });


test('atomic writer flushes the staged file before close and same-directory rename', () => {
  const source=readFileSync(fileURLToPath(new URL('./validate-local-assets.mjs', import.meta.url)),'utf8');
  const write=source.indexOf('writeFileSync(fd'); const flush=source.indexOf('fsyncSync(fd)',write); const close=source.indexOf('closeSync(fd)',flush); const rename=source.indexOf('renameSync(temp, path)',close);
  assert.ok(write>=0 && write<flush && flush<close && close<rename);
  assert.match(source,/const temp = `\$\{path\}\.tmp-/);
});

test('bootstrap reapplies and verifies final signing key permissions after atomic replacement', () => {
  const source=readFileSync(fileURLToPath(new URL('./bootstrap-supabase-local.ps1', import.meta.url)),'utf8');
  const replace=source.indexOf("Invoke-Checked 'node' @($Validator, 'validate-jwk', $stageSigningPath, $LocalRoot, $SigningPath)");
  const directory=source.indexOf('Set-PrivateDirectory $AuthRoot',replace);
  const acl=source.indexOf('Set-PrivateFile $SigningPath',directory);
  const verify=source.indexOf("Invoke-Checked 'node' @($Validator, 'validate-jwk', $SigningPath, $LocalRoot)",acl);
  assert.ok(replace>=0 && replace<directory && directory<acl && acl<verify);
});

test('Windows bootstrap ACL functions enforce and verify current-user plus SYSTEM access', { skip:process.platform!=='win32' }, () => {
  const { dir }=fixture();
  const privateDirectory=join(dir,'private');
  const privateFile=join(privateDirectory,'secret.json');
  const harness=join(dir,'acl-harness.ps1');
  writeFileSync(harness,`
param([string]$Bootstrap, [string]$Directory, [string]$File)
$ErrorActionPreference = 'Stop'
$tokens = $null
$parseErrors = $null
$ast = [System.Management.Automation.Language.Parser]::ParseFile($Bootstrap, [ref]$tokens, [ref]$parseErrors)
if ($parseErrors.Count -ne 0) { throw 'bootstrap_parse_failed' }
$required = @('Invoke-Checked','Assert-PrivateWindowsAcl','Set-PrivateDirectory','Set-PrivateFile')
$definitions = $ast.FindAll({
  param($node)
  $node -is [System.Management.Automation.Language.FunctionDefinitionAst] -and $required -contains $node.Name
}, $true)
if ($definitions.Count -ne $required.Count) { throw 'acl_functions_missing' }
Invoke-Expression (($definitions | ForEach-Object { $_.Extent.Text }) -join [Environment]::NewLine)
$script:RunningOnWindows = $true
Set-PrivateDirectory $Directory
[System.IO.File]::WriteAllText($File, '[]')
Set-PrivateFile $File
Assert-PrivateWindowsAcl $Directory
Assert-PrivateWindowsAcl $File
Write-Output 'windows_acl_ok'
`);
  const result=spawnSync('powershell.exe',['-NoProfile','-File',harness,bootstrap,privateDirectory,privateFile],{encoding:'utf8'});
  assert.equal(result.status,0,result.stderr);
  assert.match(result.stdout,/windows_acl_ok/);
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
